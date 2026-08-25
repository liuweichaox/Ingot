# Frequently asked questions

> Status: **current product and technical boundaries**.

## What core problem does Ingot solve?

A shared run identity links actual conditions, process curves, and quality outcomes so engineers can review field facts in one place. The system then compares run differences, forms candidate causes worth testing, and designs the next experiment.

## Is Ingot a data-acquisition system?

Data acquisition is not Ingot's only responsibility. Acquisition receives raw data; the system also determines the associated run, actual conditions, quality outcome, and subsequent validation task.

## Does Ingot replace process engineers?

No. The system organizes facts, performs calculations, explains uncertainty, and proposes actions. Engineers frame the problem, review field constraints, approve experiments, and make the final judgment.

## Can the system find root causes automatically?

Historical data cannot establish a root cause by itself; it can only identify associations between factors. Material, equipment, time, and tooling may change together and create confounding. The system records these limits and supports controlled, repeated experiments. A candidate becomes a validated cause only when experimental results and engineering judgment support it.

## Why does the system record conditions, runs, and inspections together?

Conditions, runs, and inspections jointly describe execution conditions, execution behavior, and final outcomes. In the code they correspond to Manufacturing, Process Executions, and Inspections. Missing any one of these records can cause runs made under different conditions to be compared incorrectly.

## Why are planned settings insufficient?

Equipment limits, deviations, operator adjustments, and dynamic response can cause planned and actual values to differ. When actual values are missing, the system excludes the run with an explicit reason and prohibits silent substitution from the plan.

## Why do process trajectories matter?

The same setting may produce different heating rates, overshoot, pressure hold, position, or cooling trajectories. Quality depends on the realized process, not only the process specification table. Stage features help engineers locate where deviation began.

## Why record material, tooling, and equipment context?

Material, tooling, and equipment context may affect quality or may serve only traceability. The system verifies that sufficient comparable data exist before estimating their influence; field presence alone does not constitute causal evidence.

## Why not always use the most complex model?

Model complexity does not establish reliability. With limited samples or confounded conditions, a controlled experiment often provides clearer evidence than a complex model. When evidence is insufficient, the system requires additional data or experiments and does not generate an unsupported conclusion.

## What responsibilities does the language model have?

A large language model (LLM) parses questions, queries authorized records, and generates explanations of calculated results. Numeric process settings come from deterministic calculations. The language model does not replace statistical analysis, constraint checks, or experimental validation and may not generate facts without sources.

## As foundation models and agents become more capable, what does Ingot become?

Ingot's product position does not change with foundation-model capability. Language models remain replaceable components, while Ingot preserves run records, evidence sources, experiment state, approvals, and final conclusions. See the [Roadmap](project-plan.en.md).

## Does adding MCP make an agent safe to drive experiments?

No. Model Context Protocol (MCP) standardizes only how a model discovers and calls tools. Project access, recommendation approval, call idempotency, device confirmation, and failure recovery remain under platform and field-system control. An agent may not approve its own proposal or bypass the platform to connect directly to equipment.

## When is Bayesian optimization appropriate?

Bayesian optimization applies when individual experiments are costly and each result can guide subsequent experiment selection. The controllable-variable count must be limited, objectives measurable, and safety boundaries explicit. When variables are numerous, the process drifts rapidly, feedback is delayed, or key factors are unmeasured, the problem scope or data must be improved first.

## How does a small-sample project start?

The project first establishes an approved safe baseline, then runs a small set of experiments that can separate the principal factors. The initial design may use engineering judgment, traditional design of experiments (DOE), or evenly distributed points within an approved region. Without safety evidence, an algorithm may not attempt arbitrary process settings.

## Can the system generate a batch of experiment recommendations?

Yes. When equipment can run in parallel or the site schedules work in batches, the system can propose a group of experiments. Unfinished experiments remain “pending points” to prevent the next round from repeating the same conditions. Field capacity and experiment design determine the batch size.

## Are experiment recommendations written automatically to controls?

Not by default. A recommendation must become a formal experiment and pass engineer review before it is executed manually, through an MES or process-specification system, or through an approved integration. Equipment interlocks and field safety remain independent of the model.

## Does an optimization or language-model outage stop acquisition?

No. Field acquisition, run records, and inspections continue; generation of new optimization recommendations and natural-language explanations pauses.

## Why don't public materials name a specific validation scenario?

Real projects usually contain restricted equipment, parameter, and production data. The repository therefore publishes validation methods, data formats, synthetic examples, and conclusion boundaries without identifying a factory. Public data with appropriate permission can reproduce software and algorithm behavior but cannot replace real-factory validation. This rule protects field information and preserves scenario neutrality.

## How is a reduction in experiment count validated?

Before results are reviewed, the validation plan fixes the target, starting data, experiment budget, comparison methods, and pass criteria. Every executed experiment is included in cost. Historical review, shadow use, and controlled online experiments respectively evaluate historical efficiency, recommendation stability on a new project, and experiment count and elapsed time after adoption. See [Scenario validation](rollout.en.md) for the complete method.

Existing public-data tests support a cautious conclusion: the system was faster than random trial and error in some tests, but did not consistently beat every applicable simple method, so overall acceptance failed. The algorithm has changed and still needs another test on data that were not inspected during development. See [Public-data experiment-efficiency validation](https://github.com/liuweichaox/Ingot/blob/main/tools/public-validation/README.en.md) for complete figures and failures.

## Is the documentation now finalized?

Core value, product boundaries, evidence principles, and stable architecture are fixed. Algorithms, default experiment parameters, menus, implementation status, roadmap, and validation results continue to evolve without redefining the core value or bypassing evidence boundaries.
