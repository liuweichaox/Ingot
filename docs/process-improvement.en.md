# Process Improvement Loop

Ingot separates process improvement into five inspectable levels. The system manages data versions, computed results, workflow states, and user activity records. The factory supplies real operating data, equipment limits, quality targets, and site approvals.

## Capability levels

| Level | System capability | Gate to the next level |
|---|---|---|
| L1 Data available | Standard production events, cycles, recipes, tooling, and quality results | Consistent cycle IDs plus complete timestamps and operating context |
| L2 Comparable analysis | Stage alignment, `cycle_phases`, `cycle_features`, late-event invalidation, historical backfill, and database feature aggregates | Versioned analysis plan and data model plus acceptable stage and sample quality |
| L3 Model operation | Training dataset versions, model versions, evaluation gates, activation, drift, automatic suspension, retirement, and rollback | At least one passing evaluation; only one active version per model |
| L4 Controlled improvement | Investigations, possible causes, parameter bounds, safety constraints, controlled trials, actual results, and conclusions | Approved and completed trial, passed safety checks, and a conclusion linked to its results |
| L5 Approved execution | Parameter recommendations with scope, expected outcomes, risk, stop rules, and rollback; review, approval, external execution reference, and outcome confirmation | Confirmed investigation conclusion, separated creation/review/approval duties, actual outcomes, and safety checks |

L5 means the software supports a constrained decision loop. It does not mean that every recommendation is automatically correct or that the system may bypass site safety controls to modify equipment.

## Cycle materialization, backfill, and database aggregation

A completed-cycle analysis is identified by the cycle ID, algorithm version, process data model version, analysis plan version, source maximum ingest ID, and source event count. A later event marks the cycle dirty and queues background recomputation. Startup and periodic scans enqueue unfinished dirty cycles, while deterministic recomputation on read remains a fallback.

`POST /api/v1/cycle-analysis-backfills` creates a background backfill job. The job stores total, processed, materialized, and failed cycle counts plus the last cycle ID. Incomplete jobs resume after a service restart.

Window boundaries, contiguous phase groups, mean, extrema, range, standard deviation, P05/P50/P95, integral, and slope are computed directly by PostgreSQL/TimescaleDB window and aggregate functions. Late-event recomputation and historical backfill share the same database calculator, algorithm version, and feature definitions. Every materialization is also recomputed by an independent in-process reference implementation and checked with a relative-error gate; mismatched results cannot become `ready`. This keeps streaming and batch calculations consistent. The implementation does not depend on TDengine.

`GET /api/v1/cycle-feature-aggregates` calculates cycle count, minimum, maximum, average, sample standard deviation, and P10/P50/P90 inside PostgreSQL. It includes only materializations whose current status is `ready`.

## Model lifecycle

A training dataset version is immutable. It stores its analysis plan and data model versions, time window, cycle IDs, input features, target, row count, and content hash.

A model begins as a draft. Inputs must belong to its dataset version and its output must match the target. Its model-file location, model-file SHA-256, and a passing evaluation are required before `validated` or `active`. A drift reading at or above the stop threshold automatically changes an active model to `suspended`. Rollback retires the current version and activates a previously evaluated version.

Main endpoints:

- `GET|POST /api/v1/training-datasets`
- `GET|POST /api/v1/process-models`
- `POST /api/v1/process-models/{modelId}/{version}/evaluations`
- `POST /api/v1/process-models/{modelId}/{version}/drift`
- `POST /api/v1/process-models/{modelId}/{version}/status`
- `POST /api/v1/process-models/{modelId}/rollback`

## Investigations and controlled trials

An investigation stores the problem and cycle scope, possible causes, controlled parameter changes, safety constraints, stop and rollback instructions, measured results, and a confirmed, rejected, or inconclusive conclusion.

A trial creator cannot approve the same trial. Results can be added only while the trial is running. A trial cannot complete without results or when a result fails its safety check. A conclusion can be created only for a completed trial and can reference only that trial's result IDs.

Trials have two rigor levels:

- `exploratory` supports investigation and small-sample learning. Engineers may record results, but those results cannot be presented as confirmatory scientific findings.
- `confirmatory` requires a preregistered hypothesis, primary metric, signal/phase/feature binding, improvement direction, alpha, estimator, planned sample sizes, allocation method, exclusion rules, and source-computable safety bindings before approval. Control and treatment cycle sets cannot overlap.

