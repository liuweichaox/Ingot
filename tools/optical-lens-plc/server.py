"""Deterministic Modbus TCP PLC simulator for the optical-lens demo.

The simulator deliberately speaks the same register protocol as a small PLC:
input registers contain process values, holding registers contain the active
recipe, and one input-register bit indicates the discrete run boundary.  It
starts idle for a few seconds, runs one 300-second batch, then remains idle.
"""

from __future__ import annotations

import json
import math
import os
import random
import socketserver
import struct
import threading
import time
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


PORT = int(os.getenv("PLC_MODBUS_PORT", "1502"))
HEALTH_PORT = int(os.getenv("PLC_HEALTH_PORT", "9100"))
IDLE_SECONDS = float(os.getenv("PLC_IDLE_SECONDS", "10"))
BATCH_SECONDS = 300.0
RNG = random.Random(42025)
STARTED_AT = time.monotonic()


def clamp(value: float, lower: float, upper: float) -> float:
    return max(lower, min(upper, value))


def phase_at(elapsed: float) -> tuple[int, str]:
    if elapsed < 20:
        return 1, "预热与抽真空"
    if elapsed < 95:
        return 2, "升温"
    if elapsed < 220:
        return 3, "恒温保压"
    if elapsed < BATCH_SECONDS:
        return 4, "降温脱模"
    return 0, "待机"


