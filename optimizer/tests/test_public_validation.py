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

BENCHMARK_V4_PATH = BENCHMARK_PATH.with_name("benchmark_v4.py")
SPEC_V4 = importlib.util.spec_from_file_location(
    "ingot_public_validation_v4", BENCHMARK_V4_PATH
)
benchmark_v4 = importlib.util.module_from_spec(SPEC_V4)
assert SPEC_V4.loader is not None
SPEC_V4.loader.exec_module(benchmark_v4)

BENCHMARK_V6_PATH = BENCHMARK_PATH.with_name("benchmark_v6.py")
SPEC_V6 = importlib.util.spec_from_file_location(
    "ingot_public_validation_v6", BENCHMARK_V6_PATH
)
benchmark_v6 = importlib.util.module_from_spec(SPEC_V6)
assert SPEC_V6.loader is not None
SPEC_V6.loader.exec_module(benchmark_v6)

BENCHMARK_V7_PATH = BENCHMARK_PATH.with_name("benchmark_v7.py")
SPEC_V7 = importlib.util.spec_from_file_location(
    "ingot_public_validation_v7", BENCHMARK_V7_PATH
)
benchmark_v7 = importlib.util.module_from_spec(SPEC_V7)
assert SPEC_V7.loader is not None
SPEC_V7.loader.exec_module(benchmark_v7)


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
    assert report["full_evaluation_allowed"] is False
    assert report["candidate_evaluation_fingerprint"] != protocol["freeze"][
        "evaluation_fingerprint"
    ]
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

    assert protocol["status"] == "frozen"
    assert len(protocol["freeze"]["optimizer_revision"]) == 40
    assert len(protocol["freeze"]["protocol_revision"]) == 40
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


def test_v4_holdout_fixtures_are_verified_after_protocol_freeze():
    protocol = benchmark_v4.load_protocol()
    report = benchmark_v4.integrity_report(protocol)

    assert report["status"] == "frozen"
    # The retained v4 result remains immutable, while a newer optimizer must not
    # be replayed under the old primary-method name and frozen fingerprint.
    assert report["full_evaluation_allowed"] is False
    assert report["evaluation_unit_count"] == 25
    assert len(report["candidate_evaluation_fingerprint"]) == 64

    energy = report["datasets"]["energy_efficiency"]
    assert energy["rows"] == 768
    assert energy["fixture_sha256"] == (
        "c8ddcfd2751dae6aa0185343eba4073bf3c15ffa55953ed006512a95574af54a"
    )
    assert energy["evaluation_unit_count"] == 24
    assert energy["minimum_unit_rows"] == 12
    assert energy["maximum_unit_rows"] == 36

    machine = report["datasets"]["synchronous_machine"]
    assert machine["rows"] == 557
    assert machine["fixture_sha256"] == (
        "75ae917e07f58c9213fcbbabc470dee3d4411a7291f53083abe55f33dbd43fec"
    )
    assert machine["evaluation_unit_count"] == 1
    assert machine["minimum_unit_rows"] == 557
    assert machine["maximum_unit_rows"] == 557

    for profile in (energy, machine):
        assert profile["mechanism_feature_count"] == 3
        assert profile["null_or_non_finite_values"] == 0
        assert profile["duplicate_identifiers"] == 0
        assert profile["duplicate_control_context_settings"] == 0
        assert profile["outcome_minimum"] < profile["outcome_median"] < profile[
            "outcome_maximum"
        ]


def test_v4_protocol_refuses_drafts_and_freezes_every_input():
    protocol = benchmark_v4.load_protocol()

    with pytest.raises(RuntimeError, match="fingerprint does not match"):
        benchmark_v4.require_frozen(protocol)

    draft = json.loads(json.dumps(protocol))
    draft["status"] = "draft"
    draft["freeze"]["optimizer_revision"] = None
    draft["freeze"]["protocol_revision"] = None
    draft["freeze"]["evaluation_fingerprint"] = None
    with pytest.raises(RuntimeError, match="not frozen"):
        benchmark_v4.require_frozen(draft)

    protocol["statistics"]["minimum_relative_reduction_ci_lower"] = -0.01
    with pytest.raises(RuntimeError, match="fingerprint does not match"):
        benchmark_v4.require_frozen(protocol)


