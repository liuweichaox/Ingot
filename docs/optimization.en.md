# Analysis and optimization

> Status: **current scientific strategy**. This document explains how methods are selected by the engineering question and describes the limits of today's numerical implementation. Algorithms may evolve without changing the core value or evidence principles.

This document defines the scientific and implementation boundaries for analysis admission, run comparison, real-recipe-run optimization, candidate-cause validation, and sequential optimization. Numerical implementations, replay protocols, and method-promotion conditions must be independently reviewable by developers and scientific-method reviewers.

## Analysis workflow overview

Ingot executes analysis in the following order:

1. Confirm that conditions, process data, and quality outcomes belong to the same real run.
2. Compare eligible runs and identify both differences and evidence gaps.
3. Turn admitted real recipe runs directly into optimization observations without requiring a separately created experiment.
4. Recommend the next recipe inside safety boundaries and observed coverage; create controlled validation only for causal confirmation or extrapolation.

The system does not prefer a method merely because it is more complex, and it does not turn historical correlation into a confirmed cause.

## Method-selection principle

Ingot starts from the engineer's decision, not from an algorithm:

| Engineering question | Preferred methods | Required output |
|---|---|---|
| Can the data be used? | completeness, units, time, provenance, and drift checks | admitted or excluded with a specific reason |
| Where did this run differ? | like-for-like matching, robust statistics, stage-trajectory comparison | size of the difference, uncertainty, and comparison group |
| Which factors deserve validation? | screening, grouped comparison, and stability checks | candidate causes, counterevidence, factors that changed together, and data limits |
| Is a factor causal? | controls, repetition, blocking, randomization, or intervention | supported, rejected, or inconclusive |
| What should the next recipe be? | response surfaces, constrained candidate ranking, or Bayesian optimization | candidate recipe, prediction interval, risk, coverage, and rationale |
| Is controlled validation required? | design of experiments (DOE), repetition, blocking, or intervention | support, rejection, or uncertainty plus executable validation conditions |
| How should it be explained? | fixed result templates plus language-model assistance | readable explanation with source citations |

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

## Diagnostic readiness and confounder disclosure

The system uses `readiness.mode` to state the strongest analysis the current evidence supports:

- `descriptive-only`: show coverage, missingness, and trends, but do not generate candidate causes;
- `exploratory`: show an early candidate ranking whose stability is still limited;
- `candidate-ranking`: the ranking has passed out-of-sample and stability checks, but it is still not a confirmed cause.

At every level, candidate causes still require controlled repeated validation before promotion to a causal conclusion. The response also lists conditions that changed at the same time, group imbalances, and known influences that were not measured so they are not mistaken for confirmed causes.

The API keeps a stricter statistical boundary: the first release fixes `sensitivityAssessment.status` at `not-estimable`. Current outputs are standardized coefficients or model importance, not risk-ratio estimates with the required confidence interval, so the product does not calculate an invalid E-value. Metrics record readiness modes and structured blocking reasons, never user-entered free text as labels.

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
6. Create separate controlled validation for important candidates that require causal confirmation.

A model cannot separate factors that never overlap. If every mold A run uses only material A, history alone cannot distinguish mold effect from material effect.

## Recipe runs and optimization observations

Every normal production recipe run is a candidate optimization sample, but it enters the model only when run boundaries, actual parameters, required process context, and valid quality outcomes are all available. An optimization task filters by product, equipment, and declared context without locking the scope to one process-specification version, so different recipes within the same optimization scope can be compared. Normal production runs remain production runs and require no engineer reclassification; excluded runs and their reasons remain visible.

At least three valid runs and two distinct actual recipes are required before a next-recipe recommendation is generated. A recommendation stays inside safety boundaries and the observed parameter envelope, includes prediction intervals and evidence scope, and requires engineer confirmation through the normal production flow. It is stored as an independent append-only record with no experiment identifier, experiment run plan, approval state, or equipment-dispatch command. New real runs change the frozen input snapshot and allow a new candidate; old recommendations do not contaminate the next optimization round as pending experiments.

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

When controlled validation is needed, an optimization task can generate an editable run plan for full factorial, fractional factorial, central composite (CCD), Box–Behnken, and Latin-hypercube designs. The generator uses only declared controllable variables with approved ranges, fixes a random seed, and returns blocks, repetitions, run order, and the alias structure for fractional designs.

A preview is not an approval and never writes settings to equipment. Engineers still declare control runs, objectives, stopping rules, and fallback plans; the preflight checklist shows repairable issues together, and the same server-side rules are applied when the experiment is created.

## Selecting the next recipe

Choose methods by scale and data conditions:

- Prefer classical DOE or response surfaces when factors are few and main effects or interactions must be estimated clearly.
- Use constrained candidate ranking or optimal design for discrete choices and complex field restrictions.
- Use Gaussian processes and Bayesian optimization when experiments are expensive, responses noisy, dimensions limited, and decisions sequential.
- Add physical features, feasible regions, or calibrated priors when mechanisms are known.
- Screen with engineering knowledge before optimization when variables greatly outnumber useful observations.

The system supports two intentions:

- **continuously optimize recipes** by searching real recipe runs for a more promising next setting that remains inside observed coverage;
- **validate a hypothesis** by selecting controlled conditions that distinguish candidate causes when an engineer explicitly needs causal confirmation.

## Current GP and Bayesian-optimization strategy

The current Optimizer targets expensive, noisy, small-data, multi-objective, constrained recipe optimization. Formal controlled validation reuses the same numerical kernel while retaining a separate approval boundary.

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

