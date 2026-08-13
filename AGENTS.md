# Repository Guidelines

## Project Structure & Module Organization

Ingot is a .NET 10 monorepo with three web applications. Backend code lives under `src/`: `edge/` contains shop-floor services, `platform/` contains the central API, `agent/` contains AI investigation logic, and `shared/` holds domain models and contracts. The xUnit suite is centralized in `tests/Ingot.Core.Tests`, mirroring those areas. User-facing applications live under `apps/`: `platform` is the React/Vite product UI, while `website` and `docs-site` are Next.js applications. Deployment files are in `deploy/`, verification utilities in `scripts/`, benchmarks in `tools/`, documentation in `docs/`, and the canonical brand assets are in `apps/website/public/brand/`.

## Build, Test, and Development Commands

Use .NET SDK 10, Node.js 22.22+, uv 0.11.32, Docker, and Docker Compose.

- `dotnet restore Ingot.sln` installs .NET dependencies.
- `dotnet build Ingot.sln` builds all C# projects.
- `dotnet test tests/Ingot.Core.Tests/Ingot.Core.Tests.csproj` runs xUnit tests.
- `npm --prefix apps/platform ci` installs the product UI dependencies; replace the prefix with `apps/website` or `apps/docs-site` for those apps.
- `npm --prefix apps/platform run dev` starts the React product UI on port 3000.
- `uv sync --project optimizer --extra service --group dev --locked` creates the locked Python environment; use `uv run --project optimizer --locked ...` for all optimizer commands.
- `docker compose -f docker-compose.app.yml up -d --build` launches the application stack.
- `./scripts/verify.sh` runs the full CI gate: builds, tests, ESLint, audits, architecture checks, Compose validation, and `git diff --check`.

## Mandatory Boundary Checks

The following checks are blocking repository contracts, not optional lint rules:

- `./scripts/verify-architecture.sh` enforces dependency direction, composition-root isolation, and read-only Agent analysis tools.
- `./scripts/verify-product-scope.sh` rejects retired desktop/code-generation surfaces and legacy multi-agent product terminology.
- `./scripts/verify-product-language.sh` protects the canonical product value, claim boundaries, scenario-neutral language, and evidence-gated roadmap wording.

Run all three directly when changing architecture, product scope, public terminology, or documentation; `./scripts/verify.sh` also includes them.

## Coding Style & Naming Conventions

Follow existing files: four-space indentation in C#, two spaces in JavaScript/TypeScript/Vue, and UTF-8 text. C# uses file-scoped namespaces, nullable reference types, `PascalCase` for public symbols, and `camelCase` for locals and parameters. Frontend components use `PascalCase` filenames; variables and functions use `camelCase`. Run each app's `npm run lint` before submitting. Keep domain and agent abstractions independent of databases, model providers, and equipment protocols.

## Testing Guidelines

Name C# test classes `*Tests.cs` and JavaScript tests `*.test.mjs`. Add success, rejection, and authorization-boundary coverage for new behavior; reproduce bugs with a failing test first. Run the focused suite while iterating and `./scripts/verify.sh` before opening a PR.

## Commit & Pull Request Guidelines

History uses concise imperative summaries, sometimes Conventional Commit scopes such as `refactor:`, `feat(central):`, or `perf(chat):`. Keep commits focused. PRs must explain the problem, contract or data-model changes, security impact, verification results, and deployment/configuration needs. Link relevant issues and include screenshots for UI changes. Update both Chinese and `.en.md` documentation when public behavior or terminology changes. Report vulnerabilities through `SECURITY.md`, not public issues.
