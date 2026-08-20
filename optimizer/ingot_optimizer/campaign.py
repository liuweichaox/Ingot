"""Campaign specification and validation for process optimization.

All numerical optimization happens in a normalized unit cube.  The public
contract remains in engineering units and rejects incomplete, non-finite, or
out-of-range observations before they can enter a model.
"""
from __future__ import annotations

from dataclasses import dataclass, field
import math
from typing import Mapping

import numpy as np


@dataclass(frozen=True)
class Variable:
    name: str
    low: float
    high: float
    unit: str = ""

    def __post_init__(self) -> None:
        name = self.name.strip()
        if not name:
            raise ValueError("variable name must not be empty")
        if not math.isfinite(self.low) or not math.isfinite(self.high) or self.low >= self.high:
            raise ValueError(f"variable {name} must have finite low < high bounds")
        object.__setattr__(self, "name", name)
        object.__setattr__(self, "unit", self.unit.strip())

    def norm(self, value: float) -> float:
        return (value - self.low) / (self.high - self.low)

    def denorm(self, unit_value: float) -> float:
        return self.low + unit_value * (self.high - self.low)


@dataclass(frozen=True)
class Objective:
    name: str
    kind: str
    threshold: float | None = None
    target: float | None = None
    tol: float | None = None
    lower: float | None = None
    upper: float | None = None
    outcome_lower_bound: float | None = None
    outcome_upper_bound: float | None = None
    unit: str = ""
    weight: float = 1.0

    def __post_init__(self) -> None:
        name = self.name.strip()
        kind = self.kind.strip().lower()
        if not name:
            raise ValueError("objective name must not be empty")
        if kind not in {"le", "ge", "target", "range"}:
            raise ValueError(f"objective {name} has unsupported kind {kind}")
        values = (
            self.threshold,
            self.target,
            self.tol,
            self.lower,
            self.upper,
            self.outcome_lower_bound,
            self.outcome_upper_bound,
        )
        if any(value is not None and not math.isfinite(value) for value in values):
            raise ValueError(f"objective {name} contains a non-finite specification")
        if not math.isfinite(self.weight) or self.weight <= 0:
            raise ValueError(f"objective {name} requires a positive finite weight")
        if kind in {"le", "ge"} and self.threshold is None:
            raise ValueError(f"objective {name} requires threshold")
        if kind == "target" and (
            self.target is None or self.tol is None or self.tol <= 0
        ):
            raise ValueError(f"objective {name} requires target and positive tolerance")
        if kind == "range" and (
            self.lower is None or self.upper is None or self.lower >= self.upper
        ):
            raise ValueError(f"objective {name} requires lower < upper")
        if (self.outcome_lower_bound is None) != (self.outcome_upper_bound is None):
            raise ValueError(f"objective {name} outcome bounds must be supplied together")
        if (
            self.outcome_lower_bound is not None
            and self.outcome_upper_bound is not None
            and self.outcome_lower_bound >= self.outcome_upper_bound
        ):
            raise ValueError(f"objective {name} requires outcome lower bound < upper bound")
        object.__setattr__(self, "name", name)
        object.__setattr__(self, "kind", kind)
        object.__setattr__(self, "unit", self.unit.strip())

    def clip(self, value):
        if self.outcome_lower_bound is None or self.outcome_upper_bound is None:
            return value
        if hasattr(value, "clamp"):
            return value.clamp(
                min=float(self.outcome_lower_bound),
                max=float(self.outcome_upper_bound),
            )
        return np.clip(
            value,
            float(self.outcome_lower_bound),
            float(self.outcome_upper_bound),
        )

    def bounded_prediction(
        self, mean: float, standard_deviation: float
    ) -> tuple[float, float, float, float]:
        lower_95 = mean - 1.96 * standard_deviation
        upper_95 = mean + 1.96 * standard_deviation
        if self.outcome_lower_bound is None or self.outcome_upper_bound is None:
            return mean, standard_deviation, lower_95, upper_95
        low = float(self.outcome_lower_bound)
        high = float(self.outcome_upper_bound)
        if standard_deviation <= 1e-12:
            bounded_mean = float(np.clip(mean, low, high))
            return bounded_mean, 0.0, bounded_mean, bounded_mean
        sigma = standard_deviation
        alpha = (low - mean) / sigma
        beta = (high - mean) / sigma
        cdf_alpha = 0.5 * (1.0 + math.erf(alpha / math.sqrt(2.0)))
        cdf_beta = 0.5 * (1.0 + math.erf(beta / math.sqrt(2.0)))
        pdf_alpha = math.exp(-0.5 * alpha * alpha) / math.sqrt(2.0 * math.pi)
        pdf_beta = math.exp(-0.5 * beta * beta) / math.sqrt(2.0 * math.pi)
        inside = cdf_beta - cdf_alpha
        bounded_mean = (
            low * cdf_alpha
            + mean * inside
            + sigma * (pdf_alpha - pdf_beta)
            + high * (1.0 - cdf_beta)
        )
        second_moment = (
            low * low * cdf_alpha
            + (mean * mean + sigma * sigma) * inside
            + 2.0 * mean * sigma * (pdf_alpha - pdf_beta)
            + sigma * sigma * (alpha * pdf_alpha - beta * pdf_beta)
            + high * high * (1.0 - cdf_beta)
        )
        bounded_deviation = math.sqrt(max(second_moment - bounded_mean * bounded_mean, 0.0))
        return (
            bounded_mean,
            bounded_deviation,
            float(np.clip(lower_95, low, high)),
            float(np.clip(upper_95, low, high)),
        )

    def badness(self, value: float) -> float:
        """Return normalized badness where ``badness <= 1`` is in specification."""
        if not math.isfinite(value):
            raise ValueError(f"objective {self.name} outcome must be finite")
        if self.kind == "le":
            threshold = float(self.threshold)
            scale = max(abs(threshold), 1.0)
            return 1.0 + (value - threshold) / scale
        if self.kind == "ge":
            threshold = float(self.threshold)
            scale = max(abs(threshold), 1.0)
            return 1.0 + (threshold - value) / scale
        if self.kind == "target":
            return abs(value - float(self.target)) / float(self.tol)
        midpoint = (float(self.lower) + float(self.upper)) / 2.0
        half_width = (float(self.upper) - float(self.lower)) / 2.0
        return abs(value - midpoint) / half_width


