# Real-scenario validation

> Status: **rolling validation protocol**. This document defines how to prove that Ingot genuinely helps process engineers, not how to prove that one algorithm looks advanced.

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

Optical-lens molding may be the first scenario, but it does not prove applicability elsewhere. A second, materially different process tests the generality of stable contracts.

## Phase 0: preregistration and data baseline

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

Reconstruct real past problems using only information available at the time. The system may not see later inspections, conclusions, or the final successful recipe in advance.

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

## Phase 3: shadow recommendations on a new project

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

## Publishing results

A public report includes data scope, anonymization, inclusion rules, variables, objectives, constraints, context coverage, exclusions, comparison baselines, random seeds, every failure, safety event, engineer rejection reason, and non-generalizable limitation.

Until real validation is complete, the website describes what the system can do without claiming unproven quantitative benefit.
