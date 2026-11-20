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

Run the commands in the table from the repository root. After installing dependencies, run `npm run dev` for a local preview, `npm run lint`, and `npm test` from the affected app directory. From the repository root, use `npm --prefix <app-directory>`.

- The workbench needs a running Platform API. Vite defaults to `http://localhost:3000` and proxies to `INGOT_PLATFORM_API_TARGET`, which defaults to `http://127.0.0.1:8000`. `npm test` runs node:test and Vitest. Use `npm run build` to check the production build. See [`apps/platform/nginx.conf`](apps/platform/nginx.conf) for production proxy rules.
- Website copy is maintained in `apps/website/app/IngotSite.tsx` and rendered through separate Chinese and English entry points. Follow the [brand guide](docs/brand.en.md) for wording and assets. The website and docs site `npm test` commands build and check rendered pages and document exports respectively. See the [deployment instructions](deploy/README.md) for public hosting.

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

Add runtime dependencies for a specific engineering need. Evaluate maintenance, license compatibility, and on-premises deployment. Pin versions or use a controlled version range, verify licensing for the actual features, versions, transitive dependencies, and release method, and retain required notices in images and releases. Keep proprietary cloud services optional for acquisition, record keeping, inspection, and numerical analysis.

Review dependency use together with manifest and lockfile changes. Include dependencies in build, vulnerability, license, and supply-chain checks. For major upgrades, run the full gate and relevant replay checks. Remove unused dependencies and address license or maintenance changes before release. Find exact versions in the corresponding commit's `.csproj` files, `Directory.Build.props`, `package-lock.json`, `optimizer/uv.lock`, Dockerfiles, and Compose manifests. Use the generated SBOM or dependency inventory for releases.

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
- express intent through domain names, explicit types, focused modules, readable control flow, and specific test names. Improve the code when a comment would only restate its behavior;
- use brief comments when external constraints, design tradeoffs, compatibility requirements, or failure consequences would be easy to miss. Keep the language consistent within a file. Default new C# business and contract explanations to Chinese and Optimizer docstrings to English. Preserve protocol names, configuration keys, and identifiers as written;
- provide type-level XML `summary` for public C# interfaces and the Application and Agent public classes covered by the gate. Provide docstrings for public Optimizer types and entry-point functions. Document the contract callers need;
- document the purpose of new migrations. Preserve committed migrations protected by checksums.

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

- Maintain each rule in one primary location and link to it from other pages. Document the shell, working directory, prerequisites, expected result, and failure-diagnosis steps for commands. Use recognizable placeholders in examples. Use read-only diagnostics by default and explain data protection for backup, recovery, and upgrade procedures.
- Verify current behavior against the implementation and runtime configuration. Check services, ports, and profiles in `docker-compose.app.yml`, defaults in `.env.example`, host configuration, and validators, API and access rules in endpoint and authorization code, and storage in SQL migrations. [Current status](docs/status.en.md) summarizes delivered capabilities. Distinguish implementation, validation left to deployers, and conditional targets. Limit conclusions to the tests or site acceptance actually performed. Support claims about site suitability, safety, and benefit with authorized evidence.
- Keep headings, steps, and behavioral boundaries aligned in `docs/<slug>.md` and `docs/<slug>.en.md`. Use relative links within the same language. Register public pages in `apps/docs-site/lib/public-docs.json`, assign each to one group in `apps/docs-site/lib/docs.ts`, and update the catalogue and index assertions in `apps/docs-site/tests/export.test.mjs`. Markdown supplies the page content. `prepare-content.mjs` generates the search index and copies brand assets.
- When renaming or merging pages, preserve published entry points or provide a same-language static redirect. Check heading anchors. Keep redirect pages outside the content catalogue, navigation, search, and sitemap. Use `npm --prefix apps/docs-site test` to check bilingual exports, links, the catalogue, and search indexing. Check language switching, navigation at desktop and narrow widths, task entry points, and source and feedback links. The build generates `out/` and `public/search-index.json`.

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
