# Current status

> Document status: **rolling facts page**. This page is the single entry point for what the code implements, what validation has established, and what production use still requires. [Brand guide](brand.en.md) governs product positioning; the [public validation record](https://github.com/liuweichaox/Ingot/blob/main/tools/public-validation/README.en.md) governs detailed method results.

## Conclusion summary

Ingot's main software workflow is implemented, including field-data integration, run comparison, process diagnosis, and engineer-confirmed next-recipe recommendations generated directly from real recipe runs. Controlled validation is a separate optional workflow.

A working software workflow does not establish real-factory benefit. The current strategy has not yet passed an independent unseen-data acceptance, and formal real-factory historical review and prospective validation remain incomplete.

- **Confirmed:** the software organizes traceable run evidence and returns candidate process settings when data and safety conditions are met.
- **Not yet confirmed:** Ingot consistently reduces experiments, development time, or production risk in a real factory.

## Status overview

| Layer | Current status | Supported conclusion |
|---|---|---|
| Synthetic demo | Runnable | The principal pages and business workflow can be toured |
| Software path | Implemented with automated tests | Main functions run as designed; unmet conditions stop a recommendation and show why |
| Public method replay | Several test rounds completed; current method awaits new acceptance | Successes and failures are visible, but old results do not mean the current method passed |
| Real historical replay | Tools implemented; formal report incomplete | It is not yet formally shown that replay of an old project avoids seeing future results |
| Prospective shadow validation | Tools implemented; results incomplete | Recommendations are not yet shown to remain reliable on a new project; shadow advice does not affect actual decisions |
| Controlled online validation | Workflow and gates implemented; results incomplete | Real-factory benefit and causal effect are not established |
| Production operation | Single-machine reference deployment available | Production still requires high-availability, recovery, security, and capacity acceptance |

## Implemented software capabilities

The repository currently covers:

- connecting field sources, standardizing fields and units, and resuming delivery after a network outage;
- linking equipment, product, specification, material, tooling, process curves, and quality outcomes to one run;
- checking completeness, actual execution values, units, sources, and versions before analysis;
- comparing eligible runs and showing key differences, candidate causes, counterevidence, and evidence gaps;
- automatically combining admitted real recipe runs with quality outcomes into optimization observations without requiring a user-created experiment;
- generating next-recipe recommendations inside safety boundaries and the observed parameter envelope without automatic dispatch;
- designing controlled validation with controls, repetition, and safety boundaries when needed, then reviewing candidate operating regions;
- selecting response-surface or Gaussian-process methods according to the data, with fallback when a complex method is unsuitable;
- preserving evidence, constraints, model versions, engineer review, and final outcomes for every recommendation;
- providing a permissioned analysis assistant plus backup, restore, monitoring, and basic failure-drill tooling.

“Implemented” means that repository code, database contracts, and tests exist. It does not mean that a factory has completed data, performance, security, or benefit acceptance.

## Public method evidence

Public-data testing asks whether the system can reach a target sooner when selecting experiments one by one from a set whose outcomes are already known. It does not replace a real-factory pilot. Three conclusions are supported:

- the system was faster than random trial and error in some tests;
- in other tests it did not consistently beat a simpler response-surface method, so overall acceptance failed;
- the algorithm has since changed, so the old data are useful for development checks but no longer provide independent proof of the current method.

The current method still needs acceptance on a new dataset group that was not inspected during development.

The complete protocols, figures, confidence intervals, and failure breakdowns are maintained only in [Optimizer experiment-efficiency validation](https://github.com/liuweichaox/Ingot/blob/main/tools/public-validation/README.en.md) and its audit files.

## Real-project evidence

Real projects advance in this order, with each step answering one question:

```text
Historical review (can an old project be replayed correctly?)
→ Shadow validation (do new recommendations remain sensible without influencing engineers?)
→ Controlled online (is adopting a recommendation safe and useful?)
→ Second scenario (does the approach still work for a different process?)
```

None of these four steps yet has a formal result that may be claimed publicly as passed. Real production data, project and equipment identities, parameter distributions, and calculated results remain in the controlled factory environment. Public-data tests cannot stand in for this evidence.

See [Scenario validation](rollout.en.md) for the acceptance questions, preregistration content, and falsification conditions.

## Production-deployment boundary

The default Docker Compose setup is for local development validation and a single-machine reference deployment; it is not production acceptance. A production deployer must still complete:

- secrets, identity, site isolation, and least-privilege configuration;
- backup and restore, acceptable data-loss and recovery-time targets, failover, and sustained-observation evidence;
- capacity, backlog, latency, and retention acceptance under representative load;
- object storage, point-in-time recovery, high availability, and external-dependency failure plans;
- equipment interlocks, human approval, stopping, and fallback drills.

See [Production architecture](production-architecture.en.md) for the target topology and [Deployment](deployment.en.md) for current operating steps.

## Status update rules

Every status change requires reviewable evidence:

1. software capability follows merged code, database migrations, and automated tests;
2. method effect follows preregistration, freeze, unseen data, and complete failure retention;
3. real-project value follows separately reviewed historical, shadow, and online reports;
4. production maturity follows recovery, capacity, security, and observation evidence from the target site.

An API, a runnable demo, or completed internal infrastructure does not automatically promote a higher evidence layer.
