# Optimizer

## Target problem

Ingot targets expensive, noisy, small-data, multi-objective, constrained sequential experiments. Optical-lens molding fits this problem: every run consumes equipment, material, and inspection capacity; process variation exists; parameter dimension is limited; objectives and safety outcomes are measurable.

## Why not an LLM or a deep network

- An LLM has no calibrated numerical uncertainty and does not generate recipes.
- Deep networks usually need more data than one campaign can provide.
- A GP provides both a mean and uncertainty in small data.
- Bayesian optimization balances exploration and exploitation with that uncertainty.

LLMs may explain, retrieve, or draft structured intent. Deterministic numerical code makes recommendations.

## Two-stage surrogate

Quality depends on the realized trajectory, not only the setpoint:

```text
control settings x
  ├─ GP₁: x → realized process features z
  └─ GP₂: [x, z] → objectives y and safety outcomes c
```

GP₂ trains on measured features. For a new recipe, GP₁ predicts trajectory features before GP₂ evaluates quality.

Only process features common to all training observations enter the current model, avoiding implicit missing-value fabrication.

## Objectives

Supported shapes:

- less than or equal to specification;
- greater than or equal to specification;
- target ± tolerance;
- acceptable range;
- objective weights.

For multiple objectives, normalized specification utility enters qLogNEHVI to improve the constrained Pareto front.

## Constraints

### Parameter constraints

Bounds and linear `<=` / `>=` rules filter candidate generation.

### Outcome constraints

Crack rate, residual stress, and similar constraints are additional GP outputs and acquisition constraints. Safety-critical outcomes also require:

```text
P(outcome satisfies limit | current evidence) ≥ minimum safety probability
```

Candidates below the threshold are not returned.

## Cold start

- Without safety outcomes, use Sobol space-filling points.
- With safety outcomes, require a verified safe baseline.
- Keep initial exploration inside a trust region around that baseline.
- Switch to the BoTorch GP engine after three observations.

## Pending experiments

Unobserved Planned, Approved, and Running settings become `X_pending` and are removed from the candidate set. Repeated clicks and parallel execution do not duplicate experiments.

## Stateless protocol

The optimizer stores no campaign. Every request supplies:

- variables, objectives, and constraints;
- all valid observations;
- pending points;
- batch size and seed.

The response includes settings, objective and constraint predictions, feasibility, acquisition value, model version, and rationale. Platform persists the snapshot and result.

## Current limits

- The trajectory surrogate currently feeds posterior means, not the full trajectory posterior, into the quality model.
- GP 95% intervals are approximate and are not a factory guarantee.
- Cross-product multi-task GP is not complete.
- Physical features exist, but a calibrated grey-box prior mean is not complete.
- Real replay must measure calibration, regret, and experiments-to-specification.
