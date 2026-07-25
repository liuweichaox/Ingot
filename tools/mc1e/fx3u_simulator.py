#!/usr/bin/env python3
"""FX3U-ENET-ADP A-compatible MC 1E binary read simulator."""

from __future__ import annotations

import argparse
import math
import socketserver
import struct
import threading
import time


DEVICE_CODES = {
    b" D": "D",
    b" R": "R",
    b" W": "W",
    b" M": "M",
    b" X": "X",
    b" Y": "Y",
    b" B": "B",
    b" T": "T",
    b" C": "C",
    b" L": "L",
    b" S": "S",
}


class Fx3uMemory:
    def __init__(self) -> None:
        self.started_at = time.monotonic()
        self.lock = threading.Lock()
        self.read_count = 0

    def read_words(self, device: str, address: int, count: int) -> list[int]:
        with self.lock:
            self.read_count += 1
            elapsed = time.monotonic() - self.started_at
            cycle = int(elapsed // 30) + 1
            phase = elapsed % 30
            registers: dict[int, int] = {
                100: round(610 + 18 * math.sin(elapsed / 5)),
                101: round(32 + 4 * math.sin(elapsed / 3)),
                102: round(85 + 8 * math.sin(elapsed / 7)),
                103: round(1180 + 90 * math.sin(elapsed / 4)),
                104: cycle,
                105: min(4, int(phase // 6) + 1),
                106: self.read_count & 0xFFFF,
                107: 1,
            }
            if device != "D":
                return [0] * count
            return [registers.get(address + offset, 0) & 0xFFFF for offset in range(count)]


class Fx3uRequestHandler(socketserver.BaseRequestHandler):
    def handle(self) -> None:
        while True:
            frame = self._read_exactly(12)
            if frame is None:
                return
            response = self.server.process_frame(frame)
            self.request.sendall(response)

    def _read_exactly(self, length: int) -> bytes | None:
        data = bytearray()
        while len(data) < length:
            chunk = self.request.recv(length - len(data))
            if not chunk:
                return None
            data.extend(chunk)
        return bytes(data)


class Fx3uServer(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True

    def __init__(self, server_address: tuple[str, int], memory: Fx3uMemory):
        super().__init__(server_address, Fx3uRequestHandler)
        self.memory = memory

    def process_frame(self, frame: bytes) -> bytes:
        command, pc_number = frame[0], frame[1]
        if command != 0x01 or pc_number != 0xFF:
            return bytes([command | 0x80, 0x5B, 0x10, 0x00])

        address = struct.unpack_from("<I", frame, 4)[0]
        device = DEVICE_CODES.get(frame[8:10])
        count = frame[10] or 256
        if device is None or frame[11] != 0 or count > 64:
            return bytes([0x81, 0x57])

        words = self.memory.read_words(device, address, count)
        payload = b"".join(struct.pack("<H", value) for value in words)
        return bytes([0x81, 0x00]) + payload


def main() -> None:
    parser = argparse.ArgumentParser(description="FX3U MC 1E 二进制读取模拟器")
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=5551)
    args = parser.parse_args()

    with Fx3uServer((args.host, args.port), Fx3uMemory()) as server:
        print(f"FX3U MC 1E simulator listening on {args.host}:{args.port}", flush=True)
        print(
            "Registers: D100 temperature, D101 pressure, D102 vacuum, "
            "D103 speed, D104 cycle, D105 step",
            flush=True,
        )
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
