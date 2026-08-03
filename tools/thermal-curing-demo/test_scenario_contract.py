import importlib.util
from pathlib import Path


_module_path = Path(__file__).with_name("bootstrap_demo.py")
_spec = importlib.util.spec_from_file_location("ingot_thermal_curing_bootstrap", _module_path)
assert _spec is not None and _spec.loader is not None
_bootstrap = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_bootstrap)

MODEL_ID = _bootstrap.MODEL_ID
analysis_plan = _bootstrap.analysis_plan
data_model = _bootstrap.data_model
recipe = _bootstrap.recipe
scenario_package = _bootstrap.scenario_package


def test_second_scenario_is_continuous_and_not_molding_specific():
    model = data_model(2)
    plan = analysis_plan(2, 2)
    package = scenario_package(2)

    assert MODEL_ID == "continuous-thermal-curing-demo"
    assert model["version"] == plan["version"] == package["version"] == 2
    assert model["modelId"] == plan["dataModelId"] == package["dataModelId"]
    assert plan["analysisScope"] == "production-run"
    assert plan["alignmentMode"] == "elapsed"
    assert not any(item["category"] == "stage" for item in model["acquisition"]["dataItems"])
    assert package["acquisitionProfiles"] == []
    assert package["qualityPlan"] is None
    assert all("mold" not in str(value).lower() for value in (model, plan, package))


def test_second_scenario_keeps_recipe_and_evidence_context_versioned():
    value = recipe(3, 2)
    package = scenario_package(2)

    assert value["dataModelVersion"] == 2
    assert {item["code"] for item in value["values"]} == {
        "oven.zone1.setpoint",
        "oven.zone2.setpoint",
        "conveyor.speed.setpoint",
    }
    required = {item["fieldCode"] for item in package["contextFields"] if item["mode"] == "required-for-analysis"}
    assert required == {"line_id", "adhesive_lot", "product_series"}
    assert all(item["minimumCoverage"] is not None for item in package["contextFields"] if item["mode"] != "record-when-available")
    assert all(item["minimum"] is not None or item["maximum"] is not None for item in package["constraints"])
