#!/usr/bin/env python3
"""Provision the clean optical-lens molding demo master data."""

from __future__ import annotations

import argparse
import json
import urllib.request

from demo_contract import (
    DATA_ITEMS,
    data_item_definitions,
    platform_recipe_values,
    recipe_parameter_definitions,
)
from provision_data_source import build_payload


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api", default="http://127.0.0.1:8000")
    parser.add_argument("--edge-id", default="EDGE-FX3U-SIM-001")
    parser.add_argument("--device-host", default="127.0.0.1")
    parser.add_argument("--device-port", type=int, default=5551)
    parser.add_argument("--profile-version", type=int, default=1)
    parser.add_argument("--data-model-version", type=int, default=1)
    return parser.parse_args()


def post_json(api: str, path: str, payload: object) -> object:
    request = urllib.request.Request(
        f"{api.rstrip('/')}{path}",
        data=json.dumps(payload, ensure_ascii=False).encode("utf-8"),
        headers={"Content-Type": "application/json; charset=utf-8"},
        method="POST",
    )
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.loads(response.read().decode("utf-8"))


def data_model() -> dict[str, object]:
    return {
        "modelId": "optical-lens-molding-demo",
        "version": 1,
        "name": "光学镜片模压（模拟）数据模型",
        "description": "按现场参数表建立的模拟采集量与配方参数，仅用于演示追因与优化闭环。",
        "status": "published",
        "acquisition": {
            "dataItems": data_item_definitions(),
        },
        "recipeParameters": recipe_parameter_definitions(),
    }


def recipe(version: int) -> dict[str, object]:
    return {
        "recipeId": "lens-molding-demo",
        "version": version,
        "name": (
            "LENS-DEMO 基线模压配方"
            if version == 1
            else "LENS-DEMO 验证配方（上模温度调整）"
        ),
        "basedOnVersion": None if version == 1 else 1,
        "dataModelId": "optical-lens-molding-demo",
        "dataModelVersion": 1,
        "status": "published",
        "contextSelector": {"product_series": "optical-lens-demo"},
        "values": platform_recipe_values(version),
    }


def analysis_plan() -> dict[str, object]:
    return {
        "planId": "optical-lens-molding-demo-analysis",
        "version": 1,
        "name": "光学镜片模压异常追因分析",
        "description": "按阶段比较温度、电气、压力、位置、速度和真空信号与质量结果。",
        "status": "published",
        "dataModelId": "optical-lens-molding-demo",
        "dataModelVersion": 1,
        "analysisScope": "production-cycle",
        "alignmentMode": "stage-relative",
        "cohortDimension": "recipe_version",
        "comparisonKeys": ["product_series", "machine_id", "mold_id"],
        "contextSelector": {"product_series": "optical-lens-demo"},
        "signals": [
            {
                "dataItemCode": item["code"],
                "includeTrace": True,
                "features": ["mean", "min", "max", "stddev"],
            }
            for item in DATA_ITEMS
            if item["category"] != "stage"
        ],
    }


def inspection_definition() -> dict[str, object]:
    return {
        "code": "lens.molding.quality",
        "version": 1,
        "name": "镜片模压质量（模拟）",
        "description": "模拟质检：中心厚度偏差与面形误差。",
        "characteristics": [
            {
                "code": "lens.center_thickness_deviation",
                "name": "中心厚度偏差",
                "inputType": "numeric",
                "unit": "mm",
                "lowerLimit": -0.015,
                "upperLimit": 0.015,
                "allowedValues": [],
                "required": True,
            },
            {
                "code": "lens.surface_form_error",
                "name": "面形误差",
                "inputType": "numeric",
                "unit": "um",
                "lowerLimit": None,
                "upperLimit": 0.15,
                "allowedValues": [],
                "required": True,
            },
        ],
    }


def quality_plan() -> dict[str, object]:
    return {
        "planId": "optical-lens-molding-demo-quality",
        "version": 1,
        "name": "光学镜片模压质量方案（模拟）",
        "description": "每个模压周期完成后关联一次模拟检验结果。",
        "status": "published",
        "priority": 100,
        "effectiveFrom": None,
        "effectiveTo": None,
        "scope": {
            "productSeries": "optical-lens-demo",
            "productCode": "LENS-DEMO-50",
            "recipeId": None,
            "machineId": "OPTICAL-MOLD-SIM-01",
            "contextSelector": {},
        },
        "items": [
            {
                "definitionCode": "lens.molding.quality",
                "definitionVersion": 1,
                "sequence": 1,
                "required": True,
                "requiresAttachment": False,
                "requiresReview": False,
            }
        ],
    }


def main() -> None:
    args = parse_args()
    resources = [
        ("process_data_model", "/api/v1/process-data-models", data_model()),
        ("recipe_v1", "/api/v1/recipe-versions", recipe(1)),
        ("recipe_v2", "/api/v1/recipe-versions", recipe(2)),
        ("analysis_plan", "/api/v1/process-analysis-plans", analysis_plan()),
        (
            "inspection_definition",
            "/api/v1/inspection-definitions",
            inspection_definition(),
        ),
        ("quality_plan", "/api/v1/inspection-plans", quality_plan()),
        (
            "acquisition_profile",
            "/api/v1/acquisition-profiles",
            build_payload(args),
        ),
    ]
    provisioned = []
    for name, path, payload in resources:
        result = post_json(args.api, path, payload)
        provisioned.append(
            {
                "resource": name,
                "id": (
                    result.get("modelId")
                    or result.get("recipeId")
                    or result.get("planId")
                    or result.get("code")
                    or result.get("profileId")
                ),
                "version": result.get("version"),
            }
        )
    print(json.dumps({"provisioned": provisioned}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
