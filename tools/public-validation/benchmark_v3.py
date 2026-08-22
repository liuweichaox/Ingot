#!/usr/bin/env python3
"""Run the protocol-frozen public physical-experiment evaluation.

Candidate outcomes remain hidden from every selection method until that method
chooses the corresponding setting. The command refuses a full run while the
protocol is marked as draft.
"""
from __future__ import annotations

import argparse
import copy
import csv
from collections import defaultdict
import hashlib
from importlib import metadata as package_metadata
import json
import math
from pathlib import Path
import platform
import re
import sys
import numpy as np

from ingot_optimizer import Campaign, DerivedFeature, Objective, Variable
from ingot_optimizer.feature_transforms import expand_inputs
from ingot_optimizer.replay import replay_optimizer_history_pool_once


ROOT = Path(__file__).resolve().parent
PROTOCOL_PATH = ROOT / "protocol-v3.json"
PRIMARY = "ingot-with-preregistered-mechanism-features"
ABLATION = "ingot-without-mechanism-features"
DATASETS = {
    "airfoil": {
        "id_field": "setting_id",
        "control_fields": (
            "frequency_hz",
            "attack_angle_deg",
            "chord_length_m",
            "free_stream_velocity_m_s",
            "displacement_thickness_m",
        ),
        "outcome_field": "sound_pressure_db",
    },
    "yacht": {
        "id_field": "setting_id",
        "control_fields": (
            "longitudinal_buoyancy_position",
            "prismatic_coefficient",
            "length_displacement_ratio",
            "beam_draught_ratio",
            "length_beam_ratio",
            "froude_number",
        ),
        "outcome_field": "residuary_resistance",
    },
}


def runtime_metadata() -> dict:
    """Record the runtime needed to interpret a retained evaluation result."""
    packages = {}
    for name in ("numpy", "torch", "botorch", "gpytorch"):
        try:
            packages[name] = package_metadata.version(name)
        except package_metadata.PackageNotFoundError:
            packages[name] = None
    return {
        "python": sys.version.split()[0],
        "implementation": platform.python_implementation(),
        "operating_system": platform.platform(),
        "machine": platform.machine(),
        "packages": packages,
    }


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def load_protocol() -> dict:
    protocol = json.loads(PROTOCOL_PATH.read_text(encoding="utf-8"))
    if protocol.get("schema") != "ingot-public-validation-protocol-v3":
        raise ValueError("unsupported public validation protocol")
    if set(protocol["sources"]) != set(DATASETS):
        raise ValueError("protocol sources and evaluation adapters disagree")
    expected_methods = {PRIMARY, ABLATION, *protocol["methods"]["baselines"]}
    if len(expected_methods) != 6:
        raise ValueError("v3 requires two Ingot variants and four distinct baselines")
    unsuccessful = int(protocol["statistics"]["unsuccessful_trial_value"])
    for name, settings in protocol["datasets"].items():
        if name not in DATASETS:
            raise ValueError(f"unsupported v3 dataset: {name}")
        initial = int(settings["initial_observations"])
        additional = int(settings["additional_trial_budget"])
        if initial < 3 or additional < 1:
            raise ValueError(f"invalid episode budget for {name}")
        if additional + 1 != unsuccessful:
            raise ValueError("all v3 datasets must use the same capped failure value")
        objective = settings["objective"]
        rule = objective["threshold_rule"]
        if rule.get("kind") != "empirical-quantile" or rule.get("method") != "linear":
            raise ValueError("v3 supports only the preregistered linear quantile rule")
        quantile = float(rule["quantile"])
        if not 0.0 < quantile < 0.5:
            raise ValueError("target quantile must be between zero and one half")
    return protocol


def evaluation_fingerprint(protocol: dict) -> str:
    """Hash every file and normalized protocol field that can affect v3 results."""
    normalized = copy.deepcopy(protocol)
    normalized["status"] = "draft"
    normalized["freeze"]["optimizer_revision"] = None
    normalized["freeze"]["protocol_revision"] = None
    normalized["freeze"]["evaluation_fingerprint"] = None
    paths = [
        ROOT / "benchmark_v3.py",
        ROOT / "data" / "airfoil-self-noise.csv",
        ROOT / "data" / "yacht-hydrodynamics.csv",
        ROOT.parents[1] / "optimizer" / "pyproject.toml",
        ROOT.parents[1] / "optimizer" / "uv.lock",
        *sorted((ROOT.parents[1] / "optimizer" / "ingot_optimizer").rglob("*.py")),
    ]
    digest = hashlib.sha256()
    digest.update(
        json.dumps(normalized, sort_keys=True, separators=(",", ":")).encode("utf-8")
    )
    for path in paths:
        digest.update(str(path.relative_to(ROOT.parents[1])).encode("utf-8"))
        digest.update(b"\0")
        digest.update(path.read_bytes())
        digest.update(b"\0")
    return digest.hexdigest()


