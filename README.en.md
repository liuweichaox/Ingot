<a id="readme-top"></a>

<div align="center">
  <a href="https://ingotstack.com">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="images/logo/ingot-lockup-dark.svg">
      <img src="images/logo/ingot-lockup.svg" alt="Ingot" width="360">
    </picture>
  </a>

  <h3>Trusted production facts and process investigation</h3>

  <p>
    Bring production records together as traceable facts; use Ingot Chat to ask questions, inspect evidence, and investigate when needed.
  </p>

  <p>
    <a href="https://ingotstack.com"><strong>Website</strong></a>
    ·
    <a href="https://docs.ingotstack.com"><strong>Documentation</strong></a>
    ·
    <a href="https://github.com/liuweichaox/Ingot/issues">Report an issue</a>
  </p>

  <p>
    <a href="https://github.com/liuweichaox/Ingot/actions/workflows/ci.yml"><img src="https://github.com/liuweichaox/Ingot/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
    <a href="LICENSE"><img src="https://img.shields.io/github/license/liuweichaox/Ingot.svg?style=flat&amp;logo=github" alt="MIT License"></a>
    <a href="https://github.com/liuweichaox/Ingot/issues"><img src="https://img.shields.io/github/issues/liuweichaox/Ingot.svg?style=flat&amp;logo=github" alt="GitHub Issues"></a>
  </p>

  <p>English · <a href="README.md">简体中文</a></p>
</div>

<details>
  <summary>Table of contents</summary>
  <ol>
    <li><a href="#about-ingot">About Ingot</a></li>
    <li><a href="#core-capabilities">Core capabilities</a></li>
    <li><a href="#architecture">Architecture</a></li>
    <li><a href="#quick-start">Quick start</a></li>
    <li><a href="#event-ingestion">Event ingestion</a></li>
    <li><a href="#chat">Chat</a></li>
    <li><a href="#public-apis">Public APIs</a></li>
    <li><a href="#repository-layout">Repository layout</a></li>
    <li><a href="#documentation">Documentation</a></li>
    <li><a href="#contributing">Contributing</a></li>
    <li><a href="#license">License</a></li>
  </ol>
</details>

## About Ingot

Ingot is a trusted production-facts and process-investigation platform for manufacturing operations. It brings important records from equipment, instruments, MES, ERP, and custom systems together as queryable, verifiable, and traceable facts. Teams map source data to the standard `ProductionEvent` or `InspectionRecord` contracts and call the public HTTP APIs; Ingot embeds no device protocol and does not replace plant control, scheduling, inventory, or quality-disposition systems.

**Ingot Chat** is the main way engineers use Ingot. It runs in Central Web and reads recorded production facts only: everyday mode checks facts and completeness, while deeper investigation lets process, quality, and challenge roles review the same verified evidence and return candidate explanations for an engineer to assess.

## Core capabilities

| Capability | Implemented scope |
|---|---|
| Standard event ingestion | Teams submit `ProductionEvent` batches with source, subject, context, correlation ID, and sequence through `POST /api/v1/events:batch` |
| Inspection facts | Human or instrument clients submit independent inspection records through `POST /api/v1/inspection-records` |
| Trusted fact store | Production events, inspection records, subjects, context, correlation IDs, query, and SSE |
| Ingot Chat | Natural-language questions, page context, streamed responses, history, limitations, and evidence links |
| Chat fact tools | `check_data_quality` and `get_cycle_trace` |
| Deeper investigation | Bounded process, quality, and challenge roles; at most 3 rounds and 9 turns; produces evidence-backed candidate explanations only |
| Operations | Health checks, Prometheus metrics, and structured logs |

### Security boundary

- Chat calls explicitly registered read-only fact tools only; it cannot execute SQL, scripts, shell commands, file writes, or open network requests.
- Answer numbers and key findings must come from tool results and have evidence references. Insufficient data produces explicit limitations.
- The standard event API accepts only contract- and token-validated batches. Source protocols, credentials, retry strategy, and runtime ownership remain with the implementing team.
- Ingot never writes to PLCs, CNCs, robots, or other field controllers.
- Secrets enter only through environment variables or a secret store, never source code, logs, or repository configuration.

## Architecture

```text
equipment, instruments, MES, ERP, or custom systems
  └─ team-owned source adaptation
       └─ ProductionEvent[] / InspectionRecord
            └─ Central API ──► PostgreSQL production facts
                                    ├─ query and SSE
                                    └─ Central Web · Ingot Chat
```

Teams own source protocols and runtime operation. Ingot owns the fact contract, access control, query, and evidence links. See the [architecture](docs/architecture.en.md) and [production event specification](docs/rfc-production-events.en.md).

<p align="right"><a href="#readme-top">Back to top</a></p>

## Quick start

### Prerequisites

- .NET SDK 10
- Node.js 22.13 or later
- Docker Engine 26 or later with Docker Compose
- OpenSSL for local credentials

### Start Central

```bash
git clone https://github.com/liuweichaox/Ingot.git
cd Ingot

export INGOT_POSTGRES_PASSWORD="$(openssl rand -hex 24)"
export INGOT_EDGE_TOKEN="$(openssl rand -hex 24)"
export INGOT_OPERATOR_TOKEN="$(openssl rand -hex 24)"

docker compose -f docker-compose.app.yml up -d --build
```

| Service | URL |
|---|---|
| Central Web | <http://localhost:3000> |
| Central API | <http://localhost:8000> |
| Central health | <http://localhost:8000/health> |

The default Compose stack starts PostgreSQL, Central API, and Central Web. Chat and Connector Host are enabled only when needed. `INGOT_OPERATOR_TOKEN` protects inspection-fact submission; Chat uses a separate `INGOT_CHAT_OPERATOR_TOKEN`.

