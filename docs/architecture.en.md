# Macro architecture

Ingot consists of Platform API, Platform Web, fact storage, and source adaptation implemented by each team. Ingot is the trusted production-facts and process-investigation platform; Ingot Chat in Platform Web is the main workspace for engineers, with everyday questions and optional bounded multi-agent deeper investigation. Sources enter through the standard event API.

```text
equipment, instruments, business systems, or custom data sources
  → team-owned adaptation and runtime
  → POST /api/v1/events:batch
  → Platform API
  → TimescaleDB (PostgreSQL + time-series extension) production facts
  → query, SSE, and Platform Web · Ingot Chat
```

For plant-local persistence and an outbox, a team may deploy `Ingot.Edge.ConnectorHost` as an optional ingress: an adapter submits `ProductionEvent[]` to the Host and the Host batches them to Platform. This operating model is owned and operated by the team.

## Product boundaries

- Ingot Chat runs in Platform Web and only queries facts, checks data quality, and returns evidence. Its deeper-investigation mode lets process, quality, and challenge roles review the same verified evidence; they cannot access equipment or expand the data scope themselves.
- Teams own device protocols, field mapping, credentials, offline buffering, retries, and local process supervision.
- `Ingot.Edge.ConnectorHost` is an optional team-operated local ingress and outbox; teams own source-adaptation implementation and runtime operation.
- `POST /api/v1/events:batch` accepts standard event batches after token and contract validation.
- `POST /api/v1/inspection-records` accepts independent inspection facts.
- Fact query, Chat, and SSE are delivered through Platform API.
- Real-time control, safety interlocks, and equipment writes are outside Ingot.

## Storage and network

- TimescaleDB (PostgreSQL + time-series extension) stores platform production events, inspection records, and query facts. The production-event table is a hypertable auto-chunked by `occurred_at`, with optional chunk compression and retention policies by configuration; idempotent dedup stays in the separate `event_ingest_keys` table. Self-hosted locally (Docker image `timescale/timescaledb`), no managed service required.
- SQLite can support optional local cache, logs, or edge runtime state, and is the optional fact-store form for offline/air-gapped single-box deployments; teams choose its operating model.
- Chat reads only through Platform fact services; model configuration never changes data permissions or the tool allowlist.
- Production deployments should place Platform and the database behind controlled network boundaries and use TLS, token rotation, and minimum data scope.

See [Ingot Chat](chat.en.md), the [production event specification](rfc-production-events.en.md), and [deployment](tutorial-deployment.en.md).
