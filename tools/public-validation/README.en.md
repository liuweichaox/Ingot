# Optimizer experiment-efficiency validation

This directory does not test whether an algorithm looks sophisticated. It answers one product question:

> When a small observed history has not reached specification, can Ingot find a passing setting with fewer additional experiments?

Public data can test selection efficiency only within fixed experimental pools; it cannot replace a real factory pilot. Every method receives the same initial history and may observe an outcome only after selecting that experiment.

## What this must establish

Acceptance has two independent claims:

1. **Core experiment selection**: use clearly fewer additional experiments than random search and maximin; avoid material regression when a linear or quadratic response surface fits the problem; and allow a Gaussian process to add value when a simple response surface is insufficient.
2. **Mechanism-feature contribution**: pass only when preregistered physical features improve prediction from visible observations and reduce experiments in a paired ablation. A failure disables those features without blocking core selection on raw controls.

The core capability is not “always use Bayesian optimization.” It is **select a method supported by current evidence**. A linear response surface, quadratic response surface, or Bayesian optimizer may be the method actually used in a campaign.

## Decision design

Every replay episode starts from the same unique seeded observations and compares:

- the current Ingot method-selection policy;
- seeded random search;
- sequential maximin space filling;
- a regularized linear response surface;
- a regularized quadratic response surface;
- the same Ingot policy with mechanism features disabled.

The primary endpoint is capped additional experiments to specification. Results also report success rate, 95% confidence intervals, and every dataset subgroup. Random and maximin use superiority tests. Linear and quadratic response surfaces use noninferiority guardrails because they are already effective experiment-reduction methods. An aggregate result cannot hide a material dataset regression.

Validation has only three user-facing states:

| State | Purpose | What it establishes |
|---|---|---|
| Development regression | improve the method and prevent regression on inspected data | the implementation behaves as intended, not new effect evidence |
| Unseen-data acceptance | freeze the method, data selection, and rules before reading outcomes | whether the current policy passes public-data acceptance |
| Real pilot | use a customer's own history and prospective experiments | whether the method reduces real experiments for that process |

Numbers embedded in older filenames identify past internal experiment rounds. They are not product versions or maturity levels that a user must understand. They remain as audit records; the current conclusion is defined only by the acceptance states on this page.

## Current state

| Item | Result | State |
|---|---:|---|
| Current policy versus random search | `+49.24%`, 95% CI `[+44.45%, +53.68%]`; non-worse on all three datasets | passed |
| Current policy versus maximin | `+62.96%`, 95% CI `[+59.93%, +65.85%]`; non-worse on all three datasets | passed |
| Current policy versus linear response surface | `+12.02%` aggregate, but `−32.08%` on Alkox and `−68.36%` on P3HT | subgroup guardrail failed |
| Current policy versus quadratic response surface | `0%`; trajectories match on all three datasets | passed, with no added value |
| Real-factory historical replay and prospective pilot | not complete | awaiting pilot |

The present conclusion is therefore: **the current policy reliably beats model-free selection, but on these three fresh datasets it collapses to the quadratic response surface and fails to recognize the more effective linear surface on Alkox and P3HT; core acceptance failed.** This rejects the current routing rule, not sequential experiment design itself. [acceptance-results.json](acceptance-results.json) retains the complete frozen result; [acceptance-selection.en.md](acceptance-selection.en.md) and [acceptance-protocol.json](acceptance-protocol.json) retain the selection and fixed rules.

## Running the checks

Run the current development regression with:

```bash
./scripts/benchmark-optimizer-development.sh
```

Check the current frozen acceptance data and protocol integrity with:

```bash
./scripts/verify-optimizer-acceptance.sh
```

`./scripts/benchmark-optimizer-acceptance.sh` reproduces only the frozen formal result and intentionally refuses to run after an algorithm change. Use `./scripts/benchmark-optimizer-development.sh` for successor regression on inspected data.

Every result must retain all baselines, dataset subgroups, confidence intervals, and failures. Once the algorithm, data, objective, budget, or gate changes, an older result becomes development evidence and cannot be relabeled as unseen-data acceptance for the current policy.

## Audit material

This directory retains older protocols, complete episode trajectories, data snapshots, provenance and SHA-256 checks, and independent summary recomputation scripts. They reproduce what a frozen method produced at the time; product users are not expected to understand the internal round numbers. See [NOTICE](NOTICE.md) for provenance and licenses, and [Scenario validation](../../docs/rollout.en.md) for staged real-project acceptance.
