# Fresh-data acceptance record for the current optimizer

Recorded: 2026-08-23

## Question

This acceptance answers one product question: when a small observed history has not reached its target, can the current Ingot policy use clearly fewer additional experiments than random search and maximin while avoiding material regression against applicable linear or quadratic response surfaces?

Mechanism features are outside this decision. Earlier public ablations found no stable contribution and the current policy disables them. Keeping that claim attached to core acceptance would confuse whether experiment selection works with whether one feature set helps.

## Outcome-inspection boundary

Before this record was committed, inspection was limited to `config.json`, file existence, and line counts at Olympus revision `440b6b58ebfcaa2391cff7e94b570fb4fda98d68`. No data row, outcome distribution, or model result was parsed. None of the three selected datasets previously appeared in Ingot development, regression, or frozen evaluation.

Selection criteria were fixed before outcomes were read:

- experimentally derived data, not an analytical function or trained emulator;
- continuous controls and one continuous outcome, matching the current optimizer boundary;
- coverage of at least two of reaction, formulation, and equipment-process work;
- at least one problem above three dimensions, testing the current rule that disables Euclidean maximin fallback in higher dimensions;
- at least 150 source rows and at least 80 unique settings after exact-setting aggregation;
- pinned source revision and SHA-256 checks for both source snapshots and normalized fixtures.

## Fixed data

| Dataset | Controls | Outcome direction | Metadata-only rows | Selection rationale |
|---|---|---|---:|---|
| Alkox | catalase, peroxidase, alcohol oxidase, and pH | maximize conversion | 208 | four-dimensional enzyme-catalysis reaction testing response routing with limited observations |
| P3HT | P3HT and four dopant-component contents | maximize conductivity | 178 | five-dimensional materials formulation testing generalization without Euclidean fallback |
| HPLC | sample loop, additional volume, tubing volume, flow, push speed, and wait time | maximize peak area | 1,386 | six-dimensional equipment process testing a larger candidate pool and higher-dimensional controls |

All are pinned to the Olympus revision above under its MIT license. Import may only add stable setting identifiers and average exact duplicate control settings while retaining replicate count and sample standard deviation. No adverse outcome may be deleted, imputed, or replaced.

Acceptance stops if columns or control ranges conflict with official configuration, values are non-finite, or any dataset retains fewer than 80 unique settings. A failed dataset cannot be replaced after outcomes are read.

## Fixed targets and budgets

Each dataset uses the empirical 90th percentile of its complete candidate-pool outcome as the target. This is a fixed-prevalence comparison device, not a field specification.

| Dataset | Initial observations | Additional budget | Capped failure score | Paired episodes |
|---|---:|---:|---:|---:|
| Alkox | 10 | 12 | 13 | 150 |
| P3HT | 15 | 12 | 13 | 150 |
| HPLC | 18 | 12 | 13 | 150 |

Every initial history must be random, unique, unsuccessful, and shared by all methods. A candidate outcome is revealed only after a method selects it. The complete decision contains 450 paired episodes.

## Fixed comparators and decision

The current policy is compared with:

1. seeded random search;
2. sequential maximin space filling;
3. a regularized linear response surface;
4. a regularized quadratic response surface.

The primary endpoint is capped additional experiments to target; success rate is also reported. Confidence intervals use 5,000 bootstraps stratified within the three fixed datasets.

Core selection passes only if all conditions hold:

- versus random and maximin: the 95% CI lower bound for relative trial reduction is at least 10%, the success-rate-difference lower bound is at least −5 percentage points, and no dataset degrades by more than 10%;
- versus linear and quadratic surfaces: the trial-reduction lower bound is at least −10%, the success-rate-difference lower bound is at least −5 percentage points, and no dataset degrades by more than 10%;
- the 95% CI lower bound is above zero against at least one response-surface baseline;
- data, trajectory, and execution integrity all pass.

## Freeze and stop rule

This record is committed first. Outcome import and a generic adapter may begin only afterward. Source snapshots, normalized fixtures, adapter, dependency lock, and draft protocol are committed again. A final freeze may then add only the preceding commit identifiers and the unified evaluation fingerprint. Full acceptance runs only after that freeze.

Targets, budgets, baselines, gates, and data cannot change after execution. A pass advances to real-history replay. A failure determines whether to repair the method, narrow applicability, or stop leading with experiment reduction. The complete failed result must be retained.
