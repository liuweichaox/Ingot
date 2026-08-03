#!/usr/bin/env python3
"""Provision a second, non-molding process scenario for architecture validation."""

from __future__ import annotations

import argparse
import json
import os
import urllib.error
import urllib.request


MODEL_ID = "continuous-thermal-curing-demo"
PLAN_ID = "continuous-thermal-curing-demo-analysis"
PACKAGE_ID = "continuous-thermal-curing-demo"


def data_model(version: int = 1) -> dict[str, object]:
    return {
        "modelId": MODEL_ID,
        "version": version,
        "name": "连续热固化工艺数据模型（模拟）",
        "description": "用于验证平台对连续输送、无模具工艺的配置化支持。",
        "status": "published",
        "acquisition": {
            "dataItems": [
                {"code": "oven.zone1.actual_temperature", "sourceField": "一区实际温度", "dataType": "double", "unit": "Cel", "category": "process", "nullable": False},
                {"code": "oven.zone2.actual_temperature", "sourceField": "二区实际温度", "dataType": "double", "unit": "Cel", "category": "process", "nullable": False},
                {"code": "conveyor.actual_speed", "sourceField": "输送带实际速度", "dataType": "double", "unit": "mm/s", "category": "process", "nullable": False},
                {"code": "oven.exhaust_pressure", "sourceField": "排风压力", "dataType": "double", "unit": "Pa", "category": "environment", "nullable": True},
                {"code": "ambient.humidity", "sourceField": "入口环境湿度", "dataType": "double", "unit": "%RH", "category": "environment", "nullable": True},
            ]
        },
        "recipeParameters": [
            {"code": "oven.zone1.setpoint", "sourceField": "一区设定温度", "dataType": "double", "unit": "Cel", "nullable": False},
            {"code": "oven.zone2.setpoint", "sourceField": "二区设定温度", "dataType": "double", "unit": "Cel", "nullable": False},
            {"code": "conveyor.speed.setpoint", "sourceField": "输送带设定速度", "dataType": "double", "unit": "mm/s", "nullable": False},
        ],
    }


def recipe(version: int = 1, data_model_version: int = 1) -> dict[str, object]:
    return {
        "recipeId": "thermal-curing-baseline",
        "version": version,
        "name": "连续热固化基线配方（模拟）",
        "dataModelId": MODEL_ID,
        "dataModelVersion": data_model_version,
        "status": "published",
        "contextSelector": {"product_series": "bonded-assembly-demo"},
        "values": [
            {"code": "oven.zone1.setpoint", "value": 110.0},
            {"code": "oven.zone2.setpoint", "value": 135.0},
            {"code": "conveyor.speed.setpoint", "value": 8.0},
        ],
    }


def analysis_plan(version: int = 1, data_model_version: int = 1) -> dict[str, object]:
    return {
        "planId": PLAN_ID,
        "version": version,
        "name": "连续热固化稳定性分析（模拟）",
        "description": "按批次时间窗比较温度、输送速度与环境条件，不依赖离散模压阶段。",
        "status": "published",
        "dataModelId": MODEL_ID,
        "dataModelVersion": data_model_version,
        "analysisScope": "production-run",
        "alignmentMode": "elapsed",
        "cohortDimension": "material_lot",
        "comparisonKeys": ["product_series", "line_id", "adhesive_lot"],
        "contextSelector": {"product_series": "bonded-assembly-demo"},
        "signals": [
            {"dataItemCode": code, "includeTrace": True, "features": ["mean", "min", "max", "stddev", "slope"]}
            for code in (
                "oven.zone1.actual_temperature",
                "oven.zone2.actual_temperature",
                "conveyor.actual_speed",
                "oven.exhaust_pressure",
                "ambient.humidity",
            )
        ],
    }


def scenario_package(version: int = 1) -> dict[str, object]:
    return {
        "packageId": PACKAGE_ID,
        "version": version,
        "name": "连续热固化验证场景（模拟）",
        "description": "第二类工艺场景：连续输送、按时间窗分析、无模具上下文。",
        "status": "published",
        "dataModelId": MODEL_ID,
        "dataModelVersion": version,
        "analysisPlanId": PLAN_ID,
        "analysisPlanVersion": version,
        "acquisitionProfiles": [],
        "qualityPlan": None,
        "contextFields": [
            {"fieldCode": "line_id", "name": "生产线", "mode": "required-for-analysis", "minimumCoverage": 1.0, "minimumFactorOverlap": 0.5},
            {"fieldCode": "adhesive_lot", "name": "胶黏剂批次", "mode": "required-for-analysis", "minimumCoverage": 0.95, "minimumFactorOverlap": 0.4},
            {"fieldCode": "product_series", "name": "产品系列", "mode": "required-for-analysis", "minimumCoverage": 1.0, "minimumFactorOverlap": 0.5},
            {"fieldCode": "ambient_humidity", "name": "入口环境湿度", "mode": "record-when-available", "minimumCoverage": None, "minimumFactorOverlap": None},
        ],
        "constraints": [
            {"code": "zone2-temperature", "name": "二区温度安全范围", "severity": "hard", "unit": "Cel", "minimum": 100.0, "maximum": 160.0},
            {"code": "exhaust-pressure", "name": "排风压力下限", "severity": "hard", "unit": "Pa", "minimum": 20.0, "maximum": None},
        ],
        "knowledgeAssets": [],
        "terminology": {
            "operation_run": "固化批次时间窗",
            "tooling": "输送与加热单元",
            "quality_result": "粘接强度检验",
        },
    }


def post_json(api: str, path: str, payload: object, token: str | None = None) -> object:
    headers = {"Content-Type": "application/json; charset=utf-8"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    request = urllib.request.Request(
        f"{api.rstrip('/')}{path}",
        data=json.dumps(payload, ensure_ascii=False).encode("utf-8"),
        headers=headers,
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"POST {path} failed with HTTP {error.code}: {detail}") from error


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api", default="http://127.0.0.1:8000")
    parser.add_argument("--version", type=int, default=1)
    parser.add_argument(
        "--token",
        default=os.environ.get("INGOT_API_TOKEN"),
        help="Authorized Platform bearer token; defaults to INGOT_API_TOKEN.",
    )
    args = parser.parse_args()
    resources = [
        ("/api/v1/process-data-models", data_model(args.version)),
        ("/api/v1/recipe-versions", recipe(args.version, args.version)),
        ("/api/v1/process-analysis-plans", analysis_plan(args.version, args.version)),
        ("/api/v1/scenario-packages", scenario_package(args.version)),
    ]
    results = [post_json(args.api, path, payload, args.token) for path, payload in resources]
    print(json.dumps({"provisioned": results}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
