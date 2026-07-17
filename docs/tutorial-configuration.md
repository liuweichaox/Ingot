# 配置说明

本文说明 Ingot 的配置模型、字段约束和目录规则。配置设计遵循以下原则：

- 顶层结构稳定
- 驱动选择明确
- 驱动私有差异放进 `ProtocolOptions`
- 配置先校验，再运行

## 配置入口

设备配置默认目录：

- [src/Ingot.Edge.Agent/Configs](../src/Ingot.Edge.Agent/Configs)

应用配置：

- [src/Ingot.Edge.Agent/appsettings.json](../src/Ingot.Edge.Agent/appsettings.json)

离线校验入口：

```bash
dotnet run --project src/Ingot.Edge.Agent -- --validate-configs
```

JSON Schema：

- [v1 设备配置](../schemas/device-config.schema.json)
- [v2 数据源配置](../schemas/device-config.v2.schema.json)
- [行业 Profile](../schemas/profile.schema.json)

示例配置：

- [../examples/device-configs](../examples/device-configs)

## 设备配置结构

最小示例：

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

字段说明：

| 字段 | 必填 | 说明 |
|------|:----:|------|
| `SchemaVersion` | ✅ | 配置结构版本；兼容配置使用 `1`，新配置建议使用 `2` |
| `IsEnabled` | ✅ | 是否启用该设备 |
| `PlcCode` | ✅ | 设备唯一编码，不能和其他文件重复 |
| `Driver` | ✅ | 稳定驱动名称，例如 `melsec-a1e`、`siemens-s7` |
| `Host` | ✅ | PLC 主机地址，支持 IP 或主机名 |
| `Port` | ✅ | PLC 端口 |
| `ProtocolOptions` | 可选 | 驱动附加参数 |
| `HeartbeatMonitorRegister` | ✅ | 心跳寄存器 |
| `HeartbeatPollingInterval` | ✅ | 心跳轮询间隔，毫秒 |
| `Channels` | ✅ | 通道列表 |

规则：

- `Driver` 只接受完整名称，不接受别名
- `ProtocolOptions` 不是自由字典，未声明支持的键会被拒绝
- `PlcCode` 在配置目录内必须唯一

## v2 数据源与事件配置

新配置建议使用 `SchemaVersion: 2`。顶层身份从 PLC 专有的 `PlcCode` 改为源中立的 `SourceCode`，并显式区分通讯源与业务资产。

```json
{
  "SchemaVersion": 2,
  "IsEnabled": true,
  "SourceCode": "POL-03-PLC",
  "Adapter": "plc",
  "Profile": "optical",
  "Asset": {
    "Type": "polishing-machine",
    "Id": "POL-03"
  },
  "Driver": "melsec-mc",
  "Host": "192.168.10.33",
  "Port": 6000,
  "HeartbeatMonitorRegister": "D100",
  "HeartbeatPollingInterval": 5000,
  "EventRules": []
}
```

v2 新字段：

| 字段 | 说明 |
|---|---|
| `SourceCode` | 通讯端点唯一编码 |
| `Adapter` | 源适配器；当前实现为 `plc` |
| `Profile` | 生效的行业事件语言 |
| `Asset` | 默认业务资产；事件的主体，不等同于通讯端点 |
| `EventRules` | 与 `Channels` 并列的事件派生规则 |

完整示例见 [optical-polisher.v2.json](../examples/device-configs/optical-polisher.v2.json)。

## EventRules

当前支持四种触发器：

| Kind | 语义 | 事件 |
|---|---|---|
| `EdgePair` | 激活/失活边沿 | `*.started` / `*.completed` |
| `ValueChanged` | 值发生变化 | `EventType` 指定的单事件 |
| `BitFlag` | 指定位从 0→1 / 1→0 | `*.raised` / `*.cleared` |
| `Threshold` | 进入/离开阈值区间 | `*.entered` / `*.exited` |

值变化规则可以同时更新上下文：

```json
{
  "RuleId": "lot-change",
  "Category": "material",
  "EventType": "material.lot_changed",
  "ContextKeys": ["material_lot"],
  "SetContext": {
    "material_lot": "$value"
  },
  "Trigger": {
    "Kind": "ValueChanged",
    "Tag": "D6200",
    "DataType": "string",
    "StringByteLength": 20
  }
}
```

