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
| Current-policy regression on disclosed data | `+53.66%` versus random, `+54.42%` versus maximin, `+50.52%` versus linear, and `+7.00%` versus quadratic; every dataset guardrail passed | development regression passed |
| Generic composition-feature ablation | `−0.10%`, with no stable benefit | failed; current policy rejects admission |
| Unseen-data acceptance for the current policy | `+16.74%` versus linear and `+7.25%` versus quadratic, both passed; Fullerenes was `−23.01%` versus random and `−40.65%` versus maximin, triggering subgroup guardrails | core acceptance failed |
| Preregistered process-feature ablation | `+0.20%`, with a 95% CI lower bound of `0` | failed; features remain disabled |
| Real-factory historical replay and prospective pilot | not complete | awaiting pilot |

The present conclusion is therefore: **the current policy passes the linear- and quadratic-response-surface guardrails on unseen data but does not yet reliably beat model-free space filling; core experiment-reduction acceptance failed.** The Fullerenes failure narrows successor work to whether insufficient model evidence should trigger a maximin fallback. [unseen-results.json](unseen-results.json) retains the complete result; it is development regression for any successor and cannot be rerun into a passing result.

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
