# Macro architecture

Ingot has three separate product surfaces: Central Web, Ingot Agent desktop, and Connector Host. Central Web presents production facts and Chat. The desktop Agent owns connector code engineering only. Connector Host accepts normalized events only.

```text
Central Web
  ├─ fact pages → Central API → PostgreSQL
  └─ Chat → read-only tools → Central fact services

Ingot Agent Desktop
  → Agent API
  → Actor-isolated workspace
  → fixed container build/test
  → operator approval
  → SHA-256 ZIP

external connector
  → Ingot.Connector.Host
  → SQLite event log/outbox
  → Central API
```

## Product boundaries

- Chat runs in Central Web and only queries facts, checks data quality, and returns evidence.
- Ingot Agent runs as a Tauri desktop app and only generates, builds, tests, repairs, and packages connector code.
- `/api/v1/chat/*` and `/api/v1/agent/*` use distinct product identifiers, permissions, and histories.
- Agent endpoints require `X-Ingot-Client: ingot-agent-desktop`; the browser cannot enter the code workflow.
- Connector Host contains no device protocol or register semantics and accepts only `ProductionEvent[]`.
- An external runtime deploys, starts, and supervises generated connectors.
- Real-time control, safety interlocks, and equipment writes are outside Ingot.

## Storage

- PostgreSQL stores central production events, inspection records, and query facts.
- SQLite Agent Store keeps Chat/Agent runs, SSE events, and artifact metadata, isolated by surface and Actor.
- Governed directories hold desktop Agent workspaces, packaging-approval metadata, and content-addressed ZIPs.
- Connector Host SQLite stores shop-floor events, context, and the bounded outbox.

## Network

The default Compose stack binds Central, Connector Host, and PostgreSQL to the host loopback interface. Chat reads only through Central fact services. Configured model APIs serve model calls; fixed build/test child containers are always network-disabled. External connectors submit events with a separate Connector Token.

See [Chat](chat.en.md), [Ingot Agent desktop](desktop-agent.en.md), and [deployment](tutorial-deployment.en.md).
