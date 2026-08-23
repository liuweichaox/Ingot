"""Production optimizer: target-aware GP with evidence-gated method admission."""
from __future__ import annotations

import math
from typing import Mapping, Sequence

import numpy as np
from scipy.stats import norm, rankdata, spearmanr

from .campaign import Campaign
from .feature_transforms import DerivedFeature, expand_inputs
from .loop import ObjectivePrediction, Suggestion


MODEL_VERSION = "botorch-cross-validated-method-routing-v11"
MONOTONIC_PEARSON_FLOOR = 0.75
MONOTONIC_SPEARMAN_FLOOR = 0.85
RAW_LINEAR_RIDGE = 1e-3
RAW_QUADRATIC_RIDGE = 1e-2
MECHANISM_QUADRATIC_RIDGE = 1.5e-2
JOINT_QUADRATIC_RIDGE = 2e-2
JOINT_LINEAR_RIDGE = 2e-2
FEATURE_ADMISSION_RELATIVE_IMPROVEMENT = 0.50
MINIMUM_OBSERVATIONS_PER_RESPONSE_COEFFICIENT = 3
GP_PROBABILITY_RANK_WEIGHT = 0.25
MINIMUM_OBSERVATIONS_PER_GP_DIMENSION = 6


def _observation_variance(values: np.ndarray) -> np.ndarray:
    """Return a scale-aware noise floor that stabilizes repeated DOE points."""
    values = np.asarray(values, dtype=float)
    spread = np.ptp(values, axis=0)
    median = np.median(values, axis=0)
    mad = np.median(np.abs(values - median), axis=0) * 1.4826
    scale = np.maximum.reduce([spread, mad, np.ones_like(spread)])
    variance = np.square(scale * 1e-3)
    return np.broadcast_to(variance, values.shape).copy()


def _response_surface_features(points: np.ndarray, *, quadratic: bool) -> np.ndarray:
    """Build a raw-control response-surface basis without process-specific terms."""
    values = np.atleast_2d(np.asarray(points, dtype=float))
    columns: list[np.ndarray] = [values]
    if quadratic:
        columns.append(values**2)
        columns.extend(
            (values[:, left] * values[:, right])[:, None]
            for left in range(values.shape[1])
            for right in range(left + 1, values.shape[1])
        )
    return np.column_stack(columns)


def _response_surface_coefficient_count(width: int, *, quadratic: bool) -> int:
    """Return intercept plus raw, squared, and interaction coefficients."""
    if width < 1:
        raise ValueError("response surfaces require at least one input")
    return 1 + width + (
        width + width * (width - 1) // 2 if quadratic else 0
    )


def _ridge_response_predictions(
    observed_points: np.ndarray,
    observed_distance: np.ndarray,
    candidate_points: np.ndarray,
    *,
    quadratic: bool,
    ridge: float,
) -> np.ndarray:
    """Predict distance-to-specification with a regularized response surface."""
    observed_features = _response_surface_features(
        observed_points, quadratic=quadratic
    )
    candidate_features = _response_surface_features(
        candidate_points, quadratic=quadratic
    )
    observed_design = np.column_stack(
        [np.ones(len(observed_features)), observed_features]
    )
    candidate_design = np.column_stack(
        [np.ones(len(candidate_features)), candidate_features]
    )
    penalty = np.eye(observed_design.shape[1]) * ridge
    penalty[0, 0] = 0.0
    coefficients = np.linalg.solve(
        observed_design.T @ observed_design + penalty,
        observed_design.T @ observed_distance,
    )
    return candidate_design @ coefficients


def _leave_one_out_error(
    points: np.ndarray,
    distance: np.ndarray,
    *,
    quadratic: bool,
    ridge: float,
) -> float:
    """Return outcome-scale RMSE from predictions made without each held-out row."""
    values = np.atleast_2d(np.asarray(points, dtype=float))
    outcomes = np.asarray(distance, dtype=float)
    if len(values) != len(outcomes) or len(values) < 3:
        raise ValueError("leave-one-out admission requires at least three matched rows")
    predictions = np.empty(len(values), dtype=float)
    for held_out in range(len(values)):
        keep = np.arange(len(values)) != held_out
        predictions[held_out] = _ridge_response_predictions(
            values[keep],
            outcomes[keep],
            values[held_out : held_out + 1],
            quadratic=quadratic,
            ridge=ridge,
        )[0]
    return float(np.sqrt(np.mean(np.square(outcomes - predictions))))