### Enable Chat in production

Production Compose leaves Chat disabled by default. Provide the model, model key, Chat Actor token, and data scope together before enabling it:

```bash
export INGOT_CHAT_ENABLED=true
export INGOT_CHAT_PROVIDER=OpenAI
export INGOT_CHAT_FAST_MODEL="<fast-model>"
export INGOT_CHAT_REASONING_MODEL="<reasoning-model>"
export OPENAI_API_KEY="<secret>"
export INGOT_CHAT_OPERATOR_TOKEN="$(openssl rand -hex 24)"
export INGOT_CHAT_OPERATOR_ALLOW_ALL=true
export INGOT_CHAT_ENABLE_DEEP_INVESTIGATION=true

docker compose -f docker-compose.app.yml up -d --build
```

The browser and Chat HTTP API use Actor `operator` with `INGOT_CHAT_OPERATOR_TOKEN`. Configure each production Actor with the data scope it needs; this Compose example gives `operator` access to all facts.

See [getting started](docs/tutorial-getting-started.en.md) and [configuration](docs/tutorial-configuration.en.md) for the complete walkthrough.

## Event ingestion

Implement one adapter per source type in the runtime that suits your environment. The adapter maps source data to `ProductionEvent`, persists its local sequence, and submits it to Central with an Edge credential. Every event `source` must start with `edge/{edgeId}/`.

```bash
curl -X POST http://localhost:8000/api/v1/events:batch \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer ${INGOT_EDGE_TOKEN}" \
  -d '{
    "edgeId": "EDGE-001",
    "events": [{
      "eventId": "0198a820-7f91-7a5d-8c44-111111111111",
      "eventType": "cycle.started",
      "eventTypeVersion": 1,
      "occurredAt": "2026-07-18T08:00:00Z",
      "recordedAt": "2026-07-18T08:00:00Z",
      "source": "edge/EDGE-001/furnace/FURNACE-01",
      "subject": { "type": "asset", "id": "FURNACE-01" },
      "context": { "workpiece_id": "WP-001" },
      "data": {},
      "correlationId": "CYCLE-001",
      "seq": 1
    }]
  }'
```

Each batch accepts 1–500 events. Central deduplicates by `eventId` and `(edgeId, seq)` and returns `ackSeq`. See the [production event specification](docs/rfc-production-events.en.md) for the contract, validation rules, and retry guidance.

When an adapter runs on a plant network, needs a local SQLite outbox, or cannot reach Central directly, a team can enable **Ingot.Connector.Host** as an optional local ingress. It accepts `ProductionEvent[]`, persists them, and ships them to Central with at-least-once delivery. Teams choose, deploy, and operate this path.

```bash
export INGOT_CONNECTOR_TOKEN="$(openssl rand -hex 24)"
docker compose -f docker-compose.app.yml --profile connector-host up -d connector-host
```

See the [production event specification](docs/rfc-production-events.en.md).

## Chat

After configuring Chat for production, open <http://localhost:3000>, select **Chat**, and ask a question with optional asset or cycle context:

```text
What happened during this cycle, and is its data complete?
```

Chat uses governed read-only fact tools and returns findings, limitations, and source-fact references. It does not change events, inspection records, configuration, or equipment state.

See [Chat](docs/chat.en.md) for the complete capability and API reference.

## Public APIs

| Endpoint | Purpose |
|---|---|
| `POST /api/v1/events:batch` | Submit a standard production-event batch |
| `GET /api/v1/events` | Query production events |
| `GET /api/v1/events/stream` | Subscribe to production events over SSE |
| `GET /api/v1/cycles/{correlationId}` | Read the event timeline for one correlation ID |
| `POST /api/v1/inspection-records` | Submit a human or instrument inspection record |
| `GET /api/v1/chat/capabilities` | Discover Chat availability, tools, and run limits |
| `POST /api/v1/chat/runs` | Create a Chat run |
| `GET /api/v1/chat/runs/{runId}` | Read an answer, plan, tool activity, and evidence |
| `GET /api/v1/chat/runs/{runId}/stream` | Stream Chat events over SSE with resume support |
| `POST /api/v1/chat/runs/{runId}:cancel` | Cancel a Chat run |

## Repository layout

```text
Ingot/
├── src/
│   ├── Ingot.Central.Api/            Central HTTP, authorization, and SSE
│   ├── Ingot.Central.Infrastructure/ central facts, inspections, webhooks, and Chat fact tools
│   ├── Ingot.Contracts/              event, inspection, and Chat HTTP contracts
│   ├── Ingot.Domain/                 production events, subject references, and domain validation
│   ├── Ingot.Application/            application-service abstractions
│   └── Ingot.Infrastructure/         storage, logging, metrics, and runtime implementations
├── site/                             product website
├── docs/                             bilingual Markdown documentation
├── docs-site/                        static documentation site
└── tests/                            automated tests
```

## Documentation

- [Documentation home](docs/index.en.md)
- [Getting started](docs/tutorial-getting-started.en.md)
- [Event ingestion](docs/rfc-production-events.en.md)
- [Chat](docs/chat.en.md)
- [Deployment](docs/tutorial-deployment.en.md)
- [Configuration](docs/tutorial-configuration.en.md)
- [Architecture](docs/architecture.en.md)
- [FAQ](docs/faq.en.md)

## Contributing

Issues, discussions, and pull requests are welcome. Read the [contribution guide](CONTRIBUTING.en.md) and [security policy](SECURITY.md) first.

## License

Distributed under the [MIT License](LICENSE).

<p align="right"><a href="#readme-top">Back to top</a></p>

