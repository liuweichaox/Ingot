# 光学镜片模压模拟回放

这个工具产生一组明确标注为**模拟**的本地演示数据：8 个正常基线周期、8 个同配方但上模实际温度偏低的异常周期、以及 8 个带上模补偿的验证周期。

它使用平台的正式事件摄入与质检接口，以验证配方回读、过程采样、周期对比、质量关联和验证流程；它不是镜片量产数据，也不应被当成优化有效性的证据。

本目录中的 `optimizer-feature-set.json` 是光学模压派生特征的声明式演示配置，
`molding_sim.py` 是演示数字孪生。两者均不属于 `ingot_optimizer` 生产包；通用优化内核
不会识别“镜片”“上模”“温度”或 FX3U 等名称。

要验证采集端也遵守同一闭环契约，先启动 `device_simulator.py`。它不是 HTTP 数据源，
而是在 5551 端口模拟 FX3U-ENET-ADP 的 A-compatible MC 1E 二进制读寄存器协议。
`provision_ingestion_task.py` 发布的配置使用 `melsec-a1e` 和 D 寄存器选择器。连接真机时，
必须按实际 PLC 型号、寄存器表、数据类型、字节序和工程单位复核配置并重新完成设备探查；
只有真机与模拟器使用完全相同的设备契约时，才可仅替换 PLC 地址和 MC 端口。该配置将运行号、实际配方、过程信号、随样本采集的阶段号和周期边界
一并交给现场节点；质检仍通过正式质量流程按运行号关联。

`submit_quality.py` 模拟检验站按同一运行号提交测量结果，故意不由设备采集端伪造质量数据。

重建空数据库后，先发布完整演示主数据：

演示模具按真实组合关系拆成四个必装、各一件的组件：上模芯、上模架、下模芯和下模架；
组件身份、序列号、制造商、型号、材料、装配修订、设备安装和生产上下文分别留痕。
组件分类只有“模芯”和“模架”；“上/下”属于不可变配置版本中的装配位置，不重复充当资产分类。

```powershell
$env:INGOT_ADMIN_USERNAME = "admin"
$env:INGOT_ADMIN_PASSWORD = "<local-admin-password>"
uv run --project optimizer --locked python tools/optical-molding-demo/bootstrap_demo.py `
  --api http://127.0.0.1:8000 `
  --edge-id EDGE-FX3U-SIM-001 `
  --device-host 127.0.0.1 `
  --device-port 5551 `
  --scenario-version 2
```

当前模拟设备契约包含 14 个采集量：整数阶段号、上/下模红外温度、电流、电压和功率，
以及压力、光栅位置、伺服速度、真空度和伺服位置。阶段号随每条过程样本采集，数据模型
不再另外维护阶段清单。配方包含 12 个设定参数：HEAT、WORK、HOST
位置，上/下模设置温度，充氮气温度，预热保温延时，压力差上限，上/下模温度上限，
压力上限和 WORK 位设定压力。名称与单位分别建模，平台编码保持稳定。
设备上下文同时回读周期号、产品、模具、材料批次和工件号，避免实时周期出现无法关联工件的缺口。

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

向开启本地账户认证的平台回传质检结果时，`submit_quality.py` 从
`INGOT_ADMIN_USERNAME` 和 `INGOT_ADMIN_PASSWORD` 读取凭据，先获取短期会话，
再调用正式质检接口；不绕过平台权限边界。

```powershell
$env:INGOT_ADMIN_USERNAME = "admin"
$env:INGOT_ADMIN_PASSWORD = "<local-admin-password>"
uv run --project optimizer --locked python tools/optical-molding-demo/submit_quality.py `
  --api http://127.0.0.1:8000 `
  --machine-id OPTICAL-MOLD-SIM-01
```

不要凭页面猜测上下文是否已经串通。至少完成一个模拟周期后，运行端到端验收：

```powershell
uv run --project optimizer --locked python tools/optical-molding-demo/verify_context_chain.py `
  --api http://127.0.0.1:8000 `
  --scenario-version 2
```

这里显式创建工艺配置 v2，因为已发布版本不可修改；v2 补齐并冻结完整的运行上下文字段策略。

验收会读取已发布工艺配置、其引用的数据摄取任务以及最新完成的真实运行，逐项打印
字段来源、运行值和 `PASS/FAIL`。只有所有工艺配置上下文字段均有明确提供者、运行开始
快照中都有值且 `context_capture_status=resolved` 时，命令才以成功状态退出。

为研发资产页面准备明确标注为模拟的数据集、模型、机理、融合、项目知识和
数据质量报告：

```powershell
uv run --project optimizer --locked python tools/optical-molding-demo/provision_research_assets.py `
  --api http://127.0.0.1:8000 `
  --project-id <research-project-id>
```

脚本是幂等的，且只调用平台正式 API；它不直接写数据库，也不把模拟资产标记为
已验证的工艺证据。
