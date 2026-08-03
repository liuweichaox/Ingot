# Real-world Validation

## Objective

The question is not whether data can be collected:

> Under the same safety boundaries and candidate space, does Ingot reach specification earlier than the historical sequence, with credible uncertainty?

The primary metric is **valid experiments required to reach specification**. Also record safety violations, failed experiments, calendar time, and cost.

## Phase 1: run-by-run historical replay

Choose a completed process-development campaign. Optical-lens molding can be the first validation scenario, but it is not the method boundary. Include:

- planned settings and actual run settings for every run;
- complete process traces or versioned process features;
- relevant quality and safety inspections;
- stable equipment identity plus available tooling revision, tooling cycle count, material lot, calibration, and maintenance context;
- a report of context-field missingness, sample coverage, and factor overlap;
- historical experiment order;
- explicit specification and safety limits.

### Context assessment rules

Equipment, tooling, and material lots begin as run-provenance fields and candidate blocking factors. Assess every context factor in this order:

1. Count samples by level and summarize missingness, quality, and time distributions.
2. Check overlap within the same product, recipe, and main-control range.
3. Estimate effects and variance contributions with matched comparisons, variance components, or mixed-effects models.
4. Schedule blocked and crossed experiments across equipment, tooling, or lots for stable associations.
5. Add repeatedly supported factors to diagnosis models, optimization features, or process applicability scopes.

Analysis reports label conclusions as stable association, confounded association, or insufficient evidence and state the grouping, replication, and randomized order needed for the next identifiable experiment.

### Replay protocol

At step `t`, the optimizer sees only the first `t` historical runs. It recommends from a predeclared candidate pool, then the corresponding historical outcome is revealed. Future data must remain hidden.

Compare:

- historical runs to specification;
- Ingot runs to first specification;
- runs to specification plus replicate confirmation;
- cumulative safety violations;
- interval coverage;
- distance from each recommendation to the nearest historical candidate.

Narrow historical coverage evaluates selection inside that pool, not continuous-space optimization.

## Phase 2: shadow recommendations

Generate recommendations on a new campaign without changing the actual experimental sequence. Record:

- optimizer recommendation;
- engineer choice;
- predictions and outcomes for both;
- reason for rejecting a recommendation;
- unmodeled factory constraints;
- the immutable context snapshot for the recommendation and actual run.

Shadow mode finds missing constraints and bad mappings.

## Phase 3: controlled online loop

Proceed only after:

- replay is free from leakage;
- safety constraints and baseline are confirmed;
- interval calibration is reviewed;
- settings can be applied and captured accurately;
- identified context factors have a blocking, randomization, and replication plan;
- engineers can review and reject;
- stop and rollback rules are rehearsed.

Start with one recommendation per run. Complete inspection before updating the model.

## Success criteria

Pre-register:

- primary: experiments to specification with replicate confirmation;
- guardrail: zero safety-limit violations;
- credibility: 95% interval coverage and calibration;
- efficiency: saved runs, material, equipment time, and inspection cost;
- usability: adoption rate and rejection reasons;
- context evidence: engineer review of stable-association, confounded-association, and insufficient-evidence conclusions.

Include consecutive eligible campaigns rather than selecting only successes.

## Publishing results

A public report should include:

- scope and anonymization;
- variables, objectives, and constraints;
- candidate pool or continuous range;
- exclusion rules;
- model and seeds;
- historical and optimized sequences;
- every failure and safety event;
- limits on generalization.

Until this evidence exists, public copy says that the system can recommend experiments—not that it has already reduced them by a specific percentage.
