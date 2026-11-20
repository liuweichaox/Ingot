# Data integration

> Document status: **Current technical guidance**. External adapters handle field communication and conversion; Edge receives, persists, and delivers events to Platform.

## Integration boundary

Ingot does not include equipment protocol drivers. A site adapter owns its equipment, gateway, or existing collector protocols, sampling frequency, triggers, and PLC writes. Engineers may write adapters with AI assistance; before use, verify real values, safety boundaries, and failure handling in the target environment.

Edge authenticates incoming requests, persists events in a local SQLite log, delivers them, and reports its health. Platform keeps a separate system of record and enforces site isolation, event ingestion, and process-data admission. Intake confirms arrival at the relevant layer. Process quality and optimization results require separate verification.

## Identity and data semantics

Each Edge binds to a stable `SiteId` and a distinct `EdgeId`. The adapter must use a reliable `ExecutionId` to associate events, actual parameters, and inspection with the same run; do not infer associations from timestamps alone. The adapter supplies the actual occurrence time, source, equipment identity, and process context, and converts values to the codes, types, and units of the published process data model.

`OccurredAt` is the source occurrence time. Edge records receipt time and assigns a local sequence; Platform ingestion time is separate. Preserve occurrence time when replaying after an outage. Do not represent recipe setpoints as actual parameters; keep missing, invalid, and unknown data visible.

## Site mapping and local storage

- **Points and runs**. Reuse point configuration where available. Retain the source reference, equipment, source and target units, type, and versioned conversion rule. Map values to stable codes in the published process data model. Use actual signals to form `process.execution.started` and `process.execution.completed` events. Use the same `ExecutionId` for samples, actual parameter readbacks, and inspection associations. Preserve inspection provenance and review state. Store times in UTC, retaining the source time zone or offset and a separate receipt time.
- **Samples and quality**. Group values from one read into `data.values` in a `process.sample` event, retaining the actual sample time. Supply published model identity and version, or recipe identity and version, in context. Use `data.quality.qualityVersion=1` with a `valueFlags` object for versioned point quality. Missing or unsupported versions are treated as uncertain. Flag points with unknown quality explicitly and verify their projection in Platform. See the [data model](data-model.en.md) for formal point, frame, and value tables.
- **Local records and replay**. If local persistence is needed, retain point versions, runs, samples, parameter readbacks, inspection references, and a delivery queue. The adapter chooses its storage and can reuse a reliable existing collector. Persist the event ID and content before sending and reuse them on retry. Query by site, equipment, and run identity. Record Edge acceptance, Platform receipt, inspection review, and optimization admission separately. Exercise full queues, duplicates, outage retries, clock differences, and backup and recovery during site acceptance.

## Submitting events to Edge

An external adapter sends a JSON array of events to `POST /api/v1/connector-events` with `Authorization: Bearer <ConnectorHost:IngestToken>`. The deployer must provide credentials securely, without putting them in source code or example payloads. The default batch limit is 1000 events; configure `ConnectorHost:MaxBatchSize` within 1–10000.

The event contract is defined in `src/shared/Ingot.Domain/Events/ProductionEvent.cs` and `ProductionEventValidator.cs`. `EventId` must be UUIDv7; `EventType` uses lowercase dot-separated names; `Subject` requires a type and identifier; `OccurredAt` must be valid. The adapter supplies stable event identity and retains the original event and identity for retries. Edge validates and restricts source identity to the node, computes an event hash, persists the events, and returns their identifiers and local sequences.

For `process.sample`, `data.values` must be an object keyed by published process data model codes. Value types must match the model and required items must be present. Platform must also resolve a published model from the event context. Passing Edge format validation does not mean Platform has admitted the event.

While an event remains in the Edge log, retrying the same `EventId` and business content returns its original sequence without adding another pending event. Reusing an identity with different content returns HTTP 409 and rolls back the batch. Edge-generated recording time, sequence, and hash are excluded from the comparison. Acknowledged events may be removed by retention and a full queue may drop the oldest pending events; local deduplication is not guaranteed after removal, so adapters must monitor and reconcile delivery. The [external adapter example](../examples/external-adapter/README.md) verifies Edge intake only, not Platform admission or the complete run workflow.

## Delivery and failure handling

HTTP `202 Accepted` means Edge accepted the batch and persisted it in its local event log; it does not mean Platform has received it or business analysis has completed. At the queue's hard limit, the same transaction may drop the oldest pending events, including events from this batch, even when it returns 202. Edge advances its local pending queue after Platform confirmation. Operators should monitor health, backlog, and rejections, and provision sufficient persistent storage and backups. Adapters should also retain events not accepted by Edge and handle errors and retries.

Do not blindly resend an ID after its original event has been removed or dropped locally: Edge may assign a new sequence and return 202, while Platform may reject and quarantine it because the existing ID has a different sequence or hash. First reconcile Platform receipt and Edge quarantine records, then follow the site's recovery process. End-to-end idempotency across the retention boundary is not currently guaranteed.

Each Edge instance needs its own persistent directory. If the directory changes or the pending queue is lost, use a new `EdgeId` to avoid resetting sequences under the same identity. Configure separate ingestion, node diagnostics, and Platform delivery credentials; see [Deployment](deployment.en.md) for deployment and recovery.

## Acceptance

In the target environment, verify at least that authentication failures and invalid events are rejected; a real run and quality result are correctly associated; values, types, units, and timestamps match their sources; delivery resumes after a temporary outage; and duplicate submissions, full queues, and invalid data are handled as the site requires. Check Platform ingestion and data quality status, and record adapter version, configuration, and test results. Acceptance of one site does not validate other equipment or factories.
