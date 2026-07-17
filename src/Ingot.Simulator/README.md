# PLC 模拟器 - Ingot Simulator

用于测试数据采集系统的 PLC 模拟器，基于 HslCommunication 库实现。

## 功能特性

- ✅ 模拟三菱 PLC（MelsecMcServer）
- ✅ 自动数据更新（心跳、6 个传感器指标、生产序号）
- ✅ 控制台实时输出当前模拟数据
- ✅ 支持条件采集测试（生产序号触发）
- ✅ RFC 剧本模式：换批次 → 换工装 → 3 个周期 → 报警并恢复

## 快速开始

### 运行模拟器

```bash
cd src/Ingot.Simulator
dotnet run
```

### 指定端口

```bash
dotnet run -- Port=502
```

或在 `appsettings.json` 中配置：

```json
{
  "Port": 502
}
```

### 运行生产事件剧本

```bash
dotnet run -- Mode=Scenario
```

需要更快完成演示时：

```bash
dotnet run -- Mode=Scenario ScenarioSpeed=4
```

剧本每 22 秒循环一次。配套的 v2 配置位于：

- `examples/scenarios/optical-polisher.v2.json`

它会从 `D6200` 获取批次、从 `D6300` 获取工装、从 `D6006` 获取周期边沿，并从 `M100` 获取主轴过载报警。

## 使用方法

### 启动后

模拟器会自动启动并开始监听指定端口（默认 502）。

**验证启动是否成功：**

```bash
# Windows
netstat -ano | findstr :502

# 应该看到类似输出：
# TCP    0.0.0.0:502    0.0.0.0:0    LISTENING    <进程ID>
```

如果端口没有在监听，检查程序日志中的错误信息。

### 自动更新的寄存器

#### 心跳寄存器

- **D100** - 心跳寄存器（默认写入 `0`，用于验证采集链路可写）

#### 传感器数据（批量读取起始地址：D6000）

| 地址    | 索引 | 指标   | 范围            | 单位     | 说明                 |
|-------|----|------|---------------|--------|--------------------|
| D6000 | 0  | 温度   | 2000-3000     | 0.1°C  | 正弦波动（实际值：20-30°C）  |
| D6001 | 2  | 压力   | 1000-2000     | 0.1MPa | 余弦波动（实际值：10-20MPa） |
| D6002 | 4  | 电流   | 0-500         | 0.1A   | 正弦波动（实际值：0-50A）    |
| D6003 | 6  | 电压   | 3800-4200     | 0.1V   | 余弦波动（实际值：380-420V） |
| D6004 | 8  | 光栅位置 | 0-1000        | mm     | 正弦波动               |
| D6005 | 10 | 伺服速度 | 0-3000        | rpm    | 余弦波动               |
| D6006 | 12 | 生产序号 | 0, 1, 2, 3... | -      | 特殊逻辑见下文            |

#### 字符串测试寄存器

- **D6010** - 批次号字符串（示例：`BATCH-001`，每个生产周期递增）
- 用于验证 `DataType=string` 的读取能力
- **D6200** - 物料批次（剧本模式）
- **D6300** - 工装编码（剧本模式）
- **M100.0** - 主轴过载报警（剧本模式）

#### 生产序号特殊逻辑

**D6006** - 生产序号，遵循以下模式：

- 每个生产序号持续 **10 秒** 保持不变
- 然后变为 **0** 持续 **5 秒**
- 然后生产序号 **+1**

**示例模式**：`111111, 000, 2222, 000, 333333...`

- 时间 0-9 秒：生产序号 = 1
- 时间 10-12 秒：生产序号 = 0
- 时间 13-22 秒：生产序号 = 2
- 时间 23-25 秒：生产序号 = 0
- 时间 26-35 秒：生产序号 = 3
- ...以此类推

## 测试配置

在 `src/Ingot.Edge.Agent/Configs/` 目录创建 `TEST_PLC.json`：

### 完整配置示例

```json
{
  "IsEnabled": true,
  "PlcCode": "TEST_PLC",
  "Driver": "melsec-a1e",
  "Host": "127.0.0.1",
  "Port": 502,
  "HeartbeatMonitorRegister": "D100",
  "HeartbeatPollingInterval": 2000,
  "Channels": [
    {
      "Measurement": "sensor",
      "ChannelCode": "CH01",
      "EnableBatchRead": true,
      "BatchReadRegister": "D6000",
      "BatchReadLength": 14,
      "BatchSize": 10,
      "AcquisitionInterval": 0,
      "AcquisitionMode": "Always",
      "Metrics": [
        { "MetricLabel": "temperature", "FieldName": "temperature", "Register": "D6000", "Index": 0, "DataType": "short", "EvalExpression": "value / 100.0" },
        { "MetricLabel": "pressure", "FieldName": "pressure", "Register": "D6001", "Index": 2, "DataType": "short", "EvalExpression": "value / 100.0" },
        { "MetricLabel": "current", "FieldName": "current", "Register": "D6002", "Index": 4, "DataType": "short", "EvalExpression": "value / 10.0" },
        { "MetricLabel": "voltage", "FieldName": "voltage", "Register": "D6003", "Index": 6, "DataType": "short", "EvalExpression": "value / 10.0" },
        { "MetricLabel": "lightBarrierPosition", "FieldName": "lightBarrierPosition", "Register": "D6004", "Index": 8, "DataType": "short" },
        { "MetricLabel": "servoSpeed", "FieldName": "servoSpeed", "Register": "D6005", "Index": 10, "DataType": "short" },
        { "MetricLabel": "productionSerial", "FieldName": "productionSerial", "Register": "D6006", "Index": 12, "DataType": "short" },
        { "MetricLabel": "productCode", "FieldName": "productCode", "Register": "D6010", "DataType": "string", "StringByteLength": 16, "Encoding": "utf-8" }
      ]
    },
    {
      "Measurement": "production",
      "ChannelCode": "CH01",
      "EnableBatchRead": false,
      "BatchReadRegister": null,
      "BatchReadLength": 0,
      "BatchSize": 1,
      "AcquisitionInterval": 0,
      "AcquisitionMode": "Conditional",
      "ConditionalAcquisition": {
        "Register": "D6006",
        "DataType": "short",
        "StartTriggerMode": "RisingEdge",
        "EndTriggerMode": "FallingEdge"
      },
      "Metrics": null
    }
  ]
}
```

