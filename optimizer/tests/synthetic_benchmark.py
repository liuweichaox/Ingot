"""Compare optimizer and random on deterministic synthetic surfaces, not plant data.

Run: uv run --project optimizer --locked python optimizer/tests/synthetic_benchmark.py
The structured perturbation scenario is reproducible and shared between methods;
it is not an estimate of measurement noise or industrial performance.
"""

from __future__ import annotations

import argparse
from collections import Counter
import json
import math

from ingot_optimizer import Campaign, Objective, Variable
from ingot_optimizer.replay import SyntheticTruthResult, replay_synthetic


def synthetic_cases():
    """Keep simulated response surfaces explicit and independent of policy."""
    def spec(name, threshold=0.01):
        return Campaign(
            name, [Variable("x", 0.0, 1.0)],
            [Objective("loss", "le", threshold=threshold)],
        )

    def smooth(params):
        return SyntheticTruthResult(outcomes={"loss": (params["x"] - 0.75) ** 2})

    def wavy(params):
        x = params["x"]
        return SyntheticTruthResult(outcomes={"loss": (x - 0.75) ** 2 + 0.02 * (1 - math.cos(12 * x))})

    def perturbed(params):
        x = params["x"]
        # Deterministic input-dependent perturbation gives both methods the same
        # observation for the same point; it is not independent repeated noise.
        return SyntheticTruthResult(outcomes={"loss": (x - 0.75) ** 2 + 0.005 * math.sin(93 * x)})

    return [(spec("smooth"), smooth), (spec("wavy", 0.035), wavy), (spec("perturbed"), perturbed)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--seeds", type=int, default=3)
    parser.add_argument("--budget", type=int, default=5)
    args = parser.parse_args()
    results = []
    for campaign, truth in synthetic_cases():
        result = replay_synthetic(campaign, truth, budget=args.budget, n_seeds=args.seeds)
        results.append({
            "case": campaign.name,
            "evidence_kind": result["evidence_kind"],
            "budget": args.budget,
            "seeds": args.seeds,
            "shared_initial_observations": result["initial_observation_count"],
            "initial_success_count": result["initial_success_count"],
            "initial_observations_by_seed": [
                trace["steps"][:result["initial_observation_count"]]
                for trace in result["optimizer_traces"]
            ],
            "target_loss_at_most": campaign.objectives[0].threshold,
            "coverage_mode": result["coverage_mode"],
            "runs_to_specification_note": (
                "first hit position, not total executed runs; all initial observations "
                "are executed before comparison. Means and medians include only successes."
            ),
            "optimizer": result["optimizer"],
            "random": result["random"],
            "optimizer_outcomes": result["raw_optimizer"],
            "random_outcomes": result["raw_random"],
            "optimizer_stop_reason_counts": dict(Counter(trace["stop_reason"] for trace in result["optimizer_traces"])),
            "random_stop_reason_counts": dict(Counter(trace["stop_reason"] for trace in result["random_traces"])),
        })
    print(json.dumps(results, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
