# 光学镜片模压模拟回放

这个工具产生一组明确标注为**模拟**的本地演示数据：8 个正常基线周期、8 个同配方但上模实际温度偏低的异常周期、以及 8 个带上模补偿的验证周期。

它使用平台的正式事件摄入与质检接口，以验证配方回读、过程采样、周期对比、质量关联和验证流程；它不是镜片量产数据，也不应被当成优化有效性的证据。

要验证采集端也遵守同一闭环契约，先启动 `device_simulator.py`，再发布
`provision_data_source.py` 创建的版本化数据源配置。该配置将运行号、实际配方、过程信号、阶段和周期边界一并交给现场节点；质检仍通过正式质量流程按运行号关联。

`submit_quality.py` 模拟检验站按同一运行号提交测量结果，故意不由设备采集端伪造质量数据。

```powershell
py -3 tools/optical-molding-demo/replay.py `
  --output .codex-temp/optical-molding-demo `
  --api http://127.0.0.1:8000 `
  --token development-device-simulator-token-0001
```
