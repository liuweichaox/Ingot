# Analysis and optimization

## Diagnostic readiness and confounder disclosure

Execution-comparison responses expose a structured `readiness.mode`. `descriptive-only` shows coverage, missingness, trends, strata, and data-chain gaps and disables candidate-hypothesis generation. `exploratory` may show ranked observational candidates. `candidate-ranking` means out-of-sample performance and stability support a stronger ranking, but it still does not mean a confirmed cause. Every candidate requires controlled repeated experiments.

The response also freezes the context variables actually adjusted, observed group imbalances, and known unmeasured confounders from both the versioned analysis plan and the current request. The first release fixes `sensitivityAssessment.status` at `not-estimable`: standardized coefficients and model importance are not an interpretable risk-ratio estimand and lack the required confidence interval, so the product does not display an invalid E-value. Metrics record only readiness modes and structured blocking reasons, never user-entered free text as labels.

> Status: **current scientific strategy**. This document explains how methods are selected by the engineering question and describes the limits of today's numerical implementation. Algorithms may evolve without changing the core value or evidence principles.

## Method-selection principle

Ingot starts from the engineer's decision, not from an algorithm:

| Engineering question | Preferred methods | Required output |
|---|---|---|
| Can the data be used? | completeness, units, time, provenance, and drift checks | admitted or excluded with a specific reason |
| Where did this run differ? | like-for-like matching, robust statistics, stage-trajectory comparison | difference, effect, uncertainty, and comparison baseline |
| Which factors deserve validation? | screening, stratification, variance components, mixed effects, or stable feature selection | candidate causes, counterevidence, confounding, and coverage limits |
| Is a factor causal? | controls, repetition, blocking, randomization, or intervention | supported, rejected, or inconclusive |
| What should the next experiment be? | DOE, response surfaces, active learning, or Bayesian optimization | settings, expected information, risk, and rationale |
| How should it be explained? | deterministic result templates plus LLM-assisted language | readable explanation with source citations |

A complex method is not inherently better than a simple one. With few samples, poor coverage, or confounded variables, the right action may be to collect data, run an identifying experiment, or refuse to answer rather than fit a more elaborate model.

## Analysis admission

Before any model, confirm that a run has trustworthy evidence:

- the run is complete;
- actual controls exist and are not silently replaced by planned values;
- process data and required features are available;
- objectives and safety outcomes have values, units, and provenance;
- scenario-required context has resolved;
- configuration, feature, and inspection versions are explicit;
- numbers are finite and run-to-quality linkage is unique.

Runs that fail remain in the data-quality report. Exclusion reasons are themselves evidence for improving wiring and workflow.

## Description and comparison

The first analytical layer favors reviewable methods:

- median, MAD, quantiles, and sample size;
- matched comparison under the same product, process specification, equipment, or tooling conditions;
- stage-aligned trajectory differences, first deviation, and planned-versus-actual gaps;
- missingness, anomaly, coverage, and temporal drift;
- effect size and intervals rather than only significance labels.

This layer answers “where is it different?” but not directly “what caused it?”

## Context and candidate causes

Equipment, material, tooling, lot, and maintenance state begin as traceability and stratification variables. Estimate their influence only when the data permit it:

1. Check sample count, missingness, and time distribution for every level.
2. Check factor overlap inside the main controlled conditions.
3. Use matching, variance components, mixed effects, time trends, or regularized models as appropriate.
4. Test stability with resampling, out-of-time validation, or selection frequency.
5. Label the result as stable association, confounded association, or insufficient evidence.
6. Design the next experiment to identify important candidates.

A model cannot separate factors that never overlap. If every mold A run uses only material A, history alone cannot distinguish mold effect from material effect.

## Causal validation and experimental design

Candidate causes require appropriate intervention before promotion. Experiment design considers:

- a declared hypothesis, objective, and minimum meaningful effect;
- controls and actually controllable variables;
- repeated runs to estimate process noise;
- blocks for equipment, tooling, material lot, or time period;
- randomization or rotated order where the field allows it;
- stopping, failure, and safety fallback conditions;
- independent confirmation runs.

A single point or single block can provide intervention support at most. A continuous operating region also requires boundary, repetition, and relevant interaction validation.

