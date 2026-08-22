"""Offline validation utilities.

Synthetic replay evaluates optimization mechanics against a callable response
surface.  Historical pool replay is deliberately more conservative: the
optimizer may only select an as-yet-unseen parameter setting that actually exists in the
history.  It never fabricates a response with nearest-neighbour substitution.
"""
from __future__ import annotations

from dataclasses import dataclass, field
import math
from typing import Callable, Mapping, Sequence

import numpy as np

from .campaign import Campaign
from .engine_selection import OptimizerObservation, build_optimizer
from .feature_transforms import DerivedFeature


@dataclass(frozen=True)
class SyntheticTruthResult:
    """Carries objective, constraint, and optional process outcomes from a simulator."""

    outcomes: Mapping[str, float]
    constraint_outcomes: Mapping[str, float] = field(default_factory=dict)
    process_features: Mapping[str, float] = field(default_factory=dict)


TruthFunction = Callable[[dict[str, float]], SyntheticTruthResult]


def _evaluate_truth(
    campaign: Campaign,
    truth_fn: TruthFunction,
    params: dict[str, float],
) -> OptimizerObservation:
    result = truth_fn(params)
    if not isinstance(result, SyntheticTruthResult):
        raise TypeError("synthetic replay truth functions must return SyntheticTruthResult")
    observation = OptimizerObservation(
        params=params,
        outcomes=result.outcomes,
        constraint_outcomes=result.constraint_outcomes,
        process_features=result.process_features,
    )
    campaign.validate_outcomes(observation.outcomes)
    campaign.validate_constraint_outcomes(observation.constraint_outcomes)
    return observation


def _is_success(campaign: Campaign, observation: OptimizerObservation) -> bool:
    return campaign.distance_to_spec(observation.outcomes) <= 0.0 and all(
        constraint.is_satisfied(
            float(observation.constraint_outcomes[constraint.name])
        )
        for constraint in campaign.outcome_constraints
    )


def _sample_feasible(campaign: Campaign, rng: np.random.Generator) -> dict[str, float]:
    for _ in range(10_000):
        point = rng.uniform(0.0, 1.0, campaign.dim)
        if campaign.is_feasible_unit(point):
            return campaign.from_unit(point)
    raise ValueError("unable to sample a feasible campaign point")


def _summarize(runs: Sequence[int | None]) -> dict[str, float | int | None]:
    hits = [value for value in runs if value is not None]
    return {
        "success_rate": len(hits) / len(runs),
        "median_trials": float(np.median(hits)) if hits else None,
        "mean_trials": float(np.mean(hits)) if hits else None,
        "runs": len(runs),
    }


def _run_optimizer(
    campaign: Campaign,
    truth_fn: TruthFunction,
    budget: int,
    seed: int,
    n_seed_points: int = 2,
    prior_means: Mapping[str, object] | None = None,
) -> int | None:
    observations: list[OptimizerObservation] = []
    rng = np.random.default_rng(seed)
    for _ in range(min(n_seed_points, budget)):
        params = _sample_feasible(campaign, rng)
        observation = _evaluate_truth(campaign, truth_fn, params)
        observations.append(observation)
        if _is_success(campaign, observation):
            return len(observations)
    while len(observations) < budget:
        optimizer = build_optimizer(
            campaign,
            observations,
            prior_means=prior_means,
            seed=seed,
        )
        params = optimizer.suggest()[0].recommended_params
        observation = _evaluate_truth(campaign, truth_fn, params)
        observations.append(observation)
        if _is_success(campaign, observation):
            return len(observations)
    return None


def _run_random(
    campaign: Campaign, truth_fn: TruthFunction, budget: int, seed: int
) -> int | None:
    rng = np.random.default_rng(seed + 10_000)
    for trial in range(1, budget + 1):
        params = _sample_feasible(campaign, rng)
        if _is_success(campaign, _evaluate_truth(campaign, truth_fn, params)):
            return trial
    return None


