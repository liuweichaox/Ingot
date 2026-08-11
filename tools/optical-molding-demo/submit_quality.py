#!/usr/bin/env python3
"""Submit simulated quality records for completed source-driven lens-molding runs.

Quality is deliberately submitted through the platform's inspection contract rather
than by the device data source. The join is the immutable executionId/run ID.
"""

from __future__ import annotations

import argparse
import json
import os
import uuid
import urllib.parse
import urllib.error
import urllib.request
from datetime import datetime, timezone


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api", default="http://127.0.0.1:8000")
    parser.add_argument("--machine-id", default="OPTICAL-MOLD-SIM-01")
    parser.add_argument(
        "--operation-run-id",
        help="Submit the inspection for one immutable operation run, as a real station would.",
    )
    parser.add_argument("--maximum-run", type=int, default=24)
    parser.add_argument("--project-id")
    parser.add_argument("--experiment-id")
    return parser.parse_args()


def request(url: str, payload: object | None = None) -> object:
    body = None if payload is None else json.dumps(payload).encode("utf-8")
    call = urllib.request.Request(url, data=body, method="POST" if body else "GET")
    if body:
        call.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(call, timeout=30) as response:
            return json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", errors="replace")
        raise RuntimeError(
            f"platform request failed with HTTP {error.code}: {detail}"
        ) from error


def uuid7() -> str:
    """Create a sortable UUIDv7 without requiring a third-party dependency."""
    timestamp_ms = int(datetime.now(timezone.utc).timestamp() * 1000)
    entropy = int.from_bytes(os.urandom(10), "big")
    raw = (timestamp_ms << 80) | (0x7 << 76) | ((entropy >> 68) & 0xFFF) << 64
    raw |= (0b10 << 62) | (entropy & ((1 << 62) - 1))
    return str(uuid.UUID(int=raw))


def read_source_execution_number(detail: dict[str, object]) -> int | None:
    for item in detail.get("events", []):
        context = item.get("event", {}).get("context", {})
        raw = context.get("source_execution_no")
        if raw is None:
            continue
        try:
            return int(raw)
        except (TypeError, ValueError):
            return None
    return None


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
                if item.get("variableCode") == "recipe.upper_temperature_setpoint"
            ),
            None,
        )
        if factor is None:
            raise ValueError(f"run {run.get('executionKey')} has no upper-temperature setpoint")
        runs[run["executionKey"]] = float(factor["value"])
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
        executions = []
        for run_id in experiment_runs:
            query = urllib.parse.urlencode({"executionId": run_id, "limit": "1"})
            matches = request(f"{api}/api/v1/process-executions?{query}").get("data", [])
            if matches and matches[0].get("status") == "completed":
                executions.append(matches[0])
    elif args.execution_id:
        query = urllib.parse.urlencode(
            {"executionId": args.execution_id, "limit": "1"}
        )
        executions = request(f"{api}/api/v1/process-executions?{query}").get("data", [])
    else:
        query = urllib.parse.urlencode(
            {"status": "completed", "limit": "200", "equipmentId": args.equipment_id}
        )
        executions = request(f"{api}/api/v1/process-executions?{query}").get("data", [])
    submitted = 0
    for execution in executions:
        if execution.get("inspectionCount", 0) > 0:
            continue
        run_id = execution["executionId"]
        if experiment_runs:
            setpoint = experiment_runs[run_id]
            adjustment = max(0.0, min(6.0, setpoint - 620.0))
            center_deviation = round(max(0.0035, 0.026 - 0.0044 * adjustment), 6)
            surface_form_error = round(max(0.065, 0.23 - 0.031 * adjustment), 6)
            failed = center_deviation > 0.015 or surface_form_error > 0.15
        else:
            detail = request(f"{api}/api/v1/process-executions/{urllib.parse.quote(run_id)}")
            ordinal = read_source_execution_number(detail)
            if ordinal is None:
                continue
            if ordinal > args.maximum_run:
                continue
            failed = 9 <= ordinal <= 16
            center_deviation = 0.026 if failed else 0.004
            surface_form_error = 0.23 if failed else 0.072
        outcome = "FAIL" if failed else "PASS"
        payload = {
            "recordId": uuid7(),
            "outputItemId": execution["outputItemId"],
            "executionId": run_id,
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
    print(json.dumps({"completed_runs": len(executions), "submitted_quality_records": submitted}, ensure_ascii=False))


if __name__ == "__main__":
    main()
