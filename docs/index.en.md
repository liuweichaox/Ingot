# Ingot documentation

> **Core value**: Move process R&D from decisions without data support to decisions supported by real data, so computers can genuinely help process engineers choose what to do next using the most effective computational methods for the problem.

Ingot is an open-source process diagnosis and optimization system. It connects the production conditions, process trajectory, and inspection result of a real run into traceable evidence, helping engineers understand what happened, what deserves validation, and what is most valuable to do next.

The computer organizes evidence, compares runs, analyzes data, and proposes experiments. Process engineers frame the problem, review data and constraints, approve experiments, and make the final judgment. The system does not present observational correlation as a definitive cause or a model recommendation as a field guarantee.

## Product loop

```text
Define process → Connect equipment → Collect production data → Close the data loop → Diagnose → Optimize
      ↑                                                                                       ↓
      └──────────── validated recipes, process windows, and knowledge return to production ───┘
```

1. **Define the process**: define equipment, products, variables, units, recipe parameters, quality measures, and safety boundaries.
2. **Connect equipment**: connect controls, instruments, vision, inspection, and business systems to stable business semantics.
3. **Collect production data**: record actual conditions, stages, and trajectories for each real run.
4. **Close the data loop**: check time, missingness, provenance, and units, then link inspections to the same run.
5. **Diagnose the process**: compare like-for-like runs and form candidate causes with evidence, counterevidence, and confounding limits.
6. **Process optimization**: validate candidates through controlled experiments and choose more valuable next experiments within safety boundaries.

These are one evidence chain, not six unrelated products. Acquisition makes facts trustworthy; analysis makes them useful; experiments determine whether a candidate survives.

## Where to start

| Goal | Read |
|---|---|
| Start the system and complete the first data loop | [Installation and the first data loop](getting-started.en.md) |
| Understand stable product and architecture boundaries | [System design](design.en.md) |
| Understand how analysis, experiment, and optimization methods are selected | [Analysis and optimization methods](optimization.en.md) |
| Connect equipment, instruments, inspections, and business data | [Equipment and data connection](data-connection.en.md) |
| Review long-term stages, priorities, and acceptance gates | [Project plan](project-plan.en.md) |
| Validate whether the product helps engineers on real projects | [Real-scenario validation](rollout.en.md) |
| Deploy on a factory network | [Deployment and operations](deployment.en.md) |
| Check product and technical boundaries | [FAQ](faq.en.md) |
| Review the normative public product language | [Brand and product language](brand.en.md) |
| Review dependency principles and audit boundaries | [Open-source dependencies](open-source-dependencies.en.md) |

## Document status

- **Normative baseline**: core value, product boundaries, evidence principles, and stable architecture. Changes require explicit product evidence or an ADR.
- **Current strategy**: today's analysis methods, default experiment designs, and information architecture. These may evolve without changing the core value.
- **Rolling status**: roadmap, implementation status, known limits, and validation results. These must track project facts.

[Brand and product language](brand.en.md) is the single source of truth for public wording; technical documents must not create another core value.

## Current facts

The code covers the main business path across acquisition, cycles, context, inspections, R&D experiments, analysis, and optimization, with automated tests. Public historical replay and prospective results for real optical molding projects are not complete, so the project describes system capabilities but does not claim a measured reduction in experiments or development time.

## Public commitments

- Never hide missing actual values with planned values.
- Never silently discard runs that fail analysis admission.
- Never present correlation directly as a definitive cause.
- Never let a language model generate numerical process recipes.
- Never use simulated data to claim real process benefit.
- Preserve inputs, provenance, versions, uncertainty, and applicability for every recommendation.

中文文档从 [index.md](index.md) 开始。