def replay_synthetic(
    campaign: Campaign,
    truth_fn: TruthFunction,
    *,
    budget: int = 40,
    n_seeds: int = 30,
    prior_means: Mapping[str, object] | None = None,
) -> dict:
    """Compare sequential optimization with random search on a simulator."""
    if budget < 1 or n_seeds < 1:
        raise ValueError("budget and n_seeds must be positive")
    optimizer_runs = [
        _run_optimizer(
            campaign,
            truth_fn,
            budget,
            seed,
            prior_means=prior_means,
        )
        for seed in range(n_seeds)
    ]
    random_runs = [
        _run_random(campaign, truth_fn, budget, seed)
        for seed in range(n_seeds)
    ]
    return {
        "optimizer": _summarize(optimizer_runs),
        "random": _summarize(random_runs),
        "raw_optimizer": optimizer_runs,
        "raw_random": random_runs,
        "evidence_kind": "synthetic",
    }


def _validate_history(campaign: Campaign, history: Sequence[dict]) -> None:
    if not history:
        raise ValueError("history must not be empty")
    seen: set[tuple[float, ...]] = set()
    previous_occurred_at: float | None = None
    for index, row in enumerate(history):
        required = {"params", "outcomes", "occurred_at"}
        allowed = required | {
            "constraint_outcomes",
            "process_features",
            "run_id",
        }
        if not required.issubset(row) or set(row).difference(allowed):
            raise ValueError(
                f"history row {index} must contain params, outcomes, occurred_at "
                "and only supported provenance fields"
            )
        try:
            occurred_at = float(row["occurred_at"])
        except (TypeError, ValueError) as error:
            raise ValueError(
                f"history row {index} occurred_at must be a finite number"
            ) from error
        if not math.isfinite(occurred_at):
            raise ValueError(
                f"history row {index} occurred_at must be a finite number"
            )
        if previous_occurred_at is not None and occurred_at <= previous_occurred_at:
            raise ValueError(
                f"history row {index} occurred_at must be strictly later than row "
                f"{index - 1}"
            )
        previous_occurred_at = occurred_at
        unit_point = campaign.to_unit(
            row["params"], enforce_candidate_constraints=False
        )
        campaign.validate_outcomes(row["outcomes"])
        campaign.validate_constraint_outcomes(row.get("constraint_outcomes", {}))
        key = tuple(np.round(unit_point, 12))
        if key in seen:
            raise ValueError(
                "historical pool replay requires unique parameter settings; aggregate "
                "replicates before replay"
            )
        seen.add(key)


def _historical_original_order(
    campaign: Campaign, history: Sequence[dict], budget: int
) -> int | None:
    for index, row in enumerate(history[:budget], start=1):
        if campaign.distance_to_spec(row["outcomes"]) <= 0.0:
            return index
    return None


