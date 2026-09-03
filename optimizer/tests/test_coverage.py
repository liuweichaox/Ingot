"""Verify observed-coverage admission, rejection, and boundary behavior."""

import numpy as np
import pytest

from ingot_optimizer import (
    Campaign,
    Objective,
    OptimizerObservation,
    Variable,
    build_coverage_envelope,
    build_optimizer,
)
from ingot_optimizer.coverage import (
    describe_coverage_envelope,
    sample_within_envelope,
)


def collinear_campaign():
    return Campaign(
        "collinear-production-runs",
        [Variable("x", 0.0, 1.0), Variable("y", 0.0, 1.0)],
        [Objective("loss", "le", threshold=0.01)],
    )


def collinear_observations():
    """Return runs that moved both settings together, as production usually does."""
    return [
        OptimizerObservation(
            params={"x": value, "y": value},
            outcomes={"loss": (value - 0.9) ** 2},
        )
        for value in (0.2, 0.4, 0.6)
    ]


def test_envelope_admits_interpolation_inside_a_sparse_cluster():
    envelope = build_coverage_envelope(np.asarray([[0.0, 0.0], [1.0, 0.0], [0.0, 1.0]]))

    assert bool(envelope.contains(np.asarray([[1 / 3, 1 / 3]]))[0]) is True
    assert bool(envelope.contains(np.asarray([[0.3, 0.3]]))[0]) is True


def test_envelope_rejects_a_corner_the_runs_never_covered():
    observed = np.asarray([[0.0, 0.0], [1.0, 0.0], [0.0, 1.0]])
    envelope = build_coverage_envelope(observed)

    # Every variable is individually inside its observed range, so only the
    # leverage gate can reject this corner.
    assert float(envelope.lower.max()) == 0.0
    assert float(envelope.upper.min()) == 1.0
    assert bool(envelope.contains(np.asarray([[1.0, 1.0]]))[0]) is False


def test_envelope_limits_a_variable_production_never_varied():
    envelope = build_coverage_envelope(
        np.asarray([[0.1, 0.5], [0.3, 0.5], [0.5, 0.5]])
    )

    assert bool(envelope.contains(np.asarray([[0.3, 0.51]]))[0]) is True
    assert bool(envelope.contains(np.asarray([[0.3, 0.60]]))[0]) is False


def test_envelope_requires_three_observations():
    with pytest.raises(ValueError, match="at least 3 observations"):
        build_coverage_envelope(np.asarray([[0.2, 0.2], [0.4, 0.4]]))


def test_sampled_candidates_stay_inside_the_envelope():
    envelope = build_coverage_envelope(
        np.asarray([[0.2, 0.2], [0.4, 0.4], [0.6, 0.6]])
    )

    points = sample_within_envelope(envelope, 64, seed=5)

    assert len(points) > 0
    assert bool(envelope.contains(points).all()) is True


def test_described_envelope_reports_engineering_units():
    campaign = Campaign(
        "described",
        [Variable("soak_temp", 320.0, 360.0, "C")],
        [Objective("form_error", "le", threshold=0.5)],
    )
    envelope = build_coverage_envelope(np.asarray([[0.125], [0.5], [0.75]]))

    described = describe_coverage_envelope(envelope, campaign)

    assert described["observation_count"] == 3
    assert described["variables"][0]["unit"] == "C"
    assert described["variables"][0]["observed_minimum"] == pytest.approx(325.0)
    assert described["variables"][0]["observed_maximum"] == pytest.approx(350.0)
    assert described["variables"][0]["lower"] == pytest.approx(322.5)
    assert described["variables"][0]["upper"] == pytest.approx(352.5)


def test_recommendation_from_collinear_runs_stays_on_the_observed_direction():
    optimizer = build_optimizer(
        collinear_campaign(), collinear_observations(), seed=11
    )

    recommended = optimizer.suggest(top_k=1, n_random=256, n_samples=64)[0]

    x = recommended.recommended_params["x"]
    y = recommended.recommended_params["y"]
    # The surrogate's optimum sits at 0.9, outside everything production covered.
    assert 0.16 <= x <= 0.64
    assert 0.16 <= y <= 0.64
    assert abs(x - y) <= 0.05


def test_coverage_gate_rejects_a_candidate_pool_outside_the_observed_region():
    optimizer = build_optimizer(
        collinear_campaign(), collinear_observations(), seed=11
    )

    with pytest.raises(ValueError, match="observed coverage envelope"):
        optimizer.suggest(
            candidate_params=[{"x": 0.9, "y": 0.1}],
            n_random=1,
            n_samples=64,
            top_k=1,
        )


def test_disabled_coverage_restores_whole_campaign_candidates():
    optimizer = build_optimizer(
        collinear_campaign(), collinear_observations(), seed=11
    )

    recommended = optimizer.suggest(
        candidate_params=[{"x": 0.9, "y": 0.1}],
        n_random=1,
        n_samples=64,
        top_k=1,
        enforce_coverage=False,
    )[0]

    assert recommended.recommended_params["x"] == pytest.approx(0.9)


def test_hypothesis_validation_may_leave_the_observed_coverage_envelope():
    optimizer = build_optimizer(
        collinear_campaign(), collinear_observations(), seed=11
    )

    recommended = optimizer.suggest(
        candidate_params=[{"x": 0.9, "y": 0.1}],
        n_random=1,
        n_samples=64,
        top_k=1,
        decision_intent="validate-hypothesis",
        hypothesis_variables=["x"],
    )[0]

    assert recommended.recommended_params["x"] == pytest.approx(0.9)
    assert recommended.recommended_params["y"] == pytest.approx(0.1)
