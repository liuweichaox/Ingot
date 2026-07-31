# 光学镜片模压模拟回放

这个工具产生一组明确标注为**模拟**的本地演示数据：8 个正常基线周期、8 个同配方但上模实际温度偏低的异常周期、以及 8 个带上模补偿的验证周期。

它使用平台的正式事件摄入与质检接口，以验证配方回读、过程采样、周期对比、质量关联和验证流程；它不是镜片量产数据，也不应被当成优化有效性的证据。

本目录中的 `optimizer-feature-set.json` 是光学模压派生特征的声明式演示配置，
`molding_sim.py` 是演示数字孪生。两者均不属于 `ingot_optimizer` 生产包；通用优化内核
不会识别“镜片”“上模”“温度”或 FX3U 等名称。

要验证采集端也遵守同一闭环契约，先启动 `device_simulator.py`。它不是 HTTP 数据源，
而是在 5551 端口模拟 FX3U-ENET-ADP 的 A-compatible MC 1E 二进制读寄存器协议。
`provision_data_source.py` 发布的配置使用 `melsec-a1e` 和 D 寄存器选择器；换成真机时只改
PLC 地址和 MC 端口。该配置将运行号、实际配方、过程信号、随样本采集的阶段号和周期边界
一并交给现场节点；质检仍通过正式质量流程按运行号关联。

`submit_quality.py` 模拟检验站按同一运行号提交测量结果，故意不由设备采集端伪造质量数据。

重建空数据库后，先发布完整演示主数据：

```powershell
uv run --project optimizer --locked python tools/optical-molding-demo/bootstrap_demo.py `
  --api http://127.0.0.1:8000 `
  --edge-id EDGE-FX3U-SIM-001 `
  --device-host 127.0.0.1 `
  --device-port 5551
```

当前模拟设备契约包含 14 个采集量：整数阶段号、上/下模红外温度、电流、电压和功率，
以及压力、光栅位置、伺服速度、真空度和伺服位置。阶段号随每条过程样本采集，数据模型
不再另外维护阶段清单。配方包含 12 个设定参数：HEAT、WORK、HOST
位置，上/下模设置温度，充氮气温度，预热保温延时，压力差上限，上/下模温度上限，
压力上限和 WORK 位设定压力。名称与单位分别建模，平台编码保持稳定。

启动 FX3U 模拟设备：

```powershell
uv run --project optimizer --locked python tools/optical-molding-demo/device_simulator.py `
  --host 127.0.0.1 `
  --port 5551
```

```powershell
uv run --project optimizer --locked python tools/optical-molding-demo/replay.py `
  --output .codex-temp/optical-molding-demo `
  --api http://127.0.0.1:8000 `
  --token development-device-simulator-token-0001
```
