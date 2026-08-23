"""Verify the committed public-data benchmark and its claim boundary."""

import importlib.util
import json
from pathlib import Path

import pytest


BENCHMARK_PATH = (
    Path(__file__).parents[2] / "tools" / "public-validation" / "benchmark_v2.py"
)
SPEC = importlib.util.spec_from_file_location("ingot_public_validation", BENCHMARK_PATH)
benchmark = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(benchmark)

BENCHMARK_V3_PATH = BENCHMARK_PATH.with_name("benchmark_v3.py")
SPEC_V3 = importlib.util.spec_from_file_location(
    "ingot_public_validation_v3", BENCHMARK_V3_PATH
)
benchmark_v3 = importlib.util.module_from_spec(SPEC_V3)
assert SPEC_V3.loader is not None
SPEC_V3.loader.exec_module(benchmark_v3)


def test_public_fixtures_are_verified_and_contexts_are_isolated():
    protocol = benchmark.load_protocol()
    fdm = benchmark.group_scenarios(
        "fdm", benchmark.load_rows("fdm", protocol), protocol
    )
    crossed_barrel = benchmark.group_scenarios(
        "crossed_barrel", benchmark.load_rows("crossed_barrel", protocol), protocol
    )

    assert len(fdm) == 6
    assert all(len(values) == 27 for _, values in fdm)
    assert len(crossed_barrel) == 4
    assert all(len(values) == 150 for _, values in crossed_barrel)
    assert sum(
        int(row["replicate_count"])
        for row in benchmark.load_rows("crossed_barrel", protocol)
    ) == 1800
    for dataset in benchmark.DATASETS.values():
        assert set(dataset["context_fields"]).isdisjoint(dataset["control_fields"])


def test_public_replay_uses_shared_initial_history_without_outcome_leakage():
    protocol = benchmark.load_protocol()
    rows = benchmark.load_rows("fdm", protocol)
    context, values = benchmark.group_scenarios("fdm", rows, protocol)[0]
    record = benchmark.run_scenario(
        "fdm",
        context,
        values,
        scenario_index=0,
        protocol=protocol,
        episode_count=2,
    )

    assert record["status"] == "completed"
    assert record["eligible_unique_initial_designs"] == 2
    assert all(len(episode["initial_run_ids"]) == 4 for episode in record["episodes"])
    assert all(
        episode["optimizer_trials"] is None or episode["optimizer_trials"] >= 1
        for episode in record["episodes"]
    )


def test_committed_full_result_keeps_public_claim_boundary_explicit():
    result_path = BENCHMARK_PATH.parent / "latest-results.json"
    payload = json.loads(result_path.read_text(encoding="utf-8"))

    assert payload["schema"] == "ingot-public-validation-v2"
    assert payload["method"]["episodes"]["count_per_scenario"] == 100
    assert payload["summary"]["workflow_validation"] == "passed"
    assert payload["summary"]["dataset_count"] == 2
    assert payload["summary"]["scenario_count"] == 10
    assert payload["summary"]["episode_count"] == 1000
    assert payload["summary"]["experiment_reduction_vs_uninformed_search"] in {
        "passed-public-benchmark",
        "not-demonstrated",
    }
    assert payload["summary"]["active_comparator_noninferiority"] in {
        "passed-public-benchmark",
        "not-demonstrated",
    }
    crossed_barrel_effect = next(
        effect
        for effect in payload["summary"]["dataset_effects"]["crossed_barrel"]
        if effect["baseline"] == "regularized-linear-response-surface"
    )
    assert crossed_barrel_effect["passed"] is False
    assert payload["summary"]["dataset_guardrails"]["crossed_barrel"][
        "regularized-linear-response-surface"
    ] is False
    assert payload["summary"]["experiment_reduction_claim"] == "not-demonstrated"
    assert "does not prove savings in another factory" in payload["method"][
        "claim_boundary"
    ]


def test_v3_external_evaluation_fixtures_and_mechanism_features_are_valid():
    protocol = benchmark_v3.load_protocol()
    report = benchmark_v3.integrity_report(protocol)

    assert report["status"] == "frozen"
    assert report["full_evaluation_allowed"] is True
    assert report["runtime"]["python"]
    assert report["runtime"]["packages"]["numpy"]
    airfoil = report["datasets"]["airfoil"]
    yacht = report["datasets"]["yacht"]
    assert airfoil["rows"] == 1503
    assert airfoil["fixture_sha256"] == (
        "d055aab9202ea1932a5ed933d549ae31f3b58926de19d67ac326360f81722f85"
    )
    assert airfoil["control_count"] == 5
    assert airfoil["mechanism_feature_count"] == 4
    assert airfoil["target_setting_rate"] == pytest.approx(0.15037, abs=1e-5)
    assert yacht["rows"] == 308
    assert yacht["fixture_sha256"] == (
        "8ce2f8eabe81b72484a5956c233cec031b840df938956d8f38740663009ce6b1"
    )
    assert yacht["control_count"] == 6
    assert yacht["mechanism_feature_count"] == 3
    assert yacht["target_setting_rate"] == pytest.approx(0.15260, abs=1e-5)
    for profile in (airfoil, yacht):
        assert profile["null_or_non_finite_values"] == 0
        assert profile["duplicate_identifiers"] == 0
        assert profile["duplicate_control_settings"] == 0
        assert profile["outcome_minimum"] < profile["outcome_median"] < profile[
            "outcome_maximum"
        ]


def test_v3_protocol_is_frozen_before_the_retained_result():
    protocol = benchmark_v3.load_protocol()

    benchmark_v3.require_frozen(protocol)

    assert protocol["methods"]["baselines"] == [
        "seeded-random-search",
        "sequential-maximin-space-filling",
        "regularized-linear-response-surface",
        "regularized-quadratic-response-surface",
    ]
    assert protocol["methods"]["ablation"] == (
        "ingot-without-mechanism-features"
    )
    assert len(protocol["claim_boundary"]["not_permitted"]) == 3

    result = json.loads(
        (BENCHMARK_PATH.parent / "latest-results-v3.json").read_text(encoding="utf-8")
    )
    assert result["evaluation_fingerprint"] == protocol["freeze"]["evaluation_fingerprint"]
    assert result["summary"]["episode_count"] == 400
    assert result["summary"]["experiment_reduction_vs_all_preregistered_baselines"] == (
        "not-demonstrated"
    )
    assert result["summary"]["mechanism_feature_contribution"] == (
        "passed-protocol-frozen-ablation"
    )
    effects = {
        item["comparator"]: item
        for item in result["summary"]["paired_effects"]
    }
    assert effects["regularized-linear-response-surface"]["passed"] is False
    assert effects["regularized-quadratic-response-surface"]["passed"] is False


def test_v3_fingerprint_freezes_algorithm_data_dependencies_and_protocol():
    protocol = benchmark_v3.load_protocol()
    candidate = benchmark_v3.evaluation_fingerprint(protocol)
    frozen = json.loads(json.dumps(protocol))
    frozen["status"] = "frozen"
    frozen["freeze"]["optimizer_revision"] = "a" * 40
    frozen["freeze"]["protocol_revision"] = "b" * 40
    frozen["freeze"]["evaluation_fingerprint"] = candidate

    benchmark_v3.require_frozen(frozen)

    frozen["datasets"]["airfoil"]["additional_trial_budget"] += 1
    with pytest.raises(RuntimeError, match="fingerprint does not match"):
        benchmark_v3.require_frozen(frozen)
