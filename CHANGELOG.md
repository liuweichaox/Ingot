# Changelog

All notable project changes will be documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). The Optimizer HTTP API is versioned independently; product releases use semantic versioning once the public contract is declared stable.

## [Unreleased]

### Added

- Platform Web workflow aligned with the canonical navigation: Workbench, Ingot Chat, Field Operations, Process Insight, Recipe Optimization, Configuration & Integration, and System Administration.
- Embedded Agent chat, investigation tools, data-quality explanations, and deterministic/OpenAI-compatible provider boundaries inside Platform API.
- Stateless BoTorch optimization service for next-recipe recommendations inside safety boundaries.
- Multi-objective specification distance (normalized worst-objective ranking), parameter constraints, safety outcome constraints, and pending-point avoidance.
- Automatic assembly of optimization observations from cycles, actual recipes, process features, and inspections.
- Idempotent next-recipe recommendations and atomic outcome persistence.
- Execution-adherence check when freezing a recommendation outcome: actual parameter readback is compared with the engineer decision; a non-adherent run remains as traceability evidence and does not count as validation for that decision, while the run itself still contributes to later surrogate observations using its actual parameters.
- Prediction evidence panel showing interval bounds, feasibility probability, model version, observation count, and cold-start state.
- Candidate-run selection and outcome receipt in the recipe correction panel: engineers explicitly link the run that executed the decision and see prediction-versus-actual plus adherence results.
- Bilingual open-source documentation and project website.
- Observed-coverage envelope that keeps next-recipe recommendations inside the region covered by real production runs. A range gate and a hat-matrix leverage gate generate the envelope from inside that region; Platform independently revalidates the range gate and requires the optimizer's coverage report.

### Changed

- Platform is now documented as a modular monolith: `Platform API` hosts Platform Infrastructure and Agent capabilities, while `Edge ConnectorHost` and `Optimizer` remain separate runtime services.
- Website and Docs are documented as a separate public-site deployment, apart from the factory application Compose stack.
- Product positioning now centers on learning from real runs to reach the next recipe version.
- Internal validation on a manufacturing scenario continues: data onboarding and diagnosis are running, while historical replay, shadow operation, and controlled-online validation are still in progress. The scenario's industry and equipment details remain outside the public repository.
- Bumped the Optimizer HTTP API to `0.6.0` for the breaking historical-replay response field rename; no legacy field compatibility layer is retained.
- Outcome linking is now explicit: the recipe correction panel lists candidate runs and requires the engineer to confirm the run that executed the decision; it no longer auto-links the first later same-version run or auto-freezes its outcome.
- The correction decision no longer sends an inferred usefulness rating; the previous values were hardcoded from the decision type rather than reported by the engineer.
- The architecture gate now rejects protocol-specific imports in Edge and the retired `Acquisition` directory under the Edge host, keeping device protocols in external adapters.

### Fixed

- Import CLI error guidance no longer claims that rerunning with the same sequence start is safe; it now matches the current non-resumable behavior and asks operators to reconcile submitted ranges first.

### Removed

- Retired the unused training dataset, process model, model evaluation, model drift, and dataset quality validation surfaces. Their historical database tables remain for migration compatibility, but no current user path reads or writes them. This removes the speculative research-asset API and validation runner; mechanism knowledge and document retrieval remain.
- Breaking upgrade: the former `/api/v1/training-datasets` endpoints return 404 after upgrade. Before upgrading, export required records through the previous API or preserve a database backup; no replacement export endpoint is provided. Restrict access to exports and backups.
- Retired in-tree protocol acquisition sources (`Acquisition/`, `AcquisitionController`, and `AcquisitionProbeTaskHostedService`) that were already excluded from compilation. Edge remains adapter-agnostic and accepts standard events from external adapters only.
- Half-open `validate-hypothesis` decision intent; recommendations use `reach-specification` within the observed coverage envelope only.
- Unused optimizer `POST /v1/designs` DOE endpoint and Platform `DesignAsync` client path; historical replay remains for offline evaluation.
