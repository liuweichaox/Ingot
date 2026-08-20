"""Single production policy for selecting and hydrating optimizer engines."""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Mapping, Protocol, Sequence

from .botorch_engine import BotorchOptimizer
from .campaign import Campaign
from .feature_transforms import DerivedFeature
from .loop import SequentialOptimizer, Suggestion


@dataclass(frozen=True)
class OptimizerObservation:
    """Immutable observation used to hydrate either production optimizer engine."""

    params: Mapping[str, float]
    outcomes: Mapping[str, float]
    constraint_outcomes: Mapping[str, float] = field(default_factory=dict)
    process_features: Mapping[str, float] = field(default_factory=dict)


class OptimizerEngine(Protocol):
    """Common behavior exposed by every optimizer selected for production use."""

    def observe(
        self,
        params: Mapping[str, float],
        outcomes: Mapping[str, float],
        *,
        constraint_outcomes: Mapping[str, float] | None = None,
        process_features: Mapping[str, float] | None = None,
    ) -> float: ...

    def suggest(self, **kwargs: object) -> list[Suggestion]: ...


def build_optimizer(
    campaign: Campaign,
    observations: Sequence[OptimizerObservation],
    *,
    seed: int = 0,
    derived_features: Sequence[DerivedFeature] | None = None,
    prior_means: Mapping[str, object] | None = None,
) -> OptimizerEngine:
    """Apply the production engine switch and hydrate all visible observations."""
    optimizer: OptimizerEngine
    if len(observations) >= 3:
        optimizer = BotorchOptimizer(
            campaign,
            derived_features=derived_features,
            seed=seed,
        )
    else:
        optimizer = SequentialOptimizer(
            campaign,
            prior_means=prior_means,
            seed=seed,
        )

    for observation in observations:
        optimizer.observe(
            observation.params,
            observation.outcomes,
            constraint_outcomes=observation.constraint_outcomes,
            process_features=observation.process_features,
        )
    return optimizer
