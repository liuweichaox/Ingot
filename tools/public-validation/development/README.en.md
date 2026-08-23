# Public-validation development workspace

Everything in this directory is **development and regression evidence only**. The v3 outcome data and retained episode results were inspected before the v7 policy was chosen, so no result produced here is an external, blind, or protocol-frozen validation result.

## v3 failure diagnosis

[`v3-failure-diagnosis.ipynb`](v3-failure-diagnosis.ipynb) verifies the retained v3 data grain and headline calculations, then isolates the two strong-baseline failures:

- the Yacht linear response surface reaches the target on its first additional query in all 200 episodes, which is the theoretical minimum;
- Airfoil leaves useful disagreement among linear, quadratic, GP, and mechanism-augmented models, so model admission rather than a universal optimizer is the appropriate development target.

## v7 candidate regression

The candidate admits a raw linear response surface only when a visible control clears both Pearson and Spearman evidence thresholds. Otherwise it uses a raw quadratic response surface, or—when declared mechanism features exist—an equal-rank ensemble of mechanism-only and joint quadratic response surfaces. Safety filtering and GP uncertainty remain active around that decision rule.

Run the disclosed regression with:

```bash
optimizer/.venv/bin/python \
  tools/public-validation/development/benchmark_candidate.py \
  --episodes 200 \
  --bootstrap-samples 5000 \
  --output /tmp/ingot-v7-development.json
```

On the inspected v3 pools, the current candidate produces 400 paired episodes with 100% primary success and 1.055 mean capped additional trials. The paired relative reductions are:

| Comparator | Development reduction | 95% CI | Gate |
|---|---:|---:|---|
| No-mechanism ablation | 10.21% | 4.64% to 16.63% | passed |
| Seeded random search | 81.91% | 80.55% to 83.14% | passed |
| Sequential maximin | 34.06% | 29.62% to 38.31% | passed |
| Regularized linear response surface | 17.42% | 10.55% to 24.09% | passed |
| Regularized quadratic response surface | 23.69% | 15.89% to 31.20% | passed |

These numbers show that the implementation is ready to become a frozen candidate. They do **not** replace the retained v3 result, justify changing its failed conclusion, or support a public experiment-reduction claim. Fresh datasets selected before outcome inspection and a frozen successor protocol are still required.
