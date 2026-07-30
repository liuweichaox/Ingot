# Brand and Identity

The Ingot name and identity share one metaphor: **real cycles, inspection outcomes, and process knowledge are ore; Ingot smelts them into the next verifiable process decision.**

This document defines Ingot brand positioning, naming assets, and identity-use rules.

## Mark meaning

The mark stacks three **ingot** cross-sections:

- two **steel ingots** represent accumulated process data, experiments, and engineering knowledge;
- one **gold ingot** represents the latest R&D conclusion smelted from evidence;
- equal **gaps** keep every record and conclusion independently traceable and reviewable.

The silhouette remains recognizable at 16px and is suitable for favicons, terminal marks, and ecosystem icons.

Tagline:

> **Ingot — The next run, optimized.** 把下一炉，交给可验证的优化。

## Brand position

- **Category**: Open-source Process Optimization / 开源工艺优化系统
- **Primary users**: Process, quality, equipment, and R&D engineers developing new products, materials, and processes
- **Product architecture**: The campaign is the formal record; Platform owns business truth; Optimizer performs deterministic numerical computation
- **Value chain**: Define objectives and constraints → join real cycles and inspections → recommend the next experiment → engineer review and execution → update the model
- **System boundary**: Ingot assists engineers without bypassing safety constraints, approval duties, or equipment control systems; MES can be a source or integration target but is not a runtime prerequisite

Public wording leads with shorter R&D cycles, fewer experiments, traceable evidence, and explicit uncertainty. It does not use performance figures that have not been validated on real projects.

## Naming assets

| Asset | Value |
|---|---|
| Product name | **Ingot** (the domain does not alter the product name) |
| Official domain | [ingotstack.com](https://ingotstack.com) |
| Repository | [github.com/liuweichaox/Ingot](https://github.com/liuweichaox/Ingot) |
| .NET namespaces | `Ingot.*` |

## Asset inventory

`apps/website/public/brand/` is the canonical location for brand source files:

| File | Use |
|---|---|
| [`ingot-lockup.svg`](../apps/website/public/brand/ingot-lockup.svg) | Horizontal lockup for light backgrounds |
| [`ingot-lockup-dark.svg`](../apps/website/public/brand/ingot-lockup-dark.svg) | Horizontal lockup for dark backgrounds |
| [`ingot-mark-dark.svg`](../apps/website/public/brand/ingot-mark-dark.svg) | Mark source for dark backgrounds |

When light, bitmap, or docs-site exports are added, derive them from canonical SVG sources and register them here.

## Palette

| Name | Value | Use |
|---|---|---|
| Molten Gold | `#E8AD56` | Recommendations, actions, primary emphasis |
| Trajectory Cyan | `#5FD4C8` | Process, connections, implemented state |
| Deep Coal | `#07100E` | Main background |
| Process Panel | `#0E1D19` | Cards and data panels |
| Fog | `#EEF5F1` | Text on dark backgrounds |

## Use rules

- Current canonical assets are for dark backgrounds; do not create temporary light variants with filters.
- Minimum display size is 16px; maintain clear space of at least half an ingot height.
- Preserve the three-ingot proportions, positions, and palette; do not add outlines, shadows, or skew.
- The wordmark font is `Inter` / `Segoe UI` Bold fallback.
- Product claims must distinguish synthetic demonstrations, historical replay, and real online experiments.

## Related documents

- [System design](design.en.md)
- [Documentation home](index.en.md)
