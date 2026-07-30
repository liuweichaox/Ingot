# 品牌与标识

Ingot 的名称与标识共享同一个隐喻：**真实周期、检验结果和工艺知识是矿砂，Ingot 把它们熔炼成下一次可验证的工艺决策。**

本文约定 Ingot 的品牌定位、命名资产和标识使用规则。

## 标识释义

标识由三块**锭**的截面（梯形）堆叠而成：

- 两块**钢色底锭**：持续沉淀的过程数据、实验记录和工程知识；
- 顶部**金锭**：由证据熔炼出的最新研发结论；
- 三块锭之间保持**等距缝隙**：每项记录和结论都有独立来源，可以追溯和复核。

整体轮廓在 16px 下仍可辨认，可用于 favicon、终端徽标与生态图标。

一句话 tagline：

> **Ingot — 把下一炉，交给可验证的优化。** The next run, optimized.

## 品牌定位

- **产品类别**：开源工艺优化系统 / Open-source Process Optimization
- **主要用户**：负责新产品、新材料和新工艺开发的工艺、质量、设备和研发工程师
- **产品架构**：研发项目是正式记录主线；Platform 保存业务事实；Optimizer 负责确定性数值计算
- **价值链**：定义目标与约束 → 关联真实周期和检验 → 推荐下一步实验 → 工程师审核与执行 → 更新模型
- **系统边界**：Ingot 辅助工程师决策，不绕过安全约束、审批职责或设备控制系统；MES 可以是数据源或集成目标，但不是运行前提

对外表达优先说明“缩短工艺研发周期、减少试验次数、保留证据与不确定性”，不使用未经真实项目验证的效果数字。

## 命名资产

| 资产 | 值 |
|---|---|
| 产品名 | **Ingot**（域名不改变品牌称谓，避免漂移成 “IngotStack”） |
| 官方域名 | [ingotstack.com](https://ingotstack.com) |
| 仓库 | [github.com/liuweichaox/Ingot](https://github.com/liuweichaox/Ingot) |
| .NET 命名空间 | `Ingot.*` |

## 资产清单

官网目录 `apps/website/public/brand/` 是品牌源文件的规范位置：

| 文件 | 用途 |
|---|---|
| [`ingot-lockup.svg`](../apps/website/public/brand/ingot-lockup.svg) | 浅色背景横排标识 |
| [`ingot-lockup-dark.svg`](../apps/website/public/brand/ingot-lockup-dark.svg) | 深色背景横排标识 |
| [`ingot-mark-dark.svg`](../apps/website/public/brand/ingot-mark-dark.svg) | 深色背景图标源文件 |

新增浅色、位图或文档站导出时，应从同一目录的规范 SVG 派生，并在本清单中登记。

## 色板

| 色名 | 值 | 用途 |
|---|---|---|
| Molten Gold | `#E8AD56` | 推荐、行动和主要强调 |
| Trajectory Cyan | `#5FD4C8` | 过程、连接与已实现状态 |
| Deep Coal | `#07100E` | 主背景 |
| Process Panel | `#0E1D19` | 卡片与数据面板 |
| Fog | `#EEF5F1` | 深色背景文字 |

## 使用规则

- 当前规范资产用于深色背景；新增浅色版本前不要通过滤镜临时改色。
- 最小展示尺寸 16px；四周留白不小于单块锭高度的一半。
- 不改变三块锭的比例、位置关系与配色；不额外添加描边、阴影或倾斜。
- 字标字体为 `Inter` / `Segoe UI` Bold 回退族。
- 产品效果表述必须区分合成演示、历史回放和真实在线实验。

## 相关文档

- [系统设计](design.md)
- [文档首页](index.md)
