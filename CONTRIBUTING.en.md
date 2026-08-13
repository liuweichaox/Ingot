# Contributing to Ingot

[简体中文](CONTRIBUTING.md)

Thank you for helping Ingot optimize manufacturing processes with fewer real experiments. Contributions are welcome across code, equipment adapters, algorithms, anonymized replay data, tests, documentation, and process knowledge.

Participation follows the [Code of Conduct](CODE_OF_CONDUCT.md).

## Before starting

1. Search Issues for duplicates.
2. For a substantial feature, open an Issue describing scenario, input, output, and validation.
3. Report vulnerabilities privately under the [security policy](SECURITY.md).
4. Never upload factory credentials, device addresses, customer data, or unauthorized experimental data.

## Development environment

Requirements:

- .NET SDK 10;
- Node.js 22.22+;
- uv 0.11.32 (uv manages the Python 3.11+ environment);
- Docker and Docker Compose.

```bash
dotnet restore Ingot.sln
npm --prefix apps/platform ci
npm --prefix apps/website ci
npm --prefix apps/docs-site ci
uv sync --project optimizer --extra service --group dev --locked
uv run --project optimizer --locked pytest
```

## Engineering principles

- Explain how a capability reduces experiments-to-specification or improves trust.
- Keep one formal record for acquisition, inspection, experiments, and optimization.
- Distinguish planned values, actual values, trajectories, and outcomes.
- Retain model version, uncertainty, and provenance.
- Never let an LLM generate numerical process settings.
- Keep equipment protocols outside the core domain.
- Never present simulation as real-process benefit.
- Update bilingual README, documentation, and website for public capability changes.

## Change workflow

```bash
git checkout -b feature/short-description
```

During implementation:

- reproduce a bug with a test first;
- cover success, rejection, and boundary paths;
- include migration and recovery for database changes;
- include deterministic seeds, baselines, and reproducible evaluation for algorithms;
- keep UI in process-engineering language and avoid raw JSON editors.

Before submitting:

```bash
./scripts/verify.sh
```

State checks not run when Docker or another runtime is unavailable.

## Pull requests

Include:

- problem and real scenario;
- solution and rejected alternatives;
- public contract or data-model changes;
- algorithm, equipment, and safety impact;
- tests and replay results;
- deployment or migration requirements;
- screenshots for UI changes.

Use concise imperative commits:

```text
feat(optimizer): add calibrated outcome constraints
fix(edge): preserve FX3U cycle correlation after reconnect
docs: document historical replay protocol
```

## High-value contribution areas

- FX3U/MELSEC and other real equipment adapters;
- optical-molding features and physical priors;
- Bayesian optimization, transfer, and calibration;
- anonymized real replay datasets and benchmarks;
- field usability, diagnostics, and documentation.
