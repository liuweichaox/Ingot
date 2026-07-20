# Modules

| Project | Responsibility |
|---|---|
| `Ingot.Platform.Api` | Platform HTTP, event ingestion, authorization, query, SSE, and Chat API |
| `Ingot.Platform.Infrastructure` | platform production records, inspections, webhooks, and Chat record tools |
| `Ingot.Contracts` | public event, inspection, and Chat HTTP contracts |
| `Ingot.Domain` | production events, subject references, and domain validation |
| `Ingot.Edge.Application` | application-service abstractions |
| `Ingot.Edge.Infrastructure` | storage, logging, metrics, and runtime implementations |
| `Ingot.Platform.Web` | record views, inspections, logs, metrics, and the Ingot Chat interface |
| `site` / `docs-site` | product website and static documentation site |

`scripts/verify-architecture.sh` checks dependency boundaries. `scripts/verify-product-scope.sh` checks the public product scope.
