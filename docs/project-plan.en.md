# Roadmap

> Document status: **rolling roadmap**. This page describes build priorities; it does not present plans as current capability.

## Product Direction

> **From run evidence to the next recipe.**

Ingot is an Open-source Process Diagnosis & Optimization system. Turn every real recipe run into optimization evidence and continuously recommend the next recipe within safety boundaries and observed coverage.

Ingot has one formal loop:

```text
R&D project → real production-run evidence → next-recipe recommendation
            → engineer adoption / modification / rejection and reason
            → actual-execution link → frozen quality outcome → next observation
```

Projects retain objectives, scope, hypotheses, evidence, and knowledge. They do not retain a second run-plan, approval, execution, or outcome state machine. Engineers always decide whether to adopt a recommendation; the system never dispatches a recipe to equipment automatically.

## Near-Term Priorities

| Priority | Objective | Completion signal |
| --- | --- | --- |
| P0 | Trustworthy run facts | Identity, units, provenance, actual values, and quality review are traceable. |
| P1 | Decision loop | Every recommendation records adoption, modification, or rejection, its reason, and its actual-execution link. |
| P2 | Outcome loop | Parameter readback and inspection facts freeze an outcome from source data exactly once. |
| P3 | Knowledge reuse | Sourced, scoped, conflict-checked knowledge can explain or constrain later recommendations. |
| P4 | Production resilience | Site isolation, backup/restore, capacity, and alerting meet the deployer's requirements. |

## Method and Effect Boundary

Response surfaces, Bayesian optimization, mechanism fusion, and other numerical methods are replaceable implementations. Offline algorithm evaluation may use frozen historical data to compare determinism, constraint compliance, and future-information isolation; it is not an engineer-facing business workflow and does not replace actual production outcomes.

The repository bundles no scenario-effect data or benefit conclusion. Deployers use their own production data to evaluate applicability, quality impact, cost, and cycle time, then decide whether to adopt recommendations.

## Long-Term Boundary

Ingot does not expand into MES, SCADA, equipment interlocks, production scheduling, a general data lake, or unattended control. Any future equipment action must be a separate safety-engineering project governed by interlocks, permissions, stopping, and recovery policy; it cannot alter the current engineer-confirmed recommendation loop.

## Related Documents

- [System design](design.en.md)
- [Analysis and optimization](optimization.en.md)
- [Production architecture](production-architecture.en.md)
