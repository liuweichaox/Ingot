# Contributing to Ingot

[简体中文](CONTRIBUTING.md)

Ingot accepts contributions across generic code, algorithms, authorized public replay data, tests, documentation, and process knowledge. Site-specific equipment adapters are maintained outside this repository. Every contribution must respect the evidence and safety claim boundaries and must not overstate run efficiency.

Participation follows the [Code of Conduct](CODE_OF_CONDUCT.md).

Unless a contributor explicitly states otherwise in writing, accepted contributions to this repository are provided under the [Apache License 2.0](LICENSE).

## Before starting

Report reproducible defects as Issues; ask usage questions in Discussions; report security findings privately under [SECURITY.md](SECURITY.md). A bug report should include the version or commit, environment, reproduction steps, expected and actual behavior, and sanitized logs.

1. Search Issues for duplicates.
2. For a substantial feature, open an Issue describing scenario, input, output, and validation.
3. Report vulnerabilities privately under the [security policy](SECURITY.md).
4. Never upload factory credentials, device addresses, customer data, or unauthorized run data.

## Development environment

Choose the smallest environment for your change. A documentation or single-app change does not require installing the entire toolchain first.

| Change area | Minimum tools | Local setup |
|---|---|---|
| Markdown, README | Python 3; Bash when running the public-language gate | `python3 scripts/verify-documentation-style.py` |
| `apps/platform` | Node.js 22.22+, npm | `npm --prefix apps/platform ci` |
| `apps/website` | Node.js 22.22+, npm | `npm --prefix apps/website ci` |
| `apps/docs-site` | Node.js 22.22+, npm | `npm --prefix apps/docs-site ci` |
| .NET services | .NET SDK 10 | `dotnet restore Ingot.sln` |
| Optimizer | Python 3.12, uv 0.12.5 | `uv sync --project optimizer --extra service --group dev --locked` |

Run these commands from the repository root. When changing application code, lint the affected app; build the website, docs site, or workbench when changing it to confirm that its pages generate.

The full pre-submission gate requires .NET SDK 10, Node.js 22.22+, Python 3.12, uv 0.12.5, Docker Compose, and Bash. It verifies all three frontends, .NET, Optimizer, deployment manifests, and architecture and product boundaries. With these tools available, run `./scripts/verify.sh` from the repository root. The gate remains required even when local iteration uses only one app.

## Engineering principles

- Explain how a capability reduces runs-to-specification or improves trust.
- Keep one real-run-centered formal record for acquisition, inspection, and optimization.
- Distinguish planned values, actual values, trajectories, and outcomes.
- Retain model version, uncertainty, and provenance.
- Never let an LLM generate numerical process settings.
- Keep equipment protocols, point mappings, and vendor adapters outside the core repository; the core retains only the event contract, validation, diagnostics, and generic business capabilities.
- Never present simulation as real-process benefit.
- Update bilingual README, documentation, and website for public capability changes.

## Core and site-adapter boundary

The core repository maintains only capabilities that are generic across sites: the domain model, event contract, evidence assembly, analysis, optimization, permissions, audit, diagnostics, and tests. Device protocols, point mappings, vendor SDKs, unit conversions, and site-specific retry policies belong to site adapters, which deployers write and maintain for their environment, with AI assistance where useful.

This is a deliberate trade-off: field connectivity is a long-tail problem. Making it generic turns the core into an unmaintainable driver collection, lowers code quality, and still fails to cover real sites. With adapters outside the repository, the core is responsible only for stable contracts.

Therefore:

- Do not submit device protocol drivers, point-mapping tables, or vendor SDK wrappers under `src/`;
- Adapters depend only on the public event contract, validators, and contract tests, never on Platform internals;
- The core owns the event contract, error codes, diagnostics, and validation; reusable adapter templates and contract tests are separate tools and contain no protocol implementations;
- Code that serves only one factory, one device, or one protocol is an adapter and belongs outside this repository;
- Do not submit speculative modules without a user-facing path, tests, and a real deployment scenario; implement them when the need appears;
- A capability is generic when it needs no code change, or only a configuration change, for another site, device, or product.

## Change workflow

```bash
git checkout -b codex/short-description
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
- use XML documentation when a public C# type or member needs explanation and docstrings for public Python modules or functions;
- every public C# interface must have a type-level `summary`, and public Optimizer types and entry-point functions must have docstrings; submission checks reject missing documentation;
- every source, test, script, and build file whose format supports comments must include at least one responsibility, constraint, or failure-boundary explanation; pure data formats such as JSON and historical migrations protected by committed checksums are exempt. New migrations must still document their purpose in their first commit.

Before submitting:

```bash
./scripts/verify.sh
```

State checks not run when Docker or another runtime is unavailable.

## Verification and documentation

Install only the frontend applications needed for your change; full verification checks all three. On Windows, use PowerShell for .NET, npm, and uv commands, and WSL or a Bash environment with the required tools for Bash gates. Do not share `node_modules` between Windows and WSL.

During iteration, run relevant tests and application lint, then run the full gate. When changing architecture, product scope, public terminology, or documentation, also run these blocking checks directly:

```bash
./scripts/verify-architecture.sh
./scripts/verify-product-scope.sh
./scripts/verify-product-language.sh
python3 scripts/verify-documentation-style.py
```

Keep Chinese documents and their `.en.md` counterparts aligned in heading structure and behavioral descriptions. Documentation sources live in `docs/`; the site generates pages from those files. When adding a page, update `apps/docs-site/lib/public-docs.json` and the navigation so both languages remain reachable. Commands should state the working directory, prerequisites, success criteria, and failure handling. Describe capabilities using inspectable code and tests, and explicitly label planned work.

## Pull requests

Keep each PR focused on an independently reviewable result. Lead with the problem and final behavior, then provide evidence. State checks that were not run and known limitations explicitly.

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

- adapter contract tests, event-mapping examples, and adapter authoring guidance;
- process-specific features and physical priors for real manufacturing scenarios;
- Bayesian optimization, transfer, and calibration;
- replay datasets and benchmarks with explicit authorization, licensing, and provenance;
- field usability, diagnostics, and documentation.
