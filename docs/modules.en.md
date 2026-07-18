# Modules

| Project | Responsibility |
|---|---|
| `desktop` | Ingot Agent Desktop: Tauri 2, Rust network boundary, React 19 UI, and connector-code workspace |
| `Ingot.Agent` | product-surface isolation, provider-neutral run state machine, typed plans, budgets, cancellation, and verification |
| `Ingot.Agent.Infrastructure` | model adapters, SQLite run store, workspace tools, and artifact tools |
| `Ingot.Connector.Builder` | Actor-isolated source, fixed container build/test, packaging-approval gate, and SHA-256 ZIP |
| `Ingot.Connector.Host` | normalized `ProductionEvent[]` ingress, SQLite event log, context, and outbox |
| `Ingot.Central.Infrastructure` | central production facts, inspections, webhooks, and read-only Chat tools |
| `Ingot.Central.Api` | `/api/v1/chat/*`, desktop-only `/api/v1/agent/*`, fact HTTP, authorization, and SSE |
| `Ingot.Central.Web` | Chat, event, inspection, log, and metric UI; no Agent code-generation surface |
| `Ingot.Contracts` | Chat, Agent, connector, event, and inspection HTTP contracts |
| `Ingot.Domain` | production events, object references, and domain validation |
| `Ingot.Application` | event-log, shipping, and context abstractions |
| `Ingot.Infrastructure` | SQLite events, outbox, central reporting, context, logs, and metrics |
| `site` / `docs-site` | product website and static documentation |

`scripts/verify-architecture.sh` validates dependency direction. `scripts/verify-product-scope.sh` validates product naming and public scope.
