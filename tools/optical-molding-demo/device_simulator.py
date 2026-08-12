#!/usr/bin/env python3
"""Expose the optical-lens molding digital twin as an FX3U MC 1E PLC.

The server implements the binary A-compatible 1E word-read command used by an
FX3U-ENET-ADP. Process values and recipe setpoints are encoded into D registers
with the same selectors and scaling used by the versioned ingestion task.
Replacing this simulator with a real PLC therefore only changes host and port.
"""

from __future__ import annotations

import argparse
import json
import math
import socketserver
import struct
import threading
import time
import urllib.request
from datetime import datetime, timezone

from demo_contract import DATA_ITEMS, RECIPE_PARAMETERS, device_recipe_values


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=5551)
    parser.add_argument("--execution-seconds", type=float, default=8.0)
    parser.add_argument("--run-prefix", default="lens-source-demo")
    parser.add_argument("--max-runs", type=int, default=24)
    parser.add_argument(
        "--recipe-version-offset",
        type=int,
        default=0,
        help="Add this offset to device recipe versions when demonstrating a new data-model generation.",
    )
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
        if "recipe.upper_temperature_setpoint" not in factors:
            raise ValueError(
                f"run {run.get('executionKey')} has no recipe.upper_temperature_setpoint factor"
            )
        plan.append(
            {
                "run_id": run["executionKey"],
                "upper_temperature_setpoint": factors["recipe.upper_temperature_setpoint"],
            }
        )
    if len(plan) < 2:
        raise ValueError("an executable optimization experiment needs at least two runs")
    return plan


def recipe(
    version: int,
    upper_temperature_setpoint: float | None = None,
    experimental: bool = False,
) -> dict[str, object]:
    parameters = device_recipe_values(version)
    if upper_temperature_setpoint is not None:
        parameters["upperTemperatureSetpoint"] = round(upper_temperature_setpoint, 6)
    return {
        "id": "lens-molding-demo",
        "version": version,
        "name": (
            "LENS-DEMO 优化器建议配方"
            if experimental
            else "LENS-DEMO 基线模压配方"
            if version == 1
            else "LENS-DEMO 验证配方（上模温度调整）"
        ),
        "parameters": parameters,
    }


def values(
    cohort: str,
    progress: float,
    upper_temperature_setpoint: float | None = None,
) -> dict[str, object]:
    phase = "preheat" if progress < 1 / 3 else "molding" if progress < 2 / 3 else "cooling"
    wave = math.sin(progress * math.pi * 9)
    if phase == "preheat":
        upper = 440 + progress * 3 * 180 + wave
        lower = 438 + progress * 3 * 180 + wave * 0.8
        pressure, grating, vacuum, upper_output = 100.0, progress * 3 * 0.8, -72 + wave, 92.0
        servo_position, servo_speed, lower_output = progress * 3 * 8.5, 2.2, 90.0
    elif phase == "molding":
        upper, lower = 620 + wave * 0.7, 618 + wave * 0.5
        pressure, grating, vacuum, upper_output = 1200 + wave * 18, 2.41 + wave * 0.006, -78 + wave * 0.4, 58 + wave * 2
        servo_position, servo_speed, lower_output = 8.5 + wave * 0.02, 0.35 + wave * 0.02, 56 + wave * 1.6
        if cohort == "heater_drift":
            upper, pressure, upper_output = 611.3 + wave * 0.9, 1170 + wave * 25, 96 + wave * 1.5
        elif cohort == "verification":
            upper, upper_output = 624.4 + wave * 0.55, 66 + wave * 1.5
        elif cohort == "experiment":
            setpoint = max(620.0, min(626.0, upper_temperature_setpoint or 620.0))
            applied = setpoint - 620.0
            upper = min(setpoint - 0.6, 611.3 + applied * 1.62) + wave * 0.65
            pressure = 1170 + applied * 6 + wave * 20
            upper_output = max(58.0, 96.0 - applied * 6.0) + wave * 1.5
    else:
        cooling = (progress - 2 / 3) * 3
        upper, lower = 620 - cooling * 180 + wave * 0.7, 618 - cooling * 178 + wave * 0.5
        pressure, grating, vacuum, upper_output = max(30, 1180 - cooling * 1000), 2.41 - cooling * 1.8, -74 + wave * 0.8, max(0, 50 - cooling * 35)
        servo_position, servo_speed, lower_output = 8.5 + cooling * 16.5, 3.5, max(0, 48 - cooling * 34)
        if cohort == "heater_drift":
            upper, upper_output = upper - 5.5, min(100, upper_output + 22)
        elif cohort == "experiment":
            setpoint = max(620.0, min(626.0, upper_temperature_setpoint or 620.0))
            applied = setpoint - 620.0
            remaining_deficit = max(0.0, 5.5 * (1.0 - applied / 5.0))
            upper, upper_output = upper - remaining_deficit, min(100, upper_output + remaining_deficit * 4)
    upper_voltage = 220.0 + wave * 0.8
    lower_voltage = 220.0 + wave * 0.6
    upper_current = max(0.0, upper_output * 0.28)
    lower_current = max(0.0, lower_output * 0.27)
    return {
        "upperMold": {
            "infraredTemperature": round(upper, 4),
            "current": round(upper_current, 4),
            "voltage": round(upper_voltage, 4),
            "power": round(upper_voltage * upper_current, 4),
        },
        "lowerMold": {
            "infraredTemperature": round(lower, 4),
            "current": round(lower_current, 4),
            "voltage": round(lower_voltage, 4),
            "power": round(lower_voltage * lower_current, 4),
        },
        "pressure": {"load": round(pressure, 4)},
        "grating": {"position": round(grating, 4)},
        "servo": {"speed": round(servo_speed, 4), "position": round(servo_position, 4)},
        "vacuum": {"pressure": round(vacuum, 4)},
    }


