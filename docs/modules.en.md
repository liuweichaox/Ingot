# Modules

## Domain

`src/Ingot.Domain` contains source configuration, the `ProductionEvent` envelope, event rules and queries, asset references, and Profile definitions. It has no infrastructure dependencies.

## Application

`src/Ingot.Application` defines acquisition, event log/sink/shipper, edge context/state, event rule collector, Profile registry, source driver, queue, storage, and metrics contracts.

## Infrastructure

`src/Ingot.Infrastructure` provides:

- acquisition orchestration and v1 Conditional event integration
- v2 `EventRuleCollector` with `EdgePair`, `ValueChanged`, `BitFlag`, and `Threshold`
- `EdgeStateStore` for active cycles and context
- `EventSink` and `SqliteEventLog`
- `HttpEventShipper` with ordered batches, acknowledgement-based advancement, and retry
- `JsonProfileRegistry`
- in-memory telemetry batching and InfluxDB storage
- Hsl PLC drivers, config hot reload, logs, and metrics

## Edge Agent

`src/Ingot.Edge.Agent` composes the runtime, loads configs and Profiles, runs background services, and exposes event, cycle, context, acquisition, log, metric, and health APIs.

## Contracts

`src/Ingot.Contracts` contains edge registration/heartbeat, control-write, event-batch, and central event-result contracts. Control writes use `SourceCode` while accepting v1 `PlcCode` JSON.

## Central

`src/Ingot.Central.Api` provides per-edge authenticated idempotent ingest, partitioned PostgreSQL event storage, JSONB context query, SSE, cycle aggregation, durable webhook subscriptions, CloudEvents/HMAC delivery, node registration, metrics, and diagnostics proxying. `src/Ingot.Central.Web` is a Vue 3/Vite UI for edges, events, metrics, and logs.

## Tests

`tests/Ingot.Core.Tests` covers event persistence and queries, rule semantics, restart state, Profile validation, configuration, queues, drivers, logs, edge-token validation, EventShipper acknowledgement behavior, scenario timelines, and CloudEvents webhook mapping/signatures.
