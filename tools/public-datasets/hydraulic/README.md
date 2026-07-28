# 公开液压数据集验收样本

这个工具将 [UCI Condition Monitoring of Hydraulic Systems](https://archive.ics.uci.edu/dataset/447/condition+monitoring+of+hydraulic+systems) 的公开周期数据转为 Ingot 的历史导入格式。来源数据采用 CC BY 4.0；压缩包不进入仓库。

它验证的是产品链路，而不是宣称液压故障数据能证明镜片模压的优化效果：

1. 历史数据经标准事件契约导入，而不是直接写入数据库；
2. 一个周期的开始、60 个过程采样、结束共享同一稳定关联标识；
3. 工艺数据模型和分析方案可以把不同协议来源的信号转为可比特征；
4. 独立质量记录与周期关联，从而支持质量分组和周期对比。

`condition_score` 是根据 UCI 已标注的系统状态生成的透明测试目标，**不是**来源数据中的实测产品质量，也不能用于评价贝叶斯优化的效果。寻优有效性必须用拥有真实配方干预和质量结果的研发历史回放来验证。

准备派生文件：

```powershell
python tools/public-datasets/hydraulic/prepare_benchmark.py `
  --source-zip $env:TEMP/condition-monitoring-hydraulic.zip `
  --output $env:TEMP/ingot-hydraulic-benchmark
```

派生目录会包含三个可由 `tools/Ingot.ImportCli` 导入的 CSV、三个映射文件、检测结果 CSV 与可审计 `manifest.json`。导入前必须先在 Platform 中创建同名的工艺数据模型、配方、分析方案、检测定义和质量方案；本次验收运行会实际走这条路径。
