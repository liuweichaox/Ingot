"""Stateless HTTP adapter for the Ingot optimization core.

The .NET platform remains the system of record.  Every request supplies the
campaign definition and immutable observations used for a recommendation, so
service restarts cannot lose business state.
"""
from __future__ import annotations

from typing import Literal

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, ConfigDict, Field

from ingot_optimizer import (
    Campaign,
    DerivedFeature,
    ForbiddenCombination,
    ForbiddenCombinationFactor,
    Objective,
    OutcomeConstraint,
    OptimizerObservation,
    ParameterConstraint,
    Variable,
    build_optimizer,
)
from ingot_optimizer.botorch_engine import MODEL_VERSION
from ingot_optimizer.coverage import describe_coverage_envelope
from ingot_optimizer.diagnosis import FeatureSpec, diagnose
from ingot_optimizer.feature_transforms import expand_inputs
from ingot_optimizer.replay import replay_history_pool
import numpy as np


app = FastAPI(title="Ingot Process Optimizer", version="0.5.0")


class StrictModel(BaseModel):
    model_config = ConfigDict(extra="forbid")


class VariableIn(StrictModel):
    name: str = Field(min_length=1, max_length=120)
    low: float
    high: float
    unit: str = Field(default="", max_length=40)


class ObjectiveIn(StrictModel):
    name: str = Field(min_length=1, max_length=120)
    kind: Literal["le", "ge", "target", "range"]
    threshold: float | None = None
    target: float | None = None
    tol: float | None = None
    lower: float | None = None
    upper: float | None = None
    outcome_lower_bound: float | None = None
    outcome_upper_bound: float | None = None
    unit: str = Field(default="", max_length=40)
    weight: float = Field(default=1.0, gt=0)


class ConstraintIn(StrictModel):
    variable: str = Field(min_length=1, max_length=120)
    operator: Literal["<=", ">="]
    limit: float
    safety_critical: bool = False


class OutcomeConstraintIn(StrictModel):
    name: str = Field(min_length=1, max_length=120)
    operator: Literal["<=", ">="]
    limit: float
    unit: str = Field(default="", max_length=40)
    safety_critical: bool = True
    minimum_probability: float = Field(default=0.95, gt=0, le=1)


class DerivedFeatureIn(StrictModel):
    name: str = Field(min_length=1, max_length=120)
    operator: Literal[
        "identity",
        "absolute",
        "sum",
        "mean",
        "product",
        "difference",
        "absolute_difference",
        "ratio",
        "minimum",
        "maximum",
        "standard_deviation",
        "affine",
    ]
    inputs: list[str] = Field(min_length=1, max_length=100)
    normalization_offset: float = 0.0
    normalization_scale: float = Field(default=1.0, gt=0)
    epsilon: float = Field(default=1e-9, gt=0)
    intercept: float = 0.0
    coefficients: list[float] = Field(default_factory=list, max_length=100)


class ForbiddenCombinationFactorIn(StrictModel):
    variable: str = Field(min_length=1, max_length=120)
    minimum: float | None = None
    maximum: float | None = None


class ForbiddenCombinationIn(StrictModel):
    name: str = Field(min_length=1, max_length=240)
    factors: list[ForbiddenCombinationFactorIn] = Field(min_length=2, max_length=100)


class CampaignIn(StrictModel):
    name: str = Field(min_length=1, max_length=240)
    feature_set_id: str = Field(default="generic", min_length=1, max_length=120)
    feature_set_version: int = Field(default=1, ge=1)
    derived_features: list[DerivedFeatureIn] = Field(
        default_factory=list,
        max_length=100,
    )
    decision_intent: Literal["reach-specification"] = "reach-specification"
    variables: list[VariableIn] = Field(min_length=1, max_length=100)
    objectives: list[ObjectiveIn] = Field(min_length=1, max_length=50)
    constraints: list[ConstraintIn] = Field(default_factory=list, max_length=100)
    outcome_constraints: list[OutcomeConstraintIn] = Field(
        default_factory=list, max_length=50
    )
    forbidden_combinations: list[ForbiddenCombinationIn] = Field(
        default_factory=list, max_length=100
    )
    context: dict[str, str] = Field(default_factory=dict)


class ObservationIn(StrictModel):
    params: dict[str, float]
    outcomes: dict[str, float]
    constraint_outcomes: dict[str, float] = Field(default_factory=dict)
    process_features: dict[str, float] = Field(default_factory=dict)


class SuggestionRequest(StrictModel):
    campaign: CampaignIn
    observations: list[ObservationIn] = Field(default_factory=list, max_length=10_000)
    pending_points: list[dict[str, float]] = Field(default_factory=list, max_length=1_000)
    candidate_pool: list[dict[str, float]] | None = Field(default=None, max_length=100_000)
    top_k: int = Field(default=1, ge=1, le=20)
    seed: int = Field(default=0, ge=0, le=2_147_483_647)
    n_random: int = Field(default=4000, ge=1, le=100_000)
    n_samples: int = Field(default=256, ge=32, le=10_000)


