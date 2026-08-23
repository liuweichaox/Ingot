"""Verify unseen-data acceptance integrity, freeze, and replay boundaries."""

from __future__ import annotations

import importlib.util
import json
from pathlib import Path

import pytest


BENCHMARK_PATH = (
    Path(__file__).parents[2]
    / "tools"
    / "public-validation"
    / "benchmark_unseen.py"
)
SPEC = importlib.util.spec_from_file_location(
    "ingot_unseen_data_acceptance", BENCHMARK_PATH
)
assert SPEC is not None and SPEC.loader is not None
benchmark = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(benchmark)


def test_unseen_reaction_fixtures_pass_preregistered_data_quality_gate():
    protocol = benchmark.load_protocol()
    report = benchmark.integrity_report(protocol)

    assert report["status"] == "frozen"
    assert report["full_evaluation_allowed"] is True
    assert report["evaluation_unit_count"] == 2
    assert report["datasets"]["fullerenes"]["rows"] == 216
    assert report["datasets"]["suzuki"]["rows"] == 247
    assert all(
        item["null_or_non_finite_values"] == 0
        and item["duplicate_identifiers"] == 0
        and item["duplicate_control_context_settings"] == 0
        for item in report["datasets"].values()
    )


def test_unseen_protocol_keeps_fixed_baselines_budgets_and_separate_claims():
    protocol = benchmark.load_protocol()

    assert protocol["methods"]["baselines"] == [
        "seeded-random-search",
        "sequential-maximin-space-filling",
        "regularized-linear-response-surface",
        "regularized-quadratic-response-surface",
    ]
    assert protocol["statistics"][
        "model_free_minimum_relative_reduction_ci_lower"
    ] == 0.1
    assert protocol["statistics"][
        "response_surface_noninferiority_margin"
    ] == -0.1
    assert protocol["episodes"]["count_per_evaluation_unit"] == 200
    assert {
        name: (
            settings["initial_observations"],
            settings["additional_trial_budget"],
            settings["mechanism_features"][0]["name"],
        )
        for name, settings in protocol["datasets"].items()
    } == {
        "fullerenes": (10, 12, "reagent_contact_exposure"),
        "suzuki": (15, 12, "catalyst_temperature_exposure"),
    }


def test_unseen_protocol_accepts_frozen_state_and_refuses_tampering():
    protocol = benchmark.load_protocol()
    benchmark.require_frozen(protocol)

    draft = json.loads(json.dumps(protocol))
    draft["status"] = "draft"
    with pytest.raises(RuntimeError, match="not frozen"):
        benchmark.require_frozen(draft)

    frozen = json.loads(json.dumps(protocol))
    frozen["datasets"]["fullerenes"]["additional_trial_budget"] += 1
    with pytest.raises(RuntimeError, match="fingerprint does not match"):
        benchmark.require_frozen(frozen)


def test_retained_unseen_result_discloses_model_free_and_feature_failures():
    protocol = benchmark.load_protocol()
    result = json.loads(
        (BENCHMARK_PATH.parent / "unseen-results.json").read_text(
            encoding="utf-8"
        )
    )

    assert result["evaluation_fingerprint"] == protocol["freeze"][
        "evaluation_fingerprint"
    ]
    assert result["summary"]["episode_count"] == 400
    assert result["summary"]["core_experiment_selection"] == "not-demonstrated"
    assert result["summary"]["response_surface_added_value"] is True
    assert result["summary"]["mechanism_feature_contribution"] == (
        "not-demonstrated"
    )
    effects = {
        item["comparator"]: item
        for item in result["summary"]["paired_effects"]
    }
    assert effects["seeded-random-search"]["passed"] is False
    assert effects["sequential-maximin-space-filling"]["passed"] is False
    assert effects["regularized-linear-response-surface"]["passed"] is True
    assert effects["regularized-quadratic-response-surface"]["passed"] is True
    assert effects[benchmark.ABLATION]["passed"] is False
    assert effects["seeded-random-search"][
        "evaluation_unit_relative_reductions"
    ]["fullerenes:all"] == pytest.approx(-0.2300762301)
    assert effects["sequential-maximin-space-filling"][
        "evaluation_unit_relative_reductions"
    ]["fullerenes:all"] == pytest.approx(-0.4064976228)


@pytest.mark.parametrize("dataset", ["fullerenes", "suzuki"])
def test_unseen_replay_shares_initial_history_without_outcome_leakage(dataset):
    protocol = benchmark.load_protocol()
    rows = benchmark.load_rows(dataset, protocol)
    unit_id, context, unit_rows = benchmark.group_evaluation_units(
        dataset, rows, protocol
    )[0]
    record = benchmark.run_unit(
        dataset,
        unit_id,
        context,
        unit_rows,
        unit_index=list(benchmark.DATASETS).index(dataset),
        protocol=protocol,
        episode_count=1,
    )

    episode = record["episodes"][0]
    assert len(episode["initial_run_ids"]) == record["initial_observations"]
    assert set(episode["methods"]) == {
        benchmark.PRIMARY,
        benchmark.ABLATION,
        *protocol["methods"]["baselines"],
    }
    assert all(
        len(method["selected_run_ids"]) <= record["additional_trial_budget"]
        for method in episode["methods"].values()
    )
