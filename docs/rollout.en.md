# Scenario validation

> Status: **rolling validation protocol**. This document defines how to prove that Ingot genuinely helps process engineers, not how to prove that one algorithm looks advanced.

[Roadmap](project-plan.en.md) defines why the system is built, what it aims to become, and when it may advance. This document only defines how historical replay, shadow validation, and controlled online experiments are preregistered, run, falsified, reviewed internally, and translated into public conclusion boundaries. The three work lines answer different questions and cannot substitute for one another.

## Validation questions

A real project answers four questions in order:

1. **Is the data more trustworthy?** Can an engineer find the actual conditions, trajectory, context, and quality outcome of a run?
2. **Is judgment more efficient?** Is the path from anomaly to executable hypothesis faster, more complete, and less dependent on manual data gathering?
3. **Can causes be validated?** Do candidate causes include evidence, counterevidence, and confounding limits, and can they become effective experiments?
4. **Are experiments more effective?** Under the same safety boundaries and candidate range, does the process reach and confirm the objective with fewer valid experiments?

Failure of an earlier question cannot be hidden by a later one. A successful model recommendation does not repair incorrect run-to-quality linkage.

## Select the first project

The first validation project has:

- a bounded problem, product, and equipment scope;
- measurable controlled variables and quality objectives;
- linkable run identity, actual settings, process data, and inspections;
- traceable material, tooling, lot, and equipment context;
- documented safety boundaries and current engineer decision workflow;
- a comparable historical sequence or permission for prospective experiments.

The first scenario's historical replay and shadow validation are still in progress, and even passing them would not prove applicability elsewhere; its industry and equipment details stay out of the public repository. A second, materially different process tests the generality of stable contracts.

## Phase 0: preregistration and data baseline

Platform stores phase 0 as an immutable, project-scoped preregistration version. Each version binds the current project-definition hash, freezes a data-reliability baseline for the declared time, Edge, and equipment scope, records the time and steps of the engineer's existing workflow, and can only be reviewed by a different project member. A project-definition change invalidates the old admission evidence. A new project cannot move from draft into active research without a current reviewed preregistration. A scope with no analyzable runs produces an explicit warning rather than inventing a universal percentage threshold.

Before seeing results, record:

- data and time range and project-inclusion method;
- primary question, variables, objectives, constraints, and context fields;
- comparison baseline and matching rules;
- data-exclusion rules;
- primary measures, guardrails, and stopping conditions;
- engineer workflow, traditional methods, and computational methods to compare;
- results that would falsify the claim that the system helped engineers.

First calculate:

- run completeness;
- actual-setting and process-feature coverage;
- unique run-to-inspection linkage;
- context required for analysis;
- unit, clock, configuration-version, and provenance anomalies;
- current engineer time and steps for gathering data, analysis, and experimentation.

When analysis conditions fail, the result is “repair the data chain first,” not “fit the model anyway.”

## Phase 1: replay historical engineering questions

Reconstruct real past problems using only information available at the time. The system may not see later inspections, conclusions, or the final successful process specification in advance.

For each question, compare:

- the engineer's original data-gathering and baseline-selection steps;
- whether Ingot automatically assembles facts for the same run;
- whether candidates cover important factors later supported by evidence;
- whether candidates cite correct records and state counterevidence, confounding, and missingness;
- whether the system refuses correctly when evidence is insufficient;
- time from problem start to the first executable validation experiment.

Engineers review the golden-question set; developers cannot author the standard answers alone.

## Phase 2: production-equivalent sequential replay

For historical projects suited to sequential optimization, round `t` exposes only facts from rounds up to `t`. The method proposes a point from a preregistered candidate pool or approved range before revealing its result.

Compare at least:

- the historical engineer sequence;
- applicable traditional DOE or response-surface methods;
- simple random or space-filling baselines;
- the current Ingot strategy.

Record:

- valid experiments to first specification attainment;
- experiments to attainment plus repeated confirmation;
- safety violations and failed experiments;
- prediction-interval coverage and feasibility calibration;
- distance from a recommendation to the nearest real candidate;
- material, equipment, inspection, and calendar cost.

When history covers only a narrow region, results evaluate candidate-pool ranking only, not continuous-space optimization.

