"""Sample-size-aware multivariable diagnosis for process observations.

The module deliberately separates observational diagnosis from causal
validation.  It adjusts candidate process variables for recorded context,
reports out-of-fold performance and bootstrap stability, and never labels a
candidate as a verified cause.
"""
from __future__ import annotations

from dataclasses import dataclass
from itertools import combinations
from math import log
from typing import Iterable

import numpy as np


ALGORITHM_VERSION = "adaptive-context-diagnosis-v1"


@dataclass(frozen=True)
class FeatureSpec:
    name: str
    source_kind: str
    actionability: str


def _soft_threshold(values: np.ndarray, threshold: np.ndarray) -> np.ndarray:
    return np.sign(values) * np.maximum(np.abs(values) - threshold, 0.0)


def _sigmoid(values: np.ndarray) -> np.ndarray:
    return 1.0 / (1.0 + np.exp(-np.clip(values, -30.0, 30.0)))


def _fit_logistic(
    matrix: np.ndarray,
    target: np.ndarray,
    weights: np.ndarray,
    penalty: np.ndarray,
    l1: float,
    l2: float,
    iterations: int = 700,
) -> np.ndarray:
    design = np.column_stack([np.ones(len(matrix)), matrix])
    coefficients = np.zeros(design.shape[1], dtype=float)
    penalized = np.concatenate([[0.0], penalty])
    spectral = np.linalg.norm(design, ord=2) ** 2
    step = 1.0 / max(0.25 * spectral / max(weights.sum(), 1.0) + l2, 1e-6)
    normalized_weights = weights / max(weights.mean(), 1e-9)
    for _ in range(iterations):
        prediction = _sigmoid(design @ coefficients)
        gradient = design.T @ ((prediction - target) * normalized_weights) / len(target)
        gradient += l2 * penalized * coefficients
        updated = coefficients - step * gradient
        updated[1:] = _soft_threshold(
            updated[1:], step * l1 * penalized[1:]
        )
        if np.max(np.abs(updated - coefficients)) < 1e-7:
            coefficients = updated
            break
        coefficients = updated
    return coefficients


def _predict_logistic(matrix: np.ndarray, coefficients: np.ndarray) -> np.ndarray:
    return _sigmoid(
        np.column_stack([np.ones(len(matrix)), matrix]) @ coefficients
    )


def _fit_regression(
    matrix: np.ndarray,
    target: np.ndarray,
    weights: np.ndarray,
    penalty: np.ndarray,
    l1: float,
    l2: float,
    iterations: int = 900,
) -> np.ndarray:
    design = np.column_stack([np.ones(len(matrix)), matrix])
    coefficients = np.zeros(design.shape[1], dtype=float)
    penalized = np.concatenate([[0.0], penalty])
    spectral = np.linalg.norm(design, ord=2) ** 2
    step = 1.0 / max(2.0 * spectral / max(weights.sum(), 1.0) + l2, 1e-6)
    normalized_weights = weights / max(weights.mean(), 1e-9)
    for _ in range(iterations):
        residual = design @ coefficients - target
        gradient = 2.0 * design.T @ (residual * normalized_weights) / len(target)
        gradient += l2 * penalized * coefficients
        updated = coefficients - step * gradient
        updated[1:] = _soft_threshold(
            updated[1:], step * l1 * penalized[1:]
        )
        if np.max(np.abs(updated - coefficients)) < 1e-7:
            coefficients = updated
            break
        coefficients = updated
    return coefficients


def _predict_regression(matrix: np.ndarray, coefficients: np.ndarray) -> np.ndarray:
    return np.column_stack([np.ones(len(matrix)), matrix]) @ coefficients


def _fit_context_regression(
    context: np.ndarray, target: np.ndarray
) -> tuple[np.ndarray, np.ndarray]:
    design = np.column_stack([np.ones(len(context)), context])
    ridge = np.eye(design.shape[1]) * 1e-4
    ridge[0, 0] = 0.0
    coefficients = np.linalg.solve(design.T @ design + ridge, design.T @ target)
    return coefficients, design @ coefficients


def _gp_predict(
    training: np.ndarray,
    target: np.ndarray,
    prediction: np.ndarray,
) -> np.ndarray:
    if len(training) == 0:
        return np.zeros(len(prediction))
    distances = np.sqrt(
        np.maximum(
            np.sum((training[:, None, :] - training[None, :, :]) ** 2, axis=2),
            0.0,
        )
    )
    positive = distances[distances > 1e-9]
    length_scale = float(np.median(positive)) if len(positive) else 1.0
    length_scale = max(length_scale, 0.25)
    kernel = np.exp(-(distances**2) / (2.0 * length_scale**2))
    noise = max(float(np.var(target)) * 0.05, 1e-5)
    kernel += np.eye(len(training)) * noise
    try:
        alpha = np.linalg.solve(kernel, target)
    except np.linalg.LinAlgError:
        alpha = np.linalg.lstsq(kernel, target, rcond=1e-8)[0]
    cross_distance = np.sum(
        (prediction[:, None, :] - training[None, :, :]) ** 2, axis=2
    )
    return np.exp(-cross_distance / (2.0 * length_scale**2)) @ alpha


