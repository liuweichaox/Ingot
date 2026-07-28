"""Offline validation utilities.

Synthetic replay evaluates optimization mechanics against a callable response
surface.  Historical pool replay is deliberately more conservative: the
optimizer may only select an as-yet-unseen recipe that actually exists in the
history.  It never fabricates a response with nearest-neighbour substitution.
"""
from __future__ import annotations

from typing import Callable, Mapping, Sequence

import numpy as np

from .campaign import Campaign
from .loop import SequentialOptimizer


TruthFunction = Callable[[dict[str, float]], Mapping[str, float]]


def _summarize(runs: Sequence[int | None]) -> dict[str, float | int]:
    hits = [value for value in runs if value is not None]
    return {
        "success_rate": len(hits) / len(runs),
        "median_trials": float(np.median(hits)) if hits else float("nan"),
        "mean_trials": float(np.mean(hits)) if hits else float("nan"),
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
        if set(row) != {"params", "outcomes"}:
            raise ValueError(
                f"history row {index} must contain exactly params and outcomes"
            )
        unit_point = campaign.to_unit(row["params"])
        campaign.validate_outcomes(row["outcomes"])
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
) -> tuple[int | None, list[int]]:
    optimizer = SequentialOptimizer(campaign, seed=seed)
    remaining = list(range(len(history)))
    selected: list[int] = []
    while remaining and len(selected) < budget:
        candidates = [history[index]["params"] for index in remaining]
        suggestion = optimizer.suggest(
            candidate_params=candidates,
            n_random=len(candidates),
            n_samples=128,
            n_restarts=2,
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
        selected.append(history_index)
        optimizer.observe(
            history[history_index]["params"], history[history_index]["outcomes"]
        )
        if optimizer.in_spec():
            return len(selected), selected
    return None, selected


def _historical_random_run(
    campaign: Campaign,
    history: Sequence[dict],
    budget: int,
    seed: int,
) -> int | None:
    order = np.random.default_rng(seed + 20_000).permutation(len(history))
    for trial, index in enumerate(order[:budget], start=1):
        if campaign.distance_to_spec(history[int(index)]["outcomes"]) <= 0.0:
            return trial
    return None


def replay_history_pool(
    campaign: Campaign,
    history: Sequence[dict],
    *,
    budget: int | None = None,
    n_seeds: int = 30,
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
    optimizer_results = [
        _historical_optimizer_run(campaign, history, effective_budget, seed)
        for seed in range(n_seeds)
    ]
    optimizer_runs = [result[0] for result in optimizer_results]
    random_runs = [
        _historical_random_run(
            campaign, history, effective_budget, seed
        )
        for seed in range(n_seeds)
    ]
    return {
        "original_order_trials": _historical_original_order(
            campaign, history, effective_budget
        ),
        "optimizer": _summarize(optimizer_runs),
        "random": _summarize(random_runs),
        "raw_optimizer": optimizer_runs,
        "raw_random": random_runs,
        "selected_history_indices": [
            result[1] for result in optimizer_results
        ],
        "budget": effective_budget,
        "evidence_kind": "historical-pool-ranking",
        "limitations": (
            "Ranks only recipes present in the supplied history; it does not "
            "estimate outcomes for recipes that were never run and does not "
            "prove online furnace savings."
        ),
    }


# Backwards-compatible name for callers of the original synthetic helper.
replay = replay_synthetic
