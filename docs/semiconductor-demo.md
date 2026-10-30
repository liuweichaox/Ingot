# 半导体晶圆刻蚀采集演示

## 目的

这个演示用一个独立的 Modbus TCP 模拟服务，展示采集器如何读取一段合成的晶圆刻蚀过程数据，并把过程样本、运行边界、配方版本和生产上下文关联起来。所有读数、配方值和结果均为演示数据，不代表任何设备型号、真实工艺窗口或生产建议。

## 启动

完整栈已经启动时，在仓库根目录执行：

```powershell
pwsh -NoProfile -File scripts/setup-semiconductor-demo.ps1 -SkipBuild
```

要由脚本重启模拟器并等待一条新的完整运行，同时验证运行边界和有效样本：

```powershell
pwsh -NoProfile -File scripts/setup-semiconductor-demo.ps1 -SkipBuild -WaitForBatch
```

脚本会启动 `semiconductor-etch-simulator` 和 `connector-host`，登录平台，并创建或复用：

- Modbus TCP 模拟端点 `semiconductor-etch-simulator:1502`，采集周期 500ms。
- 包含 19 个模拟过程项的数据模型，其中 18 个过程量和 1 个阶段值。
- 包含 20 个合成演示参数的配方。
- 一个晶圆刻蚀设备及其演示用工艺套件记录。
- 演示生产上下文 `WAFER-ETCH-DEMO-001` 和过程分析方案。

要手动从头观察一段模拟运行，可重启模拟服务：

```powershell
docker compose -f docker-compose.app.yml -f docker-compose.demo.local.yml --profile semiconductor-demo restart semiconductor-etch-simulator
```

模拟器先等待 10 秒，然后运行一个 300 秒批次：

1. 晶圆装载与抽气：0–25 秒。
2. 气体稳定与等离子体启动：25–55 秒。
3. 刻蚀阶段：55–240 秒。
4. 清洗、停机与卸载：240–300 秒。

这些阶段和持续时间只用于让演示曲线易于观察。批次结束后服务保持空闲；再次重启会重新开始。

## 验证

健康检查：

```powershell
Invoke-RestMethod http://127.0.0.1:9110/health
Invoke-RestMethod http://127.0.0.1:8003/health
```

平台工作台可从“配置与接入 → 采集配置”查看已发布任务，从“配置与接入 → 现场节点”查看采集器状态；“现场运行 → 运行记录”和“工艺洞察 → 工艺分析”可查看演示过程数据。`-WaitForBatch` 会记录当前事件游标、重启独立模拟器，并只接受重启后出现的同一运行开始与完成事件；随后确认运行边界完整且至少有一个信号含有效样本。脚本不会输出管理员密码或访问令牌。

## 配置边界

本演示只配置一个模拟端点和一个采集任务，用于说明连接、映射、探查和运行数据的关系。实际项目应按现场网络、通信接口、设备资产和逻辑对象的真实边界创建数据源与任务；系统不预设控制器数量、设备层级或工位结构。

演示数据模型里的压力、功率、气体流量、温度等字段只是合成信号。真实设备的驱动、点位地址、数据类型、单位和时间戳都需要部署方依据设备资料和现场验证配置。工艺窗口与安全边界应在机理知识与工艺控制中管理，不属于采集任务映射。不要把本演示当成真实晶圆制程证据或工艺配方。
