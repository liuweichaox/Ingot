"""Offline validation utilities.

Synthetic replay evaluates optimization mechanics against a callable response
surface.  Historical pool replay is deliberately more conservative: the
optimizer may only select an as-yet-unseen recipe that actually exists in the
history.  It never fabricates a response with nearest-neighbour substitution.
"""
from __future__ import annotations

from typing import Callable, Mapping, Sequence

import numpy as np

from .botorch_engine import BotorchOptimizer
from .campaign import Campaign
from .feature_transforms import DerivedFeature
from .loop import SequentialOptimizer


TruthFunction = Callable[[dict[str, float]], Mapping[str, float]]


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
    optimizer = SequentialOptimizer(campaign, prior_means=prior_means, seed=seed)
    rng = np.random.default_rng(seed)
    for _ in range(min(n_seed_points, budget)):
        params = campaign.from_unit(rng.uniform(0.0, 1.0, campaign.dim))
        optimizer.observe(params, truth_fn(params))
        if optimizer.in_spec():
            return len(optimizer.distances)
    while len(optimizer.distances) < budget:
        params = optimizer.suggest()[0].recommended_params
        optimizer.observe(params, truth_fn(params))
        if optimizer.in_spec():
            return len(optimizer.distances)
    return None


def _run_random(
    campaign: Campaign, truth_fn: TruthFunction, budget: int, seed: int
) -> int | None:
    rng = np.random.default_rng(seed + 10_000)
    for trial in range(1, budget + 1):
        params = campaign.from_unit(rng.uniform(0.0, 1.0, campaign.dim))
        if campaign.distance_to_spec(truth_fn(params)) <= 0.0:
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
    for index, row in enumerate(history):
        required = {"params", "outcomes"}
        allowed = required | {
            "constraint_outcomes",
            "process_features",
            "run_id",
            "occurred_at",
        }
        if not required.issubset(row) or set(row).difference(allowed):
            raise ValueError(
                f"history row {index} must contain params and outcomes and only supported provenance fields"
            )
        unit_point = campaign.to_unit(row["params"])
        campaign.validate_outcomes(row["outcomes"])
        campaign.validate_constraint_outcomes(row.get("constraint_outcomes", {}))
        key = tuple(np.round(unit_point, 12))
        if key in seen:
            raise ValueError(
                "historical pool replay requires unique recipes; aggregate "
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
        optimizer = (
            BotorchOptimizer(
                campaign,
                derived_features=derived_features,
                seed=seed,
            )
            if len(selected) >= 3
            else SequentialOptimizer(campaign, seed=seed)
        )
        for observed_index in selected:
            row = history[observed_index]
            optimizer.observe(
                row["params"],
                row["outcomes"],
                constraint_outcomes=row.get("constraint_outcomes"),
                process_features=row.get("process_features"),
            )
        candidates = [history[index]["params"] for index in remaining]
        suggestion = optimizer.suggest(
            candidate_params=candidates,
            n_random=len(candidates),
            n_samples=256,
        )[0]
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
                "candidate_history_indices": remaining[:position]
                + [history_index]
                + remaining[position:],
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
) -> tuple[int | None, list[int], int]:
    order = np.random.default_rng(seed + 20_000).permutation(len(history))
    selected: list[int] = []
    for trial, index in enumerate(order[:budget], start=1):
        selected.append(int(index))
        if campaign.distance_to_spec(history[int(index)]["outcomes"]) <= 0.0:
            return trial, selected, _safety_violations(campaign, history, selected)
    return None, selected, _safety_violations(campaign, history, selected)


def replay_history_pool(
    campaign: Campaign,
    history: Sequence[dict],
    *,
    budget: int | None = None,
    n_seeds: int = 30,
    initial_observation_count: int = 0,
    derived_features: Sequence[DerivedFeature] | None = None,
) -> dict:
    """Evaluate recipe ranking using only recipes and outcomes present in history.

    This is evidence about ranking observed recipes, not a counterfactual claim
    about untried recipes or guaranteed online performance.
    """
    _validate_history(campaign, history)
    if n_seeds < 1:
        raise ValueError("n_seeds must be positive")
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
        )
        for seed in range(n_seeds)
    ]
    optimizer_runs = [result[0] for result in optimizer_results]
    random_results = [
        _historical_random_run(
            campaign, history, effective_budget, seed
        )
        for seed in range(n_seeds)
    ]
    random_runs = [result[0] for result in random_results]
    original_hit = _historical_original_order(campaign, history, effective_budget)
    original_selected = list(range(original_hit or effective_budget))
    return {
        "original_order_trials": original_hit,
        "optimizer": _summarize(optimizer_runs),
        "random": _summarize(random_runs),
        "raw_optimizer": optimizer_runs,
        "raw_random": random_runs,
        "random_selected_history_indices": [result[1] for result in random_results],
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
        },
        "budget": effective_budget,
        "initial_observation_count": initial_observation_count,
        "engine_policy": "production-equivalent: sequential below 3 observations, BoTorch at 3 or more",
        "evidence_kind": "historical-pool-ranking",
        "limitations": (
            "Ranks only recipes present in the supplied history; it does not "
            "estimate outcomes for recipes that were never run, does not support "
            "exact-recipe replication until production repeat scheduling is enabled, "
            "and does not prove online furnace savings."
        ),
    }


# Backwards-compatible name for callers of the original synthetic helper.
replay = replay_synthetic
