import pytest

from ingot_optimizer import Campaign, Objective, Variable
from ingot_optimizer.replay import replay_history_pool, replay_synthetic


def campaign():
    return Campaign(
        "one-dimensional",
        [Variable("x", 0.0, 1.0)],
        [Objective("loss", "le", threshold=0.05)],
    )


def test_history_pool_replay_only_selects_real_rows_without_reuse():
    history = [
        {"params": {"x": 0.0}, "outcomes": {"loss": 0.64}},
        {"params": {"x": 1.0}, "outcomes": {"loss": 0.04}},
        {"params": {"x": 0.5}, "outcomes": {"loss": 0.09}},
        {"params": {"x": 0.8}, "outcomes": {"loss": 0.0}},
    ]

    result = replay_history_pool(campaign(), history, n_seeds=3)

    assert result["evidence_kind"] == "historical-pool-ranking"
    assert result["original_order_trials"] == 2
    for selected in result["selected_history_indices"]:
        assert len(selected) == len(set(selected))
        assert set(selected).issubset(range(len(history)))
    for selected in result["random_selected_history_indices"]:
        assert len(selected) == len(set(selected))
        assert set(selected).issubset(range(len(history)))
    assert set(result["safety_violations"]) == {
        "original_order", "optimizer", "random", "response_surface"
    }
    assert result["response_surface"]["applicable"] is False
    assert result["baseline_methods"] == [
        "historical-engineer-order",
        "seeded-random-order",
        "quadratic-response-surface",
    ]
    assert "does not prove online" in result["limitations"]
    assert result["engine_policy"].startswith("production-equivalent")
    for trace in result["step_traces"]:
        for step in trace:
            assert step["revealed_history_index"] not in step["visible_observation_indices_before"]
            if step["kind"] == "optimizer-selection":
                assert step["nearest_historical_candidate_distance"] == 0.0


def test_history_pool_requires_aggregated_unique_parameter_settings():
    history = [
        {"params": {"x": 0.2}, "outcomes": {"loss": 0.3}},
        {"params": {"x": 0.2}, "outcomes": {"loss": 0.2}},
    ]
    with pytest.raises(ValueError, match="aggregate replicates"):
        replay_history_pool(campaign(), history)


def test_history_pool_switches_to_botorch_production_engine_after_three_observations():
    impossible = Campaign(
        "production-switch",
        [Variable("x", 0.0, 1.0)],
        [Objective("loss", "le", threshold=-1.0)],
    )
    history = [
        {
            "params": {"x": value},
            "outcomes": {"loss": (value - 0.7) ** 2},
            "run_id": f"run-{index}",
        }
        for index, value in enumerate([0.0, 0.2, 0.4, 0.6, 0.8])
    ]

    result = replay_history_pool(
        impossible,
        history,
        n_seeds=1,
        initial_observation_count=3,
    )

    model_versions = [
        step["model_version"]
        for step in result["step_traces"][0]
        if step["kind"] == "optimizer-selection"
    ]
    assert model_versions
    assert all(version.startswith("botorch-") for version in model_versions)
    assert result["calibration"][0]["prediction_interval_checks"] > 0
    assert result["response_surface"]["runs"] == 1
    assert len(result["response_surface_selected_history_indices"]) == 1


def test_random_and_response_surface_baselines_share_preregistered_initial_rows():
    history = [
        {"params": {"x": value}, "outcomes": {"loss": (value - 0.85) ** 2}}
        for value in [0.0, 0.2, 0.4, 0.6, 0.8, 1.0]
    ]

    result = replay_history_pool(
        campaign(), history, n_seeds=3, initial_observation_count=2
    )

    for key in [
        "selected_history_indices",
        "random_selected_history_indices",
        "response_surface_selected_history_indices",
    ]:
        assert all(selected[:2] == [0, 1] for selected in result[key])


def test_synthetic_replay_accepts_prior_mapping_without_constructor_error():
    result = replay_synthetic(
        campaign(),
        lambda params: {"loss": (params["x"] - 0.8) ** 2},
        budget=4,
        n_seeds=2,
        prior_means={"loss": lambda points: (points[:, 0] - 0.8) ** 2},
    )
    assert result["evidence_kind"] == "synthetic"
    assert result["optimizer"]["runs"] == 2
