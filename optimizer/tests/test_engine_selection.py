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