@dataclass(frozen=True)
class ParameterConstraint:
    variable: str
    operator: str
    limit: float
    safety_critical: bool = False

    def __post_init__(self) -> None:
        variable = self.variable.strip()
        operator = self.operator.strip()
        if not variable:
            raise ValueError("constraint variable must not be empty")
        if operator not in {"<=", ">="}:
            raise ValueError("constraint operator must be <= or >=")
        if not math.isfinite(self.limit):
            raise ValueError("constraint limit must be finite")
        object.__setattr__(self, "variable", variable)
        object.__setattr__(self, "operator", operator)

    def is_satisfied(self, value: float, tolerance: float = 1e-12) -> bool:
        if self.operator == "<=":
            return value <= self.limit + tolerance
        return value >= self.limit - tolerance


@dataclass(frozen=True)
class ForbiddenCombinationFactor:
    variable: str
    minimum: float | None = None
    maximum: float | None = None

    def __post_init__(self) -> None:
        variable = self.variable.strip()
        if not variable:
            raise ValueError("forbidden-combination variable must not be empty")
        if self.minimum is None and self.maximum is None:
            raise ValueError("forbidden-combination factor requires a minimum or maximum")
        if self.minimum is not None and not math.isfinite(self.minimum):
            raise ValueError("forbidden-combination minimum must be finite")
        if self.maximum is not None and not math.isfinite(self.maximum):
            raise ValueError("forbidden-combination maximum must be finite")
        if self.minimum is not None and self.maximum is not None and self.minimum > self.maximum:
            raise ValueError("forbidden-combination minimum must not exceed maximum")
        object.__setattr__(self, "variable", variable)

    def matches(self, value: float, tolerance: float = 1e-12) -> bool:
        return (
            (self.minimum is None or value >= self.minimum - tolerance)
            and (self.maximum is None or value <= self.maximum + tolerance)
        )


