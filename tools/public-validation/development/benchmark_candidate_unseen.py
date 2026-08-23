#!/usr/bin/env python3
"""Run candidate policies on the inspected unseen-acceptance evidence."""
from __future__ import annotations

import argparse
import importlib.util
import json
from pathlib import Path
import sys

from ingot_optimizer.botorch_engine import MODEL_VERSION


VALIDATION_ROOT = Path(__file__).resolve().parent.parent
BENCHMARK_PATH = VALIDATION_ROOT / "benchmark_unseen.py"


def load_benchmark():
    specification = importlib.util.spec_from_file_location(
        "ingot_unseen_acceptance_development", BENCHMARK_PATH
    )
    if specification is None or specification.loader is None:
        raise RuntimeError("unable to load the unseen-data acceptance evaluator")
    benchmark = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(benchmark)
    return benchmark


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--episodes", type=int, default=10)
    parser.add_argument(
        "--dataset",
        choices=(
            "all",
            "fullerenes",
            "suzuki",
        ),
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
    benchmark.DATASETS = {
        dataset: benchmark.DATASETS[dataset] for dataset in datasets
    }
    units = []
    for dataset in datasets:
        rows = benchmark.load_rows(dataset, protocol)
        units.extend(
            (dataset, unit_id, context, unit_rows)
            for unit_id, context, unit_rows in benchmark.group_evaluation_units(
                dataset, rows, protocol
            )
        )
    records = []
    for index, (dataset, unit_id, context, rows) in enumerate(units):
        print(
            f"[{index + 1}/{len(units)}] running {dataset}:{unit_id} "
            f"({args.episodes} paired episodes)",
            file=sys.stderr,
            flush=True,
        )
        records.append(
            benchmark.run_unit(
                dataset,
                unit_id,
                context,
                rows,
                unit_index=index,
                protocol=protocol,
                episode_count=args.episodes,
            )
        )
    summary = benchmark.summarize(records, protocol)
    summary["core_experiment_selection"] = (
        "passed-development-regression"
        if all(
            effect["passed"]
            for effect in summary["paired_effects"]
            if effect["comparator"] != benchmark.ABLATION
        )
        and summary["response_surface_added_value"]
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
        "schema": "ingot-unseen-acceptance-candidate-development",
        "evidence_status": "development-regression-only",
        "source_protocol": "failed-unseen-acceptance-now-inspected",
        "candidate_model_version": MODEL_VERSION,
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