### Classical DOE previews

Before saving an experiment, an R&D project can generate an editable run plan for full factorial, fractional factorial, central composite (CCD), Box–Behnken, and Latin-hypercube designs. The generator uses only declared controllable variables with approved ranges, fixes a random seed, and returns blocks, repetitions, run order, and the alias structure for fractional designs.

A preview is not an approval and never writes settings to equipment. Engineers still declare control runs, objectives, stopping rules, and fallback plans; the preflight checklist shows repairable issues together, and the same server-side rules are applied when the experiment is created.

## Selecting the next experiment

Choose methods by scale and data conditions:

- Prefer classical DOE or response surfaces when factors are few and main effects or interactions must be estimated clearly.
- Use constrained candidate ranking or optimal design for discrete choices and complex field restrictions.
- Use Gaussian processes and Bayesian optimization when experiments are expensive, responses noisy, dimensions limited, and decisions sequential.
- Add physical features, feasible regions, or calibrated priors when mechanisms are known.
- Screen with engineering knowledge before optimization when variables greatly outnumber useful observations.

The system supports two intentions:

- **validate a hypothesis** by selecting conditions that distinguish candidate causes;
- **reach specification** by searching promising settings inside objectives and safety constraints.

## Current GP and Bayesian-optimization strategy

The current Optimizer targets expensive, noisy, small-data, multi-objective, constrained sequential experiments.

Quality depends on both settings and the trajectory the equipment actually realizes, so the current implementation supports a two-stage surrogate:

```text
control settings x
  ├─ GP₁: x → realized process features z
  └─ GP₂: [x, z] → quality objectives y and safety outcomes c
```

Training uses measured process features. A new setting first predicts process features and then evaluates quality and safety. Only features with sufficient shared coverage across valid observations enter the model.

Current objective strategies include:

- less-than or greater-than specifications;
- target values with tolerance;
- acceptable intervals;
- multiple objectives and weights;
- hard parameter bounds and linear constraints;
- modeled quality or safety outcome constraints.

Before calling Optimizer, Platform also selects active, conflict-free mechanism claims that match the project context. Hard constraints and multivariable forbidden combinations enter candidate feasibility, while soft constraints rank candidates; the input, claim versions, and knowledge-snapshot hash are frozen with the experiment. Active affine `mechanism-as-feature` definitions become declarative GP inputs with exact model and fusion versions and hashes. Bayesian priors, residual models, and the other output-fusion modes are not yet on the recommendation path.

The current reach-specification path jointly ranks the posterior probability that every objective and outcome constraint passes and the predicted distance from a regularized linear response surface, adapting the response-surface weight with leave-one-out error. The hypothesis-validation path instead prioritizes outcome uncertainty and identifiability in the key variables. The acquisition strategy and model version are regression-tested implementation choices, not product principles.

## Safety and cold start

Model constraints never replace equipment interlocks or engineering safety rules.

- Hard parameter bounds are checked before and after candidate generation.
- Safety-critical outcomes declare a minimum feasibility probability.
- A verified safe baseline is required before cold start with outcome constraints.
- Initial exploration stays in an approved candidate region or near the safe baseline.
- Engineers can reject recommendations and record unmodeled constraints.
- Recommendation failure, unavailable models, or excessive drift triggers an approved fallback.

Parameters in Planned, Approved, and Running experiments become pending points so the system does not repeat conditions whose outcomes are not yet known.

## Uncertainty and stopping

The system shows prediction intervals, feasibility, model version, and data scope. Intervals must be checked for coverage in replay and online results; a “95%” label does not make them calibrated.

Stopping may follow when:

- the objective is met and independently confirmed;
- expected improvement or information value falls below an approved threshold;
- no safe candidate remains;
- drift or model mismatch makes recommendations unreliable;
- the engineer judges further experimentation to cost more than its expected value.

## Role of the LLM

An LLM is suitable for:

- parsing engineer questions and page context;
- calling structured, authorized system tools;
- summarizing records, explaining statistical results, and drafting experiment text;
- clearly stating missing data and conclusion boundaries.

An LLM does not:

