# Design

Ingot is edge-first production event infrastructure. PLC is the first source adapter; production events are the stable product model exposed to consumers.

## Dual planes

```text
Source
  |-- telemetry --> ChannelCollector --> QueueService --> TSDB
  `-- changes ----> EventRuleEvaluator --> EventSink --> events.db
                                                  |--> query / SSE
                                                  |--> TSDB projection
                                                  `--> EventShipper --> Central API --> PostgreSQL
```

Telemetry answers “what is the value now?” It is high-rate, batched in memory, and at-most-once. A failed TSDB batch is logged and dropped.

Events answer “what happened?” They use `ProductionEvent`, are appended to SQLite before any projection, and remain queryable across restarts. `EventId`, monotonic edge `Seq`, and `CorrelationId` provide idempotency, cursors, and cycle correlation.

## Source and asset separation

`SourceCode` identifies a communication endpoint. `ObjectRef` identifies a business asset. Schema v2 adds `Adapter`, `Profile`, `Asset`, and `EventRules`; v1 `PlcCode` remains a compatibility alias only.

## Implemented event foundation

- v1 Conditional channels emit cycle and diagnostic events
- v2 rules support edge pairs, value changes, bit flags, and thresholds
- successful PLC control writes emit `parameter.applied`
- `EdgeStateStore` persists active cycles and asset context
- `/api/v1/events`, SSE, cycle lookup, and context APIs are local and edge-autonomous
- `core` and `optical` Profiles validate object types, event types, and required context

## Storage semantics

| File | Purpose |
|---|---|
| `events.db` | immutable production events, edge sequence, shipping state |
| `acquisition-state.db` | active cycles and asset context |
| `logs.db` | runtime logs |

Every dynamic context key/value is written in the same append transaction to `event_context(event_seq, ctx_key, ctx_value)`. The `(ctx_key, ctx_value, event_seq)` index supports `ctx.*` queries without predeclaring industry fields, and existing databases are backfilled from `context_json` on startup.

An event-log append failure records the latest error and consecutive failure count and degrades `/health`; a later successful append recovers it. A TSDB projection failure does not revoke an event that already exists. `EventShipper` reads pending events in edge-sequence order, retries with exponential backoff, safely resends an unacknowledged batch, and advances local shipped state only after a valid `AckSeq`.

Central uses PostgreSQL monthly partitions, JSONB/GIN context indexes, a central `IngestId` cursor, per-edge bearer tokens, and unique constraints on both `EventId` and `(EdgeId, Seq)`. Before persistence it validates UUIDv7 identity, event type/version, subject, context, positive sequence, in-batch identity uniqueness, and that `Source` belongs to the token-bound Edge. Transport is at-least-once while central persistence has an exactly-once effect.

Webhook subscriptions are also durable in PostgreSQL. Each subscription has its own central cursor; matching events advance only after a 2xx response, so failures are safely retried. Delivery uses CloudEvents 1.0 structured JSON and can include `X-Ingot-Signature: sha256=...` using the subscription secret.

`scripts/benchmark-edge-event-log.sh` and `scripts/benchmark-central-ingest.sh` enforce the RFC performance gates for million-row local append/query latency and real HTTP-to-PostgreSQL ingest throughput and sequence integrity.

Source configurations are validated strictly before runtime and then checked against their Profile. Unknown JSON members (including misspelled keys), duplicate source/rule identifiers, invalid adapter parameters, and undeclared object or event types are rejected.

Edge and Central share the same query-boundary validation. Reversed time ranges, negative cursors, out-of-range limits, invalid context keys, and malformed `Last-Event-ID` values return HTTP 400.

## API

- `GET /health`
- `GET /metrics`
- `GET /api/v1/events`
- `GET /api/v1/events/stream`
- `GET /api/v1/cycles/{correlationId}`
- `GET|PUT /api/v1/context/{subjectType}/{subjectId}`
- `GET /api/acquisition/plc-connections`
- `POST /api/acquisition`

Central API exposes:

- `POST /api/v1/events:batch`
- `GET /api/v1/events`
- `GET /api/v1/events/stream`
- `GET /api/v1/cycles/{correlationId}`
- `POST|GET /api/v1/subscriptions`
- `GET|DELETE /api/v1/subscriptions/{subscriptionId}`
- `PUT /api/v1/subscriptions/{subscriptionId}/enabled`

See the [Production Events RFC](rfc-production-events.md) for the full roadmap and trade-offs.