The current reach-specification path first uses the GP posterior to enforce outcome-safety probability thresholds, then selects a method from visible observations. A regularized linear response surface is the default. Once minimum capacity is available, paired leave-one-out predictions compare normalized target-ranking error for the linear and quadratic surfaces. Quadratic is admitted only when its improvement exceeds one standard error across three consecutive expanding histories; inconclusive evidence keeps the simpler linear surface rather than deciding from dimension, univariate correlation, or a fixed mixture weight. Information-first model discrimination belongs only to the engineer-selected hypothesis-validation path. GP posterior specification probability takes over only after nonlinear evidence is established and at least six visible observations per raw control are available. Declared mechanism features must pass capacity and paired predictive evidence; failure removes them from the surrogate. Method selection reads revealed observations and candidate controls only, never candidate outcomes or dataset names. The GP continues to supply objective predictions, intervals, and safety probabilities. The hypothesis-validation path instead prioritizes outcome uncertainty and identifiability in the key variables. Thresholds, regularization values, and the model version are implementation choices that require fresh replay and frozen validation, not product principles.

## Safety and cold start

Model constraints never replace equipment interlocks or engineering safety rules.

- Hard parameter bounds are checked before and after candidate generation.
- Safety-critical outcomes declare a minimum feasibility probability.
- A verified safe baseline is required before cold start with outcome constraints.
- Initial exploration stays in an approved candidate region or near the safe baseline.
- Engineers can reject recommendations and record unmodeled constraints.
- Recommendation failure, unavailable models, or excessive drift triggers an approved fallback.

Only pending conditions from formal controlled validation become pending points, so unknown validation outcomes are not scheduled twice. Daily recipe recommendations do not enter the experiment pending queue: the same frozen input returns the same recommendation, and a new recommendation appears only after new production evidence arrives.

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

Formal sequential validation uses fail-closed method admission: the newest historical replay for the current policy, mechanism-knowledge snapshot, and mechanism-model snapshot must have been independently reviewed and passed, and the current optimizer model version must appear in that replay before the system claims that the method can save validation runs. A daily next-recipe recommendation makes no such efficiency claim: it uses admitted real runs only, remains inside safety boundaries and the observed envelope, and always requires engineer confirmation. Controlled online validation additionally requires stricter shadow-calibration, rollback-drill, and online-stop-signal gates.

### Public-data benchmark

`tools/public-validation` commits explicitly licensed FDM DOE and Crossed Barrel additive-manufacturing mechanical-design snapshots, SHA-256 verification, a fixed-seed replay runner, and automated tests. Discrete factors such as material, equipment type, tooling identity, formulation class, and structural column count are stratified as process context: one optimization campaign fits one explicit context, and category codes must not be treated as continuous values with a false distance relationship. Comparing multiple discrete levels requires stratified campaigns or an applicable factorial design.

The public benchmark asks only how many additional experiments are needed to reach the same target. Random search and maximin represent selection without a response model and use superiority tests. Linear and quadratic response surfaces are already effective experiment-reduction methods and use noninferiority guardrails. An aggregate result cannot hide a material dataset regression. See [Optimizer experiment-efficiency validation](https://github.com/liuweichaox/Ingot/blob/main/tools/public-validation/README.en.md) for execution and complete decision rules.

Public acceptance no longer exposes internal round numbers that users must decode. It has three states only: development regression, unseen-data acceptance, and real pilot. Historical protocols, trajectories, and failed results remain available for audit but do not define the current policy's effect claim.

The previous frozen policy completed 450 paired episodes on Alkox enzyme catalysis, P3HT conductive formulations, and an HPLC injection process after data selection and protocol freeze. It reduced additional experiments by 49.24% versus random search and 62.96% versus maximin, passing confidence and all three dataset guardrails. It reduced 12.02% versus the linear response surface in aggregate, but Alkox and P3HT were −32.08% and −68.36%, triggering subgroup failure. It was 0% versus the quadratic response surface, with identical trajectories on every dataset.

This frozen result shows that the previous policy reliably beat blind exploration, but its routing collapsed to a fixed quadratic response surface and could not recognize data better served by a linear surface. Core acceptance therefore failed. The current successor addresses only how to distinguish linear from quadratic structure from revealed observations: linear by default, stable target-ranking evidence before quadratic admission, and the simpler model when evidence is inconclusive. The algorithm change invalidates the old frozen fingerprint; successor effectiveness requires another fresh-data decision.

Mechanism-feature contribution and core experiment selection are decided separately. A feature without contribution must be disabled; that neither erases a core result on raw controls nor supports a mechanism claim. Public-data conclusions always describe additional-result-query efficiency within fixed experimental pools, not development-cycle savings for an arbitrary factory.

## Current limitations

See [Mechanism knowledge design](mechanism-knowledge.en.md) for the current implementation, knowledge-absent degradation modes, and remaining fusion boundary.

- Internal chain validation from import to R&D observations has used controlled, non-public production history, while formal leakage-free replay and prospective online value validation remain incomplete.
- Real production data, parameter distributions, and derived results do not enter the public repository. Public tests use synthetic data or explicitly licensed, checksum-verified public data and must not be presented as real-project evidence.
- Prediction intervals still require continuous calibration on real projects.
- Physical features exist, while real-data-calibrated grey-box priors continue to evolve.
- Cross-product, equipment, and scenario transfer must wait for explicit applicability and second-scenario validation.
- High-dimensional, strongly drifting processes, slow quality feedback, or dominant unmeasured causes may not suit direct BO.
- Without controlled experiments, the system can help form cause candidates but cannot prove root cause.
