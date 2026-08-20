# Ingot documentation

> **Core value**: Move process R&D from decisions without data support to decisions supported by real data, so computers can genuinely help process engineers choose what to do next using the most effective computational methods for the problem.

Ingot is an open-source process diagnosis and optimization system. It connects the production conditions, process trajectory, and inspection result of a real run into traceable evidence, helping engineers understand what happened, what deserves validation, and what is most valuable to do next.

The computer organizes evidence, compares runs, analyzes data, and proposes experiments. Process engineers frame the problem, review data and constraints, approve experiments, and make the final judgment. The system does not present observational correlation as a definitive cause or a model recommendation as a field guarantee.

## Product loop

```text
Define process → Connect equipment → Collect production data → Close the data loop → Diagnose → Optimize
      ↑                                                                                       ↓
      └──────────── validated process specifications, operating regions, and knowledge return to production ───┘
```

1. **Define the process**: define equipment, products, variables, units, control parameters, quality measures, and safety boundaries.
2. **Connect equipment**: connect controls, instruments, vision, inspection, and business systems to stable business semantics.
3. **Collect production data**: record actual conditions, stages, and trajectories for each real run.
4. **Close the data loop**: check time, missingness, provenance, and units, then link inspections to the same run.
5. **Diagnose the process**: compare like-for-like runs and form candidate causes with evidence, counterevidence, and confounding limits.
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
| Contributor or integrator | [System design](design.en.md) | [Production architecture](production-architecture.en.md) |
| Public-content maintainer | [Brand guide](brand.en.md) | [Document status](#document-status) |

## Document status

- **Normative baseline**: core value, product boundaries, evidence principles, and stable architecture. Changes require explicit product evidence or an ADR.
- **Current strategy**: today's analysis methods, default experiment designs, and information architecture. These may evolve without changing the core value.
- **Rolling status**: roadmap, implementation status, known limits, and validation results. These must track project facts.

[Brand guide](brand.en.md) is the single source of truth for public wording; technical documents must not create another core value.

## Current facts

The code covers the main business path across acquisition, process executions, context, inspections, R&D experiments, analysis, and optimization, with automated tests. The project has completed internal end-to-end validation of import, run reconstruction, inspection linkage, and R&D observations using controlled, non-public production history. Formal leakage-free replay and prospective validation remain incomplete, so the project describes system capabilities but does not claim a measured reduction in experiments or development time.

Current repository implementation snapshot:

- Platform business records, Agent run snapshots, and evaluation evidence share the PostgreSQL recovery boundary.
- Edge uses a durable local outbox and at-least-once delivery; Platform fails closed on site, event identity, schema, applied-configuration version, content hash, and quality flags.
- Major Platform business workflows now live in Application, while PostgreSQL stores, external clients, and evidence assemblers remain in Infrastructure. A small set of operational adapters for Edge registration/diagnostics, identity, and runtime metrics still reaches Infrastructure from API; these are remaining convergence points, not templates for new business controllers.
- Mechanism-knowledge sources, claims, reviews, conflicts, lifecycle, hard bounds, soft ranking, frozen snapshots, and usage traceability are integrated. Model-assisted semantic drafts, Bayesian priors/mechanism features/residual fusion, and paired long-horizon calibration remain incomplete.
- The repository provides logical backup/restore, monitoring configuration, limited failure drills, and production-acceptance artifact validation. The default Compose deployment remains a single-instance reference topology, not an HA production cell, and does not include PITR, object storage, or controlled equipment writes.

Real production data, project and equipment identities, process parameters, quality distributions, and derived results are controlled factory evidence. They do not enter the public repository or public reports. Public materials provide only protocols, schemas, synthetic examples, acceptance methods, and conclusion boundaries; internal validation on real data is not public independent reproduction.

The current sequence is to prove that the evidence apparatus is reproducible and leakage-free through real historical replay, then establish value through prospective shadow and controlled online validation. Agent protocols and an open specification do not bypass those evidence stages.

## Public commitments

- Never hide missing actual values with planned values.
- Never silently discard runs that fail analysis admission.
- Never present correlation directly as a definitive cause.
- Never let a language model generate numerical process specifications.
- Never let an agent bypass provenance, permission, approval, or equipment-safety boundaries.
- Never use simulated data to claim real process benefit.
- Preserve inputs, provenance, versions, uncertainty, and applicability for every recommendation.

中文文档从 [index.md](index.md) 开始。
