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
| Unseen-data acceptance for the frozen predecessor | `+16.74%` versus linear and `+7.25%` versus quadratic, both passed; Fullerenes was `−23.01%` versus random and `−40.65%` versus maximin, triggering subgroup guardrails | core acceptance failed; the policy was retired |
| Current-policy regression on those now-disclosed data | `+48.90%` versus random, `+21.37%` versus maximin, `+39.78%` versus linear, and `+32.91%` versus quadratic; all four gates and both dataset guardrails passed | development regression passed; fresh-data acceptance pending |
| Current-policy process-feature ablation | `−0.14%`, with no stable benefit | failed; features remain disabled without blocking raw-control selection |
| Real-factory historical replay and prospective pilot | not complete | awaiting pilot |

The present conclusion is therefore: **the frozen predecessor failed unseen-data acceptance; the exposed problem has been repaired, and the current policy passes all four core comparisons on the same now-disclosed data, but still needs acceptance on another fresh dataset.** [unseen-results.json](unseen-results.json) retains the predecessor's complete failed result, while [development/current-results.json](development/current-results.json) retains the current policy's development regression. Neither may be presented as independent effect evidence for the current policy.

## Running the checks

Run the current development regression with:

```bash
./scripts/benchmark-optimizer-development.sh
```

Run the repository's fixed public-data software regression with:

```bash
./scripts/benchmark-public-validation.sh
```

Every result must retain all baselines, dataset subgroups, confidence intervals, and failures. Once the algorithm, data, objective, budget, or gate changes, an older result becomes development evidence and cannot be relabeled as unseen-data acceptance for the current policy.

## Audit material

This directory retains older protocols, complete episode trajectories, data snapshots, provenance and SHA-256 checks, and independent summary recomputation scripts. They reproduce what a frozen method produced at the time; product users are not expected to understand the internal round numbers. See [NOTICE](NOTICE.md) for provenance and licenses, and [Scenario validation](../../docs/rollout.en.md) for staged real-project acceptance.
