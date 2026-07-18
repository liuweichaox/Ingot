# Contributing to Ingot

[简体中文](CONTRIBUTING.md)

Thank you for contributing code, tests, documentation, or design feedback. Every change must preserve trusted production facts, read-only Chat, governed desktop Agent permissions, auditable connectors, and synchronized Chinese and English documentation.

## Engineering principles

- `Ingot.Domain`, `Ingot.Application`, and `Ingot.Agent` remain independent of databases, model providers, and equipment protocols.
- Connector specifications and packages represent source-specific behavior; vendor SDKs do not enter the platform core.
- Chat models handle question interpretation and response composition. Deterministic code owns fact retrieval, data validation, authorization, run limits, and evidence verification.
- Chat fact tools remain read-only. Agent artifact tools write only Actor-isolated connector workspaces and platform-controlled versioned records.
- Do not add arbitrary SQL execution, scripts, shell access, open networking, model-selected file paths, or equipment control.
- Public contracts do not retain duplicate fields, implicit aliases, or silent compatibility behavior.

## Local environment

Requirements:

- .NET SDK 10
- Node.js 22.13 or later
- Docker and Docker Compose

Install dependencies:

```bash
dotnet restore Ingot.sln
npm --prefix src/Ingot.Central.Web ci
npm --prefix desktop ci
npm --prefix site ci
npm --prefix docs-site ci
```

See [Getting started](docs/tutorial-getting-started.en.md) for local services.

## Change requirements

### Chat, Agent, and models

- The core depends only on `IModelClient`, `IAnalysisTool`, and other `Ingot.Agent` contracts.
- Model output must be typed and deterministically validated before execution.
- Every tool defines a stable name, version, JSON Schema, access type, timeout, cancellation, and result limit.
- Every key number and conclusion must resolve to a real `EvidenceRef`.
- `/api/v1/chat/*` accepts only read-only fact queries. `/api/v1/agent/*` accepts connector code generation only with the fixed desktop-client identifier.
- Chat and Agent runs, histories, events, and permissions remain isolated by product surface and Actor.

### Connectors

- Connectors emit normalized `ProductionEvent[]` and never leak source protocol models into core contracts.
- Specifications include protocol, endpoint, authentication, data contract, sampling policy, and acceptance criteria.
- Source packages include a Dockerfile, `connector.manifest.json`, and tests.
- Generated code is built and tested only through fixed Connector Builder entries in a network-disabled Docker child container with read-only source; host builds, arbitrary commands, and runtime image pulls are prohibited.
- Agent and Builder do not connect to live data sources. Tests use only fixed workspace fixtures and simulated input.
- Successful tests stop at the packaging approval gate. An operator reviews and explicitly approves the current revision; the platform emits a verifiable ZIP but does not deploy, start, or schedule connectors.

### APIs and storage

- API inputs are type- and authorization-validated at the controller boundary.
- PostgreSQL stores central facts. SQLite WAL stores shop-floor events, outbox records, Agent runs, and artifacts.
- Database changes include initialization or migration, concurrency semantics, failure handling, and integration tests.
- Logs, metrics, and traces exclude secrets, full prompts, and sensitive tool arguments.

### Web and documentation

- Central Web exposes only Chat and obtains its availability and read-only tools from `/api/v1/chat/capabilities`. Code generation appears only in Ingot Agent Desktop.
- Any chart capability first defines a `ChartSpec` type allowlist, deterministic validation, renderer, and tests; never execute model-generated front-end code.
- The product website describes implemented capabilities only and labels sample facts explicitly.
- Public capability, configuration, API, or terminology changes update the README, bilingual `docs/`, product website, and docs site together.

## Testing

Run the complete gate before submitting:

```bash
./scripts/verify.sh
```

It covers .NET build and tests; Central Web build, tests, lint, and production dependency audit; product and docs site static builds, links, lint, and audits; architecture dependencies; shell syntax; Compose configuration; and diff formatting.

New behavior includes success, rejection, and authorization-boundary tests. Bug fixes first add a test that reproduces the defect.

## Pull requests

1. Fork the repository and branch from the latest `main`.
2. Keep the change focused and exclude unrelated formatting or refactors.
3. Update implementation, tests, and affected bilingual documentation.
4. Run `./scripts/verify.sh`.
5. Open a pull request that states:
   - problem and objective;
   - public contract or data-model changes;
   - security and authorization impact;
   - verification results;
   - deployment or configuration requirements.

Use [GitHub Issues](https://github.com/liuweichaox/Ingot/issues) for regular defects and feature requests. Do not open a public issue for a vulnerability; follow [SECURITY.md](SECURITY.md).
