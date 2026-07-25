# FX3U MC 1E 模拟与采集

Ingot 直接支持 FX3U-ENET-ADP 的 A-compatible MC 1E 二进制读取。模拟 PLC、现场探针和边缘采集器使用同一帧格式。

## 本地模拟

启动模拟 PLC：

```powershell
py -3 tools/mc1e/fx3u_simulator.py --host 0.0.0.0 --port 5551
```

模拟器持续更新 D100～D107：

- D100：温度
- D101：压力
- D102：真空度
- D103：速度
- D104：周期号
- D105：工艺步骤
- D106：读取次数
- D107：运行状态

验证读取：

```powershell
py -3 tools/mc1e/mc1e_probe.py --host 127.0.0.1 --port 5551 --device D100 --count 8
```

启动已构建的边缘采集节点：

```powershell
pwsh scripts/run-fx3u-connector.ps1
```

平台中发布到 `EDGE-FX3U-SIM-001` 的 MELSEC 1E 采集任务会被自动领取。采样经边缘事件日志可靠上送，出现在运行对象、生产事件和运行记录中。

## 连接现场 FX3U

FX3U-ENET-ADP 需要在 GX Works2 中启用 TCP 的 MC Protocol，并选择二进制通信数据。采集任务填写 PLC 地址、现场配置的 MC 端口和寄存器映射即可。

选择器格式为 `软元件:地址:类型`：

```text
D:100:int16
D:200:float32
```

支持 `int16`、`uint16`、`int32`、`uint32`、`float32`、`int64`、`uint64` 和 `float64`。多字值按低字在前解码，`scale` 与 `offset` 用于还原 PLC 内的工程量。

探针可直接检查现场网络、端口、二进制通信设置和寄存器内容：

```powershell
py -3 tools/mc1e/mc1e_probe.py --host 192.168.1.10 --port 5551 --device D100 --count 4
```

## 实现位置

- `fx3u_simulator.py`：可长期运行的 FX3U MC 1E 模拟端。
- `mc1e_probe.py`：现场连接和寄存器读取探针。
- `MelsecA1EAcquisitionRunner.cs`：边缘采集器，复用统一映射、生命周期、事件日志和上送管道。
