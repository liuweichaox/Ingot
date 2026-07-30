# Changelog

All notable project changes will be documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and releases use semantic versioning once the public API is declared stable.

## [Unreleased]

### Added

- Industrial-object-centered Platform Web workflow for operations, investigation, context, connection, and administration.
- Cycle diagnosis, controllable hypotheses, hypothesis-validation experiments, independent process-window validation, and reusable research assets.
- Embedded Agent chat, investigation tools, data-quality explanations, and deterministic/OpenAI provider boundaries inside Platform API.
- Stateless BoTorch optimization service with qLogNEI and qLogNEHVI.
- Two-stage surrogate modeling for setpoint-to-trajectory and trajectory-to-quality behavior.
- Weighted objectives, parameter constraints, safety outcome constraints, and pending-point avoidance.
- Automatic assembly of experiment observations from cycles, actual recipes, process features, and inspections.
- Idempotent optimized experiments and atomic result persistence.
- Bilingual open-source documentation and project website.

### Changed

- Platform is now documented as a modular monolith: `Platform API` hosts Platform Infrastructure and Agent capabilities, while `Edge ConnectorHost` and `Optimizer` remain separate runtime services.
- Website and Docs are documented as a separate public-site deployment, apart from the factory application Compose stack.
- Product positioning now centers on reducing experiments required to reach process specification.
- The optical-lens molding and Mitsubishi FX3U workflow is the first concrete validation scenario.
