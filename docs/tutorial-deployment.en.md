# Deployment

This document explains how to deploy Ingot as long-running, observable, edge-first production event infrastructure.

The recommended deployment principles are:

- `Edge Agent` is the required runtime and should be deployed close to PLCs
- `InfluxDB` is the default TSDB implementation
- telemetry writes directly to TSDB while production events append to local `events.db`
- `Central API / Central Web` and PostgreSQL form an optional central event hub

## Recommended Deployment Topologies

### Single Node

Suitable for local validation, labs, or one production line:

- 1 `Edge Agent`
- 1 `InfluxDB`
- optional `PostgreSQL + Central API + Central Web`

### Multi Node

Suitable for multiple workshops, lines, or factories:

- one `Edge Agent` per collection node
- local event facts, business context, and logs on every edge node
- one shared `PostgreSQL + Central API + Central Web`
- centralized or site-specific `InfluxDB`

The core rule is:

- the edge node must be in the PLC-reachable network
- central unavailability does not stop local event creation or queries

## Runtime Components

### Edge Agent

Responsibilities:

- load device configuration
- connect to PLCs
- run Always / Conditional acquisition
- write batches directly into InfluxDB
- derive and persist production events
- expose local event, context, health, metric, and log APIs

### InfluxDB

Responsibilities:

- act as the default primary time-series store

### Central API / Central Web

Responsibilities:

- show node status
- show heartbeats
- aggregate metrics
- proxy edge diagnostics
- authenticate per-edge event batches
- idempotently store partitioned events in PostgreSQL
- expose cross-edge query, SSE, cycle aggregation, and event UI

Important:

- the central plane is optional
- the collection path must continue even when Central is unavailable

## Recommended Release Model

Use published binaries in production. Do not treat `dotnet run` as the primary production model.

### Publish Edge Agent

```bash
dotnet publish src/Ingot.Edge.Agent -c Release -o ./publish/edge
```

Start it:

```bash
./publish/edge/Ingot.Edge.Agent
```

### Publish Central API

```bash
dotnet publish src/Ingot.Central.Api -c Release -o ./publish/central-api
```

Start it:

```bash
./publish/central-api/Ingot.Central.Api
```

### Build Central Web

```bash
cd src/Ingot.Central.Web
npm ci
npm run build
```

Serve the generated `dist/` folder using nginx or another static host.

## Containerization Boundary

The repository-level Compose files are mainly intended for:

- `InfluxDB`
- `PostgreSQL`
- `Central API`
- `Central Web`

Start the complete event hub with:

```bash
docker compose -f docker-compose.events.yml up -d --build
```

Do not present `Edge Agent` containerization as the default path.

The reason is practical:

- Edge must reach the real PLC network reliably
- field deployments often involve real NICs, VLANs, routes, and firewalls
- a host process is easier to troubleshoot than a containerized edge runtime

The recommended rule is:

- central components can be containerized
- `InfluxDB` can be containerized
- `Edge Agent` should usually run as a host process

## Runtime Files

The important deployment artifact is not only the binary folder. It is also the local runtime state.

The default files to watch are:

- `Data/logs.db`
- `Data/acquisition-state.db`
- `Data/events.db`

Meaning:

- `logs.db`: local log database, retained for 30 days by default
- `Logging:RetentionDays`: change the local log retention window, or set `<= 0` to disable cleanup
- `acquisition-state.db`: active-cycle recovery state for conditional acquisition
- `events.db`: immutable production events and pending shipping state

The runtime does not keep a raw telemetry replay backlog; production events are persisted separately.

`Events:MaxBacklogRows` is the hard offline-backlog limit. When it is reached, the runtime reserves room for an audit fact, drops the oldest pending rows, and appends `diagnostic.backlog_dropped`. `event_backlog_dropped_total` counts discarded facts, `event_outbox_backlog` is the current-value gauge, and each failed delivery increments the affected rows' `ship_attempts`.

## Pre-Production Configuration Checklist

At minimum, verify these settings.

### Application Level