def require_frozen(protocol: dict) -> None:
    freeze = protocol["freeze"]
    revisions = (freeze.get("optimizer_revision"), freeze.get("protocol_revision"))
    if protocol.get("status") != "frozen" or any(
        not isinstance(value, str) or re.fullmatch(r"[0-9a-f]{40}", value) is None
        for value in revisions
    ):
        raise RuntimeError(
            "protocol-v3 is not frozen; commit the optimizer and protocol before running the full evaluation"
        )
    expected_fingerprint = freeze.get("evaluation_fingerprint")
    actual_fingerprint = evaluation_fingerprint(protocol)
    if (
        not isinstance(expected_fingerprint, str)
        or re.fullmatch(r"[0-9a-f]{64}", expected_fingerprint) is None
        or expected_fingerprint != actual_fingerprint
    ):
        raise RuntimeError(
            "protocol-v3 evaluation fingerprint does not match the current algorithm, data, dependencies, and protocol"
        )


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
    metadata = DATASETS[dataset]
    expected_fields = {
        metadata["id_field"],
        *metadata["control_fields"],
        metadata["outcome_field"],
    }
    if not rows or set(rows[0]) != expected_fields:
        raise ValueError(f"unexpected {dataset} fixture columns")
    identifiers = [row[metadata["id_field"]] for row in rows]
    if len(identifiers) != len(set(identifiers)):
        raise ValueError(f"{dataset} fixture contains duplicate identifiers")
    controls = []
    outcomes = []
    for index, row in enumerate(rows):
        try:
            numeric = tuple(float(row[field]) for field in metadata["control_fields"])
            outcome = float(row[metadata["outcome_field"]])
        except ValueError as error:
            raise ValueError(f"{dataset} row {index} contains a non-numeric value") from error
        if not all(math.isfinite(value) for value in (*numeric, outcome)):
            raise ValueError(f"{dataset} row {index} contains a non-finite value")
        controls.append(numeric)
        outcomes.append(outcome)
    if len(controls) != len(set(controls)):
        raise ValueError(f"{dataset} fixture contains duplicate control settings")
    matrix = np.asarray(controls)
    declared = protocol["datasets"][dataset]["controls"]
    for column, field in enumerate(metadata["control_fields"]):
        actual = (float(matrix[:, column].min()), float(matrix[:, column].max()))
        expected = tuple(float(value) for value in declared[field])
        if not np.allclose(actual, expected, rtol=0.0, atol=1e-12):
            raise ValueError(
                f"{dataset} observed range for {field} {actual} does not match {expected}"
            )
    if min(outcomes) == max(outcomes):
        raise ValueError(f"{dataset} outcome is constant")
    return rows


def threshold_for(dataset: str, rows: list[dict[str, str]], protocol: dict) -> float:
    metadata = DATASETS[dataset]
    rule = protocol["datasets"][dataset]["objective"]["threshold_rule"]
    values = np.asarray([float(row[metadata["outcome_field"]]) for row in rows])
    return float(np.quantile(values, float(rule["quantile"]), method="linear"))


def build_campaign(
    dataset: str, rows: list[dict[str, str]], protocol: dict
) -> tuple[Campaign, float]:
    metadata = DATASETS[dataset]
    settings = protocol["datasets"][dataset]
    threshold = threshold_for(dataset, rows, protocol)
    values = np.asarray([float(row[metadata["outcome_field"]]) for row in rows])
    variables = [
        Variable(name, float(settings["controls"][name][0]), float(settings["controls"][name][1]))
        for name in metadata["control_fields"]
    ]
    objective = settings["objective"]
    campaign = Campaign(
        f"{dataset}-public-v3",
        variables,
        [
            Objective(
                objective["name"],
                objective["direction"],
                threshold=threshold,
                outcome_lower_bound=float(values.min()),
                outcome_upper_bound=float(values.max()),
                unit=objective["unit"],
            )
        ],
        context={"evaluation_dataset": dataset, "protocol": "v3"},
    )
    return campaign, threshold


