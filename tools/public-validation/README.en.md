# Optimizer experiment-efficiency validation

This directory does not test whether an algorithm looks sophisticated. It answers one product question:

> When a small observed history has not reached specification, can Ingot find a passing setting with fewer additional experiments?

Public data can test selection efficiency only within fixed experimental pools; it cannot replace a real factory pilot. Every method receives the same initial history and may observe an outcome only after selecting that experiment.

## What this must establish

In plain language, the three model-based choices are:

- **Stable-trend method**: continue along a direction already visible in the data, such as quality improving consistently as temperature rises. The technical name is a linear response surface.
- **Turning-point and interaction method**: allow an intermediate best region and allow two parameters to work only in combination. The technical name is a quadratic response surface.
- **More flexible small-data method**: when simple patterns are insufficient, use predicted outcomes and uncertainty to choose the next experiment. This implementation uses a Gaussian process.

Acceptance has two independent claims:

1. **Core experiment selection**: use clearly fewer additional experiments than random search and maximin; avoid material regression when a stable trend or a turning point/parameter interaction fits the problem; and allow a Gaussian process to add value when simple patterns are insufficient.
2. **Mechanism-feature contribution**: pass only when preregistered physical features improve prediction from visible observations and reduce experiments in a paired ablation. A failure disables those features without blocking core selection on raw controls.

The core capability is not “always use Bayesian optimization.” It is **select a method supported by current evidence**. Engineers provide objectives, adjustable parameters, cost, and safety boundaries; they do not choose algorithm names.

## Decision design

Every replay episode starts from the same unique seeded observations and compares:

- the Ingot method-selection policy frozen at that time;
- seeded random search;
- sequential maximin space filling;
- a stable-trend method (regularized linear response surface);
- a turning-point and interaction method (regularized quadratic response surface);
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
| Previous frozen policy versus random search | `+49.24%`, 95% CI `[+44.45%, +53.68%]`; non-worse on all three datasets | passed |
| Previous frozen policy versus maximin | `+62.96%`, 95% CI `[+59.93%, +65.85%]`; non-worse on all three datasets | passed |
| Previous frozen policy versus stable-trend method (linear response surface) | `+12.02%` aggregate, but `−32.08%` on Alkox and `−68.36%` on P3HT | subgroup guardrail failed |
| Previous frozen policy versus turning-point and interaction method (quadratic response surface) | `0%`; trajectories match on all three datasets | passed, with no added value |
| Current successor | old frozen fingerprint is invalid; these inspected data support development regression only | awaiting new unseen-data acceptance |
| Real-factory historical replay and prospective pilot | not complete | awaiting pilot |

The supported conclusion is therefore: **the previous frozen policy reliably beat model-free selection, but on Alkox and P3HT it treated problems that could be solved by following a stable trend as unnecessarily complex and therefore ran extra experiments; core acceptance failed.** Technically, it collapsed to the quadratic response surface and failed to recognize the more effective linear surface. The successor now follows the stable trend first and changes course only after repeated evidence supports a turning point or parameter interaction. Because the algorithm changed, these three datasets serve only as development regression and do not count as independent effect evidence for the successor. The next preregistered unseen-data test decides successor effectiveness. [acceptance-results.json](acceptance-results.json) retains the complete frozen result; [acceptance-selection.en.md](acceptance-selection.en.md) and [acceptance-protocol.json](acceptance-protocol.json) retain the selection and fixed rules.

## Running the checks

Run the current development regression with:

```bash
./scripts/benchmark-optimizer-development.sh
```

Check the retained frozen data and protocol integrity, and confirm that the current algorithm no longer matches the old fingerprint, with:

```bash
./scripts/verify-optimizer-acceptance.sh
```

`./scripts/benchmark-optimizer-acceptance.sh` reproduces only the frozen formal result. The algorithm has now changed, so it intentionally refuses to run. Use `./scripts/benchmark-optimizer-development.sh` for successor regression on inspected data.

Every result must retain all baselines, dataset subgroups, confidence intervals, and failures. Once the algorithm, data, objective, budget, or gate changes, an older result becomes development evidence and cannot be relabeled as unseen-data acceptance for the current policy.

## Audit material

This directory retains older protocols, complete episode trajectories, data snapshots, provenance and SHA-256 checks, and independent summary recomputation scripts. They reproduce what a frozen method produced at the time; product users are not expected to understand the internal round numbers. See [NOTICE](NOTICE.md) for provenance and licenses, and [Scenario validation](../../docs/rollout.en.md) for staged real-project acceptance.
