# 品牌与标识

Ingot 的名称与标识共享同一个隐喻：**连接器提交的生产事件、检测事实与生产上下文是矿砂，Ingot 把它们熔炼成标准化、可信、可堆叠的生产事实。**

本文约定 Ingot 的品牌定位、命名资产和标识使用规则。

## 标识释义

标识由三块**锭**的截面（梯形）堆叠而成：

- 两块**钢色底锭**：已沉淀的事件日志——append-only，越垒越高的既成事实
- 顶部**金锭**：刚刚铸成的最新事实；锭面上的一道反光是出炉时的余温
- 三块锭之间保持**等距缝隙**：事件是离散铸成的单元，不是连续流

整体轮廓在 16px 下仍可辨认，可用于 favicon、终端徽标与 NuGet 包图标。

一句话 tagline：

> **Ingot — 把生产数据炼成事实。** Smelt production data into facts.

## 品牌定位

- **产品类别**：工厂数据与工艺分析平台 / Factory Data & Process Analytics Platform
- **主要用户**：制造现场的生产、工艺、质量、设备与 IT 角色，尤其是需要快速独立部署的中小制造企业
- **价值链**：连接器规格 → 受控源码与禁网构建/测试 → 人工打包批准 → 标准生产事件 + 检测事实 + 生产上下文 → 可信生产事实 → 周期核查与数据质量分析
- **系统边界**：MES 可以是可选数据源或集成目标，但不是运行前提；Ingot 不以排程、订单执行、库存或考勤定义自身

对外表达优先正面说明“接入标准生产事件、形成可信事实、基于证据分析”，不通过贬低其他工业数据平台或声称其缺少某项能力来建立差异。

## 命名资产

| 资产 | 值 |
|---|---|
| 产品名 | **Ingot**（域名不改变品牌称谓，避免漂移成 "IngotStack"） |
| 官方域名 | [ingotstack.com](https://ingotstack.com)（2026-07 注册；stack 同时呼应堆叠的锭与技术栈） |
| 仓库 | [github.com/liuweichaox/Ingot](https://github.com/liuweichaox/Ingot) |
| .NET 命名空间 | `Ingot.*` |

## 资产清单

| 文件 | 用途 |
|---|---|
| [`images/logo/ingot-lockup.svg`](../images/logo/ingot-lockup.svg) / [`ingot-lockup-dark.svg`](../images/logo/ingot-lockup-dark.svg) | README 明暗主题横排标识 |
| [`images/logo/ingot-mark.svg`](../images/logo/ingot-mark.svg) / [`ingot-mark-dark.svg`](../images/logo/ingot-mark-dark.svg) | 图标源文件（明/暗），一切导出以此为准 |
| [`images/logo/*.png`](../images/logo) | 各尺寸 PNG 导出（512 / 32 / 横排 1032） |
| [`images/logo/preview.html`](../images/logo/preview.html) | 品牌预览页：版式、尺寸、色板一览 |
| `site/public/brand/` / `docs-site/public/brand/` | 官网与文档站构建资产；校验测试保证内容一致 |

## 色板

| 色名 | 值 | 用途 |
|---|---|---|
| Molten Gold | `#FFC94A → #E8891A` | 金锭（最新事实）、强调色 |
| Cast Steel | `#33414F → #1F2933` | 底锭与正文字色（浅色底） |
| Steel on Dark | `#5B6E80 → #3C4A58` | 深色底上的底锭 |
| Coal | `#10161C` | 深色背景 |
| Fog | `#EDF1F5` | 深色底上的文字 |

## 使用规则

- 浅色底使用 `ingot-mark.svg`，深色底使用 `ingot-mark-dark.svg`；README 等支持处用 `<picture>` 自适应
- 最小展示尺寸 16px；四周留白不小于单块锭高度的一半
- 不改变三块锭的比例、位置关系与配色；不额外添加描边、阴影或倾斜
- 字标字体为 `Inter` / `Segoe UI` Bold 回退族，字色浅色底 `#1F2933`、深色底 `#EDF1F5`

## 相关文档

- [生产事件规范](rfc-production-events.md)
- [文档首页](index.md)
