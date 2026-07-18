# Brand and Identity

The Ingot name and identity share one metaphor: **production events, inspection facts, and production context submitted by source adaptation are ore; Ingot smelts them into standardized, trustworthy, stackable production facts.**

This document defines Ingot brand positioning, naming assets, and identity-use rules.

## Mark meaning

The mark stacks three **ingot** cross-sections (trapezoids):

- two **steel ingots**: accumulated event history—append-only facts that build over time;
- one **gold ingot**: the newest fact, with a reflective line for its recency;
- equal **gaps** between ingots: events are discrete facts, not a continuous stream.

The silhouette remains recognizable at 16px and is suitable for favicons, terminal marks, and ecosystem icons.

Tagline:

> **Ingot — Smelt production data into facts.** 把生产数据炼成事实。

## Brand position

- **Category**: Trusted Production Facts & Problem-Finding Platform / 可信生产事实与生产问题查询平台
- **Primary users**: Production, process, quality, equipment, and IT roles in manufacturing, especially teams that need independent deployment
- **Value chain**: Team-owned source adaptation → standard production events + inspection facts + production context → trusted production facts → Ingot Chat cycle review and data-quality queries
- **System boundary**: MES can be a source or integration target, but is not a runtime prerequisite; Ingot is not defined by scheduling, order execution, inventory, or attendance

Public wording leads with standard production-event ingestion, trusted facts, and evidence-based investigation.

## Naming assets

| Asset | Value |
|---|---|
| Product name | **Ingot** (the domain does not alter the product name) |
| Official domain | [ingotstack.com](https://ingotstack.com) |
| Repository | [github.com/liuweichaox/Ingot](https://github.com/liuweichaox/Ingot) |
| .NET namespaces | `Ingot.*` |

## Asset inventory

| File | Use |
|---|---|
| [`images/logo/ingot-lockup.svg`](../images/logo/ingot-lockup.svg) / [`ingot-lockup-dark.svg`](../images/logo/ingot-lockup-dark.svg) | README horizontal lockup for light and dark themes |
| [`images/logo/ingot-mark.svg`](../images/logo/ingot-mark.svg) / [`ingot-mark-dark.svg`](../images/logo/ingot-mark-dark.svg) | Source icon files; all exports derive from these |
| [`images/logo/*.png`](../images/logo) | PNG exports (512 / 32 / horizontal 1032) |
| [`images/logo/preview.html`](../images/logo/preview.html) | Brand preview: layout, scale, and palette |
| `site/public/brand/` / `docs-site/public/brand/` | Website and docs-site build assets; tests keep them identical |

## Palette

| Name | Value | Use |
|---|---|---|
| Molten Gold | `#FFC94A → #E8891A` | Gold ingot, latest fact, emphasis |
| Cast Steel | `#33414F → #1F2933` | Base ingots and body text on light backgrounds |
| Steel on Dark | `#5B6E80 → #3C4A58` | Base ingots on dark backgrounds |
| Coal | `#10161C` | Dark background |
| Fog | `#EDF1F5` | Text on dark backgrounds |

## Use rules

- Use `ingot-mark.svg` on light backgrounds and `ingot-mark-dark.svg` on dark backgrounds; use `<picture>` where theme adaptation is available.
- Minimum display size is 16px; maintain clear space of at least half an ingot height.
- Preserve the three-ingot proportions, positions, and palette; do not add outlines, shadows, or skew.
- The wordmark font is `Inter` / `Segoe UI` Bold fallback; use `#1F2933` on light and `#EDF1F5` on dark backgrounds.

## Related documents

- [Production event specification](rfc-production-events.en.md)
- [Documentation home](index.en.md)
