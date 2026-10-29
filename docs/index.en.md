# Ingot documentation

Ingot is an Open-source Process R&D and Optimization System. Organize recipe versions, experiment records, and run evidence to support quality analysis, process analysis (including process diagnosis), and recipe optimization.

Choose a path from local installation to a first recommendation and ongoing operations. Documentation is maintained with repository code. [Current status](status.en.md) describes implemented capability; the [Roadmap](project-plan.en.md) describes planned work.

The business reading order is Field Operations → Quality Inspection → Process Analysis → Recipe Optimization. Process materials and mechanism knowledge belong to Process Insight, while Ingot Chat is a cross-module verification and investigation entry rather than a production step.

Configuration and integration prepares process semantics and automated acquisition for that daily path. The user-facing boundary is a factory or production site, which also bounds data and authorization; the system identifies it internally with `SiteId`. An engineer confirms the next recipe adjustment before it enters a subsequent run through existing field procedures.

## First use

1. Read [Current status](status.en.md) to understand capabilities, required data, and usage boundaries.
2. Follow [Getting started](getting-started.en.md) to start the stack, check service health, and sign in.
3. Follow the [Recipe-optimization pilot guide](pilot.en.md) to verify a published recipe, real runs, and reviewed quality results, then complete a recommendation, engineer decision, actual run, and outcome frozen from that run.

You can start the system without a field connector. When a reliable field source exists, prefer automated acquisition, semantic mapping, buffering, and replay. The current recommendation flow still requires admitted real-run evidence; a successful installation does not establish data readiness.

## Read by task

| Role | Reading path |
|---|---|
| Process and R&D engineers | [Pilot guide](pilot.en.md) → [Analysis and optimization](optimization.en.md) → [Glossary](glossary.en.md) |
| Integration engineers | [Data integration](data-connection.en.md) → [System design](design.en.md) |
| Operators | [Production architecture](production-architecture.en.md) → [Deployment](deployment.en.md) → [Troubleshooting](troubleshooting.en.md) |
| Contributors | [Contributing](../CONTRIBUTING.en.md) → [System design](design.en.md), [Data model](data-model.en.md) → [Documentation guide](documentation-guide.en.md) |

## Documentation catalog

### Getting started

- [Getting started](getting-started.en.md): prerequisites, stack startup, health checks, and first login.
- [Current status](status.en.md): implemented capability, verification scope, and deployer responsibilities.
- [Recipe-optimization pilot guide](pilot.en.md): complete the first auditable recommendation and outcome chain.

### How-to guides

- [Data integration](data-connection.en.md): integration identities, points, mappings, and run admission.
- [Semiconductor wafer etch acquisition demo](semiconductor-demo.en.md): inspect process data, recipe versions, and a 300-second run path with an independent simulator.
- [Deployment](deployment.en.md): configuration, model services, observability, backup, and upgrades.
- [Troubleshooting](troubleshooting.en.md): diagnose startup, authorization, data, and recommendation problems by symptom.
- [Scenario evaluation boundary](rollout.en.md): assess suitability and effects using your own evidence.

### Concepts

- [System design](design.en.md): component responsibilities, business model, dependency direction, and architecture constraints.
- [Analysis and optimization](optimization.en.md): admission, safety boundaries, coverage envelope, and method selection.
- [Mechanism knowledge design](mechanism-knowledge.en.md): source review, applicability, constraints, and retrieval.
- [Production architecture](production-architecture.en.md): deployment levels, failure model, and reliability acceptance.

### Reference

- [Data model overview](data-model.en.md): table responsibilities, append-only records, migrations, and authorization boundaries.
- [Glossary](glossary.en.md): consistent definitions of runs, recipes, evidence, and recommendations.
- [FAQ](faq.en.md): suitability, data requirements, and system boundaries.
- [Roadmap](project-plan.en.md): priorities and completion criteria.
- [Brand guide](brand.en.md): positioning, visual assets, and public wording.
- [Open-source dependencies](open-source-dependencies.en.md): components, licenses, and adoption requirements.
- [Documentation guide](documentation-guide.en.md): page responsibilities, bilingual maintenance, and verification.

## Support and feedback

For usage problems, include reproduction steps, version, service status, and redacted errors in [Issues](https://github.com/liuweichaox/Ingot/issues). Use [Discussions](https://github.com/liuweichaox/Ingot/discussions) for design discussions. Report vulnerabilities privately through the [Security policy](../SECURITY.md).

Do not submit `.env`, credentials, real production data, or identifiable field materials. Documentation corrections can be submitted as a Pull Request with the corresponding Chinese page updated.

[简体中文文档](index.md)
