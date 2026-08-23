# Public-data experiment-efficiency validation

This directory turns “does the optimizer reduce additional experiments?” into reproducible paired historical-pool replay. Development regression and protocol-frozen evaluation use separate data and protocols, so data used during tuning does not continue to serve as external effect evidence. Every conclusion remains limited to the finite public candidate pools and is not extrapolated into a factory benefit.

| Evidence layer | Purpose | Data | Current state |
|---|---|---|---|
| v2 development regression | algorithm development, regression detection, and claim-boundary checks | FDM and Crossed Barrel mechanical design | complete reference result retained |
| v3 protocol-frozen evaluation | strong-baseline comparison and mechanism-feature ablation | NASA airfoil noise and Delft yacht hydrodynamics | frozen with a retained 400-episode result; overall claim not demonstrated |
| v4 protocol-frozen evaluation | v7 new-data holdout | building-energy simulation and synchronous-machine experiments | 1,250 episodes retained; linear-baseline and ablation guardrails failed |
| v5 data-quality gate | automated HPLC candidate | Olympus HPLC | stopped before algorithms after replicate reconciliation left four preregistered folds below minimum size |
| v6 protocol-frozen evaluation | v8 new-data holdout | Olympus LNP3 formulation experiments | 300 episodes retained; quadratic baseline, linear context guardrail, and mechanism ablation failed |

## Data and scenarios

The benchmark uses two explicitly licensed manufacturing-experiment datasets:

- FDM 3D printing, DOI `10.17632/zd6td6svd6.2`: 162 complete DOE records in six material-and-infill contexts; layer thickness, infill, and speed are controls, with roughness and peak stress as outcomes.
- Crossed Barrel additive-manufacturing mechanical design, DOI `10.1126/sciadv.aaz1708`: 600 structural designs were each physically printed and compression-tested three times, for 1,800 experiments; discrete hollow-column count defines four contexts, twist angle, outer radius, and wall thickness are controls, and toughness is the outcome.

Categorical factors are stratified as context and never encoded as continuous controls with false distance. Each Crossed Barrel design uses the mean of its three measured toughness values while retaining replicate count and sample standard deviation, so repeated measurements do not masquerade as separate candidate experiments. See [NOTICE](NOTICE.md) for provenance, licenses, pinned source revision, checksums, and transformations, and [protocol-v2.json](protocol-v2.json) for the protocol. The loader verifies fixture hashes, row counts, unique identities, and replicate reconciliation before running.

## Compared methods

Every replay episode gives all three methods the same unique, seeded initial history:

1. the current Ingot production optimizer;
2. seeded model-free random search for a superiority test;
3. a regularized linear response surface as an active comparator for a noninferiority test.

An episode whose initial history already meets specification is excluded before any method runs. The endpoint therefore asks: “After observed history has not met specification, how many additional experiments are required?” Methods may query only real, unobserved settings in the public pool. They cannot inspect candidate outcomes, substitute nearest neighbors, interpolate outcomes, or synthesize results.

FDM uses four initial observations and a total budget of 12; Crossed Barrel uses eight and 16. Both allow at most eight additional trials, and a failure is scored as nine to avoid survivor bias. The reference run uses 100 unique episodes in each of 10 contexts, or 1,000 paired replays. Each Crossed Barrel target is the empirical 80th percentile for its column-count context; it is a development-regression threshold, not an engineering specification.

## Decision rules

- Versus random search: the hierarchical-bootstrap 95% confidence-interval lower bound for relative additional-trial reduction must be at least 10%; the success-rate difference lower bound must be at least -5 percentage points; and at least 80% of contexts must be non-worse.
- Versus the linear response surface: the reduction CI lower bound must be at least -10% and the success-rate difference lower bound at least -5 percentage points. This establishes noninferiority, not superiority.
- Dataset guardrails prevent either dataset from hiding an average regression beyond the frozen margins.

Random search represents experimentation without a response model. A linear response surface is already a classical experiment-reduction method. The public claim must therefore say “reduced versus seeded random search,” never “better than every DOE/RSM method.”

## Current reference result

[latest-results.json](latest-results.json) is the complete reference snapshot:

| Check | Result |
|---|---|
| Data and workflow validation | Passed; 2 datasets, 10 contexts, 1,000 episodes |
| Ingot / random / linear-response success | 98.20% / 75.90% / 90.40% |
| Ingot / random / linear-response capped mean additional trials | 2.77 / 4.87 / 3.21 |
| Reduction versus random search | 43.08%, 95% CI 36.19%–49.86%; superiority and both dataset guardrails passed |
| Reduction versus linear response surface | 13.53% overall, 95% CI -9.41%–27.76%; overall noninferiority passed, but Crossed Barrel was -33.39%, 95% CI -64.45%–-8.38%, and failed its guardrail |
| Permitted public conclusion | **Not passed: no overall experiment-reduction claim** (`not-demonstrated`) |

