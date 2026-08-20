"""Stateless HTTP adapter for the Ingot optimization core.

The .NET platform remains the system of record.  Every request supplies the
campaign definition and immutable observations used for a recommendation, so
service restarts cannot lose business state.
"""
from __future__ import annotations

from itertools import combinations, product
import random
from typing import Literal

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, ConfigDict, Field

from ingot_optimizer import (
    Campaign,
    BotorchOptimizer,
    DerivedFeature,
    Objective,
    OutcomeConstraint,
    ParameterConstraint,
    SequentialOptimizer,
    Variable,
)
from ingot_optimizer.botorch_engine import MODEL_VERSION
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
    ]
    inputs: list[str] = Field(min_length=1, max_length=100)
    normalization_offset: float = 0.0
    normalization_scale: float = Field(default=1.0, gt=0)
    epsilon: float = Field(default=1e-9, gt=0)


class CampaignIn(StrictModel):
    name: str = Field(min_length=1, max_length=240)
    feature_set_id: str = Field(default="generic", min_length=1, max_length=120)
    feature_set_version: int = Field(default=1, ge=1)
    derived_features: list[DerivedFeatureIn] = Field(
        default_factory=list,
        max_length=100,
    )
    decision_intent: Literal["reach-specification", "validate-hypothesis"] = (
        "reach-specification"
    )
    hypothesis_variables: list[str] = Field(default_factory=list, max_length=100)
    variables: list[VariableIn] = Field(min_length=1, max_length=100)
    objectives: list[ObjectiveIn] = Field(min_length=1, max_length=50)
    constraints: list[ConstraintIn] = Field(default_factory=list, max_length=100)
    outcome_constraints: list[OutcomeConstraintIn] = Field(
        default_factory=list, max_length=50
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


class SuggestionResponse(StrictModel):
    model_version: str
    observation_count: int = Field(ge=0)
    suggestions: list[SuggestionOut] = Field(min_length=1, max_length=20)
    feature_set_id: str = Field(min_length=1, max_length=120)
    feature_set_version: int = Field(ge=1)
    derived_feature_count: int = Field(ge=0, le=100)
    state_persisted: Literal[False]


class DesignRequest(StrictModel):
    method: Literal[
        "full-factorial",
        "fractional-factorial",
        "response-surface",
        "latin-hypercube",
    ]
    variables: list[VariableIn] = Field(min_length=1, max_length=12)
    levels: int = Field(default=2, ge=2, le=5)
    replicates: int = Field(default=1, ge=1, le=5)
    block_count: int = Field(default=1, ge=1, le=5)
    sample_count: int = Field(default=0, ge=0, le=40)
    response_surface_family: Literal["central-composite", "box-behnken"] | None = None
    seed: int = Field(default=0, ge=0, le=2_147_483_647)


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
    occurred_at: float | None = None


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
    )


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


def _validate_design_variables(values: list[VariableIn]) -> None:
    names = [value.name for value in values]
    if len(set(names)) != len(names):
        raise ValueError("design variable names must be unique")
    if any(value.high <= value.low for value in values):
        raise ValueError("each design variable must have high > low")


def _decode_level(value: VariableIn, coded: float) -> float:
    return (value.low + value.high) / 2 + coded * (value.high - value.low) / 2


def _full_factorial(values: list[VariableIn], levels: int) -> list[dict[str, float]]:
    grids = [np.linspace(value.low, value.high, levels).tolist() for value in values]
    return [dict(zip((value.name for value in values), point, strict=True)) for point in product(*grids)]


def _fractional_factorial(values: list[VariableIn]) -> tuple[list[dict[str, float]], str, list[str]]:
    if len(values) < 3:
        return (
            _full_factorial(values, 2),
            "无部分因子缩减：两个因素以下使用完整二水平设计。",
            ["因素少于三个时，部分因子设计等同于完整二水平设计。"],
        )
    base = values[:-1]
    points: list[dict[str, float]] = []
    for signs in product((-1.0, 1.0), repeat=len(base)):
        last = float(np.prod(signs))
        coded = [*signs, last]
        points.append({value.name: _decode_level(value, sign) for value, sign in zip(values, coded, strict=True)})
    generator = " × ".join(value.name for value in values)
    resolution = len(values)
    return (
        points,
        f"I = {generator}（分辨率 {resolution}）",
        ["部分因子设计会混杂部分高阶交互；请在提交前确认别名结构与现场知识一致。"],
    )


def _central_composite(values: list[VariableIn]) -> list[dict[str, float]]:
    # Inscribed CCD: factorial points remain inside the approved bounds and axial points touch them.
    points: list[dict[str, float]] = []
    for signs in product((-0.5, 0.5), repeat=len(values)):
        points.append({value.name: _decode_level(value, sign) for value, sign in zip(values, signs, strict=True)})
    for index, value in enumerate(values):
        for sign in (-1.0, 1.0):
            points.append({
                candidate.name: _decode_level(candidate, sign if offset == index else 0.0)
                for offset, candidate in enumerate(values)
            })
    points.append({value.name: _decode_level(value, 0.0) for value in values})
    return points


def _box_behnken(values: list[VariableIn]) -> list[dict[str, float]]:
    if len(values) < 3:
        raise ValueError("Box-Behnken design requires at least three variables")
    points: list[dict[str, float]] = []
    for left, right in combinations(range(len(values)), 2):
        for left_sign, right_sign in product((-1.0, 1.0), repeat=2):
            points.append({
                value.name: _decode_level(
                    value,
                    left_sign if index == left else right_sign if index == right else 0.0,
                )
                for index, value in enumerate(values)
            })
    points.append({value.name: _decode_level(value, 0.0) for value in values})
    return points


