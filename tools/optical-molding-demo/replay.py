#!/usr/bin/env python3
"""Generate and optionally ingest a deterministic optical lens molding demo.

The replay is deliberately labelled as simulated.  It exercises the real
Ingot event and inspection contracts, but makes no claim about an actual mold
or an optimized industrial recipe.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import urllib.request
import uuid
from datetime import datetime, timedelta, timezone
from pathlib import Path

from demo_contract import platform_recipe_values


EDGE_ID = "EDGE-DEMO-001"
MACHINE_ID = "OPTICAL-MOLD-SIM-01"
PRODUCT_SERIES = "optical-lens-demo"
PRODUCT_CODE = "LENS-DEMO-50"
MOLD_ID = "MOLD-DEMO-A01"
RECIPE_ID = "lens-molding-demo"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True,
                        help="Directory for an auditable event and quality replay.")
    parser.add_argument("--api", help="Platform API base URL. Required with --token.")
    parser.add_argument("--token", help="Edge ingest token. Required with --api.")
    parser.add_argument("--replay-id", default="lens-molding-demo-20260728")
    return parser.parse_args()


def iso(value: datetime) -> str:
    return value.astimezone(timezone.utc).isoformat().replace("+00:00", "Z")


def stable_uuid7(identity: str, timestamp: datetime) -> str:
    timestamp_ms = int(timestamp.timestamp() * 1000)
    entropy = int.from_bytes(hashlib.sha256(identity.encode("utf-8")).digest()[:10], "big")
    raw = (timestamp_ms << 80) | (0x7 << 76) | ((entropy >> 68) & 0xFFF) << 64
    raw |= (0b10 << 62) | (entropy & ((1 << 62) - 1))
    return str(uuid.UUID(int=raw))


def event(
    identity: str,
    seq: int,
    event_type: str,
    occurred_at: datetime,
    cycle_id: str,
    context: dict[str, str],
    data: dict[str, object] | None = None,
) -> dict[str, object]:
    return {
        "eventId": stable_uuid7(identity, occurred_at),
        "eventType": event_type,
        "eventTypeVersion": 1,
        "occurredAt": iso(occurred_at),
        "recordedAt": iso(occurred_at),
        "source": f"edge/{EDGE_ID}/simulator/optical-lens-molding",
        "subject": {"type": "optical-molding-machine", "id": MACHINE_ID},
        "correlationId": cycle_id,
        "context": context,
        "data": data or {},
        "seq": seq,
    }


def recipe_parameters(version: int) -> dict[str, object]:
    return {
        item["code"]: item["value"]
        for item in platform_recipe_values(version)
    }


def process_values(cohort: str, seconds: int) -> dict[str, float | int]:
    phase = "preheat" if seconds < 100 else "molding" if seconds < 220 else "cooling"
    stage_number = 10 if phase == "preheat" else 20 if phase == "molding" else 30
    wave = math.sin(seconds / 17.0)
    if phase == "preheat":
        upper = 440.0 + seconds * 1.72 + 0.8 * wave
        lower = 438.0 + seconds * 1.70 + 0.7 * wave
        pressure = 80.0 + seconds
        grating_position = seconds * 0.002
        servo_position = seconds * 0.085
        servo_speed = 2.2
        vacuum = -72.0 + 0.7 * wave
        upper_output = 92.0
        lower_output = 90.0
    elif phase == "molding":
        upper = 620.0 + 0.7 * wave
        lower = 618.0 + 0.5 * wave
        pressure = 1200.0 + 18.0 * wave
        grating_position = 2.41 + 0.006 * wave
        servo_position = 8.5 + 0.02 * wave
        servo_speed = 0.35 + 0.02 * wave
        vacuum = -78.0 + 0.5 * wave
        upper_output = 58.0 + 2.0 * wave
        lower_output = 56.0 + 1.6 * wave
        if cohort == "heater_drift":
            # Same version-1 setpoint, but degraded physical response.  This is
            # intentionally a process observation, not an asserted root cause.
            upper = 611.3 + 0.9 * wave
            upper_output = 96.0 + 1.5 * wave
            pressure = 1170.0 + 25.0 * wave
        elif cohort == "verification":
            upper = 624.4 + 0.55 * wave
            upper_output = 66.0 + 1.5 * wave
    else:
        cooling_seconds = seconds - 220
        upper = 620.0 - cooling_seconds * 3.55 + 0.7 * wave
        lower = 618.0 - cooling_seconds * 3.50 + 0.6 * wave
        pressure = max(30.0, 1180.0 - cooling_seconds * 14.0)
        grating_position = 2.41 - cooling_seconds * 0.018
        servo_position = 8.5 + cooling_seconds * 0.20625
        servo_speed = 3.5
        vacuum = -74.0 + 0.8 * wave
        upper_output = max(0.0, 50.0 - cooling_seconds * 0.6)
        lower_output = max(0.0, 48.0 - cooling_seconds * 0.58)
        if cohort == "heater_drift":
            upper -= 5.5
            upper_output = min(100.0, upper_output + 22.0)
    upper_voltage = 220.0 + 0.8 * wave
    lower_voltage = 220.0 + 0.6 * wave
    upper_current = max(0.0, upper_output * 0.28)
    lower_current = max(0.0, lower_output * 0.27)
    return {
        "process.stage_number": stage_number,
        "mold.upper_infrared_temperature": round(upper, 4),
        "heater.upper_current": round(upper_current, 4),
        "heater.upper_voltage": round(upper_voltage, 4),
        "mold.lower_infrared_temperature": round(lower, 4),
        "heater.lower_current": round(lower_current, 4),
        "heater.lower_voltage": round(lower_voltage, 4),
        "molding.pressure_load": round(pressure, 4),
        "grating.position": round(grating_position, 4),
        "servo.speed": round(servo_speed, 4),
        "vacuum.pressure": round(vacuum, 4),
        "servo.position": round(servo_position, 4),
        "heater.upper_power": round(upper_voltage * upper_current, 4),
        "heater.lower_power": round(lower_voltage * lower_current, 4),
    }


def build_replay(replay_id: str) -> tuple[list[dict[str, object]], list[dict[str, object]], dict[str, object]]:
    now = datetime.now(timezone.utc).replace(microsecond=0)
    start = now - timedelta(minutes=24 * 7)
    events: list[dict[str, object]] = []
    inspections: list[dict[str, object]] = []
    sequence = 1
    groups = [("baseline", 1, 8), ("heater_drift", 1, 8), ("verification", 2, 8)]
    ordinal = 0
    for cohort, recipe_version, count in groups:
        for _ in range(count):
            ordinal += 1
            cycle_id = f"{replay_id}-{ordinal:03d}"
            workpiece_id = f"{replay_id}-lens-{ordinal:03d}"
            cycle_start = start + timedelta(minutes=ordinal * 7)
            cycle_end = cycle_start + timedelta(seconds=300)
            parameters = recipe_parameters(recipe_version)
            context = {
                "product_series": PRODUCT_SERIES,
                "product_code": PRODUCT_CODE,
                "recipe_id": RECIPE_ID,
                "recipe_version": str(recipe_version),
                "workpiece_id": workpiece_id,
                "machine_id": MACHINE_ID,
                "mold_id": MOLD_ID,
                "material_lot_ref": "GLASS-DEMO-01",
                "recipe_capture_source": "simulated_device_readback",
                "demo_replay": "true",
                "demo_cohort": cohort,
            }
            events.append(event(
                f"{cycle_id}:recipe", sequence, "recipe.applied", cycle_start, cycle_id, context,
                {"recipeId": RECIPE_ID, "recipeVersion": recipe_version,
                 "recipeName": "LENS-DEMO 基线模压配方" if recipe_version == 1 else "LENS-DEMO 验证配方（上模温度调整）",
                 "resolvedParameters": parameters},
            ))
            sequence += 1
            events.append(event(f"{cycle_id}:start", sequence, "cycle.started", cycle_start, cycle_id,
                                {**context, "stage_number": "10", "process_stage_name": "预热"}))
            sequence += 1
            for seconds in range(0, 301, 10):
                step = "preheat" if seconds < 100 else "molding" if seconds < 220 else "cooling"
                stage_number = "10" if step == "preheat" else "20" if step == "molding" else "30"
                stage_name = {"preheat": "预热", "molding": "模压保压", "cooling": "冷却脱模"}[step]
                events.append(event(
                    f"{cycle_id}:sample:{seconds}", sequence, "process.sample",
                    cycle_start + timedelta(seconds=seconds), cycle_id,
                    {**context, "stage_number": stage_number, "process_stage_name": stage_name},
                    {"values": process_values(cohort, seconds)},
                ))
                sequence += 1
            events.append(event(f"{cycle_id}:completed", sequence, "cycle.completed", cycle_end, cycle_id,
                                {**context, "stage_number": "30", "process_stage_name": "冷却脱模"}))
            sequence += 1
            failed = cohort == "heater_drift"
            thickness = 0.026 if failed else (0.004 if cohort == "baseline" else 0.003)
            form_error = 0.23 if failed else (0.072 if cohort == "baseline" else 0.061)
            outcome = "FAIL" if failed else "PASS"
            measured_at = cycle_end + timedelta(seconds=30)
            inspections.append({
                "recordId": stable_uuid7(f"{cycle_id}:inspection", measured_at),
                "workpieceId": workpiece_id,
                "operationRunId": cycle_id,
                "definitionCode": "lens.molding.quality",
                "definitionVersion": 1,
                "measuredAt": iso(measured_at),
                "recordedAt": iso(measured_at),
                "outcome": outcome,
                "submittedBy": "optical-molding-simulator",
                "measurements": [
                    {"characteristicCode": "lens.center_thickness_deviation", "outcome": outcome,
                     "numericValue": thickness, "unit": "mm"},
                    {"characteristicCode": "lens.surface_form_error", "outcome": outcome,
                     "numericValue": form_error, "unit": "um"},
                ],
                "attachments": [],
                "notes": (
                    "模拟案例：基线正常、同配方热响应异常、补偿后验证。"
                    "仅用于演示追因与验证闭环，不代表真实工艺结论。"
                ),
            })
    manifest = {
        "kind": "simulated-optical-lens-molding-replay",
        "replay_id": replay_id,
        "purpose": "UI and end-to-end workflow validation only; not a real process result.",
        "cycles": 24,
        "samples_per_cycle": 31,
        "cohorts": {"baseline": 8, "heater_drift": 8, "verification": 8},
        "known_simulation_condition": (
            "For the heater_drift cohort, the v1 recipe readback remains unchanged while "
            "upper-mold temperature is deliberately low and heater output high."
        ),
    }
    return events, inspections, manifest


def post_json(url: str, token: str | None, payload: object) -> object:
    request = urllib.request.Request(
        url,
        data=json.dumps(payload).encode("utf-8"),
        headers={
            "Content-Type": "application/json",
            **({"Authorization": f"Bearer {token}"} if token else {}),
        },
        method="POST",
    )
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.loads(response.read().decode("utf-8"))


def main() -> None:
    args = parse_args()
    if bool(args.api) != bool(args.token):
        raise SystemExit("--api and --token must be supplied together.")
    events, inspections, manifest = build_replay(args.replay_id)
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "events.json").write_text(json.dumps(events, indent=2), encoding="utf-8")
    (args.output / "inspection-records.json").write_text(json.dumps(inspections, indent=2), encoding="utf-8")
    (args.output / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    if args.api:
        api = args.api.rstrip("/")
        accepted = duplicates = 0
        for index in range(0, len(events), 500):
            result = post_json(f"{api}/api/v1/events:batch", args.token, {
                "edgeId": EDGE_ID, "events": events[index:index + 500],
            })
            accepted += result.get("accepted", 0)
            duplicates += result.get("duplicates", 0)
        for record in inspections:
            post_json(f"{api}/api/v1/inspection-records", None, record)
        manifest["ingest"] = {"accepted_events": accepted, "duplicate_events": duplicates,
                              "inspection_records": len(inspections)}
        (args.output / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(json.dumps(manifest, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
