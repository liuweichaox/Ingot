#!/usr/bin/env python3
"""Diagnose visible-evidence model routing on the inspected v7 trajectories."""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path

import numpy as np

from ingot_optimizer.botorch_engine import (
    RAW_LINEAR_RIDGE,
    RAW_QUADRATIC_RIDGE,
    _leave_one_out_error,
)


ROOT = Path(__file__).resolve().parent.parent
RESULT_PATH = ROOT / "latest-results-v7.json"
BENCHMARK_PATH = ROOT / "benchmark_v7.py"


def load_benchmark():
    specification = importlib.util.spec_from_file_location(
        "ingot_public_validation_v7_diagnosis", BENCHMARK_PATH
    )
    if specification is None or specification.loader is None:
        raise RuntimeError("unable to load v7 benchmark adapter")
    benchmark = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(benchmark)
    return benchmark


def scored(episode: dict, method: str, failure: int) -> float:
    value = episode["methods"][method]["additional_trials"]
    return float(failure if value is None else value)


def kernel_ridge_loo_error(
    points: np.ndarray, outcomes: np.ndarray, *, ridge: float
) -> float:
    squared = np.square(points[:, None, :] - points[None, :, :]).sum(axis=2)
    positive = squared[squared > 1e-12]
    length_squared = float(np.median(positive)) if len(positive) else 1.0
    kernel = np.exp(-0.5 * squared / max(length_squared, 1e-12))
    inverse = np.linalg.inv(kernel + ridge * np.eye(len(kernel)))
    hat = kernel @ inverse
    fitted = hat @ outcomes
    denominator = np.maximum(1.0 - np.diag(hat), 1e-9)
    residual = (outcomes - fitted) / denominator
    return float(np.sqrt(np.mean(np.square(residual))))


def main() -> None:
    benchmark = load_benchmark()
    protocol = benchmark.load_protocol()
    payload = json.loads(RESULT_PATH.read_text(encoding="utf-8"))
    failure = int(protocol["statistics"]["unsuccessful_trial_value"])
    rows_by_dataset = {
        name: benchmark.load_rows(name, protocol) for name in benchmark.DATASETS
    }
    unit_data = {}
    for dataset, rows in rows_by_dataset.items():
        for unit_id, context, unit_rows in benchmark.group_evaluation_units(
            dataset, rows, protocol
        ):
            campaign, _ = benchmark.build_campaign(
                dataset, unit_id, context, unit_rows, protocol
            )
            history = benchmark.build_history(dataset, unit_rows)
            unit_data[unit_id] = (campaign, {item["run_id"]: item for item in history})

    records = []
    for result_record in payload["records"]:
        unit_id = result_record["evaluation_unit"]
        campaign, history = unit_data[unit_id]
        for episode in result_record["episodes"]:
            initial = [history[run_id] for run_id in episode["initial_run_ids"]]
            points = np.vstack(
                [campaign.to_unit(item["params"]) for item in initial]
            )
            distance = np.asarray(
                [campaign.distance_to_spec(item["outcomes"]) for item in initial]
            )
            linear_error = _leave_one_out_error(
                points,
                distance,
                quadratic=False,
                ridge=RAW_LINEAR_RIDGE,
            )
            quadratic_error = _leave_one_out_error(
                points,
                distance,
                quadratic=True,
                ridge=RAW_QUADRATIC_RIDGE,
            )
            kernel_errors = {
                str(ridge): kernel_ridge_loo_error(
                    points, distance, ridge=ridge
                )
                for ridge in (0.01, 0.05, 0.1)
            }
            records.append(
                {
                    "unit": unit_id,
                    "quadratic_to_linear_loo": quadratic_error / linear_error,
                    "kernel_to_quadratic_loo": {
                        ridge: error / quadratic_error
                        for ridge, error in kernel_errors.items()
                    },
                    "primary": scored(episode, benchmark.PRIMARY, failure),
                    "quadratic": scored(
                        episode,
                        "regularized-quadratic-response-surface",
                        failure,
                    ),
                }
            )

    output = {"units": {}, "initial_loo_router_sweep": []}
    for unit in sorted({item["unit"] for item in records}):
        unit_records = [item for item in records if item["unit"] == unit]
        ratios = np.asarray(
            [item["quadratic_to_linear_loo"] for item in unit_records]
        )
        output["units"][unit] = {
            "quadratic_to_linear_loo_median": float(np.median(ratios)),
            "quadratic_loo_better_fraction": float((ratios < 1.0).mean()),
            "quadratic_loo_10pct_better_fraction": float((ratios <= 0.9).mean()),
            "kernel_to_quadratic_loo": {
                ridge: {
                    "median": float(
                        np.median(
                            [
                                item["kernel_to_quadratic_loo"][ridge]
                                for item in unit_records
                            ]
                        )
                    ),
                    "20pct_better_fraction": float(
                        np.mean(
                            [
                                item["kernel_to_quadratic_loo"][ridge] <= 0.8
                                for item in unit_records
                            ]
                        )
                    ),
                }
                for ridge in ("0.01", "0.05", "0.1")
            },
            "primary_mean": float(
                np.mean([item["primary"] for item in unit_records])
            ),
            "quadratic_mean": float(
                np.mean([item["quadratic"] for item in unit_records])
            ),
        }

    primary = np.asarray([item["primary"] for item in records])
    quadratic = np.asarray([item["quadratic"] for item in records])
    ratios = np.asarray([item["quadratic_to_linear_loo"] for item in records])
    for threshold in np.linspace(0.7, 1.1, 17):
        choose_quadratic = ratios <= threshold
        routed = np.where(choose_quadratic, quadratic, primary)
        output["initial_loo_router_sweep"].append(
            {
                "quadratic_to_linear_threshold": float(threshold),
                "quadratic_selected_fraction": float(choose_quadratic.mean()),
                "mean_capped_trials": float(routed.mean()),
                "relative_reduction_vs_primary": float(
                    (primary.mean() - routed.mean()) / primary.mean()
                ),
                "relative_reduction_vs_quadratic": float(
                    (quadratic.mean() - routed.mean()) / quadratic.mean()
                ),
                "unit_non_worse_vs_quadratic": float(
                    np.mean(
                        [
                            routed[
                                np.asarray([item["unit"] == unit for item in records])
                            ].mean()
                            <= quadratic[
                                np.asarray([item["unit"] == unit for item in records])
                            ].mean()
                            for unit in sorted(output["units"])
                        ]
                    )
                ),
            }
        )
    print(json.dumps(output, indent=2))


if __name__ == "__main__":
    main()