def snapshot() -> dict[str, object]:
    since_start = time.monotonic() - STARTED_AT
    elapsed = since_start - IDLE_SECONDS
    active = 0 <= elapsed < BATCH_SECONDS
    batch_elapsed = clamp(elapsed, 0.0, BATCH_SECONDS)
    stage, stage_name = phase_at(batch_elapsed if active else BATCH_SECONDS)
    timestamp_ms = int(time.time() * 1000)

    # The curves are piecewise-smooth and include deterministic sensor noise.
    if batch_elapsed < 20:
        upper = 25 + 40 * batch_elapsed / 20
        lower = upper - 1.5
    elif batch_elapsed < 95:
        upper = 65 + 110 * (batch_elapsed - 20) / 75
        lower = upper - 2.0
    elif batch_elapsed < 220:
        upper = 175 + 0.35 * math.sin(batch_elapsed / 11)
        lower = 173 + 0.30 * math.sin(batch_elapsed / 13 + 0.5)
    else:
        cooling = (batch_elapsed - 220) / 80
        upper = 175 - 110 * cooling
        lower = upper - 1.5

    if batch_elapsed < 20:
        pressure = 0.15 + 0.35 * batch_elapsed / 20
        force = 4 + 8 * batch_elapsed / 20
        gap = 4.60 - 0.08 * batch_elapsed / 20
        vacuum = -82 * batch_elapsed / 20
        coolant_flow = 0.0
    elif batch_elapsed < 95:
        ramp = (batch_elapsed - 20) / 75
        pressure = 0.5 + 7.5 * ramp
        force = 12 + 23 * ramp
        gap = 4.52 - 0.31 * ramp
        vacuum = -84.5
        coolant_flow = 1.0 + 4.5 * ramp
    elif batch_elapsed < 220:
        pressure = 8.0 + 0.18 * math.sin(batch_elapsed / 7)
        if 98 <= batch_elapsed <= 103:
            pressure += 0.22 * math.sin((batch_elapsed - 98) * math.pi / 5)
        force = 35 + 0.35 * math.sin(batch_elapsed / 9)
        gap = 4.20 + 0.006 * math.sin(batch_elapsed / 10)
        vacuum = -85 + 0.8 * math.sin(batch_elapsed / 15)
        coolant_flow = 6.0 + 0.12 * math.sin(batch_elapsed / 17)
    else:
        cooling = (batch_elapsed - 220) / 80
        pressure = 7.5 - 7.2 * cooling
        force = 34 - 32 * cooling
        gap = 4.21 + 0.39 * cooling
        vacuum = -84 + 84 * cooling
        coolant_flow = 6.0 - 2.0 * cooling

    noise = lambda amount: RNG.uniform(-amount, amount)
    values = {
        "mold.upper_temp": clamp(upper + noise(0.25), 20, 190),
        "mold.lower_temp": clamp(lower + noise(0.25), 20, 190),
        "cavity.pressure": clamp(pressure + noise(0.04), 0, 12),
        "press.force": clamp(force + noise(0.15), 0, 50),
        "vacuum.pressure": clamp(vacuum + noise(0.6), -101, 0),
        "plunger.position": clamp(gap + noise(0.002), 3.8, 4.8),
        "plunger.speed": clamp((0.8 if 20 <= batch_elapsed < 95 else 0.15) + noise(0.02), 0, 5),
        "coolant.inlet_temp": 22.0 + noise(0.08),
        "coolant.outlet_temp": 22.0 + coolant_flow * 1.1 + noise(0.12),
        "coolant.flow": clamp(coolant_flow + noise(0.05), 0, 12),
        "heater.upper_output": clamp(88 - upper * 0.38 if active and batch_elapsed < 220 else 0, 0, 100),
        "heater.lower_output": clamp(83 - lower * 0.36 if active and batch_elapsed < 220 else 0, 0, 100),
        "preform.temperature": clamp(60 + 8 * math.sin(batch_elapsed / 35) if active else 25, 20, 90),
        "environment.temperature": 23.0 + noise(0.05),
        "environment.humidity": 35.0 + noise(0.25),
        "ejector.force": clamp(0.2 + 1.6 * max(0, (batch_elapsed - 278) / 22) + noise(0.03), 0, 10),
        "mold.gap": clamp(gap + noise(0.002), 3.8, 4.8),
        "vacuum.leak_rate": clamp(1.0 + abs(noise(0.35)) if active else 0.2, 0, 8),
    }
    parameters = {
        "mold.upper_setpoint": 175.0,
        "mold.lower_setpoint": 173.0,
        "heating.ramp_rate": 1.8,
        "vacuum.setpoint": -85.0,
        "vacuum.pull_time": 30,
        "forming.pressure": 8.0,
        "forming.pressure_ramp": 0.4,
        "forming.hold_time": 135,
        "clamp.force": 35.0,
        "plunger.approach_speed": 0.8,
        "mold.final_gap": 4.2,
        "cooling.start_temp": 175.0,
        "cooling.target_temp": 65.0,
        "cooling.duration": 75,
        "cooling.flow": 6.0,
        "pressure.release_temp": 80.0,
        "preform.temperature": 60.0,
        "preform.mass": 4.2,
        "vent.delay": 12,
        "ejector.force_limit": 2.0,
    }
    return {
        "timestamp_ms": timestamp_ms,
        "sequence": int(since_start * 2),
        "run_active": active,
        "stage_number": stage,
        "stage_name": stage_name,
        "batch_id": "OL25-420-DEMO-001",
        "values": values,
        "parameters": parameters,
    }


def float_registers(value: float) -> list[int]:
    raw = struct.pack(">f", float(value))
    return list(struct.unpack(">HH", raw))


def uint64_registers(value: int) -> list[int]:
    raw = struct.pack(">Q", value)
    return list(struct.unpack(">HHHH", raw))


