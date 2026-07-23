# Macro architecture

Ingot consists of Platform API, Platform Web, production-record storage, and source connections implemented by each team. Ingot Chat is the main workspace for engineers, with quick queries and optional combined analysis. Sources enter through the standard production-record API.

```text
equipment, instruments, business systems, or custom data sources
  → team-owned adaptation and runtime
  → POST /api/v1/events:batch
  → Platform API
  → TimescaleDB (PostgreSQL + time-series extension) production records
  → query, SSE, and Platform Web · Ingot Chat
```

For plant-local persistence and an outbox, a team may deploy `Ingot.Edge.ConnectorHost` as an optional ingress: an adapter submits `ProductionEvent[]` to the Host and the Host batches them to Platform. This operating model is owned and operated by the team.

## Product boundaries

- Ingot Chat runs in Platform Web and only queries records, checks whether runs are complete, and returns links to original records. Combined analysis lets process, quality, and review perspectives examine the same saved production records; they cannot access equipment or expand the data scope themselves.
- Teams own device protocols, field mapping, credentials, offline buffering, retries, and local process supervision.
- `Ingot.Edge.ConnectorHost` is an optional team-operated local ingress and outbox; teams own source-adaptation implementation and runtime operation.
- `POST /api/v1/events:batch` accepts standard event batches after token and contract validation.
- `GET /api/v1/inspection-tasks` derives pending work from completed cycles; `POST /api/v1/inspection-records` accepts inspection records linked to those tasks.
- `POST /api/v1/inspection-plans` saves and publishes versioned quality plans. Product, recipe, machine, and effective-time selectors determine required checks; Platform contains no built-in industry inspection codes.
- Original vision-inspection images are written by `POST /api/v1/inspection-attachments` to the persistent `Data/inspection-attachments` volume, with SHA-256 computed by the server. `GET /api/v1/inspection-attachments/{attachmentId}/content` serves the original file for review. Event-retention policies do not delete these originals.
- `POST /api/v1/inspection-reviews` appends visual-review decisions, while `GET /api/v1/inspection-reviews/audit` queries original-access and review audit events. These endpoints use unified Platform identity roles.
- `GET /api/v1/cycle-comparisons/{correlationId}` performs deterministic full-sample comparison across historical cycles in the same product series.
- `GET /api/v1/cycles` powers a cycle-level production workspace that combines production state, complete samples, configured phases, and quality-plan execution. High-frequency samples are read through the ingest cursor without treating a transport page size as a business truncation limit.
- Platform Web uses Production Cycles as a daily-work entry, groups Data Quality and Historical Comparison under analysis and governance, and keeps raw production events in Event Query for diagnosis and traceability. Inspection entry opens from a pending task in a drawer instead of permanently occupying the left side of the workbench.
- Event Query loads older records on demand with the `beforeIngestId` cursor and starts live streaming after the latest displayed ID. The page never renders the entire event history at once, while complete-cycle queries and analysis still traverse every page internally.
- record query, Chat, and SSE are delivered through Platform API.
- Real-time control, safety interlocks, and equipment writes are outside Ingot.

## Storage and network

- TimescaleDB (PostgreSQL + time-series extension) stores platform production events, inspection records, and query records. The production-event table is a hypertable auto-chunked by `occurred_at`, with optional chunk compression and retention policies by configuration; idempotent dedup stays in the separate `event_ingest_keys` table. Self-hosted locally (Docker image `timescale/timescaledb`), no managed service required.
- Controlled attachment directory: original vision images are retained by content hash, with metadata in PostgreSQL and file bodies in the Docker `Data` volume. Thumbnails and annotated images are derivatives and cannot replace the original.
- SQLite can support optional local cache, logs, or edge runtime state, and is the optional record-store form for offline/air-gapped single-box deployments; teams choose its operating model.
- Chat reads only through Platform record services; model configuration never changes data permissions or the tool allowlist.
- Production deployments should place Platform and the database behind controlled network boundaries and use TLS, token rotation, and minimum data scope.

See [Ingot Chat](chat.en.md), the [production event specification](rfc-production-events.en.md), and [deployment](tutorial-deployment.en.md).
