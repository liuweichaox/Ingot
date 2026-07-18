<a id="readme-top"></a>

<div align="center">
  <a href="https://ingotstack.com">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="images/logo/ingot-lockup-dark.svg">
      <img src="images/logo/ingot-lockup.svg" alt="Ingot" width="360">
    </picture>
  </a>

  <h3>Trusted production facts and connector engineering</h3>

  <p>
    Central Web Chat queries facts and finds data problems.<br>
    The downloadable Ingot Agent desktop app generates, builds, tests, and packages connector code.
  </p>

  <p>
    <a href="https://ingotstack.com"><strong>Website</strong></a>
    ·
    <a href="https://docs.ingotstack.com"><strong>Documentation</strong></a>
    ·
    <a href="https://github.com/liuweichaox/Ingot/releases/latest"><strong>Download Ingot Agent</strong></a>
    ·
    <a href="https://github.com/liuweichaox/Ingot/issues">Report an issue</a>
  </p>

  <p>
    <a href="https://github.com/liuweichaox/Ingot/actions/workflows/ci.yml"><img src="https://github.com/liuweichaox/Ingot/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
    <a href="LICENSE"><img src="https://img.shields.io/github/license/liuweichaox/Ingot" alt="MIT License"></a>
    <a href="https://github.com/liuweichaox/Ingot/issues"><img src="https://img.shields.io/github/issues/liuweichaox/Ingot" alt="Issues"></a>
  </p>

  <p>English · <a href="README.md">简体中文</a></p>
</div>

<details>
  <summary>Table of contents</summary>
  <ol>
    <li>
      <a href="#about-ingot">About Ingot</a>
      <ul>
        <li><a href="#chat-and-agent">Chat and Agent</a></li>
        <li><a href="#core-capabilities">Core capabilities</a></li>
        <li><a href="#security-boundary">Security boundary</a></li>
        <li><a href="#built-with">Built with</a></li>
      </ul>
    </li>
    <li><a href="#architecture">Architecture</a></li>
    <li><a href="#getting-started">Getting started</a></li>
    <li><a href="#usage">Usage</a></li>
    <li><a href="#public-apis">Public APIs</a></li>
    <li><a href="#repository-layout">Repository layout</a></li>
    <li><a href="#documentation">Documentation</a></li>
    <li><a href="#contributing">Contributing</a></li>
    <li><a href="#license">License</a></li>
    <li><a href="#acknowledgments">Acknowledgments</a></li>
  </ol>
</details>

## About Ingot

Ingot serves manufacturing environments that must connect equipment, instruments, and business systems through different interfaces. External connectors normalize each source into `ProductionEvent` records. Connector Host authenticates and validates those records, persists them locally, and ships them through a bounded outbox. Central stores a queryable, verifiable, and traceable production fact chain.

### Chat and Agent

| Product | Runs in | Responsibility |
|---|---|---|
| **Chat** | Central Web | Conversational production-fact queries, data-quality checks, cycle-trace drill-down, and evidence links |
| **Ingot Agent** | Downloadable desktop app | Connector source generation, constrained build/test and repair, engineer review, and packaging |

Chat does not generate code or modify production facts. Ingot Agent does not provide production-analysis conversation, deploy connectors, or control equipment. Their names, entry points, permissions, and runtimes are separate.

### Core capabilities

| Capability | Implemented scope |
|---|---|
| Central Web Chat | Natural-language questions, page context, read-only tool activity, streamed answers, history, and evidence links |
| Chat fact tools | `check_data_quality` and `get_cycle_trace` |
| Ingot Agent desktop | Connector specification, Actor-isolated source workspace, code generation and repair, fixed build/test entries, operator packaging approval, and SHA-256 ZIP |
| Standard event ingress | `Ingot.Connector.Host` accepts `ProductionEvent[]` without loading equipment-protocol SDKs |
| Fact platform | Production events, inspection records, objects, context, correlation IDs, query, and SSE |
| Bounded delivery | SQLite WAL event log and outbox, PostgreSQL central facts, backlog cap, and drop audit |
| Operations | Health checks, Prometheus metrics, and structured logs |

### Security boundary

