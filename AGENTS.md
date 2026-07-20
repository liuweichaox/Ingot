# Repository Guidelines

## Project Structure & Module Organization

Ingot is a .NET 10 monorepo with three web applications. Backend code lives under `src/`: `edge/` contains shop-floor services, `platform/` contains the central API and Vue UI, `agent/` contains read-only investigation logic, and `shared/` holds domain models and contracts. The xUnit suite is centralized in `tests/Ingot.Core.Tests`, mirroring those areas. `apps/website` and `apps/docs-site` are Next.js applications; `src/platform/Ingot.Platform.Web` is Vue/Vite. Deployment files are in `deploy/`, verification utilities in `scripts/`, benchmarks in `tools/`, documentation in `docs/`, and brand assets in `images/`.

## Build, Test, and Development Commands

Use .NET SDK 10, Node.js 22.13+, Docker, and Docker Compose.

- `dotnet restore Ingot.sln` installs .NET dependencies.
- `dotnet build Ingot.sln` builds all C# projects.
- `dotnet test tests/Ingot.Core.Tests/Ingot.Core.Tests.csproj` runs xUnit tests.
- `npm --prefix src/platform/Ingot.Platform.Web ci` installs UI dependencies; replace the prefix with `apps/website` or `apps/docs-site` for those apps.
- `npm --prefix src/platform/Ingot.Platform.Web run dev` starts the Vue UI on port 3000.
- `docker compose -f docker-compose.app.yml up -d --build` launches the application stack.
- `./scripts/verify.sh` runs the full CI gate: builds, tests, ESLint, audits, architecture checks, Compose validation, and `git diff --check`.

## Coding Style & Naming Conventions

Follow existing files: four-space indentation in C#, two spaces in JavaScript/TypeScript/Vue, and UTF-8 text. C# uses file-scoped namespaces, nullable reference types, `PascalCase` for public symbols, and `camelCase` for locals and parameters. Frontend components use `PascalCase` filenames; variables and functions use `camelCase`. Run each app's `npm run lint` before submitting. Keep domain and agent abstractions independent of databases, model providers, and equipment protocols.

## Testing Guidelines

Name C# test classes `*Tests.cs` and JavaScript tests `*.test.mjs`. Add success, rejection, and authorization-boundary coverage for new behavior; reproduce bugs with a failing test first. Run the focused suite while iterating and `./scripts/verify.sh` before opening a PR.

## Commit & Pull Request Guidelines

History uses concise imperative summaries, sometimes Conventional Commit scopes such as `refactor:`, `feat(central):`, or `perf(chat):`. Keep commits focused. PRs must explain the problem, contract or data-model changes, security impact, verification results, and deployment/configuration needs. Link relevant issues and include screenshots for UI changes. Update both Chinese and `.en.md` documentation when public behavior or terminology changes. Report vulnerabilities through `SECURITY.md`, not public issues.
