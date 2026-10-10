# Ingot documentation

Ingot is an Open-source Process R&D and Optimization System. Organize recipe versions, experiment records, and run evidence to support quality analysis, process diagnosis, and recipe optimization.

Choose a path from local installation to a first recommendation and ongoing operations. Documentation is maintained with repository code. [Current status](status.en.md) describes implemented capability; the [Roadmap](project-plan.en.md) describes planned work.

The business reading order is process configuration → field integration → production runs → quality management → process diagnosis → recipe optimization. An engineer confirms the next recipe recommendation before it enters a subsequent run through existing field procedures.

## First use

1. Read [Current status](status.en.md) to understand capabilities, required data, and usage boundaries.
2. Follow [Getting started](getting-started.en.md) to start the stack, check service health, and sign in.
3. Follow the [Recipe-optimization pilot guide](pilot.en.md) to verify a published recipe, real runs, and reviewed quality results, then complete a recommendation, engineer decision, actual run, and outcome freeze.

You can start the system without a field connector. The current recommendation flow still requires admitted real-run evidence; a successful installation does not establish data readiness.

## Read by task

- **Process and R&D engineers**: the [Pilot guide](pilot.en.md) explains the procedure; [Analysis and optimization](optimization.en.md) explains why recommendations are generated or stopped; the [Glossary](glossary.en.md) defines business objects.
- **Integration engineers**: [Data integration](data-connection.en.md) covers identities, sources, and mappings; [System design](design.en.md) explains Edge and Platform responsibilities.
- **Operators**: use [Production architecture](production-architecture.en.md) to choose the reliability scope, then [Deployment](deployment.en.md) to configure, back up, and upgrade; use [Troubleshooting](troubleshooting.en.md) for failures.
- **Contributors**: prepare a development environment with [Contributing](../CONTRIBUTING.en.md); check module and record ownership against [System design](design.en.md) and the [Data model](data-model.en.md); follow the [Documentation guide](documentation-guide.en.md) for documentation changes.

## Documentation catalog

### Getting started

- [Getting started](getting-started.en.md): prerequisites, stack startup, health checks, and first login.
- [Current status](status.en.md): implemented capability, verification scope, and deployer responsibilities.
- [Recipe-optimization pilot guide](pilot.en.md): complete the first auditable recommendation and outcome chain.

### How-to guides

- [Data integration](data-connection.en.md): integration identities, points, mappings, and run admission.
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