`POST /api/v1/process-investigations/trials/{trialId}/results/calculate` calculates a confirmatory result. It reads only `ready` cycle materializations that carry feature-definition and computation hashes, applies a Welch difference-in-means estimator, and produces standard error, degrees of freedom, and a two-sided confidence interval. Safety constraints are evaluated from their bound cycle features as well. A phase-bound metric must include its phase occurrence. Control and treatment records must use exactly the same unit, feature definition, algorithm version, data-model version, and analysis-plan version; otherwise comparison is rejected. The result-check SHA-256 covers the preregistered protocol, assignment, cycle IDs, feature values, and every feature computation hash. Confirmatory trials reject manually supplied results and cannot complete without a source-derived result-check hash.

## Mechanistic models and four fusion modes

Mechanistic models are registered by model ID and immutable version. Each version records variables, units, valid ranges, applicability, equation basis, source, and a content hash. Status gates follow `draft → validated → active → retired`, with at most one active version per model. The current deterministic executor supports affine mechanistic models with intercept and interaction terms.

A fusion definition pins one mechanistic-model version and one data-model version and supports four reproducible modes:

- `calibration`: a data-model output calibrates mechanistic bias;
- `post-processing`: the mechanistic output corrects a data-model output;
- `mechanism-as-feature`: the mechanistic output becomes a data-model input feature;
- `ensemble`: registered weights combine mechanistic and data-model outputs.

Execution validates variables, units, ranges, and applicability. Results retain both components, the final output, execution hash, and audit record. Main endpoints are `GET|POST /api/v1/mechanism-models`, model status, `GET|POST /api/v1/mechanism-fusions`, and `/execute`.

## Documents, spreadsheets, images, and site knowledge

`POST /api/v1/process-knowledge` accepts PDF, XLSX/XLSM, CSV, text, and common image formats. Extraction starts automatically: PDF records retain page and full-page location, Excel records retain worksheet and cell range, and images use a configurable vision OCR service to retain polygon and confidence. Every record also carries extractor version and content hash. Failed extraction can be retried through `/extract`. The service hashes and stores the original and can copy it to an independent archive volume.

Sources move through `uploaded → indexed → reviewed → retired`. A source cannot become `reviewed` until it has at least one knowledge record and all records have human review. AI or parsing services may prepare records for review but cannot replace the site reviewer.

Image OCR uses `KnowledgeExtraction:Vision:Endpoint` and `KnowledgeExtraction:Vision:ApiKey`. Without configuration, the image source remains in an explicit retryable failed state; the system does not invent OCR content.

## Cross-industry scientific validation

`POST /api/v1/scientific-validation` accepts source CSV, XLSX/XLSM, or MATLAB Level 5 MAT data plus a versioned manifest. It checks source information, license, citation, source SHA-256, field coverage, chronology, measured-data declaration, and an independent stream/batch statistic. A failed hard gate marks the report `rejected` and prevents research conclusions. A manifest may declare documented valid signal ranges; excluded samples are never silent because their counts and reasons are retained in the report.

The manifests under `docs/validation/datasets` cover two measured datasets:

- NASA/UC Berkeley milling tool wear: 167 machining runs, 16 operating cases, six sensor signals, and intermittently measured flank wear;
- Mendeley Al-Ce aging heat treatment: 516 measured records with temperature, time, hardness, and conductivity.

Both datasets pass the source-hash and stream/batch gates in local acceptance runs. This demonstrates reproducible ingestion, quality gating, and cross-industry computation. It does not by itself prove a causal process-parameter improvement; that still requires a preregistered trial and site validation.

## Parameter recommendations and the equipment boundary

A parameter recommendation must start from a `confirmed` investigation conclusion. Every parameter must have been changed in that conclusion's controlled trial. Its recommended value must equal the completed trial value, its allowed range cannot exceed the trial bounds, expected outcomes must match the trial results, and operating constraints cannot be weaker than the trial safety constraints. A different value requires another controlled trial. The record also includes applicability scope, expected annual economic value, trial and implementation costs, downside at risk, the value calculation method, operating risk, a stop rule, and a rollback plan.

The normal workflow is `draft → reviewed → approved → executed → verified`. The creator cannot review the same recommendation, and the approver must differ from both creator and reviewer. `executed` stores an MES, change-order, or equipment-side reference; this API does not write a PLC. Outcome confirmation requires an actual value and sample counts for every expected metric plus the economic-value observation window, gross value, implementation cost, and calculation method. The service calculates net value as gross value minus implementation cost instead of trusting a client-supplied total. It derives target attainment from the expected improvement direction and derives the overall safety result from the metric checks. A missed target or failed safety check automatically produces `rollback-required`; recording the site rollback reference changes it to `rolled-back`.

## Authorization and deployment

Reads require `quality.read`. Writes, transitions, backfills, and approvals require `process.engineer` or `platform.admin`. Production requires a unified identity provider and independent archive paths for inspection attachments and process knowledge.

The Web entry is **Analysis Center → Process Improvement**.
