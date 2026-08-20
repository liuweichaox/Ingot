import math

import numpy as np
import pytest

from ingot_optimizer import (
    Campaign,
    ForbiddenCombination,
    ForbiddenCombinationFactor,
    Objective,
    ParameterConstraint,
    Variable,
)


def test_campaign_rejects_invalid_or_empty_definitions():
    with pytest.raises(ValueError, match="low < high"):
        Variable("temperature", 10, 10)
    with pytest.raises(ValueError, match="positive tolerance"):
        Objective("form", "target", target=0.0, tol=0.0)
    with pytest.raises(ValueError, match="at least one variable"):
        Campaign("empty", [], [Objective("form", "le", threshold=1.0)])
    with pytest.raises(ValueError, match="supplied together"):
        Objective("pass", "ge", threshold=1.0, outcome_lower_bound=0.0)


def test_campaign_enforces_bounds_constraints_and_exact_keys():
    campaign = Campaign(
        "lens",
        [Variable("temperature", 300.0, 400.0, "C")],
        [Objective("form", "le", threshold=0.5, unit="um")],
        [ParameterConstraint("temperature", "<=", 380.0, True)],
    )

    assert np.allclose(campaign.to_unit({"temperature": 350.0}), [0.5])
    with pytest.raises(ValueError, match="violates"):
        campaign.to_unit({"temperature": 390.0})
    with pytest.raises(ValueError, match="keys mismatch"):
        campaign.to_unit({"temperature": 350.0, "typo": 1.0})
    with pytest.raises(ValueError, match="finite"):
        campaign.distance_to_spec({"form": math.nan})


def test_campaign_rejects_matching_forbidden_combination():
    campaign = Campaign(
        name="safe-window",
        variables=[Variable("temperature", 100.0, 200.0), Variable("pressure", 1.0, 10.0)],
        objectives=[Objective("quality", "ge", threshold=0.9)],
        forbidden_combinations=[
            ForbiddenCombination(
                "hot-and-pressurized",
                (
                    ForbiddenCombinationFactor("temperature", minimum=180.0),
                    ForbiddenCombinationFactor("pressure", minimum=8.0),
                ),
            )
        ],
    )

    campaign.validate_params({"temperature": 175.0, "pressure": 9.0})
    with pytest.raises(ValueError, match="hot-and-pressurized"):
        campaign.validate_params({"temperature": 185.0, "pressure": 9.0})


def test_bounded_objective_rejects_impossible_observations_and_bounds_predictions():
    objective = Objective(
        "pass",
        "ge",
        threshold=1.0,
        outcome_lower_bound=0.0,
        outcome_upper_bound=1.0,
    )
    campaign = Campaign("quality", [Variable("x", 0.0, 1.0)], [objective])
    with pytest.raises(ValueError, match="outside"):
        campaign.validate_outcomes({"pass": 1.01})

    mean, deviation, lower_95, upper_95 = objective.bounded_prediction(1.15, 0.31)
    assert 0.0 <= mean <= 1.0
    assert 0.0 <= lower_95 <= upper_95 <= 1.0
    assert 0.0 <= deviation <= 0.5


@pytest.mark.parametrize(
    ("objective", "inside", "outside"),
    [
        (Objective("y", "le", threshold=2.0), 1.5, 2.5),
        (Objective("y", "ge", threshold=2.0), 2.5, 1.5),
        (Objective("y", "target", target=5.0, tol=0.5), 5.25, 6.0),
        (Objective("y", "range", lower=4.0, upper=6.0), 5.5, 7.0),
    ],
)
def test_objective_badness_has_consistent_spec_boundary(objective, inside, outside):
    assert objective.badness(inside) <= 1.0
    assert objective.badness(outside) > 1.0
