#!/usr/bin/env python3
"""Run development-only regression on already inspected acceptance data."""
from __future__ import annotations

import argparse
import importlib.util
import json
from pathlib import Path
import sys

from ingot_optimizer.botorch_engine import MODEL_VERSION


VALIDATION_ROOT = Path(__file__).resolve().parent.parent
BENCHMARK_PATH = VALIDATION_ROOT / "benchmark_acceptance.py"


def load_benchmark():
    """Load the current evaluator without claiming frozen acceptance."""
    specification = importlib.util.spec_from_file_location(
        "ingot_optimizer_development", BENCHMARK_PATH
    )
    if specification is None or specification.loader is None:
        raise RuntimeError("unable to load the optimizer acceptance evaluator")
    benchmark = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(benchmark)
    return benchmark


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--episodes", type=int, default=25)
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
    units = []
    for dataset in benchmark.DATASETS:
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
            f"({args.episodes} development episodes)",
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
    payload = {
        "schema": "ingot-optimizer-development-regression",
        "evidence_status": "development-regression-only",
        "source_evidence": "inspected-fresh-data-acceptance",
        "candidate_model_version": MODEL_VERSION,
        "records": records,
        "summary": summary,
    }
    rendered = json.dumps(payload, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(rendered, encoding="utf-8")
    else:
        print(rendered, end="")


if __name__ == "__main__":
    main()
