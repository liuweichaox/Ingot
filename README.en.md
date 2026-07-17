<a id="top"></a>

<div align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="./docs/assets/ingot-logo-dark.png">
    <img src="./docs/assets/ingot-logo.png" alt="Ingot" width="180" />
  </picture>
  <p align="center">
    <strong>Forge production data into facts.</strong>
    <br />
    Open-source production data infrastructure for the industrial edge, starting with reliable acquisition and evolving raw telemetry into standardized, traceable production facts.
    <br />
    <a href="./docs/index.en.md"><strong>Explore the docs »</strong></a>
    <br />
    <br />
    <a href="https://github.com/liuweichaox/Ingot">Project Home</a>
    ·
    <a href="https://github.com/liuweichaox/Ingot/issues">Report Bug</a>
    ·
    <a href="https://github.com/liuweichaox/Ingot/pulls">Contribute</a>
  </p>
</div>

<div align="center">

[![.NET][dotnet-shield]][dotnet-url]
[![Vue][vue-shield]][vue-url]
[![InfluxDB][influxdb-shield]][influxdb-url]
[![Stars][stars-shield]][stars-url]
[![Issues][issues-shield]][issues-url]
[![License][license-shield]][license-url]

</div>

[中文](README.md) | English

## Table of Contents

- [About The Project](#about-the-project)
- [Built With](#built-with)
- [Getting Started](#getting-started)
- [Usage](#usage)
- [Architecture](#architecture)
- [Repository Layout](#repository-layout)
- [Documentation](#documentation)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [License](#license)
- [Acknowledgments](#acknowledgments)

## About The Project

**Ingot** is open-source production event infrastructure for the industrial edge. It reads raw signals close to equipment, sends high-rate telemetry to a TSDB, and casts high-value changes such as cycles, parameter writes, and diagnostics into immutable, queryable production facts.

The main product is the `Edge Agent`. It owns the collection path, reads PLC values, organizes acquisition tasks, batches messages, and writes them directly into a TSDB. The default implementation is InfluxDB. `Central API / Central Web` are optional control-plane components for node status, metrics, and log visibility rather than required runtime dependencies.

PLC is the first source adapter. v2 configuration, production events, context, and APIs use source-neutral `SourceCode` and asset models. The unified event envelope, SQLite event log, event rules, context state, local query/SSE, Influx projection, Profile validation, resumable Edge-to-Central shipping, PostgreSQL event hub, central query/SSE, and event UI are implemented. See the [Production Events RFC](docs/rfc-production-events.md).

### Why Ingot

An **ingot** is a standardized unit created by refining and casting raw material. Raw telemetry resembles ore: abundant, fragmented, and low-value in isolation. Ingot refines it into standardized, event-oriented production facts.

- **Standardized**: an ingot is cast in a consistent mold, reflecting a unified production event model
- **Immutable**: once cast, an ingot has a definite form, matching the direction of immutable production events
- **Stackable**: events can accumulate, be stored, and be consolidated into a complete production record
- **Industrial by nature**: “ingot” is native vocabulary in foundries, metallurgy, and steel production

### Core Capabilities

- reliable PLC data collection
- v1/v2 JSON configuration for sources, assets, telemetry channels, and event rules
- support for both `Always` and `Conditional` acquisition modes
- batched direct telemetry writes into a `TSDB`
- durable production events in a local SQLite event log
- context state, event/cycle queries, and SSE subscriptions
- `core` and `optical` Profile validation
- configuration validation, hot reload, and runtime diagnostics
- ordered event shipping with retry and acknowledgement-based outbox advancement
- idempotent multi-edge ingest backed by partitioned PostgreSQL and JSONB/GIN indexes
- centralized event query, SSE, cycle aggregation, node, metric, log, and event views
- durable PostgreSQL webhook subscriptions with CloudEvents 1.0 structured delivery and optional HMAC signatures

### System Boundary

- the `Edge Agent` is the core runtime component and the acquisition path comes first
- `Central API / Central Web` are optional control-plane components
- telemetry remains at-most-once: a failed TSDB batch is logged and dropped
- production events are persisted before projections and remain queryable across restarts
- central downtime leaves events pending in the local outbox; recovery resumes by `Seq`, while Central deduplicates by `EventId` and `(EdgeId, Seq)`
- PostgreSQL is required by Central's event hub but not by Edge acquisition or local event creation
- drivers are selected by stable `Driver` names without hiding the real differences between PLC protocols

### Control Plane Preview

| Edges | Metrics | Logs |
| --- | --- | --- |
| ![Edges](images/edges.png) | ![Metrics](images/metrics.png) | ![Logs](images/logs.png) |

### Primary Use Cases

- shop-floor PLC real-time data acquisition
- multi-PLC deployments with explicit configuration
- direct TSDB telemetry pipelines from the edge
- environments that need metrics, logs, and centralized node visibility
- industrial scenarios that prefer a lightweight runtime close to equipment

<p align="right">(<a href="#top">back to top</a>)</p>

## Built With

- `.NET 10` / `ASP.NET Core` for Edge Agent and Central API hosting
- `Vue 3` + `Vue Router` + `Element Plus` for the control-plane web UI
- `InfluxDB 2.x` as the default time-series store
- `SQLite` for production events, context, conditional state, and runtime logs
- `PostgreSQL 18` for the central production event store
- `HslCommunication` as the default PLC driver implementation base
- `prometheus-net` for metrics
- `Serilog` for logging

<p align="right">(<a href="#top">back to top</a>)</p>

## Getting Started

### Prerequisites

- `.NET 10 SDK`
- `InfluxDB 2.x`
- `Docker` if you want to use the included compose file for InfluxDB
- `Node.js 20` and `npm` if you want to run the central web UI locally

### Local Setup

1. Clone the repository

```bash
git clone https://github.com/liuweichaox/Ingot.git
cd Ingot
```

2. Build the solution

```bash
dotnet build Ingot.sln
```

3. Start InfluxDB

```bash
docker compose -f docker-compose.tsdb.yml up -d
```

Notes:

- the default Edge Agent connection settings live in [src/Ingot.Edge.Agent/appsettings.json](src/Ingot.Edge.Agent/appsettings.json)
- if you use your own InfluxDB instance, make sure `InfluxDB:Url`, `Token`, `Bucket`, and `Org` match your environment

4. Review device configuration

- sample config: [src/Ingot.Edge.Agent/Configs/TEST_PLC.json](src/Ingot.Edge.Agent/Configs/TEST_PLC.json)
- more examples: [examples/device-configs](examples/device-configs)
- JSON Schema: [schemas/device-config.schema.json](schemas/device-config.schema.json)

5. Validate configs offline

```bash
dotnet run --project src/Ingot.Edge.Agent -- --validate-configs
```

To validate another directory:

```bash
dotnet run --project src/Ingot.Edge.Agent -- --validate-configs --config-dir ./examples/device-configs
```

6. Start the Edge Agent

```bash
dotnet run --project src/Ingot.Edge.Agent
```

7. Optional: start the local PLC simulator

```bash
dotnet run --project src/Ingot.Simulator
```

Run the RFC optical event scenario:

```bash
dotnet run --project src/Ingot.Simulator -- Mode=Scenario
```

8. Optional: start PostgreSQL, Central API, and Central Web

```bash
docker compose -f docker-compose.events.yml up -d --build
```

For local frontend development, keep Central API running and use `cd src/Ingot.Central.Web && npm install && npm run dev`.

Default local URLs:

- Edge Agent: `http://localhost:8001`
- Central API: `http://localhost:8000`
- Central Web: `http://localhost:3000`

<p align="right">(<a href="#top">back to top</a>)</p>

## Usage

### Typical Local Flow

If you want to verify the full pipeline locally, this is the easiest order:

1. Start `InfluxDB`
2. Start `Ingot.Simulator`
3. Validate [TEST_PLC.json](src/Ingot.Edge.Agent/Configs/TEST_PLC.json)
4. Start `Ingot.Edge.Agent`
5. Optionally start `Ingot.Central.Api` and `Ingot.Central.Web`
6. Confirm status through health checks, metrics, logs, and the UI

Run the repository quality gate with:

```bash
./scripts/verify.sh
```

It builds and tests .NET, validates v1/v2 examples, builds and audits Central Web, validates Compose, and checks patch formatting.

Run the Phase 2 optical closed-loop acceptance against the real simulator and local API with:

```bash
./scripts/verify-optical-trace.sh
```

It verifies the complete “lot change → tooling change → three cycles → alarm and recovery” fact chain, three cycle correlation IDs, start/end snapshots, automatically attached lot/tooling context, and filtered Edge SSE resume behavior.

Phase 1 process-crash recovery is also repeatable:

```bash
./scripts/verify-edge-restart-recovery.sh
```

It issues a real `kill -9` while a cycle is active, restarts Edge against the original `events.db` and state store, and verifies that pre-crash facts remain intact, `diagnostic.cycle_recovered` appears, and the same CorrelationId eventually completes.

Legacy configuration compatibility has its own acceptance test:

```bash
./scripts/verify-v1-compatibility.sh
```

It uses only SchemaVersion 1 `PlcCode`, `Register`, and `ConditionalAcquisition` fields, proving that no v2 fields are required to produce paired production events while preserving the legacy channel Metrics start snapshot.

Run the RFC performance gates independently with:

```bash
./scripts/benchmark-edge-event-log.sh
./scripts/benchmark-central-ingest.sh
```

The edge benchmark verifies managed/working-set memory growth (and private memory where the platform exposes it), append P99, and dynamic-context query P95 against a one-million-row SQLite event log. The central benchmark starts PostgreSQL and Central API, ingests 10,000 events, and checks throughput, sequence continuity, and idempotent integrity. Both scripts return a non-zero exit code when an RFC threshold is missed.

Phase 3 disconnect recovery is executable as an acceptance test. It defaults to the RFC's two-hour outage and can be shortened for a local smoke run:

```bash
./scripts/verify-disconnect-recovery.sh
INGOT_DISCONNECT_SECONDS=15 ./scripts/verify-disconnect-recovery.sh
```

The script runs the real simulator, Edge, Central API, and PostgreSQL, keeps producing facts while Central is offline, then verifies an empty recovered outbox, equal local/central counts, unique event IDs and sequences, no sequence gaps, and no orphan ingest reservations.

The default 7,200-second acceptance passed on 2026-07-17: local facts grew from 23 to 13,121 while offline, producing a 13,098-event backlog. After recovery, Central held all 13,121 events, pending returned to zero, sequences were continuous from 1 through 13,121, and there were no duplicates, gaps, or orphan reservations.

The external subscription path has its own real HTTP acceptance test:

```bash
./scripts/verify-webhook-delivery.sh
```

It starts the repository's minimal receiver, creates a durable subscription, ingests isolated acceptance events, and verifies CloudEvents 1.0 structured content, event-ID headers, HMAC-SHA256 signatures, duplicate-free delivery, and successful subscription-cursor advancement.

### Common Endpoints

| Component | URL | Purpose |
| --- | --- | --- |
| Edge Agent | `http://localhost:8001/health` | health check |
| Edge Agent | `http://localhost:8001/metrics` | Prometheus metrics |
| Edge Agent | `http://localhost:8001/api/logs` | local log query |
| Edge Agent | `http://localhost:8001/api/acquisition/plc-connections` | PLC connection status |
| Edge Agent | `http://localhost:8001/api/v1/events` | local production event query |
| Edge Agent | `http://localhost:8001/api/v1/events/stream` | production event SSE |
| Edge Agent | `http://localhost:8001/api/v1/cycles/{correlationId}` | cycle fact chain |
| Central API | `http://localhost:8000/api/v1/events` | cross-edge production event query |
| Central API | `http://localhost:8000/api/v1/events/stream` | central production event SSE |
| Central API | `http://localhost:8000/api/v1/cycles/{correlationId}` | central cycle fact chain |
| Central API | `http://localhost:8000/api/v1/subscriptions` | CloudEvents webhook subscriptions |
| Central API | `http://localhost:8000/metrics` | central metrics |
| Central Web | `http://localhost:3000/events` | node, metric, log, and event UI |

### Local Runtime Files

During runtime, these files matter most:

- `Data/logs.db`
- `Data/acquisition-state.db`
- `Data/events.db`

What to watch:

- `logs.db` stores local logs for PLC connectivity, configuration, and TSDB write diagnostics
- old log rows are pruned automatically after 30 days by default; use `Logging:RetentionDays` to change this, or set it to `<= 0` to disable cleanup
- `acquisition-state.db` stores active-cycle recovery state for conditional acquisition
- `events.db` stores immutable production events and pending shipping state; telemetry batches are not written into it

<p align="right">(<a href="#top">back to top</a>)</p>

## Architecture

### Main Path

```text
PLC / Source
      |
      v
 Edge Agent
   |-- telemetry --> memory queue --> TSDB
   |
   `-- event rules --> context snapshot --> events.db
                                           |-- query / SSE
                                           |-- optional TSDB projection
                                           `-- EventShipper --> Central API
                                                                |
                                                                `--> PostgreSQL
```

### Deployment View

```text
Browser
   |
   v
Central Web
   |
   v
Central API
   |-- PostgreSQL event hub
   `-- registration / diagnostics
              ^
              |  (optional for edge operation)
Edge Agent ---+----> PLC / Device
     |
     +------------> TSDB
```

### How To Read This

- `Edge Agent` is the core of the system and owns collection, batched writes, and local diagnostics
- v1/v2 configs define sources, assets, telemetry channels, event rules, and Profiles
- telemetry batching is in memory and remains at-most-once
- production events are persisted in `events.db` before any projection
- `acquisition-state.db` preserves active cycles and asset context across restarts
- `EventShipper` sends only durable events and advances shipped state only after a central acknowledgement
- `Central API` uses PostgreSQL as the cross-edge event fact store and exposes query, SSE, and cycle views; a cycle view also restores other facts for the same Edge and subject between start and completion
- `Central API / Central Web` remain optional for Edge operation and are not prerequisites for collection

### Failure Semantics

- TSDB write succeeds: the current batch is complete
- telemetry TSDB write fails: the runtime logs and drops that telemetry batch
- event-log append succeeds: the production fact exists even if a later projection fails
- event-log append fails: health and persistence-failure metrics expose the hard failure
- event backlog reaches its hard limit: the oldest pending facts are dropped, `diagnostic.backlog_dropped` is appended, and an explicit drop counter is incremented

### Design Priorities

- `Edge First`
  The Edge Agent owns the acquisition path and does not depend on the control plane to keep running.
- `Real-Time First`
  High-rate telemetry is written directly to the TSDB; low-rate, high-value events are durable.
- `Facts Before Projections`
  Events become immutable facts before they are projected to TSDB, SSE, or the central store.
- `Configuration Before Runtime`
  Device configs should be validated before the runtime is allowed to start.
- `Explicit Driver Contracts`
  Protocol implementations are selected by stable `Driver` names with explicit extension points.
- `Observability First`
  Logs, metrics, and the central view make runtime failures visible instead of hiding them behind local recovery queues.
- `UTC`
  Acquisition timestamps use UTC semantics to keep multi-node behavior predictable.

For more implementation detail, start with [docs/design.en.md](docs/design.en.md) and [docs/modules.en.md](docs/modules.en.md).

<p align="right">(<a href="#top">back to top</a>)</p>

## Repository Layout

- [src/Ingot.Edge.Agent](src/Ingot.Edge.Agent)
  edge runtime host for the acquisition pipeline and local diagnostics endpoints
- [src/Ingot.Infrastructure](src/Ingot.Infrastructure)
  PLC drivers, orchestration, queueing, InfluxDB, SQLite, logging, and metrics implementations
- [src/Ingot.Application](src/Ingot.Application)
  abstractions, application services, and runtime contracts
- [src/Ingot.Domain](src/Ingot.Domain)
  domain, configuration, and message models
- [src/Ingot.Central.Api](src/Ingot.Central.Api)
  central registration, heartbeat, diagnostics proxy, PostgreSQL event ingest, query, SSE, and cycle API
- [src/Ingot.Central.Web](src/Ingot.Central.Web)
  Vue/Vite control plane for nodes, metrics, logs, and events
- [src/Ingot.Simulator](src/Ingot.Simulator)
  local PLC simulator for integration and demo flows
- [tests/Ingot.Core.Tests](tests/Ingot.Core.Tests)
  core test project

<p align="right">(<a href="#top">back to top</a>)</p>

## Documentation

Suggested reading order:

1. [Getting Started](docs/tutorial-getting-started.en.md)
2. [Configuration](docs/tutorial-configuration.en.md)
3. [Driver Catalog](docs/hsl-drivers.en.md)
4. [Deployment](docs/tutorial-deployment.en.md)

Then go deeper by topic:

- [Design](docs/design.en.md)
- [Modules](docs/modules.en.md)
- [Production Events RFC](docs/rfc-production-events.md)
- [Brand & Logo](docs/brand.md) (Chinese)
- [Development](docs/tutorial-development.en.md)
- [FAQ](docs/faq.en.md)
- [Contributing](CONTRIBUTING.en.md)

<p align="right">(<a href="#top">back to top</a>)</p>

## Roadmap

Based on the current design and docs, the most valuable next steps are:

- [x] establish the Ingot product name and rename the solution to `Ingot.sln`
- [x] migrate projects, assemblies, and namespaces to `Ingot.*`
- [x] add the production event envelope, SQLite event log, local query, and SSE
- [x] add edge-pair cycle rules, context state, Profiles, and v2 source configuration
- [x] add central idempotent ingest, resumable event shipping, and the event stream UI
- [x] add CloudEvents 1.0 webhook subscriptions, filtering, cursors, and HMAC delivery
- [ ] add more real-world PLC sample configs
- [ ] expand end-to-end test coverage
- [ ] improve `ProtocolOptions` coverage for major drivers
- [ ] deepen troubleshooting and operations docs
- [ ] improve central observability and diagnostics workflows

Use [Issues](https://github.com/liuweichaox/Ingot/issues) for open problems and feature discussions.

<p align="right">(<a href="#top">back to top</a>)</p>

## Contributing

Contributions are welcome across driver enhancements, acquisition-path reliability fixes, TSDB write improvements, docs, sample configs, and automated tests.

Before opening a PR, it is a good idea to confirm:

- the solution builds successfully
- relevant tests pass
- README, tutorials, and sample configs are updated together

See [CONTRIBUTING.en.md](CONTRIBUTING.en.md) for the full contribution guidelines.

<p align="right">(<a href="#top">back to top</a>)</p>

## License

Distributed under the [MIT License](LICENSE).

<p align="right">(<a href="#top">back to top</a>)</p>

## Acknowledgments

- [Best-README-Template](https://github.com/othneildrew/Best-README-Template)
- [HslCommunication](https://github.com/dathlin/HslCommunication)
- [InfluxDB](https://www.influxdata.com/)

<p align="right">(<a href="#top">back to top</a>)</p>

[dotnet-shield]: https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white
[dotnet-url]: https://dotnet.microsoft.com/
[vue-shield]: https://img.shields.io/badge/Vue-3-42B883?style=for-the-badge&logo=vuedotjs&logoColor=white
[vue-url]: https://vuejs.org/
[influxdb-shield]: https://img.shields.io/badge/InfluxDB-2.x-22ADF6?style=for-the-badge&logo=influxdb&logoColor=white
[influxdb-url]: https://www.influxdata.com/
[stars-shield]: https://img.shields.io/github/stars/liuweichaox/Ingot.svg?style=for-the-badge
[stars-url]: https://github.com/liuweichaox/Ingot/stargazers
[issues-shield]: https://img.shields.io/github/issues/liuweichaox/Ingot.svg?style=for-the-badge
[issues-url]: https://github.com/liuweichaox/Ingot/issues
[license-shield]: https://img.shields.io/github/license/liuweichaox/Ingot.svg?style=for-the-badge
[license-url]: https://github.com/liuweichaox/Ingot/blob/main/LICENSE
