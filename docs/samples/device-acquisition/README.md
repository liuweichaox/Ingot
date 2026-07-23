# 本地设备服务器与采集样例

该样例运行一台连续热处理炉设备服务器，并由 Ingot Edge 每秒主动采集一次。设备原始字段、稳定代码、数据类型和上下文映射均由配置定义，采集实现不包含热处理行业的固定字段。

## 模拟数据

- 设备：`FURNACE-001`，连续运行、没有固定加工周期；
- 配方：两套可切换配方，参数同时覆盖 `double`、`integer`、`boolean`、`string`；
- 传感器：炉温、设定温度、炉压、氧含量、风机转速、加热器状态、运行模式；
- 输出事件：配方首次出现或变化时写入 `recipe.applied`，每次原子采样写入 `process.sample`。

## 运行

终端 1 启动设备服务器：

```powershell
node docs/samples/device-acquisition/device-server.mjs
```

终端 2 启动启用采集的 Edge：

```powershell
$env:DOTNET_ENVIRONMENT='DeviceSimulator'
dotnet run --project src/edge/Ingot.Edge.ConnectorHost/Ingot.Edge.ConnectorHost.csproj --no-build
```

Edge 开始上行前，先将工艺数据模型、两套配方和时间窗口分析方案发布到本地 Platform：

```powershell
node docs/samples/device-acquisition/register-platform.mjs
```

查看设备快照、采集状态和 Edge 已落盘事件：

```powershell
Invoke-RestMethod http://127.0.0.1:8100/api/v1/snapshot
Invoke-RestMethod http://127.0.0.1:8001/api/v1/acquisition/status
Invoke-RestMethod 'http://127.0.0.1:8001/api/v1/events?subjectId=FURNACE-001&limit=20'
```

切换配方后，Edge 会自动产生新的 `recipe.applied`，不需要重启：

```powershell
Invoke-RestMethod -Method Put -ContentType 'application/json' `
  -Body '{"recipeId":"HT-SHAFT-900"}' `
  http://127.0.0.1:8100/api/v1/active-recipe
```

`DeviceSimulator` 环境内置的 Edge 令牌仅用于本机开发，不是页面用户或访问密码。生产部署时必须通过安全配置注入独立的可轮换机器令牌。平台采集任务现在可配置 HTTP 轮询、MQTT、OPC UA 和 Modbus TCP，并统一输出标准事件；协议密码只保存边缘环境变量引用。平台上行仍由 Edge outbox 负责。
