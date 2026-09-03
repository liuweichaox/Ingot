"""Verify service success, rejection, and boundary behavior."""

from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from service import SuggestionResponse, app


client = TestClient(app)


def test_shared_suggestion_response_fixture_matches_python_contract():
    fixture = (
        Path(__file__).parents[2]
        / "tests"
        / "contract-fixtures"
        / "optimizer-suggestion-response.json"
    )
    response = SuggestionResponse.model_validate_json(fixture.read_text())

    assert response.feature_set_id == "molding-v2"
    assert response.feature_set_version == 2
    assert response.derived_feature_count == 4
    assert response.coverage_envelope is not None
    assert response.coverage_envelope.variables[0].name == "temperature"
    assert response.coverage_envelope.variables[0].lower == 496.0
    assert response.coverage_envelope.variables[0].upper == 544.0


def test_suggestion_endpoint_publishes_typed_response_schema():
    schema = app.openapi()["paths"]["/v1/suggestions"]["post"]["responses"]["200"]
    assert schema["content"]["application/json"]["schema"]["$ref"].endswith(
        "/SuggestionResponse"
    )


def request_body():
    return {
        "campaign": {
            "name": "service-audit",
            "variables": [{"name": "x", "low": 0.0, "high": 1.0}],
            "objectives": [
                {"name": "loss", "kind": "le", "threshold": 0.1}
            ],
            "constraints": [
                {
                    "variable": "x",
                    "operator": "<=",
                    "limit": 0.9,
                    "safety_critical": True,
                }
            ],
        },
        "observations": [
            {"params": {"x": 0.0}, "outcomes": {"loss": 0.64}},
            {"params": {"x": 0.4}, "outcomes": {"loss": 0.16}},
            {"params": {"x": 0.7}, "outcomes": {"loss": 0.01}},
        ],
        "seed": 7,
        "n_random": 80,
        "n_samples": 64,
    }


def test_service_is_stateless_and_deterministic_for_same_snapshot():
    first = client.post("/v1/suggestions", json=request_body())
    second = client.post("/v1/suggestions", json=request_body())

    assert first.status_code == 200
    assert second.status_code == 200
    assert first.json() == second.json()
    assert first.json()["state_persisted"] is False
    assert first.json()["observation_count"] == 3
    assert first.json()["suggestions"][0]["recommended_params"]["x"] <= 0.9


def test_service_rejects_empty_campaign_and_unknown_observation_keys():
    empty = request_body()
    empty["campaign"]["variables"] = []
    assert client.post("/v1/suggestions", json=empty).status_code == 422

    mismatch = request_body()
    mismatch["observations"][0]["params"]["typo"] = 1.0
    response = client.post("/v1/suggestions", json=mismatch)
    assert response.status_code == 422
    assert "keys mismatch" in response.json()["detail"]


def test_service_has_health_endpoint_and_no_process_memory_campaign_api():
    response = client.get("/health")
    assert response.status_code == 200
    assert response.json()["status"] == "ok"
    readiness = client.get("/ready")
    assert readiness.status_code == 200
    assert readiness.json()["status"] == "ready"
    assert client.post("/campaigns", json={}).status_code == 404


def test_design_service_generates_reproducible_classic_doe_without_state():
    body = {
        "method": "full-factorial",
        "variables": [
            {"name": "temperature", "low": 300.0, "high": 340.0, "unit": "C"},
            {"name": "pressure", "low": 1.0, "high": 3.0, "unit": "MPa"},
        ],
        "levels": 2,
        "replicates": 2,
        "block_count": 2,
        "seed": 42,
    }
    first = client.post("/v1/designs", json=body)
    second = client.post("/v1/designs", json=body)

    assert first.status_code == 200
    assert first.json() == second.json()
    payload = first.json()
    assert payload["state_persisted"] is False
    assert len(payload["runs"]) == 8
    assert {run["block_key"] for run in payload["runs"]} == {"block-01", "block-02"}


def test_design_service_balances_blocks_independently_of_replicates():
    response = client.post("/v1/designs", json={
        "method": "full-factorial",
        "variables": [
            {"name": "temperature", "low": 300.0, "high": 340.0},
            {"name": "pressure", "low": 1.0, "high": 3.0},
        ],
        "levels": 2,
        "replicates": 1,
        "block_count": 3,
        "seed": 42,
    })

    assert response.status_code == 200
    runs = response.json()["runs"]
    assert [run["block_key"] for run in runs] == [
        "block-01", "block-02", "block-03", "block-01",
    ]
    assert sorted(
        sum(run["block_key"] == block for run in runs)
        for block in {run["block_key"] for run in runs}
    ) == [1, 1, 2]
    assert {run["replicate_key"] for run in runs} == {"replicate-01"}


