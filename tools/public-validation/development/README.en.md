# Optimizer development regression

This directory retains one current development entry point. It reads outcome data that has already been inspected and exists only to diagnose failures and prevent code regression; it is not new effect evidence.

Run:

```bash
./scripts/benchmark-optimizer-development.sh
```

The current frozen acceptance establishes that the policy reliably beats random search and maximin and matches the quadratic response surface. It nevertheless uses 32.08% and 68.36% more additional experiments than the linear response surface on Alkox and P3HT. Development therefore has one target: distinguish linear from quadratic structure using visible observations, not add more model types.

A successor may use the current acceptance data for development regression, but a rerun cannot be called independent acceptance. A new effect decision requires committing the successor algorithm first, selecting another outcome-uninspected dataset group, and freezing the rules.

Past internal-round evaluators, candidate scripts, and diagnostic notebooks have been removed. Frozen protocols and complete original results remain in the parent directory as immutable audit records.
