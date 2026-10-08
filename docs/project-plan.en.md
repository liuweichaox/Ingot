# Roadmap

> Document status: **rolling roadmap**. This page describes build priorities; it does not present plans as current capability.

## Product Direction

> **From process data to evidence-based R&D decisions.**

Ingot is an Open-source Process R&D and Optimization System. Organize R&D projects, experiment records, and run evidence to support quality analysis, process diagnosis, and recipe optimization.

The currently implemented recommendation loop still uses production-run evidence:

```text
R&D project → real production-run evidence → next-recipe recommendation
            → engineer adoption / modification / rejection and reason
            → actual-execution link → frozen quality outcome → next observation
```

Projects retain objectives, scope, hypotheses, evidence, and knowledge. They do not retain a second run-plan, approval, execution, or outcome state machine. Engineers always decide whether to adopt a recommendation; the system never dispatches a recipe to equipment automatically.

Experiment settings reuse process configuration: the process data dictionary defines parameters, and recipe versions retain all parameter settings. R&D work references these configurations instead of establishing parallel variable definitions. Each run is an experiment; actual parameters, process trajectories, and quality outcomes reuse run and quality records.

Current recommendations require admitted runs, actual settings, and quality outcomes. Future work follows this evidence path to improve data-admission explanations, engineer decisions, and outcome tracking.

## Near-Term Priorities

| Priority | Objective | Completion signal |
| --- | --- | --- |
| P0 | Run-evidence completeness | Reuse all recipe-version parameters and quality configuration; clearly show actual values, provenance, missing data, and quality-review status. |
| P1 | Recommendation admission explanations | Show why runs qualify as optimization observations; insufficient samples and out-of-scope inputs have explicit rejection reasons traceable to run and quality records. |
| P2 | Decision and outcome interface | Connect existing recommendation, decision, and outcome APIs so engineers can review, adopt, modify, or reject recommendations, link later runs, and view materialized outcomes in the Web app. |
| P3 | Knowledge reuse | Sourced, scoped, conflict-checked knowledge can explain or constrain later recommendations. |
| P4 | Deployment resilience | Local installation, backup/restore, capacity, and alerting can be accepted independently; connector failures do not block querying or reviewing existing runs and quality outcomes. |

## Method and Effect Boundary

Response surfaces, Bayesian optimization, mechanism fusion, and other numerical methods are replaceable implementations. Offline algorithm evaluation may use frozen historical data to compare determinism, constraint compliance, and future-information isolation; it is not an engineer-facing business workflow and does not replace actual production outcomes.

The repository bundles no scenario-effect data or benefit conclusion. Deployers use their own production data to evaluate applicability, quality impact, cost, and cycle time, then decide whether to adopt recommendations.

## Long-Term Boundary

Ingot focuses on R&D records, analysis, and optimization rather than production execution, enterprise resource planning, full quality-compliance or laboratory management, equipment interlocks, production scheduling, a general data lake, or unattended control. Any future equipment action must be a separate safety-engineering project governed by interlocks, permissions, stopping, and recovery policy; it cannot alter the engineer-confirmed recommendation loop.

## Related Documents

- [System design](design.en.md)
- [Analysis and optimization](optimization.en.md)
- [Production architecture](production-architecture.en.md)
