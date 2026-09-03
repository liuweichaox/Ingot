"""Observed-coverage envelope for recommendations built from production runs.

Production runs are not a designed experiment.  Settings move together, cluster
around the current known-good recipe, and leave most of the declared range
unobserved.  A surrogate fitted on that data reports small uncertainty inside
the cluster while extrapolating freely outside it, so the declared safety bounds
alone do not bound where a recommendation may land.

The envelope restricts candidates to the region the observations actually cover.
Two independent gates apply, both in the campaign unit cube:

``range gate``
    Every variable stays between its observed minimum and maximum, widened by a
    margin proportional to the observed spread with an absolute floor, so a
    variable that never moved still permits a small local step.

``leverage gate``
    The candidate's Mahalanobis distance from the observed centre, measured with
    a ridge-regularized observation covariance, stays within the largest distance
    among the observations themselves.  This is the hat-matrix extrapolation
    criterion used in response surface work.  It admits interpolation inside a
    sparse cluster and rejects points off the directions production actually
    varied, neither of which a per-variable box can express.

The ridge term is the same absolute floor used by the range gate, so a direction
production never varied is treated as covering one minimum step rather than
either zero width or the full declared range.
"""
from __future__ import annotations

from dataclasses import dataclass
import math
from typing import TYPE_CHECKING

import numpy as np

if TYPE_CHECKING:  # pragma: no cover - import cycle guard for type checking only
    from .campaign import Campaign


COVERAGE_RELATIVE_MARGIN = 0.10
"""Fraction of the observed spread a recommendation may extend beyond it."""

COVERAGE_MINIMUM_STEP = 0.02
"""Smallest step, as a fraction of the declared range, an unvaried variable keeps."""

COVERAGE_LEVERAGE_EXPANSION = 1.10
"""Fraction by which a candidate may exceed the largest observed leverage."""

MINIMUM_COVERAGE_OBSERVATIONS = 3
"""Observations required before an envelope describes anything usable."""

_TOLERANCE = 1e-9


@dataclass(frozen=True)
class CoverageEnvelope:
    """Bounds the parameter region that observed production runs actually cover."""

    lower: np.ndarray
    upper: np.ndarray
    observed_lower: np.ndarray
    observed_upper: np.ndarray
    center: np.ndarray
    precision: np.ndarray
    leverage_limit: float
    observation_count: int

    def leverage(self, unit_points: np.ndarray) -> np.ndarray:
        """Return the squared Mahalanobis distance of each point from the centre."""
        points = np.atleast_2d(np.asarray(unit_points, dtype=float))
        if points.shape[1] != self.lower.shape[0]:
            raise ValueError("coverage envelope and candidate dimensions differ")
        delta = points - self.center
        return np.einsum("ij,jk,ik->i", delta, self.precision, delta)

    def contains(self, unit_points: np.ndarray) -> np.ndarray:
        """Return a mask selecting the points inside both coverage gates."""
        points = np.atleast_2d(np.asarray(unit_points, dtype=float))
        inside_range = np.all(
            (points >= self.lower - _TOLERANCE)
            & (points <= self.upper + _TOLERANCE),
            axis=1,
        )
        return inside_range & (
            self.leverage(points) <= self.leverage_limit + _TOLERANCE
        )


def build_coverage_envelope(observed_units: np.ndarray) -> CoverageEnvelope:
    """Derive the coverage envelope from observations already in unit space."""
    observed = np.atleast_2d(np.asarray(observed_units, dtype=float))
    if observed.ndim != 2:
        raise ValueError("coverage observations must form a two-dimensional array")
    if observed.shape[0] < MINIMUM_COVERAGE_OBSERVATIONS:
        raise ValueError(
            "an observed coverage envelope requires at least "
            f"{MINIMUM_COVERAGE_OBSERVATIONS} observations"
        )
    if not np.isfinite(observed).all():
        raise ValueError("coverage observations must be finite")

    observed_lower = observed.min(axis=0)
    observed_upper = observed.max(axis=0)
    margin = np.maximum(
        COVERAGE_RELATIVE_MARGIN * (observed_upper - observed_lower),
        COVERAGE_MINIMUM_STEP,
    )
    center = observed.mean(axis=0)
    covariance = np.atleast_2d(np.cov(observed, rowvar=False, bias=True))
    precision = np.linalg.inv(
        covariance + np.eye(observed.shape[1]) * COVERAGE_MINIMUM_STEP**2
    )
    delta = observed - center
    observed_leverage = np.einsum("ij,jk,ik->i", delta, precision, delta)
    return CoverageEnvelope(
        lower=np.clip(observed_lower - margin, 0.0, 1.0),
        upper=np.clip(observed_upper + margin, 0.0, 1.0),
        observed_lower=observed_lower,
        observed_upper=observed_upper,
        center=center,
        precision=precision,
        leverage_limit=float(np.max(observed_leverage))
        * COVERAGE_LEVERAGE_EXPANSION**2,
        observation_count=int(observed.shape[0]),
    )


def sample_within_envelope(
    envelope: CoverageEnvelope,
    count: int,
    *,
    seed: int = 0,
    oversample: int = 16,
) -> np.ndarray:
    """Draw unit-cube candidates from inside the envelope instead of filtering it.

    Production runs often vary several settings together, which leaves the
    admitted region a thin sliver of the declared cube.  Rejection sampling from
    the whole cube almost never lands in that sliver, so candidates are drawn
    directly from the leverage ellipsoid and then reduced by the range gate.
    """
    if count < 1:
        raise ValueError("candidate count must be positive")
    dimension = envelope.lower.shape[0]
    factor = np.linalg.cholesky(np.linalg.inv(envelope.precision))
    radius = math.sqrt(envelope.leverage_limit)
    rng = np.random.default_rng(seed)
    draws = rng.standard_normal((count * oversample, dimension))
    norms = np.linalg.norm(draws, axis=1, keepdims=True)
    directions = draws / np.where(norms > 0.0, norms, 1.0)
    radii = rng.random((count * oversample, 1)) ** (1.0 / dimension)
    points = envelope.center + radius * (directions * radii) @ factor.T
    inside_cube = np.all((points >= 0.0) & (points <= 1.0), axis=1)
    points = points[inside_cube]
    if points.size:
        points = points[envelope.contains(points)]
    return points[:count]


def describe_coverage_envelope(
    envelope: CoverageEnvelope, campaign: "Campaign"
) -> dict:
    """Report the envelope in engineering units so the platform can freeze it."""
    if len(campaign.variables) != envelope.lower.shape[0]:
        raise ValueError("coverage envelope and campaign dimensions differ")
    return {
        "observation_count": envelope.observation_count,
        "leverage_limit": envelope.leverage_limit,
        "variables": [
            {
                "name": variable.name,
                "unit": variable.unit,
                "lower": float(variable.denorm(float(envelope.lower[index]))),
                "upper": float(variable.denorm(float(envelope.upper[index]))),
                "observed_minimum": float(
                    variable.denorm(float(envelope.observed_lower[index]))
                ),
                "observed_maximum": float(
                    variable.denorm(float(envelope.observed_upper[index]))
                ),
            }
            for index, variable in enumerate(campaign.variables)
        ],
    }