def build_features(dataset: str, protocol: dict) -> list[DerivedFeature]:
    return [
        DerivedFeature(
            name=item["name"],
            operator=item["operator"],
            inputs=tuple(item["inputs"]),
            normalization_offset=float(item.get("normalization_offset", 0.0)),
            normalization_scale=float(item.get("normalization_scale", 1.0)),
            epsilon=float(item.get("epsilon", 1e-9)),
            intercept=float(item.get("intercept", 0.0)),
            coefficients=tuple(float(value) for value in item.get("coefficients", ())),
        )
        for item in protocol["datasets"][dataset]["mechanism_features"]
    ]


def build_history(dataset: str, rows: list[dict[str, str]]) -> list[dict]:
    metadata = DATASETS[dataset]
    ordered = sorted(rows, key=lambda row: row[metadata["id_field"]])
    return [
        {
            "params": {name: float(row[name]) for name in metadata["control_fields"]},
            "outcomes": {metadata["outcome_field"]: float(row[metadata["outcome_field"]])},
            "occurred_at": float(position),
            "run_id": row[metadata["id_field"]],
        }
        for position, row in enumerate(ordered, start=1)
    ]


def episode_history(
    history: list[dict], *, dataset_index: int, candidate_index: int, initial: int, protocol: dict
) -> tuple[list[dict], int]:
    seed = int(protocol["episodes"]["base_seed"]) + dataset_index * 100_000 + candidate_index
    rng = np.random.default_rng(seed)
    initial_indexes = [int(value) for value in rng.choice(len(history), initial, replace=False)]
    initial_set = set(initial_indexes)
    order = initial_indexes + [index for index in range(len(history)) if index not in initial_set]
    return [
        {**history[index], "occurred_at": float(position)}
        for position, index in enumerate(order, start=1)
    ], seed


def is_success(campaign: Campaign, history: list[dict], index: int) -> bool:
    return campaign.distance_to_spec(history[index]["outcomes"]) <= 0.0


def first_hit(campaign: Campaign, history: list[dict], selected: list[int]) -> int | None:
    return next(
        (position for position, index in enumerate(selected, start=1) if is_success(campaign, history, index)),
        None,
    )


def random_run(
    campaign: Campaign, history: list[dict], *, initial: int, budget: int, seed: int
) -> tuple[int | None, list[int]]:
    selected = list(range(initial))
    remaining = list(range(initial, len(history)))
    rng = np.random.default_rng(seed + 10_000)
    rng.shuffle(remaining)
    while remaining and len(selected) < budget:
        selected.append(remaining.pop())
        if is_success(campaign, history, selected[-1]):
            return len(selected) - initial, selected
    return None, selected


def maximin_run(
    campaign: Campaign, history: list[dict], *, initial: int, budget: int, seed: int
) -> tuple[int | None, list[int]]:
    selected = list(range(initial))
    remaining = list(range(initial, len(history)))
    points = np.asarray([campaign.to_unit(row["params"]) for row in history])
    rng = np.random.default_rng(seed + 20_000)
    while remaining and len(selected) < budget:
        candidates = points[remaining]
        observed = points[selected]
        minimum_distances = np.sqrt(
            ((candidates[:, None, :] - observed[None, :, :]) ** 2).sum(axis=2)
        ).min(axis=1)
        position = int(np.lexsort((rng.random(len(remaining)), -minimum_distances))[0])
        selected.append(remaining.pop(position))
        if is_success(campaign, history, selected[-1]):
            return len(selected) - initial, selected
    return None, selected


def response_features(points: np.ndarray, *, quadratic: bool) -> np.ndarray:
    values = np.atleast_2d(points)
    columns = [np.ones(len(values)), *[values[:, index] for index in range(values.shape[1])]]
    if quadratic:
        columns.extend(values[:, index] ** 2 for index in range(values.shape[1]))
        columns.extend(
            values[:, left] * values[:, right]
            for left in range(values.shape[1])
            for right in range(left + 1, values.shape[1])
        )
    return np.column_stack(columns)


