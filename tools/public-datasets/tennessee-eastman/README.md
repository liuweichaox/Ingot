# Tennessee Eastman 架构回放

将一个 Tennessee Eastman 模拟故障轨迹转为 Ingot 的统一运行契约，用来验证：操纵变量轨迹、过程时序、已知故障注入边界、质量结果和周期对比能否在同一闭环中工作。

默认选择故障前 4 个一小时窗口和故障后 4 个一小时窗口。`PASS`/`FAIL` 由公开基准的已知注入时点派生，仅用于验证证据链；它不是检测到的真实质量，也不可以作为优化器有效性的证明。

```powershell
python tools/public-datasets/tennessee-eastman/prepare_benchmark.py `
  --source mode1_10_1.xlsx --fault-code 10 `
  --output $env:TEMP/ingot-tennessee-eastman-derived
```

输入文件必须有 `Time`、`XMEAS-1/7/9/12` 与 `XMV-3/4/10` 列。数据模型把 `XMEAS` 作为过程测量、`XMV` 作为控制变量轨迹；平台不能将故障代码当作真实根因，而应输出待验证候选原因。
