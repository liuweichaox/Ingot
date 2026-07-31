from argparse import Namespace
import socket
import threading

from bootstrap_demo import data_model, recipe
from demo_contract import DATA_ITEMS, RECIPE_PARAMETERS, device_recipe_values
from device_simulator import Fx3uRegisterBank, Fx3uServer, Simulator, handler, values
from provision_data_source import build_payload


def test_sensor_and_recipe_contract_matches_reference_parameter_lists():
    assert [item["sourceField"] for item in DATA_ITEMS] == [
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
    assert [item["sourceField"] for item in RECIPE_PARAMETERS] == [
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
    assert len(data_model()["recipeParameters"]) == 12
    assert len(recipe(1)["values"]) == 12


def test_device_snapshot_and_acquisition_profile_cover_every_declared_field():
    snapshot_values = values("baseline", 0.5)
    profile = build_payload(
        Namespace(
            edge_id="EDGE-FX3U-SIM-001",
            device_host="127.0.0.1",
            device_port=5551,
            profile_version=1,
            data_model_version=1,
        )
    )

    assert profile["protocol"] == "melsec-a1e"
    assert profile["melsecA1E"]["port"] == 5551
    assert profile["melsecA1E"]["dataCode"] == "binary"
    assert profile["melsecA1E"]["pcNumber"] == 255
    assert len(profile["valueMappings"]) == len(DATA_ITEMS)
    assert len(profile["recipe"]["parameterMappings"]) == len(RECIPE_PARAMETERS)
    assert profile["valueMappings"][0]["sourcePath"] == "D:1:uint16"
    assert not any(
        item["contextKey"] == "stage_number"
        for item in profile["contextMappings"]
    )
    assert "correlationIdContextKey" not in profile["lifecycle"]
    assert not any(
        item["contextKey"] == "correlation_id"
        for item in profile["contextMappings"]
    )
    assert any(
        item["contextKey"] == "source_cycle_no" and item["sourcePath"] == "D:2:uint32"
        for item in profile["contextMappings"]
    )
    assert set(profile["staticContext"]) == {"demo_replay", "data_classification"}
    assert {
        "product_code": "D:30:string:20",
        "product_series": "D:40:string:20",
        "mold_id": "D:50:string:20",
        "material_lot_ref": "D:60:string:20",
    }.items() <= {
        item["contextKey"]: item["sourcePath"]
        for item in profile["contextMappings"]
    }.items()
    assert set(device_recipe_values(1)) == {
        item["sourcePath"] for item in RECIPE_PARAMETERS
    }
    assert snapshot_values["upperMold"]["power"] > 0
    assert snapshot_values["lowerMold"]["power"] > 0
    assert snapshot_values["pressure"]["load"] > 0


def test_device_can_offset_recipe_versions_for_a_new_data_model_generation():
    simulator = Simulator(
        cycle_seconds=60,
        run_prefix="stage-number",
        max_runs=1,
        recipe_version_offset=2,
    )

    assert simulator.snapshot()["activeRecipe"]["version"] == 3


def test_fx3u_run_active_register_has_a_real_boundary_between_molding_cycles():
    simulator = Simulator(
        cycle_seconds=8,
        run_prefix="cycle-boundary",
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
        cycle_seconds=60,
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