class ObjectivePredictionOut(StrictModel):
    mean: float
    standard_deviation: float
    lower_95: float
    upper_95: float
    unit: str


class SuggestionOut(StrictModel):
    recommended_params: dict[str, float]
    objective_predictions: dict[str, ObjectivePredictionOut]
    constraint_predictions: dict[str, ObjectivePredictionOut]
    predicted_distance_to_spec: float | None
    feasibility_probability: float | None
    acquisition_value: float | None
    cold_start: bool
    model_version: str
    rationale: str


class CoverageVariableOut(StrictModel):
    name: str = Field(min_length=1, max_length=120)
    unit: str = Field(default="", max_length=40)
    lower: float
    upper: float
    observed_minimum: float
    observed_maximum: float


class CoverageEnvelopeOut(StrictModel):
    observation_count: int = Field(ge=0)
    leverage_limit: float
    variables: list[CoverageVariableOut] = Field(min_length=1, max_length=100)


class SuggestionResponse(StrictModel):
    model_version: str
    observation_count: int = Field(ge=0)
    suggestions: list[SuggestionOut] = Field(min_length=1, max_length=20)
    coverage_envelope: CoverageEnvelopeOut | None = None
    feature_set_id: str = Field(min_length=1, max_length=120)
    feature_set_version: int = Field(ge=1)
    derived_feature_count: int = Field(ge=0, le=100)
    state_persisted: Literal[False]


class DiagnosticFeatureIn(StrictModel):
    data_source: str = Field(min_length=1, max_length=300)
    source_kind: Literal["control-parameter", "process-feature"]
    actionability: Literal["controllable", "observable"]


class DiagnosticObservationIn(StrictModel):
    run_key: str = Field(min_length=1, max_length=240)
    outcome: float
    weight: float = Field(default=1.0, gt=0, le=1)
    values: dict[str, float]
    context: dict[str, str] = Field(default_factory=dict)
    occurred_at: float = 0.0


class DiagnosisRequest(StrictModel):
    outcome_kind: Literal["binary", "continuous"] = "binary"
    features: list[DiagnosticFeatureIn] = Field(min_length=1, max_length=500)
    observations: list[DiagnosticObservationIn] = Field(min_length=4, max_length=10_000)
    seed: int = Field(default=0, ge=0, le=2_147_483_647)


class HistoricalReplayObservationIn(ObservationIn):
    run_id: str | None = Field(default=None, max_length=240)
    occurred_at: float = Field(allow_inf_nan=False)


class SoftConstraintIn(StrictModel):
    variable_code: str = Field(min_length=1, max_length=120)
    minimum: float | None = None
    maximum: float | None = None


class HistoricalReplayRequest(StrictModel):
    campaign: CampaignIn
    history: list[HistoricalReplayObservationIn] = Field(min_length=3, max_length=10_000)
    budget: int | None = Field(default=None, ge=1, le=10_000)
    n_seeds: int = Field(default=30, ge=1, le=100)
    initial_observation_count: int = Field(default=3, ge=0, le=10_000)
    soft_constraints: list[SoftConstraintIn] = Field(default_factory=list, max_length=500)


def _campaign_from_input(spec: CampaignIn) -> Campaign:
    return Campaign(
        name=spec.name,
        variables=[Variable(**value.model_dump()) for value in spec.variables],
        objectives=[Objective(**value.model_dump()) for value in spec.objectives],
        constraints=[
            ParameterConstraint(**value.model_dump()) for value in spec.constraints
        ],
        context={
            **spec.context,
            "feature_set_id": spec.feature_set_id,
            "feature_set_version": str(spec.feature_set_version),
        },
        outcome_constraints=[
            OutcomeConstraint(**value.model_dump())
            for value in spec.outcome_constraints
        ],
        forbidden_combinations=[
            ForbiddenCombination(
                name=value.name,
                factors=tuple(
                    ForbiddenCombinationFactor(**factor.model_dump())
                    for factor in value.factors
                ),
            )
            for value in spec.forbidden_combinations
        ],
    )


def _build_derived_features_and_validate(
    campaign: Campaign, spec: CampaignIn
) -> list[DerivedFeature]:
    """Build DerivedFeature list from campaign input and validate bounds."""
    derived_features = [
        DerivedFeature(
            name=value.name,
            operator=value.operator,
            inputs=tuple(value.inputs),
            normalization_offset=value.normalization_offset,
            normalization_scale=value.normalization_scale,
            epsilon=value.epsilon,
            intercept=value.intercept,
            coefficients=tuple(value.coefficients),
        )
        for value in spec.derived_features
    ]
    expand_inputs(
        np.full((1, campaign.dim), 0.5),
        [value.name for value in campaign.variables],
        [value.low for value in campaign.variables],
        [value.high for value in campaign.variables],
        derived_features,
    )
    return derived_features