def _historical_optimizer_run(
    campaign: Campaign,
    history: Sequence[dict],
    budget: int,
    seed: int,
    initial_observation_count: int,
    derived_features: Sequence[DerivedFeature] | None,
    soft_constraints: Sequence[dict] | None,
) -> tuple[int | None, list[int], list[dict], dict]:
    selected = list(range(initial_observation_count))
    remaining = list(range(initial_observation_count, len(history)))
    trace: list[dict] = [
        {
            "step": index + 1,
            "kind": "preregistered-initial-observation",
            "visible_observation_indices_before": list(range(index)),
            "revealed_history_index": index,
            "run_id": history[index].get("run_id"),
        }
        for index in selected
    ]
    interval_total = 0
    interval_covered = 0
    if any(campaign.distance_to_spec(history[index]["outcomes"]) <= 0 for index in selected):
        first = next(
            position + 1
            for position, index in enumerate(selected)
            if campaign.distance_to_spec(history[index]["outcomes"]) <= 0
        )
        return first, selected, trace, {
            "prediction_interval_checks": 0,
            "prediction_interval_covered": 0,
            "prediction_interval_coverage": None,
            "safety_violations": _safety_violations(campaign, history, selected),
        }
    while remaining and len(selected) < budget:
        optimizer = build_optimizer(
            campaign,
            [
                OptimizerObservation(
                    params=history[index]["params"],
                    outcomes=history[index]["outcomes"],
                    constraint_outcomes=history[index].get(
                        "constraint_outcomes", {}
                    ),
                    process_features=history[index].get("process_features", {}),
                )
                for index in selected
            ],
            derived_features=derived_features,
            seed=seed,
        )
        feasible_remaining = [
            index
            for index in remaining
            if campaign.is_feasible_unit(
                campaign.to_unit(
                    history[index]["params"], enforce_candidate_constraints=False
                )
            )
        ]
        if not feasible_remaining:
            break
        candidates = [history[index]["params"] for index in feasible_remaining]
        try:
            suggestions = optimizer.suggest(
                candidate_params=candidates,
                n_random=len(candidates),
                n_samples=256,
                top_k=min(4, len(candidates)),
            )
        except ValueError as error:
            if "fewer" not in str(error):
                raise
            suggestions = optimizer.suggest(
                candidate_params=candidates,
                n_random=len(candidates),
                n_samples=256,
                top_k=1,
            )
        acquisitions = np.asarray([
            value.acquisition_value if value.acquisition_value is not None else 0.0
            for value in suggestions
        ], dtype=float)
        width = max(float(np.max(acquisitions) - np.min(acquisitions)), 1e-12)
        def penalty(value) -> float:
            if not soft_constraints:
                return 0.0
            penalties = []
            variable_by_name = {item.name: item for item in campaign.variables}
            for constraint in soft_constraints:
                code = constraint["variable_code"]
                variable = variable_by_name[code]
                current = value.recommended_params[code]
                span = max(variable.high - variable.low, 1e-12)
                minimum = constraint.get("minimum")
                maximum = constraint.get("maximum")
                below = max((minimum if minimum is not None else -np.inf) - current, 0.0)
                above = max(current - (maximum if maximum is not None else np.inf), 0.0)
                penalties.append(min((below + above) / span, 1.0))
            return float(np.mean(penalties))
        suggestion = max(
            suggestions,
            key=lambda value: 0.75 * ((value.acquisition_value or 0.0) - float(np.min(acquisitions))) / width
            - 0.25 * penalty(value),
        )
        suggested_unit = campaign.to_unit(suggestion.recommended_params)
        position = next(
            position
            for position, history_index in enumerate(remaining)
            if np.allclose(
                campaign.to_unit(history[history_index]["params"]),
                suggested_unit,
                rtol=0.0,
                atol=1e-10,
            )
        )
        history_index = remaining.pop(position)
        checks = 0
        covered = 0
        for objective in campaign.objectives:
            prediction = suggestion.objective_predictions.get(objective.name)
            if prediction is None:
                continue
            checks += 1
            actual = float(history[history_index]["outcomes"][objective.name])
            if prediction.lower_95 <= actual <= prediction.upper_95:
                covered += 1
        interval_total += checks
        interval_covered += covered
        trace.append(
            {
                "step": len(selected) + 1,
                "kind": "optimizer-selection",
                "visible_observation_indices_before": selected.copy(),
                "candidate_history_indices": feasible_remaining,
                "revealed_history_index": history_index,
                "run_id": history[history_index].get("run_id"),
                "model_version": suggestion.model_version,
                "recommended_params": suggestion.recommended_params,
                "nearest_historical_candidate_distance": float(
                    np.linalg.norm(
                        campaign.to_unit(suggestion.recommended_params)
                        - campaign.to_unit(history[history_index]["params"])
                    )
                ),
                "prediction_interval_checks": checks,
                "prediction_interval_covered": covered,
                "mechanism_soft_penalty": penalty(suggestion),
            }
        )
        selected.append(history_index)
        if campaign.distance_to_spec(history[history_index]["outcomes"]) <= 0:
            break
    hit = next(
        (
            position + 1
            for position, index in enumerate(selected)
            if campaign.distance_to_spec(history[index]["outcomes"]) <= 0
        ),
        None,
    )
    return hit, selected, trace, {
        "prediction_interval_checks": interval_total,
        "prediction_interval_covered": interval_covered,
        "prediction_interval_coverage": (
            interval_covered / interval_total if interval_total else None
        ),
        "safety_violations": _safety_violations(campaign, history, selected),
    }

def _safety_violations(
    campaign: Campaign, history: Sequence[dict], selected: Sequence[int]
) -> int:
    violations = 0
    for index in selected:
        outcomes = history[index].get("constraint_outcomes", {})
        if any(
            constraint.safety_critical
            and not constraint.is_satisfied(float(outcomes[constraint.name]))
            for constraint in campaign.outcome_constraints
        ):
            violations += 1
    return violations


