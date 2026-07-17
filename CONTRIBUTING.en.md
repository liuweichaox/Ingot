# Contributing Guide

Thanks for your interest in Ingot.

The project is acquisition-first. Its primary goal is not to become a generalized central platform, but to strengthen the PLC acquisition path in terms of stability, correctness, real-time behavior, and observability. Contributions should reinforce that objective.

## Contribution Principles

- The Edge Agent is the main product; Central API / Web are diagnostics and management helpers
- Do not bypass `QueueService` and write directly into storage
- Do not reintroduce `Type + switch` style hard-coded factories
- Extend PLC protocols through `IPlcDriverProvider`
- Keep configuration stable through full `Driver` names
- Update docs and examples together with code

## Local Development

```bash
dotnet restore
dotnet test tests/Ingot.Core.Tests/Ingot.Core.Tests.csproj
dotnet build Ingot.sln --no-restore
```

For end-to-end local validation, prepare:

- InfluxDB 2.x
- `src/Ingot.Edge.Agent/Configs/TEST_PLC.json`
- `src/Ingot.Simulator`

## Contribution Scope

Useful contributions include:

- new PLC drivers or improvements to existing ones
- reliability fixes in the acquisition path
- TSDB write and queue-semantics improvements
- docs, configuration examples, tutorials
- automated tests

## Adding a New PLC Driver

Recommended path:

1. Prefer inheriting `PlcClientServiceBase`, or implement `IPlcConnectionClient` / `IPlcDataAccessClient` / `IPlcTypedWriteClient` as needed
2. Implement an `IPlcDriverProvider`
3. Register the provider through DI
4. Add docs and sample configuration for the new driver
5. Add tests for important configuration behavior

Requirements:

- use a stable `Driver` name
- do not introduce alias systems
- keep the `Host` / `Port` contract honest and never ignore endpoint config silently
- only expose `ProtocolOptions` that the driver truly supports
- fail explicitly for unsupported `ProtocolOptions`

## Extending Storage Backends

- Replace the storage backend by implementing `IDataStorageService.SaveBatchAsync`
- Adjust queue semantics by modifying `QueueService` or `QueueBatchPersister`

Requirements:

- failure behavior must stay explicit
- logs and metrics must remain part of the contract
- README, design docs, and tests must be updated together

## Pre-Submission Checklist

At minimum, make sure that:

- the code builds
- relevant tests pass
- new behavior has at least minimal automated coverage
- README / tutorials / sample configs are updated
- local runtime artifacts are not committed

## Recommended PR Content

PR descriptions should include:

- problem statement
- design trade-offs
- risks
- validation steps
- migration notes if configuration behavior changes

## Changes We Intentionally Avoid

- forcing unlike PLC protocols into an oversized “unified” model
- bloating the shared configuration model for one driver-specific need
- adding configuration without tests and docs
- making claims the implementation cannot prove, such as absolute “zero data loss”
