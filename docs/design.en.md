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

Quality inspection follows a production cycle; a blank “inspection entry” form is not modeled as a standalone business object:

1. A `cycle.completed` event matches a published `InspectionPlan` by product, recipe, machine, and effective time. No task is invented when no plan matches.
2. The plan references versioned `InspectionDefinition` records and configures required checks, ordering, original evidence, and review requirements. Selecting a task carries read-only workpiece and operation-run identifiers into the form.
3. Upload original images to controlled long-term storage first, compute SHA-256 on the server, and then store their references with the inspection record.
4. Historical inspection records can reopen the original image and append an immutable review decision. Original access and review actions are audited; derivative thumbnails cannot replace the original.
5. An inspection result records an observation and is not a QMS release or equipment-control decision.

The global navigation therefore exposes a “Quality Inspection” workbench. “Create inspection record” is an action inside that workbench. Workpiece and operation-run identifiers can only come from pending tasks; manual cycle entry is not available. Inspection submission, original-image access, and review all use the signed-in Platform identity and roles, with no inspection-specific user or access password.

The Quality Plans page maintains inspection definitions and plan versions. Published plan and definition versions cannot be overwritten; changes require a new version. Optical-molding vision and manual checks exist only in the optional sample configuration, never as Platform defaults.

Roles come from unified identity claims: `quality.inspector` submits inspections, `quality.reviewer` reviews originals, `process.engineer` queries records and historical comparisons, and `platform.admin` manages the platform. Development may use a built-in identity for evaluation; production requires host authentication to establish the user identity.

## Same-series historical cycle comparison

`GET /api/v1/cycle-comparisons/{correlationId}` uses one cycle as the baseline and selects only historical cycles with the same `context.product_series`. The comparison service reads every transport page, computes sample completeness, configured required-phase completeness, and statistics for signals marked `useInComparison`, then joins recipe version, inspection outcomes, and the latest original-image review. Phase count, signal codes, and display names come from published process configuration rather than Platform code. The page's limit of 50 controls the number of historical cycles selected; it is not a per-cycle sample-row limit.

## Complete records with bounded transport

Neither the Web timeline nor Chat cycle analysis uses 500 rows as a business-scope limit. APIs may still transport pages of 500 rows, but the page and deterministic analysis tools continue paging until the requested scope is complete. The model receives bounded summaries computed over the full dataset, never an unlimited raw-row payload.

## Isolation and recovery

- Chat creation, history, reads, SSE, and cancellation are user-authorized;
- SSE uses monotonic event sequences; clients resume with `Last-Event-ID`;
- interrupted Chat runs enter an explicit terminal state after service restart;
- event batches are validated as a unit; callers should retain submitted sequences and use `ackSeq` for retries;
- Chat never holds source credentials or calls field networks or equipment interfaces.

See [Ingot Chat](chat.en.md) and the [production event specification](rfc-production-events.en.md).
