"""Verify engine selection success, rejection, and boundary behavior."""

import numpy as np
import pytest

from ingot_optimizer import (
    BotorchOptimizer,
    Campaign,
    Objective,
    OptimizerObservation,
    OutcomeConstraint,
    SequentialOptimizer,
    Variable,
    build_optimizer,
)
from ingot_optimizer.botorch_engine import _reach_specification_scores


def safe_campaign():
    return Campaign(
        "shared-engine-policy",
        [Variable("x", 0.0, 1.0)],
        [Objective("loss", "le", threshold=0.05)],
        outcome_constraints=[
            OutcomeConstraint(
                "risk",
                "<=",
                0.5,
                safety_critical=True,
                minimum_probability=0.8,
            )
        ],
    )


def observation(x):
    return OptimizerObservation(
        params={"x": x},
        outcomes={"loss": (1.0 - x) ** 2},
        constraint_outcomes={"risk": x},
    )


def test_shared_policy_switches_to_botorch_at_three_observations():
    campaign = safe_campaign()

    assert isinstance(build_optimizer(campaign, [observation(0.1)]), SequentialOptimizer)
    assert isinstance(
        build_optimizer(campaign, [observation(0.1), observation(0.2), observation(0.3)]),
        BotorchOptimizer,
    )


def test_five_observation_policy_rejects_attractive_unsafe_candidate():
    optimizer = build_optimizer(
        safe_campaign(),
        [observation(value) for value in [0.0, 0.2, 0.4, 0.6, 0.8]],
        seed=17,
    )

    suggestions = optimizer.suggest(
        candidate_params=[{"x": 0.45}, {"x": 0.95}],
        n_random=2,
        n_samples=128,
        top_k=1,
    )

    assert suggestions[0].recommended_params["x"] == pytest.approx(0.45)


def test_reach_specification_scores_fall_back_to_dominant_linear_response():
    observed = np.asarray(
        [
            [0.0, 0.2],
            [0.1, 0.8],
            [0.2, 0.4],
            [0.4, 0.9],
            [0.6, 0.1],
            [0.8, 0.7],
            [0.9, 0.3],
            [1.0, 0.6],
        ]
    )
    distance = observed[:, 0] * 2.0 - 0.5
    candidates = np.asarray([[0.05, 0.95], [0.5, 0.0], [0.95, 0.5]])

    scores, policy = _reach_specification_scores(
        observed,
        distance,
        candidates,
        np.asarray([0.2, 0.9, 0.8]),
    )

    assert policy == "dominant-linear-response"
    assert int(np.argmax(scores)) == 0


def test_reach_specification_scores_use_raw_quadratic_without_declared_features():
    x = np.linspace(0.0, 1.0, 9)
    observed = np.column_stack([x, np.roll(x, 3)])
    distance = (x - 0.5) ** 2 + 0.1 * np.sin(8.0 * np.pi * x)
    candidates = np.asarray([[0.05, 0.5], [0.5, 0.5], [0.95, 0.5]])

    scores, policy = _reach_specification_scores(
        observed,
        distance,
        candidates,
        np.asarray([0.7, 0.6, 0.7]),
    )

    assert policy == "raw-quadratic-response"
    assert int(np.argmax(scores)) == 1


def test_reach_specification_scores_use_declared_features_in_quadratic_ensemble():
    x = np.linspace(0.0, 1.0, 9)
    observed = np.column_stack([x, np.roll(x, 3)])
    distance = (x - 0.5) ** 2 + 0.1 * np.sin(8.0 * np.pi * x)
    candidates = np.asarray([[0.05, 0.5], [0.5, 0.5], [0.95, 0.5]])
    observed_augmented = np.column_stack(
        [observed, observed[:, 0] * observed[:, 1]]
    )
    candidate_augmented = np.column_stack(
        [candidates, candidates[:, 0] * candidates[:, 1]]
    )

    scores, policy = _reach_specification_scores(
        observed,
        distance,
        candidates,
        np.asarray([0.7, 0.6, 0.7]),
        observed_augmented_points=observed_augmented,
        candidate_augmented_points=candidate_augmented,
    )

    assert policy == "mechanism-quadratic-response-ensemble"
    assert int(np.argmax(scores)) == 1
