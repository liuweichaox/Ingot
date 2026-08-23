#!/usr/bin/env python3
"""Run the versioned, paired public-data experiment-efficiency benchmark v2.

Each finite public dataset is treated as a hidden-result oracle. All methods
receive the same initial observations and reveal only outcomes for settings
they select. No outcomes are synthesized or substituted.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
from collections import defaultdict
from pathlib import Path
from typing import Iterable

import numpy as np

from ingot_optimizer import Campaign, Objective, Variable
from ingot_optimizer.replay import replay_history_pool


ROOT = Path(__file__).resolve().parent
PROTOCOL_PATH = ROOT / "protocol-v2.json"
BASELINE_NAMES = (
    "seeded-random-search",
    "regularized-linear-response-surface",
)
DATASETS = {
    "fdm": {
        "context_fields": ("printer_type", "material", "infill_pattern"),
        "control_fields": (
            "layer_thickness_mm",
            "infill_density_pct",
            "speed_mm_s",
        ),
        "outcome_fields": ("roughness_avg", "peak_stress_kpa"),
        "id_field": "sample_no",
    },
    "crossed_barrel": {
        "context_fields": ("column_count",),
        "control_fields": (
            "twist_angle_deg",
            "outer_radius_mm",
            "wall_thickness_mm",
        ),
        "outcome_fields": ("toughness_j",),
        "id_field": "setting_id",
    },
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def load_protocol() -> dict:
    protocol = json.loads(PROTOCOL_PATH.read_text(encoding="utf-8"))
    if protocol.get("schema") != "ingot-public-validation-protocol-v2":
        raise ValueError("unsupported public validation protocol")
    if set(protocol["sources"]) != set(DATASETS):
        raise ValueError("protocol sources and benchmark adapters disagree")
    unsuccessful = int(protocol["statistics"]["unsuccessful_trial_value"])
    for name, settings in protocol["datasets"].items():
        initial = int(settings["initial_observations"])
        budget = int(settings["budget"])
        if initial < 3 or budget <= initial:
            raise ValueError(f"invalid episode budget for {name}")
        if budget - initial + 1 != unsuccessful:
            raise ValueError("all datasets must use the same capped additional budget")
    return protocol


def load_rows(dataset: str, protocol: dict) -> list[dict[str, str]]:
    source = protocol["sources"][dataset]
    path = ROOT / source["fixture"]
    actual_hash = sha256(path)
    if actual_hash != source["fixture_sha256"]:
        raise ValueError(
            f"{dataset} fixture checksum mismatch: expected "
            f"{source['fixture_sha256']}, got {actual_hash}"
        )
    with path.open(encoding="utf-8", newline="") as stream:
        rows = list(csv.DictReader(stream))
    if len(rows) != int(source["fixture_rows"]):
        raise ValueError(f"unexpected {dataset} fixture row count")
    identifier = DATASETS[dataset]["id_field"]
    identifiers = [row[identifier] for row in rows]
    if len(identifiers) != len(set(identifiers)):
        raise ValueError(f"{dataset} fixture contains duplicate identifiers")
    if "replicate_count" in rows[0]:
        if sum(int(row["replicate_count"]) for row in rows) != int(
            source["source_rows"]
        ):
            raise ValueError(f"{dataset} replicate counts do not reconcile to source")
        keys = DATASETS[dataset]["context_fields"] + DATASETS[dataset]["control_fields"]
        if len({tuple(row[field] for field in keys) for row in rows}) != len(rows):
            raise ValueError(f"{dataset} fixture contains duplicate candidate settings")
    return rows


def scenario_key(context_values: tuple[str, ...]) -> str:
    return "|".join(context_values)


def group_scenarios(
    dataset: str, rows: list[dict[str, str]], protocol: dict
) -> list[tuple[tuple[str, ...], list[dict[str, str]]]]:
    fields = DATASETS[dataset]["context_fields"]
    grouped: dict[tuple[str, ...], list[dict[str, str]]] = defaultdict(list)
    for row in rows:
        grouped[tuple(row[field] for field in fields)].append(row)
    allowed = protocol["datasets"][dataset]["scenario_thresholds"]
    scenarios = sorted(
        (
            (context, values)
            for context, values in grouped.items()
            if scenario_key(context) in allowed
        ),
        key=lambda item: tuple(
            float(value) if value.replace(".", "", 1).isdigit() else value
            for value in item[0]
        ),
    )
    if set(scenario_key(context) for context, _ in scenarios) != set(allowed):
        raise ValueError(f"{dataset} fixture does not cover every frozen scenario")
    for context, values in scenarios:
        if len(values) < int(protocol["datasets"][dataset]["budget"]):
            raise ValueError(f"{dataset} context {context} is smaller than its budget")
    if dataset == "fdm" and (
        len(scenarios) != 6 or any(len(values) != 27 for _, values in scenarios)
    ):
        raise ValueError("FDM fixture must retain six complete 27-point grids")
    if dataset == "crossed_barrel" and (
        len(scenarios) != 4 or any(len(values) != 150 for _, values in scenarios)
    ):
        raise ValueError(
            "crossed-barrel fixture must retain four complete 150-setting grids"
        )
    return scenarios


def build_campaign(dataset: str, context_values: tuple[str, ...], protocol: dict) -> Campaign:
    metadata = DATASETS[dataset]
    settings = protocol["datasets"][dataset]
    key = scenario_key(context_values)
    thresholds = settings["scenario_thresholds"].get(key)
    if thresholds is None:
        raise ValueError(f"protocol has no frozen threshold for {dataset}:{key}")
    variables = [
        Variable(name, float(settings["controls"][name][0]), float(settings["controls"][name][1]))
        for name in metadata["control_fields"]
    ]
    if dataset == "fdm":
        objectives = [
            Objective(
                "roughness_avg",
                "le",
                threshold=float(thresholds["roughness_avg_max"]),
                outcome_lower_bound=float(settings["outcome_bounds"]["roughness_avg"][0]),
                outcome_upper_bound=float(settings["outcome_bounds"]["roughness_avg"][1]),
                unit="um",
            ),
            Objective(
                "peak_stress_kpa",
                "ge",
                threshold=float(thresholds["peak_stress_kpa_min"]),
                outcome_lower_bound=float(settings["outcome_bounds"]["peak_stress_kpa"][0]),
                outcome_upper_bound=float(settings["outcome_bounds"]["peak_stress_kpa"][1]),
                unit="kPa",
            ),
        ]
    else:
        objectives = [
            Objective(
                "toughness_j",
                "ge",
                threshold=float(thresholds["toughness_j_min"]),
                outcome_lower_bound=float(settings["outcome_bounds"]["toughness_j"][0]),
                outcome_upper_bound=float(settings["outcome_bounds"]["toughness_j"][1]),
                unit="J",
            )
        ]
    context = dict(zip(metadata["context_fields"], context_values, strict=True))
    return Campaign(f"{dataset}-public-v2-{key}", variables, objectives, context=context)


def build_history(dataset: str, rows: list[dict[str, str]]) -> list[dict]:
    metadata = DATASETS[dataset]
    return [
        {
            "params": {name: float(row[name]) for name in metadata["control_fields"]},
            "outcomes": {name: float(row[name]) for name in metadata["outcome_fields"]},
            "occurred_at": float(position),
            "run_id": f"{dataset}-{row[metadata['id_field']]}",
        }
        for position, row in enumerate(rows, start=1)
    ]


def episode_history(
    history: list[dict],
    *,
    scenario_index: int,
    episode_index: int,
    protocol: dict,
    initial_count: int,
) -> tuple[list[dict], list[str], int]:
    seed = int(protocol["episodes"]["base_seed"]) + scenario_index * 100_000 + episode_index
    rng = np.random.default_rng(seed)
    initial = [int(value) for value in rng.choice(len(history), initial_count, replace=False)]
    initial_set = set(initial)
    order = initial + [index for index in range(len(history)) if index not in initial_set]
    reordered = [
        {**history[index], "occurred_at": float(position)}
        for position, index in enumerate(order, start=1)
    ]
    return reordered, [str(row["run_id"]) for row in reordered[:initial_count]], seed


def first_hit(campaign: Campaign, history: list[dict], selected: Iterable[int]) -> int | None:
    return next(
        (
            position
            for position, index in enumerate(selected, start=1)
            if campaign.distance_to_spec(history[index]["outcomes"]) <= 0
        ),
        None,
    )


def regularized_linear_run(
    campaign: Campaign,
    history: list[dict],
    *,
    budget: int,
    initial_count: int,
    ridge: float,
    seed: int,
) -> int | None:
    selected = list(range(initial_count))
    remaining = list(range(initial_count, len(history)))
    hit = first_hit(campaign, history, selected)
    rng = np.random.default_rng(seed + 30_000)
    while hit is None and remaining and len(selected) < budget:
        observed = np.asarray(
            [
                np.concatenate(
                    ([1.0], campaign.to_unit(history[index]["params"]))
                )
                for index in selected
            ]
        )
        distances = np.asarray(
            [campaign.distance_to_spec(history[index]["outcomes"]) for index in selected]
        )
        penalty = np.eye(observed.shape[1]) * ridge
        penalty[0, 0] = 0.0
        coefficients = np.linalg.solve(observed.T @ observed + penalty, observed.T @ distances)
        candidates = np.asarray(
            [
                np.concatenate(
                    ([1.0], campaign.to_unit(history[index]["params"]))
                )
                for index in remaining
            ]
        )
        predictions = candidates @ coefficients
        position = int(np.argmin(predictions + rng.random(len(remaining)) * 1e-12))
        selected.append(remaining.pop(position))
        hit = first_hit(campaign, history, selected)
    return hit


def capped(values: list[int | None], unsuccessful: int) -> np.ndarray:
    return np.asarray(
        [value if value is not None else unsuccessful for value in values],
        dtype=float,
    )


def method_summary(values: list[int | None], unsuccessful: int) -> dict:
    scored = capped(values, unsuccessful)
    hits = [value for value in values if value is not None]
    return {
        "success_rate": len(hits) / len(values),
        "mean_capped_additional_trials": float(scored.mean()),
        "median_successful_additional_trials": float(np.median(hits)) if hits else None,
    }


def run_scenario(
    dataset: str,
    context_values: tuple[str, ...],
    rows: list[dict[str, str]],
    *,
    scenario_index: int,
    protocol: dict,
    episode_count: int,
) -> dict:
    metadata = DATASETS[dataset]
    rows = sorted(rows, key=lambda row: row[metadata["id_field"]])
    campaign = build_campaign(dataset, context_values, protocol)
    source_history = build_history(dataset, rows)
    settings = protocol["datasets"][dataset]
    budget = int(settings["budget"])
    initial_count = int(settings["initial_observations"])
    unsuccessful = int(protocol["statistics"]["unsuccessful_trial_value"])
    ridge = float(protocol["methods"]["linear_ridge"])
    episodes = []
    optimizer_values: list[int | None] = []
    random_values: list[int | None] = []
    linear_values: list[int | None] = []
    initial_designs: set[tuple[str, ...]] = set()
    candidate_index = excluded_initial_success = duplicate_initial_designs = 0
    while len(episodes) < episode_count:
        if candidate_index > 1_000_000:
            raise RuntimeError(
                "unable to generate enough eligible episodes for "
                f"{dataset}:{context_values}"
            )
        history, initial_run_ids, episode_seed = episode_history(
            source_history,
            scenario_index=scenario_index,
            episode_index=candidate_index,
            protocol=protocol,
            initial_count=initial_count,
        )
        current_candidate = candidate_index
        candidate_index += 1
        initial_key = tuple(sorted(initial_run_ids))
        if initial_key in initial_designs:
            duplicate_initial_designs += 1
            continue
        initial_designs.add(initial_key)
        if first_hit(campaign, history, range(initial_count)) is not None:
            excluded_initial_success += 1
            continue
        replay = replay_history_pool(
            campaign,
            history,
            budget=budget,
            n_seeds=1,
            initial_observation_count=initial_count,
            seed_offset=episode_seed,
        )
        optimizer_total = replay["raw_optimizer"][0]
        random_total = replay["raw_random"][0]
        linear_total = regularized_linear_run(
            campaign,
            history,
            budget=budget,
            initial_count=initial_count,
            ridge=ridge,
            seed=episode_seed,
        )
        optimizer_trial = optimizer_total - initial_count if optimizer_total is not None else None
        random_trial = random_total - initial_count if random_total is not None else None
        linear_trial = linear_total - initial_count if linear_total is not None else None
        optimizer_values.append(optimizer_trial)
        random_values.append(random_trial)
        linear_values.append(linear_trial)
        episodes.append(
            {
                "episode": len(episodes),
                "candidate_index": current_candidate,
                "seed": episode_seed,
                "initial_run_ids": initial_run_ids,
                "optimizer_trials": optimizer_trial,
                "seeded_random_trials": random_trial,
                "regularized_linear_trials": linear_trial,
            }
        )
    feasible = sum(campaign.distance_to_spec(row["outcomes"]) <= 0 for row in source_history)
    return {
        "dataset": dataset,
        "context": dict(zip(metadata["context_fields"], context_values, strict=True)),
        "status": "completed",
        "candidate_settings": len(source_history),
        "feasible_settings": feasible,
        "initial_observations": initial_count,
        "additional_trial_budget": budget - initial_count,
        "unsuccessful_trial_value": unsuccessful,
        "screened_initial_designs": candidate_index,
        "excluded_initial_success": excluded_initial_success,
        "duplicate_initial_designs": duplicate_initial_designs,
        "eligible_unique_initial_designs": len(episodes),
        "optimizer": method_summary(optimizer_values, unsuccessful),
        "seeded-random-search": method_summary(random_values, unsuccessful),
        "regularized-linear-response-surface": method_summary(linear_values, unsuccessful),
        "episodes": episodes,
    }


def _values(record: dict, method: str) -> list[int | None]:
    field = {
        "optimizer": "optimizer_trials",
        "seeded-random-search": "seeded_random_trials",
        "regularized-linear-response-surface": "regularized_linear_trials",
    }[method]
    return [episode[field] for episode in record["episodes"]]


def paired_effect(records: list[dict], baseline: str, protocol: dict) -> dict:
    statistics = protocol["statistics"]
    pairs = [
        (
            record["dataset"],
            capped(_values(record, "optimizer"), int(record["unsuccessful_trial_value"])),
            capped(_values(record, baseline), int(record["unsuccessful_trial_value"])),
        )
        for record in records
    ]

    def estimate(sampled: list[tuple[str, np.ndarray, np.ndarray]]) -> tuple[float, float]:
        optimizer = np.concatenate([pair[1] for pair in sampled])
        comparison = np.concatenate([pair[2] for pair in sampled])
        relative = float((comparison.mean() - optimizer.mean()) / comparison.mean())
        unsuccessful = float(statistics["unsuccessful_trial_value"])
        success = float((optimizer < unsuccessful).mean() - (comparison < unsuccessful).mean())
        return relative, success

    point_relative, point_success = estimate(pairs)
    context_non_worse = float(
        np.mean(
            [
                optimizer.mean() <= comparison.mean()
                for _, optimizer, comparison in pairs
            ]
        )
    )
    by_dataset: dict[str, list[tuple[str, np.ndarray, np.ndarray]]] = defaultdict(list)
    for pair in pairs:
        by_dataset[pair[0]].append(pair)
    rng = np.random.default_rng(int(statistics["bootstrap_seed"]))
    relative_samples = []
    success_samples = []
    for _ in range(int(statistics["bootstrap_samples"])):
        sampled_pairs = []
        for dataset_pairs in by_dataset.values():
            for context_index in rng.integers(0, len(dataset_pairs), len(dataset_pairs)):
                dataset, optimizer, comparison = dataset_pairs[int(context_index)]
                indexes = rng.integers(0, len(optimizer), len(optimizer))
                sampled_pairs.append((dataset, optimizer[indexes], comparison[indexes]))
        relative, success = estimate(sampled_pairs)
        relative_samples.append(relative)
        success_samples.append(success)
    alpha = (1.0 - float(statistics["confidence_level"])) / 2.0
    relative_ci = np.quantile(relative_samples, [alpha, 1.0 - alpha])
    success_ci = np.quantile(success_samples, [alpha, 1.0 - alpha])
    if baseline == protocol["methods"]["superiority_baseline"]:
        hypothesis = "superiority"
        passed = (
            float(relative_ci[0]) >= float(statistics["minimum_relative_reduction_ci_lower"])
            and float(success_ci[0])
            >= float(statistics["minimum_success_rate_difference_ci_lower"])
            and context_non_worse >= float(statistics["minimum_context_non_worse_fraction"])
        )
    else:
        hypothesis = "noninferiority"
        passed = (
            float(relative_ci[0])
            >= float(statistics["active_comparator_noninferiority_margin"])
            and float(success_ci[0])
            >= float(statistics["active_comparator_success_rate_margin"])
        )
    return {
        "baseline": baseline,
        "hypothesis": hypothesis,
        "relative_trial_reduction": point_relative,
        "relative_trial_reduction_ci95": [float(relative_ci[0]), float(relative_ci[1])],
        "success_rate_difference": point_success,
        "success_rate_difference_ci95": [float(success_ci[0]), float(success_ci[1])],
        "context_non_worse_fraction": context_non_worse,
        "passed": passed,
    }


def dataset_guardrail(effect: dict, statistics: dict) -> bool:
    if effect["hypothesis"] == "superiority":
        return (
            effect["relative_trial_reduction"]
            >= statistics["dataset_superiority_guardrail_minimum_point_reduction"]
            and effect["success_rate_difference"]
            >= statistics["dataset_superiority_guardrail_minimum_success_rate_difference"]
        )
    return (
        effect["relative_trial_reduction"]
        >= statistics["dataset_noninferiority_guardrail_minimum_point_reduction"]
        and effect["success_rate_difference"]
        >= statistics["dataset_noninferiority_guardrail_minimum_success_rate_difference"]
    )


def summarize(records: list[dict], protocol: dict) -> dict:
    effects = [paired_effect(records, baseline, protocol) for baseline in BASELINE_NAMES]
    dataset_effects = {}
    guardrails = {}
    present_datasets = sorted({record["dataset"] for record in records})
    for dataset in present_datasets:
        selected = [record for record in records if record["dataset"] == dataset]
        dataset_effects[dataset] = [
            paired_effect(selected, baseline, protocol)
            for baseline in BASELINE_NAMES
        ]
        guardrails[dataset] = {
            effect["baseline"]: dataset_guardrail(effect, protocol["statistics"])
            for effect in dataset_effects[dataset]
        }
    minimum_scenarios = int(protocol["statistics"]["minimum_scenarios_per_dataset"])
    enough_coverage = len(present_datasets) >= 2 and all(
        sum(record["dataset"] == dataset for record in records) >= minimum_scenarios
        for dataset in dataset_effects
    )
    passed = enough_coverage and all(effect["passed"] for effect in effects) and all(
        value for dataset in guardrails.values() for value in dataset.values()
    )
    superiority = next(
        effect for effect in effects if effect["hypothesis"] == "superiority"
    )
    noninferiority = next(
        effect for effect in effects if effect["hypothesis"] == "noninferiority"
    )
    return {
        "dataset_count": len(dataset_effects),
        "scenario_count": len(records),
        "episode_count": sum(len(record["episodes"]) for record in records),
        "all_scenarios_complete": all(record["status"] == "completed" for record in records),
        "all_initial_designs_unique": all(
            record["eligible_unique_initial_designs"] == len(record["episodes"])
            for record in records
        ),
        "paired_effects": effects,
        "dataset_effects": dataset_effects,
        "dataset_guardrails": guardrails,
        "workflow_validation": "passed",
        "experiment_reduction_vs_uninformed_search": (
            "passed-public-benchmark" if superiority["passed"] else "not-demonstrated"
        ),
        "active_comparator_noninferiority": (
            "passed-public-benchmark" if noninferiority["passed"] else "not-demonstrated"
        ),
        "experiment_reduction_claim": (
            "passed-vs-seeded-random-search" if passed else "not-demonstrated"
        ),
    }


def main() -> None:
    protocol = load_protocol()
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--episodes",
        type=int,
        default=int(protocol["episodes"]["count_per_scenario"]),
    )
    parser.add_argument("--max-scenarios", type=int, default=0)
    parser.add_argument("--dataset", choices=("all", *DATASETS), default="all")
    parser.add_argument("--bootstrap-samples", type=int)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if args.episodes < 1:
        raise SystemExit("--episodes must be positive")
    if args.bootstrap_samples is not None:
        if args.bootstrap_samples < 100:
            raise SystemExit("--bootstrap-samples must be at least 100")
        protocol["statistics"]["bootstrap_samples"] = args.bootstrap_samples

    selected_datasets = (
        DATASETS
        if args.dataset == "all"
        else {args.dataset: DATASETS[args.dataset]}
    )
    indexed_scenarios = []
    for dataset in selected_datasets:
        rows = load_rows(dataset, protocol)
        indexed_scenarios.extend(
            (dataset, context, values)
            for context, values in group_scenarios(dataset, rows, protocol)
        )
    if args.max_scenarios:
        indexed_scenarios = indexed_scenarios[: args.max_scenarios]
    records = [
        run_scenario(
            dataset,
            context,
            values,
            scenario_index=index,
            protocol=protocol,
            episode_count=args.episodes,
        )
        for index, (dataset, context, values) in enumerate(indexed_scenarios)
    ]
    payload = {
        "schema": "ingot-public-validation-v2",
        "protocol_sha256": sha256(PROTOCOL_PATH),
        "sources": protocol["sources"],
        "method": {
            "episodes": {**protocol["episodes"], "count_per_scenario": args.episodes},
            "datasets": protocol["datasets"],
            "methods": protocol["methods"],
            "statistics": protocol["statistics"],
            "claim_boundary": protocol["claim_boundary"],
            "development_disclosure": protocol["development_disclosure"],
        },
        "records": records,
        "summary": summarize(records, protocol),
    }
    rendered = json.dumps(payload, ensure_ascii=False, indent=2)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(rendered + "\n", encoding="utf-8")
        print(
            json.dumps(
                {"output": str(args.output), "summary": payload["summary"]},
                ensure_ascii=False,
                indent=2,
            )
        )
    else:
        print(rendered)


if __name__ == "__main__":
    main()
