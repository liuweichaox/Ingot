# Current status

> Document status: **rolling facts page**. This page states what the code implements and what deployers complete themselves. [Brand guide](brand.en.md) governs product positioning.

## Conclusion summary

Ingot implements quality analysis, process analysis (including process diagnosis), and recipe recommendations based on production-run evidence. The workbench manages recipe versions under “Recipe Optimization” and process variables under “Configuration and integration”. Parameter codes, names, types, units, and bounds come from process variables, and settings belong to recipe versions. Each process run is an experiment. Runs record actual parameters and process trajectories, and quality records retain experiment outcomes.

The repository claims only code, database contracts, automated tests, and reproducible software behavior. It bundles no scenario-specific validation data, historical protocols, or effect results.

- **Repository responsibility:** software capabilities, constraints, permissions, audit, fail-closed behavior, and a deployment reference.
- **Deployer responsibility:** data quality, scenario applicability, process safety, recipe adoption, and realized-benefit evaluation.

## Status overview

| Layer | Current status | Supported conclusion |
|---|---|---|
| Local stack | Runnable | See [Getting started](getting-started.en.md) for deployment components and instructions |
| Recipe versions and parameters | Web/API implemented | Parameter definitions reference the process variables; recipe versions retain multiple parameter settings |
| Runs and experiment outcomes | Web/API implemented | Each run is an experiment linking actual parameters, process data, and quality outcomes; admitted runs form optimization observations |
| Recipe recommendations and engineer decisions | Web/API implemented | A correction lives on the published recipe version. Adoption leaves that version unchanged. The engineer confirms the later same-version run that executed it, and freezing checks actual parameters. A run that did not follow the decision does not validate that decision. It can enter later observations using its actual parameters if it passes admission. A significant change creates a revision draft from the suggested settings. |
| Software path | Implemented with automated tests | Tested paths behave as contracted; unmet conditions stop a recommendation and explain why |
| Production operation | Single-machine reference deployment available | Deployers still complete factory/production-site security, recovery, capacity, and operations configuration |

## Implemented software capabilities

The repository currently covers:

- recipe-version and process-variable management; run and quality records constitute experiment facts;
- accepting standard events from site adapters and resuming Edge-to-platform delivery after an outage; site adapters acquire data and map fields and units;
- linking equipment, product, recipe version, material, process curves, and quality outcomes to one run, with tooling also linked when a scenario needs its traceability;
- checking completeness, actual execution values, units, sources, and versions before analysis;
- comparing eligible runs and showing key differences, candidate causes, counterevidence, and evidence gaps;
- automatically combining admitted real recipe runs with quality outcomes into optimization observations;
- checking actual parameter readback against the engineer decision when freezing an outcome. A run that did not follow the decision remains traceable and does not count as validation for that decision. It can still enter later observations using its actual parameters if it passes admission;
- generating next-recipe candidates with a BoTorch Gaussian process and applying configured constraints and observed coverage checks. Insufficient evidence stops generation with a reason. The optimizer checks multivariate coverage before step alignment. Platform checks per-variable range and hard constraints after alignment. See [analysis and optimization](optimization.en.md) for the scope of each check;
- preserving recommendation evidence, constraints, model versions, and engineer decisions. Rejection ends the round. Adoption or modification can link an actual run and freeze one outcome when parameter readback and inspection records are available. The system does not dispatch recommendations automatically;
- providing a permissioned Ingot Chat, in which authorized tools query structured production facts and reviewed process documents use factory/production-site-scoped keyword plus optional semantic retrieval with fragment-level citations;
- providing backup, restore, monitoring, and basic failure-drill tooling.

“Implemented” means repository code, database contracts, and tests exist. It does not mean the software fits every process or has produced a particular business benefit. The repository also does not claim a completed retrieval-quality benchmark or proof that document retrieval shortens field-analysis cycles.

## Repository validation boundary

The repository bundles no public or field-effect datasets, prescribed round protocols, result trajectories, or effect reports. Optimizer unit tests cover algorithm contracts, determinism, constraints, and fail-closed behavior. Scenario-specific effect comparisons run in the deployer's own environment.

## Deployer responsibility

Deployers are responsible for:

- defining objectives, controllable parameters, safety constraints, and acceptable risk;
- ensuring reliable identity and timing across runs, recipes, process data, and quality results;
- comparing applicable baselines on their own data and choosing acceptance thresholds;
- reviewing, adopting, or rejecting recipe recommendations;
- evaluating actual quality, cost, cycle-time, and production-safety effects.

## Production-deployment boundary

The default Docker Compose setup is for local development and a single-machine reference deployment; it does not complete production requirements. Deployers still configure secrets, identity, and factory-level data isolation and validate backup, recovery, capacity, and alerting against their availability targets. Field integration also requires validation of connector buffering, replay, and recovery; equipment interlocks, operating authorization, and stop/recovery procedures remain the responsibility of existing field-safety processes. Ingot currently dispatches no equipment actions; any future action capability is a separate safety-engineering project.

See [Production architecture](production-architecture.en.md) for the target topology and [Deployment](deployment.en.md) for current operating steps.

## Inspect the supporting evidence

- Domain and authorization behavior: `tests/Ingot.Core.Tests`, including Platform workflow, factory-level authorization, optimizer-contract, and Edge event-ingestion tests.
- Numerical contracts: the locked `optimizer` test suite; run it through `uv run --project optimizer --locked pytest` after installing service and development dependencies.
- Public documentation and site output: the architecture, product-scope, product-language, and documentation-style checks, plus exported-site tests run by `./scripts/verify.sh`.
- Operational acceptance: backup/restore, failure drills, observability checks, and `scripts/verify-production-acceptance.sh` produce evidence for the target deployment. A script being present is not a passing result.

Always report the checked commit, command, result, and environment when making a verification claim. This page describes implemented scope; it does not record a fresh full-suite run for every reader.

## Status update rules

1. Software capability follows merged code, database migrations, and automated tests.
2. Scenario effects and business benefit are confirmed by deployers using their own evaluation data.
3. Production maturity is confirmed by recovery, capacity, security, and operating evidence from the target factory/production site.
