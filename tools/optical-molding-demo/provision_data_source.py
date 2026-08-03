#!/usr/bin/env python3
"""Publish the optical-lens molding demo as a formal, versioned data source."""

from __future__ import annotations

import argparse
import json
import urllib.request

from demo_contract import DATA_ITEMS, RECIPE_PARAMETERS


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api", default="http://127.0.0.1:8000")
    parser.add_argument("--edge-id", default="EDGE-FX3U-SIM-001")
    parser.add_argument("--profile-id", default="optical-lens-molding-simulator")
    parser.add_argument("--subject-id", default="OPTICAL-MOLD-SIM-01")
    parser.add_argument(
        "--source",
        default="connector/melsec-a1e/fx3u-optical-lens-molding-simulator",
    )
    parser.add_argument("--device-host", default="127.0.0.1")
    parser.add_argument("--device-port", type=int, default=5551)
    parser.add_argument("--profile-version", type=int, default=8)
    parser.add_argument("--data-model-version", type=int, default=1)
    return parser.parse_args()


def mapping(item: dict[str, object]) -> dict[str, object]:
    return {
        "dataItemCode": item["code"],
        "sourcePath": item["register"],
        "required": True,
        "sourceDataType": str(item["register"]).rsplit(":", 1)[1],
        "scale": item["scale"],
        "offset": 0,
    }


def build_payload(args: argparse.Namespace) -> dict[str, object]:
    return {
        "profileId": getattr(args, "profile_id", "optical-lens-molding-simulator"),
        "version": args.profile_version,
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
        "connection": {"baseUrl": "", "snapshotPath": "/api/v1/snapshot", "pollIntervalMs": 1000},
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
            {"contextKey": "source_cycle_no", "sourcePath": "D:2:uint32", "required": False},
            {"contextKey": "product_code", "sourcePath": "D:30:string:20", "required": True},
            {"contextKey": "product_series", "sourcePath": "D:40:string:20", "required": True},
            {"contextKey": "mold_id", "sourcePath": "D:50:string:20", "required": True},
            {"contextKey": "material_lot_ref", "sourcePath": "D:60:string:20", "required": True},
        ],
        "valueMappings": [
            mapping(item)
            for item in DATA_ITEMS
        ],
        "recipe": {
            "eventType": "recipe.applied",
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
            "mode": "discrete-cycle",
            "activeContextKey": "run_active",
            "activeValue": "1",
            "startedEventType": "cycle.started",
            "completedEventType": "cycle.completed",
            "stepChangedEventType": "process.stage_changed",
        },
    }


def main() -> None:
    args = parse_args()
    payload = build_payload(args)
    request = urllib.request.Request(
        f"{args.api.rstrip('/')}/api/v1/acquisition-profiles",
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(request, timeout=30) as response:
        print(response.read().decode("utf-8"))


if __name__ == "__main__":
    main()
