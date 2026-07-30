"""Deterministic grey-box features for precision optical-lens molding.

The optimizer must be able to calculate the same features before a recipe is
executed. Therefore these are derived from set-points, while measured trajectory
features remain attached to observations for execution-quality checks.

The process profile deliberately does not name a PLC model. Acquisition hardware
is independent from process physics.
"""
from __future__ import annotations

import numpy as np


def optical_molding_features(x: np.ndarray, names: list[str]) -> np.ndarray:
    values = np.asarray(x, dtype=float)
    one_row = values.ndim == 1
    values = np.atleast_2d(values)
    lowered = [name.lower() for name in names]

    def find(*tokens: str) -> int | None:
        return next(
            (i for i, name in enumerate(lowered) if any(token in name for token in tokens)),
            None,
        )

    upper = find("upper_temp", "upper_mold", "top_temp")
    lower = find("lower_temp", "lower_mold", "bottom_temp")
    temperature = find("soak_temp", "mold_temp", "temperature", "temp")
    force = find("press_force", "pressure", "force")
    speed = find("press_speed", "compression_speed", "speed")
    dwell = find("soak_time", "dwell", "hold_time")
    cooling = find("anneal_rate", "cooling_rate", "cool_rate")

    if upper is not None and lower is not None:
        thermal_balance = np.abs(values[:, upper] - values[:, lower])
        mean_temperature = (values[:, upper] + values[:, lower]) / 2.0
    else:
        mean_temperature = (
            values[:, temperature] if temperature is not None else values.mean(axis=1)
        )
        thermal_balance = np.zeros(len(values))
    thermal_exposure = mean_temperature * (
        0.5 + values[:, dwell] if dwell is not None else 1.0
    )
    compression_exposure = (
        values[:, force] / np.maximum(values[:, speed], 0.05)
        if force is not None and speed is not None
        else np.square(values).mean(axis=1)
    )
    cooling_severity = (
        values[:, cooling] if cooling is not None else values.std(axis=1)
    )
    features = np.column_stack(
        [thermal_balance, thermal_exposure, compression_exposure, cooling_severity]
    )
    return features[0] if one_row else features


def expand_inputs(x: np.ndarray, names: list[str], process_profile: str) -> np.ndarray:
    values = np.atleast_2d(np.asarray(x, dtype=float))
    if process_profile in {
        "optical-lens-molding-v1",
        "fx3u-optical-molding",  # backward-compatible alias
    }:
        features = optical_molding_features(values, names)
        # Inputs are already normalized to [0, 1]. These fixed physical scales
        # keep training and candidate transforms identical.
        scale = np.array([1.0, 1.5, 20.0, 1.0])
        return np.column_stack([values, features / scale])
    return values
