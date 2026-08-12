#!/usr/bin/env python3
"""Verify every demo process-context field from configuration to a real run."""

from __future__ import annotations

import argparse
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request


SYSTEM_SOURCES = {
    "execution_id": "Edge lifecycle: UUIDv7 generated when run_active becomes 1",
    "equipment_id": "ingestion task: equipment subjectId",
    "tooling_usage_count": "Platform: tooling usage counter at execution start",
}

MANUFACTURING_SOURCES = {
    "product_family_code": "Platform production context: productFamilyCode",
    "product_code": "Platform production context: productCode",
    "process_specification_id": "device recipe readback + Platform production context",
    "process_specification_version": "device recipe readback + Platform production context",
    "tooling_assembly_id": "device register + Platform tooling installation",
    "assembly_revision": "Platform tooling assembly revision",
    "material_lot_ref": "device register + Platform production context",
    "maintenance_status": "Platform production context: maintenanceStatus",
    "calibration_status": "Platform production context: calibrationStatus/validUntil",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api", default="http://127.0.0.1:8000")
    parser.add_argument("--username", default=os.environ.get("INGOT_ADMIN_USERNAME"))
    parser.add_argument("--password", default=os.environ.get("INGOT_ADMIN_PASSWORD"))
    parser.add_argument("--scenario-id", default="optical-lens-molding-demo")
    parser.add_argument("--scenario-version", type=int, default=2)
    parser.add_argument("--equipment-id", default="OPTICAL-MOLD-SIM-01")
    parser.add_argument("--execution-id")
    parser.add_argument("--json", action="store_true", dest="as_json")
    return parser.parse_args()


class PlatformClient:
    def __init__(self, api: str, username: str | None, password: str | None) -> None:
        self.api = api.rstrip("/")
        self.token: str | None = None
        if username or password:
            if not username or not password:
                raise ValueError("both --username and --password are required")
            response = self.request(
                "POST",
                "/api/v1/auth/login",
                {"username": username, "password": password},
            )
            token = response.get("token")
            if not isinstance(token, str) or not token:
                raise RuntimeError("local authentication did not return a token")
            self.token = token

    def request(self, method: str, path: str, payload: object | None = None) -> dict:
        body = None if payload is None else json.dumps(payload).encode("utf-8")
        headers = {"Accept": "application/json"}
        if body is not None:
            headers["Content-Type"] = "application/json"
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        request = urllib.request.Request(
            f"{self.api}{path}", data=body, headers=headers, method=method
        )
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                return json.loads(response.read().decode("utf-8"))
        except urllib.error.HTTPError as error:
            detail = error.read().decode("utf-8", errors="replace")
            raise RuntimeError(
                f"{method} {path} failed with HTTP {error.code}: {detail}"
            ) from error


def configured_sources(task: dict) -> dict[str, str]:
    sources = dict(SYSTEM_SOURCES)
    sources.update(MANUFACTURING_SOURCES)
    for mapping in task.get("contextMappings", []):
        key = mapping.get("contextKey")
        path = mapping.get("sourcePath")
        if key and path:
            device = f"device mapping: {path}"
            sources[key] = f"{device}; {sources[key]}" if key in sources else device
    return sources


def runtime_context(detail: dict) -> dict[str, str]:
    started = next(
        (
            row.get("event", {})
            for row in detail.get("events", [])
            if str(row.get("event", {}).get("eventType", "")).endswith(".started")
        ),
        None,
    )
    if not started:
        raise RuntimeError("execution detail has no *.started event")
    context = dict(started.get("context") or {})
    execution_id = started.get("executionId") or detail.get("executionId")
    if execution_id:
        context.setdefault("execution_id", str(execution_id))
    return {str(key): str(value) for key, value in context.items() if value is not None}


def audit_rows(scenario: dict, task: dict, context: dict[str, str]) -> list[dict[str, str]]:
    sources = configured_sources(task)
    rows = []
    for field in scenario.get("contextFields", []):
        code = str(field.get("fieldCode", ""))
        value = context.get(code, "").strip()
        source = sources.get(code, "")
        rows.append(
            {
                "field": code,
                "mode": str(field.get("mode", "")),
                "source": source,
                "value": value,
                "status": "PASS" if source and value else "FAIL",
            }
        )
    return rows


def latest_execution_id(client: PlatformClient, equipment_id: str) -> str:
    query = urllib.parse.urlencode(
        {"equipmentId": equipment_id, "status": "completed", "limit": 20}
    )
    response = client.request("GET", f"/api/v1/process-executions?{query}")
    executions = response.get("data", [])
    if not executions:
        raise RuntimeError(f"no completed execution found for {equipment_id}")
    execution_id = executions[0].get("executionId")
    if not execution_id:
        raise RuntimeError("latest execution has no executionId")
    return str(execution_id)


def print_table(rows: list[dict[str, str]]) -> None:
    print(f"{'STATUS':<6} {'FIELD':<31} {'VALUE':<24} SOURCE")
    for row in rows:
        value = row["value"] if len(row["value"]) <= 22 else row["value"][:19] + "..."
        print(f"{row['status']:<6} {row['field']:<31} {value:<24} {row['source']}")


def main() -> None:
    args = parse_args()
    client = PlatformClient(args.api, args.username, args.password)
    scenario = client.request(
        "GET", f"/api/v1/scenario-packages/{args.scenario_id}/{args.scenario_version}"
    )
    references = scenario.get("ingestionTasks", [])
    if len(references) != 1:
        raise RuntimeError("demo scenario must reference exactly one ingestion task")
    reference = references[0]
    task = client.request(
        "GET", f"/api/v1/ingestion-tasks/{reference['id']}/{reference['version']}"
    )
    execution_id = args.execution_id or latest_execution_id(client, args.equipment_id)
    detail = client.request("GET", f"/api/v1/process-executions/{execution_id}")
    context = runtime_context(detail)
    rows = audit_rows(scenario, task, context)
    result = {
        "scenario": f"{args.scenario_id}:{args.scenario_version}",
        "ingestionTask": f"{reference['id']}:{reference['version']}",
        "executionId": execution_id,
        "contextCaptureStatus": context.get("context_capture_status"),
        "passed": all(row["status"] == "PASS" for row in rows)
        and context.get("context_capture_status") == "resolved",
        "fields": rows,
    }
    if args.as_json:
        print(json.dumps(result, ensure_ascii=False, indent=2))
    else:
        print(
            f"scenario={result['scenario']} task={result['ingestionTask']} "
            f"execution={execution_id} context={result['contextCaptureStatus']}"
        )
        print_table(rows)
        print("PASS: complete context chain" if result["passed"] else "FAIL: context chain is incomplete")
    raise SystemExit(0 if result["passed"] else 1)


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, ValueError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        raise SystemExit(1) from error
