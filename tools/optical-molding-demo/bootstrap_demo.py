#!/usr/bin/env python3
"""Provision the clean optical-lens molding demo master data."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
import urllib.error
import urllib.parse
import urllib.request
import uuid

from demo_contract import (
    DATA_ITEMS,
    data_item_definitions,
    platform_recipe_values,
    recipe_parameter_definitions,
)
from provision_data_source import build_payload


TOOLING_COMPONENT_TYPES = [
    ("mold.core", "模芯"),
    ("mold.frame", "模架"),
]

TOOLING_ROLES = [
    {
        "code": "upper.core",
        "name": "上模芯",
        "required": True,
        "maxCount": 1,
        "sortOrder": 1,
        "acceptedComponentTypeCodes": ["mold.core"],
    },
    {
        "code": "upper.frame",
        "name": "上模架",
        "required": True,
        "maxCount": 1,
        "sortOrder": 2,
        "acceptedComponentTypeCodes": ["mold.frame"],
    },
    {
        "code": "lower.core",
        "name": "下模芯",
        "required": True,
        "maxCount": 1,
        "sortOrder": 3,
        "acceptedComponentTypeCodes": ["mold.core"],
    },
    {
        "code": "lower.frame",
        "name": "下模架",
        "required": True,
        "maxCount": 1,
        "sortOrder": 4,
        "acceptedComponentTypeCodes": ["mold.frame"],
    },
]

TOOLING_COMPONENTS = [
    {
        "componentId": "CORE-UC-A01",
        "componentTypeCode": "mold.core",
        "serialNo": "SIM-UC-A01",
        "name": "光学上模芯 UC-A01",
        "status": "available",
        "attributes": {
            "manufacturer": "Ingot Demo Tooling",
            "productCode": "OC-50-UPPER-CORE",
            "model": "50mm Aspheric Core",
            "designRevision": "A",
            "material": "SUS440C",
            "dataClassification": "simulated",
        },
    },
    {
        "componentId": "FRAME-UF-A01",
        "componentTypeCode": "mold.frame",
        "serialNo": "SIM-UF-A01",
        "name": "光学上模架 UF-A01",
        "status": "available",
        "attributes": {
            "manufacturer": "Ingot Demo Tooling",
            "productCode": "OC-50-UPPER-FRAME",
            "model": "50mm Upper Frame",
            "designRevision": "A",
            "material": "H13",
            "dataClassification": "simulated",
        },
    },
    {
        "componentId": "CORE-LC-A01",
        "componentTypeCode": "mold.core",
        "serialNo": "SIM-LC-A01",
        "name": "光学下模芯 LC-A01",
        "status": "available",
        "attributes": {
            "manufacturer": "Ingot Demo Tooling",
            "productCode": "OC-50-LOWER-CORE",
            "model": "50mm Aspheric Core",
            "designRevision": "A",
            "material": "SUS440C",
            "dataClassification": "simulated",
        },
    },
    {
        "componentId": "FRAME-LF-A01",
        "componentTypeCode": "mold.frame",
        "serialNo": "SIM-LF-A01",
        "name": "光学下模架 LF-A01",
        "status": "available",
        "attributes": {
            "manufacturer": "Ingot Demo Tooling",
            "productCode": "OC-50-LOWER-FRAME",
            "model": "50mm Lower Frame",
            "designRevision": "A",
            "material": "H13",
            "dataClassification": "simulated",
        },
    },
]

TOOLING_MEMBERS = [
    {"roleCode": "upper.core", "componentId": "CORE-UC-A01"},
    {"roleCode": "upper.frame", "componentId": "FRAME-UF-A01"},
    {"roleCode": "lower.core", "componentId": "CORE-LC-A01"},
    {"roleCode": "lower.frame", "componentId": "FRAME-LF-A01"},
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api", default="http://127.0.0.1:8000")
    parser.add_argument("--edge-id", default="EDGE-FX3U-SIM-001")
    parser.add_argument("--device-host", default="127.0.0.1")
    parser.add_argument("--device-port", type=int, default=5551)
    parser.add_argument("--profile-version", type=int, default=1)
    parser.add_argument("--data-model-version", type=int, default=1)
    parser.add_argument("--scenario-version", type=int)
    return parser.parse_args()


def post_json(api: str, path: str, payload: object) -> object:
    request = urllib.request.Request(
        f"{api.rstrip('/')}{path}",
        data=json.dumps(payload, ensure_ascii=False).encode("utf-8"),
        headers={"Content-Type": "application/json; charset=utf-8"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"POST {path} failed with HTTP {error.code}: {detail}") from error


def get_json(api: str, path: str) -> object:
    with urllib.request.urlopen(f"{api.rstrip('/')}{path}", timeout=30) as response:
        return json.loads(response.read().decode("utf-8"))


def data_model(version: int = 1) -> dict[str, object]:
    return {
        "modelId": "optical-lens-molding-demo",
        "version": version,
        "name": "光学镜片模压（模拟）数据模型",
        "description": "按现场参数表建立的模拟采集量与配方参数，仅用于演示追因与优化闭环。",
        "status": "published",
        "acquisition": {
            "dataItems": data_item_definitions(),
        },
        "controlParameters": recipe_parameter_definitions(),
    }


def recipe(
    version: int,
    data_model_version: int = 1,
    *,
    based_on_version: int | None = None,
    variant: int = 1,
) -> dict[str, object]:
    return {
        "processSpecificationId": "lens-molding-demo",
        "version": version,
        "name": (
            "LENS-DEMO 基线模压配方"
            if variant == 1
            else "LENS-DEMO 验证配方（上模温度调整）"
        ),
        "basedOnVersion": based_on_version,
        "dataModelId": "optical-lens-molding-demo",
        "dataModelVersion": data_model_version,
        "status": "published",
        "contextSelector": {"product_family_code": "optical-lens-demo"},
        "values": platform_recipe_values(variant),
    }


def analysis_plan(version: int = 1, data_model_version: int = 1) -> dict[str, object]:
    return {
        "planId": "optical-lens-molding-demo-analysis",
        "version": version,
        "name": "光学镜片模压异常追因分析",
        "description": "按阶段比较温度、电气、压力、位置、速度和真空信号与质量结果。",
        "status": "published",
        "dataModelId": "optical-lens-molding-demo",
        "dataModelVersion": data_model_version,
        "analysisScope": "production-execution",
        "alignmentMode": "stage-relative",
        "cohortDimension": "process_specification_version",
        "comparisonKeys": ["product_family_code", "equipment_id", "tooling_assembly_id"],
        "contextSelector": {"product_family_code": "optical-lens-demo"},
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
            "productFamilyCode": "optical-lens-demo",
            "productCode": "LENS-DEMO-50",
            "processSpecificationId": None,
            "equipmentId": "OPTICAL-MOLD-SIM-01",
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


def scenario_package(
    data_model_version: int,
    profile_version: int,
    scenario_version: int | None = None,
) -> dict[str, object]:
    return {
        "packageId": "optical-lens-molding-demo",
        "version": scenario_version or data_model_version,
        "name": "精密模压验证场景（模拟）",
        "description": "首个版本化验证场景，组合工艺模型、采集映射、分析、质量与上下文证据策略。",
        "status": "published",
        "dataModelId": "optical-lens-molding-demo",
        "dataModelVersion": data_model_version,
        "analysisPlanId": "optical-lens-molding-demo-analysis",
        "analysisPlanVersion": data_model_version,
        "acquisitionProfiles": [
            {"id": "optical-lens-molding-simulator", "version": profile_version}
        ],
        "qualityPlan": {"id": "optical-lens-molding-demo-quality", "version": 1},
        "contextFields": [
            {"fieldCode": "equipment_id", "name": "设备", "mode": "required-for-analysis", "minimumCoverage": 1.0, "minimumFactorOverlap": 0.5},
            {"fieldCode": "assembly_revision", "name": "工装版本", "mode": "record-when-available", "minimumCoverage": None, "minimumFactorOverlap": None},
            {"fieldCode": "tooling_usage_count", "name": "工装累计运行次数", "mode": "record-when-available", "minimumCoverage": None, "minimumFactorOverlap": None},
            {"fieldCode": "material_lot_ref", "name": "材料批次", "mode": "record-when-available", "minimumCoverage": None, "minimumFactorOverlap": None},
            {"fieldCode": "calibration_status", "name": "校准状态", "mode": "required-for-analysis", "minimumCoverage": 0.95, "minimumFactorOverlap": None},
            {"fieldCode": "maintenance_status", "name": "维护状态", "mode": "record-when-available", "minimumCoverage": None, "minimumFactorOverlap": None},
        ],
        "constraints": [],
        "knowledgeAssets": [],
        "terminology": {
            "process_execution": "模压周期",
            "tooling": "模具组合",
            "quality_result": "镜片检验结果",
        },
    }


def provision_manufacturing_context(
    api: str, process_specification_version: int
) -> list[dict[str, object]]:
    """Create the active tooling and production intervals required by run snapshots.

    Master data uses upsert endpoints. Immutable revisions and active intervals are
    looked up and reused, so rerunning the demo does not create overlapping facts.
    """
    for component_type_code, name in TOOLING_COMPONENT_TYPES:
        post_json(
            api,
            "/api/v1/tooling-component-types",
            {
                "componentTypeCode": component_type_code,
                "name": f"{name}（模拟）",
                "status": "active",
                "attributes": {"dataClassification": "simulated"},
            },
        )
    tooling_types = get_json(api, "/api/v1/tooling-types").get("data", [])
    if not any(
        item.get("toolingTypeCode") == "optical-molding.four-part-mold"
        and item.get("version") == 1
        for item in tooling_types
    ):
        post_json(
            api,
            "/api/v1/tooling-types",
            {
                "toolingTypeCode": "optical-molding.four-part-mold",
                "version": 1,
                "name": "光学模压四件式模具装配模板（模拟）",
                "status": "active",
                "roles": TOOLING_ROLES,
            },
        )
    for component in TOOLING_COMPONENTS:
        post_json(api, "/api/v1/tooling-components", component)
    post_json(
        api,
        "/api/v1/tooling-assemblies",
        {
            "toolingAssemblyId": "MOLD-DEMO-A01",
            "toolingTypeCode": "optical-molding.four-part-mold",
            "name": "光学模压模具 A01（模拟）",
            "status": "active",
        },
    )
    revisions = get_json(
        api, "/api/v1/tooling-assemblies/MOLD-DEMO-A01/revisions"
    ).get("data", [])
    revision = next((item for item in revisions if item.get("revision") == 1), None)
    if revision is None:
        revision = post_json(
            api,
            "/api/v1/tooling-assemblies/MOLD-DEMO-A01/revisions",
            {
                "assemblyRevisionId": "bd1a0a54-54c1-4d03-8b6a-8ff25934dcf1",
                "toolingAssemblyId": "MOLD-DEMO-A01",
                "revision": 1,
                "members": TOOLING_MEMBERS,
                "createdBy": "optical-molding-demo",
                "createdAt": "2020-01-01T00:00:00Z",
            },
        )
    query = urllib.parse.urlencode(
        {"equipmentId": "OPTICAL-MOLD-SIM-01", "activeOnly": "true"}
    )
    installations = get_json(api, f"/api/v1/tooling-installations?{query}").get(
        "data", []
    )
    installation = next(
        (
            item
            for item in installations
            if item.get("assemblyRevisionId") == revision["assemblyRevisionId"]
        ),
        None,
    )
    if installation is None:
        installation = post_json(
            api,
            "/api/v1/tooling-installations",
            {
                "installationId": "9b274e27-3de8-4505-a7d8-c54ca34a82e7",
                "equipmentId": "OPTICAL-MOLD-SIM-01",
                "assemblyRevisionId": revision["assemblyRevisionId"],
                "installedAt": "2020-01-01T00:00:00Z",
                "source": "import",
                "commandId": "optical-molding-demo-installation-v1",
                "userId": "optical-molding-demo",
            },
        )
    contexts = get_json(
        api, f"/api/v1/production-contexts?{query}"
    ).get("data", [])
    context = next(
        (
            item
            for item in contexts
            if item.get("toolingInstallationId") == installation["installationId"]
            and item.get("processSpecificationId") == "lens-molding-demo"
            and item.get("processSpecificationVersion") == str(process_specification_version)
        ),
        None,
    )
    if context is None:
        switched_at = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
        valid_from = switched_at if contexts else "2020-01-01T00:00:00Z"
        for active in contexts:
            post_json(
                api,
                f"/api/v1/production-contexts/{active['contextId']}:close",
                {"at": switched_at, "actor": "optical-molding-demo"},
            )
        context_id = str(
            uuid.uuid5(
                uuid.UUID("98b41076-45fe-41b6-83d4-dcba7f812e2c"),
                f"optical-molding-demo-production-{process_specification_version}",
            )
        )
        context = post_json(
            api,
            "/api/v1/production-contexts",
            {
                "contextId": context_id,
                "equipmentId": "OPTICAL-MOLD-SIM-01",
                "productFamilyCode": "optical-lens-demo",
                "productCode": "LENS-DEMO-50",
                "processSpecificationId": "lens-molding-demo",
                "processSpecificationVersion": str(process_specification_version),
                "toolingInstallationId": installation["installationId"],
                "validFrom": valid_from,
                "source": "import",
                "commandId": f"optical-molding-demo-production-v{process_specification_version}",
                "externalOrderRef": "DEMO-ORDER-001",
                "externalBatchRef": "DEMO-BATCH-001",
                "materialLotRef": "GLASS-DEMO-01",
                "materialSpecification": "OPTICAL-GLASS-DEMO",
                "maintenanceStatus": "available",
                "calibrationStatus": "valid",
                "calibrationRef": "DEMO-CAL-2026-001",
                "calibrationValidUntil": "2030-01-01T00:00:00Z",
                "userId": "optical-molding-demo",
            },
        )
    return [
        {"resource": "tooling_revision", "id": revision["assemblyRevisionId"], "version": 1},
        {"resource": "tooling_installation", "id": installation["installationId"], "version": None},
        {"resource": "production_context", "id": context["contextId"], "version": None},
    ]


def main() -> None:
    args = parse_args()
    baseline_process_specification_version = (args.data_model_version - 1) * 2 + 1
    resources = [
        (
            "process_data_model",
            "/api/v1/process-data-models",
            data_model(args.data_model_version),
        ),
        (
            "recipe_v1",
            "/api/v1/process-specifications",
            recipe(
                baseline_process_specification_version,
                args.data_model_version,
                variant=1,
            ),
        ),
        (
            "analysis_plan",
            "/api/v1/process-analysis-plans",
            analysis_plan(args.data_model_version, args.data_model_version),
        ),
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
        (
            "scenario_package",
            "/api/v1/scenario-packages",
            scenario_package(
                args.data_model_version,
                args.profile_version,
                args.scenario_version,
            ),
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
                    or result.get("processSpecificationId")
                    or result.get("planId")
                    or result.get("code")
                    or result.get("profileId")
                    or result.get("packageId")
                ),
                "version": result.get("version"),
            }
        )
    provisioned.extend(
        provision_manufacturing_context(args.api, baseline_process_specification_version)
    )
    print(json.dumps({"provisioned": provisioned}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
