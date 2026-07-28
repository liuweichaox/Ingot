#!/usr/bin/env python3
"""Create a bounded, reproducible Ingot benchmark from UCI hydraulic cycles.

The source data stays outside the repository.  This tool makes a small derived
replay set which exercises the same import contract used by historical factory
data: cycle boundaries, process samples, immutable context, and inspection
outcomes.  It never presents the derived inspection score as a real process
quality measurement; it is a labelled validation target only.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import uuid
import zipfile
from collections import Counter
from datetime import datetime, timedelta, timezone
from pathlib import Path


SOURCE_DATASET = "UCI Condition Monitoring of Hydraulic Systems (dataset 447)"
SOURCE_URL = "https://archive.ics.uci.edu/dataset/447/condition+monitoring+of+hydraulic+systems"
LICENSE = "CC BY 4.0"
EDGE_ID = "EDGE-DEMO-001"
MACHINE_ID = "UCI-HYDRAULIC-RIG-01"
PRODUCT_SERIES = "uci-hydraulic-benchmark"
PRODUCT_CODE = "hydraulic-condition-monitoring"
RECIPE_ID = "uci-hydraulic-load-cycle"


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--source-zip", type=Path, required=True,
                        help="Downloaded UCI dataset zip (not committed to this repository).")
    result.add_argument("--output", type=Path, required=True,
                        help="Directory for derived replay CSV files and mappings.")
    # Dataset 447 has ten fully nominal, stable rows. Keep the default below
    # that documented source constraint while still allowing a useful cohort.
    result.add_argument("--reference-count", type=int, default=8)
    result.add_argument("--degraded-count", type=int, default=12)
    result.add_argument("--replay-id", default="uci-hydraulic-447",
                        help="Stable prefix for generated cycle and workpiece ids.")
    result.add_argument("--start", default="2026-07-01T08:00:00Z",
                        help="Synthetic replay start in ISO-8601 UTC; not a source timestamp.")
    return result


def read_lines(archive: zipfile.ZipFile, suffix: str) -> list[str]:
    member = next((name for name in archive.namelist() if name.lower().endswith(suffix.lower())), None)
    if member is None:
        raise ValueError(f"Dataset zip does not contain {suffix}.")
    with archive.open(member) as source:
        return [line.decode("utf-8").strip() for line in source if line.strip()]


def values(row: str) -> list[float]:
    return [float(value) for value in row.replace("\t", " ").split()]


def profile(row: str) -> tuple[int, int, int, int, int]:
    parsed = [int(float(value)) for value in row.replace("\t", " ").split()]
    if len(parsed) != 5:
        raise ValueError(f"Expected five profile labels, found {len(parsed)}: {row!r}")
    return tuple(parsed)  # cooler, valve, pump leakage, accumulator, stable flag


def sample_at(signal: list[float], second: int) -> float:
    """Downsample a 60-second signal deterministically to its value at second."""
    index = min(len(signal) - 1, int(second * len(signal) / 60))
    return signal[index]


def condition(profile_values: tuple[int, int, int, int, int]) -> tuple[int, str]:
    cooler, valve, leakage, accumulator, stable = profile_values
    penalties = {
        "cooler": {100: 0, 20: 25, 3: 45}.get(cooler, 45),
        "valve": {100: 0, 90: 15, 80: 25, 73: 35}.get(valve, 35),
        "pump leakage": {0: 0, 1: 30, 2: 50}.get(leakage, 50),
        "accumulator": {130: 0, 115: 10, 100: 20, 90: 30}.get(accumulator, 30),
        "transient": 10 if stable else 0,
    }
    score = max(0, 100 - sum(penalties.values()))
    parts = [name for name, penalty in penalties.items() if penalty]
    label = "reference nominal condition" if not parts else "degraded: " + ", ".join(parts)
    return score, label


def stage(second: int) -> str:
    return "load_init" if second < 20 else "load_hold" if second < 40 else "load_release"


def iso(value: datetime) -> str:
    return value.astimezone(timezone.utc).isoformat().replace("+00:00", "Z")


def stable_uuid7(cycle_id: str, measured_at: datetime) -> str:
    """Make a replay-stable UUIDv7 for the inspection-record idempotency key."""
    timestamp_ms = int(measured_at.timestamp() * 1000)
    entropy = int.from_bytes(hashlib.sha256(cycle_id.encode("utf-8")).digest()[:10], "big")
    value = (timestamp_ms << 80) | (0x7 << 76) | ((entropy >> 68) & 0xFFF) << 64
    value |= (0b10 << 62) | (entropy & ((1 << 62) - 1))
    return str(uuid.UUID(int=value))


def mapping(event_type: str, include_values: bool) -> dict:
    result = {
        "edgeId": EDGE_ID,
        "eventType": {"value": event_type},
        "occurredAt": {"column": "occurred_at"},
        "subjectType": {"value": "asset"},
        "subjectId": {"value": MACHINE_ID},
        "correlationId": {"column": "cycle_id"},
        "context": {
            "product_series": {"value": PRODUCT_SERIES},
            "product_code": {"value": PRODUCT_CODE},
            "recipe_id": {"value": RECIPE_ID},
            "recipe_version": {"value": "1"},
            "workpiece_id": {"column": "workpiece_id"},
            "recipe_step": {"column": "recipe_step"},
            "source_dataset": {"value": "uci-hydraulic-447"},
            "benchmark_kind": {"value": "public-dataset-replay"},
        },
    }
    if include_values:
        result["values"] = {
            "pressure.ps1": {"column": "pressure_ps1", "type": "number"},
            "flow.fs1": {"column": "flow_fs1", "type": "number"},
            "temperature.ts1": {"column": "temperature_ts1", "type": "number"},
        }
    return result


def write_csv(path: Path, fields: list[str], rows: list[dict]) -> None:
    with path.open("w", newline="", encoding="utf-8") as target:
        writer = csv.DictWriter(target, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)


def main() -> None:
    args = parser().parse_args()
    if args.reference_count < 2 or args.degraded_count < 2:
        raise ValueError("At least two reference and two degraded cycles are required for comparison.")
    start = datetime.fromisoformat(args.start.replace("Z", "+00:00")).astimezone(timezone.utc)
    args.output.mkdir(parents=True, exist_ok=True)

    with zipfile.ZipFile(args.source_zip) as archive:
        profiles = [profile(row) for row in read_lines(archive, "profile.txt")]
        ps1 = [values(row) for row in read_lines(archive, "PS1.txt")]
        fs1 = [values(row) for row in read_lines(archive, "FS1.txt")]
        ts1 = [values(row) for row in read_lines(archive, "TS1.txt")]

    if not (len(profiles) == len(ps1) == len(fs1) == len(ts1)):
        raise ValueError("Source files have inconsistent cycle counts.")

    nominal = [index for index, item in enumerate(profiles) if item == (100, 100, 0, 130, 0)]
    degraded = [index for index, item in enumerate(profiles) if item[2] == 2 and item[4] == 0]
    if len(nominal) < args.reference_count or len(degraded) < args.degraded_count:
        raise ValueError(
            f"Insufficient labelled cycles: nominal={len(nominal)}, severe-leakage={len(degraded)}. "
            "The official dataset contents may have changed.")
    selected = [(index, "reference") for index in nominal[:args.reference_count]] + [
        (index, "degraded") for index in degraded[:args.degraded_count]
    ]

    boundaries: list[dict] = []
    samples: list[dict] = []
    inspections: list[dict] = []
    inspection_requests: list[dict] = []
    for ordinal, (source_index, cohort) in enumerate(selected, start=1):
        cycle_id = f"{args.replay_id}-{ordinal:03d}"
        workpiece_id = f"{args.replay_id}-sample-{ordinal:03d}"
        cycle_start = start + timedelta(minutes=ordinal * 2)
        cycle_end = cycle_start + timedelta(seconds=60)
        profile_values = profiles[source_index]
        score, label = condition(profile_values)
        boundaries.extend([
            {"cycle_id": cycle_id, "workpiece_id": workpiece_id, "occurred_at": iso(cycle_start), "recipe_step": "load_init"},
            {"cycle_id": cycle_id, "workpiece_id": workpiece_id, "occurred_at": iso(cycle_end), "recipe_step": "load_release"},
        ])
        for second in range(60):
            samples.append({
                "cycle_id": cycle_id,
                "workpiece_id": workpiece_id,
                "occurred_at": iso(cycle_start + timedelta(seconds=second)),
                "recipe_step": stage(second),
                "pressure_ps1": f"{sample_at(ps1[source_index], second):.6f}",
                "flow_fs1": f"{sample_at(fs1[source_index], second):.6f}",
                "temperature_ts1": f"{sample_at(ts1[source_index], second):.6f}",
            })
        inspections.append({
            "cycle_id": cycle_id,
            "workpiece_id": workpiece_id,
            "measured_at": iso(cycle_end + timedelta(seconds=5)),
            "condition_score": score,
            "condition_label": label,
            "cohort": cohort,
            "source_profile": "/".join(str(value) for value in profile_values),
        })
        inspection_requests.append({
            "recordId": stable_uuid7(cycle_id, cycle_end + timedelta(seconds=5)),
            "workpieceId": workpiece_id,
            "operationRunId": cycle_id,
            "definitionCode": "uci.hydraulic.condition",
            "definitionVersion": 1,
            "measuredAt": iso(cycle_end + timedelta(seconds=5)),
            "recordedAt": iso(cycle_end + timedelta(seconds=5)),
            "outcome": "PASS" if score >= 80 else "FAIL",
            "submittedBy": "public-dataset-replay",
            "measurements": [{
                "characteristicCode": "condition.score",
                "outcome": "PASS" if score >= 80 else "FAIL",
                "numericValue": score,
                "unit": "1",
            }],
            "attachments": [],
            "notes": (
                f"Public-dataset replay only. UCI profile {profile_values}; {label}. "
                "condition.score is a transparent derived validation target, not a measured product quality."),
        })

    boundary_fields = ["cycle_id", "workpiece_id", "occurred_at", "recipe_step"]
    write_csv(args.output / "cycle-started.csv", boundary_fields, boundaries[::2])
    write_csv(args.output / "cycle-completed.csv", boundary_fields, boundaries[1::2])
    write_csv(args.output / "process-samples.csv", [
        *boundary_fields, "pressure_ps1", "flow_fs1", "temperature_ts1"], samples)
    write_csv(args.output / "inspection-results.csv", [
        "cycle_id", "workpiece_id", "measured_at", "condition_score", "condition_label", "cohort", "source_profile"], inspections)
    (args.output / "inspection-requests.json").write_text(
        json.dumps(inspection_requests, indent=2), encoding="utf-8")
    (args.output / "cycle-started.mapping.json").write_text(json.dumps(mapping("cycle.started", False), indent=2), encoding="utf-8")
    (args.output / "process-samples.mapping.json").write_text(json.dumps(mapping("process.sample", True), indent=2), encoding="utf-8")
    (args.output / "cycle-completed.mapping.json").write_text(json.dumps(mapping("cycle.completed", False), indent=2), encoding="utf-8")
    manifest = {
        "source": {"name": SOURCE_DATASET, "url": SOURCE_URL, "license": LICENSE},
        "purpose": "Integration and usability validation only; not evidence of optimization effectiveness.",
        "synthetic_replay_time": True,
        "derived_quality_target": (
            "condition_score is a transparent score derived from source condition labels, "
            "not an observed manufacturing quality measurement."),
        "cycles": len(selected),
        "replay_id": args.replay_id,
        "process_samples": len(samples),
        "cohorts": dict(Counter(cohort for _, cohort in selected)),
        "profile_labels": {str(item[0] + 1): "/".join(map(str, profiles[item[0]])) for item in selected},
    }
    (args.output / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(json.dumps({"output": str(args.output), "cycles": len(selected), "samples": len(samples)}, indent=2))


if __name__ == "__main__":
    main()
