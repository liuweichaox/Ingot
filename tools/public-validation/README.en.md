# Public-data experiment-efficiency validation

This directory turns “does the optimizer reduce additional experiments?” into reproducible paired historical-pool replay. Development regression and protocol-frozen evaluation use separate data and protocols, so data used during tuning does not continue to serve as external effect evidence. Every conclusion remains limited to the finite public candidate pools and is not extrapolated into a factory benefit.

| Evidence layer | Purpose | Data | Current state |
|---|---|---|---|
| v2 development regression | algorithm development, regression detection, and claim-boundary checks | FDM and concrete | complete reference result retained |
| v3 protocol-frozen evaluation | strong-baseline comparison and mechanism-feature ablation | NASA airfoil noise and Delft yacht hydrodynamics | draft protocol; code blocks a full run |

## Data and scenarios

The benchmark uses two CC BY 4.0 manufacturing datasets:

- FDM 3D printing, DOI `10.17632/zd6td6svd6.2`: 162 complete DOE records in six material-and-infill contexts; layer thickness, infill, and speed are controls, with roughness and peak stress as outcomes.
- UCI Concrete Compressive Strength, DOI `10.24432/C5PK67`: 1,030 source records aggregated into 996 unique mixtures and eight sufficiently populated curing-age contexts; seven ingredients are controls and compressive strength is the outcome.

Categorical factors are stratified as context and never encoded as continuous controls with false distance. Identical concrete age-and-mixture records are averaged while retaining replicate count and sample standard deviation, so repeated measurements do not masquerade as separate candidate experiments. See [NOTICE](NOTICE.md) for provenance, licenses, archive checksums, and transformations, and [protocol-v2.json](protocol-v2.json) for the frozen protocol. The loader verifies fixture hashes, row counts, unique identities, and replicate reconciliation before running.

## Compared methods

Every replay episode gives all three methods the same unique, seeded initial history:

1. the current Ingot production optimizer;
2. seeded model-free random search for a superiority test;
3. a regularized linear response surface as an active comparator for a noninferiority test.

An episode whose initial history already meets specification is excluded before any method runs. The endpoint therefore asks: “After observed history has not met specification, how many additional experiments are required?” Methods may query only real, unobserved settings in the public pool. They cannot inspect candidate outcomes, substitute nearest neighbors, interpolate outcomes, or synthesize results.

FDM uses four initial observations and a total budget of 12; concrete uses eight and 16. Both allow at most eight additional trials, and a failure is scored as nine to avoid survivor bias. The reference run uses 100 unique episodes in each of 14 contexts, or 1,400 paired replays.

## Decision rules

- Versus random search: the hierarchical-bootstrap 95% confidence-interval lower bound for relative additional-trial reduction must be at least 10%; the success-rate difference lower bound must be at least -5 percentage points; and at least 80% of contexts must be non-worse.
- Versus the linear response surface: the reduction CI lower bound must be at least -10% and the success-rate difference lower bound at least -5 percentage points. This establishes noninferiority, not superiority.
- Dataset guardrails prevent either dataset from hiding an average regression beyond the frozen margins.

Random search represents experimentation without a response model. A linear response surface is already a classical experiment-reduction method. The public claim must therefore say “reduced versus seeded random search,” never “better than every DOE/RSM method.”

## Current reference result

[latest-results.json](latest-results.json) is the complete reference snapshot:

| Check | Result |
|---|---|
| Data and workflow validation | Passed; 2 datasets, 14 contexts, 1,400 episodes |
| Ingot / random / linear-response success | 98.71% / 80.79% / 92.86% |
| Ingot / random / linear-response capped mean additional trials | 2.26 / 4.47 / 2.69 |
| Reduction versus random search | 49.45%, 95% CI 42.40%–56.01%; superiority passed |
| Reduction versus linear response surface | 16.03%, 95% CI -1.15%–27.44%; noninferiority passed, superiority not demonstrated |
| Permitted public conclusion | **Passed only versus seeded random search** (`passed-vs-seeded-random-search`) |

This is a development-stage public benchmark, not preregistered independent external validation. It shows that this version significantly reduces model-free additional experiments in these two fixed public pools and is noninferior overall to the linear response surface. It does not prove the same savings for another factory, material, machine, or objective.

## v3 protocol-frozen evaluation

[`protocol-v3.json`](protocol-v3.json) introduces two CC BY 4.0 physical-experiment datasets that were not used for v2 development: 1,503 NASA wind-tunnel airfoil-noise experiments and 308 Delft yacht-hydrodynamics experiments. The evaluator derives a common comparison target through a preregistered quantile rule. This target is an evaluation device, not a field engineering specification.

Every episode shares the same unique seeded initial observations and compares:

1. Ingot with preregistered mechanism-derived features;
2. the same Ingot optimizer without those features;
3. seeded random search;
4. sequential maximin space filling;
5. a regularized linear response surface; and
6. a regularized quadratic response surface.

A method may read candidate parameters but receives an outcome only after selecting that setting. The primary endpoint is capped additional trials to target, with budgeted success rate as a guardrail. A statement of reduction against all registered baselines requires superiority to every one of the four baselines. Mechanism-feature contribution requires the paired ablation against the same optimizer without those features; the overall optimizer result cannot substitute for that test.

The result retains per-dataset summaries and the actual runtime versions. Confidence intervals resample episodes within each of the two fixed datasets. They describe episode uncertainty in those datasets and do not estimate transfer uncertainty across processes, equipment, or factories.

v3 currently has `draft` status, and the full evaluator refuses to run. The freeze procedure is:

1. commit the algorithm, data snapshots, conversion script, and draft protocol;
2. obtain `candidate_evaluation_fingerprint` from the integrity check; in a following metadata-only commit, record the preceding commit in `optimizer_revision` and `protocol_revision`, copy that fingerprint into `evaluation_fingerprint`, and change the status to `frozen`;
3. run the complete evaluation without changing the algorithm, data, target, budget, baselines, or decision thresholds in response to the result; and
4. retain an unfavorable result and publish `not-demonstrated` where applicable.

Because public outcome columns are not objectively inaccessible to maintainers, v3 is described as a protocol-frozen external-data evaluation with sequentially hidden outcomes, not an independent blind test. Independent third-party evaluation remains a higher evidence level.

## Run and automation

Run the complete benchmark:

```bash
./scripts/benchmark-public-validation.sh
```

The default output is `artifacts/public-validation.json`. For a fast check:

```bash
uvx --from uv==0.11.32 uv run --project optimizer --locked \
  python tools/public-validation/benchmark_v2.py \
  --episodes 2 --max-scenarios 2 --bootstrap-samples 100
```

- Ordinary CI verifies fixtures, protocol, claim boundaries, and a fast paired replay without pinning stochastic floating-point scores across platforms.
- The `Performance` workflow runs the full benchmark weekly or on demand and uploads the result.
- `latest-results.json` changes only after human review of an algorithm, dataset, dependency lock, or decision-protocol change.

Verify the v3 data and draft protocol without running effect evaluation:

```bash
./scripts/verify-public-validation-v3.sh
```

The integrity fingerprint covers optimizer source, the dependency lock, evaluator, normalized protocol, and both data snapshots. A change to any of them blocks the full evaluation. After protocol freeze, run v3 with:

```bash
./scripts/benchmark-public-validation-v3.sh
```

When refreshing the reference result, retain every context and failure, run the complete benchmark first, and update Chinese and English documentation together. A real deployment still needs in-factory local-history replay and a small controlled transfer test; factory data does not need to leave the site.
