#!/usr/bin/env python3
"""Run candidate policies on the inspected v4 development evidence."""
from __future__ import annotations

import argparse
import importlib.util
import json
from pathlib import Path


VALIDATION_ROOT = Path(__file__).resolve().parent.parent
BENCHMARK_PATH = VALIDATION_ROOT / "benchmark_v4.py"


def load_benchmark():
    specification = importlib.util.spec_from_file_location(
        "ingot_public_validation_v4_development", BENCHMARK_PATH
    )
    if specification is None or specification.loader is None:
        raise RuntimeError("unable to load the v4 public-validation evaluator")
    benchmark = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(benchmark)
    return benchmark


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--episodes", type=int, default=10)
    parser.add_argument(
        "--dataset",
        choices=("all", "energy_efficiency", "synchronous_machine"),
        default="all",
    )
    parser.add_argument("--bootstrap-samples", type=int, default=1000)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if args.episodes < 1:
        raise SystemExit("--episodes must be positive")
    if args.bootstrap_samples < 100:
        raise SystemExit("--bootstrap-samples must be at least 100")

    benchmark = load_benchmark()
    protocol = benchmark.load_protocol()
    protocol["statistics"]["bootstrap_samples"] = args.bootstrap_samples
    datasets = (
        list(benchmark.DATASETS)
        if args.dataset == "all"
        else [args.dataset]
    )
    units = []
    for dataset in datasets:
        rows = benchmark.load_rows(dataset, protocol)
        units.extend(
            (dataset, unit_id, context, unit_rows)
            for unit_id, context, unit_rows in benchmark.group_evaluation_units(
                dataset, rows, protocol
            )
        )
    records = [
        benchmark.run_unit(
            dataset,
            unit_id,
            context,
            rows,
            unit_index=index,
            protocol=protocol,
            episode_count=args.episodes,
        )
        for index, (dataset, unit_id, context, rows) in enumerate(units)
    ]
    summary = benchmark.summarize(records, protocol)
    summary["experiment_reduction_vs_all_preregistered_baselines"] = (
        "passed-development-regression"
        if all(
            effect["passed"]
            for effect in summary["paired_effects"]
            if effect["comparator"] != benchmark.ABLATION
        )
        else "not-demonstrated"
    )
    ablation = next(
        effect
        for effect in summary["paired_effects"]
        if effect["comparator"] == benchmark.ABLATION
    )
    summary["mechanism_feature_contribution"] = (
        "passed-development-regression"
        if ablation["passed"]
        else "not-demonstrated"
    )
    payload = {
        "schema": "ingot-public-validation-candidate-development-v4",
        "evidence_status": "development-regression-only",
        "source_protocol": "v4-inspected-not-external",
        "records": records,
        "summary": summary,
    }
    rendered = json.dumps(payload, ensure_ascii=False, indent=2)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(rendered + "\n", encoding="utf-8")
        print(
            json.dumps(
                {"output": str(args.output), "summary": summary},
                ensure_ascii=False,
                indent=2,
            )
        )
    else:
        print(rendered)


if __name__ == "__main__":
    main()
