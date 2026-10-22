# Changelog

All notable project changes will be documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). The Optimizer HTTP API is versioned independently; product releases use semantic versioning once the public contract is declared stable.

## [Unreleased]

### Added

- Platform Web workflow aligned with the canonical navigation: Workbench, Ingot Chat, Field Operations, Process Insight, Recipe Optimization, Configuration & Integration, and System Administration.
- Embedded Agent chat, investigation tools, data-quality explanations, and deterministic/OpenAI-compatible provider boundaries inside Platform API.
- Stateless BoTorch optimization service for next-recipe recommendations inside safety boundaries.
- Weighted objectives, parameter constraints, safety outcome constraints, and pending-point avoidance.
- Automatic assembly of optimization observations from cycles, actual recipes, process features, and inspections.
- Idempotent next-recipe recommendations and atomic outcome persistence.
- Bilingual open-source documentation and project website.
- Observed-coverage envelope that keeps next-recipe recommendations inside the region covered by real production runs. A range gate and a hat-matrix leverage gate generate the envelope from inside that region, and Platform revalidates it independently.

### Changed

- Platform is now documented as a modular monolith: `Platform API` hosts Platform Infrastructure and Agent capabilities, while `Edge ConnectorHost` and `Optimizer` remain separate runtime services.
- Website and Docs are documented as a separate public-site deployment, apart from the factory application Compose stack.
- Product positioning now centers on learning from real runs to reach the next recipe version.
- Internal validation on a manufacturing scenario continues: data onboarding and diagnosis are running, while historical replay, shadow operation, and controlled-online validation are still in progress. The scenario's industry and equipment details remain outside the public repository.
- Bumped the Optimizer HTTP API to `0.6.0` for the breaking historical-replay response field rename; no legacy field compatibility layer is retained.

### Removed

- Half-open `validate-hypothesis` decision intent; recommendations use `reach-specification` within the observed coverage envelope only.
- Unused optimizer `POST /v1/designs` DOE endpoint and Platform `DesignAsync` client path; historical replay remains for offline evaluation.
