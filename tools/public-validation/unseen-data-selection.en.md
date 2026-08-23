# Unseen-data acceptance selection record

Record date: 2026-08-23

## Acceptance question

This acceptance asks only whether, after a small observed history has not reached its target, the current Ingot method-selection policy clearly reduces additional experiments versus model-free selection without materially trailing an applicable linear or quadratic response surface.

Mechanism-feature contribution is decided separately. A failure disables the features but does not override the core experiment-selection result.

## Selection process

Before this record was committed, selection inspected only `config.json`, descriptions, file listings, and row-count metadata in the official Olympus repository. Candidate outcome columns were not parsed and no method was run. Neither selected dataset appears in Ingot's history or current tree.

Pinned source: Olympus commit `440b6b58ebfcaa2391cff7e94b570fb4fda98d68`, MIT license.

The selection criteria were fixed before reading outcomes:

- physical or chemical experiments, with no analytic function or trained emulator used as the result source;
- continuous controllable variables and one scalar outcome, permitting a fair comparison of the production optimizer and all four baselines;
- at least 200 experiments declared by official metadata, sufficient for unique initial designs;
- clear process meaning that permits one conservative low-dimensional mechanism interaction to be declared without outcomes;
- no use in any previous Ingot development, regression, or frozen evaluation.

## Fixed data

| Dataset | Controls | Outcome and direction | Official size | Rationale |
|---|---|---|---:|---|
| Buckminsterfullerene adducts | reaction time, sultine/C60 ratio, temperature | desired-product mole fraction, maximize | 246 | three-factor full-factorial flow-reaction experiments with direct continuous process settings |
| Suzuki reaction | temperature, Pd catalyst loading, ArBpin equivalents, K3PO4 equivalents | yield, maximize | 247 | four continuous reaction conditions directly representing formulation and reaction-process optimization |

After import, provenance revision, raw-file SHA-256, finite values, control bounds, and unique settings must pass. Exact duplicate control settings may only be aggregated as experimental replicates by mean outcome while retaining replicate count. If either dataset has fewer than 150 unique settings or contains unexplained columns or range conflicts, the data-quality gate stops acceptance; outcomes may not be replaced and another dataset may not be substituted ad hoc.

## Fixed target and budget

Each dataset uses the empirical 90th percentile of its complete candidate-pool outcome as the reach-specification threshold. This is a fixed-prevalence comparison device, not a field engineering specification.

| Dataset | Initial observations | Maximum additions | Failure score | Episodes |
|---|---:|---:|---:|---:|
| Buckminsterfullerene adducts | 10 | 12 | 13 | 200 |
| Suzuki reaction | 15 | 12 | 13 | 200 |

Initial histories must be random, unique, not already successful, and shared by every method. A candidate outcome is revealed only after a method selects that setting. Each dataset contributes 200 unique initial designs, for 400 paired episodes.

## Fixed methods

The core policy is compared with:

1. seeded random search;
2. sequential maximin space filling;
3. a regularized linear response surface;
4. a regularized quadratic response surface.

The same policy with mechanism features disabled is an additional paired ablation. The only preregistered features are:

- Buckminsterfullerene: `reagent_contact_exposure = reaction_time × sultine`;
- Suzuki: `catalyst_temperature_exposure = pd_mol × temperature`.

These are low-dimensional process interactions, not claims of validated mechanisms. Admission uses the current policy's capacity and leave-one-out predictive-gain gates on revealed observations only.

## Fixed decisions

The primary endpoint is capped additional experiments to target, with within-budget success also reported. Confidence intervals use 5,000 episode bootstrap samples stratified within the two fixed datasets.

Core experiment selection passes only if all conditions hold:

- versus random and maximin: the 95% CI lower bound for relative additional-trial reduction is at least 10%, the success-rate-difference lower bound is at least −5 percentage points, and neither dataset is worse;
- versus linear and quadratic response surfaces: the reduction CI lower bound is at least −10%, the success-rate-difference lower bound is at least −5 percentage points, and neither dataset exceeds the 10% degradation margin;
- the reduction CI lower bound is above zero for at least one of the linear or quadratic response surfaces, establishing value beyond merely repackaging a baseline;
- every data and execution integrity check passes.

Mechanism-feature contribution passes only when the reduction CI lower bound versus the no-feature policy is above zero, the success-rate-difference lower bound is at least −5 percentage points, and neither dataset is worse. Its failure does not change the core decision, but it must be reported and the corresponding features must remain disabled.

## Freeze and stop rules

Outcome columns and a deterministic adapter may be imported only after this record is committed. The data snapshots, adapter, dependency lock, and draft protocol must then be committed together. A metadata-only commit may subsequently fill the preceding revision and unified evaluation fingerprint and change the protocol from `draft` to `frozen`. The complete 400-episode acceptance may run only after freeze.

Targets, budgets, baselines, mechanism features, and gates may not change after results are run. Every failure is retained in full. A successor algorithm may use these outcomes only as development regression and must use another unseen dataset group for new acceptance.
