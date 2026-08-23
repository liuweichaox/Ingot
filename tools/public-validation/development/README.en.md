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

The v4 fingerprint freezes the candidate algorithm used at the time. Once the main branch advances to a successor algorithm, the complete v4 evaluator intentionally refuses to rerun from the current worktree. Reproducing the original trajectories requires checking out the candidate revision recorded by the protocol; current main retains the result, fixture-integrity checks, and independent summary recomputation.

| Comparator | v4 relative reduction | 95% CI | Unit non-worse | Gate |
|---|---:|---:|---:|---|
| No-mechanism ablation | 7.55% | 5.19% to 9.88% | 92% | failed |
| Seeded random search | 59.85% | 57.93% to 61.70% | 100% | passed |
| Sequential maximin | 39.55% | 36.43% to 42.52% | 100% | passed |
| Regularized linear response surface | −8.70% | −11.69% to −5.89% | 12% | failed |
| Regularized quadratic response surface | 25.39% | 22.39% to 28.21% | 100% | passed |

v4 shows that the v7 Pearson/Spearman admission rule is still too coarse. Most Energy Efficiency units call for a linear response surface, but declared mechanism features admit the mechanism-quadratic ensemble too early. v4 is development and regression evidence for v8 from this point onward; it cannot be relabeled as external validation after the policy changes. The next public decision requires fresh, uninspected data and a newly frozen protocol.

## v8 method-routing development regression

v8 no longer enters a quadratic model merely because mechanism features exist. A raw control with strong Pearson and Spearman evidence still routes to the raw linear surface. No mechanism features routes to the raw quadratic surface. With mechanism features but no more observations than first-order joint-model coefficients, leave-one-out error selects between raw linear and joint linear surfaces. Only after that minimum complexity gate does the method enter a mechanism-only plus joint-feature quadratic rank consensus. The rule uses revealed observations and model dimension only; it never reads candidate outcomes or branches on dataset names.

Both regressions below use inspected v3/v4 outcomes and therefore remain development evidence:

- Across the 400 v3 episodes, v8 reduces trials by 80.75%, 29.84%, 12.13%, and 18.81% versus random, maximin, linear, and quadratic response surfaces. All four pass and neither dataset is worse. The reduction versus the no-mechanism ablation is only 4.47%, with a 95% CI of −3.53% to 12.34%, so the ablation gate fails.
- Across the 1,250 v4 episodes, reductions versus random, maximin, and quadratic response surfaces are 64.11%, 45.97%, and 33.31%; the mechanism ablation reduction is 17.36%. All four pass. The aggregate reduction versus the linear response surface is 2.84%, with a 95% CI of 1.43% to 4.29%, but only 96% of evaluation units are non-worse, so the per-unit guardrail still fails.

Both failures are retained alongside the passes. They show that routing fixes v7's aggregate linear-model mismatch, but does not establish that mechanism features help every pool or that every small context is non-worse than an applicable linear baseline. v8 needs a newly frozen successor protocol with fresh outcome columns before it can support a new public conclusion.

## v6 holdout result and v9 development boundary

After freeze, v6 completed 300 LNP3 formulation episodes. The full result is retained in [`../latest-results-v6.json`](../latest-results-v6.json) and independently recomputed by [`validate_v6_result.py`](validate_v6_result.py). Reductions versus random and maximin are 43.75% and 35.96%, both passed. The aggregate reduction versus linear is 27.61%, but only 2/3 contexts are non-worse; the effect versus quadratic is −8.46%; and mechanism features are −5.80% versus the no-mechanism version, with zero of three contexts non-worse. Neither the overall nor mechanism-contribution claim is demonstrated.

This failure narrows successor development to mechanism-feature admission rather than adding model complexity. Exceeding a coefficient-count threshold does not establish predictive value for derived features. A successor router must compare raw and mechanism-augmented models using revealed observations and retain the raw response surface when evidence is insufficient. Because v6 outcomes are now inspected, they are development and regression evidence only; any new public decision requires fresh uninspected outcomes and a newly frozen protocol.

## v9 conservative method-routing development regression

