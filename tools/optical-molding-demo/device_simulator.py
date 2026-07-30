#!/usr/bin/env python3
"""Expose deterministic optical-lens molding snapshots through a generic HTTP source.

This is a local, simulated data source for validating Ingot's versioned acquisition
contract. It is intentionally protocol-neutral: a real PLC, OPC UA server, MQTT
publisher, or MES adapter only needs to supply equivalent fields.
"""

from __future__ import annotations

import argparse
import json
import math
import time
import urllib.request
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8102)
    parser.add_argument("--cycle-seconds", type=float, default=8.0)
    parser.add_argument("--run-prefix", default="lens-source-demo")
    parser.add_argument("--max-runs", type=int, default=24)
    parser.add_argument("--api", default="http://127.0.0.1:8000")
    parser.add_argument("--project-id")
    parser.add_argument("--experiment-id")
    return parser.parse_args()


def load_experiment_plan(args: argparse.Namespace) -> list[dict[str, object]] | None:
    if bool(args.project_id) != bool(args.experiment_id):
        raise ValueError("--project-id and --experiment-id must be supplied together")
    if not args.experiment_id:
        return None
    url = f"{args.api.rstrip('/')}/api/v1/research-projects/{args.project_id}"
    with urllib.request.urlopen(url, timeout=30) as response:
        workspace = json.loads(response.read().decode("utf-8"))
    experiment = next(
        (
            item
            for item in workspace.get("experiments", [])
            if item.get("experimentId") == args.experiment_id
        ),
        None,
    )
    if experiment is None:
        raise ValueError(f"experiment {args.experiment_id} was not found in project")
    plan = []
    for run in experiment.get("runPlan", []):
        factors = {
            item["variableCode"]: float(item["value"])
            for item in run.get("factors", [])
        }
        if "recipe.upper_heat_compensation" not in factors:
            raise ValueError(
                f"run {run.get('runKey')} has no recipe.upper_heat_compensation factor"
            )
        plan.append(
            {
                "run_id": run["runKey"],
                "compensation": factors["recipe.upper_heat_compensation"],
            }
        )
    if len(plan) < 2:
        raise ValueError("an executable optimization experiment needs at least two runs")
    return plan


def recipe(
    version: int,
    compensation: float | None = None,
    experimental: bool = False,
) -> dict[str, object]:
    compensation = (0.0 if version == 1 else 5.0) if compensation is None else compensation
    return {
        "id": "lens-molding-demo",
        "version": version,
        "name": (
            "LENS-DEMO 优化器建议配方"
            if experimental
            else "LENS-DEMO 基线模压配方"
            if version == 1
            else "LENS-DEMO 验证配方（上模补偿）"
        ),
        "parameters": {
            "upperTemperatureTarget": 620.0,
            "lowerTemperatureTarget": 618.0,
            "pressureTarget": 1.20,
            "dwellSeconds": 120,
            "upperHeatCompensation": round(compensation, 6),
        },
    }


def values(
    cohort: str,
    progress: float,
    compensation: float | None = None,
) -> dict[str, float]:
    phase = "preheat" if progress < 1 / 3 else "molding" if progress < 2 / 3 else "cooling"
    wave = math.sin(progress * math.pi * 9)
    if phase == "preheat":
        upper = 440 + progress * 3 * 180 + wave
        lower = 438 + progress * 3 * 180 + wave * 0.8
        pressure, displacement, vacuum, output = 0.10, progress * 3 * 0.8, -72 + wave, 92.0
    elif phase == "molding":
        upper, lower = 620 + wave * 0.7, 618 + wave * 0.5
        pressure, displacement, vacuum, output = 1.20 + wave * 0.02, 2.41 + wave * 0.006, -78 + wave * 0.4, 58 + wave * 2
        if cohort == "heater_drift":
            upper, pressure, output = 611.3 + wave * 0.9, 1.17 + wave * 0.025, 96 + wave * 1.5
        elif cohort == "verification":
            upper, output = 619.4 + wave * 0.55, 66 + wave * 1.5
        elif cohort == "experiment":
            applied = max(0.0, min(6.0, compensation or 0.0))
            upper = min(620.2, 611.3 + applied * 1.62) + wave * 0.65
            pressure = 1.17 + applied * 0.006 + wave * 0.02
            output = max(58.0, 96.0 - applied * 6.0) + wave * 1.5
    else:
        cooling = (progress - 2 / 3) * 3
        upper, lower = 620 - cooling * 180 + wave * 0.7, 618 - cooling * 178 + wave * 0.5
        pressure, displacement, vacuum, output = max(0.03, 1.18 - cooling), 2.41 - cooling * 1.8, -74 + wave * 0.8, max(0, 50 - cooling * 35)
        if cohort == "heater_drift":
            upper, output = upper - 5.5, min(100, output + 22)
        elif cohort == "experiment":
            applied = max(0.0, min(6.0, compensation or 0.0))
            remaining_deficit = max(0.0, 5.5 * (1.0 - applied / 5.0))
            upper, output = upper - remaining_deficit, min(100, output + remaining_deficit * 4)
    return {
        "mold": {"upperTemperature": round(upper, 4), "lowerTemperature": round(lower, 4), "displacement": round(displacement, 4)},
        "molding": {"pressure": round(pressure, 4)},
        "vacuum": {"pressure": round(vacuum, 4)},
        "heater": {"upperOutput": round(output, 4)},
    }


