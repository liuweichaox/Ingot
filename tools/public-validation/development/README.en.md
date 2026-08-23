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

## v4 holdout result and successor boundary

v4 ran only after data selection, candidate implementation, and protocol freeze. The complete 1,250 paired episodes are retained in [`../latest-results-v4.json`](../latest-results-v4.json), with [`validate_v4_result.py`](validate_v4_result.py) providing an independent recomputation. The result is again “not demonstrated”:

| Comparator | v4 relative reduction | 95% CI | Unit non-worse | Gate |
|---|---:|---:|---:|---|
| No-mechanism ablation | 7.55% | 5.19% to 9.88% | 92% | failed |
| Seeded random search | 59.85% | 57.93% to 61.70% | 100% | passed |
| Sequential maximin | 39.55% | 36.43% to 42.52% | 100% | passed |
| Regularized linear response surface | −8.70% | −11.69% to −5.89% | 12% | failed |
| Regularized quadratic response surface | 25.39% | 22.39% to 28.21% | 100% | passed |

v4 shows that the v7 Pearson/Spearman admission rule is still too coarse. Most Energy Efficiency units call for a linear response surface, but declared mechanism features admit the mechanism-quadratic ensemble too early. v4 is development and regression evidence for v8 from this point onward; it cannot be relabeled as external validation after the policy changes. The next public decision requires fresh, uninspected data and a newly frozen protocol.