def test_design_service_rejects_more_blocks_than_generated_runs():
    response = client.post("/v1/designs", json={
        "method": "full-factorial",
        "variables": [{"name": "temperature", "low": 300.0, "high": 340.0}],
        "levels": 2,
        "replicates": 1,
        "block_count": 3,
    })

    assert response.status_code == 422
    assert "block_count cannot exceed" in response.json()["detail"]


def test_design_service_rejects_oversized_factorial_before_materializing_points():
    response = client.post("/v1/designs", json={
        "method": "full-factorial",
        "variables": [
            {"name": f"factor-{index}", "low": 0.0, "high": 1.0}
            for index in range(12)
        ],
        "levels": 5,
        "replicates": 5,
    })

    assert response.status_code == 422
    assert "40-run experiment limit" in response.json()["detail"]


def test_design_service_supports_fractional_response_surface_and_latin_hypercube():
    variables = [
        {"name": "a", "low": 0.0, "high": 10.0},
        {"name": "b", "low": 0.0, "high": 10.0},
        {"name": "c", "low": 0.0, "high": 10.0},
    ]
    fractional = client.post("/v1/designs", json={
        "method": "fractional-factorial", "variables": variables, "seed": 3,
    })
    ccd = client.post("/v1/designs", json={
        "method": "response-surface", "variables": variables[:2],
        "response_surface_family": "central-composite", "seed": 3,
    })
    lhs = client.post("/v1/designs", json={
        "method": "latin-hypercube", "variables": variables,
        "sample_count": 6, "seed": 3,
    })

    assert fractional.status_code == 200
    assert fractional.json()["alias_structure"]
    assert ccd.status_code == 200
    assert ccd.json()["response_surface_family"] == "central-composite"
    assert lhs.status_code == 200
    assert len(lhs.json()["runs"]) == 6


def test_service_runs_batch_multiobjective_spec_ensemble_with_declared_features():
    body = {
        "campaign": {
            "name": "lens-molding",
            "feature_set_id": "optical-lens-molding-demo",
            "feature_set_version": 1,
            "derived_features": [
                {
                    "name": "normalized_temperature",
                    "operator": "identity",
                    "inputs": ["soak_temp"],
                    "normalization_offset": 320.0,
                    "normalization_scale": 40.0,
                },
                {
                    "name": "compression_exposure",
                    "operator": "ratio",
                    "inputs": ["press_force", "press_speed"],
                    "normalization_scale": 20.0,
                    "epsilon": 0.05,
                },
            ],
            "variables": [
                {"name": "soak_temp", "low": 320.0, "high": 360.0, "unit": "C"},
                {"name": "press_force", "low": 1.0, "high": 5.0, "unit": "kN"},
                {"name": "press_speed", "low": 0.1, "high": 1.0, "unit": "mm/s"},
                {"name": "anneal_rate", "low": 0.1, "high": 1.0, "unit": "C/s"},
            ],
            "objectives": [
                {
                    "name": "form_error", "kind": "le", "threshold": 0.5,
                    "unit": "um", "weight": 2.0,
                },
                {"name": "defect_rate", "kind": "le", "threshold": 0.02, "unit": "ratio"},
            ],
            "constraints": [
                {"variable": "soak_temp", "operator": "<=", "limit": 355.0}
            ],
            "outcome_constraints": [
                {
                    "name": "crack_rate",
                    "operator": "<=",
                    "limit": 0.05,
                    "unit": "ratio",
                    "minimum_probability": 0.05,
                }
            ],
        },
        "observations": [
            {
                "params": {
                    "soak_temp": 325.0, "press_force": 1.5,
                    "press_speed": 0.8, "anneal_rate": 0.8,
                },
                "outcomes": {"form_error": 1.2, "defect_rate": 0.08},
                "constraint_outcomes": {"crack_rate": 0.12},
                "process_features": {"mold_temp.overshoot": 8.0},
            },
            {
                "params": {
                    "soak_temp": 340.0, "press_force": 3.0,
                    "press_speed": 0.5, "anneal_rate": 0.5,
                },
                "outcomes": {"form_error": 0.65, "defect_rate": 0.035},
                "constraint_outcomes": {"crack_rate": 0.06},
                "process_features": {"mold_temp.overshoot": 3.0},
            },
            {
                "params": {
                    "soak_temp": 350.0, "press_force": 4.0,
                    "press_speed": 0.3, "anneal_rate": 0.3,
                },
                "outcomes": {"form_error": 0.45, "defect_rate": 0.03},
                "constraint_outcomes": {"crack_rate": 0.03},
                "process_features": {"mold_temp.overshoot": 1.5},
            },
        ],
        "top_k": 2,
        "n_random": 64,
        "n_samples": 32,
        "seed": 11,
    }
    response = client.post("/v1/suggestions", json=body)
    assert response.status_code == 200, response.text
    payload = response.json()
    assert payload["model_version"] == (
        "conservative-target-ranking-selector-2026-08-23"
    )
    assert payload["feature_set_id"] == "optical-lens-molding-demo"
    assert payload["derived_feature_count"] == 2
    assert len(payload["suggestions"]) == 2
    assert all(
        item["recommended_params"]["soak_temp"] <= 355.0
        for item in payload["suggestions"]
    )
    assert all(
        set(item["objective_predictions"]) == {"form_error", "defect_rate"}
        for item in payload["suggestions"]
    )
    assert all(
        set(item["constraint_predictions"]) == {"crack_rate"}
        for item in payload["suggestions"]
    )


