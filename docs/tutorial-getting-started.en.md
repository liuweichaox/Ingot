# Getting Started

This guide explains how to complete a minimal local validation of Ingot, including configuration validation, Edge Agent startup, and verification of the primary acquisition path.

## Prerequisites

- .NET 10 SDK
- Docker
- InfluxDB 2.x

If you only want to validate configuration first, you do not need a real PLC yet.

## Step 1: Build the Solution

From the repository root:

```bash
dotnet build Ingot.sln
```

## Step 2: Start InfluxDB

The repository includes a simple compose file:

```bash
docker compose -f docker-compose.tsdb.yml up -d
```

If you already have your own InfluxDB instance, just make sure the `InfluxDB` section in [appsettings.json](../src/Ingot.Edge.Agent/appsettings.json) points to the right endpoint.

## Step 3: Review Device Configuration

The default device config directory is:

- [src/Ingot.Edge.Agent/Configs](../src/Ingot.Edge.Agent/Configs)

The repository already includes a local development sample:

- [TEST_PLC.json](../src/Ingot.Edge.Agent/Configs/TEST_PLC.json)

You can also use:

- [examples/device-configs](../examples/device-configs)
- [device-config.schema.json](../schemas/device-config.schema.json)

## Step 4: Validate Configuration Offline

Validate configs before starting the runtime:

```bash
dotnet run --project src/Ingot.Edge.Agent -- --validate-configs
```

To validate another directory:

```bash
dotnet run --project src/Ingot.Edge.Agent -- --validate-configs --config-dir ./examples/device-configs
```

On success, you should see output like:

```text
[OK] .../TEST_PLC.json (TEST_PLC)
```

## Step 5: Start the Edge Agent

```bash
dotnet run --project src/Ingot.Edge.Agent
```

The default URL comes from [appsettings.json](../src/Ingot.Edge.Agent/appsettings.json):

- `http://localhost:8001`

Useful endpoints after startup:

- `/health`
- `/metrics`
- `/api/logs`
- `/api/acquisition/plc-connections`
- `/api/v1/events`
- `/api/v1/events/stream`

## Optional: Start the PLC Simulator

For a local closed-loop workflow, start the simulator:

```bash
dotnet run --project src/Ingot.Simulator
```

The simulator listens on port `502` by default and prints changing registers to the console. Details:

- [src/Ingot.Simulator/README.md](../src/Ingot.Simulator/README.md)

## Verification

You can verify the system from four angles.

### 1. Agent liveness

```bash
curl http://localhost:8001/health
```

### 2. Config loading

Startup logs should show successful config validation and runtime startup for PLCs and channels.

### 3. Local diagnostic files

The runtime directory should usually contain:

- `Data/logs.db`
- `Data/acquisition-state.db`

Meaning:

- `logs.db` supports local log querying and keeps 30 days of logs by default
- `Logging:RetentionDays` can change the retention window, and `<= 0` disables cleanup
- `acquisition-state.db` stores active-cycle recovery state for conditional acquisition

### 4. InfluxDB writes

If InfluxDB is reachable, you should see measurements being written into the configured bucket.

If you do not, check:

- Edge logs for TSDB write failures
- `/metrics` for runtime errors
- `InfluxDB:Url`, `Bucket`, `Org`, and `Token`

## Optional: Start Central Components

Central services are not required by the acquisition path. The simplest option starts PostgreSQL, Central API, and Central Web together:

```bash
docker compose -f docker-compose.events.yml up -d --build
```

For local web development, keep PostgreSQL and Central API running, then use:

```bash
cd src/Ingot.Central.Web
npm install
npm run dev
```

Open `http://localhost:3000/events` for cross-edge events. The tokens must match: `Edge:EventIngestToken` maps to `EventIngest:EdgeTokens:{EdgeId}`.

## Common Issues

### Config validation fails

Check:

- whether `Driver` is a full stable driver name
- whether `ProtocolOptions` contains keys unsupported by that driver
- whether `PlcCode` is duplicated across files

### InfluxDB is not receiving data

This usually means storage is unavailable or write settings are incorrect.

Check:

- whether InfluxDB is running
- whether `InfluxDB:Url` is correct
- whether `Bucket`, `Org`, and `Token` match the target instance
- Edge logs for storage write failures

### The simulator works, but the real PLC does not

Check:

- `Host` and `Port`
- network connectivity
- whether the selected driver matches the real device and protocol

## Related Docs

- [Configuration](tutorial-configuration.en.md)
- [Driver Catalog](hsl-drivers.en.md)
- [Deployment](tutorial-deployment.en.md)
