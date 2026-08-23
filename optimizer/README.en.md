# Ingot Process Optimizer

> Status: current numerical-service development guide.

This directory implements surrogate modeling and sequential experiment recommendation within Ingot's method toolbox. It receives a complete project snapshot and valid observations from Platform, then returns experiments for **engineer review**. It stores no business state and never controls equipment. GP/BO is a current method for expensive small-data sequential experiments, not the only answer to every process question and not a replacement product value.

See the [system design](../docs/design.en.md) for boundaries and [analysis and optimization methods](../docs/optimization.en.md) for method-selection principles.

## Current capabilities

- Continuous controls, hard parameter bounds, and measured outcome-safety constraints
- Explicit `context` stratification for discrete factors such as material, equipment, tooling, and formulation; category codes are never disguised as continuous controls
- Less-than, greater-than, target, and range objectives
- Objective weights and BoTorch/GPyTorch multi-output GPs with 95% intervals
- Declared physical outcome bounds; formal PASS/FAIL objectives keep posterior means, intervals, and acquisition samples inside 0-1
- GP outcome-safety filtering followed by visible-evidence admission for response surfaces, GP probability, and mechanism features
- Two decision intents: `reach-specification` for specification seeking, and `validate-hypothesis` for safely maximizing identifiable information in hypothesis variables
- A two-stage set-point-to-trajectory-to-quality surrogate
- Safe derived features declared by versioned project configuration, with no hidden behavior selected by industry, equipment, or variable names
- Safe-baseline local cold start, pending experiments, and idempotent batches
- Historical pool replay that can select only real, unconsumed parameter settings
- Stateless `POST /v1/suggestions` HTTP contract
- Synthetic digital-twin demonstration

The NumPy/SciPy GP remains a cold-start and regression baseline. Online suggestions, historical replay, and synthetic replay all use one engine-selection entry point: fewer than three valid observations use the sequential cold start and may apply NumPy GP priors; three or more use BoTorch. For specification seeking, histories with fewer than two observations per raw control remain on the regularized linear surface. With enough data, strong monotonic evidence retains the linear surface; otherwise the quadratic surface is used. On low-dimensional problems, disagreement between the linear and quadratic best candidates combines the response rank with maximin coverage, preventing weak early models from exploiting one local region; Euclidean distance fallback is disabled above three dimensions. GP posterior specification probability joins at 25% weight only after at least six visible observations per raw control. Declared mechanism features face stricter capacity and leave-one-out predictive-gain gates; failure removes them from both the surrogate and selection rule. Admission reads revealed observations and candidate controls only, never candidate outcomes or dataset names. The GP always supplies prediction intervals and outcome-safety probabilities; a frozen replay protocol, not product wording, must validate these implementation thresholds. Every caller relies on the selected engine's `suggest` path to enforce measured outcome-safety constraints and must not instantiate a concrete engine directly.

The numerical optimizer directly searches continuous controls only. Comparing multiple discrete levels requires separate campaigns stratified by categorical context or an applicable full/fractional factorial design. Adjacent identifiers never make different materials, machines, or tooling artificially similar.

## Local validation

Python environments are managed exclusively with `uv 0.11.32`; do not create a
project venv or install project dependencies with pip:

```bash
cd optimizer
uv sync --extra service --extra viz --group dev --locked
uv run --locked pytest
uv run --locked uvicorn service:app --port 8110
```

If a compatible Python is not installed, let `uv` install and select Python 3.12:

```bash
uv python install 3.12
uv sync --python 3.12 --extra service --extra viz --group dev --locked
```

`uv.lock` is authoritative. After changing `pyproject.toml`, run `uv lock` and
commit both files; CI and container builds reject an out-of-date lock.

Local development uses `8110` by default to avoid conflicting with the optimizer service on port `8100` in Docker Compose.

## Public-data regression

