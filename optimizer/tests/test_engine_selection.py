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
from ingot_optimizer.botorch_engine import (
    _feature_representation_is_admitted,
    _has_one_standard_error_improvement,
    _reach_specification_scores,
)


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


def test_reach_specification_scores_keep_linear_when_quadratic_lacks_evidence():
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
        np.ones(len(candidates)),
    )

    assert policy == "one-standard-error-linear-response"
    assert int(np.argmax(scores)) == 0


def test_reach_specification_scores_keep_sparse_history_on_linear_response():
    observed = np.asarray(
        [[0.0, 0.0], [0.5, 1.0], [1.0, 0.25]]
    )
    distance = np.asarray([0.8, 0.2, 0.6])
    candidates = np.asarray([[0.1, 0.9], [0.5, 0.5], [0.9, 0.1]])

    _, policy = _reach_specification_scores(
        observed,
        distance,
        candidates,
        np.ones(len(candidates)),
    )

    assert policy == "capacity-linear-response"


def test_reach_specification_scores_use_raw_quadratic_without_features():
    x = np.linspace(0.0, 1.0, 9)
    observed = np.column_stack([x, np.roll(x, 3)])
    distance = (x - 0.5) ** 2 + 0.1 * np.sin(8.0 * np.pi * x)
    candidates = np.asarray([[0.05, 0.5], [0.5, 0.5], [0.95, 0.5]])

    scores, policy = _reach_specification_scores(
        observed,
        distance,
        candidates,
        np.ones(len(candidates)),
    )

    assert policy == "evidence-quadratic-response"
    assert int(np.argmax(scores)) == 1


def test_feature_representation_admission_requires_paired_predictive_evidence():
    observed = np.asarray(
        [
            [0.0, 0.0],
            [0.0, 0.5],
            [0.0, 1.0],
            [0.5, 0.0],
            [0.5, 0.5],
            [0.5, 1.0],
            [1.0, 0.0],
            [1.0, 0.5],
            [1.0, 1.0],
        ]
    )
    distance = np.abs(observed[:, 0] - observed[:, 1])
    candidates = np.asarray([[0.1, 0.1], [0.5, 0.5], [0.9, 0.9]])
    observed_augmented = np.column_stack(
        [observed, np.abs(observed[:, 0] - observed[:, 1])]
    )
    admitted = _feature_representation_is_admitted(
        observed,
        distance,
        observed_augmented,
    )

    assert admitted is True


def test_reach_specification_scores_reject_unhelpful_declared_features():
    observed = np.asarray(
        [
            [0.0, 0.0],
            [0.0, 0.5],
            [0.0, 1.0],
            [0.5, 0.0],
            [0.5, 0.5],
            [0.5, 1.0],
            [1.0, 0.0],
            [1.0, 0.5],
            [1.0, 1.0],
        ]
    )
    distance = observed[:, 0] + observed[:, 1]
    unhelpful = np.asarray(
        [
            [0, 1, 0, 1, 0, 1, 0],
            [1, 0, 1, 0, 1, 0, 1],
            [0, 0, 1, 1, 0, 0, 1],
            [1, 1, 0, 0, 1, 1, 0],
            [0, 1, 1, 0, 0, 1, 1],
            [1, 0, 0, 1, 1, 0, 0],
            [0, 1, 0, 1, 1, 0, 1],
            [1, 0, 1, 0, 0, 1, 0],
            [0, 0, 1, 1, 1, 0, 0],
        ]
    )
    admitted = _feature_representation_is_admitted(
        observed,
        distance,
        np.column_stack([observed, unhelpful]),
    )

    assert admitted is False


def test_reach_specification_scores_require_predictive_gain_with_enough_observations():
    x = np.linspace(0.0, 1.0, 9)
    observed = np.column_stack([x, np.roll(x, 3)])
    distance = (x - 0.5) ** 2 + 0.1 * np.sin(8.0 * np.pi * x)
    observed_augmented = np.column_stack(
        [observed, observed[:, 0] * observed[:, 1]]
    )

    admitted = _feature_representation_is_admitted(
        observed,
        distance,
        observed_augmented,
    )

    assert admitted is False


def test_reach_specification_scores_admit_gp_only_after_six_observations_per_dimension():
    observed = np.random.default_rng(0).random((12, 2))
    distance = (observed[:, 0] - 0.3) ** 2 + 2.0 * (
        observed[:, 1] - 0.7
    ) ** 2
    candidates = np.asarray([[0.05, 0.5], [0.5, 0.5], [0.95, 0.5]])

    _, before_policy = _reach_specification_scores(
        observed[:-1],
        distance[:-1],
        candidates,
        np.asarray([0.7, 0.6, 0.7]),
    )
    _, admitted_policy = _reach_specification_scores(
        observed,
        distance,
        candidates,
        np.asarray([0.7, 0.6, 0.7]),
    )

    assert before_policy == "evidence-quadratic-response"
    assert admitted_policy == "nonlinear-evidence-gp-probability"


def test_reach_specification_scores_do_not_infer_complexity_from_dimension():
    rng = np.random.default_rng(42)
    observed = rng.random((12, 4))
    distance = (
        np.sin(observed[:, 0] * 7.0)
        + np.cos(observed[:, 1] * 5.0)
        + observed[:, 2] * observed[:, 3]
    )
    candidates = rng.random((16, 4))

    _, policy = _reach_specification_scores(
        observed,
        distance,
        candidates,
        np.full(len(candidates), 0.5),
    )

    assert policy == "one-standard-error-linear-response"


def test_one_standard_error_rule_rejects_uncertain_gain():
    reference = np.asarray([4.0, 1.0, 4.0, 1.0])
    candidate = np.asarray([1.0, 4.0, 1.0, 4.0])

    assert _has_one_standard_error_improvement(reference, candidate) is False
    assert _has_one_standard_error_improvement(
        reference, np.asarray([1.0, 0.25, 1.0, 0.25])
    ) is True


def test_reach_specification_scores_keep_linear_when_models_disagree_without_evidence():
    rng = np.random.default_rng(1)
    observed = rng.random((8, 2))
    distance = observed[:, 0] + 0.2 * rng.normal(size=8)
    candidates = np.random.default_rng(3).random((5, 2))

    scores, policy = _reach_specification_scores(
        observed,
        distance,
        candidates,
        np.ones(len(candidates)),
    )

    assert policy == "one-standard-error-linear-response"
    assert int(np.argmax(scores)) == 0
