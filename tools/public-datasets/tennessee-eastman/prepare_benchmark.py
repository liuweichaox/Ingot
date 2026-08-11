#!/usr/bin/env python3
"""Turn one Tennessee Eastman fault trace into Ingot experiment-window replay data.

The output is deliberately an architectural test fixture: it preserves measured
variables and manipulated-variable traces, while the PASS/FAIL result comes
only from the benchmark's documented fault injection time. It must never be
interpreted as a manufacturing-quality measurement or as an optimizer result.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import uuid
from datetime import datetime, timedelta, timezone
from pathlib import Path

import openpyxl


EDGE_ID = "EDGE-DEMO-001"
MACHINE_ID = "TE-SIMULATOR-01"
PRODUCT_SERIES = "tennessee-eastman-benchmark"
PRODUCT_CODE = "tennessee-eastman-mode-1"
PROCESS_SPECIFICATION_ID = "te-mode-1-window"

SIGNALS = {
    "process.a_feed": ("XMEAS-1", "A feed", "kscmh"),
    "process.reactor_pressure": ("XMEAS-7", "Reactor pressure", "kPa"),
    "process.reactor_temperature": ("XMEAS-9", "Reactor temperature", "degC"),
    "process.separator_level": ("XMEAS-12", "Separator level", "%"),
    "control.a_feed": ("XMV-3", "A feed control", "%"),
    "control.ac_feed": ("XMV-4", "A/C feed control", "%"),
    "control.reactor_cooling": ("XMV-10", "Reactor cooling-water control", "%"),
}


def stable_uuid7(key: str, at: datetime) -> str:
    milliseconds = int(at.timestamp() * 1000)
    entropy = int.from_bytes(hashlib.sha256(key.encode("utf-8")).digest()[:10], "big")
    value = (milliseconds << 80) | (0x7 << 76) | ((entropy >> 68) & 0xFFF) << 64
    value |= (0b10 << 62) | (entropy & ((1 << 62) - 1))
    return str(uuid.UUID(int=value))


def iso(value: datetime) -> str:
    return value.astimezone(timezone.utc).isoformat().replace("+00:00", "Z")


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True, help="TE fault trace .xlsx file.")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--fault-code", type=int, required=True, help="Published Tennessee Eastman fault code.")
    parser.add_argument("--fault-start-hours", type=float, default=8,
                        help="Documented fault injection hour for this trace.")
    parser.add_argument("--reference-windows", type=int, default=4)
    parser.add_argument("--fault-windows", type=int, default=4)
    parser.add_argument("--replay-id", default="te-mode1-fault")
    parser.add_argument("--start", default="2026-07-10T08:00:00Z")
    return parser.parse_args()


def mapping(event_type: str, with_values: bool) -> dict:
    result = {
        "edgeId": EDGE_ID,
        "eventType": {"value": event_type},
        "occurredAt": {"column": "occurred_at"},
        "subjectType": {"value": "simulator"},
        "subjectId": {"value": MACHINE_ID},
        "executionId": {"column": "execution_id"},
        "context": {
            "product_family_code": {"value": PRODUCT_SERIES},
            "product_code": {"value": PRODUCT_CODE},
            "process_specification_id": {"value": PROCESS_SPECIFICATION_ID},
            "process_specification_version": {"value": "1"},
            "output_item_id": {"column": "output_item_id"},
            "stage_number": {"value": "analysis_window"},
            "benchmark_kind": {"value": "tennessee-eastman-replay"},
            "fault_code": {"column": "fault_code"},
        },
    }
    if with_values:
        result["values"] = {
            code: {"column": code.replace(".", "_"), "type": "number"}
            for code in SIGNALS
        }
    return result


def write_csv(path: Path, fields: list[str], rows: list[dict]) -> None:
    with path.open("w", newline="", encoding="utf-8") as target:
        writer = csv.DictWriter(target, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)


def main() -> None:
    args = parse_arguments()
    if args.reference_windows < 2 or args.fault_windows < 2:
        raise ValueError("At least two reference and two fault windows are required.")
    start = datetime.fromisoformat(args.start.replace("Z", "+00:00")).astimezone(timezone.utc)
    args.output.mkdir(parents=True, exist_ok=True)
    workbook = openpyxl.load_workbook(args.source, read_only=True, data_only=True)
    sheet = workbook.active
    rows = sheet.iter_rows(values_only=True)
    header = next(rows)
    positions = {str(name).strip().upper(): index for index, name in enumerate(header) if name is not None}
    time_index = next((index for name, index in positions.items() if name.startswith("TIME")), None)
    if time_index is None or any(source not in positions for source, _, _ in SIGNALS.values()):
        raise ValueError("Source must contain Time, XMEAS-1/7/9/12 and XMV-3/4/10 columns.")

    selected_hours = list(range(int(args.fault_start_hours) - args.reference_windows, int(args.fault_start_hours)))
    selected_hours += list(range(int(args.fault_start_hours), int(args.fault_start_hours) + args.fault_windows))
    grouped: dict[int, list[tuple[float, tuple]]] = {hour: [] for hour in selected_hours}
    for row in rows:
        source_hour = float(row[time_index])
        hour = int(source_hour)
        if hour in grouped:
            grouped[hour].append((source_hour, row))
    if any(not values for values in grouped.values()):
        missing = [hour for hour, values in grouped.items() if not values]
        raise ValueError(f"Requested replay windows are absent from source: {missing}")

    boundaries: list[dict] = []
    samples: list[dict] = []
    inspections: list[dict] = []
    requests: list[dict] = []
    for ordinal, source_hour in enumerate(selected_hours, start=1):
        execution_id = f"{args.replay_id}-{args.fault_code:02d}-{ordinal:03d}"
        output_item_id = f"{args.replay_id}-sample-{ordinal:03d}"
        execution_start = start + timedelta(hours=ordinal)
        execution_end = execution_start + timedelta(hours=1)
        failed = source_hour >= args.fault_start_hours
        boundaries.extend([
            {"execution_id": execution_id, "output_item_id": output_item_id, "occurred_at": iso(execution_start), "fault_code": args.fault_code},
            {"execution_id": execution_id, "output_item_id": output_item_id, "occurred_at": iso(execution_end), "fault_code": args.fault_code},
        ])
        for source_time, row in grouped[source_hour]:
            sample_time = execution_start + timedelta(hours=source_time - source_hour)
            item = {
                "execution_id": execution_id, "output_item_id": output_item_id,
                "occurred_at": iso(sample_time), "fault_code": args.fault_code,
            }
            for code, (source, _, _) in SIGNALS.items():
                item[code.replace(".", "_")] = f"{float(row[positions[source]]):.8f}"
            samples.append(item)
        score = 40 if failed else 100
        note = (
            f"Tennessee Eastman replay. Fault {args.fault_code}; source hour {source_hour}. "
            "Outcome derives solely from the benchmark's documented injection time; it is not measured product quality.")
        requests.append({
            "recordId": stable_uuid7(execution_id, execution_end + timedelta(seconds=5)),
            "outputItemId": output_item_id, "executionId": execution_id,
            "definitionCode": "te.process.stability", "definitionVersion": 1,
            "measuredAt": iso(execution_end + timedelta(seconds=5)),
            "recordedAt": iso(execution_end + timedelta(seconds=5)),
            "outcome": "FAIL" if failed else "PASS", "submittedBy": "public-dataset-replay",
            "measurements": [{"characteristicCode": "stability.score", "outcome": "FAIL" if failed else "PASS", "numericValue": score, "unit": "1"}],
            "attachments": [], "notes": note,
        })
        inspections.append({"execution_id": execution_id, "output_item_id": output_item_id, "source_hour": source_hour,
                            "fault_code": args.fault_code, "stability_score": score,
                            "outcome": "FAIL" if failed else "PASS"})

    boundary_fields = ["execution_id", "output_item_id", "occurred_at", "fault_code"]
    sample_fields = [*boundary_fields, *[code.replace(".", "_") for code in SIGNALS]]
    write_csv(args.output / "execution-started.csv", boundary_fields, boundaries[::2])
    write_csv(args.output / "execution-completed.csv", boundary_fields, boundaries[1::2])
    write_csv(args.output / "process-samples.csv", sample_fields, samples)
    write_csv(args.output / "inspection-results.csv", ["execution_id", "output_item_id", "source_hour", "fault_code", "stability_score", "outcome"], inspections)
    (args.output / "inspection-requests.json").write_text(json.dumps(requests, indent=2), encoding="utf-8")
    (args.output / "execution-started.mapping.json").write_text(json.dumps(mapping("process.execution.started", False), indent=2), encoding="utf-8")
    (args.output / "process-samples.mapping.json").write_text(json.dumps(mapping("process.sample", True), indent=2), encoding="utf-8")
    (args.output / "execution-completed.mapping.json").write_text(json.dumps(mapping("process.execution.completed", False), indent=2), encoding="utf-8")
    manifest = {
        "source": str(args.source), "fault_code": args.fault_code,
        "fault_start_hours": args.fault_start_hours, "executions": len(selected_hours), "samples": len(samples),
        "purpose": "Architecture replay: controlled-variable traces, process data, known injection boundary and quality linkage.",
        "not_valid_for": "Claims about optical molding behavior or Bayesian-optimization effectiveness.",
    }
    (args.output / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(json.dumps({"executions": len(selected_hours), "samples": len(samples), "output": str(args.output)}, indent=2))


if __name__ == "__main__":
    main()
