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

| Object | Current conclusion |
|---|---|
| Current strategy | Has not passed an independent unseen-data acceptance |
| Core experiment-selection capability | Historical freezes contain successful comparisons, but no evaluation passed every preregistered baseline and dataset guardrail together |
| Mechanism or process-feature contribution | Stable incremental contribution has not been demonstrated |
| Real-factory historical replay and prospective pilot | Incomplete |

It is therefore not supported to say that Ingot has proved a general experiment-count advantage over random, space-filling, or classical response-surface methods. The supported statement is that the repository retains multiple frozen protocols, successful comparisons, and failures in full; the current implementation addresses known failures; and another preregistered dataset group that did not participate in development decides promotion. See [Current status](../../docs/status.en.md) for the capability and production boundary.

## Retained frozen evidence

| Frozen evaluation | Main observation | Decision |
|---|---|---|
| 450 paired Alkox, P3HT, and HPLC replays | `49.24%` fewer trials than random and `62.96%` fewer than maximin, but substantial Alkox and P3HT regression versus the linear response surface and identical trajectories to the quadratic surface | Core acceptance failed; see [acceptance-results.json](acceptance-results.json) |
| 400 paired unseen reaction-process replays | `85.5%` aggregate success and better aggregate results than the tested linear and quadratic surfaces, but preregistered gates against random and maximin failed | Core selection was not demonstrated; see [unseen-results.json](unseen-results.json) |
| 400 paired OER composition-plate replays | Strong aggregate results against several baselines, but the quadratic-response-surface guardrail did not pass on every composition plate | Advantage over every baseline was not demonstrated; see [latest-results-v7.json](latest-results-v7.json) |
| Preregistered feature ablations | Neither process interactions nor composition descriptors showed stable incremental contribution | Mechanism-feature contribution was not demonstrated |

These results belong to different frozen candidates and data selections. They cannot be combined into one aggregate win rate, and a successful comparison from one freeze cannot be transferred to the current strategy. After an algorithm, dataset, objective, budget, or gate changes, the old result serves only development regression and failure audit.

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
