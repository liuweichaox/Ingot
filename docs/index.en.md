# Ingot documentation

> **Core value**: Turn every real run into comparable, testable engineering evidence so process engineers can avoid unproductive experiments and reach target process conditions faster.

Ingot is an open-source process diagnosis and optimization system. It connects the production conditions, process trajectory, and inspection result of a real run into traceable evidence, helping engineers determine what happened, what warrants validation, and which next action has the highest expected value.

The computer organizes evidence, compares runs, analyzes data, and proposes experiments. Process engineers frame the problem, review data and constraints, approve experiments, and make the final judgment. Public entry points lead with the user outcome; full method boundaries and retained failures are disclosed in the validation documents.

## Product loop

```text
Process configuration → Field integration → Production runs → Quality management → Diagnosis → Process R&D
        ↑                                                                                              ↓
        └──────── validated process specifications, operating regions, and knowledge return to production ────────┘
```

1. **Process configuration**: define products, variables, units, control parameters, quality measures, and safety boundaries.
2. **Field integration**: connect controls, instruments, vision, inspection, and business systems to stable business semantics.
3. **Production runs**: record actual conditions, stages, and trajectories for each real run.
4. **Quality management**: enter, link, and independently review quality and safety outcomes for the same run.
5. **Process diagnosis**: review data trust, compare like-for-like runs, and form candidate causes with evidence, counterevidence, and confounding limits.
6. **Process R&D**: validate candidates through controlled experiments and choose more valuable next experiments within safety boundaries.

These are one evidence chain, not six unrelated products. Acquisition makes facts trustworthy; analysis makes them useful; experiments determine whether a candidate survives.

## Where to start

Choose an entry by role, then follow the links into method detail. You do not need to read every document in order.

| Role or goal | Start with | Continue with |
|---|---|---|
| First evaluation or trial | [Getting started](getting-started.en.md) | [FAQ](faq.en.md) |
| Process or data engineer | [Data integration](data-connection.en.md) | [Analysis and optimization](optimization.en.md) |
| Process knowledge builder | [Mechanism knowledge design](mechanism-knowledge.en.md) | [Analysis and optimization](optimization.en.md) |
| Platform operations or security | [Production architecture](production-architecture.en.md) | [Deployment](deployment.en.md) |
| Project or validation lead | [Scenario validation](rollout.en.md) | [Roadmap](project-plan.en.md) |
| Algorithm or validation contributor | [Public-data experiment-efficiency validation](https://github.com/liuweichaox/Ingot/blob/main/tools/public-validation/README.en.md) | [Analysis and optimization](optimization.en.md) |
| Contributor or integrator | [System design](design.en.md) | [Production architecture](production-architecture.en.md) |
| Public-content maintainer | [Brand guide](brand.en.md) | [Document status](#document-status) |

## Document status

- **Normative baseline**: core value, product boundaries, evidence principles, and stable architecture. Changes require explicit product evidence or an ADR.
- **Current strategy**: today's analysis methods, default experiment designs, and information architecture. These may evolve without changing the core value.
- **Rolling status**: roadmap, implementation status, known limits, and validation results. These must track project facts.

[Brand guide](brand.en.md) is the single source of truth for public wording; technical documents must not create another core value.

## Current facts

The code covers the main business path across acquisition, run reconstruction, comparable-run analysis, quality linkage, candidate causes, controlled experiments, and next-experiment recommendations, with automated tests. Public physical-experiment replay confirms that the system finds passing settings faster than random trial and error. The unresolved question is whether, with limited data, it can reliably decide to continue along the observed trend or explore a possible best region and parameter interaction. Exact algorithm names, figures, failed subgroups, and confidence intervals are maintained only in [Public-data experiment-efficiency validation](https://github.com/liuweichaox/Ingot/blob/main/tools/public-validation/README.en.md).

Current repository implementation snapshot:

- Platform business records, Agent run snapshots, and evaluation evidence share the PostgreSQL recovery boundary.
- Edge uses a durable local outbox and at-least-once delivery; Platform fails closed on site, event identity, schema, applied-configuration version, content hash, and quality flags.
- Major Platform business workflows now live in Application, while PostgreSQL stores, external clients, and evidence assemblers remain in Infrastructure. A small set of operational adapters for Edge registration/diagnostics, identity, and runtime metrics still reaches Infrastructure from API; these are remaining convergence points, not templates for new business controllers.
- Mechanism-knowledge sources, claims, reviews, conflicts, lifecycle, hard bounds, soft ranking, frozen snapshots, and usage traceability are integrated. Model-assisted semantic drafts, Bayesian priors/mechanism features/residual fusion, and paired long-horizon calibration remain incomplete.
- The repository provides logical backup/restore, monitoring configuration, limited failure drills, and production-acceptance artifact validation. The default Compose deployment remains a single-instance reference topology, not an HA production cell, and does not include PITR, object storage, or controlled equipment writes.

Real production data, project and equipment identities, process parameters, quality distributions, and derived results are controlled factory evidence. They do not enter the public repository or public reports. Public materials provide protocols, schemas, synthetic examples, an explicitly licensed and checksum-verified public-data benchmark, acceptance methods, and conclusion boundaries. The public benchmark independently reproduces software and method behavior, but it does not replace internal validation on real data.

The current sequence is to prove that the evidence apparatus is reproducible and leakage-free through real historical replay, then establish value through prospective shadow and controlled online validation. Agent protocols and an open specification do not bypass those evidence stages.

## Trustworthy-delivery principles

- Planned and actual values remain separate, and missingness stays visible.
- Every run retains its analysis-admission or rejection reason.
- Observational results remain candidate relationships until a controlled experiment decides them.
- A constrained numerical service produces numerical process recommendations; language models organize evidence.
- Platform enforces provenance, permission, approval, and equipment-safety boundaries for agents.
- Every recommendation preserves inputs, provenance, versions, uncertainty, and applicability.

中文文档从 [index.md](index.md) 开始。
