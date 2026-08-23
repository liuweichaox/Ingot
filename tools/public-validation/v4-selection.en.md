# v4 new-data selection record

> Status: selected from public metadata; data files have not been downloaded, outcome columns have not been read, and no evaluation has been run.

## Selection time and purpose

This record is created after candidate commit `9cc4ea0` and before downloading v4 data. v4 asks one question: on new public engineering data not used to tune v7, can evidence-gated method admission pass the random-search, maximin, linear-response, quadratic-response, and mechanism-feature-ablation gates together?

## Preregistered inclusion criteria

A dataset must satisfy all of the following:

1. An official repository states CC BY 4.0 or a more permissive license and a stable DOI.
2. The data describe physical engineering experiments or engineering simulation with explicit physical meaning.
3. At least 500 settings provide numerical parameters and a continuous result.
4. Parameters can be interpreted as design or operating settings, not sensor features extracted after the outcome.
5. The data were not used in v2, v3, or v7 candidate development.
6. Evaluation units can be formed without disguising category identifiers as continuous variables.

## Selected data

### UCI Energy Efficiency

- DOI: `10.24432/C51307`
- Official record: `https://archive.ics.uci.edu/dataset/242/energy+efficiency`
- License: CC BY 4.0
- Metadata size: 768 Ecotect building-energy simulation settings
- Continuous controls: relative compactness, surface area, wall area, roof area, overall height, and glazing area
- Categorical context: orientation and glazing distribution; both must be stratified rather than treated as continuous controls
- Outcome: heating load, evaluated in the lower direction
- Preregistered mechanism features: envelope area (wall area + roof area), glazing-exposure proxy (surface area × glazing area), and compactness-height interaction (relative compactness × overall height)

### UCI Synchronous Machine

- DOI: `10.24432/C5W32R`
- Official record: `https://archive.ics.uci.edu/dataset/607/synchronous+machine+data+set`
- License: CC BY 4.0
- Metadata size: 557 real experimental operating settings
- Numerical controls: load current, power factor, power-factor error, and excitation-current change
- Outcome: excitation current, evaluated in the lower direction
- Preregistered mechanism features: active-load proxy (load current × power factor), error-correction interaction (power-factor error × excitation-current change), and correction per load (excitation-current change ÷ load current)

The quantile targets are evaluation devices for comparing methods. They do not claim that lower excitation current or minimum heating load is a complete engineering objective under every real constraint.

## Draft evaluation contract

- Each evaluation unit uses the 15th outcome percentile as its offline target.
- Initial observations and additional budget will be fixed after download using control-row counts only and cannot be adjusted from algorithm results.
- Every method shares the same initial design, and candidate outcomes remain hidden until selected.
- Comparators remain seeded random search, sequential maximin, a regularized linear response surface, and a regularized quadratic response surface.
- Mechanism contribution remains a paired ablation of the same optimizer with and without the preregistered derived features above.
- All five paired comparisons retain the strict v3 gates: the 95% CI lower bound for relative additional-trial reduction must exceed zero, the success-rate-difference lower bound must be at least -5 percentage points, and no evaluation unit may be worse.
- The full evaluation may run only after data snapshots, adapters, dependency lock, candidate algorithm, and protocol fingerprint are frozen together.

## Metadata-stage exclusion

UCI Servo has a clear license and engineering meaning, but two mechanical-linkage inputs are categorical and each context has a small candidate pool. The current continuous-control evaluator cannot form sufficiently stable independent evaluation units without disguising category codes as continuous variables. It is therefore excluded before outcome inspection and is not part of v4.
