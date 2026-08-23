# v6 new-data selection record

> Status: selected from public metadata; `data.csv` has not been downloaded, none of the three outcome columns has been read, and no evaluation has been run.

## Selection time and purpose

This record follows v8 candidate commit `2911e9fd4fa7c527834a1dd18f3eb70c63aa5bb8` and v5 data-quality stop commit `82b0e52`. v6 evaluates the same frozen candidate without another algorithm change. It asks whether v8 method routing can pass all five gates against seeded random search, sequential maximin, a regularized linear response surface, a regularized quadratic response surface, and mechanism-feature ablation on previously uninspected physical formulation experiments.

## Selected data

### Olympus LNP3 lipid-nanoparticle formulation experiments

- Official repository: `https://github.com/the-matter-lab/olympus`
- Pinned source revision: `440b6b58ebfcaa2391cff7e94b570fb4fda98d68`
- Source file: `src/olympus/datasets/dataset_lnp3/data.csv`
- Metadata and parameter contract: `description.txt` and `config.json` in the same directory
- License: repository-root MIT License
- Metadata size: 768 CBD lipid-nanoparticle formulation settings
- Categorical context: `solid_lipid`, with three levels that must be evaluated separately and never encoded as continuous distance
- Numerical formulation controls: four levels each of drug input, solid-lipid input, liquid-lipid input, and surfactant input
- Published outcomes: drug loading, encapsulation efficiency, and particle diameter
- Sole primary outcome for this evaluation: particle diameter, evaluated in the lower direction; the other two columns are retained for integrity only and do not affect thresholds, features, or method choice

Particle diameter was selected before reading the CSV because the configuration declares it as one continuous minimization outcome with direct manufacturing meaning. v6 does not choose multi-objective weights or select whichever of the three outcomes is easiest to pass after inspection.

## Preregistered mechanism features

The following use formulation inputs only:

1. total lipid input: `solid_lipid_input + liquid_lipid_input`;
2. liquid-lipid fraction: `liquid_lipid_input / total_lipid_input`;
3. drug-to-lipid ratio: `drug_input / total_lipid_input`;
4. surfactant-to-lipid ratio: `surfractant_input / total_lipid_input`.

The pinned metadata gives a minimum solid-lipid input of 72, so the three ratios have no zero denominator. These are preregistered formulation structures, not claimed particle-size mechanisms; contribution must pass the paired with/without-feature ablation of the same optimizer.

## Data-quality stop conditions

Before any algorithm runs, the downloaded data must satisfy all of the following:

1. exactly 768 rows and eight columns, with every cell finite;
2. exactly the three declared `solid_lipid` levels and 256 rows per context;
3. a complete, unique 4×4×4×4 factorial grid of the numerical controls within every context;
4. all three outcome columns present, with particle diameter unambiguously identified;
5. no duplicate control combinations, missing outcomes, or control levels outside the configuration contract.

Any failure stops v6. Rows cannot be removed by outcome, contexts cannot be merged, the primary outcome cannot be changed, and evaluation units cannot be repartitioned.

## Draft evaluation protocol

- Each solid-lipid identity forms one independent evaluation unit. Olympus stores numerical inputs as discrete/categorical parameters with descriptors, but their options are ordered, physically dimensioned input quantities and are treated as numerical controls within each context.
- Replay can select only from the 256 real finite-pool formulations. It generates no off-grid interpolation and makes no continuous-space performance claim.
- Each unit uses the 15th particle-diameter percentile as its offline success threshold. This fixed-prevalence comparison device is not a drug-product specification.
- Each episode uses 12 unique initial observations and at most 12 additional queries. Every method shares the initial design, and candidate outcomes remain hidden until selected.
- Run 100 fixed-seed paired episodes per unit, for 300 episodes total.
- Comparators are fixed as seeded random search, sequential maximin, a regularized linear response surface, and a regularized quadratic response surface. Mechanism contribution is fixed as the paired ablation of the same v8 optimizer with and without the four features above.
- All five comparisons retain the strict v4 gates: the 95% CI lower bound for relative additional-trial reduction must exceed zero, the 95% CI lower bound for success-rate difference must be at least −5 percentage points, and none of the three solid-lipid evaluation units may be worse.
- Full evaluation may run only after one commit contains the data snapshot, converter, dependency lock, v8 candidate, and draft-protocol fingerprint, followed by a metadata-only freeze commit.

## Metadata-stage exclusions

- Olympus AgNP has only 164 settings and Electrochem only 30, so both fail the 500-row minimum.
- Olympus THF-500 has a continuous parameter contract, but `description.txt` is empty at the pinned revision; outcome meaning cannot be established without inventing provenance, so it is excluded.
- LNP3's three solid-lipid levels are never pooled to hide a failure. An aggregate cannot override a failing context.