def _historical_random_run(
    campaign: Campaign,
    history: Sequence[dict],
    budget: int,
    seed: int,
    initial_observation_count: int,
) -> tuple[int | None, list[int], int]:
    selected = list(range(initial_observation_count))
    first_hit = next((
        position + 1
        for position, index in enumerate(selected)
        if campaign.distance_to_spec(history[index]["outcomes"]) <= 0.0
    ), None)
    if first_hit is not None:
        return first_hit, selected, _safety_violations(campaign, history, selected)
    remaining = np.asarray([
        index
        for index in range(initial_observation_count, len(history))
        if campaign.is_feasible_unit(
            campaign.to_unit(
                history[index]["params"], enforce_candidate_constraints=False
            )
        )
    ])
    order = np.random.default_rng(seed + 20_000).permutation(remaining)
    for index in order[: max(0, budget - len(selected))]:
        selected.append(int(index))
        if campaign.distance_to_spec(history[int(index)]["outcomes"]) <= 0.0:
            return len(selected), selected, _safety_violations(campaign, history, selected)
    return None, selected, _safety_violations(campaign, history, selected)


def _quadratic_features(points: np.ndarray) -> np.ndarray:
    columns = [np.ones(points.shape[0])]
    columns.extend(points[:, index] for index in range(points.shape[1]))
    columns.extend(points[:, index] ** 2 for index in range(points.shape[1]))
    columns.extend(
        points[:, left] * points[:, right]
        for left in range(points.shape[1])
        for right in range(left + 1, points.shape[1])
    )
    return np.column_stack(columns)


def _historical_response_surface_run(
    campaign: Campaign,
    history: Sequence[dict],
    budget: int,
    seed: int,
    initial_observation_count: int,
) -> tuple[int | None, list[int], int]:
    """Rank the frozen candidate pool with a classical quadratic response surface.

    The fit uses only already revealed rows. It is deliberately an unregularized,
    low-complexity comparator rather than another production model.
    """
    selected = list(range(initial_observation_count))
    remaining = [
        index
        for index in range(initial_observation_count, len(history))
        if campaign.is_feasible_unit(
            campaign.to_unit(
                history[index]["params"], enforce_candidate_constraints=False
            )
        )
    ]
    first_hit = next((
        position + 1
        for position, index in enumerate(selected)
        if campaign.distance_to_spec(history[index]["outcomes"]) <= 0.0
    ), None)
    if first_hit is not None:
        return first_hit, selected, _safety_violations(campaign, history, selected)
    rng = np.random.default_rng(seed + 30_000)
    while remaining and len(selected) < budget:
        observed_points = np.asarray([
            campaign.to_unit(history[index]["params"]) for index in selected
        ])
        observed_distances = np.asarray([
            campaign.distance_to_spec(history[index]["outcomes"]) for index in selected
        ])
        candidate_points = np.asarray([
            campaign.to_unit(history[index]["params"]) for index in remaining
        ])
        coefficients = np.linalg.lstsq(
            _quadratic_features(observed_points), observed_distances, rcond=None
        )[0]
        predictions = _quadratic_features(candidate_points) @ coefficients
        # Randomized tie-breaking is preregistered by seed; outcomes remain hidden.
        position = int(np.lexsort((rng.random(len(remaining)), predictions))[0])
        history_index = remaining.pop(position)
        selected.append(history_index)
        if campaign.distance_to_spec(history[history_index]["outcomes"]) <= 0.0:
            return len(selected), selected, _safety_violations(campaign, history, selected)
    return None, selected, _safety_violations(campaign, history, selected)


def replay_optimizer_history_pool_once(
    campaign: Campaign,
    history: Sequence[dict],
    *,
    budget: int,
    initial_observation_count: int,
    seed: int,
    derived_features: Sequence[DerivedFeature] | None = None,
    soft_constraints: Sequence[dict] | None = None,
) -> dict:
    """Run one auditable optimizer episode against a finite hidden-result pool."""
    _validate_history(campaign, history)
    if budget < 1 or budget > len(history):
        raise ValueError("budget must be between 1 and the history length")
    if initial_observation_count < 0 or initial_observation_count > budget:
        raise ValueError("initial_observation_count must be between 0 and budget")
    if seed < 0:
        raise ValueError("seed must be non-negative")
    if campaign.outcome_constraints and initial_observation_count == 0:
        raise ValueError(
            "historical replay with outcome safety constraints requires preregistered initial observations"
        )
    total_trials, selected, trace, diagnostics = _historical_optimizer_run(
        campaign,
        history,
        budget,
        seed,
        initial_observation_count,
        derived_features,
        soft_constraints,
    )
    return {
        "total_trials": total_trials,
        "additional_trials": (
            total_trials - initial_observation_count
            if total_trials is not None
            else None
        ),
        "selected_history_indices": selected,
        "step_trace": trace,
        "diagnostics": diagnostics,
        "budget": budget,
        "initial_observation_count": initial_observation_count,
        "seed": seed,
        "evidence_kind": "historical-pool-ranking",
    }