v9 applies capacity and leave-one-out gain gates to the complete surrogate, not just the final ranking. With insufficient observations, the mechanism and ablation variants must produce identical trajectories. Strong monotonic evidence directly admits the linear surface. Other cases use a linear–quadratic rank consensus, with GP specification probability joining only after three visible observations per raw control. Complete regressions on disclosed evidence are:

| Disclosed data | Random | Maximin | Linear | Quadratic | Mechanism ablation | Remaining failed guardrail |
|---|---:|---:|---:|---:|---:|---|
| v3, 400 episodes | +79.64% | +25.78% | +7.05% | +14.10% | 0% | quadratic non-worse on only 1/2 datasets; no ablation gain |
| v4, 1,250 episodes | +62.82% | +44.03% | −0.65% | +30.91% | 0% | linear non-worse on 52% and quadratic on 88% of units; no ablation gain |
| v6, 300 episodes | +49.97% | +43.05% | +35.62% | +3.54% | 0% | quadratic non-worse in only 1/3 contexts with CI crossing zero; no ablation gain |

All figures are post-inspection development evidence and cannot replace any frozen result. They show that v9 executes safe degradation and consistently beats random search across these pools. They also rule out claiming that the current version generally beats every response surface or that mechanism features are already validated. A new decision must select uninspected outcomes only after committing v9, then freeze the objective, contexts, sample sizes, and every gate.

## v7 OER holdout result and successor-development boundary

v10 runs 400 frozen episodes on four measured OER composition plates not used for development. It reduces trials by 57.70%, 58.01%, and 53.67% versus random, maximin, and linear response surfaces, passing all three. The aggregate reduction versus quadratic is 11.67% (95% CI 4.96%–17.81%), but plates 3851 and 3860 are individually worse, leaving only 50% of plates non-worse. Composition features reduce trials by only 0.13% versus the no-feature version (95% CI −2.00%–2.23%), again with only 2/4 plates non-worse. The full result is retained in [`../latest-results-v7.json`](../latest-results-v7.json) and independently recomputed by [`validate_v7_result.py`](validate_v7_result.py).

This failure shows that v10 avoids the earlier linear mismatch, but can still be slowed by GP and the linear–quadratic rank consensus when a quadratic response surface is strongly applicable. The preregistered generic composition descriptors also provide no reproducible gain. OER outcomes are development evidence from this point onward and may be used only for successor raw-model selection, feature rejection, and regression. Any new public decision still requires uninspected outcomes selected before a new freeze; tuning these four plates to pass cannot rewrite v7.

## v11 staged-complexity routing

[`diagnose_v7_model_router.py`](diagnose_v7_model_router.py) reads disclosed v7 trajectories only. Initial leave-one-out error cannot distinguish plate 3496, where GP helps, from plate 3851, where the quadratic response surface is better; kernel-ridge leave-one-out error also provides no stable separator. v11 therefore adds no dataset rule chosen from known outcomes. It uses the quadratic response surface by default, admits GP at 25% weight only after six visible observations per raw control, and requires mechanism features to improve leave-one-out error by at least 50% over the raw quadratic surface.

[`benchmark_candidate_v7.py`](benchmark_candidate_v7.py) runs 25 episodes on each disclosed OER plate, for 100 paired development episodes:

| Comparator | v11 development reduction | 95% CI | Plate non-worse | Gate |
|---|---:|---:|---:|---|
| Seeded random search | 53.66% | 47.45% to 59.10% | 100% | passed |
| Sequential maximin | 54.42% | 48.12% to 59.86% | 100% | passed |
| Regularized linear response surface | 50.52% | 43.57% to 56.55% | 100% | passed |
| Regularized quadratic response surface | 7.00% | 3.99% to 10.33% | 100% | passed |
| No-composition-feature ablation | −0.10% | −0.31% to 0% | 75% | failed |

This small regression shows that staged routing repairs the known OER quadratic-baseline regression while correctly rejecting generic composition features that provide no gain. It is a 100-episode regression on disclosed outcomes, does not replace the frozen v7 result, and does not satisfy a new public-success condition.