This is a development-stage public benchmark, not preregistered independent external validation. Superiority to seeded random search holds, but the dataset guardrail takes precedence over the aggregate: in Crossed Barrel, Ingot required 2.13 capped mean additional trials versus 1.60 for the linear response surface and was non-worse in none of the four contexts. The overall experiment-reduction claim is therefore marked not demonstrated. The result also does not prove the same savings for another factory, material, machine, or objective.

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

v3 has completed its first run under the following freeze procedure:

1. commit the algorithm, data snapshots, conversion script, and draft protocol;
2. obtain `candidate_evaluation_fingerprint` from the integrity check; in a following metadata-only commit, record the preceding commit in `optimizer_revision` and `protocol_revision`, copy that fingerprint into `evaluation_fingerprint`, and change the status to `frozen`;
3. run the complete evaluation without changing the algorithm, data, target, budget, baselines, or decision thresholds in response to the result; and
4. retain an unfavorable result and publish `not-demonstrated` where applicable.

The freeze references candidate commit `70cd12c16df55247dff70971cded312705f4b88b` and evaluation fingerprint `f82d3745530de851a901307f720b127f4db38bc2a1ed4d44d9a0d99fa66bbb5d`. The complete result is retained in [latest-results-v3.json](latest-results-v3.json):

| v3 check | Result |
|---|---|
| Data and execution integrity | passed; two datasets and 400 episodes with unique initial designs |
| Ingot with mechanism features | 100% target success; 1.4525 mean capped additional trials |
| Versus random search | +75.10%, 95% CI 72.67%–77.32%, passed |
| Versus maximin space filling | +9.22%, 95% CI 0.33%–17.19%, passed |
| Versus regularized linear response surface | -13.70%, 95% CI -22.94%–-4.55%, failed; non-worse on only 1/2 datasets |
| Versus regularized quadratic response surface | -5.06%, 95% CI -17.07%–6.51%, failed; non-worse on only 1/2 datasets |
| Paired mechanism-feature ablation | +22.64%, 95% CI 17.63%–27.50%, passed |
| Permitted overall conclusion | **failed: no claim of trial reduction versus every registered baseline** (`not-demonstrated`) |

On Yacht, the linear response surface averages 1.00 additional query versus Ingot's 1.38. On Airfoil, the quadratic response surface averages 1.36 versus Ingot's 1.525. Those dataset-level failures override the wins against random search and maximin. Passing the mechanism-feature ablation means only that the preregistered derived features improve the same optimizer in these two fixed pools; it does not validate them as physical laws or causal claims.

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

Verify the v3 data, frozen protocol, and unified fingerprint:

```bash
./scripts/verify-public-validation-v3.sh
```

The integrity fingerprint covers optimizer source, the dependency lock, evaluator, normalized protocol, and both data snapshots. A change to any of them blocks the full evaluation. After protocol freeze, run v3 with:

```bash
./scripts/benchmark-public-validation-v3.sh
```

## v6 protocol-frozen result

v6 uses the complete 768-setting formulation grid in three solid-lipid contexts. See [v6-selection.en.md](v6-selection.en.md) for data selection and stop conditions. After freezing the algorithm, data, evaluator, and decision rules, the evaluation completed 300 paired episodes. The full trajectories are retained in [latest-results-v6.json](latest-results-v6.json), and [validate_v6_result.py](development/validate_v6_result.py) independently recomputes the summary:

| v6 comparator | Relative additional-trial reduction | 95% CI | Context non-worse | Gate |
|---|---:|---:|---:|---|
| No-mechanism ablation | −5.80% | −16.22% to 3.58% | 0% | failed |
| Seeded random search | 43.75% | 36.64% to 50.08% | 100% | passed |
| Sequential maximin | 35.96% | 28.64% to 42.69% | 100% | passed |
| Regularized linear response surface | 27.61% | 20.95% to 34.00% | 66.67% | failed |
| Regularized quadratic response surface | −8.46% | −20.54% to 2.42% | 0% | failed |

The aggregate effect versus the linear response surface is positive, but in the Stearic-acid context Ingot averages 1.72 additional trials versus 1.66 for linear, triggering the preregistered 100% context-non-worse guardrail. The no-mechanism ablation and quadratic response surface are non-worse in all three contexts, refuting v8's automatic admission of the mechanism-quadratic ensemble in this pool. Both the overall and mechanism-feature conclusions remain `not-demonstrated`; v6 is development evidence only for any successor policy.

Verify the v6 data, frozen protocol, and unified fingerprint with:

```bash
./scripts/verify-public-validation-v6.sh
```

The freeze references candidate commit `ab1675bccb1283a679a86f61b7175359fc83c1af` and unified evaluation fingerprint `06099fa53a4d5f9c7898380f2bf24bb1982e830b7926cf567625507c04bf9cca`. The current tree can still rerun the frozen evaluation with `./scripts/benchmark-public-validation-v6.sh`; after a successor algorithm commit, the fingerprint check intentionally prevents new-algorithm output from being presented as v6.

When refreshing the reference result, retain every context and failure, run the complete benchmark first, and update Chinese and English documentation together. A real deployment still needs in-factory local-history replay and a small controlled transfer test; factory data does not need to leave the site.
