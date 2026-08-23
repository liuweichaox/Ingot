#!/usr/bin/env python3
"""Independently recompute the frozen v6 result from episode records."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import numpy as np


PRIMARY = "ingot-v8-with-preregistered-mechanism-features"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def scored(record: dict, method: str) -> np.ndarray:
    failure = int(record["unsuccessful_trial_value"])
    values = []
    for episode in record["episodes"]:
        result = episode["methods"][method]
        value = result["additional_trials"]
        selected = result["selected_run_ids"]
        if value is None:
            if len(selected) != int(record["additional_trial_budget"]):
                raise ValueError("failed episode does not exhaust its budget")
            values.append(failure)
        else:
            if not 1 <= int(value) <= int(record["additional_trial_budget"]):
                raise ValueError("successful episode has an invalid trial count")
            if len(selected) != int(value):
                raise ValueError("selected-run trace disagrees with trial count")
            values.append(int(value))
        if len(selected) != len(set(selected)):
            raise ValueError("method selected the same setting twice")
        if set(selected).intersection(episode["initial_run_ids"]):
            raise ValueError("method reselected an initial observation")
    return np.asarray(values, dtype=float)


def estimate(records: list[dict], comparator: str, failure: int) -> tuple[float, float]:
    primary = np.concatenate([scored(record, PRIMARY) for record in records])
    comparison = np.concatenate([scored(record, comparator) for record in records])
    relative = float((comparison.mean() - primary.mean()) / comparison.mean())
    success = float((primary < failure).mean() - (comparison < failure).mean())
    return relative, success


def recompute(payload: dict, protocol_path: Path) -> dict:
    protocol = json.loads(protocol_path.read_text(encoding="utf-8"))
    if payload["schema"] != "ingot-public-validation-result-v6":
        raise ValueError("unexpected result schema")
    if payload["protocol_sha256"] != sha256(protocol_path):
        raise ValueError("result and protocol hashes disagree")
    if payload["evaluation_fingerprint"] != protocol["freeze"][
        "evaluation_fingerprint"
    ]:
        raise ValueError("result and frozen evaluation fingerprints disagree")

    records = payload["records"]
    expected_units = {
        item["evaluation_unit"] for item in records
    }
    if len(records) != 3 or len(expected_units) != 3:
        raise ValueError("v6 must contain 3 unique evaluation units")
    if any(len(record["episodes"]) != 100 for record in records):
        raise ValueError("every v6 evaluation unit must contain 100 episodes")
    if any(
        len({tuple(episode["initial_run_ids"]) for episode in record["episodes"]})
        != 100
        for record in records
    ):
        raise ValueError("v6 initial designs are not unique within a unit")

    methods = [
        protocol["methods"]["ablation"],
        *protocol["methods"]["baselines"],
    ]
    failure = int(protocol["statistics"]["unsuccessful_trial_value"])
    samples = int(protocol["statistics"]["bootstrap_samples"])
    confidence = float(protocol["statistics"]["confidence_level"])
    alpha = (1.0 - confidence) / 2.0
    output = {}
    for comparator in methods:
        rng = np.random.default_rng(
            int(protocol["statistics"]["bootstrap_seed"])
        )
        relative, success = estimate(records, comparator, failure)
        unit_non_worse = float(
            np.mean(
                [
                    scored(record, PRIMARY).mean()
                    <= scored(record, comparator).mean()
                    for record in records
                ]
            )
        )
        relative_samples = []
        success_samples = []
        for _ in range(samples):
            sampled = []
            for record in records:
                indexes = rng.integers(0, len(record["episodes"]), len(record["episodes"]))
                sampled_record = {
                    **record,
                    "episodes": [record["episodes"][int(index)] for index in indexes],
                }
                sampled.append(sampled_record)
            sampled_relative, sampled_success = estimate(
                sampled, comparator, failure
            )
            relative_samples.append(sampled_relative)
            success_samples.append(sampled_success)
        output[comparator] = {
            "relative_trial_reduction": relative,
            "relative_trial_reduction_ci95": [
                float(value)
                for value in np.quantile(
                    relative_samples, [alpha, 1.0 - alpha]
                )
            ],
            "success_rate_difference": success,
            "success_rate_difference_ci95": [
                float(value)
                for value in np.quantile(
                    success_samples, [alpha, 1.0 - alpha]
                )
            ],
            "evaluation_unit_non_worse_fraction": unit_non_worse,
        }

    recorded = {
        item["comparator"]: item for item in payload["summary"]["paired_effects"]
    }
    for comparator, actual in output.items():
        expected = recorded[comparator]
        for key, value in actual.items():
            if not np.allclose(value, expected[key], rtol=0.0, atol=1e-12):
                raise ValueError(f"independent recomputation differs for {comparator}.{key}")
    return {
        "canonical_payload_sha256": hashlib.sha256(
            json.dumps(payload, sort_keys=True).encode()
        ).hexdigest(),
        "protocol_sha256": sha256(protocol_path),
        "evaluation_unit_count": len(records),
        "episode_count": sum(len(record["episodes"]) for record in records),
        "paired_effects": output,
        "matches_recorded_summary": True,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("result", type=Path)
    parser.add_argument(
        "--protocol",
        type=Path,
        default=Path(__file__).parents[1] / "protocol-v6.json",
    )
    args = parser.parse_args()
    payload = json.loads(args.result.read_text(encoding="utf-8"))
    print(json.dumps(recompute(payload, args.protocol), indent=2))


if __name__ == "__main__":
    main()
