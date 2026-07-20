<a id="readme-top"></a>

<div align="center">
  <a href="https://ingotstack.com">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="images/logo/ingot-lockup-dark.svg">
      <img src="images/logo/ingot-lockup.svg" alt="Ingot" width="360">
    </picture>
  </a>

  <h3>Manufacturing data collection and process analysis</h3>

  <p>
    Bring equipment parameters, production processes, and inspection results together; use Ingot Chat to query abnormalities, compare cycles, and analyze possible causes.
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

Ingot is a manufacturing data collection and process analysis platform. It combines important records from equipment, instruments, MES, ERP, and custom systems into a searchable production history. Teams map source data to the standard `ProductionEvent` or `InspectionRecord` contracts and call the public HTTP APIs; Ingot embeds no device protocol and does not replace plant control, scheduling, inventory, or quality-disposition systems.

**Ingot Chat** is the main way engineers use Ingot. It runs in Platform Web and reads saved production data only. Quick Query retrieves records and checks completeness; Combined Analysis compares similar cycles from process, quality, and review perspectives and lists possible causes for an engineer to assess.

## Core capabilities

| Capability | Implemented scope |
|---|---|
| Standard event ingestion | Teams submit `ProductionEvent` batches with equipment, workpiece, production-cycle, and collection-order fields through `POST /api/v1/events:batch` |
| Inspection records | Human or instrument clients submit inspection results through `POST /api/v1/inspection-records` |
| Central data store | Stores production processes, inspection results, equipment, workpieces, and production-cycle information with query and live-update support |
| Ingot Chat | Accepts everyday questions and shows analysis results, missing data, and related production records |
| Chat-assisted analysis | Checks completeness, reconstructs production cycles, and compares similar cycles |
| Combined analysis | Reviews one problem from process, quality, and review perspectives and lists possible causes and opposing results |
| Operations | Health checks, Prometheus metrics, and structured logs |

### Security boundary

- Chat calls explicitly registered read-only record tools only; it cannot execute SQL, scripts, shell commands, file writes, or open network requests.
- Answer numbers come from actual query results and link to the corresponding production records. When data is incomplete, Chat names what is missing.
- The standard event API accepts only contract- and token-validated batches. Source protocols, credentials, retry strategy, and runtime ownership remain with the implementing team.
- Ingot never writes to PLCs, CNCs, robots, or other field controllers.
- Secrets enter only through environment variables or a secret store, never source code, logs, or repository configuration.

## Architecture

```text
equipment, instruments, MES, ERP, or custom systems
  └─ team-owned source adaptation
       └─ ProductionEvent[] / InspectionRecord
            └─ Platform API ──► PostgreSQL production records
                                    ├─ query and SSE
                                    └─ Platform Web · Ingot Chat
```

Teams own source protocols and runtime operation. Ingot owns the common event format, access control, queries, and links to original production records. See the [architecture](docs/architecture.en.md) and [production event specification](docs/rfc-production-events.en.md).

<p align="right"><a href="#readme-top">Back to top</a></p>

## Quick start

### Prerequisites

- .NET SDK 10
- Node.js 22.13 or later
- Docker Engine 26 or later with Docker Compose
- OpenSSL for local credentials

### Start Platform

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
| Platform Web | <http://localhost:3000> |
| Platform API | <http://localhost:8000> |
| Platform health | <http://localhost:8000/health> |

The default Compose stack starts PostgreSQL, Platform API, and Platform Web. Chat and Connector Host are enabled only when needed. `INGOT_OPERATOR_TOKEN` protects inspection-record submission; Chat uses a separate `INGOT_CHAT_OPERATOR_TOKEN`.

### Enable Chat in production

Production Compose leaves Chat disabled by default. Provide the model, model key, Chat user token, and data scope together before enabling it:

