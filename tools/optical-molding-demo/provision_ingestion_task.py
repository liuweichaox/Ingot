#!/usr/bin/env python3
"""Publish the optical-lens molding demo as a formal, versioned ingestion task."""

from __future__ import annotations

import argparse
import json
import os
import urllib.error
import urllib.request

from demo_contract import DATA_ITEMS, RECIPE_PARAMETERS


_AUTH_TOKEN: str | None = None


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api", default="http://127.0.0.1:8000")
    parser.add_argument("--edge-id", default="EDGE-FX3U-SIM-001")
    parser.add_argument("--task-id", default="optical-lens-molding-simulator")
    parser.add_argument("--subject-id", default="OPTICAL-MOLD-SIM-01")
    parser.add_argument(
        "--source",
        default="connector/melsec-a1e/fx3u-optical-lens-molding-simulator",
    )
    parser.add_argument("--device-host", default="127.0.0.1")
    parser.add_argument("--device-port", type=int, default=5551)
    parser.add_argument("--task-version", type=int, default=8)
    parser.add_argument("--data-model-version", type=int, default=1)
    parser.add_argument("--username", default=os.environ.get("INGOT_ADMIN_USERNAME"))
    parser.add_argument("--password", default=os.environ.get("INGOT_ADMIN_PASSWORD"))
    return parser.parse_args()


def request(url: str, payload: object) -> object:
    call = urllib.request.Request(
        url,
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    if _AUTH_TOKEN:
        call.add_header("Authorization", f"Bearer {_AUTH_TOKEN}")
    try:
        with urllib.request.urlopen(call, timeout=120) as response:
            return json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", errors="replace")
        raise RuntimeError(
            f"platform request failed with HTTP {error.code}: {detail}"
        ) from error


def authenticate(api: str, username: str | None, password: str | None) -> None:
    global _AUTH_TOKEN
    if not username and not password:
        return
    if not username or not password:
        raise ValueError("both --username and --password are required for local authentication")
    response = request(
        f"{api}/api/v1/auth/login",
        {"username": username, "password": password},
    )
    token = response.get("token")
    if not isinstance(token, str) or not token:
        raise RuntimeError("local authentication did not return a session token")
    _AUTH_TOKEN = token


def mapping(item: dict[str, object]) -> dict[str, object]:
    result = {
        "dataItemCode": item["code"],
        "sourcePath": item["register"],
        "required": True,
        "sourceDataType": str(item["register"]).rsplit(":", 1)[1],
        "scale": item["scale"],
        "offset": 0,
    }
    if item.get("unit"):
        result["sourceUnit"] = item["unit"]
    return result


def build_payload(args: argparse.Namespace) -> dict[str, object]:
    return {
        "taskId": getattr(args, "task_id", "optical-lens-molding-simulator"),
        "version": args.task_version,
        "name": "光学镜片模压模拟数据源",
        "status": "published",
        "edgeId": args.edge_id,
        "protocol": "melsec-a1e",
        "dataModelId": "optical-lens-molding-demo",
        "dataModelVersion": args.data_model_version,
        "source": getattr(
            args,
            "source",
            "connector/melsec-a1e/fx3u-optical-lens-molding-simulator",
        ),
        "subjectType": "equipment",
        "subjectId": getattr(args, "subject_id", "OPTICAL-MOLD-SIM-01"),
        "melsecA1E": {
            "host": args.device_host,
            "port": args.device_port,
            "dataCode": "binary",
            "pcNumber": 255,
            "pollIntervalMs": 1000,
            "monitoringTimer": 16,
            "wordOrderLayout": "A",
        },
        "execution": {"timeoutMs": 10000, "reconnectDelayMs": 5000},
        "timestampMode": "edge-received",
        "timestampPath": "",
        "sequencePath": None,
        "sampleEventType": "process.sample",
        "staticContext": {
            "demo_replay": "true",
            "data_classification": "simulated",
        },
        "contextMappings": [
            {"contextKey": "run_active", "sourcePath": "D:0:uint16", "required": True},
            {"contextKey": "source_execution_no", "sourcePath": "D:2:uint32", "required": False},
            {"contextKey": "product_code", "sourcePath": "D:30:string:20", "required": True},
            {"contextKey": "product_family_code", "sourcePath": "D:40:string:20", "required": True},
            {"contextKey": "tooling_assembly_id", "sourcePath": "D:50:string:20", "required": True},
            {"contextKey": "material_lot_ref", "sourcePath": "D:60:string:20", "required": True},
            {"contextKey": "output_item_id", "sourcePath": "D:70:string:40", "required": True},
        ],
        "valueMappings": [
            mapping(item)
            for item in DATA_ITEMS
        ],
        "processSpecification": {
            "eventType": "process.specification.applied",
            "idPath": "D:10:string:20",
            "versionPath": "D:5:uint16",
            "namePath": None,
            "parametersPath": "D",
            "parameterMappings": [
                mapping(item)
                for item in RECIPE_PARAMETERS
            ],
        },
        "lifecycle": {
            "mode": "discrete",
            "activeContextKey": "run_active",
            "activeValue": "1",
            "startedEventType": "process.execution.started",
            "completedEventType": "process.execution.completed",
            "stepChangedEventType": "process.stage_changed",
        },
    }


def main() -> None:
    args = parse_args()
    api = args.api.rstrip("/")
    authenticate(api, args.username, args.password)
    payload = build_payload(args)
    print(json.dumps(request(f"{api}/api/v1/ingestion-tasks", payload), ensure_ascii=False))


if __name__ == "__main__":
    main()
