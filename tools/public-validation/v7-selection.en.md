# v7 new-data selection and preregistration record

> Status: selected from public metadata; none of the four `data.csv` files has been downloaded, no overpotential outcome has been read, and no data-quality check or evaluation has run.

## Selection time and question under decision

This record was created on 2026-08-23 after v10 candidate commit `93bd8cf7b9f388a9114449d77673fdd20c006d83`. The candidate's composition operators, method routing, and thresholds are already committed. Repository history contains none of the four OER datasets or their outcomes; selection used only official configurations and the paper's dataset description and row-count range.

v7 does not try to prove that one optimizer fits every process. It decides a preregistered method class: in expensive, sequential, six-component OER composition screening, can v10 outperform seeded random search, sequential maximin, a regularized linear response surface, and a regularized quadratic response surface, while also demonstrating contribution from preregistered composition descriptors against the no-feature version of the same method? Failure of any comparison or any composition plate leaves the overall conclusion as not demonstrated.

## Selected data

### Four Olympus high-throughput OER composition plates

- Official repository: `https://github.com/the-matter-lab/olympus`
- Pinned source revision: `440b6b58ebfcaa2391cff7e94b570fb4fda98d68`
- License: repository-root MIT License
- Physical source: four high-throughput oxygen-evolution-reaction catalyst composition screens
- Parameters: six non-negative elemental fractions summing to one; the published design contains unary, binary, ternary, and quaternary compositions on a 10 at% grid
- Outcome: OER overpotential, lower is better
- Metadata size: 2,119–2,121 measured compositions per plate

The four fixed evaluation units and source files are:

1. `oer_plate_3496`: Ni–Fe–Co–Mn–Ce–La, `src/olympus/datasets/dataset_oer_plate_3496/data.csv`;
2. `oer_plate_3851`: Ni–Fe–Co–Ta–Mn–Cu, `src/olympus/datasets/dataset_oer_plate_3851/data.csv`;
3. `oer_plate_3860`: Sn–Fe–Co–Ta–Mn–Cu, `src/olympus/datasets/dataset_oer_plate_3860/data.csv`;
4. `oer_plate_4098`: Sn–Sb–Co–Ca–Ni–Mn, `src/olympus/datasets/dataset_oer_plate_4098/data.csv`.

The plates must be evaluated separately. They are never pooled into one large sample, and an aggregate cannot override a failing plate. Evaluation selects only from the measured finite pools in the files; probabilistic emulator values for quinary and senary compositions described by the paper are outside this evaluation and claim.

## Preregistered composition descriptors

Each plate uses exactly three features built only from composition controls and elemental constants:

1. `mean_pauling_electronegativity`: composition-weighted mean Pauling electronegativity;
2. `pauling_electronegativity_dispersion`: composition-weighted population standard deviation of the same constants;
3. `first_row_transition_metal_fraction`: total fraction of Mn, Fe, Co, Ni, and Cu, with coefficients of zero for Ca, La, Ce, Ta, Sn, and Sb.

Fixed Pauling coefficients are: Ca 1.00, La 1.10, Ce 1.12, Ta 1.50, Mn 1.55, Fe 1.83, Co 1.88, Cu 1.90, Ni 1.91, Sn 1.96, and Sb 2.05. The frozen protocol expands coefficients, input order, and normalization for every plate; none may change after outcomes are read.

These features represent average electron-attracting tendency, elemental heterogeneity, and first-row-transition-metal content. They are outcome-blind chemical descriptors, not established mechanisms or causes of overpotential. Contribution must pass the paired with/without-feature ablation of the same v10 optimizer. If visible observations fail the capacity and leave-one-out gain gates, v10 must reject the features and the two trajectories should remain identical during that stage.

## Data-quality stop conditions

After download and before any optimization method runs, every plate must satisfy all of the following:

1. the six control columns declared by its official configuration and one unambiguous `overpotential` outcome column;
2. 2,119–2,121 data rows, with finite controls and outcomes;
3. every composition control within `[0, 1]` and every row sum within `1 ± 1e-8`;
4. every nonzero fraction on the `0.1 ± 1e-8` grid and at most four nonzero elements per row;
5. unique six-dimensional compositions, with no duplicate outcomes, missing outcomes, or ambiguous renaming outside the configuration contract;
6. all four plates passing and exact source-file hashes recorded in the protocol.

Any failure stops v7. Unfavorable rows cannot be removed, values cannot be imputed, missing compositions cannot be filled with an emulator, the outcome cannot be changed, plates cannot be narrowed, and evaluation units cannot be repartitioned.

## Draft frozen-evaluation protocol

- Each plate is one independent evaluation unit. Replay selects only from that measured finite pool, and candidate outcomes remain hidden until selected.
- Each unit uses its first overpotential percentile as the offline success threshold. This fixed-prevalence comparison device is not an industrial catalyst specification.
- Each episode starts with 24 unique observations and permits at most 24 additional queries. Every method shares the same initial design.
- Run 100 fixed-seed paired episodes per plate, for 400 episodes total. The initial sample clears the raw-dimensional GP capacity gate, while the three composition features must still pass the independent predictive-gain gate.
- The primary method is fixed as `ingot-v10-with-preregistered-composition-features`; the ablation is fixed as `ingot-v10-without-composition-features`.
- Comparators are seeded random search, sequential maximin, a regularized linear response surface, and a regularized quadratic response surface. Ridge values remain those already committed and are never tuned from outcomes.
- All five comparisons retain the original strict gates: the paired-bootstrap 95% CI lower bound for relative additional-trial reduction must exceed zero; the 95% CI lower bound for success-rate difference must be at least −5 percentage points; and the non-worse fraction across the four plates must be 100%.
- An unsuccessful episode counts as 25 additional queries. Bootstrap count is fixed at 5,000 with a seed fixed in the protocol.
- Full evaluation may run exactly once only after a commit contains the data snapshot, converter, dependency lock, candidate, and draft-protocol fingerprint, followed by a metadata-only freeze commit.

## Permitted and forbidden conclusions

If all five gates pass, the only permitted statement is that, on the four frozen measured OER finite composition pools, v10 required fewer additional result queries than every preregistered baseline to reach the fixed low-overpotential percentile from the same initial observations, and the preregistered composition descriptors passed paired ablation.

The result still cannot establish that:

- experiments are reduced for an arbitrary material, formulation, factory, or objective;
- finite-pool historical replay equals continuous-space, prospective-experiment, or production-cycle reduction;
- electronegativity or transition-metal fraction is a proven causal mechanism;
- a passing result permits deleting, overriding, or reinterpreting failed v3, v4, or v6 evidence;
- thresholds, budgets, features, baselines, evaluation units, or pass gates may change after this evaluation's outcomes are seen.
