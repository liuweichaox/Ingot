# Macro architecture

Ingot consists of Central API, Central Web, fact storage, and source adaptation implemented by each team. Ingot is the trusted production-facts and process-investigation platform; Ingot Chat in Central Web is the main workspace for engineers, with everyday questions and optional bounded multi-agent deeper investigation. Sources enter through the standard event API.

```text
equipment, instruments, business systems, or custom data sources
  → team-owned adaptation and runtime
  → POST /api/v1/events:batch
  → Central API
  → PostgreSQL production facts
  → query, SSE, and Central Web · Ingot Chat
```

For plant-local persistence and an outbox, a team may deploy `Ingot.Connector.Host` as an optional ingress: an adapter submits `ProductionEvent[]` to the Host and the Host batches them to Central. This operating model is owned and operated by the team.

## Product boundaries

- Ingot Chat runs in Central Web and only queries facts, checks data quality, and returns evidence. Its deeper-investigation mode lets process, quality, and challenge roles review the same verified evidence; they cannot access equipment or expand the data scope themselves.
- Teams own device protocols, field mapping, credentials, offline buffering, retries, and local process supervision.
- `Ingot.Connector.Host` is an optional team-operated local ingress and outbox; teams own source-adaptation implementation and runtime operation.
- `POST /api/v1/events:batch` accepts standard event batches after token and contract validation.
- `POST /api/v1/inspection-records` accepts independent inspection facts.
- Fact query, Chat, and SSE are delivered through Central API.
- Real-time control, safety interlocks, and equipment writes are outside Ingot.

## Storage and network

- PostgreSQL stores central production events, inspection records, and query facts.
- SQLite can support optional local cache, logs, or edge runtime state; teams choose its operating model.
- Chat reads only through Central fact services; model configuration never changes data permissions or the tool allowlist.
- Production deployments should place Central and the database behind controlled network boundaries and use TLS, token rotation, and minimum data scope.

See [Ingot Chat](chat.en.md), the [production event specification](rfc-production-events.en.md), and [deployment](tutorial-deployment.en.md).
