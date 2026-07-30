#!/usr/bin/env python3
"""Publish the optical-lens molding demo as a formal, versioned data source."""

from __future__ import annotations

import argparse
import json
import urllib.request


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api", default="http://127.0.0.1:8000")
    parser.add_argument("--edge-id", default="EDGE-FX3U-SIM-001")
    parser.add_argument("--device-url", default="http://127.0.0.1:8102")
    parser.add_argument("--profile-version", type=int, default=2)
    return parser.parse_args()


def mapping(code: str, path: str) -> dict[str, object]:
    return {"dataItemCode": code, "sourcePath": path, "required": True}


def main() -> None:
    args = parse_args()
    payload = {
        "profileId": "optical-lens-molding-simulator",
        "version": args.profile_version,
        "name": "光学镜片模压模拟数据源",
        "status": "published",
        "edgeId": args.edge_id,
        "protocol": "http-polling",
        "dataModelId": "optical-lens-molding-demo",
        "dataModelVersion": 1,
        "source": "connector/http-polling/optical-lens-molding-simulator",
        "subjectType": "optical-molding-machine",
        "subjectId": "OPTICAL-MOLD-SIM-01",
        "connection": {"baseUrl": args.device_url.rstrip("/"), "snapshotPath": "/api/v1/snapshot", "pollIntervalMs": 1000},
        "execution": {"timeoutMs": 10000, "reconnectDelayMs": 5000},
        "timestampMode": "source",
        "timestampPath": "timestamp",
        "sequencePath": "sequence",
        "sampleEventType": "process.sample",
        "staticContext": {"demo_replay": "true", "data_classification": "simulated"},
        "contextMappings": [
            {"contextKey": "correlation_id", "sourcePath": "runId", "required": True},
            {"contextKey": "run_active", "sourcePath": "runActive", "required": True},
            {"contextKey": "recipe_step", "sourcePath": "step.code", "required": True},
            {"contextKey": "recipe_step_name", "sourcePath": "step.name", "required": True},
            {"contextKey": "product_series", "sourcePath": "productSeries", "required": True},
            {"contextKey": "product_code", "sourcePath": "productCode", "required": True},
            {"contextKey": "workpiece_id", "sourcePath": "workpieceId", "required": True},
            {"contextKey": "machine_id", "sourcePath": "machineId", "required": True},
            {"contextKey": "mold_id", "sourcePath": "moldId", "required": True},
            {"contextKey": "material_lot_ref", "sourcePath": "materialLotRef", "required": True},
        ],
        "valueMappings": [
            mapping("mold.upper_temperature", "signals.mold.upperTemperature"),
            mapping("mold.lower_temperature", "signals.mold.lowerTemperature"),
            mapping("molding.pressure", "signals.molding.pressure"),
            mapping("mold.displacement", "signals.mold.displacement"),
            mapping("vacuum.pressure", "signals.vacuum.pressure"),
            mapping("heater.upper_output", "signals.heater.upperOutput"),
        ],
        "recipe": {
            "eventType": "recipe.applied",
            "idPath": "activeRecipe.id",
            "versionPath": "activeRecipe.version",
            "namePath": "activeRecipe.name",
            "parametersPath": "activeRecipe.parameters",
            "parameterMappings": [
                mapping("recipe.upper_temperature_target", "upperTemperatureTarget"),
                mapping("recipe.lower_temperature_target", "lowerTemperatureTarget"),
                mapping("recipe.pressure_target", "pressureTarget"),
                mapping("recipe.dwell_seconds", "dwellSeconds"),
                mapping("recipe.upper_heat_compensation", "upperHeatCompensation"),
            ],
        },
        "lifecycle": {
            "mode": "discrete-cycle",
            "correlationIdContextKey": "correlation_id",
            "activeContextKey": "run_active",
            "activeValue": "true",
            "stepContextKey": "recipe_step",
            "stepNameContextKey": "recipe_step_name",
            "startedEventType": "cycle.started",
            "completedEventType": "cycle.completed",
            "stepChangedEventType": "recipe.step_changed",
            "expectedDurationMs": 8000,
        },
    }
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