def _latin_hypercube(values: list[VariableIn], sample_count: int, rng: random.Random) -> list[dict[str, float]]:
    if sample_count < 2:
        raise ValueError("latin hypercube design requires sample_count of at least two")
    columns: list[list[float]] = []
    for value in values:
        strata = list(range(sample_count))
        rng.shuffle(strata)
        columns.append([
            value.low + (stratum + rng.random()) / sample_count * (value.high - value.low)
            for stratum in strata
        ])
    return [
        {value.name: columns[index][row] for index, value in enumerate(values)}
        for row in range(sample_count)
    ]


def _design_runs(request: DesignRequest) -> tuple[list[dict[str, float]], str | None, list[str], str | None]:
    _validate_design_variables(request.variables)
    family = request.response_surface_family
    alias_structure: str | None = None
    warnings: list[str] = []
    if request.method == "full-factorial":
        points = _full_factorial(request.variables, request.levels)
    elif request.method == "fractional-factorial":
        if request.levels != 2:
            raise ValueError("fractional factorial design supports exactly two levels")
        points, alias_structure, warnings = _fractional_factorial(request.variables)
    elif request.method == "response-surface":
        if len(request.variables) < 2:
            raise ValueError("response surface design requires at least two variables")
        family = family or "central-composite"
        if family == "central-composite":
            points = _central_composite(request.variables)
        else:
            points = _box_behnken(request.variables)
    else:
        points = _latin_hypercube(
            request.variables,
            request.sample_count,
            random.Random(request.seed),
        )
    if len(points) * request.replicates > 40:
        raise ValueError("design exceeds the 40-run experiment limit after replication")
    return points, alias_structure, warnings, family


@app.post("/v1/designs")
def create_design(request: DesignRequest) -> dict:
    try:
        points, alias_structure, warnings, family = _design_runs(request)
    except ValueError as error:
        raise HTTPException(status_code=422, detail=str(error)) from error
    rng = random.Random(request.seed)
    runs = []
    for replicate in range(request.replicates):
        for condition, params in enumerate(points, start=1):
            runs.append({
                "condition_key": f"condition-{condition:02d}",
                "replicate_key": f"replicate-{replicate + 1:02d}",
                "block_key": f"block-{replicate % request.block_count + 1:02d}",
                "params": params,
            })
    rng.shuffle(runs)
    for sequence, run in enumerate(runs, start=1):
        run["sequence"] = sequence
        run["execution_key"] = f"{run['condition_key']}-{run['replicate_key']}"
    return {
        "method": request.method,
        "seed": request.seed,
        "runs": runs,
        "warnings": warnings,
        "alias_structure": alias_structure,
        "response_surface_family": family,
        "state_persisted": False,
    }


@app.post("/v1/suggestions", response_model=SuggestionResponse)
def create_suggestions(request: SuggestionRequest) -> SuggestionResponse:
    try:
        campaign = _campaign_from_input(request.campaign)
        derived_features = [
            DerivedFeature(
                name=value.name,
                operator=value.operator,
                inputs=tuple(value.inputs),
                normalization_offset=value.normalization_offset,
                normalization_scale=value.normalization_scale,
                epsilon=value.epsilon,
            )
            for value in request.campaign.derived_features
        ]
        expand_inputs(
            np.full((1, campaign.dim), 0.5),
            [value.name for value in campaign.variables],
            [value.low for value in campaign.variables],
            [value.high for value in campaign.variables],
            derived_features,
        )
        if request.campaign.decision_intent == "validate-hypothesis":
            if len(request.observations) < 3:
                raise ValueError(
                    "hypothesis validation requires at least three valid observations"
                )
            unknown = set(request.campaign.hypothesis_variables).difference(
                campaign.variable_names
            )
            if unknown:
                raise ValueError(
                    f"hypothesis variables are not controllable campaign variables: {sorted(unknown)}"
                )
            if not request.campaign.hypothesis_variables:
                raise ValueError(
                    "hypothesis validation requires at least one controllable hypothesis variable"
                )
        optimizer = (
            BotorchOptimizer(
                campaign,
                derived_features=derived_features,
                seed=request.seed,
            )
            if len(request.observations) >= 3
            else SequentialOptimizer(campaign, seed=request.seed)
        )
        for observation in request.observations:
            optimizer.observe(
                observation.params,
                observation.outcomes,
                constraint_outcomes=observation.constraint_outcomes,
                process_features=observation.process_features,
            )
        suggestion_args = {
            "top_k": request.top_k,
            "candidate_params": request.candidate_pool,
            "n_random": request.n_random,
            "n_samples": request.n_samples,
            "pending_params": request.pending_points,
        }
        if request.campaign.decision_intent == "validate-hypothesis":
            suggestion_args.update(
                decision_intent=request.campaign.decision_intent,
                hypothesis_variables=request.campaign.hypothesis_variables,
            )
        suggestions = optimizer.suggest(**suggestion_args)
    except ValueError as error:
        raise HTTPException(status_code=422, detail=str(error)) from error
    model_version = suggestions[0].model_version
    return SuggestionResponse(
        model_version=model_version,
        observation_count=len(request.observations),
        suggestions=[SuggestionOut.model_validate(suggestion.to_dict()) for suggestion in suggestions],
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
        derived_features = [
            DerivedFeature(
                name=value.name,
                operator=value.operator,
                inputs=tuple(value.inputs),
                normalization_offset=value.normalization_offset,
                normalization_scale=value.normalization_scale,
                epsilon=value.epsilon,
            )
            for value in request.campaign.derived_features
        ]
        expand_inputs(
            np.full((1, campaign.dim), 0.5),
            [value.name for value in campaign.variables],
            [value.low for value in campaign.variables],
            [value.high for value in campaign.variables],
            derived_features,
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