def response_surface_run(
    campaign: Campaign,
    history: list[dict],
    *,
    initial: int,
    budget: int,
    seed: int,
    ridge: float,
    quadratic: bool,
) -> tuple[int | None, list[int]]:
    selected = list(range(initial))
    remaining = list(range(initial, len(history)))
    points = np.asarray([campaign.to_unit(row["params"]) for row in history])
    rng = np.random.default_rng(seed + (40_000 if quadratic else 30_000))
    while remaining and len(selected) < budget:
        observed = response_features(points[selected], quadratic=quadratic)
        distances = np.asarray(
            [campaign.distance_to_spec(history[index]["outcomes"]) for index in selected]
        )
        penalty = np.eye(observed.shape[1]) * ridge
        penalty[0, 0] = 0.0
        coefficients = np.linalg.solve(observed.T @ observed + penalty, observed.T @ distances)
        predictions = response_features(points[remaining], quadratic=quadratic) @ coefficients
        position = int(np.lexsort((rng.random(len(remaining)), predictions))[0])
        selected.append(remaining.pop(position))
        if is_success(campaign, history, selected[-1]):
            return len(selected) - initial, selected
    return None, selected


def selection_payload(history: list[dict], selected: list[int], initial: int) -> list[str]:
    return [str(history[index]["run_id"]) for index in selected[initial:]]


def run_dataset(
    dataset: str,
    rows: list[dict[str, str]],
    *,
    dataset_index: int,
    protocol: dict,
    episode_count: int,
) -> dict:
    campaign, threshold = build_campaign(dataset, rows, protocol)
    source_history = build_history(dataset, rows)
    settings = protocol["datasets"][dataset]
    initial = int(settings["initial_observations"])
    budget = initial + int(settings["additional_trial_budget"])
    features = build_features(dataset, protocol)
    methods = protocol["methods"]
    episodes = []
    initial_designs: set[tuple[str, ...]] = set()
    candidate_index = excluded_initial_success = duplicate_initial_designs = 0
    while len(episodes) < episode_count:
        if candidate_index > 1_000_000:
            raise RuntimeError(f"unable to generate enough eligible episodes for {dataset}")
        history, seed = episode_history(
            source_history,
            dataset_index=dataset_index,
            candidate_index=candidate_index,
            initial=initial,
            protocol=protocol,
        )
        current_candidate = candidate_index
        candidate_index += 1
        initial_ids = tuple(sorted(str(row["run_id"]) for row in history[:initial]))
        if initial_ids in initial_designs:
            duplicate_initial_designs += 1
            continue
        initial_designs.add(initial_ids)
        if first_hit(campaign, history, list(range(initial))) is not None:
            excluded_initial_success += 1
            continue

        primary = replay_optimizer_history_pool_once(
            campaign,
            history,
            budget=budget,
            initial_observation_count=initial,
            seed=seed,
            derived_features=features,
        )
        ablation = replay_optimizer_history_pool_once(
            campaign,
            history,
            budget=budget,
            initial_observation_count=initial,
            seed=seed,
        )
        random_value, random_selected = random_run(
            campaign, history, initial=initial, budget=budget, seed=seed
        )
        maximin_value, maximin_selected = maximin_run(
            campaign, history, initial=initial, budget=budget, seed=seed
        )
        linear_value, linear_selected = response_surface_run(
            campaign,
            history,
            initial=initial,
            budget=budget,
            seed=seed,
            ridge=float(methods["linear_ridge"]),
            quadratic=False,
        )
        quadratic_value, quadratic_selected = response_surface_run(
            campaign,
            history,
            initial=initial,
            budget=budget,
            seed=seed,
            ridge=float(methods["quadratic_ridge"]),
            quadratic=True,
        )
        values = {
            PRIMARY: (
                primary["additional_trials"],
                primary["selected_history_indices"],
            ),
            ABLATION: (
                ablation["additional_trials"],
                ablation["selected_history_indices"],
            ),
            "seeded-random-search": (random_value, random_selected),
            "sequential-maximin-space-filling": (maximin_value, maximin_selected),
            "regularized-linear-response-surface": (linear_value, linear_selected),
            "regularized-quadratic-response-surface": (quadratic_value, quadratic_selected),
        }
        episodes.append(
            {
                "episode": len(episodes),
                "candidate_index": current_candidate,
                "seed": seed,
                "initial_run_ids": list(initial_ids),
                "methods": {
                    name: {
                        "additional_trials": value,
                        "selected_run_ids": selection_payload(history, selected, initial),
                    }
                    for name, (value, selected) in values.items()
                },
            }
        )
    return {
        "dataset": dataset,
        "status": "completed",
        "candidate_settings": len(source_history),
        "target_threshold": threshold,
        "target_rule": settings["objective"]["threshold_rule"],
        "feasible_settings": sum(is_success(campaign, source_history, index) for index in range(len(source_history))),
        "initial_observations": initial,
        "additional_trial_budget": budget - initial,
        "unsuccessful_trial_value": int(protocol["statistics"]["unsuccessful_trial_value"]),
        "screened_initial_designs": candidate_index,
        "excluded_initial_success": excluded_initial_success,
        "duplicate_initial_designs": duplicate_initial_designs,
        "eligible_unique_initial_designs": len(episodes),
        "mechanism_features": [item["name"] for item in settings["mechanism_features"]],
        "episodes": episodes,
    }


