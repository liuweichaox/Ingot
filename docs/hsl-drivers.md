# 驱动目录

本文档说明项目当前内置 PLC 驱动目录及其支持的 `ProtocolOptions`。相关驱动实现由 [HslStandardPlcDriverProvider.cs](../src/Ingot.Infrastructure/Clients/HslStandardPlcDriverProvider.cs) 提供。

本文档主要覆盖以下内容：

- 告诉你当前有哪些稳定 `Driver` 名称
- 告诉你每个驱动当前支持哪些 `ProtocolOptions`

## 使用约束

设备配置里通过 `Driver` 选择协议：

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

规则：

- 只接受完整驱动名称
- 不接受别名
- `ProtocolOptions` 只允许当前驱动声明支持的键

## 通用 `ProtocolOptions`

所有内置驱动都支持：

- `connect-timeout-ms`
- `receive-timeout-ms`

驼峰写法也兼容，例如：

- `cpuType`
- `slotNo`

## 扩展协议选项

如果某个驱动没有出现在下面的列表里，说明它当前只支持通用超时配置。

### 西门子

#### `siemens-s7`

- `plc`
  可选值示例：`S1200`、`S1500`、`S200Smart`

### 汇川

#### `inovance-tcp`

- `series`
- `station`

#### `inovance-serial-over-tcp`

- `series`
- `station`

### 信捷

#### `xinje-tcp`

- `series`
- `station`

#### `xinje-serial-over-tcp`

- `series`
- `station`

#### `xinje-internal`

- `station`

### 台达

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

## 内置驱动清单

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

### 其他内置驱动

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

## 相关示例

示例配置目录：

- [../examples/device-configs](../examples/device-configs)

你也可以直接用离线校验命令检查自己的目录：

```bash
dotnet run --project src/Ingot.Edge.Agent -- --validate-configs --config-dir ./examples/device-configs
```

## 扩展新驱动

如果当前内置驱动不够用，扩展入口是：

- [IPlcDriverProvider](../src/Ingot.Application/Abstractions/IPlcDriverProvider.cs)
- [IPlcClientService](../src/Ingot.Application/Abstractions/IPlcClientService.cs)
- [../CONTRIBUTING.md](../CONTRIBUTING.md)
