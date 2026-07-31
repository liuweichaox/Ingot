import numpy as np
import pytest

from ingot_optimizer import DerivedFeature
from ingot_optimizer.feature_transforms import expand_inputs


def test_declared_features_use_engineering_units_and_prior_feature_outputs():
    features = [
        DerivedFeature(
            name="balance",
            operator="absolute_difference",
            inputs=("a", "b"),
            normalization_scale=10.0,
        ),
        DerivedFeature(
            name="mean_level",
            operator="mean",
            inputs=("a", "b"),
            normalization_offset=100.0,
            normalization_scale=20.0,
        ),
        DerivedFeature(
            name="exposure",
            operator="product",
            inputs=("mean_level", "duration"),
            normalization_scale=1000.0,
        ),
    ]

    expanded = expand_inputs(
        np.asarray([[0.5, 0.25, 0.5]]),
        ["a", "b", "duration"],
        [100.0, 100.0, 5.0],
        [120.0, 120.0, 15.0],
        features,
    )

    assert expanded.shape == (1, 6)
    assert expanded[0, :3] == pytest.approx([0.5, 0.25, 0.5])
    assert expanded[0, 3:] == pytest.approx([0.5, 0.375, 1.075])


def test_declared_features_reject_unknown_forward_reference_and_name_collision():
    with pytest.raises(ValueError, match="unknown or forward"):
        expand_inputs(
            np.asarray([[0.5]]),
            ["x"],
            [0.0],
            [1.0],
            [
                DerivedFeature(
                    name="first",
                    operator="identity",
                    inputs=("later",),
                ),
                DerivedFeature(
                    name="later",
                    operator="identity",
                    inputs=("x",),
                ),
            ],
        )

    with pytest.raises(ValueError, match="collides"):
        expand_inputs(
            np.asarray([[0.5]]),
            ["x"],
            [0.0],
            [1.0],
            [DerivedFeature(name="x", operator="identity", inputs=("x",))],
        )


def test_ratio_uses_declared_epsilon_without_non_finite_values():
    expanded = expand_inputs(
        np.asarray([[0.5, 0.5]]),
        ["numerator", "denominator"],
        [0.0, -1.0],
        [2.0, 1.0],
        [
            DerivedFeature(
                name="safe_ratio",
                operator="ratio",
                inputs=("numerator", "denominator"),
                epsilon=0.1,
            )
        ],
    )

    assert expanded[0, 2] == pytest.approx(10.0)
