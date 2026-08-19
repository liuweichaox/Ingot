# ingot-import：历史数据导入

把既有系统导出的 CSV 按映射文件转换为标准 `ProductionEvent` 批次写入 Platform，使**数据体检和周期分析不依赖任何实时采集**——拿历史导出表即可开始。

## 用法

```bash
# 1. 先本地校验（不联网）：校验完整文件，默认不打印真实值
dotnet run --project tools/Ingot.ImportCli -- \
  --file .ingot-import/history.csv \
  --mapping .ingot-import/mapping.json \
  --site-id SITE-FACTORY-001 \
  --dry-run

# 2. 正式导入
dotnet run --project tools/Ingot.ImportCli -- \
  --file .ingot-import/history.csv \
  --mapping .ingot-import/mapping.json \
  --site-id SITE-FACTORY-001 \
  --url http://localhost:8000 --token "$INGOT_EDGE_TOKEN"
```

要点：

- 平台侧需在 `EventIngest:EdgeTokens` 为映射中的 `edgeId`（如 `IMPORT-01`）配置令牌，并在 `EventIngest:EdgeSites` 把它绑定到命令中的 `--site-id`；
- `--seq-start` 缺省取启动时刻 unix 毫秒 ×1000，多次导入不同文件天然单调；**失败重跑用相同 `--seq-start`**，平台按 `eventId` 与 `(siteId, edgeId, seq)` 去重，重复行计入 `duplicates`，安全；
- 历史时间戳受 `EventIngest:MaxPastDays`（默认约 10 年）窗口约束；
- 缺失单元格不写入、不猜测（与生产事件规范一致）；
- `--dry-run` 会校验完整文件但默认不输出转换后的真实事件；只有在受控终端排查时才显式添加 `--show-values`，该选项最多预览 3 行；
- 映射和契约错误只报告行号、字段及期望格式，不回显出错的生产值；
- 事件来源默认使用不含原始文件名的 `historical-data`。确需区分导入批次时使用不泄露项目事实的 `--source-tag <opaque-tag>`；
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
