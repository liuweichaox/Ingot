# 光学玻璃模压多协议模拟设备

该工具模拟一台采用 Siemens S7-1500 控制架构的光学玻璃精密模压机。同一份设备状态同时通过 HTTP、MQTT、OPC UA 和 Modbus TCP 暴露，Ingot 只需要发布新的采集任务版本即可切换数据来源，不需要修改工艺数据模型或采集程序。

## 模拟内容

- 10 分钟逻辑周期，1 秒一组数据，5 个控制器步序：预热、均热、模压、退火、冷却；
- 用户给出的 13 路传感器；
- 整数、布尔和字符串运行状态；
- 31 项已解析配方参数；
- 周期号、工件号、产品系列、配方版本和控制器步序；
- 四种协议读取的是同一个内存状态，不生成四份相互矛盾的数据。

默认每 1000 毫秒推进 1 个逻辑秒：每秒产生一组传感器值，一个 10 分钟周期按真实时间完成。自动化测试如需加速，可显式设置 `SIMULATOR_TICK_MS=100`；加速数据用于协议与采集验证，不应作为长期连续运行的数据源。

## 运行

```powershell
npm install
npm start
npm run smoke
```

服务端点：

| 协议 | 地址 |
|---|---|
| HTTP | `http://127.0.0.1:8101/api/v1/snapshot` |
| MQTT 3.1.1 | `mqtt://127.0.0.1:1883`，主题 `ingot/simulator/optical-molding/telemetry` |
| OPC UA | `opc.tcp://127.0.0.1:4840/UA/IngotOpticalMolding` |
| Modbus TCP | `127.0.0.1:1502`，Unit ID `1` |

## 注册与切换

首次命令会注册光学模压工艺数据模型 v2、配方版本、分析方案，并发布采集任务。以后每次执行都会发布同一任务的新版本，平台自动停用旧版本，边缘节点无需重启。

```powershell
node register-platform.mjs --protocol=http-polling
node register-platform.mjs --protocol=mqtt
node register-platform.mjs --protocol=opc-ua
node register-platform.mjs --protocol=modbus-tcp
```

可用 `--api=http://平台地址:端口` 和 `--edge=采集节点编号` 改变目标。连接、点位、上下文、配方、周期边界、比例换算和寄存器字节序都保存在采集任务版本中。

## Modbus 寄存器表

所有地址为零基地址，大端字节、高字在前。

| 地址 | 内容 | 类型 |
|---|---|---|
| 0–25 | 13 路传感器，每项 2 个寄存器 | Float32 |
| 40 | 周期已运行秒数 | UInt16 |
| 41 | 加热输出 | UInt16 / Boolean |
| 42 | 报警代码 | UInt16 |
| 50–57 | 运行模式 | UTF-8 String(16) |
| 100–111 | 周期号 | UTF-8 String(24) |
| 112 | 配方步序 | UInt16 |
| 113–116 | 步序名称 | UTF-8 String(8) |
| 120–127 | 配方编号 | UTF-8 String(16) |
| 128 | 配方版本 | UInt16 |
| 129–144 | 配方名称 | UTF-8 String(32) |
| 145–148 | 源时间戳（Unix 毫秒） | UInt64 |
| 160–283 | 31 项配方参数，每项 4 个寄存器 | Float64 |

## 开源依赖

- Aedes 1.1.1，MIT：MQTT Broker；
- MQTT.js 5.15.2，MIT：模拟器发布客户端；
- node-opcua 2.175.2，MIT：OPC UA Server；
- modbus-serial 8.0.25，ISC：Modbus TCP Server。

依赖版本锁定在 `package-lock.json`，`npm install` 审计结果必须为零个已知漏洞。