def test_v4_uses_all_preregistered_comparators_and_strict_unit_guardrail():
    protocol = benchmark_v4.load_protocol()

    assert protocol["methods"]["baselines"] == [
        "seeded-random-search",
        "sequential-maximin-space-filling",
        "regularized-linear-response-surface",
        "regularized-quadratic-response-surface",
    ]
    assert protocol["statistics"]["minimum_relative_reduction_ci_lower"] == 0.0
    assert protocol["statistics"][
        "minimum_success_rate_difference_ci_lower"
    ] == -0.05
    assert protocol["statistics"][
        "minimum_evaluation_unit_non_worse_fraction"
    ] == 1.0
    assert len(protocol["claim_boundary"]["not_permitted"]) == 4


def test_v4_retained_result_discloses_failed_linear_and_ablation_guardrails():
    protocol = benchmark_v4.load_protocol()
    result = json.loads(
        (BENCHMARK_PATH.parent / "latest-results-v4.json").read_text(
            encoding="utf-8"
        )
    )

    assert result["evaluation_fingerprint"] == protocol["freeze"][
        "evaluation_fingerprint"
    ]
    assert result["summary"]["episode_count"] == 1250
    assert result["summary"][
        "experiment_reduction_vs_all_preregistered_baselines"
    ] == "not-demonstrated"
    assert result["summary"]["mechanism_feature_contribution"] == (
        "not-demonstrated"
    )
    effects = {
        item["comparator"]: item
        for item in result["summary"]["paired_effects"]
    }
    assert effects["seeded-random-search"]["passed"] is True
    assert effects["sequential-maximin-space-filling"]["passed"] is True
    assert effects["regularized-quadratic-response-surface"]["passed"] is True
    assert effects["regularized-linear-response-surface"]["passed"] is False
    assert effects["regularized-linear-response-surface"][
        "relative_trial_reduction"
    ] == pytest.approx(-0.0869822485)
    assert effects["ingot-v7-without-mechanism-features"]["passed"] is False
    assert effects["ingot-v7-without-mechanism-features"][
        "evaluation_unit_non_worse_fraction"
    ] == pytest.approx(0.92)


def test_v6_frozen_fixture_rejects_successor_algorithm_and_keeps_data_checks():
    protocol = benchmark_v6.load_protocol()
    report = benchmark_v6.integrity_report(protocol)

    assert report["status"] == "frozen"
    assert report["full_evaluation_allowed"] is False
    assert report["evaluation_unit_count"] == 3
    assert len(report["candidate_evaluation_fingerprint"]) == 64
    profile = report["datasets"]["lnp3"]
    assert profile["rows"] == 768
    assert profile["fixture_sha256"] == (
        "30d2b926a53976b36c210d988f7dd241be94037bb34d2913bc1077ac4e5bfdac"
    )
    assert profile["evaluation_unit_count"] == 3
    assert profile["minimum_unit_rows"] == 256
    assert profile["maximum_unit_rows"] == 256
    assert profile["mechanism_feature_count"] == 4
    assert profile["duplicate_control_context_settings"] == 0


def test_v6_freeze_covers_algorithm_data_dependencies_and_protocol():
    protocol = benchmark_v6.load_protocol()
    candidate = benchmark_v6.evaluation_fingerprint(protocol)
    frozen = json.loads(json.dumps(protocol))
    frozen["status"] = "frozen"
    frozen["freeze"]["optimizer_revision"] = "a" * 40
    frozen["freeze"]["protocol_revision"] = "b" * 40
    frozen["freeze"]["evaluation_fingerprint"] = candidate

    benchmark_v6.require_frozen(frozen)
    frozen["datasets"]["lnp3"]["additional_trial_budget"] += 1
    with pytest.raises(RuntimeError, match="fingerprint does not match"):
        benchmark_v6.require_frozen(frozen)


