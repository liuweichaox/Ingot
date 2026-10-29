"""Serve deterministic synthetic wafer-etch telemetry over Modbus TCP.

The simulator is read-only: input registers hold generated process values and
holding registers expose demo recipe metadata and values. It does not control
real equipment or model a production process window.
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
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


PORT = int(os.getenv("DEVICE_MODBUS_PORT", "1502"))
HEALTH_PORT = int(os.getenv("DEVICE_HEALTH_PORT", "9100"))
IDLE_SECONDS = float(os.getenv("DEVICE_IDLE_SECONDS", "10"))
BATCH_SECONDS = 300.0
RNG = random.Random(20261029)
STARTED_AT = time.monotonic()


def clamp(value: float, lower: float, upper: float) -> float:
    return max(lower, min(upper, value))


def stage_at(elapsed: float) -> int:
    if elapsed < 25:
        return 1
    if elapsed < 55:
        return 2
    if elapsed < 240:
        return 3
    if elapsed < 280:
        return 4
    if elapsed < BATCH_SECONDS:
        return 5
    return 0


def synthetic_snapshot() -> dict[str, object]:
    since_start = time.monotonic() - STARTED_AT
    elapsed = since_start - IDLE_SECONDS
    active = 0 <= elapsed < BATCH_SECONDS
    cycle_time = clamp(elapsed, 0.0, BATCH_SECONDS)
    stage = stage_at(cycle_time) if active else 0
    timestamp_ms = int(time.time() * 1000)
    etch_progress = clamp((cycle_time - 55) / 185, 0.0, 1.0)
    plasma_ramp = clamp((cycle_time - 55) / 10, 0.0, 1.0)

    if cycle_time < 25:
        pressure = 101325.0 - (101325.0 - 50.0) * cycle_time / 25.0
    elif cycle_time < 55:
        pressure = 50.0 + 5.0 * math.sin(cycle_time / 8.0)
    elif cycle_time < 240:
        pressure = 35.0 + 1.2 * math.sin(cycle_time / 11.0)
    elif cycle_time < 280:
        pressure = 50.0 + (cycle_time - 240.0) * 2.0
    else:
        pressure = 101325.0

    plasma_active = 55 <= cycle_time < 240
    purge_active = 240 <= cycle_time < 280
    noise = lambda amount: RNG.uniform(-amount, amount)
    values = {
        "chamber.pressure": clamp(pressure + noise(max(0.1, pressure * 0.005)), 0, 101325),
        "rf.source_power": (600.0 * plasma_ramp + noise(2.0)) if plasma_active else 0.0,
        "rf.bias_power": (180.0 * plasma_ramp + noise(1.5)) if plasma_active else 0.0,
        "rf.reflected_power": clamp(26.0 + noise(3.0), 0, 100) if plasma_active else 0.0,
        "gas.cf4_flow": clamp(45.0 + noise(1.0), 0, 100) if plasma_active else 0.0,
        "gas.o2_flow": clamp(12.0 + noise(0.5), 0, 50) if plasma_active else 0.0,
        "gas.ar_flow": clamp(80.0 + noise(1.5), 0, 150) if plasma_active else (20.0 if purge_active else 0.0),
        "wafer.temperature": clamp(25.0 + 2.5 * etch_progress + noise(0.3), -50, 100),
        "chamber.wall_temperature": clamp(42.0 + 5.0 * plasma_ramp + noise(0.5), 20, 120),
        "coolant.inlet_temperature": clamp(18.0 + noise(0.15), 0, 35),
        "coolant.outlet_temperature": clamp(21.0 + (2.0 if plasma_active else 0.0) + noise(0.2), 0, 50),
        "coolant.flow": clamp(6.0 + noise(0.1), 0, 20),
        "plasma.voltage": clamp(380.0 + noise(8.0), 0, 1000) if plasma_active else 0.0,
        "plasma.current": clamp(1.5 + noise(0.05), 0, 10) if plasma_active else 0.0,
        "endpoint.signal": clamp(0.94 - 0.30 * etch_progress + noise(0.01), 0, 1),
        "wafer.rotation_speed": clamp(60.0 + noise(0.5), 0, 120) if plasma_active else 0.0,
        "vacuum.pump_current": clamp(18.0 + noise(0.4), 0, 50) if stage in (1, 2, 3, 4) else 2.0,
        "environment.temperature": clamp(22.0 + noise(0.1), 15, 35),
    }
    parameters = {
        "etch.duration": 185,
        "chamber.pressure_target": 35.0,
        "rf.source_power_target": 600.0,
        "rf.bias_power_target": 180.0,
        "gas.cf4_flow_target": 45.0,
        "gas.o2_flow_target": 12.0,
        "gas.ar_flow_target": 80.0,
        "wafer.temperature_target": 25.0,
        "wafer.rotation_target": 60.0,
        "endpoint.signal_threshold": 0.72,
        "endpoint.timeout": 210,
        "pumpdown.target_pressure": 50.0,
        "gas.stabilization_duration": 15,
        "plasma.ramp_duration": 10,
        "gas.purge_duration": 35,
        "coolant.flow_target": 6.0,
        "coolant.temperature_target": 18.0,
        "rf.reflected_power_limit": 40.0,
        "process.repeat_count": 1,
        "endpoint.mode_code": 1,
    }
    return {
        "timestamp_ms": timestamp_ms,
        "sequence": int(since_start * 2),
        "run_active": active,
        "stage_number": stage,
        "batch_id": "WAFER-ETCH-DEMO-001",
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
    "chamber.pressure": 0,
    "rf.source_power": 2,
    "rf.bias_power": 4,
    "rf.reflected_power": 6,
    "gas.cf4_flow": 8,
    "gas.o2_flow": 10,
    "gas.ar_flow": 12,
    "wafer.temperature": 14,
    "chamber.wall_temperature": 16,
    "coolant.inlet_temperature": 18,
    "coolant.outlet_temperature": 20,
    "coolant.flow": 22,
    "plasma.voltage": 24,
    "plasma.current": 26,
    "endpoint.signal": 28,
    "wafer.rotation_speed": 30,
    "vacuum.pump_current": 32,
    "environment.temperature": 34,
}

PARAM_ADDRESSES = {
    "etch.duration": 200,
    "chamber.pressure_target": 202,
    "rf.source_power_target": 204,
    "rf.bias_power_target": 206,
    "gas.cf4_flow_target": 208,
    "gas.o2_flow_target": 210,
    "gas.ar_flow_target": 212,
    "wafer.temperature_target": 214,
    "wafer.rotation_target": 216,
    "endpoint.signal_threshold": 218,
    "endpoint.timeout": 220,
    "pumpdown.target_pressure": 222,
    "gas.stabilization_duration": 224,
    "plasma.ramp_duration": 226,
    "gas.purge_duration": 228,
    "coolant.flow_target": 230,
    "coolant.temperature_target": 232,
    "rf.reflected_power_limit": 234,
    "process.repeat_count": 236,
    "endpoint.mode_code": 238,
}


def register_map() -> tuple[list[int], list[int]]:
    data = synthetic_snapshot()
    input_registers = [0] * 256
    holding_registers = [0] * 256
    for code, address in PROCESS_ADDRESSES.items():
        input_registers[address:address + 2] = float_registers(data["values"][code])
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
    holding_registers[160:174] = string_registers("wafer-etch-demo-v1", 28)
    holding_registers[176] = 1
    holding_registers[180:194] = string_registers("Synthetic wafer etch demo", 28)
    return input_registers, holding_registers


class ModbusHandler(socketserver.BaseRequestHandler):
    def receive_exact(self, count: int) -> bytes:
        data = bytearray()
        while len(data) < count:
            chunk = self.request.recv(count - len(data))
            if not chunk:
                return b""
            data.extend(chunk)
        return bytes(data)

    def handle(self) -> None:
        while True:
            header = self.receive_exact(7)
            if not header:
                return
            transaction, protocol, length, unit = struct.unpack(">HHHB", header)
            if protocol != 0 or length < 2:
                return
            pdu = self.receive_exact(length - 1)
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
                    response_pdu = bytes([function, quantity * 2]) + b"".join(
                        struct.pack(">H", item) for item in selected
                    )
            response = struct.pack(">HHHB", transaction, protocol, len(response_pdu) + 1, unit) + response_pdu
            self.request.sendall(response)


class ReusableTcpServer(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


class HealthHandler(BaseHTTPRequestHandler):
    def do_GET(self) -> None:  # noqa: N802
        if self.path == "/health":
            body = json.dumps({
                "status": "healthy",
                "service": "semiconductor-etch-simulator",
                "batchSeconds": BATCH_SECONDS,
            }).encode()
            self.send_response(200)
        else:
            body = b'{"status":"not-found"}'
            self.send_response(404)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format: str, *args: object) -> None:
        return


if __name__ == "__main__":
    simulator = ReusableTcpServer(("0.0.0.0", PORT), ModbusHandler)
    health = ThreadingHTTPServer(("0.0.0.0", HEALTH_PORT), HealthHandler)
    threading.Thread(target=health.serve_forever, daemon=True).start()
    print(f"semiconductor-etch-simulator listening on Modbus TCP :{PORT}; health :{HEALTH_PORT}", flush=True)
    simulator.serve_forever()