def test_service_reports_and_enforces_the_observed_coverage_envelope():
    response = client.post("/v1/suggestions", json=request_body())

    assert response.status_code == 200, response.text
    payload = response.json()
    covered = {
        value["name"]: value
        for value in payload["coverage_envelope"]["variables"]
    }

    assert payload["coverage_envelope"]["observation_count"] == 3
    assert covered["x"]["observed_minimum"] == pytest.approx(0.0)
    assert covered["x"]["observed_maximum"] == pytest.approx(0.7)
    # The observed spread is 0.7, so the range gate widens it by 0.07.
    assert covered["x"]["upper"] == pytest.approx(0.77)
    assert all(
        covered["x"]["lower"]
        <= item["recommended_params"]["x"]
        <= covered["x"]["upper"]
        for item in payload["suggestions"]
    )


def test_service_stops_when_runs_never_covered_enough_of_the_space():
    body = request_body()
    body["candidate_pool"] = [{"x": 0.89}]

    response = client.post("/v1/suggestions", json=body)

    assert response.status_code == 422
    assert "observed coverage envelope" in response.text


def test_service_keeps_binary_objective_predictions_inside_declared_bounds():
    body = request_body()
    body["campaign"]["objectives"] = [
        {
            "name": "pass",
            "kind": "ge",
            "threshold": 1.0,
            "outcome_lower_bound": 0.0,
            "outcome_upper_bound": 1.0,
        }
    ]
    body["observations"] = [
        {"params": {"x": 0.0}, "outcomes": {"pass": 0.0}},
        {"params": {"x": 0.4}, "outcomes": {"pass": 1.0}},
        {"params": {"x": 0.7}, "outcomes": {"pass": 1.0}},
    ]
    response = client.post("/v1/suggestions", json=body)
    assert response.status_code == 200, response.text
    prediction = response.json()["suggestions"][0]["objective_predictions"]["pass"]
    assert 0.0 <= prediction["mean"] <= 1.0
    assert 0.0 <= prediction["lower_95"] <= prediction["upper_95"] <= 1.0


def test_service_rejects_hidden_process_profiles_and_invalid_feature_graphs():
    unsupported = request_body()
    unsupported["campaign"]["process_profile"] = "fx3u-optical-molding"
    response = client.post("/v1/suggestions", json=unsupported)
    assert response.status_code == 422
    assert "process_profile" in response.text

    invalid = request_body()
    invalid["campaign"]["derived_features"] = [
        {
            "name": "unknown-input-feature",
            "operator": "identity",
            "inputs": ["not-a-variable"],
        }
    ]
    response = client.post("/v1/suggestions", json=invalid)
    assert response.status_code == 422
    assert "unknown or forward" in response.json()["detail"]


