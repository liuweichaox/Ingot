import pytest

from ingot_optimizer.molding_sim import REGISTERS, SimulatedFx3u


def recipe():
    return {
        "soak_temp": 345.0,
        "press_force": 510.0,
        "press_speed": 5.0,
        "anneal_rate": 3.0,
    }


def test_simulator_emits_the_declared_register_contract():
    result = SimulatedFx3u(seed=3).run_cycle(recipe())

    assert result.cycle_id == 1
    assert len(result.samples) == 16
    assert set(result.samples[0]) == {
        address for address, _, _ in REGISTERS.values()
    }
    assert set(result.outcomes) == {"surface_form_error", "defect_rate"}


def test_simulator_rejects_recipe_outside_campaign_bounds():
    invalid = recipe()
    invalid["soak_temp"] = 500.0
    with pytest.raises(ValueError, match="soak_temp"):
        SimulatedFx3u().run_cycle(invalid)
