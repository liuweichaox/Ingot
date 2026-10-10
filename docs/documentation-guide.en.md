# Documentation guide

This guide defines page responsibilities, fact checking, and pre-publication checks for documentation contributors. See the [Documentation home](index.en.md) for reader paths and [Contributing](../CONTRIBUTING.en.md) for development setup.

## Organization principles

The README explains the project, its audience, how to start, and where to get help. The documentation site groups content into getting started, how-to guides, concepts, and reference. Give each business rule one primary explanation and link to it elsewhere; avoid conflicting copies of rules or configuration.

- **Getting started**: a short executable path, prerequisites, checkpoints, and next steps.
- **How-to guides**: inputs, steps, success conditions, and failure handling for a specific task.
- **Concepts**: responsibilities, data flows, design rationale, and constraints, separating implementation from target architecture.
- **Reference**: searchable object definitions, configuration sources, table responsibilities, and terminology.

Keep existing URLs stable. When moving content, retain the entry point or add a redirect and check heading anchors.

## Reference projects

The structure draws on these projects; Ingot business content, configuration, and capability claims are grounded in this repository:

- [Best-README-Template](https://github.com/othneildrew/Best-README-Template): project overview, installation, usage, contribution, license, and support entry points.
- [OpenTelemetry Documentation](https://opentelemetry.io/docs/): task-oriented starting paths with concepts separated from implementation reference.
- [Gitea Documentation](https://docs.gitea.com/): installation, usage, administration, and maintenance for a self-hosted project.
- [Docker Get started](https://docs.docker.com/get-started/): prerequisites followed by procedures and checkpoints.

Use information architecture and writing patterns without copying project descriptions or importing unsuitable runtime capabilities. Check licensing and retain required attribution when quoting text, code, or assets.

## Sources and capability claims

The [Brand guide](brand.en.md) governs positioning and wording. [Current status](status.en.md) and corresponding code govern implemented capability; planned work belongs in the [Roadmap](project-plan.en.md).

Verify operating instructions directly against:

- Services, ports, profiles, and dependencies: `docker-compose.app.yml`.
- Configuration keys and defaults: `.env.example`, host configuration, and validators.
- API routes and authorization: controllers, endpoint registrations, and authorization rules; see [Getting started](getting-started.en.md) for runtime OpenAPI.
- Record ownership and migrations: `src/platform/Ingot.Platform.Infrastructure/Migrations/sql` and the [Data model](data-model.en.md).
- Verification scope: tests and gates actually run; passing tests establish only the software behavior they cover.

Distinguish implemented behavior, deployer configuration or verification, and planned capabilities. Do not describe single-node Compose as high availability or minimum sample counts as sufficient statistical evidence. Benefit, causality, safety, and field suitability need corresponding evidence.

## Bilingual content and site

Chinese files use `docs/<slug>.md`; English files use `docs/<slug>.en.md`. Keep heading levels, step order, examples, and boundary meanings aligned while writing naturally in each language. Prefer relative links to the same language; use `../` for root documents.

`docs/` is the single source of documentation content, rendered by the site. For a new public page, update `apps/docs-site/lib/public-docs.json` and navigation so both languages support access, search, and switching. Do not duplicate maintained prose in React components or generated directories.

## Writing procedures

Include prerequisites, working directory, commands, success conditions, and a failure-diagnosis entry point. Label Bash and PowerShell differences so readers know which terminal to use.

Use placeholders and state replacement requirements. Examples contain no real production data, tokens, or equipment identities. Backup, restore, and upgrade instructions explain data protection. Diagnostics default to read-only operations; destructive cleanup is not a routine troubleshooting step.

## Verification before submission

Run from the repository root:

```bash
./scripts/verify-architecture.sh
./scripts/verify-product-scope.sh
./scripts/verify-product-language.sh
python3 scripts/verify-documentation-style.py
npm --prefix apps/docs-site run lint
npm --prefix apps/docs-site test
git diff --check
```

Site tests include static build and export checks. Manually inspect desktop and narrow-screen reading, task entry points, heading anchors, language switching, search, and source/feedback links. For public behavior, configuration, or architecture changes, also run relevant tests; run `./scripts/verify.sh` before a PR as required by the repository.

Report checks actually passed and reasons for incomplete checks. Do not label unexecuted commands as passing. Finally, verify the paired language page and all relative links.
