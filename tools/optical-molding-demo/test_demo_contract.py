from argparse import Namespace
from datetime import datetime, timezone
import hashlib
import socket
import threading

from bootstrap_demo import (
    TOOLING_COMPONENTS,
    TOOLING_COMPONENT_TYPES,
    TOOLING_MEMBERS,
    TOOLING_ROLES,
    analysis_plan,
    data_model,
    recipe,
    scenario_package,
)
from demo_contract import DATA_ITEMS, RECIPE_PARAMETERS, device_recipe_values
from device_simulator import (
    Fx3uRegisterBank,
    Fx3uServer,
    Simulator,
    handler,
    parse_args as parse_device_args,
    values,
)
from provision_ingestion_task import build_payload
import provision_ingestion_task
from provision_research_assets import (
    fusion_definition,
    mechanism_model,
    process_model,
    quality_csv,
    quality_manifest,
    training_dataset,
)
import submit_quality
from submit_quality import authenticate, parse_args as parse_quality_args, read_source_execution_number
from verify_context_chain import audit_rows


def test_sensor_and_recipe_contract_matches_reference_parameter_lists():
    assert [item["displayName"] for item in DATA_ITEMS] == [
        "阶段号",
        "上模红外温度",
        "上模电流",
        "上模电压",
        "下模红外温度",
        "下模电流",
        "下模电压",
        "压力",
        "光栅位置",
        "伺服速度",
        "真空度",
        "伺服位置",
        "上模功率",
        "下模功率",
    ]
    assert [item["displayName"] for item in RECIPE_PARAMETERS] == [
        "HEAT位置",
        "WORK位置",
        "HOST位置",
        "上模设置温度",
        "下模设置温度",
        "充氮气温度",
        "预热保温延时",
        "压力差上限",
        "上模温度上限",
        "下模温度上限",
        "压力上限",
        "WORK位设定压力",
    ]
    assert len(data_model()["acquisition"]["dataItems"]) == 14
    assert "stages" not in data_model()
    assert len(data_model()["controlParameters"]) == 12
    assert len(recipe(1)["values"]) == 12


def test_demo_tooling_separates_asset_classification_from_assembly_position():
    assert TOOLING_COMPONENT_TYPES == [
        ("mold.core", "模芯"),
        ("mold.frame", "模架"),
    ]
    roles = {item["code"]: item for item in TOOLING_ROLES}
    assert roles["upper.core"]["acceptedComponentTypeCodes"] == ["mold.core"]
    assert roles["lower.core"]["acceptedComponentTypeCodes"] == ["mold.core"]
    assert roles["upper.frame"]["acceptedComponentTypeCodes"] == ["mold.frame"]
    assert roles["lower.frame"]["acceptedComponentTypeCodes"] == ["mold.frame"]
    components = {item["componentId"]: item for item in TOOLING_COMPONENTS}
    assert len(components) == 4
    assert {item["componentTypeCode"] for item in components.values()} == {
        "mold.core",
        "mold.frame",
    }
    assert all("lifecycleCount" not in item["attributes"] for item in components.values())
    assert {item["roleCode"] for item in TOOLING_MEMBERS} == set(roles)
    assert {item["componentId"] for item in TOOLING_MEMBERS} == set(components)


def test_device_snapshot_and_ingestion_task_cover_every_declared_field():
    snapshot_values = values("baseline", 0.5)
    task = build_payload(
        Namespace(
            edge_id="EDGE-FX3U-SIM-001",
            device_host="127.0.0.1",
            device_port=5551,
            task_version=1,
            data_model_version=1,
        )
    )

    assert task["protocol"] == "melsec-a1e"
    assert task["melsecA1E"]["port"] == 5551
    assert task["melsecA1E"]["dataCode"] == "binary"
    assert task["melsecA1E"]["pcNumber"] == 255
    assert len(task["valueMappings"]) == len(DATA_ITEMS)
    assert len(task["processSpecification"]["parameterMappings"]) == len(RECIPE_PARAMETERS)
    assert task["valueMappings"][0]["sourcePath"] == "D:1:uint16"
    assert all(
        mapping.get("sourceUnit") == definition["unit"]
        for mapping, definition in zip(task["valueMappings"], DATA_ITEMS, strict=True)
        if definition["unit"] is not None
    )
    assert not any(
        item["contextKey"] == "stage_number"
        for item in task["contextMappings"]
    )
    assert "executionIdContextKey" not in task["lifecycle"]
    assert not any(
        item["contextKey"] == "execution_id"
        for item in task["contextMappings"]
    )
    assert any(
        item["contextKey"] == "source_execution_no" and item["sourcePath"] == "D:2:uint32"
        for item in task["contextMappings"]
    )
    assert set(task["staticContext"]) == {"demo_replay", "data_classification"}
    assert {
        "product_code": "D:30:string:20",
        "product_family_code": "D:40:string:20",
        "tooling_assembly_id": "D:50:string:20",
        "material_lot_ref": "D:60:string:20",
        "output_item_id": "D:70:string:40",
    }.items() <= {
        item["contextKey"]: item["sourcePath"]
        for item in task["contextMappings"]
    }.items()
    assert set(device_recipe_values(1)) == {
        item["sourcePath"] for item in RECIPE_PARAMETERS
    }
    assert snapshot_values["upperMold"]["power"] > 0
    assert snapshot_values["lowerMold"]["power"] > 0
    assert snapshot_values["pressure"]["load"] > 0