def replay_history_pool(
    campaign: Campaign,
    history: Sequence[dict],
    *,
    budget: int | None = None,
    n_seeds: int = 30,
    initial_observation_count: int = 0,
    seed_offset: int = 0,
    derived_features: Sequence[DerivedFeature] | None = None,
    soft_constraints: Sequence[dict] | None = None,
) -> dict:
    """Evaluate parameter setting ranking using only parameter settings and outcomes present in history.

    This is evidence about ranking observed parameter settings, not a counterfactual claim
    about untried parameter settings or guaranteed online performance.
    """
    _validate_history(campaign, history)
    if n_seeds < 1:
        raise ValueError("n_seeds must be positive")
    if seed_offset < 0:
        raise ValueError("seed_offset must be non-negative")
    effective_budget = len(history) if budget is None else budget
    if effective_budget < 1 or effective_budget > len(history):
        raise ValueError("budget must be between 1 and the history length")
    if initial_observation_count < 0 or initial_observation_count > effective_budget:
        raise ValueError("initial_observation_count must be between 0 and budget")
    if campaign.outcome_constraints and initial_observation_count == 0:
        raise ValueError(
            "historical replay with outcome safety constraints requires preregistered initial observations"
        )
    optimizer_results = [
        _historical_optimizer_run(
            campaign,
            history,
            effective_budget,
            seed,
            initial_observation_count,
            derived_features,
            soft_constraints,
        )
        for seed in range(seed_offset, seed_offset + n_seeds)
    ]
    optimizer_runs = [result[0] for result in optimizer_results]
    random_results = [
        _historical_random_run(
            campaign, history, effective_budget, seed, initial_observation_count
        )
        for seed in range(seed_offset, seed_offset + n_seeds)
    ]
    random_runs = [result[0] for result in random_results]
    response_surface_applicable = initial_observation_count >= 2
    response_surface_results = [
        _historical_response_surface_run(
            campaign, history, effective_budget, seed, initial_observation_count
        )
        for seed in range(seed_offset, seed_offset + n_seeds)
    ] if response_surface_applicable else []
    response_surface_runs = [result[0] for result in response_surface_results]
    original_hit = _historical_original_order(campaign, history, effective_budget)
    original_selected = list(range(original_hit or effective_budget))
    return {
        "original_order_trials": original_hit,
        "optimizer": _summarize(optimizer_runs),
        "random": _summarize(random_runs),
        "response_surface": (
            _summarize(response_surface_runs)
            if response_surface_applicable
            else {
                "applicable": False,
                "reason": "quadratic response-surface baseline requires at least two preregistered initial observations",
            }
        ),
        "raw_optimizer": optimizer_runs,
        "raw_random": random_runs,
        "random_selected_history_indices": [result[1] for result in random_results],
        "response_surface_selected_history_indices": [
            result[1] for result in response_surface_results
        ],
        "selected_history_indices": [
            result[1] for result in optimizer_results
        ],
        "step_traces": [result[2] for result in optimizer_results],
        "calibration": [result[3] for result in optimizer_results],
        "safety_violations": {
            "original_order": _safety_violations(
                campaign, history, original_selected
            ),
            "optimizer": [result[3]["safety_violations"] for result in optimizer_results],
            "random": [result[2] for result in random_results],
            "response_surface": [result[2] for result in response_surface_results],
        },
        "budget": effective_budget,
        "initial_observation_count": initial_observation_count,
        "seed_offset": seed_offset,
        "engine_policy": "production-equivalent: sequential below 3 observations, BoTorch at 3 or more",
        "evidence_kind": "historical-pool-ranking",
        "baseline_methods": [
            "historical-engineer-order",
            "seeded-random-order",
            "quadratic-response-surface",
        ],
        "limitations": (
            "Ranks only parameter settings present in the supplied history; it does not "
            "estimate outcomes for parameter settings that were never run, does not support "
            "exact control-setting replication until production repeat scheduling is enabled, "
            "and does not prove online furnace savings."
        ),
    }
