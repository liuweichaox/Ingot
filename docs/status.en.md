# Current status

> Document status: **rolling facts page**. This page states what the code implements and what deployers complete themselves. [Brand guide](brand.en.md) governs product positioning.

## Conclusion summary

Ingot implements quality analysis, process diagnosis, and recipe recommendations based on production-run evidence. The Web app manages recipe versions through process configuration: parameter codes, names, types, units, and bounds come from the process variables, while settings belong to recipe versions. It does not create a separate experiment-variable definition. Each process run is an experiment: recipe versions define parameter settings, runs record actual execution parameters and process trajectories, and quality records retain experiment outcomes. The system has no separate experiment-record model.

The repository claims only code, database contracts, automated tests, and reproducible software behavior. It bundles no scenario-specific validation data, historical protocols, or effect results.

- **Repository responsibility:** software capabilities, constraints, permissions, audit, fail-closed behavior, and a deployment reference.
- **Deployer responsibility:** data quality, scenario applicability, process safety, recipe adoption, and realized-benefit evaluation.

## Status overview

| Layer | Current status | Supported conclusion |
|---|---|---|
| Local stack | Runnable | See [Getting started](getting-started.en.md) for deployment components and instructions |
| Recipe versions and parameters | Web/API implemented | Parameter definitions reference the process variables; recipe versions retain multiple parameter settings |
| Runs and experiment outcomes | Web/API implemented | Each run is an experiment linking actual parameters, process data, and quality outcomes; admitted runs form optimization observations |
| Recipe recommendations and engineer decisions | Web/API implemented | A correction lives on the published recipe version. Adoption leaves that version unchanged and the next run of the same version attaches automatically. A significant change creates a revision draft from the suggested settings |
| Software path | Implemented with automated tests | Main functions run as designed; unmet conditions stop a recommendation and explain why |
| Production operation | Single-machine reference deployment available | Deployers still complete site security, recovery, capacity, and operations configuration |

## Implemented software capabilities

The repository currently covers:

- recipe-version and process-variable management; run and quality records constitute experiment facts;
- connecting field sources, standardizing fields and units, and resuming delivery after a network outage;
- linking equipment, product, specification, material, tooling, process curves, and quality outcomes to one run;
- checking completeness, actual execution values, units, sources, and versions before analysis;
- comparing eligible runs and showing key differences, candidate causes, counterevidence, and evidence gaps;
- automatically combining admitted real recipe runs with quality outcomes into optimization observations;
- generating next-recipe recommendations inside safety boundaries and the observed parameter envelope without automatic dispatch, then append-only freezing the engineer's adoption, modification, or rejection, reason, actual recipe, and linked run;
- selecting response-surface or Gaussian-process methods according to the data and degrading when evidence is insufficient;
- preserving evidence, constraints, model versions, engineer decisions, and one-time frozen final outcomes from actual execution, parameter readback, and inspection records for every recommendation;
- providing a permissioned analysis assistant in which authorized tools query structured production facts and reviewed process documents use site-scoped keyword plus optional semantic retrieval with fragment-level citations, together with backup, restore, monitoring, and basic failure-drill tooling.

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

The default Docker Compose setup is for local development and a single-machine reference deployment; it does not complete production requirements. Deployers still configure secrets, identity, and site isolation and validate backup, recovery, capacity, and alerting against their availability targets. Field integration also requires validation of connector buffering, replay, and recovery; equipment interlocks, operating authorization, and stop/recovery procedures remain the responsibility of existing field-safety processes. Ingot currently dispatches no equipment actions; any future action capability is a separate safety-engineering project.

See [Production architecture](production-architecture.en.md) for the target topology and [Deployment](deployment.en.md) for current operating steps.

## Inspect the supporting evidence

- Domain and authorization behavior: `tests/Ingot.Core.Tests`, including Platform workflow, site authorization, optimizer-contract, and Edge acquisition tests.
- Numerical contracts: the locked `optimizer` test suite; run it through `uv run --project optimizer --locked pytest` after installing service and development dependencies.
- Public documentation and site output: the architecture, product-scope, product-language, and documentation-style checks, plus exported-site tests run by `./scripts/verify.sh`.
- Operational acceptance: backup/restore, failure drills, observability checks, and `scripts/verify-production-acceptance.sh` produce evidence for the target deployment. A script being present is not a passing result.

Always report the checked commit, command, result, and environment when making a verification claim. This page describes implemented scope; it does not record a fresh full-suite run for every reader.

## Status update rules

1. Software capability follows merged code, database migrations, and automated tests.
2. Scenario effects and business benefit are confirmed by deployers using their own evaluation data.
3. Production maturity is confirmed by recovery, capacity, security, and operating evidence from the target site.