def test_v6_paired_replay_keeps_solid_lipid_context_out_of_controls():
    protocol = benchmark_v6.load_protocol()
    rows = benchmark_v6.load_rows("lnp3", protocol)
    unit_id, context, unit_rows = benchmark_v6.group_evaluation_units(
        "lnp3", rows, protocol
    )[0]
    record = benchmark_v6.run_unit(
        "lnp3",
        unit_id,
        context,
        unit_rows,
        unit_index=0,
        protocol=protocol,
        episode_count=1,
    )

    assert context == {"solid_lipid": "Compritol_888"}
    assert record["candidate_settings"] == 256
    assert len(record["episodes"][0]["initial_run_ids"]) == 12
    assert set(record["episodes"][0]["methods"]) == {
        benchmark_v6.PRIMARY,
        benchmark_v6.ABLATION,
        *protocol["methods"]["baselines"],
    }


def test_v6_uses_all_strong_baselines_and_strict_context_guardrail():
    protocol = benchmark_v6.load_protocol()

    assert protocol["methods"]["baselines"] == [
        "seeded-random-search",
        "sequential-maximin-space-filling",
        "regularized-linear-response-surface",
        "regularized-quadratic-response-surface",
    ]
    assert protocol["statistics"]["minimum_relative_reduction_ci_lower"] == 0.0
    assert protocol["statistics"][
        "minimum_success_rate_difference_ci_lower"
    ] == -0.05
    assert protocol["statistics"][
        "minimum_evaluation_unit_non_worse_fraction"
    ] == 1.0
    assert len(protocol["claim_boundary"]["not_permitted"]) == 4


def test_v6_retained_result_discloses_failed_model_and_ablation_guardrails():
    protocol = benchmark_v6.load_protocol()
    result = json.loads(
        (BENCHMARK_PATH.parent / "latest-results-v6.json").read_text(
            encoding="utf-8"
        )
    )

    assert result["evaluation_fingerprint"] == protocol["freeze"][
        "evaluation_fingerprint"
    ]
    assert result["summary"]["episode_count"] == 300
    assert result["summary"][
        "experiment_reduction_vs_all_preregistered_baselines"
    ] == "not-demonstrated"
    assert result["summary"]["mechanism_feature_contribution"] == (
        "not-demonstrated"
    )
    effects = {
        item["comparator"]: item
        for item in result["summary"]["paired_effects"]
    }
    assert effects["seeded-random-search"]["passed"] is True
    assert effects["sequential-maximin-space-filling"]["passed"] is True
    assert effects["regularized-linear-response-surface"]["passed"] is False
    assert effects["regularized-linear-response-surface"][
        "relative_trial_reduction"
    ] == pytest.approx(0.2761250953)
    assert effects["regularized-linear-response-surface"][
        "evaluation_unit_non_worse_fraction"
    ] == pytest.approx(2 / 3)
    assert effects["regularized-quadratic-response-surface"]["passed"] is False
    assert effects["regularized-quadratic-response-surface"][
        "relative_trial_reduction"
    ] == pytest.approx(-0.0845714286)
    assert effects["ingot-v8-without-mechanism-features"]["passed"] is False
    assert effects["ingot-v8-without-mechanism-features"][
        "evaluation_unit_non_worse_fraction"
    ] == 0.0


def test_v7_frozen_fixture_rejects_successor_algorithm_and_keeps_data_checks():
    protocol = benchmark_v7.load_protocol()
    report = benchmark_v7.integrity_report(protocol)

    assert report["status"] == "frozen"
    assert report["full_evaluation_allowed"] is False
    assert report["candidate_evaluation_fingerprint"] != protocol["freeze"][
        "evaluation_fingerprint"
    ]
    assert report["evaluation_unit_count"] == 4
    assert len(report["candidate_evaluation_fingerprint"]) == 64
    assert set(report["datasets"]) == {
        "oer_plate_3496",
        "oer_plate_3851",
        "oer_plate_3860",
        "oer_plate_4098",
    }
    assert {profile["rows"] for profile in report["datasets"].values()} == {
        2119,
        2120,
        2121,
    }
    assert all(
        profile["control_count"] == 6
        and profile["context_count"] == 0
        and profile["mechanism_feature_count"] == 3
        and profile["evaluation_unit_count"] == 1
        and profile["duplicate_control_context_settings"] == 0
        and "outcome_minimum" not in profile
        for profile in report["datasets"].values()
    )