`SetContext` 先写入资产上下文，随后事件获取快照，因此本次事件会携带变更后的值。规则中的对象类型、事件类型与必需上下文必须在所选 Profile 中声明。

成对事件还可以在边沿成立时读取额外字段并固化进事件载荷：

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

`SnapshotOnStart` 与 `SnapshotOnEnd` 的字段读取失败时，周期事实仍然发出，并在 `data.snapshot_errors` 中列出失败字段。`StringByteLength` 在 Ingot 配置层始终表示字节数；HSL 适配器会截断协议层可能返回的额外字节，避免读入相邻寄存器。

## 通道配置

一个设备下可以配置多个通道。每个通道通常对应一个 measurement。

示例：

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

字段说明：

| 字段 | 必填 | 说明 |
|------|:----:|------|
| `Measurement` | ✅ | 写入存储的 measurement |
| `ChannelCode` | ✅ | 通道编码 |
| `EnableBatchRead` | ✅ | 是否启用批量读取 |
| `BatchReadRegister` | 条件 | 批量读取起始地址 |
| `BatchReadLength` | 条件 | 批量读取长度 |
| `BatchSize` | ✅ | 队列聚合大小 |
| `AcquisitionInterval` | ✅ | 采集间隔，毫秒 |
| `AcquisitionMode` | ✅ | `Always` 或 `Conditional` |
| `ConditionalAcquisition` | 条件 | 条件采集配置 |
| `Metrics` | 条件 | 指标列表 |

## 指标配置

示例：

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

字段说明：

| 字段 | 必填 | 说明 |
|------|:----:|------|
| `MetricLabel` | ✅ | 可读标签 |
| `FieldName` | ✅ | 存储字段名 |
| `Register` | ✅ | PLC 地址 |
| `Index` | ✅ | 批量读取缓冲区偏移 |
| `DataType` | ✅ | 数据类型 |
| `EvalExpression` | 可选 | 表达式计算 |
| `StringByteLength` | 条件 | 字符串字节长度 |
| `Encoding` | 条件 | 字符串编码，建议 `utf-8` |

说明：

- 固定长度字符串会自动去除尾部 `\0`
- 表达式仅对数值类型生效

## 采集模式

### Always

适合连续信号、实时量：

```json
{
  "AcquisitionMode": "Always",
  "AcquisitionInterval": 100
}
```

### Conditional

适合周期开始/结束、事件触发：

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

Conditional 模式语义：

- 正式业务事件写为 `Start` / `End`
- 恢复诊断写入 `<measurement>_diagnostic`
- 正式统计只应基于成对的 `Start` / `End`

## `ProtocolOptions` 说明

`ProtocolOptions` 是驱动的附加参数区。

通用键：

- `connect-timeout-ms`
- `receive-timeout-ms`

部分驱动还有专属键，例如：

- `siemens-s7` 使用 `plc`
- `inovance-tcp` 使用 `series`、`station`
- `lsis-fast-enet` 使用 `cpu-type`、`slot-no`

完整清单见：

- [hsl-drivers.md](hsl-drivers.md)

## 配置目录

默认设备配置目录来自应用配置：

```json
{
  "Acquisition": {
    "DeviceConfigService": {
      "ConfigDirectory": "Configs"
    }
  }
}
```

规则：

- 相对路径基于应用运行目录解析
- 离线校验默认使用同一个目录
- 可用 `--config-dir` 临时覆盖

## 应用日志配置

默认本地日志配置如下：

```json
{
  "Logging": {
    "DatabasePath": "Data/logs.db",
    "RetentionDays": 30
  }
}
```

说明：

- `Logging:DatabasePath` 用于设置 SQLite 日志数据库路径
- 相对路径基于应用运行目录解析
- `Logging:RetentionDays` 默认值为 `30`
- `Logging:RetentionDays <= 0` 表示关闭自动清理

## 配置建议

- v2 的 `SourceCode`、`ChannelCode` 使用稳定、可读、可搜索的命名
- 连续寄存器优先批量读取
- 在采集阶段做基础单位换算，不把脏原始值留给下游
- 在上线前先执行配置校验
- 不要把驱动不支持的私有参数硬塞进 `ProtocolOptions`

## 相关文档

- [快速开始](tutorial-getting-started.md)
- [驱动目录](hsl-drivers.md)
- [部署说明](tutorial-deployment.md)
