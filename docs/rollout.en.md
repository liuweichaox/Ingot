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
- material, tooling, equipment, and lot context;
- historical experiment order;
- explicit specification and safety limits.

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
- unmodeled factory constraints.

Shadow mode finds missing constraints and bad mappings.

## Phase 3: controlled online loop

Proceed only after:

- replay is free from leakage;
- safety constraints and baseline are confirmed;
- interval calibration is reviewed;
- settings can be applied and captured accurately;
- engineers can review and reject;
- stop and rollback rules are rehearsed.

Start with one recommendation per run. Complete inspection before updating the model.

## Success criteria

Pre-register:

- primary: experiments to specification with replicate confirmation;
- guardrail: zero safety-limit violations;
- credibility: 95% interval coverage and calibration;
- efficiency: saved runs, material, equipment time, and inspection cost;
- usability: adoption rate and rejection reasons.

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
