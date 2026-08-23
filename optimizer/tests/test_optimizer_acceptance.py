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


def test_acceptance_fixtures_pass_preregistered_quality_gates():
    protocol = benchmark.load_protocol()
    report = benchmark.integrity_report(protocol)

    assert report["status"] == "draft"
    assert report["full_evaluation_allowed"] is False
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


def test_acceptance_protocol_refuses_execution_before_freeze():
    protocol = benchmark.load_protocol()
    with pytest.raises(RuntimeError, match="not frozen"):
        benchmark.require_frozen(protocol)

    frozen = json.loads(json.dumps(protocol))
    frozen["status"] = "frozen"
    frozen["freeze"]["optimizer_revision"] = "a" * 40
    frozen["freeze"]["protocol_revision"] = "b" * 40
    frozen["freeze"]["evaluation_fingerprint"] = (
        benchmark.evaluation_fingerprint(frozen)
    )
    benchmark.require_frozen(frozen)
    frozen["datasets"]["alkox"]["additional_trial_budget"] += 1
    with pytest.raises(RuntimeError, match="fingerprint does not match"):
        benchmark.require_frozen(frozen)


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
