# 公开数据离线验证

本目录把公开制造数据验证固化为可重复运行的基准。它验证数据检查、分类上下文隔离、历史池回放、基线比较和声明边界，不把公开数据结果外推为某个工厂的收益证明。

## 数据来源

- 数据集：FDM 3D Printing Dataset，DOI `10.17632/zd6td6svd6.2`
- 来源：https://data.mendeley.com/datasets/zd6td6svd6/2
- 许可：CC BY 4.0
- 仓库快照：原始 500 条实验中的 162 条完整 DOE 子集，覆盖封闭式打印机、PLA+/PETG、三种填充图案，以及层厚、填充率、速度的 3×3×3 网格。

具体引用、筛选和字段转换见 [NOTICE](NOTICE.md)。加载器在运行前校验固定 SHA-256、行数、样本身份和 DOE 网格；校验不通过时直接停止，不静默修补或插补结果。

材料和填充图案属于分类工艺上下文，每个上下文单独建模；它们不会被编码成具有虚假距离关系的连续控制量。层厚、填充率和速度才是本基准中的连续可控变量。

## 当前参考结果

[latest-results.json](latest-results.json) 是当前提交的完整参考快照，使用 6 个分类上下文、每个上下文 20 个固定种子、3 个初始观察和 12 次实验预算：

| 检查 | 当前结果 |
|---|---|
| 流程验证 | 通过，6/6 个上下文完成 |
| 分类上下文隔离 | 通过 |
| Optimizer 平均成功率 / 截断平均实验数 | 100% / 7.59 |
| 随机基线平均成功率 / 截断平均实验数 | 71.67% / 8.72 |
| 响应面基线平均成功率 / 截断平均实验数 | 87.5% / 8.07 |
| Optimizer 实验数优于随机 / 响应面的上下文数 | 4/6 / 4/6 |
| 严格减少实验次数结论 | **未证明**（`not-demonstrated`） |

“截断平均实验数”把预算内未成功的运行计为 `budget + 1`，避免只统计成功运行而产生幸存者偏差。聚合指标虽然更好，但并非每个上下文都同时优于两种基线，因此严格结论保持未证明。

## 运行

```bash
./scripts/benchmark-public-validation.sh
```

默认结果写入 `artifacts/public-validation.json`；也可以把输出路径作为第一个参数传入脚本。

快速检查一个场景：

```bash
uvx --from uv==0.11.32 uv run --project optimizer --locked \
  python tools/public-validation/benchmark.py --seeds 1 --max-scenarios 1
```

## 自动化分层

- 普通 PR/CI 通过 `optimizer/tests/test_public_validation.py` 校验数据快照、分类隔离、Schema、声明边界，并快速回放一个场景；它用于发现软件回归，不把随机性能小数锁成跨平台完全一致。
- `.github/workflows/performance.yml` 每周或手动运行完整 6×20 基准并上传 `public-validation.json`，用于观察算法与依赖升级后的性能变化。
- `latest-results.json` 是人工审核后提交的参考证据快照，不是每次 PR 自动改写的生成文件。

## 结果解释

输出中的 `workflow_validation` 应为 `passed`。`experiment_reduction_claim` 是独立结论：只有六个分类上下文分别都不低于两种基线的成功率，并分别以更少实验完成时才会通过。聚合平均值更好但部分上下文退化仍判定为未证明，基准失败不会被改写成成功叙事。

公开数据只能验证软件、方法比较和安全边界。工厂收益必须使用不出厂的本地历史回放与少量受控实验验证。

## 更新规则

只有数据来源、筛选规则、算法、依赖锁或判定策略发生有意变化时才更新参考快照：

1. 数据变化必须同步更新 `NOTICE.md`、许可说明、SHA-256、行数和 DOE 结构检查；不得用插补或合成结果替换原始公开结果而不披露。
2. 算法或依赖变化先运行完整基准，并保留所有上下文和失败结果。
3. 经审核后使用以下命令更新参考快照：

   ```bash
   ./scripts/benchmark-public-validation.sh tools/public-validation/latest-results.json
   ```

4. 如果 `workflow_validation` 或 `experiment_reduction_claim` 改变，在同一变更中同步更新中英文 README、文档首页、FAQ 和优化文档；结论变差同样必须更新。
5. 精确浮点分数不作为普通 PR 的唯一阻断条件；数据完整性、上下文隔离、允许的结论状态和安全声明边界必须阻断。