The repository uses fixed public experimental data to check historical-pool replay, comparisons with random and response-surface baselines, and preservation of the correct claim boundary when results are unfavorable. Check current frozen-acceptance integrity with:

```bash
./scripts/verify-optimizer-acceptance.sh
```

Ordinary CI runs fast deterministic integrity and no-leakage checks in `optimizer/tests/test_optimizer_acceptance.py`. The complete 450-episode frozen result is retained and cannot become new acceptance after an algorithm change. See [Public-data experiment-efficiency validation](../tools/public-validation/README.en.md) for provenance, the current failed result, and update rules.

The current method-selection policy's development regression uses four strong baselines and a mechanism-feature ablation:

```bash
./scripts/benchmark-optimizer-development.sh
```

This command reads disclosed data and exists only for development and regression prevention. The current development problem is distinguishing applicable linear from quadratic structure. New effect evidence requires committing a successor first, then selecting fresh data and freezing objectives, budgets, and rules; a modified method cannot rerun old outcomes and relabel them as independent validation.

## Stateless contract

`POST /v1/suggestions` accepts:

- a campaign with variables, weighted objectives, parameter and outcome constraints;
- an optional versioned `derived_features` DAG using only bounded numeric operators;
- the complete immutable observation snapshot;
- measured process features, outcome-constraint measurements, and pending parameter settings;
- optional candidate parameter settings;
- top-k, seed, candidate-count, and posterior-sample settings.

It returns recommended parameters, objective means and 95% intervals, predicted distance to specification, feasibility probability, acquisition value, model version, and rationale. Platform also checks that proposed conditions are distinguishable at the minimum spacing seen in actual historical conditions; nearby floating-point values cannot masquerade as separate experiment conditions.

Derived operators run on engineering-unit controls and are normalized with the
declared offset and scale. Inputs may reference campaign controls or an earlier
derived feature. Composition problems may also use `weighted_mean` and
`weighted_standard_deviation` with fixed property coefficients; weights must
be non-negative and have a positive total. Arbitrary Python expressions, unknown inputs, forward
references, and legacy hidden `process_profile` switches are rejected.

For `validate-hypothesis`, the campaign must also provide `hypothesis_variables`. The platform sends this intent only after an engineer defines the outcome, expected direction, and minimum meaningful effect; the service never treats an association as a causal conclusion.

The .NET platform turns the returned batch into an ordinary `ResearchExperiment`, storing its input hash, model version, predictions, review, and outcome. No recommendation is sent directly to a PLC.

## Historical validation

Use `replay_history_pool` for real history. Every row must contain a finite numeric `occurred_at`, and rows must be strictly increasing in time. Missing, duplicate, or out-of-order timestamps are rejected rather than silently sorted, preserving the no-future-leakage contract. Replay measures whether the model ranks successful parameter settings earlier among parameter settings that were actually run. It does not invent outcomes for untried parameter settings and cannot by itself prove prospective trial savings. Aggregate repeated parameter settings with a predefined statistical method before replay.

Synthetic replay truth functions must return `SyntheticTruthResult` with explicit `outcomes`, `constraint_outcomes`, and optional `process_features`. A synthetic run succeeds only when its objectives and every outcome-safety constraint pass.

## Platform integration

The .NET platform remains the only business system of record:

1. An experiment `ExecutionKey` maps to the field run identifier. Platform assembles measured process features, realized control values, and inspection results into one observation.
2. Platform sends the complete project definition, valid observations, and constraints to this service.
3. This service calculates the next parameter batch without retaining business state.
4. Platform creates ordinary experiments with the input hash, model version, and prediction intervals.
5. After all runs are complete, Platform records the reviewed result. If the experiment tests a hypothesis, the result interval updates that hypothesis as supported, rejected, or inconclusive through the existing approval and completion workflow.

PLC, instrument, vision, file, and API connectors only map source data into this contract; they do not change the optimizer's responsibility or authority.
