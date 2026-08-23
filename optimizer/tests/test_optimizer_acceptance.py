"""Verify the current optimizer acceptance protocol and replay boundaries."""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path

import pytest


BENCHMARK_PATH = (
    Path(__file__).parents[2]
    / "tools"
    / "public-validation"
    / "benchmark_acceptance.py"
)
SPEC = importlib.util.spec_from_file_location(
    "ingot_optimizer_acceptance", BENCHMARK_PATH
)
assert SPEC is not None and SPEC.loader is not None
benchmark = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(benchmark)


def test_acceptance_fixtures_pass_quality_gates_but_current_candidate_is_new():
    protocol = benchmark.load_protocol()
    report = benchmark.integrity_report(protocol)

    assert report["status"] == "frozen"
    assert report["full_evaluation_allowed"] is False
    assert report["candidate_evaluation_fingerprint"] != protocol["freeze"][
        "evaluation_fingerprint"
    ]
    assert report["evaluation_unit_count"] == 3
    assert {
        name: (item["rows"], item["control_count"])
        for name, item in report["datasets"].items()
    } == {
        "alkox": (104, 4),
        "p3ht": (178, 5),
        "hplc": (1007, 6),
    }
    assert all(
        item["null_or_non_finite_values"] == 0
        and item["duplicate_identifiers"] == 0
        and item["duplicate_control_context_settings"] == 0
        for item in report["datasets"].values()
    )


def test_acceptance_protocol_keeps_fixed_baselines_budgets_and_gates():
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
    assert protocol["statistics"][
        "evaluation_unit_noninferiority_margin"
    ] == -0.1
    assert protocol["episodes"]["count_per_evaluation_unit"] == 150
    assert {
        name: (
            settings["initial_observations"],
            settings["additional_trial_budget"],
        )
        for name, settings in protocol["datasets"].items()
    } == {
        "alkox": (10, 12),
        "p3ht": (15, 12),
        "hplc": (18, 12),
    }


def test_old_acceptance_protocol_refuses_the_successor_and_tampering():
    protocol = benchmark.load_protocol()
    with pytest.raises(RuntimeError, match="fingerprint does not match"):
        benchmark.require_frozen(protocol)

    draft = json.loads(json.dumps(protocol))
    draft["status"] = "draft"
    with pytest.raises(RuntimeError, match="not frozen"):
        benchmark.require_frozen(draft)

    frozen = json.loads(json.dumps(protocol))
    frozen["datasets"]["alkox"]["additional_trial_budget"] += 1
    with pytest.raises(RuntimeError, match="fingerprint does not match"):
        benchmark.require_frozen(frozen)


def test_retained_acceptance_result_discloses_linear_subgroup_failures():
    protocol = benchmark.load_protocol()
    result = json.loads(
        (BENCHMARK_PATH.parent / "acceptance-results.json").read_text(
            encoding="utf-8"
        )
    )

    assert result["evaluation_fingerprint"] == protocol["freeze"][
        "evaluation_fingerprint"
    ]
    assert result["summary"]["episode_count"] == 450
    assert result["summary"]["core_experiment_selection"] == (
        "not-demonstrated"
    )
    assert result["summary"]["response_surface_added_value"] is True
    effects = {
        item["comparator"]: item
        for item in result["summary"]["paired_effects"]
    }
    assert effects["seeded-random-search"]["passed"] is True
    assert effects["sequential-maximin-space-filling"]["passed"] is True
    assert effects["regularized-linear-response-surface"]["passed"] is False
    assert effects["regularized-quadratic-response-surface"]["passed"] is True
    assert effects["regularized-linear-response-surface"][
        "evaluation_unit_relative_reductions"
    ]["alkox:all"] == pytest.approx(-0.3208430913)
    assert effects["regularized-linear-response-surface"][
        "evaluation_unit_relative_reductions"
    ]["p3ht:all"] == pytest.approx(-0.6836158192)


@pytest.mark.parametrize("dataset", ["alkox", "p3ht", "hplc"])
def test_acceptance_replay_shares_history_without_outcome_leakage(dataset):
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
        *protocol["methods"]["baselines"],
    }
    assert all(
        len(method["selected_run_ids"])
        <= record["additional_trial_budget"]
        for method in episode["methods"].values()
    )
