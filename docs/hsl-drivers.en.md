# Driver Catalog

This document describes the current built-in PLC driver catalog and the supported `ProtocolOptions`. The corresponding driver implementation is provided by [HslStandardPlcDriverProvider.cs](../src/Ingot.Infrastructure/Clients/HslStandardPlcDriverProvider.cs).

It covers the following:

- list the stable `Driver` names supported by the runtime
- document which `ProtocolOptions` are currently supported per driver

## Usage Constraints

Select a protocol through `Driver` in the device config:

```json
{
  "Driver": "siemens-s7",
  "Host": "192.168.1.20",
  "Port": 102,
  "ProtocolOptions": {
    "plc": "S1200"
  }
}
```

Rules:

- full driver names only
- no aliases
- `ProtocolOptions` must match what the selected driver declares

## Common `ProtocolOptions`

All built-in drivers support:

- `connect-timeout-ms`
- `receive-timeout-ms`

CamelCase variants are also accepted internally, for example:

- `cpuType`
- `slotNo`

## Extended Protocol Options

If a driver is not listed below, it currently supports only the common timeout options.

### Siemens

#### `siemens-s7`

- `plc`
  Example values: `S1200`, `S1500`, `S200Smart`

### Inovance

#### `inovance-tcp`

- `series`
- `station`

#### `inovance-serial-over-tcp`

- `series`
- `station`

### XINJE

#### `xinje-tcp`

- `series`
- `station`

#### `xinje-serial-over-tcp`

- `series`
- `station`

#### `xinje-internal`

- `station`

### Delta

#### `delta-tcp`

- `station`

#### `delta-serial-over-tcp`

- `station`

#### `delta-serial-ascii-over-tcp`

- `station`

### LSIS

#### `lsis-fast-enet`

- `cpu-type`
- `slot-no`

### Panasonic

#### `panasonic-mewtocol`

- `station`

### MegMeet

#### `megmeet-tcp`

- `station`

#### `megmeet-serial-over-tcp`

- `station`

## Built-In Driver Catalog

### Mitsubishi / Melsec

- `melsec-a1e`
- `melsec-a1e-ascii`
- `melsec-a3c`
- `melsec-cip`
- `melsec-fxlinks`
- `melsec-fxserial`
- `melsec-mc`
- `melsec-mc-ascii`
- `melsec-mc-ascii-udp`
- `melsec-mc-udp`
- `melsec-mcr`

### Siemens

- `siemens-fetch-write`
- `siemens-ppi-over-tcp`
- `siemens-s7`

### Omron

- `omron-cip`
- `omron-connected-cip`
- `omron-fins`
- `omron-hostlink`
- `omron-hostlink-cmode`

### Allen-Bradley

- `allen-bradley-connected-cip`
- `allen-bradley-micro-cip`
- `allen-bradley-net`
- `allen-bradley-pccc`
- `allen-bradley-slc`

### Beckhoff

- `beckhoff-ads`

### Inovance

- `inovance-serial-over-tcp`
- `inovance-tcp`

### XINJE

- `xinje-internal`
- `xinje-serial-over-tcp`
- `xinje-tcp`

### Delta

- `delta-serial-ascii-over-tcp`
- `delta-serial-over-tcp`
- `delta-tcp`

### Keyence

- `keyence-mc`
- `keyence-mc-ascii`
- `keyence-nano-serial-over-tcp`

### LSIS

- `lsis-cnet-over-tcp`
- `lsis-fast-enet`

### Panasonic

- `panasonic-mc`
- `panasonic-mewtocol`

### MegMeet

- `megmeet-serial-over-tcp`
- `megmeet-tcp`

### Other built-in drivers

- `fatek-program`
- `freedom-tcp`
- `freedom-udp`
- `fuji-command-setting-type`
- `fuji-spb`
- `fuji-sph`
- `ge-srtp`
- `toyota-toyopuc`
- `turck-reader`
- `vigor-serial-over-tcp`
- `yamatake-digitron-cpl-over-tcp`
- `yaskawa-memobus-tcp`
- `yaskawa-memobus-udp`
- `yokogawa-link`

## Examples

Example config directory:

- [../examples/device-configs](../examples/device-configs)

You can validate your own directory with:

```bash
dotnet run --project src/Ingot.Edge.Agent -- --validate-configs --config-dir ./examples/device-configs
```

## Extending Drivers

If the built-in catalog is not enough, start here:

- [IPlcDriverProvider](../src/Ingot.Application/Abstractions/IPlcDriverProvider.cs)
- [IPlcClientService](../src/Ingot.Application/Abstractions/IPlcClientService.cs)
- [../CONTRIBUTING.en.md](../CONTRIBUTING.en.md)