Phases 1 and 2 together test whether the historical evidence apparatus is trustworthy, reproducible, and leakage-free. They do not by themselves prove that recommendations improve a real process. The replay artifact freezes at least the data scope, per-round visible information, inclusion and exclusion, baselines, policy and model versions, random seeds, sequential outputs, failures, review records, and content hashes.

## Phase 3: shadow recommendations on a new project

Platform audits the preceding sequential replay traces fail-closed: the trace count must match the seed count, each step's visible set must exactly equal the previously revealed set, candidate indices must be valid and unused, and the trace must match the final selected sequence. Empty, incomplete, duplicate, out-of-range, or self-inconsistent traces cannot pass the gate.

Generate recommendations without changing the engineer's original experiment order. Before the outcome, freeze:

- the data and context snapshot visible to the system;
- system recommendation, prediction, risk, and rationale;
- independent engineer choice and rationale;
- field constraints behind any rejection;
- later outcomes for both choices where observable.

Shadow mode discovers unmodeled constraints, incorrect mappings, unexecutable settings, and explanations engineers actually need. Recommendations cannot be edited after the fact to look closer to the outcome.

## Phase 4: controlled online experiments

Enter only when:

- the data chain and analysis admission have field acceptance;
- historical replay has no future-data leakage;
- hard bounds, safe baseline, and fallback have been rehearsed;
- recommendations can be set accurately and actual values captured;
- intervals and feasibility passed basic calibration checks;
- important context has blocking, randomization, or repetition plans;
- an engineer can review, modify, or reject every recommendation.

Begin with one recommendation at a time. Update only after its quality outcome is complete. Any safety anomaly, data drift, or run-linkage failure pauses recommendations.

## Measures

### Data trust

- percentage of complete, traceable runs;
- actual-setting, trajectory, context, and inspection coverage;
- automatic linkage success and manual repair time;
- percentage of exclusion causes repaired.

### Engineering-decision value

- time from anomaly to first executable hypothesis;
- candidate usefulness rated useful, partly useful, or not useful by engineers;
- source-citation coverage for important claims;
- unsupported causal-claim rate and correct-refusal rate;
- repeat analyses reproducing the same facts and results.

### Experiment and optimization value

- valid experiments to attain and repeatedly confirm specification;
- candidate causes supported, rejected, or left inconclusive;
- recommendation acceptance and rejection reasons;
- calibration, failed experiments, and zero known safety violations;
- material, equipment, inspection, and calendar time relative to baselines.

Do not assume one universal percentage target. Phase 0 records a baseline before a scenario approves its targets.

## Failure and falsification conditions

The project cannot use feature count as evidence of success when:

- runs, context, and inspections cannot link reliably;
- candidates regularly omit major field factors or cite incorrect evidence;
- the system cannot refuse under insufficient evidence;
- engineers cannot convert output into executable experiments;
- sequential recommendations do not beat applicable simple baselines or traditional DOE;
- uncertainty remains miscalibrated;
- recommendations fail to reproduce in confirmation runs;
- a known safety boundary is violated.

These findings trigger data repair, workflow changes, method downgrade, or an optimization pause rather than automatic progression to a more complex phase.

## Evidence confidentiality and public wording

The complete report for a real project is a controlled internal evidence artifact. It preserves data scope, inclusion rules, variables, objectives, constraints, context coverage, exclusions, comparison baselines, random seeds, every failure, safety event, engineer rejection reason, non-generalizable limitation, and content hash. Access is limited to authorized project members and reviewers and follows the deployer's retention, export, backup, and deletion rules.

Real production data, project and equipment identities, process parameters, quality distributions, sequential traces, and derived results do not enter the public repository or public reports. Public materials may expose general protocols, schemas, synthetic examples, explicitly licensed and checksum-verified public-data benchmarks, acceptance methods, and conclusion boundaries that disclose no project facts. Synthetic data validates contracts and software behavior; public data may additionally reproduce method comparisons. Neither is real-project evidence or proof of factory benefit. Data used during algorithm development remains development regression evidence. External-data evaluation freezes its data, baselines, budget, and decision rules before the effect run and retains unfavorable results.

Public wording may publish independently reproducible public-benchmark results and may state which internal validation stage has been completed, but the two evidence sources must remain explicit. Internal real-project evidence remains non-public and cannot be independently reproduced by the public, and neither a public benchmark nor internal evidence without prospective controlled validation may support a quantitative factory-benefit claim.
