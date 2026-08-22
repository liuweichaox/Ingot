"""Verify the committed public-data benchmark and its claim boundary."""

import importlib.util
from pathlib import Path


BENCHMARK_PATH = (
    Path(__file__).parents[2] / "tools" / "public-validation" / "benchmark.py"
)
SPEC = importlib.util.spec_from_file_location("ingot_public_validation", BENCHMARK_PATH)
benchmark = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(benchmark)


def test_public_fixture_is_verified_and_categorical_contexts_are_isolated():
    rows = benchmark.load_rows()
    scenarios = benchmark.group_scenarios(rows)

    assert len(scenarios) == 6
    assert all(len(values) == 27 for _, values in scenarios)
    assert set(benchmark.CONTEXT_FIELDS).isdisjoint(benchmark.CONTROL_FIELDS)


def test_public_replay_reports_workflow_and_efficiency_as_separate_claims():
    context, rows = benchmark.group_scenarios(benchmark.load_rows())[0]
    record = benchmark.run_scenario(context, rows, seeds=1, budget=6)
    summary = benchmark.summarize([record])

    assert record["context_isolated"] is True
    assert summary["workflow_validation"] == "passed"
    assert summary["experiment_reduction_claim"] in {
        "passed-public-benchmark",
        "not-demonstrated",
    }


def test_committed_full_result_keeps_public_claim_boundary_explicit():
    result_path = BENCHMARK_PATH.parent / "latest-results.json"
    payload = __import__("json").loads(result_path.read_text(encoding="utf-8"))

    assert payload["method"]["seeds"] == 20
    assert payload["summary"]["workflow_validation"] == "passed"
    assert payload["summary"]["categorical_context_isolation_passed"] is True
    assert payload["summary"]["experiment_reduction_claim"] == "not-demonstrated"
