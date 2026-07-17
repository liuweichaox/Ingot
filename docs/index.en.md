# Documentation Home

This documentation set describes how to configure, deploy, operate, understand, and extend Ingot.

For faster navigation, it is organized by usage goal rather than as a loose collection of isolated notes.

## Recommended Reading Order

If this is your first time with the project, read in this order:

1. [Getting Started](tutorial-getting-started.en.md)
2. [Configuration](tutorial-configuration.en.md)
3. [Driver Catalog](hsl-drivers.en.md)
4. [Deployment](tutorial-deployment.en.md)

## Read by Usage Goal

### Local Startup and Integration

- [Getting Started](tutorial-getting-started.en.md)
- [Configuration](tutorial-configuration.en.md)

### Connecting a Real PLC

- [Configuration](tutorial-configuration.en.md)
- [Driver Catalog](hsl-drivers.en.md)
- [FAQ](faq.en.md)

### Deployment and Operations

- [Deployment](tutorial-deployment.en.md)
- [FAQ](faq.en.md)

### Architecture and Module Understanding

- [Design](design.en.md)
- [Modules](modules.en.md)
- [Production Events RFC](rfc-production-events.md) (Chinese)

### Extension and Contribution

- [Development](tutorial-development.en.md)
- [Contributing](../CONTRIBUTING.en.md)

## Core Constraints

Before going deeper, keep these rules in mind:

- the `Edge Agent` is the main product
- `Central` is an optional control plane
- the runtime has two planes: telemetry writes directly to TSDB, events append to `events.db`
- PLC is the first source adapter; v2 event contracts use `SourceCode` and asset models
- drivers are selected by stable `Driver` names
- configuration must be validated before runtime
- formal business events and recovery diagnostics are stored separately
- Profiles constrain object types, event types, and required context

## Documentation Set

The documentation tree intentionally keeps only the core set:

- [Getting Started](tutorial-getting-started.en.md)
- [Configuration](tutorial-configuration.en.md)
- [Driver Catalog](hsl-drivers.en.md)
- [Deployment](tutorial-deployment.en.md)
- [Design](design.en.md)
- [Modules](modules.en.md)
- [Production Events RFC](rfc-production-events.md) (Chinese)
- [Brand & Logo](brand.md) (Chinese)
- [Development](tutorial-development.en.md)
- [FAQ](faq.en.md)
