# Optimizer development regression

This directory retains one current development entry point. It reads outcome data that has already been inspected and exists only to diagnose failures and prevent code regression; it is not new effect evidence.

Run:

```bash
./scripts/benchmark-optimizer-development.sh
```

The retained frozen acceptance shows that the previous policy reliably beat random search and maximin and matched the quadratic response surface. It nevertheless used 32.08% and 68.36% more additional experiments than the linear response surface on Alkox and P3HT. The current successor defaults to linear response and admits quadratic only when normalized target-ranking error improves beyond one standard error across three consecutive expanding histories; inconclusive evidence keeps linear. Reruns here check whether that implementation addresses the known failure; they do not create new effect evidence.

At the original protocol scale of 450 development episodes, the successor reduces additional trials by 50.57% versus random search, 63.93% versus maximin, and 14.32% versus the linear surface; those three comparisons pass the original gates. It reduces 2.61% versus the quadratic surface in aggregate, but the HPLC subgroup is −26.47%, so the overall result remains `not-demonstrated`. These data have now informed method development and support failure diagnosis and regression only.

A successor may use the current acceptance data for development regression, but a rerun cannot be called independent acceptance. A new effect decision requires committing the successor algorithm first, selecting another outcome-uninspected dataset group, and freezing the rules.

Past internal-round evaluators, candidate scripts, and diagnostic notebooks have been removed. Frozen protocols and complete original results remain in the parent directory as immutable audit records.
