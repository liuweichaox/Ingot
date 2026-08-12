#!/usr/bin/env python3
"""Provision clearly labeled simulated research assets through the platform API."""

from __future__ import annotations

import argparse
from datetime import datetime, timedelta, timezone
import hashlib
import json
import os
from pathlib import Path
import urllib.error
import urllib.request
import uuid


DATASET_ID = "optical-lens-molding-demo-training"
PROCESS_MODEL_ID = "optical-lens-thickness-demo-model"
MECHANISM_MODEL_ID = "optical-lens-thermal-response-demo"
FUSION_ID = "optical-lens-hybrid-demo"
QUALITY_DATASET_ID = "optical-lens-molding-demo-quality-check"
KNOWLEDGE_TITLE = "光学模压现场核对说明（模拟）"
DEMO_SOURCE_URI = (
    "https://github.com/liuweichaox/Ingot/blob/main/"
    "tools/optical-molding-demo/provision_research_assets.py"
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api", default="http://127.0.0.1:8000")
    parser.add_argument("--project-id", required=True)
    parser.add_argument("--username", default=os.environ.get("INGOT_ADMIN_USERNAME"))
    parser.add_argument("--password", default=os.environ.get("INGOT_ADMIN_PASSWORD"))
    return parser.parse_args()


class PlatformClient:
    def __init__(self, api: str, username: str | None, password: str | None) -> None:
        self.api = api.rstrip("/")
        self.token: str | None = None
        if username or password:
            if not username or not password:
                raise ValueError("both username and password are required")
            response = self.request_json(
                "POST",
                "/api/v1/auth/login",
                {"username": username, "password": password},
            )
            self.token = str(response["token"])

    def request_json(self, method: str, path: str, payload: object | None = None) -> dict:
        body = None if payload is None else json.dumps(payload, ensure_ascii=False).encode("utf-8")
        headers = {"Accept": "application/json"}
        if body is not None:
            headers["Content-Type"] = "application/json; charset=utf-8"
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        request = urllib.request.Request(
            f"{self.api}{path}", data=body, headers=headers, method=method
        )
        return self._open_json(request)

    def post_multipart(
        self,
        path: str,
        fields: dict[str, str],
        file_field: str,
        file_name: str,
        content_type: str,
        content: bytes,
    ) -> dict:
        boundary = f"ingot-{uuid.uuid4().hex}"
        chunks: list[bytes] = []
        for name, value in fields.items():
            chunks.extend(
                [
                    f"--{boundary}\r\n".encode(),
                    f'Content-Disposition: form-data; name="{name}"\r\n\r\n'.encode(),
                    value.encode("utf-8"),
                    b"\r\n",
                ]
            )
        chunks.extend(
            [
                f"--{boundary}\r\n".encode(),
                (
                    f'Content-Disposition: form-data; name="{file_field}"; '
                    f'filename="{file_name}"\r\n'
                ).encode(),
                f"Content-Type: {content_type}\r\n\r\n".encode(),
                content,
                b"\r\n",
                f"--{boundary}--\r\n".encode(),
            ]
        )
        headers = {
            "Accept": "application/json",
            "Content-Type": f"multipart/form-data; boundary={boundary}",
        }
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        request = urllib.request.Request(
            f"{self.api}{path}",
            data=b"".join(chunks),
            headers=headers,
            method="POST",
        )
        return self._open_json(request)

    @staticmethod
    def _open_json(request: urllib.request.Request) -> dict:
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                return json.loads(response.read().decode("utf-8"))
        except urllib.error.HTTPError as error:
            detail = error.read().decode("utf-8", errors="replace")
            raise RuntimeError(
                f"{request.method} {request.full_url} failed with HTTP {error.code}: {detail}"
            ) from error


def training_dataset(now: datetime, content_hash: str) -> dict[str, object]:
    return {
        "datasetId": DATASET_ID,
        "version": 1,
        "name": "光学模压中心厚度训练快照（模拟）",
        "analysisPlanId": "optical-lens-molding-demo-analysis",
        "analysisPlanVersion": 1,
        "dataModelId": "optical-lens-molding-demo",
        "dataModelVersion": 1,
        "contextSelector": {"product_family_code": "optical-lens-demo"},
        "processExecutionIds": [],
        "featureCodes": ["upper_mold.temperature.mean", "pressure.load.mean"],
        "targetCode": "lens.center_thickness_deviation",
        "windowStart": (now - timedelta(days=30)).isoformat(),
        "windowEnd": now.isoformat(),
        "rowCount": 24,
        "contentHash": content_hash,
    }


def process_model() -> dict[str, object]:
    return {
        "modelId": PROCESS_MODEL_ID,
        "version": 1,
        "name": "中心厚度偏差解释模型（模拟）",
        "modelKind": "quality-risk",
        "problemCode": "lens.center-thickness-deviation",
        "algorithm": "regularized-linear-regression-demo",
        "datasetId": DATASET_ID,
        "datasetVersion": 1,
        "contextSelector": {"product_family_code": "optical-lens-demo"},
        "inputFeatureCodes": ["upper_mold.temperature.mean", "pressure.load.mean"],
        "outputCode": "lens.center_thickness_deviation",
        "uncertaintyMethod": "bootstrap-demo",
        "changeNote": "仅用于页面与契约验收，不代表已验证工艺模型。",
    }


def mechanism_model() -> dict[str, object]:
    return {
        "modelId": MECHANISM_MODEL_ID,
        "version": 1,
        "name": "上模温度响应近似式（模拟）",
        "equationKind": "affine",
        "inputs": [
            {
                "code": "upper_mold.temperature.mean",
                "unit": "Cel",
                "validMinimum": 500,
                "validMaximum": 700,
            }
        ],
        "output": {
            "code": "lens.center_thickness_deviation",
            "unit": "mm",
            "validMinimum": -0.05,
            "validMaximum": 0.05,
        },
        "intercept": 0.08,
        "coefficients": {"upper_mold.temperature.mean": -0.00012},
        "applicabilityContext": {"product_family_code": "optical-lens-demo"},
        "scientificBasis": "模拟线性近似，仅用于验证机理资产的版本、边界和审计流程。",
        "sourceReference": "optical-molding-demo-v1",
    }


def fusion_definition() -> dict[str, object]:
    return {
        "fusionId": FUSION_ID,
        "version": 1,
        "name": "温度机理与数据模型融合（模拟）",
        "mode": "ensemble",
        "mechanismModelId": MECHANISM_MODEL_ID,
        "mechanismModelVersion": 1,
        "dataModelId": "optical-lens-molding-demo",
        "dataModelVersion": 1,
        "mechanismWeight": 0.35,
        "outputCode": "lens.center_thickness_deviation",
        "applicabilityContext": {"product_family_code": "optical-lens-demo"},
    }


def quality_csv() -> bytes:
    rows = ["execution,upper_temperature,center_deviation"]
    rows.extend(
        f"demo-{index:02d},{618 + index * 0.5:.1f},{0.012 - index * 0.0005:.4f}"
        for index in range(1, 13)
    )
    return ("\n".join(rows) + "\n").encode("utf-8")


def quality_manifest(content: bytes) -> dict[str, object]:
    return {
        "datasetId": QUALITY_DATASET_ID,
        "version": 1,
        "industry": "optical-manufacturing-demo",
        "process": "precision-molding-demo",
        "dataKind": "simulated-validation",
        "isMeasuredData": False,
        "sourceUri": DEMO_SOURCE_URI,
        "license": "internal-demo",
        "citation": (
            "Ingot optical molding simulated UI validation dataset v1; "
            "generated by provision_research_assets.py"
        ),
        "expectedSha256": hashlib.sha256(content).hexdigest(),
        "processExecutionColumn": "execution",
        "signalColumns": ["upper_temperature"],
        "outcomeColumns": ["center_deviation"],
        "units": {"upper_temperature": "Cel", "center_deviation": "mm"},
        "validSignalRanges": {
            "upper_temperature": {
                "minimum": 500,
                "maximum": 700,
                "basis": "模拟设备契约的验收范围。",
            }
        },
    }


def main() -> None:
    args = parse_args()
    client = PlatformClient(args.api, args.username, args.password)
    existing_datasets = client.request_json("GET", "/api/v1/training-datasets").get("data", [])
    dataset_bytes = quality_csv()
    dataset_hash = hashlib.sha256(dataset_bytes).hexdigest()
    created: list[str] = []

    if not any(item.get("datasetId") == DATASET_ID and item.get("version") == 1 for item in existing_datasets):
        client.request_json("POST", "/api/v1/training-datasets", training_dataset(datetime.now(timezone.utc), dataset_hash))
        created.append("training-dataset")

    models = client.request_json("GET", "/api/v1/process-models").get("data", [])
    if not any(item.get("modelId") == PROCESS_MODEL_ID and item.get("version") == 1 for item in models):
        client.request_json("POST", "/api/v1/process-models", process_model())
        created.append("process-model")

    mechanisms = client.request_json("GET", "/api/v1/mechanism-models").get("data", [])
    mechanism = next(
        (
            item
            for item in mechanisms
            if item.get("modelId") == MECHANISM_MODEL_ID and item.get("version") == 1
        ),
        None,
    )
    if mechanism is None:
        client.request_json("POST", "/api/v1/mechanism-models", mechanism_model())
        created.append("mechanism-model")
        mechanism = {"status": "draft"}
    if mechanism.get("status") == "draft":
        client.request_json(
            "POST",
            f"/api/v1/mechanism-models/{MECHANISM_MODEL_ID}/1/status",
            {"targetStatus": "validated"},
        )
        created.append("mechanism-model-validated")

    fusions = client.request_json("GET", "/api/v1/mechanism-fusions").get("data", [])
    fusion = next(
        (
            item
            for item in fusions
            if item.get("fusionId") == FUSION_ID and item.get("version") == 1
        ),
        None,
    )
    if fusion is None:
        client.request_json("POST", "/api/v1/mechanism-fusions", fusion_definition())
        created.append("mechanism-fusion")

    knowledge = client.request_json(
        "GET", f"/api/v1/process-knowledge?projectId={args.project_id}"
    ).get("data", [])
    if not any(item.get("title") == KNOWLEDGE_TITLE for item in knowledge):
        note = (
            "# 光学模压现场核对说明（模拟）\n\n"
            "本说明只用于验收知识上传、抽取和项目隔离。中心厚度异常必须结合质量结果、"
            "温度、压力和工装上下文验证，不能由单一相关性直接判定因果。\n"
        ).encode("utf-8")
        client.post_multipart(
            "/api/v1/process-knowledge",
            {
                "projectId": args.project_id,
                "title": KNOWLEDGE_TITLE,
                "sourceKind": "field-note",
            },
            "file",
            "optical-molding-field-note-demo.md",
            "text/markdown",
            note,
        )
        created.append("knowledge-source")

    reports = client.request_json("GET", "/api/v1/dataset-quality-validations").get("data", [])
    if not any(item.get("datasetId") == QUALITY_DATASET_ID and item.get("datasetVersion") == 1 for item in reports):
        client.post_multipart(
            "/api/v1/dataset-quality-validations",
            {"manifestJson": json.dumps(quality_manifest(dataset_bytes), ensure_ascii=False)},
            "file",
            "optical-molding-quality-check-demo.csv",
            "text/csv",
            dataset_bytes,
        )
        created.append("dataset-quality-report")

    print(json.dumps({"created": created}, ensure_ascii=False))


if __name__ == "__main__":
    main()
