# 快速开始

本文说明如何在本地完成 Ingot 的最小可运行验证，包括配置校验、Edge Agent 启动以及主采集链路验证。

## 前置要求

- .NET 10 SDK
- Docker
- InfluxDB 2.x

如果你只是想先验证配置，不需要先准备 PLC。

## 第一步：构建项目

在仓库根目录执行：

```bash
dotnet build Ingot.sln
```

## 第二步：启动 InfluxDB

项目附带了一个简单的 compose 文件：

```bash
docker compose -f docker-compose.tsdb.yml up -d
```

如果你已经有自己的 InfluxDB，只需要确认 [appsettings.json](../src/Ingot.Edge.Agent/appsettings.json) 里的 `InfluxDB` 配置正确即可。

## 第三步：检查设备配置

默认设备配置目录是：

- [src/Ingot.Edge.Agent/Configs](../src/Ingot.Edge.Agent/Configs)

仓库里已经带了一个本地联调示例：

- [TEST_PLC.json](../src/Ingot.Edge.Agent/Configs/TEST_PLC.json)

你也可以参考：

- [examples/device-configs](../examples/device-configs)
- [device-config.schema.json](../schemas/device-config.schema.json)

## 第四步：离线校验配置

先确认配置本身是合法的：

```bash
dotnet run --project src/Ingot.Edge.Agent -- --validate-configs
```

如果你要校验其他目录：

```bash
dotnet run --project src/Ingot.Edge.Agent -- --validate-configs --config-dir ./examples/device-configs
```

校验通过后，你应该能看到类似输出：

```text
[OK] .../TEST_PLC.json (TEST_PLC)
```

## 第五步：启动 Edge Agent

```bash
dotnet run --project src/Ingot.Edge.Agent
```

默认端口来自 [appsettings.json](../src/Ingot.Edge.Agent/appsettings.json) 中的 `Urls`，默认是：

- `http://localhost:8001`

启动后常用端点：

- `/health`
- `/metrics`
- `/api/logs`
- `/api/acquisition/plc-connections`
- `/api/v1/events`
- `/api/v1/events/stream`

## 可选：启动 PLC 模拟器

如果你想在本地做闭环联调，可以启动模拟器：

```bash
dotnet run --project src/Ingot.Simulator
```

模拟器默认监听 `502` 端口，并输出当前寄存器变化。详细说明见：

- [src/Ingot.Simulator/README.md](../src/Ingot.Simulator/README.md)

## 验证结果

你可以从这几个角度确认：

### 1. Agent 存活

```bash
curl http://localhost:8001/health
```

### 2. 配置被成功加载

Agent 启动日志里应能看到配置校验成功和 PLC/通道启动相关信息。

### 3. 本地诊断文件创建成功

默认运行目录下通常会出现：

- `Data/logs.db`
- `Data/acquisition-state.db`

说明：

- `logs.db` 用于本地日志查询
- 默认保留 30 天，可通过 `Logging:RetentionDays` 调整；设置为 `<= 0` 时关闭清理
- `acquisition-state.db` 用于条件采集的 active cycle 状态恢复

### 4. InfluxDB 有数据写入

如果 InfluxDB 可达，你应能在 bucket 中看到对应 measurement 的写入结果。

如果没有数据，请优先检查：

- Edge Agent 日志中的 TSDB 写入错误
- `/metrics` 中的错误指标
- `InfluxDB:Url`、`Bucket`、`Org`、`Token` 是否正确

## 可选：启动中心侧

中心侧不是采集主链路的前置条件。最简单的方式是一次启动 PostgreSQL、Central API 和 Central Web：

```bash
docker compose -f docker-compose.events.yml up -d --build
```

如果要本地开发 Web，先确保 PostgreSQL 和 Central API 正在运行，然后执行：

```bash
cd src/Ingot.Central.Web
npm install
npm run dev
```

打开 `http://localhost:3000/events` 查看跨 Edge 生产事件。Edge 与 Central 的 token 必须一致：`Edge:EventIngestToken` 对应 `EventIngest:EdgeTokens:{EdgeId}`。

## 常见问题

### 配置校验失败

优先检查：

- `Driver` 是否为完整稳定名称
- `ProtocolOptions` 是否包含当前驱动不支持的键
- `PlcCode` 是否在多个文件中重复

### InfluxDB 没有数据

这通常说明存储不可达，或者写入配置不正确。

优先检查：

- InfluxDB 服务是否启动
- `InfluxDB:Url` 是否正确
- `Bucket`、`Org`、`Token` 是否匹配
- Edge Agent 日志中是否出现写入错误

### 本地模拟器能连，现场 PLC 不能连

优先检查：

- `Host` 和 `Port`
- 现场网络连通性
- 驱动选择是否正确

## 相关文档

- [配置说明](tutorial-configuration.md)
- [驱动目录](hsl-drivers.md)
- [部署说明](tutorial-deployment.md)