@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok", "model_version": MODEL_VERSION}


@app.get("/ready")
def ready() -> dict[str, str]:
    try:
        import botorch
        import gpytorch
        import torch
    except ImportError as error:
        raise HTTPException(
            status_code=503, detail=f"numerical runtime unavailable: {error}"
        ) from error
    return {
        "status": "ready",
        "model_version": MODEL_VERSION,
        "botorch": botorch.__version__,
        "gpytorch": gpytorch.__version__,
        "torch": torch.__version__,
    }


@app.post("/v1/suggestions", response_model=SuggestionResponse)
def create_suggestions(request: SuggestionRequest) -> SuggestionResponse:
    try:
        campaign = _campaign_from_input(request.campaign)
        derived_features = _build_derived_features_and_validate(
            campaign, request.campaign
        )
        optimizer = build_optimizer(
            campaign,
            [
                OptimizerObservation(
                    params=observation.params,
                    outcomes=observation.outcomes,
                    constraint_outcomes=observation.constraint_outcomes,
                    process_features=observation.process_features,
                )
                for observation in request.observations
            ],
            derived_features=derived_features,
            seed=request.seed,
        )
        suggestions = optimizer.suggest(
            top_k=request.top_k,
            candidate_params=request.candidate_pool,
            n_random=request.n_random,
            n_samples=request.n_samples,
            pending_params=request.pending_points,
            decision_intent=request.campaign.decision_intent,
        )
    except ValueError as error:
        raise HTTPException(status_code=422, detail=str(error)) from error
    model_version = suggestions[0].model_version
    envelope = optimizer.coverage_envelope
    return SuggestionResponse(
        model_version=model_version,
        observation_count=len(request.observations),
        suggestions=[SuggestionOut.model_validate(suggestion.to_dict()) for suggestion in suggestions],
        coverage_envelope=(
            CoverageEnvelopeOut.model_validate(
                describe_coverage_envelope(envelope, campaign)
            )
            if envelope is not None
            else None
        ),
        feature_set_id=request.campaign.feature_set_id,
        feature_set_version=request.campaign.feature_set_version,
        derived_feature_count=len(request.campaign.derived_features),
        state_persisted=False,
    )


@app.post("/v1/diagnosis")
def create_diagnosis(request: DiagnosisRequest) -> dict:
    feature_names = [feature.data_source for feature in request.features]
    if len(set(feature_names)) != len(feature_names):
        raise HTTPException(status_code=422, detail="diagnostic feature keys must be unique")
    rows = []
    for observation in request.observations:
        unknown = set(observation.values).difference(feature_names)
        if unknown:
            raise HTTPException(
                status_code=422,
                detail=f"observation contains unknown diagnostic features: {sorted(unknown)}",
            )
        rows.append(
            [observation.values.get(name, float("nan")) for name in feature_names]
        )
    target = np.asarray(
        [observation.outcome for observation in request.observations], dtype=float
    )
    if request.outcome_kind == "binary" and not set(np.unique(target)).issubset(
        {0.0, 1.0}
    ):
        raise HTTPException(status_code=422, detail="binary outcomes must be 0 or 1")
    try:
        return diagnose(
            [
                FeatureSpec(
                    feature.data_source,
                    feature.source_kind,
                    feature.actionability,
                )
                for feature in request.features
            ],
            np.asarray(rows, dtype=float),
            target,
            np.asarray(
                [observation.weight for observation in request.observations],
                dtype=float,
            ),
            [observation.context for observation in request.observations],
            np.asarray(
                [observation.occurred_at for observation in request.observations],
                dtype=float,
            ),
            request.outcome_kind,
            request.seed,
        )
    except ValueError as error:
        raise HTTPException(status_code=422, detail=str(error)) from error


@app.post("/v1/historical-replay")
def create_historical_replay(request: HistoricalReplayRequest) -> dict:
    try:
        campaign = _campaign_from_input(request.campaign)
        derived_features = _build_derived_features_and_validate(
            campaign, request.campaign
        )
        history = [value.model_dump(exclude_none=True) for value in request.history]
        result = replay_history_pool(
            campaign,
            history,
            budget=request.budget,
            n_seeds=request.n_seeds,
            initial_observation_count=request.initial_observation_count,
            derived_features=derived_features,
            soft_constraints=[value.model_dump() for value in request.soft_constraints],
        )
    except ValueError as error:
        raise HTTPException(status_code=422, detail=str(error)) from error
    return {
        **result,
        "feature_set_id": request.campaign.feature_set_id,
        "feature_set_version": request.campaign.feature_set_version,
        "derived_feature_count": len(request.campaign.derived_features),
        "state_persisted": False,
    }
