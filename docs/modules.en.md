# Modules

| Project | Responsibility |
|---|---|
| `Ingot.Central.Api` | Central HTTP, event ingestion, authorization, query, SSE, and Chat API |
| `Ingot.Central.Infrastructure` | central production facts, inspections, webhooks, and Chat fact tools |
| `Ingot.Contracts` | public event, inspection, and Chat HTTP contracts |
| `Ingot.Domain` | production events, subject references, and domain validation |
| `Ingot.Application` | application-service abstractions |
| `Ingot.Infrastructure` | storage, logging, metrics, and runtime implementations |
| `Ingot.Central.Web` | fact views, inspections, logs, metrics, and the Ingot Chat interface |
| `site` / `docs-site` | product website and static documentation site |

`scripts/verify-architecture.sh` checks dependency boundaries. `scripts/verify-product-scope.sh` checks the public product scope.