@dataclass(frozen=True)
class ForbiddenCombination:
    name: str
    factors: tuple[ForbiddenCombinationFactor, ...]

    def __post_init__(self) -> None:
        name = self.name.strip()
        factors = tuple(self.factors)
        if not name:
            raise ValueError("forbidden-combination name must not be empty")
        if len(factors) < 2:
            raise ValueError("forbidden combination requires at least two factors")
        names = [value.variable for value in factors]
        if len(names) != len(set(names)):
            raise ValueError("forbidden-combination variables must be unique")
        object.__setattr__(self, "name", name)
        object.__setattr__(self, "factors", factors)

    def matches(self, params: Mapping[str, float]) -> bool:
        return all(factor.matches(float(params[factor.variable])) for factor in self.factors)


@dataclass(frozen=True)
class OutcomeConstraint:
    name: str
    operator: str
    limit: float
    unit: str = ""
    safety_critical: bool = True
    minimum_probability: float = 0.95

    def __post_init__(self) -> None:
        name = self.name.strip()
        operator = self.operator.strip()
        if not name:
            raise ValueError("outcome constraint name must not be empty")
        if operator not in {"<=", ">="}:
            raise ValueError("outcome constraint operator must be <= or >=")
        if not math.isfinite(self.limit):
            raise ValueError("outcome constraint limit must be finite")
        if (
            not math.isfinite(self.minimum_probability)
            or self.minimum_probability <= 0
            or self.minimum_probability > 1
        ):
            raise ValueError(
                "outcome constraint minimum_probability must be in (0, 1]"
            )
        object.__setattr__(self, "name", name)
        object.__setattr__(self, "operator", operator)
        object.__setattr__(self, "unit", self.unit.strip())

    def is_satisfied(self, value: float, tolerance: float = 1e-12) -> bool:
        if self.operator == "<=":
            return value <= self.limit + tolerance
        return value >= self.limit - tolerance


