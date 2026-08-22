# Contributing to Ingot

[简体中文](CONTRIBUTING.md)

Ingot accepts contributions across code, equipment adapters, algorithms, authorized public replay data, tests, documentation, and process knowledge. Every contribution preserves the experiment-efficiency, evidence, and safety claim boundaries.

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

Code and comment style:

- follow the root `.editorconfig`: use four spaces for C# and Python, two spaces for JavaScript, JSX, JSON, YAML, and shell, plus UTF-8, LF, and a final newline;
- keep comment language consistent within each file: default new C# business and contract code to Chinese explanations, retain English docstrings in the Optimizer Python module, and preserve protocol names, configuration keys, and code identifiers verbatim;
- use comments for business constraints, design rationale, failure boundaries, or non-obvious invariants; do not narrate each line or retain dead commented-out code;
- use XML documentation when a public C# type or member needs explanation and docstrings for public Python modules or functions; punctuate complete sentences consistently.
- every public C# interface must have a type-level `summary`, and public Optimizer types and entry-point functions must have docstrings; submission checks reject missing documentation.
- every source, test, script, and build file whose format supports comments must include at least one responsibility, constraint, or failure-boundary explanation; pure data formats such as JSON and historical migrations protected by committed checksums are exempt. New migrations must still document their purpose in their first commit.

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

- additional real equipment protocol adapters;
- process-specific features and physical priors for real manufacturing scenarios;
- Bayesian optimization, transfer, and calibration;
- replay datasets and benchmarks with explicit authorization, licensing, and provenance;
- field usability, diagnostics, and documentation.
