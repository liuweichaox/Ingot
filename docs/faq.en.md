# FAQ

This document collects recurring questions about the project while avoiding unnecessary repetition of tutorial content.

If you are new to the project, start with:

- [Documentation Index](index.en.md)
- [Getting Started](tutorial-getting-started.en.md)
- [Configuration](tutorial-configuration.en.md)

## Project Scope

### What is the role of Ingot

It is edge-first production event infrastructure. PLC is the first source adapter.

It is responsible for:

- reading values from PLCs
- writing high-rate telemetry batches to a TSDB
- persisting cycles, parameter writes, and diagnostics as immutable production events
- exposing local event queries, SSE, cycle correlation, and business context
- exposing local logs, metrics, and diagnostics
- recovering active-cycle context for conditional acquisition

### What is the current system boundary

The current boundary is:

- the main product is the `Edge Agent`
- the central UI is an auxiliary control plane, not the acquisition path itself
- telemetry writes directly to InfluxDB and failed telemetry batches are dropped
- production events are appended to `events.db` before projection
- production events ship automatically from the local outbox to an idempotent PostgreSQL event hub
- central downtime does not stop local acquisition or event creation; pending events resume after recovery

Telemetry still has no local replay queue. Event buffering is bounded by `Events:MaxBacklogRows` and available edge disk space.

## Configuration and Drivers

### Which driver name should be used

Use the exact `Driver` names listed in the [driver catalog](hsl-drivers.en.md).

Do not rely on old aliases, abbreviations, or guessed names.

### Why does configuration validation fail

Common reasons:

- invalid JSON
- missing required fields
- empty or duplicated `SourceCode` (`PlcCode` in v1)
- unknown Profiles, object types, or event types in v2
- missing context keys required by a Profile
- `Driver` not found in the built-in catalog
- unsupported keys in `ProtocolOptions`

Run this first:

```bash
dotnet run --project src/Ingot.Edge.Agent -- --validate-configs
```

### Do I need to restart after editing config

Usually no.

The device configuration directory is watched for file changes, and valid configuration updates are reloaded automatically.

The important condition is:

- the new configuration must pass validation first

### How do I add a new PLC protocol

If the built-in catalog does not cover your protocol, add a provider.

Recommended extension path:

1. implement a new `IPlcDriverProvider`
2. reuse `PlcClientServiceBase` or provide your own `IPlcClientService`
3. register the provider at startup
4. document the new `Driver` and provide a config example

If you only use the built-in Hsl drivers, no core change is usually required.

## Acquisition and Storage

### Why is telemetry direct-to-TSDB while events use SQLite

Telemetry is high-rate and remains at-most-once. Production events are low-rate, high-value facts and are appended to `events.db` before any projection.

### Does InfluxDB downtime stop collection

It stops the current batch from being stored successfully.

Expected behavior:

- failed batches are logged
- failed batches are dropped
- later acquisition work keeps running and continues trying to write

That means TSDB write failures should be treated as operational alerts, not something a replay worker will eventually fix.

### Which local files should I expect

By default, the main runtime files are:

- `Data/logs.db`
- `Data/acquisition-state.db`

Meaning:

- `logs.db` supports local log querying and troubleshooting, with 30 days of retention by default
- `Logging:RetentionDays` changes the retention window, and `<= 0` disables cleanup
- `acquisition-state.db` stores active-cycle recovery state for conditional acquisition

### Why store active cycle state at all

Because conditional acquisition needs restart-time context recovery.

The active cycle is mirrored to:

- memory
- `Data/acquisition-state.db`

The purpose is not raw data replay. It is to preserve context for in-progress cycle semantics.

## Cycle Collection

### Does the first conditional sample trigger a fake edge

No.

Current behavior:

- the first sample builds a baseline
- initialization is not treated as a real edge event

### Why do I see `RecoveredStart` or `Interrupted`

These are recovery diagnostics emitted when the process restarts or resumes during an active cycle.

They should not be used as the formal business definition of a complete cycle.

For formal cycle analytics, still use paired `Start` / `End`.

## Operations and Troubleshooting

### How do I know the system is healthy

Start with:

```bash
curl http://localhost:8001/health
curl http://localhost:8001/metrics
```

Then verify:

- whether Edge logs contain PLC connectivity errors
- whether Edge logs contain TSDB write failures
- whether InfluxDB is receiving measurements

### Why is host-process deployment recommended for Edge

Because PLC networking problems are usually real network problems:

- NIC selection
- routes
- VLANs
- firewalls
- device reachability

That is easier to troubleshoot with a host process than with a containerized edge runtime.

Central components and InfluxDB are better containerization candidates.

### What happens if Central is down

If Central is unavailable:

- registration and heartbeat reporting fail
- the central UI is unavailable
- events remain pending in the Edge `events.db` outbox
- EventShipper backs off and resumes from the first unacknowledged `Seq`
- safe resends may occur, but Central deduplicates by `EventId` and `(EdgeId, Seq)`

But the collection path should continue running.

## Extension and Development

### How do I replace the storage backend

Implement `IDataStorageService` and replace the default registration in the host.

### How do I change failure behavior

Modify `QueueService`, and update:

- automated tests
- README
- design docs

### Why does the project use JSON configuration

Because the goal here is:

- simple
- readable
- hot-reload friendly
- easy to validate and bind in .NET

What matters more than switching to YAML or TOML is:

- a stable config contract
- validation
- examples
- good error messages

## Related Docs

- [Configuration](tutorial-configuration.en.md)
- [Deployment](tutorial-deployment.en.md)
- [Driver Catalog](hsl-drivers.en.md)
- [Design](design.en.md)
