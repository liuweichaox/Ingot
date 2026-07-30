#!/usr/bin/env python3
"""Submit simulated quality records for completed source-driven lens-molding runs.

Quality is deliberately submitted through the platform's inspection contract rather
than by the device data source. The join is the immutable operationRunId/run ID.
"""

from __future__ import annotations

import argparse
import json
import os
import uuid
import urllib.parse
import urllib.request
from datetime import datetime, timezone


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api", default="http://127.0.0.1:8000")
    parser.add_argument("--run-prefix", default="lens-source-demo")
    parser.add_argument("--maximum-run", type=int, default=24)
    parser.add_argument("--project-id")
    parser.add_argument("--experiment-id")
    return parser.parse_args()


def request(url: str, payload: object | None = None) -> object:
    body = None if payload is None else json.dumps(payload).encode("utf-8")
    call = urllib.request.Request(url, data=body, method="POST" if body else "GET")
    if body:
        call.add_header("Content-Type", "application/json")
    with urllib.request.urlopen(call, timeout=30) as response:
        return json.loads(response.read().decode("utf-8"))


def uuid7() -> str:
    """Create a sortable UUIDv7 without requiring a third-party dependency."""
    timestamp_ms = int(datetime.now(timezone.utc).timestamp() * 1000)
    entropy = int.from_bytes(os.urandom(10), "big")
    raw = (timestamp_ms << 80) | (0x7 << 76) | ((entropy >> 68) & 0xFFF) << 64
    raw |= (0b10 << 62) | (entropy & ((1 << 62) - 1))
    return str(uuid.UUID(int=raw))


def load_experiment_runs(api: str, project_id: str, experiment_id: str) -> dict[str, float]:
    workspace = request(f"{api}/api/v1/research-projects/{project_id}")
    experiment = next(
        (
            item
            for item in workspace.get("experiments", [])
            if item.get("experimentId") == experiment_id
        ),
        None,
    )
    if experiment is None:
        raise ValueError(f"experiment {experiment_id} was not found in project")
    runs: dict[str, float] = {}
    for run in experiment.get("runPlan", []):
        factor = next(
            (
                item
                for item in run.get("factors", [])
                if item.get("variableCode") == "recipe.upper_heat_compensation"
            ),
            None,
        )
        if factor is None:
            raise ValueError(f"run {run.get('runKey')} has no compensation factor")
        runs[run["runKey"]] = float(factor["value"])
    return runs


def main() -> None:
    args = parse_args()
    api = args.api.rstrip("/")
    if bool(args.project_id) != bool(args.experiment_id):
        raise ValueError("--project-id and --experiment-id must be supplied together")
    experiment_runs = (
        load_experiment_runs(api, args.project_id, args.experiment_id)
        if args.experiment_id
        else None
    )
    if experiment_runs:
        cycles = []
        for run_id in experiment_runs:
            query = urllib.parse.urlencode({"correlationId": run_id, "limit": "1"})
            matches = request(f"{api}/api/v1/cycles?{query}").get("data", [])
            if matches and matches[0].get("status") == "completed":
                cycles.append(matches[0])
    else:
        query = urllib.parse.urlencode(
            {"status": "completed", "limit": "200", "search": args.run_prefix}
        )
        cycles = request(f"{api}/api/v1/cycles?{query}").get("data", [])
    submitted = 0
    for cycle in cycles:
        if cycle.get("inspectionCount", 0) > 0:
            continue
        run_id = cycle["correlationId"]
        if experiment_runs:
            compensation = experiment_runs[run_id]
            center_deviation = round(max(0.0035, 0.026 - 0.0044 * compensation), 6)
            surface_form_error = round(max(0.065, 0.23 - 0.031 * compensation), 6)
            failed = center_deviation > 0.015 or surface_form_error > 0.15
        else:
            try:
                ordinal = int(run_id.rsplit("-", 1)[1])
            except (ValueError, IndexError):
                continue
            if ordinal > args.maximum_run:
                continue
            failed = 9 <= ordinal <= 16
            center_deviation = 0.026 if failed else 0.004
            surface_form_error = 0.23 if failed else 0.072
        outcome = "FAIL" if failed else "PASS"
        payload = {
            "recordId": uuid7(),
            "workpieceId": cycle["workpieceId"],
            "operationRunId": run_id,
            "definitionCode": "lens.molding.quality",
            "definitionVersion": 1,
            "measuredAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
            "recordedAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
            "outcome": outcome,
            "submittedBy": "simulated-inspection-station",
            "measurements": [
                {"characteristicCode": "lens.center_thickness_deviation", "outcome": outcome, "numericValue": center_deviation, "unit": "mm"},
                {"characteristicCode": "lens.surface_form_error", "outcome": outcome, "numericValue": surface_form_error, "unit": "um"},
            ],
            "attachments": [],
            "notes": (
                "模拟检验站按优化实验运行标识回传实测结果。"
                if experiment_runs
                else "模拟检验站按运行号回传。仅用于验证配方、曲线、周期和质检的关联契约。"
            ),
        }
        request(f"{api}/api/v1/inspection-records", payload)
        submitted += 1
    print(json.dumps({"completed_runs": len(cycles), "submitted_quality_records": submitted}, ensure_ascii=False))


if __name__ == "__main__":
    main()