def test_v7_frozen_protocol_rejects_the_current_successor_algorithm():
    protocol = benchmark_v7.load_protocol()
    with pytest.raises(RuntimeError, match="fingerprint does not match"):
        benchmark_v7.require_frozen(protocol)


def test_v7_freeze_fingerprint_covers_all_data_and_method_inputs():
    protocol = benchmark_v7.load_protocol()
    candidate = benchmark_v7.evaluation_fingerprint(protocol)
    frozen = json.loads(json.dumps(protocol))
    frozen["status"] = "frozen"
    frozen["freeze"]["optimizer_revision"] = "a" * 40
    frozen["freeze"]["protocol_revision"] = "b" * 40
    frozen["freeze"]["evaluation_fingerprint"] = candidate

    benchmark_v7.require_frozen(frozen)
    frozen["datasets"]["oer_plate_3496"]["additional_trial_budget"] += 1
    with pytest.raises(RuntimeError, match="fingerprint does not match"):
        benchmark_v7.require_frozen(frozen)


def test_v7_keeps_all_strong_baselines_and_per_plate_guardrail():
    protocol = benchmark_v7.load_protocol()

    assert protocol["methods"]["baselines"] == [
        "seeded-random-search",
        "sequential-maximin-space-filling",
        "regularized-linear-response-surface",
        "regularized-quadratic-response-surface",
    ]
    assert protocol["statistics"]["minimum_relative_reduction_ci_lower"] == 0.0
    assert protocol["statistics"][
        "minimum_success_rate_difference_ci_lower"
    ] == -0.05
    assert protocol["statistics"][
        "minimum_evaluation_unit_non_worse_fraction"
    ] == 1.0
    assert protocol["statistics"]["unsuccessful_trial_value"] == 25
    assert all(
        settings["objective"]["threshold_rule"]["quantile"] == 0.01
        and settings["initial_observations"] == 24
        and settings["additional_trial_budget"] == 24
        for settings in protocol["datasets"].values()
    )


def test_v7_retained_result_discloses_quadratic_and_feature_failures():
    protocol = benchmark_v7.load_protocol()
    result = json.loads(
        (BENCHMARK_PATH.parent / "latest-results-v7.json").read_text(
            encoding="utf-8"
        )
    )

    assert result["evaluation_fingerprint"] == protocol["freeze"][
        "evaluation_fingerprint"
    ]
    assert result["summary"]["episode_count"] == 400
    assert result["summary"][
        "experiment_reduction_vs_all_preregistered_baselines"
    ] == "not-demonstrated"
    assert result["summary"]["mechanism_feature_contribution"] == (
        "not-demonstrated"
    )
    effects = {
        item["comparator"]: item
        for item in result["summary"]["paired_effects"]
    }
    assert effects["seeded-random-search"]["passed"] is True
    assert effects["sequential-maximin-space-filling"]["passed"] is True
    assert effects["regularized-linear-response-surface"]["passed"] is True
    assert effects["regularized-quadratic-response-surface"]["passed"] is False
    assert effects["regularized-quadratic-response-surface"][
        "relative_trial_reduction"
    ] == pytest.approx(0.1166627934)
    assert effects["regularized-quadratic-response-surface"][
        "evaluation_unit_non_worse_fraction"
    ] == 0.5
    assert effects["ingot-v10-without-composition-features"]["passed"] is False
    assert effects["ingot-v10-without-composition-features"][
        "relative_trial_reduction"
    ] == pytest.approx(0.0013137152)
    assert effects["ingot-v10-without-composition-features"][
        "evaluation_unit_non_worse_fraction"
    ] == 0.5