def string_registers(value: str, byte_length: int) -> list[int]:
    raw = value.encode("ascii")[:byte_length].ljust(byte_length, b"\0")
    if len(raw) % 2:
        raw += b"\0"
    return list(struct.unpack(">" + "H" * (len(raw) // 2), raw))


PROCESS_ADDRESSES = {
    "mold.upper_temp": 0, "mold.lower_temp": 2, "cavity.pressure": 4,
    "press.force": 6, "vacuum.pressure": 8, "plunger.position": 10,
    "plunger.speed": 12, "coolant.inlet_temp": 14, "coolant.outlet_temp": 16,
    "coolant.flow": 18, "heater.upper_output": 20, "heater.lower_output": 22,
    "preform.temperature": 24, "environment.temperature": 26,
    "environment.humidity": 28, "ejector.force": 30, "mold.gap": 32,
    "vacuum.leak_rate": 34,
}

PARAM_ADDRESSES = {
    "mold.upper_setpoint": 200, "mold.lower_setpoint": 202, "heating.ramp_rate": 204,
    "vacuum.setpoint": 206, "vacuum.pull_time": 208, "forming.pressure": 210,
    "forming.pressure_ramp": 212, "forming.hold_time": 214, "clamp.force": 216,
    "plunger.approach_speed": 218, "mold.final_gap": 220, "cooling.start_temp": 222,
    "cooling.target_temp": 224, "cooling.duration": 226, "cooling.flow": 228,
    "pressure.release_temp": 230, "preform.temperature": 232, "preform.mass": 234,
    "vent.delay": 236, "ejector.force_limit": 238,
}


def register_map() -> tuple[list[int], list[int]]:
    data = snapshot()
    input_registers = [0] * 256
    holding_registers = [0] * 256
    for code, address in PROCESS_ADDRESSES.items():
        regs = float_registers(data["values"][code])
        input_registers[address:address + 2] = regs
    input_registers[80] = 1 if data["run_active"] else 0
    input_registers[82] = int(data["stage_number"])
    input_registers[84:88] = uint64_registers(int(data["timestamp_ms"]))
    input_registers[90:100] = string_registers(data["batch_id"], 20)
    for code, address in PARAM_ADDRESSES.items():
        value = data["parameters"][code]
        if isinstance(value, int):
            holding_registers[address] = value & 0xFFFF
        else:
            holding_registers[address:address + 2] = float_registers(value)
    # Keep the recipe identity fields on non-overlapping register ranges.
    # The string fields use 28 bytes (14 registers), so the version must not
    # sit inside the recipe id or name payload.
    holding_registers[160:174] = string_registers("pmma-lens-4p2", 28)
    holding_registers[176] = 1
    holding_registers[180:194] = string_registers("PMMA Lens 4.2mm thermoform", 28)
    return input_registers, holding_registers


class ModbusHandler(socketserver.BaseRequestHandler):
    def handle(self) -> None:
        while True:
            header = self.request.recv(7)
            if not header:
                return
            if len(header) != 7:
                return
            transaction, protocol, length, unit = struct.unpack(">HHHB", header)
            if protocol != 0 or length < 2:
                return
            pdu = self.request.recv(length - 1)
            if len(pdu) != length - 1:
                return
            function = pdu[0]
            if function not in (3, 4) or len(pdu) != 5:
                response_pdu = bytes([function | 0x80, 1])
            else:
                address, quantity = struct.unpack(">HH", pdu[1:5])
                inputs, holdings = register_map()
                registers = holdings if function == 3 else inputs
                if quantity < 1 or quantity > 125 or address + quantity > len(registers):
                    response_pdu = bytes([function | 0x80, 2])
                else:
                    selected = registers[address:address + quantity]
                    response_pdu = bytes([function, quantity * 2]) + b"".join(struct.pack(">H", item) for item in selected)
            response = struct.pack(">HHHB", transaction, protocol, len(response_pdu) + 1, unit) + response_pdu
            self.request.sendall(response)


class ReusableTcpServer(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


class HealthHandler(BaseHTTPRequestHandler):
    def do_GET(self) -> None:  # noqa: N802
        if self.path == "/health":
            body = json.dumps({"status": "healthy", "service": "optical-lens-plc", "batchSeconds": BATCH_SECONDS}).encode()
            self.send_response(200)
        elif self.path == "/snapshot":
            body = json.dumps(snapshot(), ensure_ascii=False).encode()
            self.send_response(200)
        else:
            body = b"not found"
            self.send_response(404)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *_args: object) -> None:
        return


def main() -> None:
    modbus = ReusableTcpServer(("0.0.0.0", PORT), ModbusHandler)
    health = ThreadingHTTPServer(("0.0.0.0", HEALTH_PORT), HealthHandler)
    threading.Thread(target=modbus.serve_forever, daemon=True).start()
    threading.Thread(target=health.serve_forever, daemon=True).start()
    print(f"optical-lens-plc listening on Modbus TCP :{PORT}; health :{HEALTH_PORT}", flush=True)
    try:
        while True:
            time.sleep(3600)
    except KeyboardInterrupt:
        modbus.shutdown()
        health.shutdown()


if __name__ == "__main__":
    main()
