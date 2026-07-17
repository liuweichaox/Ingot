# 品牌与标识

Ingot 的名称与标识共享同一个隐喻：**遥测是矿砂，Ingot 把它熔炼成标准化、不可变、可堆叠的生产事实。**

命名释义的完整版本见 [生产事件 RFC §12](rfc-production-events.md)，README 的「为什么叫 Ingot」是其摘要。本文只约定标识本身。

## 标识释义

标识由三块**锭**的截面（梯形）堆叠而成：

- 两块**钢色底锭**：已沉淀的事件日志——append-only，越垒越高的既成事实
- 顶部**金锭**：刚刚铸成的最新事实；锭面上的一道反光是出炉时的余温
- 三块锭之间保持**等距缝隙**：事件是离散铸成的单元，不是连续流

整体轮廓在 16px 下仍可辨认，可用于 favicon、终端徽标与 NuGet 包图标。

一句话 tagline：

> **Ingot — 把生产数据炼成事实。** Smelt production data into facts.

## 资产清单

| 文件 | 用途 |
|---|---|
| [`docs/assets/ingot-logo.png`](assets/ingot-logo.png) | README 浅色模式展示（透明底） |
| [`docs/assets/ingot-logo-dark.png`](assets/ingot-logo-dark.png) | README 深色模式展示（透明底，配合 `<picture>`） |
| [`images/logo/ingot-mark.svg`](../images/logo/ingot-mark.svg) / [`ingot-mark-dark.svg`](../images/logo/ingot-mark-dark.svg) | 图标源文件（明/暗），一切导出以此为准 |
| [`images/logo/ingot-lockup.svg`](../images/logo/ingot-lockup.svg) / [`ingot-lockup-dark.svg`](../images/logo/ingot-lockup-dark.svg) | 横排锁定版式（图标 + 字标） |
| [`images/logo/*.png`](../images/logo) | 各尺寸 PNG 导出（512 / 32 / 横排 1032） |
| [`images/logo/preview.html`](../images/logo/preview.html) | 品牌预览页：版式、尺寸、色板一览 |

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

- [生产事件 RFC](rfc-production-events.md)
- [文档首页](index.md)
