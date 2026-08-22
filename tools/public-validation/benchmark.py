#!/usr/bin/env python3
"""Run the reproducible public-data offline validation benchmark.

This benchmark validates workflow behavior and claim boundaries. It does not
claim that public FDM data proves savings in another factory or process.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import statistics
from collections import defaultdict
from pathlib import Path

import numpy as np

from ingot_optimizer import Campaign, Objective, Variable
from ingot_optimizer.replay import replay_history_pool


ROOT = Path(__file__).resolve().parent
DATASET = ROOT / "data" / "fdm-doe-grid.csv"
SOURCE_URL = "https://data.mendeley.com/datasets/zd6td6svd6/2"
SOURCE_DOI = "10.17632/zd6td6svd6.2"
LICENSE = "CC BY 4.0"
EXPECTED_SHA256 = "dda1eab160b52004e2beae0d49f8cd2e8027ad8293eaeec182b32205eb8b0fc4"
CONTEXT_FIELDS = ("printer_type", "material", "infill_pattern")
CONTROL_FIELDS = ("layer_thickness_mm", "infill_density_pct", "speed_mm_s")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def load_rows() -> list[dict[str, str]]:
    actual_hash = sha256(DATASET)
    if actual_hash != EXPECTED_SHA256:
        raise ValueError(
            f"public fixture checksum mismatch: expected {EXPECTED_SHA256}, got {actual_hash}"
        )
    with DATASET.open(encoding="utf-8", newline="") as stream:
        rows = list(csv.DictReader(stream))
    if len(rows) != 162:
        raise ValueError(f"expected 162 public DOE rows, got {len(rows)}")
    sample_ids = [row["sample_no"] for row in rows]
    if len(sample_ids) != len(set(sample_ids)):
        raise ValueError("public DOE fixture contains duplicate sample identifiers")
    return rows


def group_scenarios(rows: list[dict[str, str]]) -> list[tuple[tuple[str, ...], list[dict[str, str]]]]:
    grouped: dict[tuple[str, ...], list[dict[str, str]]] = defaultdict(list)
    for row in rows:
        grouped[tuple(row[field] for field in CONTEXT_FIELDS)].append(row)
    scenarios = sorted(grouped.items())
    if len(scenarios) != 6:
        raise ValueError(f"expected six categorical contexts, got {len(scenarios)}")
    for context, values in scenarios:
        if len(values) != 27:
            raise ValueError(f"context {context} expected 27 DOE points, got {len(values)}")
        for field in CONTROL_FIELDS:
            if len({row[field] for row in values}) != 3:
                raise ValueError(f"context {context} does not contain three levels for {field}")
        if any(len({row[field] for row in values}) != 1 for field in CONTEXT_FIELDS):
            raise ValueError(f"context {context} mixes categorical factor levels")
    return scenarios


def capped_mean(values: list[int | None], budget: int) -> float:
    return statistics.mean(value if value is not None else budget + 1 for value in values)


def selected_trials(selected_runs, history, campaign) -> list[int | None]:
    results: list[int | None] = []
    for selected in selected_runs:
        success = any(
            campaign.distance_to_spec(history[index]["outcomes"]) <= 0
            for index in selected
        )
        results.append(len(selected) if success else None)
    return results


def run_scenario(
    context_values: tuple[str, ...],
    rows: list[dict[str, str]],
    *,
    seeds: int,
    budget: int,
) -> dict:
    rows = sorted(rows, key=lambda row: int(row["sample_no"]))
    roughness = np.asarray([float(row["roughness_avg"]) for row in rows])
    stress = np.asarray([float(row["peak_stress_kpa"]) for row in rows])
    roughness_limit = float(np.quantile(roughness, 0.40))
    stress_limit = float(np.quantile(stress, 0.60))
    context = dict(zip(CONTEXT_FIELDS, context_values, strict=True))
    history = [
        {
            "params": {
                "layer_thickness_mm": float(row["layer_thickness_mm"]),
                "infill_density_pct": float(row["infill_density_pct"]),
                "speed_mm_s": float(row["speed_mm_s"]),
            },
            "outcomes": {
                "roughness_avg": float(row["roughness_avg"]),
                "peak_stress_kpa": float(row["peak_stress_kpa"]),
            },
            "occurred_at": float(sequence),
            "run_id": f"fdm-{row['sample_no']}",
        }
        for sequence, row in enumerate(rows, start=1)
    ]
    campaign = Campaign(
        "fdm-public-" + "-".join(context_values),
        [
            Variable("layer_thickness_mm", 0.1, 0.3, "mm"),
            Variable("infill_density_pct", 20.0, 80.0, "%"),
            Variable("speed_mm_s", 30.0, 70.0, "mm/s"),
        ],
        [
            Objective(
                "roughness_avg",
                "le",
                threshold=roughness_limit,
                outcome_lower_bound=max(0.0, float(roughness.min()) - 1e-6),
                outcome_upper_bound=float(roughness.max()) + 1e-6,
                unit="um",
            ),
            Objective(
                "peak_stress_kpa",
                "ge",
                threshold=stress_limit,
                outcome_lower_bound=max(0.0, float(stress.min()) - 1e-6),
                outcome_upper_bound=float(stress.max()) + 1e-6,
                unit="kPa",
            ),
        ],
        context=context,
    )
    feasible = sum(campaign.distance_to_spec(row["outcomes"]) <= 0 for row in history)
    if feasible == 0:
        return {"context": context, "status": "no-observed-setting-meets-spec"}
    result = replay_history_pool(
        campaign,
        history,
        budget=budget,
        n_seeds=seeds,
        initial_observation_count=3,
    )
    response_runs = selected_trials(
        result["response_surface_selected_history_indices"], history, campaign
    )
    return {
        "context": context,
        "status": "completed",
        "context_isolated": campaign.context == context,
        "feasible_settings": feasible,
        "budget": budget,
        "optimizer_success_rate": result["optimizer"]["success_rate"],
        "optimizer_capped_mean_trials": capped_mean(result["raw_optimizer"], budget),
        "random_success_rate": result["random"]["success_rate"],
        "random_capped_mean_trials": capped_mean(result["raw_random"], budget),
        "response_surface_success_rate": sum(value is not None for value in response_runs) / len(response_runs),
        "response_surface_capped_mean_trials": capped_mean(response_runs, budget),
        "raw_optimizer": result["raw_optimizer"],
        "raw_random": result["raw_random"],
        "raw_response_surface": response_runs,
    }


def summarize(records: list[dict]) -> dict:
    completed = [record for record in records if record["status"] == "completed"]
    summary: dict[str, object] = {
        "scenario_count": len(records),
        "completed_scenario_count": len(completed),
        "categorical_context_isolation_passed": all(
            record.get("context_isolated", False) for record in completed
        ),
    }
    for method in ("optimizer", "random", "response_surface"):
        summary[f"{method}_mean_success_rate"] = statistics.mean(
            record[f"{method}_success_rate"] for record in completed
        )
        summary[f"{method}_mean_capped_trials"] = statistics.mean(
            record[f"{method}_capped_mean_trials"] for record in completed
        )
    summary["optimizer_faster_than_random_scenarios"] = sum(
        record["optimizer_capped_mean_trials"] < record["random_capped_mean_trials"]
        for record in completed
    )
    summary["optimizer_faster_than_response_surface_scenarios"] = sum(
        record["optimizer_capped_mean_trials"]
        < record["response_surface_capped_mean_trials"]
        for record in completed
    )
    efficiency_passed = len(completed) >= 6 and all(
        record["optimizer_success_rate"]
        >= max(record["random_success_rate"], record["response_surface_success_rate"])
        and record["optimizer_capped_mean_trials"]
        < min(
            record["random_capped_mean_trials"],
            record["response_surface_capped_mean_trials"],
        )
        for record in completed
    )
    summary["workflow_validation"] = "passed"
    summary["experiment_reduction_claim"] = (
        "passed-public-benchmark" if efficiency_passed else "not-demonstrated"
    )
    return summary


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--seeds", type=int, default=20)
    parser.add_argument("--budget", type=int, default=12)
    parser.add_argument("--max-scenarios", type=int, default=0)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if args.seeds < 1 or args.budget < 4:
        raise SystemExit("--seeds must be positive and --budget must be at least four")

    rows = load_rows()
    scenarios = group_scenarios(rows)
    if args.max_scenarios:
        scenarios = scenarios[: args.max_scenarios]
    records = [
        run_scenario(context, values, seeds=args.seeds, budget=args.budget)
        for context, values in scenarios
    ]
    payload = {
        "schema": "ingot-public-validation-v1",
        "source": {
            "url": SOURCE_URL,
            "doi": SOURCE_DOI,
            "license": LICENSE,
            "fixture_sha256": EXPECTED_SHA256,
            "fixture_rows": len(rows),
        },
        "method": {
            "categorical_factors": list(CONTEXT_FIELDS),
            "continuous_controls": list(CONTROL_FIELDS),
            "initial_observations": 3,
            "seeds": args.seeds,
            "budget": args.budget,
            "claim_boundary": (
                "Public data validates reproducibility, safety behavior, and method comparison; "
                "it does not prove savings for a different factory."
            ),
        },
        "records": records,
        "summary": summarize(records),
    }
    rendered = json.dumps(payload, ensure_ascii=False, indent=2)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(rendered + "\n", encoding="utf-8")
    print(rendered)


if __name__ == "__main__":
    main()
