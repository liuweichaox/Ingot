# Brand and Identity

The Ingot name and identity share one metaphor: **real cycles, inspection outcomes, and process knowledge are ore; Ingot smelts them into two things—an explanation of this run, and a recommendation for the next.**

This document defines Ingot brand positioning, naming assets, and identity-use rules.

## Mark meaning

The mark stacks three **ingot** cross-sections:

- two **steel ingots** represent accumulated process data, experiments, and engineering knowledge—the basis for diagnosis;
- one **gold ingot** represents the latest conclusion smelted from evidence: the attributed cause and the next recommended run;
- equal **gaps** keep every record and conclusion independently traceable and reviewable.

The silhouette remains recognizable at 16px and is suitable for favicons, terminal marks, and ecosystem icons.

Tagline:

> **Ingot — Explain this run, optimize the next.** 看清这次运行，优化下一次运行。

## Brand position

- **Category**: Open-source Process Diagnosis & Optimization / 开源工艺追因与优化系统
- **One-line definition**: Ingot is an open-source process diagnosis and optimization system for expensive, small-data manufacturing experiments; it links real cycles, realized trajectories, and inspection outcomes into traceable evidence, explains why a run missed specification, and recommends what to set next
- **Two core questions**: why did this run miss specification, and which variable or trajectory segment caused it; what settings should the next run use to reach specification in as few experiments as possible
- **Primary users**: Process, quality, equipment, and R&D engineers developing new products, materials, and processes
- **Product architecture**: The campaign is the formal record; Platform owns business truth; Optimizer performs deterministic numerical computation
- **Value chain**: Define objectives and constraints → join real cycles and inspections → diagnose deviation sources and candidate variables → recommend the next experiment → engineer review and execution → update the model
- **System boundary**: Ingot assists engineers without bypassing safety constraints, approval duties, or equipment control systems; MES can be a source or integration target but is not a runtime prerequisite

Diagnosis and optimization are two uses of one evidence chain, not two products: diagnosis explains the deviation, optimization proposes the next action, and both read the same real cycles, actual recipes, versioned process features, and inspection outcomes.

Public wording leads with shorter R&D cycles, fewer experiments, traceable evidence, and explicit uncertainty. It does not use performance figures that have not been validated on real projects.

## Naming assets

| Asset | Value |
|---|---|
| Product name | **Ingot** (the domain does not alter the product name) |
| Category line (EN) | Open-source Process Diagnosis & Optimization |
| Category line (ZH) | 开源工艺追因与优化系统 |
| Tagline | Explain this run, optimize the next. / 看清这次运行，优化下一次运行。 |
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
- This document is the single source of truth for the category line and tagline; README, docs site, and website metadata sync from here rather than rewording independently.
- Terminology: 追因 is rendered as *diagnosis* in the category line, tagline, and metadata. *Cycle diagnosis* and *deviation attribution* remain valid names for the specific features, but do not replace the category term. *Root-cause attribution* stays available inside technical documents such as the architecture loop section.

## Related documents

- [System design](design.en.md)
- [Documentation home](index.en.md)
