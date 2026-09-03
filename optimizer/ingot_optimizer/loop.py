"""Sequential experiment recommendation with independent GP surrogates."""
from __future__ import annotations

from dataclasses import asdict, dataclass
from typing import Mapping, Sequence

import numpy as np

from .campaign import Campaign, Objective
from .coverage import (
    MINIMUM_COVERAGE_OBSERVATIONS,
    CoverageEnvelope,
    build_coverage_envelope,
)
from .gp import GaussianProcess


MODEL_VERSION = "numpy-gp-mcei-v1"


@dataclass(frozen=True)
class ObjectivePrediction:
    """Summarizes one objective prediction and its uncertainty."""

    mean: float
    standard_deviation: float
    lower_95: float
    upper_95: float
    unit: str


@dataclass(frozen=True)
class Suggestion:
    """Represents one feasible recommendation and its supporting predictions."""

    recommended_params: dict[str, float]
    objective_predictions: dict[str, ObjectivePrediction]
    constraint_predictions: dict[str, ObjectivePrediction]
    predicted_distance_to_spec: float | None
    feasibility_probability: float | None
    acquisition_value: float | None
    cold_start: bool
    model_version: str
    rationale: str

    def to_dict(self) -> dict:
        return asdict(self)


class SequentialOptimizer:
    """Runs the dependency-light optimizer used before three observations exist."""

    def __init__(
        self,
        campaign: Campaign,
        prior_means: Mapping[str, object] | None = None,
        seed: int = 0,
    ):
        self.campaign = campaign
        self.prior_means = dict(prior_means or {})
        unknown_priors = set(self.prior_means).difference(campaign.objective_names)
        if unknown_priors:
            raise ValueError(f"priors reference unknown objectives: {sorted(unknown_priors)}")
        self.rng = np.random.default_rng(seed)
        self.X = np.empty((0, campaign.dim), dtype=float)
        self.outcomes: dict[str, list[float]] = {
            objective.name: [] for objective in campaign.objectives
        }
        self.constraint_outcomes: dict[str, list[float]] = {
            constraint.name: [] for constraint in campaign.outcome_constraints
        }
        self.distances: list[float] = []
        self.coverage_envelope: CoverageEnvelope | None = None

    def observe(
        self,
        params: Mapping[str, float],
        outcomes: Mapping[str, float],
        *,
        constraint_outcomes: Mapping[str, float] | None = None,
        process_features: Mapping[str, float] | None = None,
    ) -> float:
        unit_point = self.campaign.to_unit(
            params, enforce_candidate_constraints=False
        )
        self.campaign.validate_outcomes(outcomes)
        resolved_constraints = dict(constraint_outcomes or {})
        self.campaign.validate_constraint_outcomes(resolved_constraints)
        self.X = np.vstack([self.X, unit_point])
        for objective in self.campaign.objectives:
            self.outcomes[objective.name].append(float(outcomes[objective.name]))
        for constraint in self.campaign.outcome_constraints:
            self.constraint_outcomes[constraint.name].append(
                float(resolved_constraints[constraint.name])
            )
        distance = self.campaign.distance_to_spec(outcomes)
        self.distances.append(distance)
        return distance


    def _fit(self, n_restarts: int) -> dict[str, GaussianProcess]:
        models: dict[str, GaussianProcess] = {}
        for objective in self.campaign.objectives:
            model = GaussianProcess(
                prior_mean=self.prior_means.get(objective.name),
                n_restarts=n_restarts,
                seed=int(self.rng.integers(1_000_000)),
            )
            model.fit(self.X, np.asarray(self.outcomes[objective.name], dtype=float))
            models[objective.name] = model
        return models

    @staticmethod
    def _badness_samples(objective: Objective, samples: np.ndarray) -> np.ndarray:
        samples = objective.clip(samples)
        if objective.kind == "le":
            threshold = float(objective.threshold)
            return 1.0 + (samples - threshold) / max(abs(threshold), 1.0)
        if objective.kind == "ge":
            threshold = float(objective.threshold)
            return 1.0 + (threshold - samples) / max(abs(threshold), 1.0)
        if objective.kind == "target":
            return np.abs(samples - float(objective.target)) / float(objective.tol)
        midpoint = (float(objective.lower) + float(objective.upper)) / 2.0
        half_width = (float(objective.upper) - float(objective.lower)) / 2.0
        return np.abs(samples - midpoint) / half_width

    def _posterior_samples(
        self,
        candidates: np.ndarray,
        models: Mapping[str, GaussianProcess],
        standard_normals: np.ndarray,
    ) -> tuple[np.ndarray, dict[str, tuple[np.ndarray, np.ndarray]]]:
        sample_count = standard_normals.shape[1]
        badness = np.empty(
            (len(self.campaign.objectives), sample_count, len(candidates)),
            dtype=float,
        )
        predictions: dict[str, tuple[np.ndarray, np.ndarray]] = {}
        for index, objective in enumerate(self.campaign.objectives):
            mean, standard_deviation = models[objective.name].predict(candidates)
            predictions[objective.name] = (mean, standard_deviation)
            raw_samples = (
                mean[None, :]
                + standard_deviation[None, :]
                * standard_normals[index, :, None]
            )
            badness[index] = self._badness_samples(objective, raw_samples)
        return badness.max(axis=0) - 1.0, predictions

    def _candidate_units(
        self,
        candidate_params: Sequence[Mapping[str, float]] | None,
        n_random: int,
    ) -> np.ndarray:
        if candidate_params is not None:
            if not candidate_params:
                raise ValueError("candidate pool must not be empty")
            points = np.vstack(
                [self.campaign.to_unit(params) for params in candidate_params]
            )
            return np.unique(points, axis=0)

        accepted: list[np.ndarray] = []
        attempts = 0
        while sum(len(batch) for batch in accepted) < n_random and attempts < 50:
            batch = self.rng.uniform(0.0, 1.0, (max(n_random, 64), self.campaign.dim))
            feasible = np.array(
                [self.campaign.is_feasible_unit(point) for point in batch],
                dtype=bool,
            )
            if feasible.any():
                accepted.append(batch[feasible])
            attempts += 1
        if not accepted:
            raise ValueError("unable to sample a feasible candidate")
        return np.unique(np.vstack(accepted), axis=0)[:n_random]

    def _select_diverse(
        self, candidates: np.ndarray, scores: np.ndarray, top_k: int
    ) -> list[int]:
        order = np.argsort(-scores, kind="stable")
        selected: list[int] = []
        minimum_separation = 0.02 * np.sqrt(self.campaign.dim)
        for index in order:
            point = candidates[index]
            if selected and min(
                np.linalg.norm(candidates[other] - point) for other in selected
            ) < minimum_separation:
                continue
            selected.append(int(index))
            if len(selected) == top_k:
                return selected
        for index in order:
            if int(index) not in selected:
                selected.append(int(index))
                if len(selected) == top_k:
                    return selected
        return selected

    def _cold_start_indices(self, candidates: np.ndarray, top_k: int) -> list[int]:
        selected: list[int] = []
        anchors = self.X.copy()
        for _ in range(top_k):
            if anchors.size:
                distances = np.min(
                    np.linalg.norm(
                        candidates[:, None, :] - anchors[None, :, :], axis=2
                    ),
                    axis=1,
                )
                if selected:
                    distances[selected] = -np.inf
                index = int(np.argmax(distances))
            else:
                center_distance = np.linalg.norm(candidates - 0.5, axis=1)
                if selected:
                    center_distance[selected] = np.inf
                index = int(np.argmin(center_distance))
            selected.append(index)
            anchors = np.vstack([anchors, candidates[index]])
        return selected

    def suggest(
        self,
        *,
        top_k: int = 1,
        candidate_params: Sequence[Mapping[str, float]] | None = None,
        n_random: int = 4000,
        n_samples: int = 256,
        n_restarts: int = 3,
        pending_params: Sequence[Mapping[str, float]] | None = None,
        enforce_coverage: bool = True,
    ) -> list[Suggestion]:
        if top_k < 1 or top_k > 20:
            raise ValueError("top_k must be between 1 and 20")
        if n_random < top_k or n_random > 100_000:
            raise ValueError("n_random must be between top_k and 100000")
        if n_samples < 32 or n_samples > 10_000:
            raise ValueError("n_samples must be between 32 and 10000")
        candidates = self._candidate_units(candidate_params, n_random)
        pending_units = (
            np.vstack([self.campaign.to_unit(value) for value in pending_params])
            if pending_params
            else np.empty((0, self.campaign.dim), dtype=float)
        )
        if self.X.size:
            unobserved = np.min(
                np.linalg.norm(
                    candidates[:, None, :] - self.X[None, :, :],
                    axis=2,
                ),
                axis=1,
            ) >= 1e-8
            candidates = candidates[unobserved]
        if pending_units.size:
            unplanned = np.min(
                np.linalg.norm(
                    candidates[:, None, :] - pending_units[None, :, :],
                    axis=2,
                ),
                axis=1,
            ) >= 1e-8
            candidates = candidates[unplanned]
        if len(self.X) >= MINIMUM_COVERAGE_OBSERVATIONS:
            self.coverage_envelope = build_coverage_envelope(self.X)
            if enforce_coverage:
                candidates = candidates[self.coverage_envelope.contains(candidates)]
                if len(candidates) < top_k:
                    raise ValueError(
                        "fewer candidates inside the observed coverage envelope "
                        "than top_k; production runs have not varied enough of "
                        "the parameter space to support a recommendation"
                    )
        safety_constraints = [
            value
            for value in self.campaign.outcome_constraints
            if value.safety_critical
        ]
        if len(self.X) < 3 and safety_constraints:
            safe_indices = [
                index
                for index in range(len(self.X))
                if all(
                    constraint.is_satisfied(
                        self.constraint_outcomes[constraint.name][index]
                    )
                    for constraint in safety_constraints
                )
            ]
            if not safe_indices:
                raise ValueError(
                    "a verified safe baseline observation is required before "
                    "cold-start recommendations with safety outcome constraints"
                )
            safe_anchors = self.X[safe_indices]
            trust_radius = 0.2 * np.sqrt(self.campaign.dim)
            inside_trust_region = np.min(
                np.linalg.norm(
                    candidates[:, None, :] - safe_anchors[None, :, :],
                    axis=2,
                ),
                axis=1,
            ) <= trust_radius
            candidates = candidates[inside_trust_region]
        if len(candidates) < top_k:
            raise ValueError(
                "candidate pool contains fewer unobserved unique points than top_k"
            )

        if len(self.X) < 3:
            return [
                Suggestion(
                    recommended_params=self.campaign.from_unit(candidates[index]),
                    objective_predictions={},
                    constraint_predictions={},
                    predicted_distance_to_spec=None,
                    feasibility_probability=None,
                    acquisition_value=None,
                    cold_start=True,
                    model_version=MODEL_VERSION,
                    rationale=(
                        "Insufficient observations for a surrogate; selected a "
                        "local experiment around a verified safe baseline."
                        if safety_constraints
                        else
                        "Insufficient observations for a surrogate; selected a "
                        "feasible space-filling experiment."
                    ),
                )
                for index in self._cold_start_indices(candidates, top_k)
            ]

        models = self._fit(n_restarts)
        standard_normals = self.rng.standard_normal(
            (len(self.campaign.objectives), n_samples)
        )
        distance_samples, predictions = self._posterior_samples(
            candidates, models, standard_normals
        )
        current_best = min(self.distances)
        expected_improvement = np.maximum(
            current_best - distance_samples, 0.0
        ).mean(axis=0)
        feasibility = (distance_samples <= 0.0).mean(axis=0)
        selected = self._select_diverse(candidates, expected_improvement, top_k)

        results: list[Suggestion] = []
        for index in selected:
            objective_predictions: dict[str, ObjectivePrediction] = {}
            mean_outcomes: dict[str, float] = {}
            for objective in self.campaign.objectives:
                means, deviations = predictions[objective.name]
                mean, deviation, lower_95, upper_95 = objective.bounded_prediction(
                    float(means[index]), float(deviations[index])
                )
                mean_outcomes[objective.name] = mean
                objective_predictions[objective.name] = ObjectivePrediction(
                    mean=mean,
                    standard_deviation=deviation,
                    lower_95=lower_95,
                    upper_95=upper_95,
                    unit=objective.unit,
                )
            results.append(
                Suggestion(
                    recommended_params=self.campaign.from_unit(candidates[index]),
                    objective_predictions=objective_predictions,
                    constraint_predictions={},
                    predicted_distance_to_spec=self.campaign.distance_to_spec(
                        mean_outcomes
                    ),
                    feasibility_probability=float(feasibility[index]),
                    acquisition_value=float(expected_improvement[index]),
                    cold_start=False,
                    model_version=MODEL_VERSION,
                    rationale=(
                        "Ranked by Monte Carlo expected improvement while "
                        "preserving independent uncertainty for each objective."
                    ),
                )
            )
        return results