@dataclass
class Campaign:
    name: str
    variables: list[Variable]
    objectives: list[Objective]
    constraints: list[ParameterConstraint] = field(default_factory=list)
    context: dict[str, str] = field(default_factory=dict)
    outcome_constraints: list[OutcomeConstraint] = field(default_factory=list)
    forbidden_combinations: list[ForbiddenCombination] = field(default_factory=list)

    def __post_init__(self) -> None:
        self.name = self.name.strip()
        if not self.name:
            raise ValueError("campaign name must not be empty")
        if not self.variables:
            raise ValueError("campaign requires at least one variable")
        if not self.objectives:
            raise ValueError("campaign requires at least one objective")
        variable_names = [value.name for value in self.variables]
        objective_names = [value.name for value in self.objectives]
        if len(variable_names) != len(set(variable_names)):
            raise ValueError("campaign variable names must be unique")
        if len(objective_names) != len(set(objective_names)):
            raise ValueError("campaign objective names must be unique")
        outcome_constraint_names = [
            value.name for value in self.outcome_constraints
        ]
        if len(outcome_constraint_names) != len(set(outcome_constraint_names)):
            raise ValueError("campaign outcome constraint names must be unique")
        overlap = set(objective_names).intersection(outcome_constraint_names)
        if overlap:
            raise ValueError(
                f"objectives and outcome constraints must be distinct: {sorted(overlap)}"
            )
        unknown_constraints = {
            constraint.variable for constraint in self.constraints
        }.difference(variable_names)
        if unknown_constraints:
            raise ValueError(
                f"constraints reference unknown variables: {sorted(unknown_constraints)}"
            )
        unknown_forbidden = {
            factor.variable
            for combination in self.forbidden_combinations
            for factor in combination.factors
        }.difference(variable_names)
        if unknown_forbidden:
            raise ValueError(
                "forbidden combinations reference unknown variables: "
                f"{sorted(unknown_forbidden)}"
            )
        self.context = {
            str(key).strip(): str(value).strip()
            for key, value in self.context.items()
            if str(key).strip() and str(value).strip()
        }
        if not self._has_feasible_bounds():
            raise ValueError("campaign constraints leave no feasible variable range")

    @property
    def dim(self) -> int:
        return len(self.variables)

    @property
    def variable_names(self) -> set[str]:
        return {value.name for value in self.variables}

    @property
    def objective_names(self) -> set[str]:
        return {value.name for value in self.objectives}

    @property
    def outcome_constraint_names(self) -> set[str]:
        return {value.name for value in self.outcome_constraints}

    def _has_feasible_bounds(self) -> bool:
        bounds = {value.name: [value.low, value.high] for value in self.variables}
        for constraint in self.constraints:
            if constraint.operator == "<=":
                bounds[constraint.variable][1] = min(
                    bounds[constraint.variable][1], constraint.limit
                )
            else:
                bounds[constraint.variable][0] = max(
                    bounds[constraint.variable][0], constraint.limit
                )
        return all(low < high for low, high in bounds.values())

    def validate_params(
        self, params: Mapping[str, float], *, enforce_candidate_constraints: bool = True
    ) -> None:
        names = set(params)
        if names != self.variable_names:
            missing = sorted(self.variable_names.difference(names))
            extra = sorted(names.difference(self.variable_names))
            raise ValueError(f"parameter keys mismatch; missing={missing}, extra={extra}")
        for variable in self.variables:
            value = float(params[variable.name])
            if not math.isfinite(value):
                raise ValueError(f"parameter {variable.name} must be finite")
            if value < variable.low or value > variable.high:
                raise ValueError(
                    f"parameter {variable.name}={value} is outside "
                    f"[{variable.low}, {variable.high}]"
                )
        if enforce_candidate_constraints:
            for constraint in self.constraints:
                if not constraint.is_satisfied(float(params[constraint.variable])):
                    raise ValueError(
                        f"parameter {constraint.variable} violates "
                        f"{constraint.operator} {constraint.limit}"
                    )
            for combination in self.forbidden_combinations:
                if combination.matches(params):
                    raise ValueError(
                        f"parameters match forbidden combination {combination.name}"
                    )

    def validate_outcomes(self, outcomes: Mapping[str, float]) -> None:
        names = set(outcomes)
        if names != self.objective_names:
            missing = sorted(self.objective_names.difference(names))
            extra = sorted(names.difference(self.objective_names))
            raise ValueError(f"outcome keys mismatch; missing={missing}, extra={extra}")
        for objective in self.objectives:
            value = float(outcomes[objective.name])
            if not math.isfinite(value):
                raise ValueError(f"outcome {objective.name} must be finite")
            if (
                (objective.outcome_lower_bound is not None
                 and value < objective.outcome_lower_bound)
                or (objective.outcome_upper_bound is not None
                    and value > objective.outcome_upper_bound)
            ):
                raise ValueError(
                    f"outcome {objective.name}={value} is outside "
                    f"[{objective.outcome_lower_bound}, {objective.outcome_upper_bound}]"
                )

    def validate_constraint_outcomes(self, outcomes: Mapping[str, float]) -> None:
        names = set(outcomes)
        if names != self.outcome_constraint_names:
            missing = sorted(self.outcome_constraint_names.difference(names))
            extra = sorted(names.difference(self.outcome_constraint_names))
            raise ValueError(
                "constraint outcome keys mismatch; "
                f"missing={missing}, extra={extra}"
            )
        for constraint in self.outcome_constraints:
            value = float(outcomes[constraint.name])
            if not math.isfinite(value):
                raise ValueError(
                    f"constraint outcome {constraint.name} must be finite"
                )

    def distance_to_spec(self, outcomes: Mapping[str, float]) -> float:
        self.validate_outcomes(outcomes)
        badness = [
            objective.badness(float(outcomes[objective.name]))
            for objective in self.objectives
        ]
        return max(badness) - 1.0

    def to_unit(
        self, params: Mapping[str, float], *, enforce_candidate_constraints: bool = True
    ) -> np.ndarray:
        self.validate_params(
            params, enforce_candidate_constraints=enforce_candidate_constraints
        )
        return np.array(
            [variable.norm(float(params[variable.name])) for variable in self.variables],
            dtype=float,
        )

    def from_unit(self, unit_values: np.ndarray) -> dict[str, float]:
        values = np.asarray(unit_values, dtype=float).reshape(-1)
        if values.shape != (self.dim,) or not np.isfinite(values).all():
            raise ValueError(f"unit point must contain {self.dim} finite values")
        if np.any(values < 0.0) or np.any(values > 1.0):
            raise ValueError("unit point must remain inside [0, 1]")
        result = {
            variable.name: float(variable.denorm(values[index]))
            for index, variable in enumerate(self.variables)
        }
        self.validate_params(result)
        return result

    def is_feasible_unit(self, unit_values: np.ndarray) -> bool:
        try:
            self.from_unit(unit_values)
            return True
        except ValueError:
            return False