class Simulator:
    def __init__(
        self,
        execution_seconds: float,
        run_prefix: str,
        max_runs: int,
        experiment_plan: list[dict[str, object]] | None = None,
        process_specification_version_offset: int = 0,
    ) -> None:
        if execution_seconds <= 0:
            raise ValueError("execution_seconds must be greater than zero")
        if max_runs < 1:
            raise ValueError("max_runs must be at least one")
        self.started = time.monotonic()
        self.execution_seconds = execution_seconds
        self.run_prefix = run_prefix
        self.experiment_plan = experiment_plan
        self.max_runs = len(experiment_plan) if experiment_plan else max_runs
        self.process_specification_version_offset = process_specification_version_offset
        self.sequence = 0

    def snapshot(self, elapsed_seconds: float | None = None) -> dict[str, object]:
        self.sequence += 1
        elapsed = (
            time.monotonic() - self.started
            if elapsed_seconds is None
            else max(0.0, elapsed_seconds)
        )
        slot_elapsed = elapsed % self.execution_seconds
        idle_seconds = min(
            2.0,
            max(0.5, self.execution_seconds * 0.2),
            self.execution_seconds * 0.4,
        )
        active_seconds = self.execution_seconds - idle_seconds
        run_active = (
            elapsed < self.max_runs * self.execution_seconds
            and slot_elapsed < active_seconds
        )
        ordinal = min(int(elapsed // self.execution_seconds) + 1, self.max_runs)
        progress = min(slot_elapsed / active_seconds, 1.0) if run_active else 1.0
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
        upper_temperature_setpoint = (
            float(plan_item["upper_temperature_setpoint"]) if plan_item else None
        )
        base_version = 3 if plan_item else 1 if cohort != "verification" else 2
        version = base_version + self.process_specification_version_offset
        run_id = str(plan_item["run_id"]) if plan_item else f"{self.run_prefix}-{ordinal:03d}"
        phase = "preheat" if progress < 1 / 3 else "molding" if progress < 2 / 3 else "cooling"
        stage_number = {"preheat": 10, "molding": 20, "cooling": 30}[phase]
        phase_name = {"preheat": "预热", "molding": "模压保压", "cooling": "冷却脱模"}[phase]
        now = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
        return {
            "timestamp": now,
            "sequence": self.sequence,
            "runActive": run_active,
            "runId": run_id,
            "runNumber": ordinal,
            "productFamilyCode": "optical-lens-demo",
            "productCode": "LENS-DEMO-50",
            "outputItemId": f"{run_id}-workpiece",
            "equipmentId": "OPTICAL-MOLD-SIM-01",
            "toolingAssemblyId": "MOLD-DEMO-A01",
            "materialLotRef": "GLASS-DEMO-01",
            "stageNumber": stage_number,
            "step": {"code": phase, "name": phase_name},
            "activeRecipe": {
                **recipe(base_version, upper_temperature_setpoint, experimental=bool(plan_item)),
                "version": version,
            },
            "signals": values(cohort, progress, upper_temperature_setpoint),
        }


def resolve_path(value: dict[str, object], path: str) -> object:
    current: object = value
    for segment in path.split("."):
        if not isinstance(current, dict):
            raise KeyError(path)
        current = current[segment]
    return current


def encode_register_value(value: object, selector: str, scale: float) -> bytes:
    parts = selector.split(":")
    register_type = parts[2]
    if register_type == "string":
        length = int(parts[3])
        encoded = str(value).encode("ascii")
        if len(encoded) > length:
            raise ValueError(f"cannot encode {selector}={value}: string is too long")
        return encoded.ljust(length + length % 2, b"\x00")
    raw = round(float(value) / scale)
    formats = {
        "int16": "<h",
        "uint16": "<H",
        "int32": "<i",
        "uint32": "<I",
    }
    try:
        return struct.pack(formats[register_type], raw)
    except (KeyError, struct.error) as error:
        raise ValueError(f"cannot encode {selector}={value}") from error


class Fx3uRegisterBank:
    def __init__(self, simulator: Simulator) -> None:
        self.simulator = simulator
        self.words: dict[int, int] = {}
        self.lock = threading.Lock()
        self.stop_event = threading.Event()
        self.thread = threading.Thread(target=self._update_loop, daemon=True)

    def start(self) -> None:
        self._refresh()
        self.thread.start()

    def stop(self) -> None:
        self.stop_event.set()
        self.thread.join(timeout=2)

    def read_words(self, address: int, count: int) -> list[int]:
        with self.lock:
            return [self.words.get(address + offset, 0) for offset in range(count)]

    def _write(self, words: dict[int, int], selector: str, value: object, scale: float = 1) -> None:
        _, address_text, *_ = selector.split(":")
        address = int(address_text)
        payload = encode_register_value(value, selector, scale)
        for offset in range(0, len(payload), 2):
            words[address + offset // 2] = int.from_bytes(payload[offset : offset + 2], "little")

    def _refresh(self) -> None:
        snapshot = self.simulator.snapshot()
        words: dict[int, int] = {}
        self._write(words, "D:0:uint16", int(bool(snapshot["runActive"])))
        self._write(words, "D:1:uint16", snapshot["stageNumber"])
        self._write(words, "D:2:uint32", snapshot["runNumber"])
        self._write(words, "D:5:uint16", snapshot["activeRecipe"]["version"])
        self._write(words, "D:10:string:20", snapshot["activeRecipe"]["id"])
        self._write(words, "D:30:string:20", "LENS-DEMO-50")
        self._write(words, "D:40:string:20", "optical-lens-demo")
        self._write(words, "D:50:string:20", "MOLD-DEMO-A01")
        self._write(words, "D:60:string:20", "GLASS-DEMO-01")
        self._write(words, "D:70:string:40", snapshot["outputItemId"])
        for item in DATA_ITEMS:
            self._write(
                words,
                str(item["register"]),
                resolve_path(snapshot, str(item["sourcePath"])),
                float(item["scale"]),
            )
        parameters = snapshot["activeRecipe"]["parameters"]
        for item in RECIPE_PARAMETERS:
            self._write(
                words,
                str(item["register"]),
                parameters[str(item["sourcePath"])],
                float(item["scale"]),
            )
        with self.lock:
            self.words = words

    def _update_loop(self) -> None:
        while not self.stop_event.wait(0.1):
            self._refresh()


def handler(registers: Fx3uRegisterBank):
    class Mc1EHandler(socketserver.BaseRequestHandler):
        def handle(self) -> None:
            while True:
                request = self._read_exact(12)
                if request is None:
                    return
                if request[:2] != b"\x01\xff" or request[8:10] != b" D":
                    self.request.sendall(b"\x81\x10")
                    continue
                address = int.from_bytes(request[4:8], "little")
                count = request[10] or 256
                words = registers.read_words(address, count)
                payload = b"".join(word.to_bytes(2, "little") for word in words)
                self.request.sendall(b"\x81\x00" + payload)

        def _read_exact(self, size: int) -> bytes | None:
            chunks = bytearray()
            while len(chunks) < size:
                chunk = self.request.recv(size - len(chunks))
                if not chunk:
                    return None
                chunks.extend(chunk)
            return bytes(chunks)

    return Mc1EHandler


class Fx3uServer(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


def main() -> None:
    args = parse_args()
    experiment_plan = load_experiment_plan(args)
    simulator = Simulator(
        args.execution_seconds,
        args.run_prefix,
        args.max_runs,
        experiment_plan,
        args.process_specification_version_offset,
    )
    registers = Fx3uRegisterBank(simulator)
    registers.start()
    server = Fx3uServer((args.host, args.port), handler(registers))
    print(
        f"FX3U optical molding simulator listening on MC 1E {args.host}:{args.port} "
        f"for {simulator.max_runs} bounded runs "
        f"{'from experiment ' + args.experiment_id if experiment_plan else 'with prefix ' + args.run_prefix}",
        flush=True,
    )
    try:
        server.serve_forever()
    finally:
        registers.stop()
        server.server_close()


if __name__ == "__main__":
    main()
