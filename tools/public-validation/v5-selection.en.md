# v5 new-data selection record

> Status: stopped at the data-quality gate; no protocol was created and no algorithm evaluation was run.

## Post-download data-quality decision

After selection commit `f18e93b`, the `data.csv` downloaded from the pinned source revision had SHA-256 `9c94222798229c1391f75445f44d9c0ed285e83c1b1e0608ab76b28bf05decef`. The headerless file contains 1,386 rows of seven finite numeric values. Reconciliation by the exact text of the first six control values leaves 1,007 unique settings: 628 occur once and 379 occur twice. Of the repeated settings, 326 have different outcomes across their two measurements, confirming that they are replicates rather than identical rows that could be silently discarded.

Following the preregistration, repeated settings were first treated as one candidate. SHA-256 of the six controls in `.17g` canonical form modulo five then produced evaluation-unit sizes 204, 197, 215, 196, and 195. Four units fall below the preregistered 200-row minimum, so v5 stopped before any replay or algorithm comparison. No outcome-driven fold or gate change was made. The HPLC outcome column is development evidence from this point onward and cannot be repackaged as a fresh external validation.

## Selection time and purpose

This record follows v8 candidate commit `2911e9fd4fa7c527834a1dd18f3eb70c63aa5bb8`. v5 asks one question: on previously uninspected automated physical-experiment data that did not participate in v8 development, can evidence-gated method routing pass all five gates against seeded random search, sequential maximin, a regularized linear response surface, a regularized quadratic response surface, and mechanism-feature ablation?

## Preregistered inclusion criteria

The data must satisfy all of the following:

1. The official repository gives the selected fixed revision an MIT, Apache-2.0, CC BY 4.0, or more permissive license.
2. The data come from a physical engineering/laboratory process or engineering simulation with explicit physical meaning.
3. At least 500 settings provide numerical controllable parameters and a continuous result.
4. Inputs are set before the experiment, not sensor features extracted after the outcome.
5. The data were not used in v2, v3, v4, or v8 candidate development.
6. Provenance, identity, license, row count, and transformations can be pinned and checked with SHA-256.
7. Category identifiers are not disguised as continuous distance, and repeated measurements do not masquerade as independent candidate settings.

## Selected data

### Olympus automated HPLC process experiments

- Official repository: `https://github.com/the-matter-lab/olympus`
- Pinned source revision: `440b6b58ebfcaa2391cff7e94b570fb4fda98d68`
- Source file: `src/olympus/datasets/dataset_hplc/data.csv`
- Metadata and parameter contract: `description.txt` and `config.json` in the same directory
- License: repository-root MIT License
- Associated method paper: ChemOS, `10.26434/chemrxiv.5953606.v1`
- Metadata size: 1,386 automated HPLC process settings
- Continuous controls: sample-loop volume, additional volume, tubing volume, sample flow, push speed, and wait time
- Outcome: peak response/peak area, evaluated in the higher direction

The summary target label in `description.txt` is inconsistent with the `peak_area` name in `config.json`. The converter must use the parameter contract's `peak_area` column and the dataset's peak-response description. If the pinned CSV does not contain one unambiguous matching column, contains unexplained additional outcome columns, or conflicts with the contracted ranges, v5 must stop before protocol freeze rather than selecting a different outcome ad hoc.

## Preregistered mechanism features

The following features use controls and dimensional relationships only and will not be adjusted from outcomes:

1. total draw volume: `sample_loop + additional_volume`;
2. tubing residence-time proxy: `tubing_volume / sample_flow`;
3. sample share of draw volume: `sample_loop / total_draw_volume`, rejected under the existing derived-feature safety rule if the denominator is zero;
4. push-exposure proxy: `push_speed × wait_time`.

These are hypothesized structural terms, not established HPLC mechanisms. Their experiment-efficiency contribution is decided by the paired with/without-feature ablation.

## Evaluation units and draft contract

- Use every unique numeric setting from the pinned revision; repeated control combinations must be reconciled rather than silently retained.
- In an outcome-blind conversion step, join the normalized six-control tuple in a fixed decimal representation, compute SHA-256, and take the integer hash modulo five to form five deterministic evaluation units.
- The converter must verify that every unit contains at least 200 rows or stop.
- Each evaluation unit uses its 85th outcome percentile as the offline success threshold. This fixed-prevalence comparison device is not an HPLC engineering specification.
- Each episode uses eight unique initial observations and at most twelve additional queries. All methods share the initial design, and candidate outcomes stay hidden until selected.
- Run 100 fixed-seed paired episodes per unit, for 500 episodes total.
- Comparators are fixed as seeded random search, sequential maximin, a regularized linear response surface, and a regularized quadratic response surface. Mechanism contribution is the paired ablation of the same v8 optimizer with and without the four features above.
- All five comparisons retain the strict v4 gates: the 95% CI lower bound for relative additional-trial reduction must exceed zero, the 95% CI lower bound for success-rate difference must be at least −5 percentage points, and none of the five evaluation units may be worse.
- After download, only license, structure, missingness, duplicates, ranges, and unit size may determine stop/continue. Algorithm results cannot change folds, target quantile, initial design size, budget, baselines, features, or gates.
- Full evaluation may run only after one commit contains the data snapshot, converter, dependency lock, v8 candidate, and draft-protocol fingerprint, followed by a metadata-only freeze commit.

## Metadata-stage exclusions

- UCI Wave Energy Converters has 288,000 engineering-simulation results and a clear license, but its public candidate pool comes from trajectories of several existing evolutionary algorithms rather than a neutral design grid. Direct historical-pool comparison would inherit that selection bias, so it is excluded before outcome inspection.
- Olympus Thin Film is a relevant physical materials experiment but has only 94 settings and fails the preregistered minimum size.
- Observational regression data such as combined-cycle power-plant records include ambient state or outcome-side sensors that cannot all be interpreted as pre-experiment controls, so they are not used for this experiment-reduction validation.