def _feature_representation_is_admitted(
    observed_points: np.ndarray,
    observed_distance: np.ndarray,
    augmented_points: np.ndarray,
) -> bool:
    """Admit declared features only with capacity and predictive-gain evidence."""
    observed = np.atleast_2d(np.asarray(observed_points, dtype=float))
    distance = np.asarray(observed_distance, dtype=float)
    augmented = np.atleast_2d(np.asarray(augmented_points, dtype=float))
    if len(observed) != len(distance) or len(augmented) != len(observed):
        raise ValueError("feature admission requires matched observed rows")
    if augmented.shape[1] <= observed.shape[1]:
        return False

    raw_error = _leave_one_out_error(
        observed,
        distance,
        quadratic=True,
        ridge=RAW_QUADRATIC_RIDGE,
    )
    mechanism = augmented[:, observed.shape[1] :]
    errors = []
    for model_points, quadratic, ridge in (
        (augmented, False, JOINT_LINEAR_RIDGE),
        (mechanism, True, MECHANISM_QUADRATIC_RIDGE),
        (augmented, True, JOINT_QUADRATIC_RIDGE),
    ):
        minimum_observations = (
            MINIMUM_OBSERVATIONS_PER_RESPONSE_COEFFICIENT
            * _response_surface_coefficient_count(
                model_points.shape[1], quadratic=quadratic
            )
        )
        if len(observed) < minimum_observations:
            continue
        error = _leave_one_out_error(
            model_points,
            distance,
            quadratic=quadratic,
            ridge=ridge,
        )
        if math.isfinite(error):
            errors.append(error)
    return bool(
        errors
        and min(errors)
        <= raw_error * (1.0 - FEATURE_ADMISSION_RELATIVE_IMPROVEMENT)
    )


