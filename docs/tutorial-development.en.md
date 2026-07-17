# Development

This guide is intended for developers who want to modify the runtime, add drivers, or replace default implementations. It summarizes code entry points, extension boundaries, and pre-submission checks.

If you only want to run the system, start with:

- [Getting Started](tutorial-getting-started.en.md)
- [Configuration](tutorial-configuration.en.md)

## Code Reading Entry Points

Recommended order:

1. `src/Ingot.Edge.Agent/Program.cs`
2. `src/Ingot.Infrastructure/Acquisition/AcquisitionService.cs`
3. `src/Ingot.Infrastructure/Queues/QueueService.cs`
4. `src/Ingot.Infrastructure/Clients/HslStandardPlcDriverProvider.cs`
5. `src/Ingot.Infrastructure/DataStorages/InfluxDbDataStorageService.cs`

If you want the architectural picture first, read:

- [Modules](modules.en.md)
- [Design](design.en.md)

## Add a PLC Driver

Do not extend the runtime by piling more `switch` branches into a factory.

The recommended path is:

1. implement a new `IPlcDriverProvider`
2. reuse `PlcClientServiceBase` when it fits, or provide a new `IPlcClientService`
3. register the provider in the host
4. document the new `Driver` and provide an example config

Follow these rules:

- keep the `Driver` name stable and explicit
- be honest about whether `Host` and `Port` are used
- expose only real `ProtocolOptions`
- do not silently accept unused configuration
- do not leak driver-private logic into the upper acquisition flow

## Extend Storage

The current runtime batches messages in memory and writes them directly to storage.

### Replace the Storage Backend

Implement:

- `IDataStorageService`

The main entry point is:

- `SaveBatchAsync(List<DataMessage>)`

When extending storage:

- do not bypass `QueueService`
- define your success-return contract clearly
- update docs and tests together when failure behavior changes

### Adjust Queue Semantics

If you change `QueueService`, make the following decisions explicit:

- how batch boundaries are defined
- whether later batches continue after a failed batch
- how failures surface through logs and metrics

Do not hide runtime failure semantics inside implementation details.

## Change Acquisition Logic

If you are changing runtime acquisition behavior, understand these boundaries first:

- `HeartbeatMonitor` decides whether a PLC is readable
- `ChannelCollector` owns channel-level orchestration
- `ChannelMetricReader` reads fields
- `MetricExpressionEvaluator` evaluates expressions
- `AcquisitionStateManager` owns conditional recovery state

Do not merge:

- low-level PLC reads
- cycle semantics
- storage persistence

back into a single class.

## Change the Configuration System

The current configuration system is designed to be:

- JSON-based
- hot-reload friendly
- offline-validatable
- explicit about driver contracts

If you extend it, try to preserve:

- stable top-level fields
- protocol-specific differences pushed into `ProtocolOptions`
- validation rules that evolve together with documentation

## Testing Expectations

If you add new behavior, add at least one of:

- unit tests
- integration tests
- configuration validation tests

The highest-value coverage areas are:

- driver configuration contracts
- queue batching and failure behavior
- conditional acquisition recovery semantics

## Pre-Submission Checklist

Before opening a PR, run at least:

```bash
dotnet build Ingot.sln --no-restore
dotnet test tests/Ingot.Core.Tests/Ingot.Core.Tests.csproj
dotnet run --project src/Ingot.Edge.Agent -- --validate-configs
```

## Related Docs

- [Contributing](../CONTRIBUTING.en.md)
- [Modules](modules.en.md)
- [Design](design.en.md)
