"""Production Bayesian optimizer: multi-output GP + batch qLogNEHVI."""
from __future__ import annotations

import math
from typing import Mapping, Sequence

import numpy as np
from scipy.stats import norm

from .campaign import Campaign, Objective
from .loop import ObjectivePrediction, Suggestion
from .optical_molding import expand_inputs


MODEL_VERSION = "botorch-qlogbo-v2"


class BotorchOptimizer:
    def __init__(self, campaign: Campaign, *, process_profile: str = "generic", seed: int = 0):
        self.campaign = campaign
        self.process_profile = process_profile
        self.seed = seed
        self.x: list[np.ndarray] = []
        self.y: list[list[float]] = []
        self.constraint_y: list[list[float]] = []
        self.process_features: list[dict[str, float]] = []

    def observe(
        self,
        params: Mapping[str, float],
        outcomes: Mapping[str, float],
        *,
        constraint_outcomes: Mapping[str, float] | None = None,
        process_features: Mapping[str, float] | None = None,
    ) -> float:
        self.x.append(self.campaign.to_unit(params))
        self.campaign.validate_outcomes(outcomes)
        resolved_constraints = dict(constraint_outcomes or {})
        self.campaign.validate_constraint_outcomes(resolved_constraints)
        resolved_features = {
            str(name): float(value)
            for name, value in (process_features or {}).items()
            if str(name) and math.isfinite(float(value))
        }
        self.y.append([float(outcomes[o.name]) for o in self.campaign.objectives])
        self.constraint_y.append(
            [
                float(resolved_constraints[constraint.name])
                for constraint in self.campaign.outcome_constraints
            ]
        )
        self.process_features.append(resolved_features)
        return self.campaign.distance_to_spec(outcomes)

    @staticmethod
    def _utility(samples, objectives: list[Objective]):
        import torch

        columns = []
        for index, objective in enumerate(objectives):
            value = samples[..., index]
            if objective.kind == "le":
                threshold = float(objective.threshold)
                badness = 1.0 + (value - threshold) / max(abs(threshold), 1.0)
            elif objective.kind == "ge":
                threshold = float(objective.threshold)
                badness = 1.0 + (threshold - value) / max(abs(threshold), 1.0)
            elif objective.kind == "target":
                badness = torch.abs(value - float(objective.target)) / float(objective.tol)
            else:
                midpoint = (float(objective.lower) + float(objective.upper)) / 2.0
                half_width = (float(objective.upper) - float(objective.lower)) / 2.0
                badness = torch.abs(value - midpoint) / half_width
            columns.append(-badness * float(objective.weight))
        return torch.stack(columns, dim=-1)

    def _common_process_feature_names(self) -> list[str]:
        if not self.process_features or any(
            not values for values in self.process_features
        ):
            return []
        common = set(self.process_features[0])
        for values in self.process_features[1:]:
            common.intersection_update(values)
        return sorted(common)

    def _candidate_units(
        self,
        candidate_params: Sequence[Mapping[str, float]] | None,
        n_random: int,
    ) -> np.ndarray:
        from scipy.stats import qmc

        if candidate_params is not None:
            points = np.vstack([self.campaign.to_unit(value) for value in candidate_params])
        else:
            sampler = qmc.Sobol(self.campaign.dim, scramble=True, seed=self.seed)
            points = sampler.random_base2(math.ceil(math.log2(max(n_random, 2))))[:n_random]
            points = np.asarray(
                [point for point in points if self.campaign.is_feasible_unit(point)]
            )
        points = np.unique(points, axis=0)
        if self.x:
            observed = np.vstack(self.x)
            points = points[
                np.min(np.linalg.norm(points[:, None] - observed[None, :], axis=2), axis=1)
                > 1e-7
            ]
        return points

    def suggest(
        self,
        *,
        top_k: int = 1,
        candidate_params: Sequence[Mapping[str, float]] | None = None,
        n_random: int = 4096,
        n_samples: int = 256,
        pending_params: Sequence[Mapping[str, float]] | None = None,
        decision_intent: str = "reach-specification",
        hypothesis_variables: Sequence[str] | None = None,
        **_: object,
    ) -> list[Suggestion]:
        if len(self.x) < 3:
            raise ValueError("qLogNEHVI requires at least three observations")
        if top_k < 1 or top_k > 20:
            raise ValueError("top_k must be between 1 and 20")
        if decision_intent not in {"reach-specification", "validate-hypothesis"}:
            raise ValueError("decision_intent must be reach-specification or validate-hypothesis")
        candidates = self._candidate_units(candidate_params, n_random)
        pending_units = (
            np.vstack([self.campaign.to_unit(value) for value in pending_params])
            if pending_params
            else np.empty((0, self.campaign.dim), dtype=float)
        )
        if pending_units.size:
            candidates = candidates[
                np.min(
                    np.linalg.norm(
                        candidates[:, None, :] - pending_units[None, :, :],
                        axis=2,
                    ),
                    axis=1,
                )
                > 1e-7
            ]
        if len(candidates) < top_k:
            raise ValueError("fewer feasible unobserved candidates than top_k")

        import torch
        from botorch import fit_gpytorch_mll
        from botorch.acquisition.multi_objective.logei import (
            qLogNoisyExpectedHypervolumeImprovement,
        )
        from botorch.acquisition.logei import qLogNoisyExpectedImprovement
        from botorch.acquisition.objective import GenericMCObjective
        from botorch.acquisition.multi_objective.objective import (
            GenericMCMultiOutputObjective,
        )
        from botorch.models import SingleTaskGP
        from botorch.models.transforms.outcome import Standardize
        from botorch.optim import optimize_acqf_discrete
        from botorch.sampling.normal import SobolQMCNormalSampler
        from gpytorch.mlls import ExactMarginalLogLikelihood

        torch.manual_seed(self.seed)
        dtype = torch.double
        names = [value.name for value in self.campaign.variables]
        train_unit = np.vstack(self.x)
        train_unit_tensor = torch.tensor(train_unit, dtype=dtype)
        candidate_unit_tensor = torch.tensor(candidates, dtype=dtype)
        pending_unit_tensor = (
            torch.tensor(pending_units, dtype=dtype)
            if pending_units.size
            else None
        )
        train_x_np = expand_inputs(train_unit, names, self.process_profile)
        choices_np = expand_inputs(candidates, names, self.process_profile)
        pending_np = (
            expand_inputs(pending_units, names, self.process_profile)
            if pending_units.size
            else None
        )
        process_feature_names = self._common_process_feature_names()
        if process_feature_names:
            process_train_np = np.asarray(
                [
                    [values[name] for name in process_feature_names]
                    for values in self.process_features
                ],
                dtype=float,
            )
            process_train = torch.tensor(process_train_np, dtype=dtype)
            process_model = SingleTaskGP(
                train_unit_tensor,
                process_train,
                outcome_transform=Standardize(process_train.shape[-1]),
            )
            process_mll = ExactMarginalLogLikelihood(
                process_model.likelihood, process_model
            )
            fit_gpytorch_mll(process_mll)
            with torch.no_grad():
                predicted_process = (
                    process_model.posterior(candidate_unit_tensor)
                    .mean.cpu()
                    .numpy()
                )
                predicted_pending_process = (
                    process_model.posterior(pending_unit_tensor)
                    .mean.cpu()
                    .numpy()
                    if pending_unit_tensor is not None
                    else None
                )
            process_minimum = process_train_np.min(axis=0)
            process_scale = process_train_np.max(axis=0) - process_minimum
            process_scale = np.where(process_scale > 1e-9, process_scale, 1.0)
            train_x_np = np.column_stack(
                [train_x_np, (process_train_np - process_minimum) / process_scale]
            )
            choices_np = np.column_stack(
                [choices_np, (predicted_process - process_minimum) / process_scale]
            )
            if pending_np is not None and predicted_pending_process is not None:
                pending_np = np.column_stack(
                    [
                        pending_np,
                        (predicted_pending_process - process_minimum)
                        / process_scale,
                    ]
                )
        train_x = torch.tensor(train_x_np, dtype=dtype)
        train_y_np = np.column_stack(
            [np.asarray(self.y), np.asarray(self.constraint_y)]
        )
        train_y = torch.tensor(train_y_np, dtype=dtype)
        choices = torch.tensor(choices_np, dtype=dtype)
        pending_x = (
            torch.tensor(pending_np, dtype=dtype)
            if pending_np is not None
            else None
        )

        model = SingleTaskGP(train_x, train_y, outcome_transform=Standardize(train_y.shape[-1]))
        mll = ExactMarginalLogLikelihood(model.likelihood, model)
        fit_gpytorch_mll(mll)
        objective_count = len(self.campaign.objectives)
        posterior_constraints = []
        for index, constraint_spec in enumerate(
            self.campaign.outcome_constraints
        ):
            output_index = objective_count + index
            if constraint_spec.operator == "<=":
                posterior_constraints.append(
                    lambda samples, output_index=output_index, limit=float(
                        constraint_spec.limit
                    ): samples[..., output_index] - limit
                )
            else:
                posterior_constraints.append(
                    lambda samples, output_index=output_index, limit=float(
                        constraint_spec.limit
                    ): limit - samples[..., output_index]
                )

        if self.campaign.outcome_constraints:
            with torch.no_grad():
                safety_posterior = model.posterior(choices)
                safety_means = safety_posterior.mean.cpu().numpy()
                safety_deviations = (
                    safety_posterior.variance.clamp_min(0).sqrt().cpu().numpy()
                )
            safe = np.ones(len(candidates), dtype=bool)
            for index, constraint_spec in enumerate(
                self.campaign.outcome_constraints
            ):
                if not constraint_spec.safety_critical:
                    continue
                output_index = objective_count + index
                sigma = np.maximum(safety_deviations[:, output_index], 1e-12)
                if constraint_spec.operator == "<=":
                    probability = norm.cdf(
                        (constraint_spec.limit - safety_means[:, output_index])
                        / sigma
                    )
                else:
                    probability = 1.0 - norm.cdf(
                        (constraint_spec.limit - safety_means[:, output_index])
                        / sigma
                    )
                safe &= probability >= constraint_spec.minimum_probability
            if safe.sum() < top_k:
                raise ValueError(
                    "fewer candidates than top_k satisfy safety outcome "
                    "probability thresholds"
                )
            candidates = candidates[safe]
            choices = choices[safe]
        sampler = SobolQMCNormalSampler(
            sample_shape=torch.Size([n_samples]), seed=self.seed
        )
        validation_scores = None
        selected_indices = None
        if decision_intent == "validate-hypothesis":
            requested = [str(value) for value in (hypothesis_variables or [])]
            variable_indexes = [names.index(value) for value in requested if value in names]
            if not variable_indexes:
                raise ValueError(
                    "hypothesis validation requires at least one controllable hypothesis variable"
                )
            with torch.no_grad():
                validation_posterior = model.posterior(choices)
                objective_uncertainty = (
                    validation_posterior.variance[:, : len(self.campaign.objectives)]
                    .clamp_min(0)
                    .sqrt()
                    .mean(dim=1)
                    .cpu()
                    .numpy()
                )
            projected_candidates = candidates[:, variable_indexes]
            projected_observations = train_unit[:, variable_indexes]
            separation = np.min(
                np.linalg.norm(
                    projected_candidates[:, None, :] - projected_observations[None, :, :],
                    axis=2,
                ),
                axis=1,
            )
            validation_scores = objective_uncertainty * (1.0 + separation)
            selected_indices = self._select_diverse_validation_points(
                candidates, validation_scores, top_k
            )
            selected_x = choices[selected_indices]
        elif len(self.campaign.objectives) == 1:
            objective = GenericMCObjective(
                lambda samples, X=None: self._utility(
                    samples, self.campaign.objectives
                ).squeeze(-1)
            )
            acquisition = qLogNoisyExpectedImprovement(
                model=model,
                X_baseline=train_x,
                sampler=sampler,
                objective=objective,
                constraints=posterior_constraints or None,
                X_pending=pending_x,
                prune_baseline=True,
            )
        else:
            objective = GenericMCMultiOutputObjective(
                lambda samples, X=None: self._utility(
                    samples, self.campaign.objectives
                )
            )
            with torch.no_grad():
                observed_utility = objective(train_y)
            spread = (
                observed_utility.max(dim=0).values
                - observed_utility.min(dim=0).values
            )
            ref_point = (
                observed_utility.min(dim=0).values
                - torch.clamp(0.1 * spread, min=0.1)
            ).tolist()
            acquisition = qLogNoisyExpectedHypervolumeImprovement(
                model=model,
                ref_point=ref_point,
                X_baseline=train_x,
                sampler=sampler,
                objective=objective,
                constraints=posterior_constraints or None,
                X_pending=pending_x,
                prune_baseline=True,
            )
        if decision_intent == "reach-specification":
            selected_x, _ = optimize_acqf_discrete(
                acq_function=acquisition,
                q=top_k,
                choices=choices,
                unique=True,
            )
        selected_expanded = selected_x.detach().cpu().numpy()
        selected_unit = selected_expanded[:, : self.campaign.dim]
        with torch.no_grad():
            posterior = model.posterior(selected_x)
            means = posterior.mean.cpu().numpy()
            deviations = posterior.variance.clamp_min(0).sqrt().cpu().numpy()

        results: list[Suggestion] = []
        for row, (unit, mean, deviation) in enumerate(
            zip(selected_unit, means, deviations, strict=True)
        ):
            params = self.campaign.from_unit(unit)
            predictions = {
                objective_spec.name: ObjectivePrediction(
                    mean=float(mean[index]),
                    standard_deviation=float(deviation[index]),
                    lower_95=float(mean[index] - 1.96 * deviation[index]),
                    upper_95=float(mean[index] + 1.96 * deviation[index]),
                    unit=objective_spec.unit,
                )
                for index, objective_spec in enumerate(self.campaign.objectives)
            }
            constraint_predictions = {
                constraint_spec.name: ObjectivePrediction(
                    mean=float(mean[objective_count + index]),
                    standard_deviation=float(deviation[objective_count + index]),
                    lower_95=float(
                        mean[objective_count + index]
                        - 1.96 * deviation[objective_count + index]
                    ),
                    upper_95=float(
                        mean[objective_count + index]
                        + 1.96 * deviation[objective_count + index]
                    ),
                    unit=constraint_spec.unit,
                )
                for index, constraint_spec in enumerate(
                    self.campaign.outcome_constraints
                )
            }
            feasibility = 1.0
            for index, objective_spec in enumerate(self.campaign.objectives):
                sigma = max(float(deviation[index]), 1e-12)
                if objective_spec.kind == "le":
                    probability = norm.cdf((float(objective_spec.threshold) - mean[index]) / sigma)
                elif objective_spec.kind == "ge":
                    probability = 1.0 - norm.cdf((float(objective_spec.threshold) - mean[index]) / sigma)
                else:
                    lower = (
                        float(objective_spec.target) - float(objective_spec.tol)
                        if objective_spec.kind == "target"
                        else float(objective_spec.lower)
                    )
                    upper = (
                        float(objective_spec.target) + float(objective_spec.tol)
                        if objective_spec.kind == "target"
                        else float(objective_spec.upper)
                    )
                    probability = norm.cdf((upper - mean[index]) / sigma) - norm.cdf(
                        (lower - mean[index]) / sigma
                    )
                feasibility *= float(np.clip(probability, 0.0, 1.0))
            for index, constraint_spec in enumerate(
                self.campaign.outcome_constraints
            ):
                output_index = objective_count + index
                sigma = max(float(deviation[output_index]), 1e-12)
                if constraint_spec.operator == "<=":
                    probability = norm.cdf(
                        (float(constraint_spec.limit) - mean[output_index])
                        / sigma
                    )
                else:
                    probability = 1.0 - norm.cdf(
                        (float(constraint_spec.limit) - mean[output_index])
                        / sigma
                    )
                feasibility *= float(np.clip(probability, 0.0, 1.0))
            acq_value = (
                float(validation_scores[selected_indices[row]])
                if validation_scores is not None and selected_indices is not None
                else float(acquisition(selected_x[row : row + 1].unsqueeze(0)).item())
            )
            mean_outcomes = {
                objective_spec.name: float(mean[index])
                for index, objective_spec in enumerate(self.campaign.objectives)
            }
            results.append(
                Suggestion(
                    recommended_params=params,
                    objective_predictions=predictions,
                    constraint_predictions=constraint_predictions,
                    predicted_distance_to_spec=self.campaign.distance_to_spec(mean_outcomes),
                    feasibility_probability=feasibility,
                    acquisition_value=acq_value,
                    cold_start=False,
                    model_version=MODEL_VERSION,
                    rationale=(
                        "Hypothesis-validation design selected this safe point because it "
                        "maximizes outcome uncertainty while separating the hypothesis variables "
                        "from prior observations."
                        if validation_scores is not None
                        else "Two-stage trajectory surrogate and qLogNEHVI selected "
                        "this recipe for constrained Pareto-front improvement "
                        "under GP uncertainty."
                        if process_feature_names
                        else
                        "qLogNEHVI selected this recipe for constrained "
                        "Pareto-front improvement under GP uncertainty."
                    ),
                )
            )
        return results

    def _select_diverse_validation_points(
        self,
        candidates: np.ndarray,
        scores: np.ndarray,
        top_k: int,
    ) -> list[int]:
        order = np.argsort(-scores, kind="stable")
        selected: list[int] = []
        minimum_separation = 0.02 * math.sqrt(self.campaign.dim)
        for index in order:
            if selected and min(
                np.linalg.norm(candidates[index] - candidates[other])
                for other in selected
            ) < minimum_separation:
                continue
            selected.append(int(index))
            if len(selected) == top_k:
                return selected
        return [int(index) for index in order[:top_k]]
