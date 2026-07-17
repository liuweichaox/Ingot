# Configuration

This document describes the configuration model, field constraints, and directory rules used by Ingot.

The configuration model is designed to be:

- stable at the top level
- explicit about driver selection
- extensible through `ProtocolOptions`
- validated before runtime

## Configuration Entry Points

Default device config directory:

- [src/Ingot.Edge.Agent/Configs](../src/Ingot.Edge.Agent/Configs)

Application settings:

- [src/Ingot.Edge.Agent/appsettings.json](../src/Ingot.Edge.Agent/appsettings.json)

Offline validation:

```bash
dotnet run --project src/Ingot.Edge.Agent -- --validate-configs
```

JSON Schema:

- [v1 device config](../schemas/device-config.schema.json)
- [v2 source config](../schemas/device-config.v2.schema.json)
- [industry Profile](../schemas/profile.schema.json)

Example configs:

- [../examples/device-configs](../examples/device-configs)

## Device Configuration Structure

Minimal example:

```json
{
  "SchemaVersion": 1,
  "IsEnabled": true,
  "PlcCode": "PLC01",
  "Driver": "melsec-a1e",
  "Host": "127.0.0.1",
  "Port": 502,
  "ProtocolOptions": {
    "connect-timeout-ms": "5000",
    "receive-timeout-ms": "5000"
  },
  "HeartbeatMonitorRegister": "D100",
  "HeartbeatPollingInterval": 5000,
  "Channels": []
}
```

Field reference:

| Field | Required | Description |
|------|:--------:|-------------|
| `SchemaVersion` | ✅ | use `1` for compatibility configs and `2` for new source/event configs |
| `IsEnabled` | ✅ | whether the device is enabled |
| `PlcCode` | ✅ | unique device identifier, must not be duplicated across files |
| `Driver` | ✅ | stable driver name such as `melsec-a1e` or `siemens-s7` |
| `Host` | ✅ | PLC endpoint host, accepts IPs and DNS hostnames |
| `Port` | ✅ | PLC endpoint port |
| `ProtocolOptions` | Optional | driver-specific parameters |
| `HeartbeatMonitorRegister` | ✅ | heartbeat register |
| `HeartbeatPollingInterval` | ✅ | heartbeat polling interval in milliseconds |
| `Channels` | ✅ | channel list |

Rules:

- `Driver` accepts full names only
- `ProtocolOptions` is not an unrestricted bag; unsupported keys are rejected
- `PlcCode` must be unique inside the config directory

## v2 Source and Event Configuration

New configurations should use SchemaVersion 2 with source-neutral `SourceCode`, `Adapter`, `Profile`, `Asset`, and `EventRules`. See [optical-polisher.v2.json](../examples/device-configs/optical-polisher.v2.json).

Supported trigger kinds:

| Kind | Meaning | Output |
|---|---|---|
| `EdgePair` | active/inactive edge pair | `*.started` / `*.completed` |
| `ValueChanged` | value changed | one configured `EventType` |
| `BitFlag` | bit 0→1 / 1→0 | `*.raised` / `*.cleared` |
| `Threshold` | enter/leave a numeric range | `*.entered` / `*.exited` |

`SetContext` can update asset context before the event snapshot is created:

```json
{
  "RuleId": "lot-change",
  "Category": "material",
  "EventType": "material.lot_changed",
  "ContextKeys": ["material_lot"],
  "SetContext": { "material_lot": "$value" },
  "Trigger": {
    "Kind": "ValueChanged",
    "Tag": "D6200",
    "DataType": "string",
    "StringByteLength": 20
  }
}
```

The selected Profile must declare every referenced object type, event type, and required context key.

Pair rules can also capture source values into the immutable event payload at each edge:

```json
{
  "RuleId": "polish-cycle",
  "Category": "cycle",
  "ContextKeys": ["material_lot", "tooling"],
  "SnapshotOnStart": [
    {
      "FieldName": "recipe_id",
      "Tag": "D6100",
      "DataType": "string",
      "StringByteLength": 16
    }
  ],
  "SnapshotOnEnd": [
    {
      "FieldName": "good_count",
      "Tag": "D6110",
      "DataType": "ushort"
    }
  ],
  "Trigger": {
    "Kind": "EdgePair",
    "Tag": "D6006",
    "DataType": "short"
  }
}
```

Snapshot read failures do not suppress the cycle fact; failed fields are listed in `data.snapshot_errors`. `StringByteLength` always means bytes at the Ingot configuration boundary, and the HSL adapter truncates protocol over-reads so adjacent registers cannot leak into the value.