### 配置说明

#### 传感器通道（sensor）

- **采集模式**：Always（无条件持续采集）
- **批量读取**：启用，从 D6000 开始读取 14 个寄存器
- **数据点**：包含所有 7 个指标
- **表达式转换**：部分指标配置了表达式，将原始值转换为实际物理值

#### 生产通道（production）

- **采集模式**：Conditional（条件采集）
- **触发条件**：
    - **StartTriggerMode**: `RisingEdge` - 当生产序号从 0 变为非 0 时触发开始事件
    - **EndTriggerMode**: `FallingEdge` - 当生产序号从非 0 变为 0 时触发结束事件
- **数据点**：null（仅记录触发事件，不采集具体数据）

## 数据流说明

### 传感器通道（sensor）

- 持续采集所有 7 个指标（包括生产序号）
- 采集频率：100ms
- 数据保存到 InfluxDB 的 `sensor` measurement
- 每条记录都包含所有传感器数据

### 生产通道（production）

- 仅在以下情况采集：
    - **Start 事件**：当生产序号从 0 变为非 0 时（例如：0→1, 0→2）
    - **End 事件**：当生产序号从非 0 变为 0 时
- 数据保存到 InfluxDB 的 `production` measurement
- 每个生产周期生成唯一的 `cycle_id`（GUID）
- 事件类型通过 `event_type` 字段标识（正常情况下为 `Start` / `End`）
- 如果服务在生产周期中间重启，系统还可能写入 `RecoveredStart` / `Interrupted` 作为恢复诊断事件
- 恢复诊断事件会写入 `production_diagnostic` measurement
- 正式周期统计应只使用成对的 `Start` / `End`

## 测试流程

1. **启动模拟器**：

   ```bash
   cd src/Ingot.Simulator
   dotnet run
   ```

   观察输出，确认模拟器已启动并开始更新数据。

2. **启动采集系统**：

   ```bash
   dotnet run --project ./src/Ingot.Edge.Agent/Ingot.Edge.Agent.csproj
   dotnet run --project ./src/Ingot.Central.Api/Ingot.Central.Api.csproj

   cd ./src/Ingot.Central.Web
   npm install
   npm run dev
   ```

   系统会自动连接到模拟器（127.0.0.1:502）并开始采集。

3. **观察数据**：
    - 访问 `http://localhost:3000/events` 查看中心生产事件
    - 访问 `http://localhost:8000/metrics` 查看中心 API Prometheus 指标
    - 检查 InfluxDB 中的 `sensor` 和 `production` measurement

### Phase 2 事件链验收

1. 将 Agent 的 `Acquisition:DeviceConfigService:ConfigDirectory` 指向 `examples/scenarios`。
2. 使用 `Mode=Scenario` 启动模拟器，再启动 Edge Agent。
3. 等待一轮剧本完成，然后查询：

   ```bash
   curl "http://localhost:8001/api/v1/events?ctx.material_lot=LOT-001&limit=100"
   ```

4. 结果应包含批次变更、工装变更、三组成对周期，以及报警 raised/cleared；周期事件应携带 `material_lot=LOT-001` 与对应 `tooling`。

## 数据映射表

| 字段名                  | 寄存器   | 索引 | 原始范围          | 转换后范围         | 表达式           | 单位  |
|----------------------|-------|----|---------------|---------------|---------------|-----|
| temperature          | D6000 | 0  | 2000-3000     | 20.0-30.0     | value / 100.0 | °C  |
| pressure             | D6001 | 2  | 1000-2000     | 10.0-20.0     | value / 100.0 | MPa |
| current              | D6002 | 4  | 0-500         | 0-50          | value / 10.0  | A   |
| voltage              | D6003 | 6  | 3800-4200     | 380-420       | value / 10.0  | V   |
| lightBarrierPosition | D6004 | 8  | 0-1000        | 0-1000        | 无             | mm  |
| servoSpeed           | D6005 | 10 | 0-3000        | 0-3000        | 无             | rpm |
| productionSerial     | D6006 | 12 | 0, 1, 2, 3... | 0, 1, 2, 3... | 无             | -   |

## 架构说明

- **直接使用 HslCommunication**：使用 `MelsecMcServer`，无需自定义内存管理
- **易于扩展**：可轻松添加更多传感器或修改模拟逻辑

## 注意事项

1. **端口占用**：确保 502 端口未被占用，或使用其他端口
2. **生产序号重置**：模拟器重启后，生产序号会从 1 重新开始
3. **时间同步**：生产序号逻辑基于模拟器启动时间，确保时间准确
4. **批量读取**：传感器数据使用批量读取优化，地址必须连续
