"""Small Gaussian-process surrogate used by the dependency-light prototype.

The model uses an ARD RBF kernel and can fit residuals around an optional
physics prior mean.  Production deployments can replace this class with a
BoTorch/GPyTorch adapter without changing the campaign or HTTP contracts.
"""
from __future__ import annotations

from collections.abc import Callable

import numpy as np
from scipy.linalg import cholesky, cho_solve, solve_triangular
from scipy.optimize import minimize


class GaussianProcess:
    """Dependency-light Gaussian-process surrogate for sequential cold starts."""

    def __init__(
        self,
        prior_mean: Callable[[np.ndarray], np.ndarray] | None = None,
        n_restarts: int = 6,
        jitter: float = 1e-6,
        seed: int = 0,
    ):
        if n_restarts < 1:
            raise ValueError("n_restarts must be positive")
        if jitter <= 0:
            raise ValueError("jitter must be positive")
        self.prior_mean = prior_mean
        self.n_restarts = n_restarts
        self.jitter = jitter
        self.rng = np.random.default_rng(seed)
        self.is_fitted = False

    @staticmethod
    def _rbf(
        first: np.ndarray,
        second: np.ndarray,
        length_scales: np.ndarray,
        signal_variance: float,
    ) -> np.ndarray:
        first_scaled = first / length_scales
        second_scaled = second / length_scales
        distances = (
            (first_scaled**2).sum(1)[:, None]
            + (second_scaled**2).sum(1)[None, :]
            - 2.0 * first_scaled @ second_scaled.T
        )
        return signal_variance * np.exp(-0.5 * np.maximum(distances, 0.0))

    def _unpack(self, theta: np.ndarray) -> tuple[np.ndarray, float, float]:
        dimensions = self.X_.shape[1]
        clipped = np.clip(theta, -8.0, 4.0)
        return (
            np.exp(clipped[:dimensions]),
            float(np.exp(clipped[dimensions])),
            float(np.exp(clipped[dimensions + 1])),
        )

    def _negative_log_likelihood(self, theta: np.ndarray) -> float:
        length_scales, signal_variance, noise_variance = self._unpack(theta)
        count = self.X_.shape[0]
        covariance = self._rbf(
            self.X_, self.X_, length_scales, signal_variance
        ) + (noise_variance + self.jitter) * np.eye(count)
        try:
            factor = cholesky(covariance, lower=True)
        except np.linalg.LinAlgError:
            return 1e25
        alpha = cho_solve((factor, True), self.residuals_)
        result = (
            0.5 * self.residuals_ @ alpha
            + np.log(np.diag(factor)).sum()
            + 0.5 * count * np.log(2 * np.pi)
        )
        return float(result)

    def _evaluate_prior(self, points: np.ndarray) -> np.ndarray:
        if self.prior_mean is None:
            return np.zeros(points.shape[0], dtype=float)
        values = np.asarray(self.prior_mean(points), dtype=float).reshape(-1)
        if values.shape != (points.shape[0],) or not np.isfinite(values).all():
            raise ValueError("prior mean must return one finite value per point")
        return values

    def fit(self, points: np.ndarray, outcomes: np.ndarray) -> "GaussianProcess":
        points = np.atleast_2d(np.asarray(points, dtype=float))
        outcomes = np.asarray(outcomes, dtype=float).reshape(-1)
        if points.ndim != 2 or points.shape[0] != outcomes.shape[0]:
            raise ValueError("points and outcomes must contain the same number of rows")
        if points.shape[0] < 2 or points.shape[1] < 1:
            raise ValueError("GaussianProcess requires at least two observations")
        if not np.isfinite(points).all() or not np.isfinite(outcomes).all():
            raise ValueError("GaussianProcess training data must be finite")

        self.X_ = points
        residuals = outcomes - self._evaluate_prior(points)
        self.residual_mean_ = float(residuals.mean())
        self.residual_scale_ = max(float(residuals.std()), 0.05)
        self.residuals_ = (
            residuals - self.residual_mean_
        ) / self.residual_scale_

        dimensions = points.shape[1]
        bounds = [(-4.0, 3.0)] * dimensions + [(-4.0, 3.0), (-8.0, 0.0)]
        best = None
        for _ in range(self.n_restarts):
            initial = np.concatenate(
                [
                    self.rng.uniform(-2, 1, dimensions),
                    [self.rng.uniform(-1, 1)],
                    [self.rng.uniform(-5, -2)],
                ]
            )
            result = minimize(
                self._negative_log_likelihood,
                initial,
                method="L-BFGS-B",
                bounds=bounds,
            )
            if np.isfinite(result.fun) and (best is None or result.fun < best.fun):
                best = result

        self.theta_ = (
            best.x
            if best is not None
            else np.concatenate([np.zeros(dimensions), [0.0, -4.0]])
        )
        length_scales, signal_variance, noise_variance = self._unpack(self.theta_)
        covariance = self._rbf(
            points, points, length_scales, signal_variance
        ) + (noise_variance + self.jitter) * np.eye(len(outcomes))
        self.factor_ = cholesky(covariance, lower=True)
        self.alpha_ = cho_solve((self.factor_, True), self.residuals_)
        self.is_fitted = True
        return self

    def predict(self, points: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
        if not self.is_fitted:
            raise RuntimeError("GaussianProcess must be fitted before prediction")
        points = np.atleast_2d(np.asarray(points, dtype=float))
        if points.shape[1] != self.X_.shape[1] or not np.isfinite(points).all():
            raise ValueError("prediction points have invalid shape or values")
        length_scales, signal_variance, _ = self._unpack(self.theta_)
        cross_covariance = self._rbf(
            points, self.X_, length_scales, signal_variance
        )
        residual_mean = cross_covariance @ self.alpha_
        projection = solve_triangular(
            self.factor_, cross_covariance.T, lower=True
        )
        residual_variance = np.maximum(
            signal_variance - (projection**2).sum(0), 1e-12
        )
        mean = (
            residual_mean * self.residual_scale_
            + self.residual_mean_
            + self._evaluate_prior(points)
        )
        standard_deviation = np.sqrt(residual_variance) * self.residual_scale_
        return mean, standard_deviation
