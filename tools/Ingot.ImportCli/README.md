# ingot-import：历史数据导入

把既有系统导出的 CSV 按映射文件转换为标准 `ProductionEvent` 批次写入 Platform，使**数据体检和周期分析不依赖任何实时采集**——拿历史导出表即可开始。

## 用法

```bash
# 1. 先本地校验（不联网）：打印前 3 行转换结果并跑完整契约校验
dotnet run --project tools/Ingot.ImportCli -- \
  --file history.csv --mapping mapping.json --dry-run

# 2. 正式导入
dotnet run --project tools/Ingot.ImportCli -- \
  --file history.csv --mapping mapping.json \
  --url http://localhost:8000 --token "$INGOT_EDGE_TOKEN"
```

要点：

- 平台侧需在 `EventIngest:EdgeTokens` 为映射中的 `edgeId`（如 `IMPORT-01`）配置令牌；
- `--seq-start` 缺省取启动时刻 unix 毫秒 ×1000，多次导入不同文件天然单调；**失败重跑用相同 `--seq-start`**，平台按 `eventId` 与 `(edgeId, seq)` 去重，重复行计入 `duplicates`，安全；
- 历史时间戳受 `EventIngest:MaxPastDays`（默认约 10 年）窗口约束；
- 缺失单元格不写入、不猜测（与生产事件规范一致）；
- 映射文件字段见 `sample-mapping.json`：每个字段取列（`column`）或常量（`value`）；时间戳可指定 `format` 与 `utcOffset`；`values` 数据项类型为 `number|integer|boolean|string`。

## 周期边界

若 CSV 只有过程采样、没有周期开始/结束事件，可分两次导入：先用一份按周期聚合的 CSV（每周期一行，`eventType` 取 `process.execution.started`/`process.execution.completed` 两列拆分或分成两个文件），再导入采样行；同一 `executionId` 会自动关联。

## 数据与仓库边界

生产导出文件和现场映射文件可能泄露项目、设备、工艺参数、质量结果和数据结构，必须视为部署方的受控资料：

- 原始 CSV、现场映射、导入日志、命令输出和真实结果截图不得提交到公开仓库；
- 建议把本地导入资料放在仓库根目录的 `.ingot-import/` 中，该目录默认被 Git 忽略；
- 仓库中的 `sample-mapping.json` 是虚构示例，只用于说明映射格式；
- 缺陷复现和自动化测试必须使用人工生成或充分合成的数据，不得从真实生产数据抽行后直接提交；
- 即使删除名称，参数范围、时间分布、字段组合和结果比例仍可能识别生产场景，不能把简单改名当作脱敏。
