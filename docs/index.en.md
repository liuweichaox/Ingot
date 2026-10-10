# Ingot documentation

Ingot is an Open-source Process R&D and Optimization System. Organize recipe versions, experiment records, and run evidence to support quality analysis, process diagnosis, and recipe optimization.

Each page covers one subject. The [Brand guide](brand.en.md) governs wording, and [Current status](status.en.md) governs implemented capability.

## Read by task

| Objective | Read |
|---|---|
| Run it locally | [Getting started](getting-started.en.md) |
| See what works today | [Current status](status.en.md) |
| Reach the first next recipe recommendation | [Recipe-optimization pilot guide](pilot.en.md) |
| Understand the stable boundary | [System design](design.en.md) |
| Connect field data | [Data integration](data-connection.en.md) |
| Prepare production | [Production architecture](production-architecture.en.md), then [Deployment](deployment.en.md) |
| Contribute | [Contributing](https://github.com/liuweichaox/Ingot/blob/main/CONTRIBUTING.en.md) |

The working order is process configuration → field integration → production runs → quality management → process diagnosis → recipe optimization. The next recipe recommendation returns to the published recipe version, and the engineer decides whether it starts the next run.

## Documentation catalog

### Start

- [Getting started](getting-started.en.md): run the local stack
- [Current status](status.en.md): implemented capability and deployer responsibility
- [Recipe-optimization pilot guide](pilot.en.md): from real runs to the first recommendation

### System

- [System design](design.en.md): business model and component responsibilities
- [Analysis and optimization](optimization.en.md): admission, observed coverage, and numerical methods
- [Mechanism knowledge design](mechanism-knowledge.en.md): how knowledge constrains a recommendation

### Deployment and integration

- [Data integration](data-connection.en.md): identity, points, mapping, and data admission
- [Deployment](deployment.en.md): configuration, health, backup, and upgrade
- [Production architecture](production-architecture.en.md): failure model and production admission

### Reference

- [Roadmap](project-plan.en.md): build priorities
- [Scenario evaluation boundary](rollout.en.md): conclusion limits for a deployer's own evaluation
- [Frequently asked questions](faq.en.md)
- [Glossary](glossary.en.md)
- [Brand guide](brand.en.md)
- [Open-source dependencies](open-source-dependencies.en.md)

中文文档从 [index.md](index.md) 开始。
