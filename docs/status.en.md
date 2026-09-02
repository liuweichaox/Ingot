# Current status

> Document status: **rolling facts page**. This page states what the code implements and what deployers complete themselves. [Brand guide](brand.en.md) governs product positioning.

## Conclusion summary

Ingot's main software workflow is implemented, including field-data integration, run comparison, process diagnosis, and engineer-confirmed next-recipe recommendations generated directly from real recipe runs.

The repository claims only code, database contracts, automated tests, and reproducible software behavior. It bundles no scenario-specific validation data, historical protocols, or effect results.

- **Repository responsibility:** software capabilities, constraints, permissions, audit, fail-closed behavior, and a deployment reference.
- **Deployer responsibility:** data quality, scenario applicability, process safety, recipe adoption, and realized-benefit evaluation.

## Status overview

| Layer | Current status | Supported conclusion |
|---|---|---|
| Synthetic demo | Runnable | The principal pages and business workflow can be toured |
| Software path | Implemented with automated tests | Main functions run as designed; unmet conditions stop a recommendation and explain why |
| Production operation | Single-machine reference deployment available | Deployers still complete site security, recovery, capacity, and operations configuration |

## Implemented software capabilities

The repository currently covers:

- connecting field sources, standardizing fields and units, and resuming delivery after a network outage;
- linking equipment, product, specification, material, tooling, process curves, and quality outcomes to one run;
- checking completeness, actual execution values, units, sources, and versions before analysis;
- comparing eligible runs and showing key differences, candidate causes, counterevidence, and evidence gaps;
- automatically combining admitted real recipe runs with quality outcomes into optimization observations;
- generating next-recipe recommendations inside safety boundaries and the observed parameter envelope without automatic dispatch, then append-only freezing the engineer's adoption, modification, or rejection, reason, actual recipe, and linked run;
- selecting response-surface or Gaussian-process methods according to the data and degrading when evidence is insufficient;
- preserving evidence, constraints, model versions, engineer decisions, and one-time frozen final outcomes from actual execution, parameter readback, and inspection records for every recommendation;
- providing a permissioned analysis assistant in which authorized tools query structured production facts and reviewed process documents use project- and applicability-scoped keyword plus optional semantic retrieval with fragment-level citations, together with backup, restore, monitoring, and basic failure-drill tooling.

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

The default Docker Compose setup is for local development and a single-machine reference deployment; it does not complete production requirements. Production deployers still configure secrets, identity, and site isolation and complete backup, recovery, capacity, alerting, equipment interlock, human approval, stopping, and fallback drills.

See [Production architecture](production-architecture.en.md) for the target topology and [Deployment](deployment.en.md) for current operating steps.

## Status update rules

1. Software capability follows merged code, database migrations, and automated tests.
2. Scenario effects and business benefit are confirmed by deployers using their own evaluation data.
3. Production maturity is confirmed by recovery, capacity, security, and operating evidence from the target site.
