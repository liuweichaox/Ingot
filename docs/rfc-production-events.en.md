# Production Event Specification

`ProductionEvent` is the immutable event envelope shared by connectors, Connector Host, Central API, and Chat fact tools.

## JSON contract

| Field | Type | Constraint |
|---|---|---|
| `eventId` | string | UUIDv7 global event identifier |
| `eventType` | string | lowercase dotted name such as `cycle.started` |
| `eventTypeVersion` | integer | greater than 0; defaults to 1 |
| `occurredAt` | ISO 8601 timestamp | UTC time at which the connector observed the fact |
| `recordedAt` | ISO 8601 timestamp | required in the connector request; Connector Host replaces it with host UTC at persistence |
| `source` | string | source path inside the connector; Host prefixes `edge/{EdgeId}/` |
| `subject` | object | non-empty `type` and `id` |
| `context` | object | string key/value context; may be `{}` but not null |
| `data` | object | event-specific fields; may be `{}` |
| `correlationId` | string or null | optional cycle or business-chain identifier |
| `seq` | integer | connectors send 0; Connector Host assigns a monotonic local sequence |

Example:

```json
{
  "eventId": "0198a820-7f91-7a5d-8c44-111111111111",
  "eventType": "cycle.started",
  "eventTypeVersion": 1,
  "occurredAt": "2026-07-18T08:00:00Z",
  "recordedAt": "2026-07-18T08:00:00Z",
  "source": "connector/FURNACE-01",
  "subject": { "type": "asset", "id": "FURNACE-01" },
  "context": { "workpiece_id": "WP-001", "lot": "LOT-0718" },
  "data": {},
  "correlationId": "CYCLE-001",
  "seq": 0
}
```

## Connector Host ingress

```http
POST /api/v1/connector-events
Authorization: Bearer <connector-host-token>
Content-Type: application/json
```

The request body is `ProductionEvent[]`. Connector Host:

1. validates the Bearer token and `ConnectorHost:MaxBatchSize`;
2. rejects an empty source or a source claiming another Edge;
3. normalizes the source to `edge/{EdgeId}/{connector-source}`;
4. resets `seq` to 0 and sets `recordedAt` from host UTC;
5. runs `ProductionEventValidator`;
6. commits to the SQLite event log, which assigns local `seq`;
7. returns `202 Accepted`, event IDs, and local sequences.

## Delivery and idempotency

```text
Connector
  → Connector Host
  → SQLite event log + outbox
  → POST /api/v1/events:batch
  → Central PostgreSQL fact store
```

Delivery retries with at-least-once semantics. Central deduplicates by `EventId` and `(EdgeId, Seq)` and returns an acknowledged `AckSeq`. Connector Host marks outbox records shipped only after a valid acknowledgment. This mechanism does not promise end-to-end exactly-once delivery.

`Events:MaxBacklogRows` sets a hard outbox limit of 500,000 records by default. At the limit, Connector Host deletes the oldest unshipped events and emits a `diagnostic.backlog_dropped` audit event with the count and sequence range plus the `event_backlog_dropped_total` metric. Deployments must monitor both signals.

Central requires every source in a batch to start with `edge/{EdgeId}/`. Connector Host source normalization makes connector submissions satisfy that contract.

## Queries and fact chains

- `GET /api/v1/events` filters by Edge, event type, subject, context, `correlationId`, and time.
- `GET /api/v1/events/stream` publishes event SSE.
- `GET /api/v1/cycles/{correlationId}` returns events sharing the correlation ID.
- `get_cycle_trace` orders cycle events by occurrence and central ingest order and returns `EvidenceRef` values.
- `check_data_quality` checks cycle pairing, empty context, Edge sequence gaps, and latest event time.

Inspection records use a separate `InspectionRecord` contract and API. Current `get_cycle_trace` does not automatically merge inspections.

## Extension rules

- Use stable business names for `eventType` and `data` fields.
- Carry numeric units as explicit event fields; the core does not infer units.
- Define source quality codes and timestamp semantics in the connector specification.
- Increase `eventTypeVersion` or introduce a new event type for incompatible semantics.
- Keep `context` to stable string identifiers needed for queries; never put secrets or large objects in it.