def _folds(target: np.ndarray, kind: str, seed: int) -> list[np.ndarray]:
    random = np.random.default_rng(seed)
    if kind == "binary":
        classes = [np.where(target == value)[0] for value in (0.0, 1.0)]
        count = min(5, *(len(indices) for indices in classes))
        if count < 2:
            return []
        buckets: list[list[int]] = [[] for _ in range(count)]
        for indices in classes:
            shuffled = random.permutation(indices)
            for position, index in enumerate(shuffled):
                buckets[position % count].append(int(index))
        return [np.array(bucket, dtype=int) for bucket in buckets]
    count = min(5, max(2, len(target) // 5))
    return [
        np.array(bucket, dtype=int)
        for bucket in np.array_split(random.permutation(len(target)), count)
        if len(bucket)
    ]


def _score(target: np.ndarray, prediction: np.ndarray, kind: str) -> float:
    if kind == "binary":
        clipped = np.clip(prediction, 1e-8, 1.0 - 1e-8)
        loss = -np.mean(target * np.log(clipped) + (1.0 - target) * np.log(1.0 - clipped))
        prevalence = float(np.clip(target.mean(), 1e-8, 1.0 - 1e-8))
        null_loss = -np.mean(
            target * log(prevalence) + (1.0 - target) * log(1.0 - prevalence)
        )
        return float(1.0 - loss / max(null_loss, 1e-9))
    denominator = float(np.sum((target - target.mean()) ** 2))
    return float(1.0 - np.sum((target - prediction) ** 2) / max(denominator, 1e-9))


def _standardize(matrix: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    center = np.nanmedian(matrix, axis=0)
    filled = np.where(np.isfinite(matrix), matrix, center)
    mad = np.nanmedian(np.abs(filled - center), axis=0) * 1.4826
    standard = np.where(mad > 1e-9, mad, np.nanstd(filled, axis=0))
    standard = np.where(standard > 1e-9, standard, 1.0)
    return (filled - center) / standard, center, standard


def _context_matrix(contexts: list[dict[str, str]], timestamps: np.ndarray) -> tuple[np.ndarray, list[str]]:
    columns: list[np.ndarray] = []
    names: list[str] = []
    keys = sorted({key for context in contexts for key in context})
    for key in keys:
        values = [context.get(key, "") for context in contexts]
        levels = sorted({value for value in values if value})
        if len(levels) <= 1:
            continue
        # Reference coding avoids exact collinearity with the intercept.
        for level in levels[1:]:
            columns.append(np.array([1.0 if value == level else 0.0 for value in values]))
            names.append(f"context:{key}={level}")
    if len(timestamps) and np.ptp(timestamps) > 0:
        columns.append((timestamps - timestamps.mean()) / np.ptp(timestamps))
        names.append("context:time-trend")
    return (
        np.column_stack(columns) if columns else np.empty((len(contexts), 0)),
        names,
    )


def _additive_basis(matrix: np.ndarray, feature_names: list[str]) -> tuple[np.ndarray, list[str], list[int]]:
    columns = [matrix]
    names = list(feature_names)
    owners = list(range(len(feature_names)))
    for index, name in enumerate(feature_names):
        values = matrix[:, index]
        columns.extend([(values**2)[:, None], (values**3)[:, None]])
        names.extend([f"{name}^2", f"{name}^3"])
        owners.extend([index, index])
    return np.column_stack(columns), names, owners


def _interaction_basis(
    matrix: np.ndarray, feature_names: list[str], ranked: Iterable[int]
) -> tuple[np.ndarray, list[str], list[tuple[int, int]]]:
    pairs = list(combinations(list(ranked)[:6], 2))[:12]
    if not pairs:
        return np.empty((len(matrix), 0)), [], []
    return (
        np.column_stack([matrix[:, left] * matrix[:, right] for left, right in pairs]),
        [f"{feature_names[left]} × {feature_names[right]}" for left, right in pairs],
        pairs,
    )


def diagnose(
    features: list[FeatureSpec],
    values: np.ndarray,
    target: np.ndarray,
    weights: np.ndarray,
    contexts: list[dict[str, str]],
    timestamps: np.ndarray,
    outcome_kind: str,
    seed: int = 0,
) -> dict:
    row_count, feature_count = values.shape
    if row_count < 4 or feature_count == 0:
        return {
            "algorithm_version": ALGORITHM_VERSION,
            "model_family": "robust-screening-only",
            "adjustment_method": "none",
            "cross_validation_score": None,
            "fold_count": 0,
            "stability_runs": 0,
            "context_variables": [],
            "candidates": [],
            "interactions": [],
            "limitations": ["有效样本或候选变量不足，未运行多变量模型。"],
        }
    if outcome_kind == "binary" and len(np.unique(target)) < 2:
        raise ValueError("binary diagnosis requires both outcome classes")

    standardized, _, _ = _standardize(values)
    context_design, context_names = _context_matrix(contexts, timestamps)
    if context_design.shape[1]:
        context_design, _, _ = _standardize(context_design)

    # Marginal robust association is only used to cap dimensionality. It does
    # not replace the adjusted model.
    associations = []
    for column in range(feature_count):
        current = standardized[:, column]
        if outcome_kind == "binary":
            effect = float(np.median(current[target == 1]) - np.median(current[target == 0]))
        else:
            effect = float(np.corrcoef(current, target)[0, 1]) if np.std(current) > 0 else 0.0
        associations.append(abs(effect) if np.isfinite(effect) else 0.0)
    maximum = min(feature_count, max(4, min(18, row_count // 3)))
    retained = np.array(
        sorted(range(feature_count), key=lambda index: (-associations[index], features[index].name))[:maximum],
        dtype=int,
    )
    process = standardized[:, retained]
    retained_names = [features[index].name for index in retained]

    if row_count < 12 or (outcome_kind == "binary" and min(np.sum(target == 0), np.sum(target == 1)) < 4):
        return {
            "algorithm_version": ALGORITHM_VERSION,
            "model_family": "robust-screening-only",
            "adjustment_method": "context-balance-audit",
            "cross_validation_score": None,
            "fold_count": 0,
            "stability_runs": 0,
            "context_variables": context_names,
            "candidates": [],
            "interactions": [],
            "limitations": [
                "样本量不足以可靠拟合多变量模型，保留稳健单变量筛选结果。",
                "多变量候选在样本增加后自动启用。",
            ],
        }

    nonlinear = row_count >= 40 and len(retained) <= 12
    if nonlinear:
        process_design, design_names, owners = _additive_basis(process, retained_names)
        model_family = (
            "regularized-additive-logistic"
            if outcome_kind == "binary"
            else "regularized-additive-regression"
        )
    else:
        process_design, design_names, owners = process, retained_names, list(range(len(retained)))
        model_family = "elastic-net-logistic" if outcome_kind == "binary" else "elastic-net-regression"

    interaction_names: list[str] = []
    interaction_pairs: list[tuple[int, int]] = []
    interaction_offset: int | None = None
    if row_count >= 30:
        ranked_local = sorted(
            range(len(retained)),
            key=lambda index: -associations[retained[index]],
        )
        interaction_matrix, interaction_names, interaction_pairs = _interaction_basis(
            process, retained_names, ranked_local
        )
        if interaction_matrix.shape[1]:
            interaction_offset = process_design.shape[1]
            process_design = np.column_stack([process_design, interaction_matrix])
            design_names.extend(interaction_names)
            owners.extend([-1] * len(interaction_names))
            model_family += "+interactions"

    design = np.column_stack([process_design, context_design])
    process_column_count = process_design.shape[1]
    penalty = np.concatenate(
        [np.ones(process_column_count), np.zeros(context_design.shape[1])]
    )
    fold_indices = _folds(target, outcome_kind, seed)
    lambdas = (0.01, 0.03, 0.08, 0.16)
    best_lambda = lambdas[0]
    best_score = -float("inf")
    for candidate_lambda in lambdas:
        predictions = np.zeros(row_count)
        for validation in fold_indices:
            training = np.setdiff1d(np.arange(row_count), validation)
            if outcome_kind == "binary":
                coefficients = _fit_logistic(
                    design[training], target[training], weights[training], penalty,
                    candidate_lambda, candidate_lambda
                )
                predictions[validation] = _predict_logistic(design[validation], coefficients)
            else:
                coefficients = _fit_regression(
                    design[training], target[training], weights[training], penalty,
                    candidate_lambda, candidate_lambda
                )
                predictions[validation] = _predict_regression(design[validation], coefficients)
        score = _score(target, predictions, outcome_kind)
        if score > best_score:
            best_score = score
            best_lambda = candidate_lambda

    gp_score: float | None = None
    if outcome_kind == "continuous" and row_count >= 25 and len(retained) <= 8:
        gp_predictions = np.zeros(row_count)
        for validation in fold_indices:
            training = np.setdiff1d(np.arange(row_count), validation)
            context_coefficients, context_fitted = _fit_context_regression(
                context_design[training], target[training]
            )
            validation_context = np.column_stack(
                [np.ones(len(validation)), context_design[validation]]
            )
            gp_predictions[validation] = (
                validation_context @ context_coefficients
                + _gp_predict(
                    process[training],
                    target[training] - context_fitted,
                    process[validation],
                )
            )
        gp_score = _score(target, gp_predictions, outcome_kind)
        if gp_score > best_score:
            best_score = gp_score
            model_family = "gaussian-process-regression+elastic-net-attribution"

    fit = _fit_logistic if outcome_kind == "binary" else _fit_regression
    coefficients = fit(design, target, weights, penalty, best_lambda, best_lambda)
    process_coefficients = coefficients[1 : process_column_count + 1]

    random = np.random.default_rng(seed + 71)
    stability_runs = 24
    selected_counts = np.zeros(process_column_count)
    sign_sums = np.zeros(process_column_count)
    for _ in range(stability_runs):
        sample = random.choice(row_count, row_count, replace=True)
        if outcome_kind == "binary" and len(np.unique(target[sample])) < 2:
            continue
        current = fit(
            design[sample], target[sample], weights[sample], penalty,
            best_lambda, best_lambda, iterations=400
        )
        current = current[1 : process_column_count + 1]
        selected = np.abs(current) > 1e-5
        selected_counts += selected
        sign_sums += np.sign(current) * selected
    stability = selected_counts / stability_runs
    sign_stability = np.abs(sign_sums) / np.maximum(selected_counts, 1.0)

    candidates = []
    gp_importance: dict[int, float] = {}
    if model_family.startswith("gaussian-process"):
        context_coefficients, context_fitted = _fit_context_regression(
            context_design, target
        )
        baseline_prediction = context_fitted + _gp_predict(
            process, target - context_fitted, process
        )
        baseline_score = _score(target, baseline_prediction, outcome_kind)
        permutation_random = np.random.default_rng(seed + 113)
        for local_index in range(len(retained)):
            permuted = process.copy()
            permuted[:, local_index] = permutation_random.permutation(
                permuted[:, local_index]
            )
            prediction = context_fitted + _gp_predict(
                process, target - context_fitted, permuted
            )
            gp_importance[local_index] = max(
                0.0, baseline_score - _score(target, prediction, outcome_kind)
            )
    for local_index, source_index in enumerate(retained):
        owned = [index for index, owner in enumerate(owners) if owner == local_index]
        if not owned:
            continue
        primary = owned[0]
        importance = gp_importance.get(
            local_index, float(np.linalg.norm(process_coefficients[owned]))
        )
        selection = float(np.max(stability[owned]))
        direction = float(process_coefficients[primary])
        candidates.append(
            {
                "data_source": features[source_index].name,
                "adjusted_effect": direction,
                "model_importance": importance,
                "stability_selection_rate": selection,
                "sign_stability": float(sign_stability[primary]),
                "rank_score": importance * selection,
            }
        )
    candidates.sort(key=lambda value: (-value["rank_score"], value["data_source"]))

    interactions = []
    if interaction_offset is not None:
        for offset, _ in enumerate(interaction_names):
            index = interaction_offset + offset
            if index >= len(process_coefficients):
                continue
            importance = abs(float(process_coefficients[index]))
            selection = float(stability[index])
            if importance <= 1e-5 or selection < 0.25:
                continue
            left, right = interaction_pairs[offset]
            interactions.append(
                {
                    "left_data_source": retained_names[left],
                    "right_data_source": retained_names[right],
                    "adjusted_effect": float(process_coefficients[index]),
                    "stability_selection_rate": selection,
                    "rank_score": importance * selection,
                }
            )
    interactions.sort(key=lambda value: -value["rank_score"])

    limitations = [
        "结果已校正记录到的上下文，但未记录的混杂因素仍可能存在。",
        "交叉验证分数和稳定性选择用于抑制偶然相关，不构成因果证明。",
        "只有安全的跨区组重复干预实验成立后，候选原因才能升级为已验证原因。",
    ]
    if best_score <= 0:
        limitations.insert(0, "模型的样本外表现未优于基线，当前多变量排名仅供探索。")
    return {
        "algorithm_version": ALGORITHM_VERSION,
        "model_family": model_family,
        "adjustment_method": "context-fixed-effects+time-trend",
        "cross_validation_score": float(best_score),
        "fold_count": len(fold_indices),
        "stability_runs": stability_runs,
        "context_variables": context_names,
        "candidates": candidates,
        "interactions": interactions[:12],
        "limitations": limitations,
    }