- generate numerical process settings directly;
- replace deterministic statistics or constraint calculations;
- invent facts absent from source records;
- promote candidate associations to definitive causes.

## Reproducibility

Optimizer remains free of business state. Variables, objectives, constraints, valid observations, pending points, strategy version, and random seed fully define each calculation. Platform preserves the input snapshot, content hash, output, approval record, and final result.

Historical replay reveals outcomes sequentially in time; future runs are never visible early. Every algorithm change is compared with the historical engineer sequence, applicable traditional DOE, and simple baselines.

Reach-specification experiments use fail-closed method admission: Optimizer produces a sequential recommendation only when the latest independently reviewed historical replay for the current policy, mechanism-knowledge snapshot, and mechanism-model snapshot has passed and the current optimizer model version actually appears in that replay trace. A missing or failing replay, or a model-version mismatch, pauses the method and explicitly downgrades to an applicable response surface or traditional DOE. Engineers may still design hypothesis-validation experiments because those do not claim that the sequential method saves trials. Controlled online experiments must additionally pass the stricter shadow-calibration, rollback-drill, and online-stop-signal gates.

### Public-data benchmark

`tools/public-validation` commits explicitly licensed FDM DOE and Crossed Barrel additive-manufacturing mechanical-design snapshots, SHA-256 verification, a fixed-seed replay runner, and automated tests. Discrete factors such as material, equipment type, tooling identity, formulation class, and structural column count are stratified as process context: one optimization campaign fits one explicit context, and category codes must not be treated as continuous values with a false distance relationship. Comparing multiple discrete levels requires stratified campaigns or an applicable factorial design.

The public benchmark separately reports workflow validation, superiority to model-free search, and noninferiority to a linear response surface. Passing the first does not pass the other two. Superiority requires a 95% confidence-interval lower bound of at least 10% for relative additional-trial reduction, with success-rate, context, and dataset guardrails. Because a linear response surface is itself an experiment-reduction method, it is checked against a frozen noninferiority margin; noninferiority is never described as superiority. See [Public-data experiment-efficiency validation](https://github.com/liuweichaox/Ingot/blob/main/tools/public-validation/README.en.md).

The committed reference result covers two datasets, 10 contexts, and 1,000 paired episodes. Relative to seeded random search, additional trials fall by 43.08% (95% CI 36.19%–49.86%), passing superiority. Relative to the linear response surface, the aggregate reduction is 13.53% (95% CI -9.41%–27.76%), but the Crossed Barrel result is -33.39% (95% CI -64.45%–-8.38%) and triggers the dataset guardrail. The overall experiment-reduction claim is therefore not demonstrated; aggregate noninferiority must not hide a per-dataset failure, and the result is not evidence of factory benefit.

That result is the v2 development regression. v3 uses NASA wind-tunnel airfoil-noise and Delft yacht-hydrodynamics physical experiments as external data not used during v2 development. It preregisters random search, maximin space filling, linear response surface, and quadratic response surface, and performs a paired ablation between the same optimizer with and without mechanism-derived features. The v3 protocol remains a draft, and a code gate blocks the full evaluation; there is no citable v3 result before freeze. Even a passing v3 result would establish only additional-result-query efficiency in the fixed public physical-experiment pools, not development-cycle reduction for an arbitrary factory.

## Current limitations

See [Mechanism knowledge design](mechanism-knowledge.en.md) for the current implementation, knowledge-absent degradation modes, and remaining fusion boundary.

- Internal chain validation from import to R&D observations has used controlled, non-public production history, while formal leakage-free replay and prospective online value validation remain incomplete.
- Real production data, parameter distributions, and derived results do not enter the public repository. Public tests use synthetic data or explicitly licensed, checksum-verified public data and must not be presented as real-project evidence.
- Prediction intervals still require continuous calibration on real projects.
- Physical features exist, while real-data-calibrated grey-box priors continue to evolve.
- Cross-product, equipment, and scenario transfer must wait for explicit applicability and second-scenario validation.
- High-dimensional, strongly drifting processes, slow quality feedback, or dominant unmeasured causes may not suit direct BO.
- Without controlled experiments, the system can help form cause candidates but cannot prove root cause.
