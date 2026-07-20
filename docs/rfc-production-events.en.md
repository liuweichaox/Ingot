# Production Event Specification

`ProductionEvent` is the immutable event envelope shared by team-owned source adaptation, Platform API, query, and Ingot Chat. Teams map equipment, instrument, or business-system semantics to this contract and submit it through Platform API.

## Batch request

```http
POST /api/v1/events:batch
Authorization: Bearer <edge-token>
Content-Type: application/json
```

```json
{
  "edgeId": "EDGE-001",
  "events": [
    {
      "eventId": "0198a820-7f91-7a5d-8c44-111111111111",
      "eventType": "cycle.started",
      "eventTypeVersion": 1,
      "occurredAt": "2026-07-18T08:00:00Z",
      "recordedAt": "2026-07-18T08:00:00Z",
      "source": "edge/EDGE-001/furnace/FURNACE-01",
      "subject": { "type": "asset", "id": "FURNACE-01" },
      "context": { "workpiece_id": "WP-001", "lot": "LOT-0718" },
      "data": {},
      "correlationId": "CYCLE-001",
      "seq": 1
    }
  ]
}
```

## `ProductionEvent` fields

| Field | Type | Constraint |
|---|---|---|
| `eventId` | string | UUIDv7 global event identifier |
| `eventType` | string | lowercase dotted name such as `cycle.started` |
| `eventTypeVersion` | integer | greater than 0; defaults to 1 |
| `occurredAt` | ISO 8601 timestamp | UTC time at which the fact occurred |
| `recordedAt` | ISO 8601 timestamp | UTC time at which the adapter recorded or submitted the event |
| `source` | string | must start with `edge/{edgeId}/` |
| `subject` | object | non-empty `type` and `id` |
| `context` | object | string values needed for query association; may be `{}` |
| `data` | object | event-specific fields; may be `{}` |
| `correlationId` | string or null | optional cycle or business-chain identifier |
| `seq` | integer | persisted, monotonically increasing sequence within one `edgeId` |

## Validation and response

- `edgeId` may contain letters, numbers, dots, underscores, and hyphens, with 1–128 characters;
- every batch contains 1–500 events;
- `eventId` and `seq` cannot repeat within a batch;
- every `source` must match the request `edgeId`;
- the Bearer token must match the configured token for that `edgeId`;
- Platform deduplicates by `eventId` and `(edgeId, seq)`.

A successful response contains:

```json
{
  "accepted": 1,
  "duplicates": 0,
  "ackSeq": 1,
  "gapDetected": false
}
```

`ackSeq` means all preceding contiguous sequences are safely accepted or recognized as duplicates. Callers should persist unacknowledged events and retry from acknowledgments; this does not promise end-to-end exactly-once delivery.

## Optional Connector Host ingress

`Ingot.Edge.ConnectorHost` is a plant-local ingress and SQLite outbox that a team may deploy itself. Teams deploy and operate both source adaptation and the Host. An adapter may submit:

```http
POST http://<host>:8001/api/v1/connector-events
Authorization: Bearer <connector-host-token>
Content-Type: application/json
```

The body is `ProductionEvent[]`. The Host validates the local token and events, assigns local sequence values, persists them, and sends standard batches to Platform. It fits network boundaries that need offline buffering or cannot reach Platform directly. Direct Platform batches and Host ingress use separate tokens; the deployment owns the choice, monitoring, and operation of either path.

Default Compose does not start the Host. Enable the `connector-host` profile when a local ingress is needed:

```bash
export INGOT_CONNECTOR_TOKEN="$(openssl rand -hex 24)"
docker compose -f docker-compose.app.yml --profile connector-host up -d connector-host
```

## Query and fact chains

- `GET /api/v1/events`: filter by Edge, event type, subject, context, `correlationId`, and time;
- `GET /api/v1/events/stream`: production-event SSE;
- `GET /api/v1/cycles/{correlationId}`: the event chain for one correlation ID;
- `get_cycle_trace`: a cycle timeline with evidence, ordered by occurrence time and Platform ingest order;
- `check_data_quality`: cycle pairing, empty context, sequence gaps, and latest-event-time checks.

Inspection facts use a separate `InspectionRecord` contract and API. Current cycle tools build cycle fact chains from production events.

## Extension rules

- Use stable business names for `eventType` and `data`;
- carry numeric units as explicit event fields; the core does not infer units;
- increase `eventTypeVersion` or introduce an event type for incompatible semantics;
- keep `context` to stable strings needed for query association; never put secrets or large objects there;
- teams maintain source protocols, mapping code, buffering, and retry logic.
