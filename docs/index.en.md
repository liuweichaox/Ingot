# Ingot documentation

> **Core value**: Organize recipe versions, experiment records, and run evidence to support quality analysis, process diagnosis, and recipe optimization.

**Product category:** Open-source Process R&D and Optimization System. The documentation covers recipe versions and experiment records, quality analysis, process diagnosis, recipe optimization, system design, and deployment.

The documentation can be read by task rather than in sequence.

## Choose documentation by task

| Objective | Read first | Continue with |
|---|---|---|
| Evaluate the product workflow | [Getting started](getting-started.en.md) | [Current status](status.en.md) |
| Deploy Ingot | [Getting started](getting-started.en.md) | [Current status](status.en.md) |
| Validate the current production-evidence workflow | [Recipe-optimization pilot guide](pilot.en.md) | [Data integration](data-connection.en.md) |
| Prepare production | [Production architecture](production-architecture.en.md) | [Deployment](deployment.en.md) |
| Review how the system forms a recommendation | [System design](design.en.md) | [Analysis and optimization](optimization.en.md) |
| Build process knowledge | [Mechanism knowledge design](mechanism-knowledge.en.md) | [Analysis and optimization](optimization.en.md) |
| Review effect claims | [Current status](status.en.md) | [Analysis and optimization](optimization.en.md) |
| Contribute code | [Contributing](https://github.com/liuweichaox/Ingot/blob/main/CONTRIBUTING.en.md) | [System design](design.en.md) |

## Current production-evidence workflow

```text
Process configuration → Field integration → Production runs → Quality management → Process diagnosis → Recipe optimization
           ↑                                                                                         ↓
           └──────── Validated recipe versions and mechanism knowledge return to production ─────────┘
```

1. **Process configuration** tells the system which variables, units, quality rules, and safety boundaries matter.
2. **Field integration** can turn control, instrument, and business data into consistent process fields.
3. **Production runs** record actual conditions, stages, trajectories, and manufacturing context.
4. **Quality management** links inspections uniquely and subjects them to independent review.
5. **Process diagnosis** checks whether the data are reliable, compares runs, and finds differences worth testing.
6. **Recipe optimization** aggregates real recipe runs, recommends the next recipe within safety boundaries and observed coverage, and records the engineer's adoption, modification, or rejection together with later actual-run outcomes.

This order means “what must exist before the next step.” Navigation may follow day-to-day role needs, but analysis must still begin with trustworthy data.

## Current maturity

The production-run recommendation workflow is implemented. The Web app manages recipe-version parameters through process configuration; each run is an experiment, and run records with quality outcomes provide factual inputs for analysis and optimization. Recipe recommendations freeze their brief by site and recipe, and engineer decisions and later outcomes are appended. See [Current status](status.en.md) and the [Roadmap](project-plan.en.md) for capability, validation, and planned-work boundaries.

See [Current status](status.en.md) for the complete boundary.

## Documentation map

### Development and operation

- [Getting started](getting-started.en.md): complete local stack and software checks
- [Recipe-optimization pilot guide](pilot.en.md): move from real runs to the first next-recipe recommendation
- [Data integration](data-connection.en.md): identity, protocols, points, mappings, and data admission
- [Deployment](deployment.en.md): configuration, health, monitoring, backup, and upgrade
- [Frequently asked questions](faq.en.md): concise answers on product boundaries and method choice

### System and algorithm design

- [System design](design.en.md): stable business model and component responsibilities
- [Analysis and optimization](optimization.en.md): how real recipe runs become observations and support the next recipe recommendation
- [Mechanism knowledge design](mechanism-knowledge.en.md): sources, claims, review, fusion, and applicability

### Validation and production engineering

- [Current status](status.en.md): what works today and what remains unproven
- [Production architecture](production-architecture.en.md): what a production deployment must satisfy

### Project governance

- [Roadmap](project-plan.en.md): long-term direction, priorities, and promotion gates
- [Brand guide](brand.en.md): product positioning and public wording boundaries
- [Open-source dependencies](open-source-dependencies.en.md): introduction and audit principles

## Reading status labels

- **Current operating guide**: procedures that can be executed with the current release.
- **Current facts**: what is implemented, validated, and still limited.
- **Architecture or specification baseline**: boundaries the product must continue to respect.
- **Staged implementation or target design**: includes future work and is not current behavior.
- **Rolling roadmap**: build order changes as evidence changes.

Numeric facts are not copied across entry documents. [Brand guide](brand.en.md) governs the product category, [Current status](status.en.md) governs maturity, and the public validation record governs method figures.

中文文档从 [index.md](index.md) 开始。