def _reach_specification_scores(
    observed_points: np.ndarray,
    observed_distance: np.ndarray,
    candidate_points: np.ndarray,
    specification_probability: np.ndarray,
    *,
    observed_augmented_points: np.ndarray | None = None,
    candidate_augmented_points: np.ndarray | None = None,
) -> tuple[np.ndarray, str]:
    """Select the least-complex response model supported by visible evidence.

    A raw control must show both strong Pearson association and strong monotonic
    rank association before a linear response surface is admitted. Otherwise,
    the raw quadratic surface is the conservative default. Declared mechanism
    features admit joint-linear, mechanism-quadratic, and
    joint-quadratic candidates to the same comparison, but an augmented model
    must improve on the best raw model by a fixed relative margin. Candidate
    outcomes are never read by this policy; only revealed observations can
    admit model complexity. The GP remains responsible for uncertainty and
    returned predictions, not for overriding the routed response surface.
    """
    observed = np.atleast_2d(np.asarray(observed_points, dtype=float))
    distance = np.asarray(observed_distance, dtype=float)
    candidates = np.atleast_2d(np.asarray(candidate_points, dtype=float))
    probabilities = np.asarray(specification_probability, dtype=float)
    if len(observed) != len(distance):
        raise ValueError("observed points and distances must have equal length")
    if len(candidates) != len(probabilities):
        raise ValueError("candidate points and probabilities must have equal length")

    augmented_observed = np.atleast_2d(
        np.asarray(
            observed_augmented_points
            if observed_augmented_points is not None
            else observed,
            dtype=float,
        )
    )
    augmented_candidates = np.atleast_2d(
        np.asarray(
            candidate_augmented_points
            if candidate_augmented_points is not None
            else candidates,
            dtype=float,
        )
    )
    if len(augmented_observed) != len(observed):
        raise ValueError("augmented observed points must match observed rows")
    if len(augmented_candidates) != len(candidates):
        raise ValueError("augmented candidate points must match candidate rows")
    if augmented_observed.shape[1] != augmented_candidates.shape[1]:
        raise ValueError("augmented observed and candidate widths must match")
    if augmented_observed.shape[1] < observed.shape[1]:
        raise ValueError("augmented points cannot omit raw controls")

    probability_rank = rankdata(probabilities, method="average") / len(candidates)
    gp_probability_admitted = len(observed) >= (
        MINIMUM_OBSERVATIONS_PER_GP_DIMENSION * observed.shape[1]
    )

    def finish(response_score: np.ndarray, policy: str) -> tuple[np.ndarray, str]:
        if not gp_probability_admitted:
            return response_score, policy
        return (
            (1.0 - GP_PROBABILITY_RANK_WEIGHT) * response_score
            + GP_PROBABILITY_RANK_WEIGHT * probability_rank,
            f"gp-{policy}",
        )

    linear_distance = _ridge_response_predictions(
        observed,
        distance,
        candidates,
        quadratic=False,
        ridge=RAW_LINEAR_RIDGE,
    )
    monotonic_control = False
    if np.ptp(distance) > 1e-12:
        for column in range(observed.shape[1]):
            if np.ptp(observed[:, column]) <= 1e-12:
                continue
            pearson = float(np.corrcoef(observed[:, column], distance)[0, 1])
            spearman = float(spearmanr(observed[:, column], distance).statistic)
            if (
                math.isfinite(pearson)
                and math.isfinite(spearman)
                and abs(pearson) >= MONOTONIC_PEARSON_FLOOR
                and abs(spearman) >= MONOTONIC_SPEARMAN_FLOOR
            ):
                monotonic_control = True
                break
    linear_rank = rankdata(-linear_distance, method="average")
    if monotonic_control:
        return linear_rank / len(candidates), "dominant-linear-response"

    raw_quadratic_distance = _ridge_response_predictions(
        observed,
        distance,
        candidates,
        quadratic=True,
        ridge=RAW_QUADRATIC_RIDGE,
    )
    raw_quadratic_error = _leave_one_out_error(
        observed,
        distance,
        quadratic=True,
        ridge=RAW_QUADRATIC_RIDGE,
    )
    raw_error = raw_quadratic_error
    raw_score = rankdata(-raw_quadratic_distance, method="average") / len(
        candidates
    )
    raw_policy = "quadratic-response"

    raw_width = observed.shape[1]
    if augmented_observed.shape[1] == raw_width:
        return finish(raw_score, raw_policy)

    if not _feature_representation_is_admitted(
        observed,
        distance,
        augmented_observed,
    ):
        return finish(raw_score, raw_policy)

    mechanism_observed = augmented_observed[:, raw_width:]
    mechanism_candidates = augmented_candidates[:, raw_width:]
    augmented_models: list[tuple[float, np.ndarray, str]] = []
    for model_points, model_candidates, quadratic, ridge, policy in (
        (
            augmented_observed,
            augmented_candidates,
            False,
            JOINT_LINEAR_RIDGE,
            "cross-validated-joint-linear-response",
        ),
        (
            mechanism_observed,
            mechanism_candidates,
            True,
            MECHANISM_QUADRATIC_RIDGE,
            "cross-validated-mechanism-quadratic-response",
        ),
        (
            augmented_observed,
            augmented_candidates,
            True,
            JOINT_QUADRATIC_RIDGE,
            "cross-validated-joint-quadratic-response",
        ),
    ):
        error = _leave_one_out_error(
            model_points,
            distance,
            quadratic=quadratic,
            ridge=ridge,
        )
        if not math.isfinite(error):
            continue
        predictions = _ridge_response_predictions(
            model_points,
            distance,
            model_candidates,
            quadratic=quadratic,
            ridge=ridge,
        )
        augmented_models.append((error, predictions, policy))

    if augmented_models:
        augmented_error, augmented_distance, augmented_policy = min(
            augmented_models, key=lambda item: item[0]
        )
        if augmented_error <= raw_error * (
            1.0 - FEATURE_ADMISSION_RELATIVE_IMPROVEMENT
        ):
            return finish(
                rankdata(-augmented_distance, method="average") / len(candidates),
                augmented_policy,
            )
    return finish(raw_score, raw_policy)