def values(record: dict, method: str) -> np.ndarray:
    unsuccessful = int(record["unsuccessful_trial_value"])
    return np.asarray(
        [
            episode["methods"][method]["additional_trials"]
            if episode["methods"][method]["additional_trials"] is not None
            else unsuccessful
            for episode in record["episodes"]
        ],
        dtype=float,
    )


def method_summary(records: list[dict], method: str) -> dict:
    scored = np.concatenate([values(record, method) for record in records])
    unsuccessful = float(records[0]["unsuccessful_trial_value"])
    hits = scored[scored < unsuccessful]
    return {
        "success_rate": float((scored < unsuccessful).mean()),
        "mean_capped_additional_trials": float(scored.mean()),
        "median_successful_additional_trials": float(np.median(hits)) if len(hits) else None,
    }


def paired_effect(records: list[dict], comparator: str, protocol: dict) -> dict:
    pairs = [(record["dataset"], values(record, PRIMARY), values(record, comparator)) for record in records]

    def estimate(sampled: list[tuple[str, np.ndarray, np.ndarray]]) -> tuple[float, float]:
        primary = np.concatenate([item[1] for item in sampled])
        comparison = np.concatenate([item[2] for item in sampled])
        relative = float((comparison.mean() - primary.mean()) / comparison.mean())
        unsuccessful = float(protocol["statistics"]["unsuccessful_trial_value"])
        success = float((primary < unsuccessful).mean() - (comparison < unsuccessful).mean())
        return relative, success

    point_relative, point_success = estimate(pairs)
    dataset_non_worse = float(np.mean([primary.mean() <= comparison.mean() for _, primary, comparison in pairs]))
    rng = np.random.default_rng(int(protocol["statistics"]["bootstrap_seed"]))
    relative_samples = []
    success_samples = []
    for _ in range(int(protocol["statistics"]["bootstrap_samples"])):
        sampled = []
        for dataset, primary, comparison in pairs:
            indexes = rng.integers(0, len(primary), len(primary))
            sampled.append((dataset, primary[indexes], comparison[indexes]))
        relative, success = estimate(sampled)
        relative_samples.append(relative)
        success_samples.append(success)
    alpha = (1.0 - float(protocol["statistics"]["confidence_level"])) / 2.0
    relative_ci = np.quantile(relative_samples, [alpha, 1.0 - alpha])
    success_ci = np.quantile(success_samples, [alpha, 1.0 - alpha])
    statistics = protocol["statistics"]
    mechanism_comparison = comparator == ABLATION
    relative_floor = float(
        statistics[
            "mechanism_ablation_relative_reduction_ci_lower"
            if mechanism_comparison
            else "minimum_relative_reduction_ci_lower"
        ]
    )
    success_floor = float(
        statistics[
            "mechanism_ablation_success_rate_difference_ci_lower"
            if mechanism_comparison
            else "minimum_success_rate_difference_ci_lower"
        ]
    )
    passed = (
        float(relative_ci[0]) > relative_floor
        and float(success_ci[0]) >= success_floor
        and dataset_non_worse >= float(statistics["minimum_dataset_non_worse_fraction"])
    )
    return {
        "comparator": comparator,
        "relative_trial_reduction": point_relative,
        "relative_trial_reduction_ci95": [float(relative_ci[0]), float(relative_ci[1])],
        "success_rate_difference": point_success,
        "success_rate_difference_ci95": [float(success_ci[0]), float(success_ci[1])],
        "dataset_non_worse_fraction": dataset_non_worse,
        "passed": passed,
    }