def test_ingestion_task_can_identify_an_independent_second_device():
    task = build_payload(
        Namespace(
            edge_id="EDGE-01",
            task_id="second-device",
            subject_id="PRESS-02",
            source="connector/melsec-a1e/press-02",
            device_host="press-02.internal",
            device_port=5552,
            task_version=1,
            data_model_version=1,
        )
    )

    assert task["taskId"] == "second-device"
    assert task["subjectId"] == "PRESS-02"
    assert task["source"] == "connector/melsec-a1e/press-02"


def test_device_can_offset_process_specification_versions_for_a_new_data_model_generation():
    simulator = Simulator(
        execution_seconds=60,
        run_prefix="stage-number",
        max_runs=1,
        process_specification_version_offset=2,
    )

    assert simulator.snapshot()["activeRecipe"]["version"] == 3


def test_bootstrap_keeps_model_recipe_and_analysis_versions_aligned():
    assert data_model(3)["version"] == 3
    assert recipe(5, 3, variant=1)["dataModelVersion"] == 3
    assert recipe(6, 3, based_on_version=5, variant=2)["basedOnVersion"] == 5
    assert analysis_plan(3, 3)["version"] == 3
    assert analysis_plan(3, 3)["dataModelVersion"] == 3
    package = scenario_package(3, 2)
    assert package["version"] == 3
    assert package["dataModelVersion"] == 3
    assert package["analysisPlanVersion"] == 3
    assert package["ingestionTasks"] == [
        {"id": "optical-lens-molding-simulator", "version": 2}
    ]
    assert any(
        field["fieldCode"] == "calibration_status"
        and field["mode"] == "required-for-analysis"
        for field in package["contextFields"]
    )
    assert {field["fieldCode"] for field in package["contextFields"]} >= {
        "execution_id",
        "product_family_code",
        "product_code",
        "process_specification_id",
        "process_specification_version",
        "output_item_id",
        "tooling_assembly_id",
        "assembly_revision",
        "material_lot_ref",
    }


def test_every_scenario_context_field_has_a_provider_and_runtime_value():
    package = scenario_package(1, 1)
    task = build_payload(
        Namespace(
            edge_id="EDGE-FX3U-SIM-001",
            device_host="127.0.0.1",
            device_port=5551,
            task_version=1,
            data_model_version=1,
        )
    )
    runtime = {
        "execution_id": "019f-demo-run",
        "equipment_id": "OPTICAL-MOLD-SIM-01",
        "product_family_code": "optical-lens-demo",
        "product_code": "LENS-DEMO-50",
        "process_specification_id": "lens-molding-demo",
        "process_specification_version": "1",
        "output_item_id": "LENS-DEMO-0001",
        "tooling_assembly_id": "MOLD-DEMO-A01",
        "assembly_revision": "1",
        "tooling_usage_count": "1",
        "material_lot_ref": "GLASS-DEMO-01",
        "calibration_status": "valid",
        "maintenance_status": "available",
    }

    rows = audit_rows(package, task, runtime)

    assert rows
    assert all(row["source"] for row in rows)
    assert all(row["value"] for row in rows)
    assert all(row["status"] == "PASS" for row in rows)


def test_quality_station_uses_source_execution_context_not_execution_id_shape():
    detail = {
        "events": [
            {
                "event": {
                    "executionId": "019fc719-a02f-7e19-8971-5c24033d7f69",
                    "context": {"source_execution_no": "12"},
                }
            }
        ]
    }

    assert read_source_execution_number(detail) == 12


def test_quality_station_cli_uses_the_declared_machine_and_run_options(monkeypatch):
    monkeypatch.setattr(
        "sys.argv",
        [
            "submit_quality.py",
            "--machine-id",
            "PRESS-02",
            "--operation-run-id",
            "run-42",
        ],
    )

    args = parse_quality_args()

    assert args.machine_id == "PRESS-02"
    assert args.operation_run_id == "run-42"


def test_device_simulator_cli_uses_process_specification_version_term(monkeypatch):
    monkeypatch.setattr(
        "sys.argv",
        [
            "device_simulator.py",
            "--process-specification-version-offset",
            "3",
        ],
    )

    args = parse_device_args()

    assert args.process_specification_version_offset == 3


def test_quality_station_authenticates_subsequent_platform_requests(monkeypatch):
    monkeypatch.setattr(submit_quality, "_AUTH_TOKEN", None)
    monkeypatch.setattr(
        submit_quality,
        "request",
        lambda url, payload=None: {"token": "session-token"},
    )

    authenticate("http://platform", "operator", "secret")

    assert submit_quality._AUTH_TOKEN == "session-token"


