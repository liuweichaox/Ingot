"""Safe, declarative feature transforms for optimization inputs.

The optimizer core deliberately has no process-specific vocabulary. A feature
may only reference campaign variables or an earlier declared feature and may
only use one of the bounded numerical operators below. Raw Python expressions
and dynamically imported code are not accepted.
"""
from __future__ import annotations

from dataclasses import dataclass
import math
from typing import Sequence

import numpy as np


FEATURE_OPERATORS = frozenset(
    {
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
    }
)

_EXACT_ARITY = {
    "identity": 1,
    "absolute": 1,
    "difference": 2,
    "absolute_difference": 2,
    "ratio": 2,
}


@dataclass(frozen=True)
class DerivedFeature:
    name: str
    operator: str
    inputs: tuple[str, ...]
    normalization_offset: float = 0.0
    normalization_scale: float = 1.0
    epsilon: float = 1e-9
    intercept: float = 0.0
    coefficients: tuple[float, ...] = ()

    def __post_init__(self) -> None:
        name = self.name.strip()
        operator = self.operator.strip().lower()
        inputs = tuple(value.strip() for value in self.inputs)
        if not name:
            raise ValueError("derived feature name must not be empty")
        if operator not in FEATURE_OPERATORS:
            raise ValueError(
                f"derived feature {name} has unsupported operator {operator}"
            )
        if not inputs or any(not value for value in inputs):
            raise ValueError(f"derived feature {name} requires non-empty inputs")
        expected = _EXACT_ARITY.get(operator)
        if expected is not None and len(inputs) != expected:
            raise ValueError(
                f"derived feature {name} operator {operator} requires "
                f"exactly {expected} inputs"
            )
        if not math.isfinite(self.normalization_offset):
            raise ValueError(
                f"derived feature {name} normalization offset must be finite"
            )
        if (
            not math.isfinite(self.normalization_scale)
            or self.normalization_scale <= 0
        ):
            raise ValueError(
                f"derived feature {name} normalization scale must be positive and finite"
            )
        if not math.isfinite(self.epsilon) or self.epsilon <= 0:
            raise ValueError(
                f"derived feature {name} epsilon must be positive and finite"
            )
        if not math.isfinite(self.intercept):
            raise ValueError(f"derived feature {name} intercept must be finite")
        coefficients = tuple(float(value) for value in self.coefficients)
        if operator == "affine":
            if len(coefficients) != len(inputs):
                raise ValueError(
                    f"derived feature {name} affine coefficients must match its inputs"
                )
            if any(not math.isfinite(value) for value in coefficients):
                raise ValueError(
                    f"derived feature {name} affine coefficients must be finite"
                )
        elif coefficients:
            raise ValueError(
                f"derived feature {name} coefficients are only valid for affine features"
            )
        object.__setattr__(self, "name", name)
        object.__setattr__(self, "operator", operator)
        object.__setattr__(self, "inputs", inputs)
        object.__setattr__(self, "coefficients", coefficients)


def expand_inputs(
    unit_values: np.ndarray,
    variable_names: Sequence[str],
    variable_lows: Sequence[float],
    variable_highs: Sequence[float],
    features: Sequence[DerivedFeature],
) -> np.ndarray:
    """Append declared derived features to normalized campaign inputs.

    Operators run in engineering units. Every emitted feature is normalized as
    ``(raw - normalization_offset) / normalization_scale`` before it is added
    to the model input. Later features reference the raw value of earlier
    features, so declarations form a deterministic directed acyclic graph.
    """

    values = np.asarray(unit_values, dtype=float)
    one_row = values.ndim == 1
    values = np.atleast_2d(values)
    names = [str(value).strip() for value in variable_names]
    lows = np.asarray(variable_lows, dtype=float)
    highs = np.asarray(variable_highs, dtype=float)
    if values.shape[1] != len(names) or lows.shape != highs.shape or len(lows) != len(names):
        raise ValueError("optimizer input dimensions do not match campaign variables")
    if len(names) != len(set(names)) or any(not value for value in names):
        raise ValueError("campaign variable names must be non-empty and unique")
    if not np.isfinite(values).all():
        raise ValueError("optimizer inputs must be finite")
    if not np.isfinite(lows).all() or not np.isfinite(highs).all() or np.any(lows >= highs):
        raise ValueError("campaign bounds must be finite and ordered")
    if not features:
        return values[0] if one_row else values

    engineering = lows + values * (highs - lows)
    columns: dict[str, np.ndarray] = {
        name: engineering[:, index] for index, name in enumerate(names)
    }
    output_columns: list[np.ndarray] = [values]
    for feature in features:
        if feature.name in columns:
            raise ValueError(
                f"derived feature name collides with an existing input: {feature.name}"
            )
        missing = [name for name in feature.inputs if name not in columns]
        if missing:
            raise ValueError(
                f"derived feature {feature.name} references unknown or forward inputs: "
                f"{missing}"
            )
        raw = _evaluate(feature, [columns[name] for name in feature.inputs])
        if not np.isfinite(raw).all():
            raise ValueError(
                f"derived feature {feature.name} produced a non-finite value"
            )
        columns[feature.name] = raw
        normalized = (
            raw - feature.normalization_offset
        ) / feature.normalization_scale
        output_columns.append(normalized[:, None])

    expanded = np.column_stack(output_columns)
    return expanded[0] if one_row else expanded


def _evaluate(feature: DerivedFeature, inputs: list[np.ndarray]) -> np.ndarray:
    operator = feature.operator
    stacked = np.column_stack(inputs)
    if operator == "identity":
        return inputs[0]
    if operator == "absolute":
        return np.abs(inputs[0])
    if operator == "sum":
        return stacked.sum(axis=1)
    if operator == "mean":
        return stacked.mean(axis=1)
    if operator == "product":
        return stacked.prod(axis=1)
    if operator == "difference":
        return inputs[0] - inputs[1]
    if operator == "absolute_difference":
        return np.abs(inputs[0] - inputs[1])
    if operator == "ratio":
        denominator = inputs[1]
        safe_denominator = np.where(
            np.abs(denominator) >= feature.epsilon,
            denominator,
            np.where(denominator < 0, -feature.epsilon, feature.epsilon),
        )
        return inputs[0] / safe_denominator
    if operator == "minimum":
        return stacked.min(axis=1)
    if operator == "maximum":
        return stacked.max(axis=1)
    if operator == "standard_deviation":
        return stacked.std(axis=1)
    if operator == "affine":
        return feature.intercept + stacked @ np.asarray(feature.coefficients, dtype=float)
    raise AssertionError(f"unhandled derived feature operator: {operator}")