def test_diagnosis_adjusts_context_and_reports_stability_and_interactions():
    observations = []
    for index in range(60):
        temperature = 500.0 + (index % 10) * 2.0
        pressure = 8.0 + ((index * 3) % 11) * 0.4
        machine = "PRESS-A" if index < 30 else "PRESS-B"
        failed = int(
            temperature > 512.0
            and pressure > 10.0
            or (index % 17 == 0)
        )
        observations.append(
            {
                "run_key": f"run-{index:03d}",
                "outcome": failed,
                "weight": 1.0,
                "values": {
                    "control-parameter:temperature": temperature,
                    "control-parameter:pressure": pressure,
                    "signal:mold-temperature:overshoot": max(0.0, temperature - 510.0),
                },
                "context": {
                    "product_code": "LENS-A",
                    "equipment_id": machine,
                    "material_lot": f"LOT-{index // 10}",
                },
                "occurred_at": float(index),
            }
        )
    response = client.post(
        "/v1/diagnosis",
        json={
            "outcome_kind": "binary",
            "features": [
                {
                    "data_source": "control-parameter:temperature",
                    "source_kind": "control-parameter",
                    "actionability": "controllable",
                },
                {
                    "data_source": "control-parameter:pressure",
                    "source_kind": "control-parameter",
                    "actionability": "controllable",
                },
                {
                    "data_source": "signal:mold-temperature:overshoot",
                    "source_kind": "process-feature",
                    "actionability": "observable",
                },
            ],
            "observations": observations,
            "seed": 19,
        },
    )
    assert response.status_code == 200, response.text
    payload = response.json()
    assert payload["algorithm_version"] == "adaptive-context-diagnosis-v1"
    assert payload["model_family"].startswith("regularized-additive-logistic")
    assert payload["fold_count"] >= 2
    assert payload["stability_runs"] == 24
    assert "context:equipment_id=PRESS-B" in payload["context_variables"]
    assert payload["candidates"][0]["stability_selection_rate"] >= 0
    assert all("verified" not in item for item in payload["limitations"])


def test_continuous_diagnosis_selects_gp_when_nonlinearity_is_supported():
    observations = []
    for index in range(48):
        value = -3.0 + index * 6.0 / 47.0
        observations.append(
            {
                "run_key": f"continuous-{index:03d}",
                "outcome": float(__import__("math").sin(value * 2.2)),
                "values": {"control-parameter:x": value},
                "context": {"equipment_id": "PRESS-A" if index % 2 else "PRESS-B"},
                "occurred_at": float(index),
            }
        )
    response = client.post(
        "/v1/diagnosis",
        json={
            "outcome_kind": "continuous",
            "features": [
                {
                    "data_source": "control-parameter:x",
                    "source_kind": "control-parameter",
                    "actionability": "controllable",
                }
            ],
            "observations": observations,
            "seed": 5,
        },
    )
    assert response.status_code == 200, response.text
    payload = response.json()
    assert payload["model_family"].startswith("gaussian-process-regression")
    assert payload["cross_validation_score"] > 0


def test_historical_replay_endpoint_is_production_equivalent_and_auditable():
    body = {
        "campaign": {
            "name": "historical-project",
            "variables": [{"name": "x", "low": 0.0, "high": 1.0}],
            "objectives": [{"name": "loss", "kind": "le", "threshold": 0.1}],
        },
        "history": [
            {
                "params": {"x": value},
                "outcomes": {"loss": (value - 0.8) ** 2},
                "run_id": f"run-{index}",
                "occurred_at": float(index),
            }
            for index, value in enumerate([0.0, 0.2, 0.4, 0.6, 0.8, 1.0])
        ],
        "budget": 6,
        "n_seeds": 3,
        "initial_observation_count": 3,
    }

    response = client.post("/v1/historical-replay", json=body)

    assert response.status_code == 200, response.text
    payload = response.json()
    assert payload["engine_policy"].startswith("production-equivalent")
    assert payload["evidence_kind"] == "historical-pool-ranking"
    assert payload["state_persisted"] is False
    assert len(payload["step_traces"]) == 3
    assert all(
        step["revealed_history_index"]
        not in step.get("visible_observation_indices_before", [])
        for trace in payload["step_traces"]
        for step in trace
    )
    assert "does not prove online" in payload["limitations"]


def test_historical_replay_rejects_duplicate_parameter_settings_and_unknown_fields():
    body = {
        "campaign": {
            "name": "duplicate-history",
            "variables": [{"name": "x", "low": 0.0, "high": 1.0}],
            "objectives": [{"name": "loss", "kind": "le", "threshold": 0.1}],
        },
        "history": [
            {"params": {"x": 0.1}, "outcomes": {"loss": 0.5}},
            {"params": {"x": 0.1}, "outcomes": {"loss": 0.4}},
            {"params": {"x": 0.8}, "outcomes": {"loss": 0.0}},
        ],
    }
    assert client.post("/v1/historical-replay", json=body).status_code == 422
    body["history"][1]["params"] = {"x": 0.2}
    body["history"][0]["future_outcome"] = 0.0
    assert client.post("/v1/historical-replay", json=body).status_code == 422
