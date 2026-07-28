from fastapi.testclient import TestClient

from service import app


client = TestClient(app)


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


def test_service_runs_batch_multiobjective_qlognehvi_for_optical_molding():
    body = {
        "campaign": {
            "name": "lens-molding",
            "process_profile": "fx3u-optical-molding",
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
    assert payload["model_version"] == "botorch-qlogbo-v2"
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