```bash
export INGOT_CHAT_ENABLED=true
export INGOT_CHAT_PROVIDER=OpenAI
export INGOT_CHAT_FAST_MODEL="<fast-model>"
export INGOT_CHAT_REASONING_MODEL="<reasoning-model>"
export OPENAI_API_KEY="<secret>"
export INGOT_CHAT_OPERATOR_TOKEN="$(openssl rand -hex 24)"
export INGOT_CHAT_OPERATOR_ALLOW_ALL=true
export INGOT_CHAT_ENABLE_COMBINED_ANALYSIS=true

docker compose -f docker-compose.app.yml up -d --build
```

The browser and Chat HTTP API use user `operator` with `INGOT_CHAT_OPERATOR_TOKEN`. Configure each production user with the data scope it needs; this Compose example gives `operator` access to all records.

See [getting started](docs/tutorial-getting-started.en.md) and [configuration](docs/tutorial-configuration.en.md) for the complete walkthrough.

## Event ingestion

Implement one adapter per source type in the runtime that suits your environment. The adapter maps source data to `ProductionEvent`, persists its local sequence, and submits it to Platform with an Edge credential. Every event `source` must start with `edge/{edgeId}/`.

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

Each batch accepts 1–500 events. Platform deduplicates by `eventId` and `(edgeId, seq)` and returns `ackSeq`. See the [production event specification](docs/rfc-production-events.en.md) for the contract, validation rules, and retry guidance.

When an adapter runs on a plant network or cannot reach Platform directly, a team can enable **Ingot.Edge.ConnectorHost**. It saves `ProductionEvent[]` locally and uploads them in order after the network recovers; duplicate uploads do not create duplicate records.

```bash
export INGOT_CONNECTOR_TOKEN="$(openssl rand -hex 24)"
docker compose -f docker-compose.app.yml --profile connector-host up -d connector-host
```

See the [production event specification](docs/rfc-production-events.en.md).

## Chat

After configuring Chat for production, open <http://localhost:3000>, select **Chat**, and ask a question for an optional equipment number or production-cycle number:

```text
What happened during this cycle, and is its data complete?
```

Chat only queries saved production data. It returns analysis results, missing-data notices, and related production records. It does not change events, inspection records, configuration, or equipment state.

See [Chat](docs/chat.en.md) for the complete capability and API reference.

## Public APIs

| Endpoint | Purpose |
|---|---|
| `POST /api/v1/events:batch` | Submit a standard production-event batch |
| `GET /api/v1/events` | Query production events |
| `GET /api/v1/events/stream` | Subscribe to production events over SSE |
| `GET /api/v1/cycles/{correlationId}` | Read the production process for one cycle |
| `POST /api/v1/inspection-attachments` | Upload an inspection photo or attachment |
| `POST /api/v1/inspection-records` | Submit a human or instrument inspection record |
| `GET /api/v1/chat/capabilities` | Discover Chat availability, tools, and run limits |
| `POST /api/v1/chat/runs` | Create a Chat run |
| `GET /api/v1/chat/runs/{runId}` | Read an answer, query progress, and related production records |
| `GET /api/v1/chat/runs/{runId}/stream` | Stream Chat events over SSE with resume support |
| `POST /api/v1/chat/runs/{runId}:cancel` | Cancel a Chat run |

## Repository layout

```text
Ingot/
├── src/
│   ├── Ingot.Platform.Api/            Platform HTTP, authorization, and SSE
│   ├── Ingot.Platform.Infrastructure/ platform data, inspections, webhooks, and Chat analysis capabilities
│   ├── Ingot.Contracts/              event, inspection, and Chat HTTP contracts
│   ├── Ingot.Domain/                 production events, subject references, and domain validation
│   ├── Ingot.Edge.Application/            application-service abstractions
│   └── Ingot.Edge.Infrastructure/         storage, logging, metrics, and runtime implementations
├── apps/website/                     product website
├── docs/                             bilingual Markdown documentation
├── apps/docs-site/                   static documentation site
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