class BotorchOptimizer:
    """Runs the production Bayesian optimizer after the cold-start threshold."""

    def __init__(
        self,
        campaign: Campaign,
        *,
        derived_features: Sequence[DerivedFeature] | None = None,
        seed: int = 0,
    ):
        self.campaign = campaign
        self.derived_features = tuple(derived_features or ())
        self.seed = seed
        self.x: list[np.ndarray] = []
        self.y: list[list[float]] = []
        self.constraint_y: list[list[float]] = []
        self.process_features: list[dict[str, float]] = []

    def observe(
        self,
        params: Mapping[str, float],
        outcomes: Mapping[str, float],
        *,
        constraint_outcomes: Mapping[str, float] | None = None,
        process_features: Mapping[str, float] | None = None,
    ) -> float:
        self.x.append(
            self.campaign.to_unit(params, enforce_candidate_constraints=False)
        )
        self.campaign.validate_outcomes(outcomes)
        resolved_constraints = dict(constraint_outcomes or {})
        self.campaign.validate_constraint_outcomes(resolved_constraints)
        resolved_features = {
            str(name): float(value)
            for name, value in (process_features or {}).items()
            if str(name) and math.isfinite(float(value))
        }
        self.y.append([float(outcomes[o.name]) for o in self.campaign.objectives])
        self.constraint_y.append(
            [
                float(resolved_constraints[constraint.name])
                for constraint in self.campaign.outcome_constraints
            ]
        )
        self.process_features.append(resolved_features)
        return self.campaign.distance_to_spec(outcomes)

    def _common_process_feature_names(self) -> list[str]:
        if not self.process_features or any(
            not values for values in self.process_features
        ):
            return []
        common = set(self.process_features[0])
        for values in self.process_features[1:]:
            common.intersection_update(values)
        return sorted(common)

    def _candidate_units(
        self,
        candidate_params: Sequence[Mapping[str, float]] | None,
        n_random: int,
    ) -> np.ndarray:
        from scipy.stats import qmc

        if candidate_params is not None:
            points = np.vstack([self.campaign.to_unit(value) for value in candidate_params])
        else:
            sampler = qmc.Sobol(self.campaign.dim, scramble=True, seed=self.seed)
            points = sampler.random_base2(math.ceil(math.log2(max(n_random, 2))))[:n_random]
            points = np.asarray(
                [point for point in points if self.campaign.is_feasible_unit(point)]
            )
        points = np.unique(points, axis=0)
        if self.x:
            observed = np.vstack(self.x)
            points = points[
                np.min(np.linalg.norm(points[:, None] - observed[None, :], axis=2), axis=1)
                > 1e-7
            ]
        return points

    def suggest(
        self,
        *,
        top_k: int = 1,
        candidate_params: Sequence[Mapping[str, float]] | None = None,
        n_random: int = 4096,
        n_samples: int = 256,
        pending_params: Sequence[Mapping[str, float]] | None = None,
        decision_intent: str = "reach-specification",
        hypothesis_variables: Sequence[str] | None = None,
        **_: object,
    ) -> list[Suggestion]:
        if len(self.x) < 3:
            raise ValueError(
                "the production surrogate requires at least three observations"
            )
        if top_k < 1 or top_k > 20:
            raise ValueError("top_k must be between 1 and 20")
        if decision_intent not in {"reach-specification", "validate-hypothesis"}:
            raise ValueError("decision_intent must be reach-specification or validate-hypothesis")
        candidates = self._candidate_units(candidate_params, n_random)
        pending_units = (
            np.vstack([self.campaign.to_unit(value) for value in pending_params])
            if pending_params
            else np.empty((0, self.campaign.dim), dtype=float)
        )
        if pending_units.size:
            candidates = candidates[
                np.min(
                    np.linalg.norm(
                        candidates[:, None, :] - pending_units[None, :, :],
                        axis=2,
                    ),
                    axis=1,
                )
                > 1e-7
            ]
        if len(candidates) < top_k:
            raise ValueError("fewer feasible unobserved candidates than top_k")

        import torch
        from botorch import fit_gpytorch_mll
        from botorch.models import SingleTaskGP
        from botorch.models.transforms.outcome import Standardize
        from gpytorch.mlls import ExactMarginalLogLikelihood

        torch.manual_seed(self.seed)
        dtype = torch.double
        names = [value.name for value in self.campaign.variables]
        train_unit = np.vstack(self.x)
        train_unit_tensor = torch.tensor(train_unit, dtype=dtype)
        candidate_unit_tensor = torch.tensor(candidates, dtype=dtype)
        lows = [value.low for value in self.campaign.variables]
        highs = [value.high for value in self.campaign.variables]
        expanded_train_np = expand_inputs(
            train_unit,
            names,
            lows,
            highs,
            self.derived_features,
        )
        expanded_choices_np = expand_inputs(
            candidates,
            names,
            lows,
            highs,
            self.derived_features,
        )
        observed_distance = np.asarray(
            [
                self.campaign.distance_to_spec(
                    {
                        objective.name: float(values[index])
                        for index, objective in enumerate(
                            self.campaign.objectives
                        )
                    }
                )
                for values in self.y
            ]
        )
        features_admitted = _feature_representation_is_admitted(
            train_unit,
            observed_distance,
            expanded_train_np,
        )
        train_x_np = expanded_train_np if features_admitted else train_unit.copy()
        choices_np = expanded_choices_np if features_admitted else candidates.copy()
        decision_train_np = train_x_np.copy()
        decision_choices_np = choices_np.copy()
        process_feature_names = self._common_process_feature_names()
        if process_feature_names:
            process_train_np = np.asarray(
                [
                    [values[name] for name in process_feature_names]
                    for values in self.process_features
                ],
                dtype=float,
            )
            process_train = torch.tensor(process_train_np, dtype=dtype)
            process_model = SingleTaskGP(
                train_unit_tensor,
                process_train,
                train_Yvar=torch.tensor(
                    _observation_variance(process_train_np), dtype=dtype
                ),
                outcome_transform=Standardize(process_train.shape[-1]),
            )
            process_mll = ExactMarginalLogLikelihood(
                process_model.likelihood, process_model
            )
            fit_gpytorch_mll(process_mll)
            with torch.no_grad():
                predicted_process = (
                    process_model.posterior(candidate_unit_tensor)
                    .mean.cpu()
                    .numpy()
                )
            process_minimum = process_train_np.min(axis=0)
            process_scale = process_train_np.max(axis=0) - process_minimum
            process_scale = np.where(process_scale > 1e-9, process_scale, 1.0)
            train_x_np = np.column_stack(
                [train_x_np, (process_train_np - process_minimum) / process_scale]
            )
            choices_np = np.column_stack(
                [choices_np, (predicted_process - process_minimum) / process_scale]
            )
        train_x = torch.tensor(train_x_np, dtype=dtype)
        train_y_np = np.column_stack(
            [np.asarray(self.y), np.asarray(self.constraint_y)]
        )
        train_y = torch.tensor(train_y_np, dtype=dtype)
        choices = torch.tensor(choices_np, dtype=dtype)
        model = SingleTaskGP(
            train_x,
            train_y,
            train_Yvar=torch.tensor(_observation_variance(train_y_np), dtype=dtype),
            outcome_transform=Standardize(train_y.shape[-1]),
        )
        mll = ExactMarginalLogLikelihood(model.likelihood, model)
        fit_gpytorch_mll(mll)
        objective_count = len(self.campaign.objectives)
        if self.campaign.outcome_constraints:
            with torch.no_grad():
                safety_posterior = model.posterior(choices)
                safety_means = safety_posterior.mean.cpu().numpy()
                safety_deviations = (
                    safety_posterior.variance.clamp_min(0).sqrt().cpu().numpy()
                )
            safe = np.ones(len(candidates), dtype=bool)
            for index, constraint_spec in enumerate(
                self.campaign.outcome_constraints
            ):
                if not constraint_spec.safety_critical:
                    continue
                output_index = objective_count + index
                sigma = np.maximum(safety_deviations[:, output_index], 1e-12)
                if constraint_spec.operator == "<=":
                    probability = norm.cdf(
                        (constraint_spec.limit - safety_means[:, output_index])
                        / sigma
                    )
                else:
                    probability = 1.0 - norm.cdf(
                        (constraint_spec.limit - safety_means[:, output_index])
                        / sigma
                    )
                safe &= probability >= constraint_spec.minimum_probability
            if safe.sum() < top_k:
                raise ValueError(
                    "fewer candidates than top_k satisfy safety outcome "
                    "probability thresholds"
                )
            candidates = candidates[safe]
            choices = choices[safe]
            decision_choices_np = decision_choices_np[safe]
        validation_scores = None
        selected_indices = None
        selected_acquisition_values = None
        reach_policy = None
        if decision_intent == "validate-hypothesis":
            requested = [str(value) for value in (hypothesis_variables or [])]
            variable_indexes = [names.index(value) for value in requested if value in names]
            if not variable_indexes:
                raise ValueError(
                    "hypothesis validation requires at least one controllable hypothesis variable"
                )
            with torch.no_grad():
                validation_posterior = model.posterior(choices)
                objective_uncertainty = (
                    validation_posterior.variance[:, : len(self.campaign.objectives)]
                    .clamp_min(0)
                    .sqrt()
                    .mean(dim=1)
                    .cpu()
                    .numpy()
                )
            projected_candidates = candidates[:, variable_indexes]
            projected_observations = train_unit[:, variable_indexes]
            separation = np.min(
                np.linalg.norm(
                    projected_candidates[:, None, :] - projected_observations[None, :, :],
                    axis=2,
                ),
                axis=1,
            )
            validation_scores = objective_uncertainty * (1.0 + separation)
            selected_indices = self._select_diverse_points(
                candidates, validation_scores, top_k
            )
            selected_x = choices[selected_indices]
        else:
            with torch.no_grad():
                reach_posterior = model.posterior(choices)
                reach_means = reach_posterior.mean.cpu().numpy()
                reach_deviations = (
                    reach_posterior.variance.clamp_min(0).sqrt().cpu().numpy()
                )
            specification_probability = np.ones(len(candidates), dtype=float)
            for index, objective_spec in enumerate(self.campaign.objectives):
                sigma = np.maximum(reach_deviations[:, index], 1e-12)
                if objective_spec.kind == "le":
                    probability = norm.cdf(
                        (float(objective_spec.threshold) - reach_means[:, index])
                        / sigma
                    )
                elif objective_spec.kind == "ge":
                    probability = 1.0 - norm.cdf(
                        (float(objective_spec.threshold) - reach_means[:, index])
                        / sigma
                    )
                else:
                    lower = (
                        float(objective_spec.target) - float(objective_spec.tol)
                        if objective_spec.kind == "target"
                        else float(objective_spec.lower)
                    )
                    upper = (
                        float(objective_spec.target) + float(objective_spec.tol)
                        if objective_spec.kind == "target"
                        else float(objective_spec.upper)
                    )
                    probability = norm.cdf(
                        (upper - reach_means[:, index]) / sigma
                    ) - norm.cdf((lower - reach_means[:, index]) / sigma)
                specification_probability *= np.clip(probability, 0.0, 1.0)
            for index, constraint_spec in enumerate(
                self.campaign.outcome_constraints
            ):
                output_index = objective_count + index
                sigma = np.maximum(
                    reach_deviations[:, output_index], 1e-12
                )
                if constraint_spec.operator == "<=":
                    probability = norm.cdf(
                        (
                            float(constraint_spec.limit)
                            - reach_means[:, output_index]
                        )
                        / sigma
                    )
                else:
                    probability = 1.0 - norm.cdf(
                        (
                            float(constraint_spec.limit)
                            - reach_means[:, output_index]
                        )
                        / sigma
                    )
                specification_probability *= np.clip(probability, 0.0, 1.0)
            acquisition_score, reach_policy = _reach_specification_scores(
                train_unit,
                observed_distance,
                candidates,
                specification_probability,
                observed_augmented_points=decision_train_np,
                candidate_augmented_points=decision_choices_np,
            )
            selected_indices = self._select_diverse_points(
                candidates, acquisition_score, top_k
            )
            selected_x = choices[selected_indices]
            selected_acquisition_values = acquisition_score[selected_indices]
        selected_expanded = selected_x.detach().cpu().numpy()
        selected_unit = selected_expanded[:, : self.campaign.dim]
        with torch.no_grad():
            posterior = model.posterior(selected_x)
            means = posterior.mean.cpu().numpy()
            deviations = posterior.variance.clamp_min(0).sqrt().cpu().numpy()

        results: list[Suggestion] = []
        for row, (unit, mean, deviation) in enumerate(
            zip(selected_unit, means, deviations, strict=True)
        ):
            params = self.campaign.from_unit(unit)
            predictions = {}
            bounded_means = {}
            for index, objective_spec in enumerate(self.campaign.objectives):
                bounded_mean, bounded_deviation, lower_95, upper_95 = (
                    objective_spec.bounded_prediction(
                        float(mean[index]), float(deviation[index])
                    )
                )
                bounded_means[objective_spec.name] = bounded_mean
                predictions[objective_spec.name] = ObjectivePrediction(
                    mean=bounded_mean,
                    standard_deviation=bounded_deviation,
                    lower_95=lower_95,
                    upper_95=upper_95,
                    unit=objective_spec.unit,
                )
            constraint_predictions = {
                constraint_spec.name: ObjectivePrediction(
                    mean=float(mean[objective_count + index]),
                    standard_deviation=float(deviation[objective_count + index]),
                    lower_95=float(
                        mean[objective_count + index]
                        - 1.96 * deviation[objective_count + index]
                    ),
                    upper_95=float(
                        mean[objective_count + index]
                        + 1.96 * deviation[objective_count + index]
                    ),
                    unit=constraint_spec.unit,
                )
                for index, constraint_spec in enumerate(
                    self.campaign.outcome_constraints
                )
            }
            feasibility = 1.0
            for index, objective_spec in enumerate(self.campaign.objectives):
                sigma = max(float(deviation[index]), 1e-12)
                if objective_spec.kind == "le":
                    probability = norm.cdf((float(objective_spec.threshold) - mean[index]) / sigma)
                elif objective_spec.kind == "ge":
                    probability = 1.0 - norm.cdf(
                        (float(objective_spec.threshold) - mean[index]) / sigma
                    )
                else:
                    lower = (
                        float(objective_spec.target) - float(objective_spec.tol)
                        if objective_spec.kind == "target"
                        else float(objective_spec.lower)
                    )
                    upper = (
                        float(objective_spec.target) + float(objective_spec.tol)
                        if objective_spec.kind == "target"
                        else float(objective_spec.upper)
                    )
                    probability = norm.cdf((upper - mean[index]) / sigma) - norm.cdf(
                        (lower - mean[index]) / sigma
                    )
                feasibility *= float(np.clip(probability, 0.0, 1.0))
            for index, constraint_spec in enumerate(
                self.campaign.outcome_constraints
            ):
                output_index = objective_count + index
                sigma = max(float(deviation[output_index]), 1e-12)
                if constraint_spec.operator == "<=":
                    probability = norm.cdf(
                        (float(constraint_spec.limit) - mean[output_index])
                        / sigma
                    )
                else:
                    probability = 1.0 - norm.cdf(
                        (float(constraint_spec.limit) - mean[output_index])
                        / sigma
                    )
                feasibility *= float(np.clip(probability, 0.0, 1.0))
            acq_value = (
                float(validation_scores[selected_indices[row]])
                if validation_scores is not None and selected_indices is not None
                else float(selected_acquisition_values[row])
            )
            mean_outcomes = bounded_means
            results.append(
                Suggestion(
                    recommended_params=params,
                    objective_predictions=predictions,
                    constraint_predictions=constraint_predictions,
                    predicted_distance_to_spec=self.campaign.distance_to_spec(mean_outcomes),
                    feasibility_probability=feasibility,
                    acquisition_value=acq_value,
                    cold_start=False,
                    model_version=MODEL_VERSION,
                    rationale=(
                        "Hypothesis-validation design selected this safe point because it "
                        "maximizes outcome uncertainty while separating the hypothesis variables "
                        "from prior observations."
                        if validation_scores is not None
                        else "The production surrogate selected this parameter setting "
                        f"under the versioned {reach_policy} policy using only visible "
                        "observations."
                    ),
                )
            )
        return results

    def _select_diverse_points(
        self,
        candidates: np.ndarray,
        scores: np.ndarray,
        top_k: int,
    ) -> list[int]:
        order = np.argsort(-scores, kind="stable")
        selected: list[int] = []
        minimum_separation = 0.02 * math.sqrt(self.campaign.dim)
        for index in order:
            if selected and min(
                np.linalg.norm(candidates[index] - candidates[other])
                for other in selected
            ) < minimum_separation:
                continue
            selected.append(int(index))
            if len(selected) == top_k:
                return selected
        return [int(index) for index in order[:top_k]]