class Simulator:
    def __init__(
        self,
        cycle_seconds: float,
        run_prefix: str,
        max_runs: int,
        experiment_plan: list[dict[str, object]] | None = None,
    ) -> None:
        if cycle_seconds <= 0:
            raise ValueError("cycle_seconds must be greater than zero")
        if max_runs < 1:
            raise ValueError("max_runs must be at least one")
        self.started = time.monotonic()
        self.cycle_seconds = cycle_seconds
        self.run_prefix = run_prefix
        self.experiment_plan = experiment_plan
        self.max_runs = len(experiment_plan) if experiment_plan else max_runs
        self.sequence = 0

    def snapshot(self) -> dict[str, object]:
        self.sequence += 1
        elapsed = time.monotonic() - self.started
        run_active = elapsed < self.max_runs * self.cycle_seconds
        ordinal = min(int(elapsed // self.cycle_seconds) + 1, self.max_runs)
        progress = (elapsed % self.cycle_seconds) / self.cycle_seconds if run_active else 1.0
        plan_item = self.experiment_plan[ordinal - 1] if self.experiment_plan else None
        cohort = (
            "experiment"
            if plan_item
            else "baseline"
            if ordinal <= 8
            else "heater_drift"
            if ordinal <= 16
            else "verification"
        )
        compensation = float(plan_item["compensation"]) if plan_item else None
        version = 3 if plan_item else 1 if cohort != "verification" else 2
        run_id = str(plan_item["run_id"]) if plan_item else f"{self.run_prefix}-{ordinal:03d}"
        phase = "preheat" if progress < 1 / 3 else "molding" if progress < 2 / 3 else "cooling"
        phase_name = {"preheat": "预热", "molding": "模压保压", "cooling": "冷却脱模"}[phase]
        now = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
        return {
            "timestamp": now,
            "sequence": self.sequence,
            "runActive": run_active,
            "runId": run_id,
            "productSeries": "optical-lens-demo",
            "productCode": "LENS-DEMO-50",
            "workpieceId": f"{run_id}-workpiece",
            "machineId": "OPTICAL-MOLD-SIM-01",
            "moldId": "MOLD-DEMO-A01",
            "materialLotRef": "GLASS-DEMO-01",
            "step": {"code": phase, "name": phase_name},
            "activeRecipe": recipe(version, compensation, experimental=bool(plan_item)),
            "signals": values(cohort, progress, compensation),
        }


def handler(simulator: Simulator):
    class SnapshotHandler(BaseHTTPRequestHandler):
        def do_GET(self) -> None:  # noqa: N802
            if self.path != "/api/v1/snapshot":
                self.send_error(404)
                return
            body = json.dumps(simulator.snapshot(), ensure_ascii=False).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, _format: str, *_args: object) -> None:
            return

    return SnapshotHandler


def main() -> None:
    args = parse_args()
    experiment_plan = load_experiment_plan(args)
    simulator = Simulator(
        args.cycle_seconds,
        args.run_prefix,
        args.max_runs,
        experiment_plan,
    )
    server = ThreadingHTTPServer((args.host, args.port), handler(simulator))
    print(
        f"Optical molding simulator listening on http://{args.host}:{args.port}/api/v1/snapshot "
        f"for {simulator.max_runs} bounded runs "
        f"{'from experiment ' + args.experiment_id if experiment_plan else 'with prefix ' + args.run_prefix}",
        flush=True,
    )
    server.serve_forever()


if __name__ == "__main__":
    main()
