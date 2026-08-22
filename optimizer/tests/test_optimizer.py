"""Verify optimizer success, rejection, and boundary behavior."""

import numpy as np
import pytest

from ingot_optimizer import (
    Campaign,
    Objective,
    OutcomeConstraint,
    ParameterConstraint,
    SequentialOptimizer,
    Variable,
)
from ingot_optimizer.gp import GaussianProcess


def make_campaign():
    return Campaign(
        "audit",
        [Variable("x", 0.0, 1.0), Variable("z", -1.0, 1.0)],
        [
            Objective("loss", "le", threshold=0.15),
            Objective("strength", "ge", threshold=0.7),
        ],
        [ParameterConstraint("x", "<=", 0.9, True)],
    )


def outcomes(params):
    x = params["x"]
    z = params["z"]
    return {
        "loss": (x - 0.65) ** 2 + 0.03 * z**2,
        "strength": 1.0 - (x - 0.7) ** 2 - 0.05 * z**2,
    }


def test_cold_start_is_feasible_and_deterministic():
    first = SequentialOptimizer(make_campaign(), seed=42).suggest(
        top_k=3, n_random=100
    )
    second = SequentialOptimizer(make_campaign(), seed=42).suggest(
        top_k=3, n_random=100
    )

    assert [value.to_dict() for value in first] == [
        value.to_dict() for value in second
    ]
    assert all(value.cold_start for value in first)
    assert all(value.recommended_params["x"] <= 0.9 for value in first)
    assert len({tuple(value.recommended_params.values()) for value in first}) == 3


def test_modelled_suggestions_include_uncertainty_and_are_reproducible():
    observations = [
        {"x": 0.05, "z": -0.8},
        {"x": 0.30, "z": 0.4},
        {"x": 0.55, "z": -0.2},
        {"x": 0.82, "z": 0.6},
    ]

    def run():
        optimizer = SequentialOptimizer(make_campaign(), seed=9)
        for params in observations:
            optimizer.observe(params, outcomes(params))
        return optimizer.suggest(
            top_k=2,
            n_random=150,
            n_samples=64,
            n_restarts=1,
        )

    first = run()
    second = run()
    assert [value.to_dict() for value in first] == pytest.approx(
        [value.to_dict() for value in second]
    )
    assert all(not value.cold_start for value in first)
    assert all(set(value.objective_predictions) == {"loss", "strength"} for value in first)
    assert all(0.0 <= value.feasibility_probability <= 1.0 for value in first)
    assert all(np.isfinite(value.predicted_distance_to_spec) for value in first)


def test_observe_rejects_incomplete_outcomes():
    optimizer = SequentialOptimizer(make_campaign())
    with pytest.raises(ValueError, match="outcome keys mismatch"):
        optimizer.observe({"x": 0.5, "z": 0.0}, {"loss": 0.1})


def test_candidate_pool_does_not_repeat_an_observed_parameter_setting():
    optimizer = SequentialOptimizer(make_campaign(), seed=1)
    params = {"x": 0.5, "z": 0.0}
    optimizer.observe(params, outcomes(params))
    with pytest.raises(ValueError, match="unobserved"):
        optimizer.suggest(
            candidate_params=[params],
            n_random=1,
            n_samples=32,
        )


def test_safety_constrained_cold_start_requires_and_stays_near_safe_baseline():
    campaign = Campaign(
        "safe-cold-start",
        [Variable("x", 0.0, 1.0)],
        [Objective("loss", "le", threshold=0.2)],
        outcome_constraints=[
            OutcomeConstraint(
                "crack_rate",
                "<=",
                0.05,
                safety_critical=True,
            )
        ],
    )
    optimizer = SequentialOptimizer(campaign, seed=3)
    with pytest.raises(ValueError, match="safe baseline"):
        optimizer.suggest(top_k=1, n_random=100)

    optimizer.observe(
        {"x": 0.5},
        {"loss": 0.3},
        constraint_outcomes={"crack_rate": 0.01},
    )
    suggestion = optimizer.suggest(top_k=1, n_random=500)[0]
    assert abs(suggestion.recommended_params["x"] - 0.5) <= 0.2


def test_dependency_light_gp_stays_finite_with_repeated_points_and_large_units():
    points = np.array([[0.1], [0.1], [0.5], [0.9]], dtype=float)
    outcomes = np.array([24_000.0, 24_000.0, 31_000.0, 40_000.0], dtype=float)

    model = GaussianProcess(n_restarts=1, seed=7).fit(points, outcomes)
    mean, deviation = model.predict(np.array([[0.1], [0.7]], dtype=float))

    assert np.isfinite(mean).all()
    assert np.isfinite(deviation).all()
    assert model.effective_jitter_ <= 1e-2
