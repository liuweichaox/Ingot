# Design

## Ingot Chat design

```text
question and page context
  → typed query plan
  → authorization, scope, and tool-allowlist validation
  → check_data_quality / get_cycle_trace
  → number and related records verification
  → streamed answer
```

The Chat model interprets language and composes responses. Platform deterministic code owns data query, permissions, tool execution, number validation, and related records validation. Chat runs are user-isolated and available only under `/api/v1/chat/*`.

## Event-ingestion design

Teams map any source to `ProductionEvent` batches and call `/api/v1/events:batch`. Platform deterministically:

1. validates the Edge token, `edgeId`, batch size, and event fields;
2. validates source prefixes, event IDs, and sequence uniqueness;
3. handles duplicate submission by `eventId` and `(edgeId, seq)`;
4. persists platform records and returns `ackSeq`;
5. exposes query, SSE, and correlation-ID cycle record chains.

Teams choose source protocols, local buffering, retry timing, and process supervision. The standard contract keeps those implementation details decoupled from Platform.

## Quality-inspection workflow

Quality inspection follows an analyzed operation; a blank “inspection entry” form is not modeled as a standalone business object:

1. Discrete production may create a cycle quality task from `cycle.completed`. Continuous production, production runs, and material lots use an explicit quality scope that binds an operating object, time range, and published `InspectionPlan`. No task is invented when no plan matches.
2. The plan references versioned `InspectionDefinition` records and configures required checks, ordering, original attachments, and review requirements. Selecting a task carries read-only quality-object and analysis-scope identifiers into the form.
3. Upload original images to controlled long-term storage first, compute SHA-256 on the server, and then store their references with the inspection record.
4. Historical inspection records can reopen the original image and append an immutable review decision. Original access and review actions are audited; derivative thumbnails cannot replace the original.
5. An inspection result records an observation and is not a QMS release or equipment-control decision.

The global navigation therefore exposes a “Quality Inspection” workbench. “Create inspection record” is an action inside that workbench. Quality-object and analysis-scope identifiers can only come from pending tasks; fabricated manual associations are not available. Inspection submission, original-image access, and review all use the signed-in Platform identity and roles, with no inspection-specific user or access password.

The Quality Plans page maintains inspection definitions and plan versions. Published plan and definition versions cannot be overwritten; changes require a new version. Optical-molding vision and manual checks exist only in the optional sample configuration, never as Platform defaults.

Roles come from the signed-in Platform account: `quality.inspector` submits inspections, `quality.reviewer` reviews originals, `process.engineer` queries records and historical comparisons, and `platform.admin` manages the platform. Development may use a built-in identity for evaluation; production requires host authentication to establish the user identity.

## Operating objects and analysis scopes

Platform does not require every industrial dataset to have a production cycle. The event `subject.type + subject.id` forms the operating-object catalog. An object may be equipment, a line, a station, a sensor, or another caller-defined type. The catalog and data-health view aggregate event count, sample count, correlated operations, first/latest observations, and maximum sample gaps directly from the event store, so a continuous object remains visible even when it has no cycle.

Analysis joins process data and quality through an explicit scope:

- `production-cycle`: a single-piece or cyclic operation with a clear start and end;
- `production-run`: a bounded run that may cover multiple products;
- `analysis-window`: an explicit time window over continuous data;
- `material-lot`: a quality scope organized by material lot.

Inspection records are first-class data linked to an analysis scope, a quality object, and an inspection-definition version. Quality analysis reads effective inspection records instead of inferring quality from cycle status, allowing cycle inspections and continuous-operation windows to be grouped together by product, recipe, operating object, and outcome.

## Same-series historical cycle comparison

`GET /api/v1/cycle-comparisons/{correlationId}` uses one cycle as the baseline and selects comparable history according to the published analysis plan. Start and end events determine lifecycle completion; process data is independently classified as available, degraded, or unavailable. The service computes whole-cycle time-weighted features from actual timestamps, never interpolates across a detected acquisition gap, and joins recipe versions, inspection outcomes, and the latest original-image review. Available cycles have weight 1, degraded cycles have weight 0.5, and unavailable cycles do not enter formal statistics.

When an analysis plan uses `stage-relative` alignment, the `stage-relative-v1` calculator forms half-open stage intervals from per-sample `process_phase` / `process_stage` records or the configured `recipe_step` mapping. Adjacent samples in the same stage are merged. Repeated occurrences of a stage remain separate and are never mixed. Every stage feature carries the stage code, occurrence order, attribution source, boundaries, valid duration, and coverage. Unattributed samples, missing required stages, and open boundaries become explicit data-quality limitations.

Cycle comparison also divides cycles by their effective inspection outcome and calculates pass-versus-fail medians, robust effects, and a candidate score for the same signal, stage, stage occurrence, and feature. Candidate scores only prioritize investigation. Results list possible product, machine, recipe, and mold confounders and distinguish insufficient data, exploratory results, and stable results. Historical association is never reported as a confirmed root cause.

The deterministic `stage-relative-v1` calculator now persists completed-cycle results through `cycle_analysis_materializations`, `cycle_phases`, and `cycle_features`. A materialization identity includes the cycle, algorithm version, process-data-model version, and analysis-plan version; both the maximum source ingest ID and source-event count must match before a result can be reused. A newly accepted event immediately marks existing materializations for the same cycle `dirty`, and the next cycle-list or historical-comparison read recomputes from the complete event history. Active cycles remain query-time calculations and do not persist unsealed intermediate features. If materialization is unavailable, the service falls back to the same deterministic calculator without changing the numeric and record-checking rules.

## Complete records with bounded transport

Neither the Web timeline nor Chat cycle analysis uses 500 rows as a business-scope limit. APIs may still transport pages of 500 rows, but the page and deterministic analysis tools continue paging until the requested scope is complete. The model receives bounded summaries computed over the full dataset, never an unlimited raw-row payload.

## Isolation and recovery

- Chat creation, history, reads, SSE, and cancellation are user-authorized;
- SSE uses monotonic event sequences; clients resume with `Last-Event-ID`;
- interrupted Chat runs enter an explicit terminal state after service restart;
- event batches are validated as a unit; callers should retain submitted sequences and use `ackSeq` for retries;
- Chat never holds source credentials or calls field networks or equipment interfaces.

See [Ingot Chat](chat.en.md) and the [production event specification](rfc-production-events.en.md).
