# Ingot Process Optimizer

This directory implements the surrogate and sequential-recommendation kernel described in the [system design](../docs/design.en.md). It receives a complete project snapshot and valid observations from the platform, then returns experiments for **engineer review**. It stores no business state and never controls equipment.

## Current capabilities

- Continuous controls, hard parameter bounds, and measured outcome-safety constraints
- Less-than, greater-than, target, and range objectives
- Objective weights and BoTorch/GPyTorch multi-output GPs with 95% intervals
- Outcome-constrained batch `qLogNEHVI` and `qLogNEI`
- Two decision intents: `reach-specification` for specification seeking, and `validate-hypothesis` for safely maximizing identifiable information in hypothesis variables
- A two-stage set-point-to-trajectory-to-quality surrogate
- Safe derived features declared by versioned project configuration, with no hidden behavior selected by industry, equipment, or variable names
- Safe-baseline local cold start, pending experiments, and idempotent batches
- Historical pool replay that can select only real, unconsumed recipes
- Stateless `POST /v1/suggestions` HTTP contract
- Synthetic digital-twin demonstration

The NumPy/SciPy GP remains a cold-start and regression baseline. The production path uses BoTorch after three valid observations.

## Local validation

Python environments are managed exclusively with `uv 0.11.32`; do not create a
project venv or install project dependencies with pip:

```bash
cd optimizer
uv sync --extra service --extra viz --group dev --locked
uv run --locked pytest
uv run --locked python ../tools/optical-molding-demo/optimizer_demo.py
uv run --locked uvicorn service:app --port 8110
```

If a compatible Python is not installed, let `uv` install and select Python 3.12:

```bash
uv python install 3.12
uv sync --python 3.12 --extra service --extra viz --group dev --locked
```

`uv.lock` is authoritative. After changing `pyproject.toml`, run `uv lock` and
commit both files; CI and container builds reject an out-of-date lock.

The synthetic demo tests mechanics only. Its result can vary with numerical-library versions and is not evidence of real process savings.

Local development uses `8110` by default to avoid the common `8100` port used by device-acquisition examples. The optimizer remains on `8100` inside Docker Compose.

## Stateless contract

`POST /v1/suggestions` accepts:

- a campaign with variables, weighted objectives, parameter and outcome constraints;
- an optional versioned `derived_features` DAG using only bounded numeric operators;
- the complete immutable observation snapshot;
- measured process features, outcome-constraint measurements, and pending recipes;
- optional candidate recipes;
- top-k, seed, candidate-count, and posterior-sample settings.

It returns recommended parameters, objective means and 95% intervals, predicted distance to specification, feasibility probability, acquisition value, model version, and rationale.

Derived operators run on engineering-unit controls and are normalized with the
declared offset and scale. Inputs may reference campaign controls or an earlier
derived feature. Arbitrary Python expressions, unknown inputs, forward
references, and legacy hidden `process_profile` switches are rejected. Industry
examples live under `tools/` and are not part of the production optimizer
package.

For `validate-hypothesis`, the campaign must also provide `hypothesis_variables`. The platform sends this intent only after an engineer defines the outcome, expected direction, and minimum meaningful effect; the service never treats an association as a causal conclusion.

The .NET platform turns the returned batch into an ordinary `ResearchExperiment`, storing its input hash, model version, predictions, review, and outcome. No recommendation is sent directly to a PLC.

## Historical validation

Use `replay_history_pool` for real history. It measures whether the model ranks successful recipes earlier among recipes that were actually run. It does not invent outcomes for untried recipes and cannot by itself prove prospective trial savings. Aggregate repeated recipes with a predefined statistical method before replay.