def test_ingestion_provisioner_authenticates_subsequent_platform_requests(monkeypatch):
    monkeypatch.setattr(provision_ingestion_task, "_AUTH_TOKEN", None)
    monkeypatch.setattr(
        provision_ingestion_task,
        "request",
        lambda url, payload: {"token": "session-token"},
    )

    provision_ingestion_task.authenticate("http://platform", "operator", "secret")

    assert provision_ingestion_task._AUTH_TOKEN == "session-token"


def test_research_asset_demo_is_explicitly_simulated_and_contract_aligned():
    now = datetime.now(timezone.utc)
    content = quality_csv()
    dataset = training_dataset(now, hashlib.sha256(content).hexdigest())
    model = process_model()
    mechanism = mechanism_model()
    fusion = fusion_definition()
    manifest = quality_manifest(content)

    assert dataset["analysisPlanId"] == "optical-lens-molding-demo-analysis"
    assert model["datasetId"] == dataset["datasetId"]
    assert model["inputFeatureCodes"] == dataset["featureCodes"]
    assert model["outputCode"] == dataset["targetCode"]
    assert fusion["mechanismModelId"] == mechanism["modelId"]
    assert "模拟" in mechanism["scientificBasis"]
    assert manifest["isMeasuredData"] is False
    assert manifest["dataKind"] == "simulated-validation"
    assert manifest["sourceUri"].startswith("https://github.com/liuweichaox/Ingot/")


def test_research_asset_provisioner_contains_required_mechanism_status_transition():
    source = (__import__("pathlib").Path(__file__).parent / "provision_research_assets.py").read_text(
        encoding="utf-8"
    )

    assert 'mechanism.get("status") == "draft"' in source
    assert '{"targetStatus": "validated"}' in source
    assert source.index('"targetStatus": "validated"') < source.index(
        'client.request_json("POST", "/api/v1/mechanism-fusions"'
    )


def test_fx3u_run_active_register_has_a_real_boundary_between_molding_executions():
    simulator = Simulator(
        execution_seconds=8,
        run_prefix="execution-boundary",
        max_runs=2,
    )

    first = simulator.snapshot(elapsed_seconds=0.1)
    boundary = simulator.snapshot(elapsed_seconds=7.0)
    second = simulator.snapshot(elapsed_seconds=8.1)

    assert first["runActive"] is True
    assert first["runNumber"] == 1
    assert boundary["runActive"] is False
    assert boundary["runNumber"] == 1
    assert second["runActive"] is True
    assert second["runNumber"] == 2


def test_fx3u_server_answers_mc_1e_binary_word_reads():
    simulator = Simulator(
        execution_seconds=60,
        run_prefix="fx3u",
        max_runs=1,
    )
    registers = Fx3uRegisterBank(simulator)
    registers.start()
    server = Fx3uServer(("127.0.0.1", 0), handler(registers))
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        with socket.create_connection(server.server_address, timeout=2) as client:
            request = (
                b"\x01\xff"
                + (16).to_bytes(2, "little")
                + (101).to_bytes(4, "little")
                + b" D"
                + b"\x01\x00"
            )
            client.sendall(request)
            response = client.recv(4)
        assert response[:2] == b"\x81\x00"
        assert 4300 <= int.from_bytes(response[2:4], "little", signed=True) <= 4500
    finally:
        server.shutdown()
        server.server_close()
        registers.stop()
        thread.join(timeout=2)


def test_fx3u_server_fault_modes_expose_protocol_error_and_disconnect():
    simulator = Simulator(execution_seconds=60, run_prefix="fx3u-fault", max_runs=1)
    registers = Fx3uRegisterBank(simulator)
    registers.start()
    server = Fx3uServer(
        ("127.0.0.1", 0),
        handler(registers, protocol_error_every=2, disconnect_every=3),
    )
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    request = (
        b"\x01\xff"
        + (16).to_bytes(2, "little")
        + (101).to_bytes(4, "little")
        + b" D"
        + b"\x01\x00"
    )
    try:
        responses = []
        for _ in range(3):
            with socket.create_connection(server.server_address, timeout=2) as client:
                client.sendall(request)
                responses.append(client.recv(4))
        assert responses[0][:2] == b"\x81\x00"
        assert responses[1] == b"\x81\x10"
        assert responses[2] == b""
    finally:
        server.shutdown()
        server.server_close()
        registers.stop()
        thread.join(timeout=2)


def test_fx3u_server_rejects_invalid_fault_configuration():
    simulator = Simulator(execution_seconds=60, run_prefix="fx3u-fault", max_runs=1)
    registers = Fx3uRegisterBank(simulator)
    try:
        handler(registers, response_delay_ms=-1)
        raise AssertionError("negative response delay must be rejected")
    except ValueError as error:
        assert "response_delay_ms" in str(error)