## Channel Configuration

A device can contain multiple channels. Each channel usually maps to one measurement.

Example:

```json
{
  "Measurement": "sensor",
  "ChannelCode": "PLC01C01",
  "EnableBatchRead": true,
  "BatchReadRegister": "D6000",
  "BatchReadLength": 10,
  "BatchSize": 10,
  "AcquisitionInterval": 100,
  "AcquisitionMode": "Always",
  "Metrics": []
}
```

Field reference:

| Field | Required | Description |
|------|:--------:|-------------|
| `Measurement` | ✅ | target measurement name |
| `ChannelCode` | ✅ | channel identifier |
| `EnableBatchRead` | ✅ | whether batch read is enabled |
| `BatchReadRegister` | Conditional | batch read starting register |
| `BatchReadLength` | Conditional | batch read length |
| `BatchSize` | ✅ | queue aggregation size |
| `AcquisitionInterval` | ✅ | collection interval in milliseconds |
| `AcquisitionMode` | ✅ | `Always` or `Conditional` |
| `ConditionalAcquisition` | Conditional | trigger configuration |
| `Metrics` | Conditional | metric list |

## Metric Configuration

Example:

```json
{
  "MetricLabel": "temperature",
  "FieldName": "temperature",
  "Register": "D6000",
  "Index": 0,
  "DataType": "short",
  "EvalExpression": "value / 100.0"
}
```

Field reference:

| Field | Required | Description |
|------|:--------:|-------------|
| `MetricLabel` | ✅ | human-readable label |
| `FieldName` | ✅ | stored field name |
| `Register` | ✅ | PLC address |
| `Index` | ✅ | offset inside a batch-read buffer |
| `DataType` | ✅ | data type |
| `EvalExpression` | Optional | transform expression |
| `StringByteLength` | Conditional | string byte length |
| `Encoding` | Conditional | string encoding, prefer `utf-8` |

Notes:

- fixed-length strings are sanitized to remove trailing `\0`
- expressions apply only to numeric values

## Acquisition Modes

### Always

Use for continuous signals:

```json
{
  "AcquisitionMode": "Always",
  "AcquisitionInterval": 100
}
```

### Conditional

Use for cycle boundaries and event-driven capture:

```json
{
  "AcquisitionMode": "Conditional",
  "ConditionalAcquisition": {
    "Register": "D6006",
    "DataType": "short",
    "StartTriggerMode": "RisingEdge",
    "EndTriggerMode": "FallingEdge"
  }
}
```

Conditional semantics:

- formal business events are written as `Start` / `End`
- recovery diagnostics are written to `<measurement>_diagnostic`
- formal analytics should be based only on paired `Start` / `End`

## `ProtocolOptions`

`ProtocolOptions` is the driver-specific extension area.

Common keys:

- `connect-timeout-ms`
- `receive-timeout-ms`

Some drivers add their own keys, for example:

- `siemens-s7` uses `plc`
- `inovance-tcp` uses `series` and `station`
- `lsis-fast-enet` uses `cpu-type` and `slot-no`

Full details:

- [hsl-drivers.en.md](hsl-drivers.en.md)

## Configuration Directory

The default device config directory comes from app settings:

```json
{
  "Acquisition": {
    "DeviceConfigService": {
      "ConfigDirectory": "Configs"
    }
  }
}
```

Rules:

- relative paths are resolved from the application base directory
- offline validation uses the same directory by default
- `--config-dir` can override it temporarily

## Application Logging Settings

The default local log settings are:

```json
{
  "Logging": {
    "DatabasePath": "Data/logs.db",
    "RetentionDays": 30
  }
}
```

Notes:

- `Logging:DatabasePath` sets the SQLite log database path
- relative log paths are resolved from the application base directory
- `Logging:RetentionDays` defaults to `30`
- setting `Logging:RetentionDays` to `<= 0` disables automatic log cleanup

## Configuration Guidance

- use stable, searchable `SourceCode` and `ChannelCode` values for v2 configs
- prefer batch reads for contiguous registers
- do basic unit conversion during acquisition, not downstream
- validate configs before deployment
- do not push private unsupported driver parameters into `ProtocolOptions`

## Related Docs

- [Getting Started](tutorial-getting-started.en.md)
- [Driver Catalog](hsl-drivers.en.md)
- [Deployment](tutorial-deployment.en.md)
