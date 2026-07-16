<a id="top"></a>

<div align="center">
  <h1 align="center">Ingot</h1>
  <p align="center">
    <strong>把生产数据炼成事实。</strong>
    <br />
    面向工业边缘的开源生产数据基础设施，从稳定采集起步，逐步将原始遥测标准化、事件化为可追溯的生产事实。
    <br />
    <a href="./docs/index.md"><strong>阅读项目文档 »</strong></a>
    <br />
    <br />
    <a href="https://github.com/liuweichaox/DataAcquisition">项目主页</a>
    ·
    <a href="https://github.com/liuweichaox/DataAcquisition/issues">反馈问题</a>
    ·
    <a href="https://github.com/liuweichaox/DataAcquisition/pulls">参与贡献</a>
  </p>
</div>

<div align="center">

[![.NET][dotnet-shield]][dotnet-url]
[![Vue][vue-shield]][vue-url]
[![InfluxDB][influxdb-shield]][influxdb-url]
[![Stars][stars-shield]][stars-url]
[![Issues][issues-shield]][issues-url]
[![License][license-shield]][license-url]

</div>

中文 | [English](README.en.md)

## 目录

- [关于项目](#关于项目)
- [技术栈](#技术栈)
- [快速开始](#快速开始)
- [运行与验证](#运行与验证)
- [架构概览](#架构概览)
- [仓库结构](#仓库结构)
- [文档导航](#文档导航)
- [路线图](#路线图)
- [贡献](#贡献)
- [许可证](#许可证)
- [致谢](#致谢)

## 关于项目

**Ingot**（原 DataAcquisition）是一个面向工业边缘场景的开源生产数据基础设施项目。当前版本用于在靠近设备侧的节点上完成 PLC 通信、配置化采集、批量写入时序数据库，以及运行状态诊断；后续将把来自 PLC、OPC UA、CNC、视觉和其他工业系统的数据，逐步标准化、事件化为不可变、可追溯的生产事实。

项目主产品是 `Edge Agent`。它负责读取 PLC 数据、组织采集任务、聚合批次并直接写入 TSDB。当前默认实现是 InfluxDB。`Central API / Central Web` 提供可选的中心化状态查看、指标浏览与日志代理能力，但不是采集主链路的前置依赖。

生产数据事件化的演进方案见 [生产事件 RFC](docs/rfc-production-events.md)。README 描述当前已经实现的能力，RFC 中的统一事件信封、上下文状态、不可变事件日志与中心事件库将按阶段落地。

> 更名说明：产品名和解决方案文件已经改为 **Ingot** 与 `Ingot.sln`。现有项目目录、程序集及命名空间暂时保留 `DataAcquisition.*`，将在后续全仓迁移中统一调整。

### 为什么叫 Ingot

**Ingot** 意为“锭”。原始遥测数据像矿砂——量大、分散、单位价值低；Ingot 将它熔炼为标准化、事件化的生产事实。

- **标准化**：锭由统一模具铸成，对应统一的生产事件模型
- **不可变**：锭一旦铸成便具有确定形态，对应不可变生产事件的设计方向
- **可堆叠**：事件可以持续积累、存储和汇聚，形成完整的生产记录
- **制造业语境**：锭是铸造、冶金和钢铁生产中的原生词汇，与工业现场天然相连

### 核心能力

- 从 PLC 稳定读取数据并生成统一的采集消息
- 通过明确的 JSON 配置管理设备、通道和采集模式
- 支持 `Always` 与 `Conditional` 两类采集模式
- 按批次直接写入 `TSDB`
- 提供配置校验、热更新与运行诊断能力
- 提供可选的中心化状态、指标与日志查看能力

### 系统边界

- `Edge Agent` 是核心运行组件，采集链路优先
- `Central API / Central Web` 是可选控制面，不是采集前提
- 项目当前定位是实时采集与可观测性，不提供本地 WAL、后台回放或补偿队列
- 当 TSDB 写入失败时，系统会记录错误并丢弃当前批次，不承诺本地持久化补偿
- 驱动通过稳定的 `Driver` 名称选择，并保留不同 PLC 协议的真实差异

### 控制面预览

| Edges | Metrics | Logs |
| --- | --- | --- |
| ![Edges](images/edges.png) | ![Metrics](images/metrics.png) | ![Logs](images/logs.png) |

### 主要使用场景

- 车间或产线侧 PLC 实时数据采集
- 多 PLC 的配置化接入与统一运维
- 直接写入 TSDB 的现场遥测链路
- 需要 Prometheus 指标、日志和中心化状态查看的边缘系统
- 需要在靠近设备侧部署轻量采集运行时的工业场景

<p align="right">(<a href="#top">回到顶部</a>)</p>

## 技术栈

- `.NET 10` / `ASP.NET Core`：Edge Agent 与 Central API 宿主
- `Vue 3` + `Vue Router` + `Element Plus`：Central Web
- `InfluxDB 2.x`：默认时序存储实现
- `SQLite`：本地日志与条件采集状态存储
- `HslCommunication`：默认 PLC 驱动实现基础
- `prometheus-net`：运行指标暴露
- `Serilog`：日志记录

<p align="right">(<a href="#top">回到顶部</a>)</p>

## 快速开始

### 前置要求

- `.NET 10 SDK`
- `InfluxDB 2.x`
- `Docker`，如果你想直接使用仓库中的 compose 文件启动 InfluxDB
- `pnpm`，如果你要运行中心侧 Web

### 本地启动

1. 克隆仓库

```bash
git clone https://github.com/liuweichaox/DataAcquisition.git
cd DataAcquisition
```

2. 构建解决方案

```bash
dotnet build Ingot.sln
```

3. 启动 InfluxDB

```bash
docker compose -f docker-compose.tsdb.yml up -d
```

说明：

- Edge Agent 默认连接配置位于 [src/DataAcquisition.Edge.Agent/appsettings.json](src/DataAcquisition.Edge.Agent/appsettings.json)
- 如果你使用自己的 InfluxDB，请确保 `InfluxDB:Url`、`Token`、`Bucket`、`Org` 与实际实例一致

4. 检查设备配置

- 示例配置：[src/DataAcquisition.Edge.Agent/Configs/TEST_PLC.json](src/DataAcquisition.Edge.Agent/Configs/TEST_PLC.json)
- 更多样例：[examples/device-configs](examples/device-configs)
- 配置 Schema：[schemas/device-config.schema.json](schemas/device-config.schema.json)

5. 离线校验配置

```bash
dotnet run --project src/DataAcquisition.Edge.Agent -- --validate-configs
```

如果你要校验其他目录：

```bash
dotnet run --project src/DataAcquisition.Edge.Agent -- --validate-configs --config-dir ./examples/device-configs
```

6. 启动 Edge Agent

```bash
dotnet run --project src/DataAcquisition.Edge.Agent
```

7. 可选：启动本地 PLC 模拟器

```bash
dotnet run --project src/DataAcquisition.Simulator
```

8. 可选：启动中心侧 API 与 Web

```bash
dotnet run --project src/DataAcquisition.Central.Api
cd src/DataAcquisition.Central.Web
pnpm install
pnpm run serve
```

默认访问地址：

- Edge Agent: `http://localhost:8001`
- Central API: `http://localhost:8000`
- Central Web: `http://localhost:3000`

<p align="right">(<a href="#top">回到顶部</a>)</p>

## 运行与验证

### 典型联调流程

如果你只是想在本地确认整条链路能跑通，推荐按这个顺序：

1. 启动 `InfluxDB`
2. 启动 `DataAcquisition.Simulator`
3. 校验 [TEST_PLC.json](src/DataAcquisition.Edge.Agent/Configs/TEST_PLC.json)
4. 启动 `DataAcquisition.Edge.Agent`
5. 可选启动 `DataAcquisition.Central.Api` 和 `DataAcquisition.Central.Web`
6. 通过健康检查、指标接口、日志和 UI 确认系统状态

### 常用端点

| 组件 | 地址 | 用途 |
| --- | --- | --- |
| Edge Agent | `http://localhost:8001/health` | 存活与健康检查 |
| Edge Agent | `http://localhost:8001/metrics` | Prometheus 指标 |
| Edge Agent | `http://localhost:8001/api/logs` | 本地日志查询 |
| Edge Agent | `http://localhost:8001/api/DataAcquisition/plc-connections` | PLC 连接状态 |
| Central API | `http://localhost:8000/metrics` | 中心指标 |
| Central Web | `http://localhost:3000` | 节点、指标与日志界面 |

### 本地数据目录

运行后优先关注这些目录和文件：

- `Data/logs.db`
- `Data/acquisition-state.db`

观察重点：

- `logs.db` 保存本地日志，适合排查 PLC 连接、配置加载和 TSDB 写入错误
- 默认自动保留 30 天日志，可通过 `Logging:RetentionDays` 调整；设置为 `<= 0` 时关闭清理
- `acquisition-state.db` 保存条件采集的 active cycle 状态，用于进程重启后的上下文恢复
- 如果 TSDB 没有收到数据，应优先查看 Edge Agent 日志和 `/metrics`，因为当前实现不会在本地累积待回放数据

<p align="right">(<a href="#top">回到顶部</a>)</p>

## 架构概览

### 主链路

```text
JSON Device Configs
        |
        v
+-------------------+       +----------------+
|    Edge Agent     | <---- | PLC / Device   |
| - load configs    |       +----------------+
| - collect data    |
| - batch messages  |
| - expose metrics  |
+---------+---------+
          |
          v
   +-------------+
   | Queue/Batch |
   +------+------+ 
          |
          v
   +-------------+
   |    TSDB     |
   +-------------+

Edge Agent
  |--> SQLite: acquisition-state.db
  |--> SQLite logs + /metrics
```

### 部署关系

```text
Browser
   |
   v
Central Web
   |
   v
Central API
   |
   |  (optional control plane)
   v
Edge Agent -----> PLC / Device
     |
     +---------> TSDB
```

### 怎么理解这张图

- `Edge Agent` 是系统核心，真正负责采集、批量写入和本地诊断
- `JSON Device Configs` 决定采什么、怎么连 PLC、用什么采集模式
- `Queue / Batch` 表示内存中的聚合与批量写入过程，不是本地持久化缓冲
- `TSDB` 是存储抽象，当前默认实现是 InfluxDB；数据成功写入以存储返回结果为准
- `SQLite acquisition-state.db` 只保存条件采集的上下文状态，便于进程重启后恢复周期语义
- `SQLite logs + /metrics` 用于排障和观测，不保存待补写原始数据
- `Central API / Central Web` 是可选控制面，方便看节点状态、日志和指标，但不是采集前提

### 失败语义

- TSDB 写入成功：当前批次完成
- TSDB 写入失败：记录日志和指标，然后丢弃当前批次
- 后续采集任务继续运行，不依赖后台回放或本地 WAL

### 设计重点

- `Edge First`
  Edge Agent 是采集主链路，不把中心侧当成前置依赖
- `Real-Time First`
  批次直接写入 TSDB；失败记录并丢弃，不做本地补偿回放
- `Configuration Before Runtime`
  设备配置先校验，再运行
- `Explicit Driver Contracts`
  通过稳定的 `Driver` 名称选择协议实现，并保留清晰扩展点
- `Observability First`
  用日志、指标和中心视图暴露运行状态，而不是隐藏失败
- `UTC`
  统一使用 UTC 时间语义，避免跨节点采集与展示歧义

如果你想继续看设计细节，建议直接阅读 [docs/design.md](docs/design.md) 和 [docs/modules.md](docs/modules.md)。

<p align="right">(<a href="#top">回到顶部</a>)</p>

## 仓库结构

- [src/DataAcquisition.Edge.Agent](src/DataAcquisition.Edge.Agent)
  边缘运行时宿主，负责启动采集主链路和本地诊断接口
- [src/DataAcquisition.Infrastructure](src/DataAcquisition.Infrastructure)
  PLC 驱动、采集编排、队列、InfluxDB、SQLite、日志和指标实现
- [src/DataAcquisition.Application](src/DataAcquisition.Application)
  抽象接口、应用服务与运行时契约
- [src/DataAcquisition.Domain](src/DataAcquisition.Domain)
  领域模型、配置模型和消息模型
- [src/DataAcquisition.Central.Api](src/DataAcquisition.Central.Api)
  中心侧注册、心跳、日志和指标代理 API
- [src/DataAcquisition.Central.Web](src/DataAcquisition.Central.Web)
  中心侧 Vue 控制面
- [src/DataAcquisition.Simulator](src/DataAcquisition.Simulator)
  本地 PLC 联调模拟器
- [tests/DataAcquisition.Core.Tests](tests/DataAcquisition.Core.Tests)
  核心测试项目

<p align="right">(<a href="#top">回到顶部</a>)</p>

## 文档导航

推荐阅读顺序：

1. [快速开始](docs/tutorial-getting-started.md)
2. [配置说明](docs/tutorial-configuration.md)
3. [驱动目录](docs/hsl-drivers.md)
4. [部署说明](docs/tutorial-deployment.md)

按主题继续深入：

- [设计说明](docs/design.md)
- [模块说明](docs/modules.md)
- [生产事件 RFC](docs/rfc-production-events.md)
- [开发扩展](docs/tutorial-development.md)
- [常见问题](docs/faq.md)
- [贡献指南](CONTRIBUTING.md)

<p align="right">(<a href="#top">回到顶部</a>)</p>

## 路线图

基于当前文档和实现，后续更值得持续投入的方向包括：

- [x] 确立 Ingot 产品名并将解决方案更名为 `Ingot.sln`
- [ ] 增加更多真实 PLC 的示例配置
- [ ] 补充更多端到端测试
- [ ] 继续完善主流驱动的 `ProtocolOptions`
- [ ] 强化故障排查与运维文档
- [ ] 完善中心侧观测与诊断体验

已知问题和功能讨论可在 [Issues](https://github.com/liuweichaox/DataAcquisition/issues) 中跟进。

<p align="right">(<a href="#top">回到顶部</a>)</p>

## 贡献

欢迎贡献驱动增强、采集链路可靠性修复、TSDB 写入改进、文档改进、示例配置和自动化测试。

提交前建议至少确认：

- 代码可以构建
- 相关测试通过
- README / 教程 / 示例配置已同步更新

详细约定见 [CONTRIBUTING.md](CONTRIBUTING.md)。

<p align="right">(<a href="#top">回到顶部</a>)</p>

## 许可证

本项目使用 [MIT License](LICENSE)。

<p align="right">(<a href="#top">回到顶部</a>)</p>

## 致谢

- [Best-README-Template](https://github.com/othneildrew/Best-README-Template)
- [HslCommunication](https://github.com/dathlin/HslCommunication)
- [InfluxDB](https://www.influxdata.com/)

<p align="right">(<a href="#top">回到顶部</a>)</p>

[dotnet-shield]: https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white
[dotnet-url]: https://dotnet.microsoft.com/
[vue-shield]: https://img.shields.io/badge/Vue-3-42B883?style=for-the-badge&logo=vuedotjs&logoColor=white
[vue-url]: https://vuejs.org/
[influxdb-shield]: https://img.shields.io/badge/InfluxDB-2.x-22ADF6?style=for-the-badge&logo=influxdb&logoColor=white
[influxdb-url]: https://www.influxdata.com/
[stars-shield]: https://img.shields.io/github/stars/liuweichaox/DataAcquisition.svg?style=for-the-badge
[stars-url]: https://github.com/liuweichaox/DataAcquisition/stargazers
[issues-shield]: https://img.shields.io/github/issues/liuweichaox/DataAcquisition.svg?style=for-the-badge
[issues-url]: https://github.com/liuweichaox/DataAcquisition/issues
[license-shield]: https://img.shields.io/github/license/liuweichaox/DataAcquisition.svg?style=for-the-badge
[license-url]: https://github.com/liuweichaox/DataAcquisition/blob/main/LICENSE
