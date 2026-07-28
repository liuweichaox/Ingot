import math

import numpy as np
import pytest

from ingot_optimizer import (
    Campaign,
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