- Chat calls only explicitly registered read-only fact tools. It cannot execute SQL, scripts, shell commands, file writes, or open network requests.
- Chat numbers and conclusions must come from tool results and include resolvable evidence references. Insufficient data produces explicit limitations.
- Ingot Agent can change only the current Actor's connector workspace. It cannot choose arbitrary host paths, commands, images, or working directories.
- Agent does not connect to data sources. Connector build and test run platform-fixed entries in a network-disabled environment using workspace fixtures. Passing tests still require explicit operator approval before SHA-256 ZIP generation.
- Ingot does not deploy, start, or schedule generated connectors and cannot control PLCs, CNCs, robots, or other field controllers.
- Secrets enter through environment variables or a secret store and never belong in source, logs, artifacts, or repository configuration.

### Built with

- [.NET 10](https://dotnet.microsoft.com/) and ASP.NET Core
- Microsoft Agent Framework and the OpenAI Responses API on Central for connector-code workflows initiated by the desktop Agent
- PostgreSQL 18 and SQLite WAL
- Vue 3, Vite, and Element Plus
- Next.js 16 and React 19
- Docker Compose, Prometheus, and Serilog

<p align="right"><a href="#readme-top">Back to top</a></p>

## Architecture

```text
Central Web
  ├─ Chat ──read-only queries──► Central API ──► PostgreSQL production facts
  └─ event, inspection, log, and metric views

Ingot Agent desktop
  └─ source specification → isolated workspace → fixed build/test → operator approval → SHA-256 ZIP

externally deployed connector
  └─ source payload → ProductionEvent[] → Ingot.Connector.Host
                                           └─ SQLite event log/outbox → Central API
```

[`scripts/verify-architecture.sh`](scripts/verify-architecture.sh) enforces dependency boundaries. See [architecture](docs/architecture.en.md) and [design](docs/design.en.md) for details.

<p align="right"><a href="#readme-top">Back to top</a></p>

## Getting started

### Prerequisites

- .NET SDK 10
- Node.js 22.13 or later
- Docker Engine 26 or later, with Docker Compose
- OpenSSL for local credential generation

### Start Central

```bash
git clone https://github.com/liuweichaox/Ingot.git
cd Ingot

export INGOT_POSTGRES_PASSWORD="$(openssl rand -hex 24)"
export INGOT_EDGE_TOKEN="$(openssl rand -hex 24)"
export INGOT_OPERATOR_TOKEN="$(openssl rand -hex 24)"
export INGOT_CONNECTOR_TOKEN="$(openssl rand -hex 24)"

docker compose -f docker-compose.app.yml up -d --build
```

| Service | URL |
|---|---|
| Central Web | <http://localhost:3000> |
| Central API | <http://localhost:8000> |
| Connector Host | <http://localhost:8001> |
| Central health | <http://localhost:8000/health> |
| Connector Host health | <http://localhost:8001/health> |

### Download Ingot Agent

Download and install the appropriate Ingot Agent package from the [latest GitHub Release](https://github.com/liuweichaox/Ingot/releases/latest). The desktop configures only Central URL, Actor, and token; models, workspaces, and build environment belong to the Central deployment. Central Web does not expose connector code generation.

See [getting started](docs/tutorial-getting-started.en.md), [Chat](docs/chat.en.md), and [Ingot Agent desktop](docs/desktop-agent.en.md).

<p align="right"><a href="#readme-top">Back to top</a></p>

## Usage

### Use Chat in Central Web

Open <http://localhost:3000>, select **Chat**, and enter a question with optional asset or cycle context:

```text
What happened during this cycle, and is its data complete?
```

Chat creates a governed query plan, calls read-only tools, and returns findings, limitations, and source-fact references. Chat does not generate connector code.

### Generate a connector in Ingot Agent

```text
source specification and samples
  → connector code
  → fixed build entry
  → fixed test entry
  → bounded repair
  → operator approval
  → SHA-256 ZIP
```

The default .NET connector template reads source JSONL from stdin and writes ProductionEvent JSONL to stdout. The package contains no Connector Host credentials, retry queue, or HTTP submission client. The external deployment runtime owns batching, authentication, and event submission to Connector Host.

### Submit normalized production events

```bash
curl -X POST http://localhost:8001/api/v1/connector-events \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer ${INGOT_CONNECTOR_TOKEN}" \
  -d '[{
    "eventId": "0198a820-7f91-7a5d-8c44-111111111111",
    "eventType": "cycle.started",
    "eventTypeVersion": 1,
    "occurredAt": "2026-07-18T08:00:00Z",
    "recordedAt": "2026-07-18T08:00:00Z",
    "source": "connector/FURNACE-01",
    "subject": { "type": "asset", "id": "FURNACE-01" },
    "context": { "workpiece_id": "WP-001" },
    "data": {},
    "correlationId": "CYCLE-001",
    "seq": 0
  }]'
```

See the [production event specification](docs/rfc-production-events.en.md).

### Run the complete gate

```bash
./scripts/verify.sh
```

<p align="right"><a href="#readme-top">Back to top</a></p>

## Public APIs

| Endpoint | Purpose |
|---|---|
| `GET /api/v1/chat/capabilities` | Discover Chat availability, read-only tools, and run limits |
| `POST /api/v1/chat/runs` | Create a Chat run |
| `GET /api/v1/chat/runs` | List Chat history for the current Actor |
| `GET /api/v1/chat/runs/{runId}` | Read status, plan, tool activity, evidence, and answer |
| `GET /api/v1/chat/runs/{runId}/stream` | Stream run events over SSE with resume support |
| `POST /api/v1/chat/runs/{runId}:cancel` | Cancel a Chat run |
| `POST /api/v1/connector-events` | Submit normalized events to Connector Host |
| `POST /api/v1/events:batch` | Ship a Connector Host event batch to Central |
| `POST /api/v1/inspection-records` | Submit human or instrument inspection records |

Ingot Agent desktop owns its code-generation, workspace, and packaging API boundary. Those endpoints are not Central Web Chat capabilities. See [Chat](docs/chat.en.md) and [Ingot Agent desktop](docs/desktop-agent.en.md) for the complete contracts.

## Repository layout

```text
Ingot/
├── desktop/                           Ingot Agent Desktop: Tauri, Rust, React, and TypeScript
├── src/
│   ├── Ingot.Agent/                  Chat/Agent surfaces, provider-neutral workflows, and validation
│   ├── Ingot.Agent.Infrastructure/   server-side model adapters, run store, and tool implementations
│   ├── Ingot.Connector.Builder/      isolated workspace, fixed build/test, and packaging
│   ├── Ingot.Connector.Host/         standard event ingress, SQLite log, and outbox
│   ├── Ingot.Central.Infrastructure/ central facts, inspections, webhooks, and read-only Chat tools
│   ├── Ingot.Central.Api/            Central HTTP, authorization, and SSE
│   ├── Ingot.Central.Web/            Chat and production-fact UI
│   ├── Ingot.Contracts/              public HTTP, event, and inspection contracts
│   ├── Ingot.Domain/                 domain models and production-event validation
│   └── Ingot.Infrastructure/         event, context, log, and metric infrastructure
├── tests/                             .NET automated tests
├── docs/                              bilingual source documentation
├── docs-site/                         docs.ingotstack.com
├── site/                              ingotstack.com
└── scripts/                           architecture and release gates
```

## Documentation

- [Documentation home](docs/index.en.md)
- [Getting started](docs/tutorial-getting-started.en.md)
- [Chat](docs/chat.en.md)
- [Ingot Agent desktop](docs/desktop-agent.en.md)
- [Architecture](docs/architecture.en.md)
- [Design](docs/design.en.md)
- [Modules](docs/modules.en.md)
- [Configuration](docs/tutorial-configuration.en.md)
- [Deployment](docs/tutorial-deployment.en.md)
- [Development](docs/tutorial-development.en.md)
- [FAQ](docs/faq.en.md)
- [Production event specification](docs/rfc-production-events.en.md)
- [Security policy](SECURITY.md)

<p align="right"><a href="#readme-top">Back to top</a></p>

## Contributing

1. Fork the project.
2. Create a branch: `git checkout -b feature/your-change`.
3. Update code, tests, and bilingual documentation.
4. Run `./scripts/verify.sh`.
5. Push the change and open a pull request.

Every contribution must preserve read-only Chat, desktop-only Agent code generation, fact contracts, and bilingual documentation. See [CONTRIBUTING.en.md](CONTRIBUTING.en.md) for the complete requirements. Report vulnerabilities privately under the [security policy](SECURITY.md).

<p align="right"><a href="#readme-top">Back to top</a></p>

## License

Distributed under the MIT License. See [LICENSE](LICENSE).

## Acknowledgments

- README structure inspired by [othneildrew/Best-README-Template](https://github.com/othneildrew/Best-README-Template).
- The desktop code-generation runtime uses Microsoft Agent Framework.

Project link: <https://github.com/liuweichaox/Ingot>

<p align="right"><a href="#readme-top">Back to top</a></p>