- `Urls`
- `Logging:DatabasePath`
- `Logging:RetentionDays`
- `InfluxDB:*`
- `Acquisition:DeviceConfigService:ConfigDirectory`
- `Acquisition:StateStore:DatabasePath`
- `Events:DatabasePath`
- `Events:RetentionDays`
- `Events:CleanupIntervalSeconds`
- `Events:MaxBacklogRows`
- `Profiles:Directory`
- `Edge:EnableCentralReporting`
- `Edge:CentralApiBaseUrl`
- `Edge:EnableEventShipping`
- `Edge:EdgeId`
- `Edge:EventIngestToken`
- `Edge:EventBatchSize`

Central also requires:

- `ConnectionStrings:Events`
- `EventIngest:RequireToken`
- `EventIngest:EdgeTokens:{EdgeId}`
- `Webhook:Enabled`
- `Webhook:PollIntervalMs`
- `Webhook:RequestTimeoutSeconds`
- `Webhook:EventTypePrefix`

Replace all example passwords and tokens before production use.

### Webhook subscriptions

This example subscribes to completed cycles for one material lot. By default, delivery starts with events ingested after subscription creation; use `StartAfterIngestId: 0` to replay central history:

```bash
curl -X POST http://localhost:8000/api/v1/subscriptions \
  -H 'Content-Type: application/json' \
  -d '{
    "name": "mes-cycle-consumer",
    "endpoint": "https://mes.example.com/hooks/ingot",
    "eventTypes": ["cycle.completed"],
    "context": {"material_lot": "LOT-001"},
    "secret": "replace-with-a-long-random-secret"
  }'
```

Consumers should deduplicate on the CloudEvent `id`. When a secret is configured, they should also verify the HMAC-SHA256 value in `X-Ingot-Signature`.

### SQL reports and integrity checks

Two scripts are ready for direct `psql` use:

```bash
docker exec -i ingot-postgres psql -U ingot -d ingot \
  -v material_lot=LOT-001 < scripts/report-production-events.sql

docker exec -i ingot-postgres psql -U ingot -d ingot \
  -v edge_id=EDGE-001 < scripts/verify-event-integrity.sql
```

The first produces cycle duration, recipe, good-count, and material-trace reports. The second verifies event identity, per-edge sequence continuity, and orphaned ingest reservations.

### Device Level

- `SourceCode` for v2 (`PlcCode` remains accepted in v1)
- `Driver`
- `Host`
- `Port`
- `ProtocolOptions`
- `Channels`
- `Asset`
- `Profile`
- `EventRules`

Before you go live, run:

```bash
dotnet run --project src/Ingot.Edge.Agent -- --validate-configs
```

That is part of the normal deployment path, not an optional trick.

## Post-Deployment Checks

After the system starts, verify these first.

### 1. Process State

- `Edge Agent` is running
- `InfluxDB` is reachable
- PostgreSQL and `Central API` are healthy when central shipping is enabled

### 2. Health Endpoint

```bash
curl http://localhost:8001/health
```

### 3. Metrics Endpoint

```bash
curl http://localhost:8001/metrics
```

### 4. Logs and Errors

Watch for:

- PLC connectivity errors
- TSDB write failures
- event-log health or event persistence failures
- event shipping failures or edge-sequence gap metrics
- configuration reload problems

### 5. Storage Writes

Confirm that measurements are being written into InfluxDB.

## Backup Strategy

If you need to retain diagnostics and conditional acquisition context, back up at least two categories of data.

### Local Runtime State

- `Data/logs.db`
- `Data/acquisition-state.db`
- `Data/events.db`

If you need a longer local diagnostic window, increase `Logging:RetentionDays` explicitly instead of assuming `logs.db` grows indefinitely.

### Storage

- InfluxDB bucket data
- the central PostgreSQL database, including an appropriate backup/WAL policy

Because the current runtime does not depend on a local raw-data replay queue, the primary backup target should be InfluxDB itself.

## Operational Advice

- run `Edge Agent` under `systemd`, Windows Service, or another service manager
- treat Central and Edge as separate operational surfaces
- operate `Edge -> InfluxDB` telemetry and `Edge outbox -> Central PostgreSQL` events as separate health paths
- if TSDB writes fail, treat that as an operational alarm to fix immediately rather than something a replay worker will clean up later

## Related Docs

- [Getting Started](tutorial-getting-started.en.md)
- [Configuration](tutorial-configuration.en.md)
- [Driver Catalog](hsl-drivers.en.md)
