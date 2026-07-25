# Contributing to Ingot

[简体中文](CONTRIBUTING.md)

Thank you for contributing code, tests, documentation, or design feedback. Ingot combines experimental data, real-time process data, physical mechanisms, and expert knowledge to help process engineers design experiments, discover relationships, optimize parameters, and validate process windows faster.

Every change should advance that goal and keep the Chinese and English documentation synchronized.

## Engineering principles

- Organize objectives, experiments, process data, analysis findings, validation results, and reusable knowledge around process R&D projects.
- Keep source data, calculations, model versions, and supporting evidence traceable and reproducible.
- Treat data acquisition as a native foundation. Edge drivers implement common protocols; versioned device contracts and adapters handle equipment-specific differences.
- Keep `Ingot.Domain`, `Ingot.Edge.Application`, and `Ingot.Agent` independent of databases, model providers, and equipment protocols.
- Connect physical mechanisms, statistical analysis, optimization algorithms, and expert rules through stable contracts instead of hard-coding one method into the core workflow.
- AI interprets tasks, organizes analysis, and explains results. Deterministic code owns validation, authorization, execution, operating limits, and evidence links.
- Process engineers retain approval authority for experiments, parameter releases, and process-window confirmation.
- Public contracts use explicit types, units, time semantics, quality states, and versions rather than implicit compatibility.

## Local environment

Requirements:

- .NET SDK 10
- Node.js 22.13 or later
- Docker and Docker Compose

Install dependencies:

```bash
dotnet restore Ingot.sln
npm --prefix src/platform/Ingot.Platform.Web ci
npm --prefix apps/website ci
npm --prefix apps/docs-site ci
```

See the repository-root `AGENTS.md` and each application's README for startup commands.

## Change requirements

### Process R&D domain

- Every new capability explains which stage of the process R&D loop it serves, along with its inputs, outputs, and validation method.
- Use stable identifiers to relate experiments, samples, equipment, materials, recipes, process parameters, quality results, and analysis runs.
- Key numbers and conclusions link to real data, calculation methods, run versions, and applicability conditions.
- A process window expresses objectives, constraints, feasible regions, confidence, and validation status together.

### Data acquisition and equipment adaptation

- Common protocol drivers own connection, reading, writing, subscription, reconnection, and error classification.
- Device contracts own address maps, data types, units, scaling, timestamps, quality states, and semantic names.
- The acquisition runtime owns buffering, deduplication, ordering, checkpoint recovery, observability, and safety boundaries.
- New protocol or device support includes contract examples, simulation tests, recovery tests, and site-acceptance criteria.
- Protocol- and vendor-specific models remain inside adapter boundaries and do not enter the process R&D domain model.

### AI, algorithms, and mechanisms

- The core depends on `IModelClient`, `IAnalysisTool`, and other stable interfaces.
- Model output is typed and deterministically validated before execution.
- Analysis tools declare versions, input and output structures, unit requirements, timeouts, cancellation, resource limits, and provenance.
- New algorithms include baseline comparisons, applicability assumptions, failure conditions, and reproducible experiments.
- AI recommendations distinguish data facts, model inferences, and hypotheses that still require validation.

### APIs and storage

- API inputs are type-, tenant-, and authorization-validated at the boundary.
- Database changes include migrations, indexes, concurrency rules, failure handling, and integration tests.
- Experimental data, real-time process data, analysis artifacts, and knowledge entries retain source, time, version, and quality information.
- Logs, metrics, and traces exclude secrets, full prompts, and sensitive business data.

### Web and documentation

- Organize pages around process-engineering workflows, not internal services or database structures.
- Critical tasks have a clear entry point, current state, next action, completion feedback, and recovery path.
- The website, README, and public documentation explain project value, core capabilities, operating model, and rollout validation; they are not API manuals.
- Changes to public capabilities, terminology, or core workflows update the README, bilingual `docs/`, product website, and docs site together.
- The product website describes implemented capabilities or clearly labels validation-stage capabilities, and it identifies sample data explicitly.

## Testing

Run the complete gate before submitting:

```bash
./scripts/verify.sh
```

It covers:

- .NET builds, unit tests, and integration tests;
- Platform Web builds, tests, lint, and production dependency audits;
- product and documentation site static builds, link tests, lint, and production dependency audits;
- architecture dependencies, shell syntax, Compose configuration, and diff formatting.

New behavior includes success, rejection, and authorization-boundary tests. Bug fixes first add a test that reproduces the defect.

## Pull requests

1. Fork the repository and branch from the latest `main`.
2. Keep the change focused and exclude unrelated formatting or refactors.
3. Update implementation, tests, and affected bilingual documentation.
4. Run `./scripts/verify.sh`.
5. Open a pull request that states:
   - the problem and objective;
   - the effect on the process R&D workflow;
   - public contract or data-model changes;
   - security and authorization impact;
   - verification results;
   - deployment or configuration requirements.

Use [GitHub Issues](https://github.com/liuweichaox/Ingot/issues) for regular defects and feature requests. Do not open a public issue for a vulnerability; follow [SECURITY.md](SECURITY.md).
