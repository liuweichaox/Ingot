#!/usr/bin/env python3
"""Minimal CloudEvents receiver used by Ingot's end-to-end acceptance script."""

from __future__ import annotations

import argparse
import hashlib
import hmac
import json
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--secret", required=True)
    parser.add_argument("--output", type=Path, required=True)
    return parser.parse_args()


class ReceiverState:
    def __init__(self, secret: str, output: Path) -> None:
        self.secret = secret.encode("utf-8")
        self.output = output
        self.lock = threading.Lock()
        self.event_ids: set[str] = set()
        self.received = 0
        self.duplicates = 0
        self.invalid = 0

    def stats(self) -> dict[str, int]:
        with self.lock:
            return {
                "received": self.received,
                "unique": len(self.event_ids),
                "duplicates": self.duplicates,
                "invalid": self.invalid,
            }

    def reject(self) -> None:
        with self.lock:
            self.invalid += 1

    def accept(self, event: dict[str, Any]) -> None:
        event_id = str(event["id"])
        with self.lock:
            self.received += 1
            if event_id in self.event_ids:
                self.duplicates += 1
            self.event_ids.add(event_id)
            with self.output.open("a", encoding="utf-8") as stream:
                stream.write(json.dumps(event, ensure_ascii=False, separators=(",", ":")))
                stream.write("\n")


def create_handler(state: ReceiverState) -> type[BaseHTTPRequestHandler]:
    class Handler(BaseHTTPRequestHandler):
        server_version = "IngotWebhookAcceptance/1.0"

        def do_GET(self) -> None:  # noqa: N802
            if self.path == "/health":
                self._json(200, {"status": "ok"})
                return
            if self.path == "/stats":
                self._json(200, state.stats())
                return
            self._json(404, {"error": "not found"})

        def do_POST(self) -> None:  # noqa: N802
            if self.path != "/events":
                self._json(404, {"error": "not found"})
                return

            try:
                length = int(self.headers.get("Content-Length", "0"))
                body = self.rfile.read(length)
                expected = "sha256=" + hmac.new(
                    state.secret, body, hashlib.sha256
                ).hexdigest()
                signature = self.headers.get("X-Ingot-Signature", "")
                content_type = self.headers.get("Content-Type", "")
                event = json.loads(body)
                event_id = str(event.get("id", ""))
                valid = (
                    hmac.compare_digest(signature, expected)
                    and content_type.startswith("application/cloudevents+json")
                    and event.get("specversion") == "1.0"
                    and event_id
                    and self.headers.get("X-Ingot-Event-Id") == event_id
                    and str(event.get("type", "")).startswith("com.ingot.")
                    and event.get("source")
                    and event.get("time")
                    and event.get("data") is not None
                )
                if not valid:
                    state.reject()
                    self._json(400, {"error": "invalid CloudEvent or signature"})
                    return

                state.accept(event)
                self.send_response(204)
                self.end_headers()
            except Exception as exception:  # acceptance helper: report malformed input
                state.reject()
                self._json(400, {"error": str(exception)})

        def log_message(self, format: str, *args: Any) -> None:
            return

        def _json(self, status: int, value: dict[str, Any]) -> None:
            body = json.dumps(value, separators=(",", ":")).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

    return Handler


def main() -> None:
    args = parse_args()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    state = ReceiverState(args.secret, args.output)
    server = ThreadingHTTPServer((args.host, args.port), create_handler(state))
    server.serve_forever()


if __name__ == "__main__":
    main()