def summarize(records: list[dict], protocol: dict) -> dict:
    methods = [PRIMARY, ABLATION, *protocol["methods"]["baselines"]]
    comparisons = [
        paired_effect(records, method, protocol)
        for method in [ABLATION, *protocol["methods"]["baselines"]]
    ]
    ablation = next(item for item in comparisons if item["comparator"] == ABLATION)
    baselines = [item for item in comparisons if item["comparator"] != ABLATION]
    return {
        "dataset_count": len(records),
        "episode_count": sum(len(record["episodes"]) for record in records),
        "all_datasets_complete": all(record["status"] == "completed" for record in records),
        "all_initial_designs_unique": all(
            record["eligible_unique_initial_designs"] == len(record["episodes"])
            for record in records
        ),
        "method_summaries": {method: method_summary(records, method) for method in methods},
        "dataset_method_summaries": {
            record["dataset"]: {
                method: method_summary([record], method) for method in methods
            }
            for record in records
        },
        "paired_effects": comparisons,
        "experiment_reduction_vs_all_preregistered_baselines": (
            "passed-protocol-frozen-public-evaluation"
            if all(item["passed"] for item in baselines)
            else "not-demonstrated"
        ),
        "mechanism_feature_contribution": (
            "passed-protocol-frozen-ablation" if ablation["passed"] else "not-demonstrated"
        ),
    }


def integrity_report(protocol: dict) -> dict:
    datasets = {}
    for name in DATASETS:
        rows = load_rows(name, protocol)
        features = build_features(name, protocol)
        campaign, _ = build_campaign(name, rows, protocol)
        # Feature expansion is validated without running a selection episode.
        unit_point = campaign.to_unit(build_history(name, rows)[0]["params"])
        expand_inputs(
            unit_point,
            [variable.name for variable in campaign.variables],
            [variable.low for variable in campaign.variables],
            [variable.high for variable in campaign.variables],
            features,
        )
        metadata = DATASETS[name]
        outcomes = np.asarray(
            [float(row[metadata["outcome_field"]]) for row in rows], dtype=float
        )
        threshold = threshold_for(name, rows, protocol)
        feasible = int((outcomes <= threshold).sum())
        datasets[name] = {
            "rows": len(rows),
            "fixture_sha256": protocol["sources"][name]["fixture_sha256"],
            "control_count": len(DATASETS[name]["control_fields"]),
            "mechanism_feature_count": len(features),
            "null_or_non_finite_values": 0,
            "duplicate_identifiers": 0,
            "duplicate_control_settings": 0,
            "outcome_minimum": float(outcomes.min()),
            "outcome_median": float(np.median(outcomes)),
            "outcome_maximum": float(outcomes.max()),
            "target_threshold": threshold,
            "target_settings": feasible,
            "target_setting_rate": feasible / len(rows),
        }
    try:
        require_frozen(protocol)
        full_evaluation_allowed = True
    except RuntimeError:
        full_evaluation_allowed = False
    return {
        "schema": protocol["schema"],
        "status": protocol["status"],
        "protocol_sha256": sha256(PROTOCOL_PATH),
        "candidate_evaluation_fingerprint": evaluation_fingerprint(protocol),
        "runtime": runtime_metadata(),
        "datasets": datasets,
        "full_evaluation_allowed": full_evaluation_allowed,
    }


def main() -> None:
    protocol = load_protocol()
    parser = argparse.ArgumentParser()
    parser.add_argument("--integrity-only", action="store_true")
    parser.add_argument("--episodes", type=int, default=int(protocol["episodes"]["count_per_dataset"]))
    parser.add_argument("--bootstrap-samples", type=int)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if args.integrity_only:
        payload = integrity_report(protocol)
    else:
        require_frozen(protocol)
        if args.episodes < 1:
            raise SystemExit("--episodes must be positive")
        if args.bootstrap_samples is not None:
            if args.bootstrap_samples < 100:
                raise SystemExit("--bootstrap-samples must be at least 100")
            protocol["statistics"]["bootstrap_samples"] = args.bootstrap_samples
        records = [
            run_dataset(
                name,
                load_rows(name, protocol),
                dataset_index=index,
                protocol=protocol,
                episode_count=args.episodes,
            )
            for index, name in enumerate(DATASETS)
        ]
        payload = {
            "schema": "ingot-public-validation-result-v3",
            "protocol_sha256": sha256(PROTOCOL_PATH),
            "evaluation_fingerprint": evaluation_fingerprint(protocol),
            "runtime": runtime_metadata(),
            "protocol": protocol,
            "records": records,
            "summary": summarize(records, protocol),
        }
    output = json.dumps(payload, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(output, encoding="utf-8")
    else:
        print(output, end="")


if __name__ == "__main__":
    main()
