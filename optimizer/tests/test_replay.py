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
    assert "does not prove online" in result["limitations"]


def test_history_pool_requires_aggregated_unique_recipes():
    history = [
        {"params": {"x": 0.2}, "outcomes": {"loss": 0.3}},
        {"params": {"x": 0.2}, "outcomes": {"loss": 0.2}},
    ]
    with pytest.raises(ValueError, match="aggregate replicates"):
        replay_history_pool(campaign(), history)


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
