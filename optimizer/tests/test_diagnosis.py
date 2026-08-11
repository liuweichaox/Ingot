import numpy as np
import pytest

from ingot_optimizer.diagnosis import FeatureSpec, diagnose


@pytest.mark.parametrize("row_count", [12, 20, 29])
def test_small_samples_never_report_unfitted_interactions(row_count):
    random = np.random.default_rng(20260731)
    values = random.normal(size=(row_count, 6))
    target = (values[:, 0] + 0.25 * values[:, 1] > 0).astype(float)
    target[:4] = 0
    target[-4:] = 1
    features = [
        FeatureSpec(f"control-parameter:v{index}", "parameter setting", "controllable")
        for index in range(values.shape[1])
    ]

    result = diagnose(
        features=features,
        values=values,
        target=target,
        weights=np.ones(row_count),
        contexts=[{} for _ in range(row_count)],
        timestamps=np.arange(row_count, dtype=float),
        outcome_kind="binary",
        seed=17,
    )

    assert result["model_family"] != "robust-screening-only"
    assert result["interactions"] == []
