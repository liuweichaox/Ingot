# Brand and product language

> Status: **v1 normative baseline**. This file is the single source of truth for product positioning, core value, and public language. Do not redefine the core value unless the product direction actually changes.

## Core value

> **Move process R&D from decisions without data support to decisions supported by real data, so computers can genuinely help process engineers choose what to do next using the most effective computational methods for the problem.**

This is Ingot's stable starting point. Data acquisition, process diagnosis, experiment design, numerical optimization, mechanism fusion, and AI interaction serve it; none creates a new core value.

## Product position

- **Category**: Open-source Process Diagnosis & Optimization / 开源工艺追因与优化系统
- **Primary users**: process, quality, equipment, and R&D engineers developing new products, materials, and processes
- **Unit of work**: the conditions, trajectory, quality result, engineering judgment, and next experiment for a real run
- **Product responsibility**: organize trustworthy facts, compare runs, form testable candidates, help select the next experiment, and preserve the limits of every conclusion
- **Engineer responsibility**: frame the problem, review data and constraints, judge executability, approve experiments, and interpret field context
- **System boundary**: Ingot does not replace engineers or bypass safety constraints, approvals, or equipment control systems

Ingot is not built merely to collect more points or showcase one algorithm. It turns collected data into usable engineering evidence and then selects robust statistics, controlled comparison, experimental design, causal validation, machine learning, Bayesian optimization, physical models, or language models according to the problem.

## Public commitments

Public material may state that the system can:

- link actual production conditions, process trajectories, and inspection results;
- expose missingness, provenance, versions, and uncertainty;
- help engineers compare runs and narrow candidate causes;
- turn candidate causes into falsifiable experiments;
- recommend the next experiment within declared variables and safety boundaries;
- preserve validated conclusions as process knowledge with an explicit scope.

Without evidence from real projects, public material must not claim that:

- the system has automatically discovered a definitive root cause;
- it has already reduced experiments or development time by a stated percentage;
- a model recommendation is a field guarantee;
- one successful setting proves a complete operating region;
- results from one scenario transfer unconditionally to another.

Observational data can support candidate causes, stable associations, confounded associations, or insufficient-evidence judgments. Causal conclusions require engineering review and appropriate controls, repetitions, blocks, or interventions.

## Canonical language

| Use | Chinese | English |
|---|---|---|
| Product category | 开源工艺追因与优化系统 | Open-source Process Diagnosis & Optimization |
| Core value | 让真实数据帮助工艺工程师抉择 | Help process engineers make decisions with real data |
| Short tagline | 看清这次运行，优化下一次运行。 | Explain this run, optimize the next. |
| Data unit | 运行 / 过程执行 | run / process execution |
| Observational conclusion | 候选原因、稳定关联、混杂关联、证据不足 | candidate cause, stable association, confounded association, insufficient evidence |
| Experimental conclusion | 支持、否决、不确定、已验证原因 | supported, rejected, inconclusive, validated cause |
| Optimization result | 下一步实验建议、候选设置、已验证工艺操作域 | next-experiment recommendation, candidate setting, validated operating region |

Use *root cause* only when the validating evidence is stated. *AI process R&D* may describe the interaction model but does not replace the product category. Algorithm names belong in technical explanations, not in the product value itself.

*Industrial AGI*, *autonomous factory*, and *TCP/IP for manufacturing AI* are not current product categories or implemented capabilities. They may appear only as long-term ambitions with explicit validation gates and current limitations.

The short tagline is a communication shorthand for the core value, not a separate product definition.

## Mark meaning

The Ingot mark stacks three ingot cross-sections:

- the two steel ingots represent accumulated run data, experiment records, and engineering knowledge;
- the gold ingot represents the current judgment refined from evidence and still open to review;
- equal spacing keeps facts, analysis, and conclusions independently traceable.

## Naming assets

| Asset | Value |
|---|---|
| Product name | **Ingot** |
| Official domain | [ingotstack.com](https://ingotstack.com) |
| Repository | [github.com/liuweichaox/Ingot](https://github.com/liuweichaox/Ingot) |
| .NET namespace | `Ingot.*` |

The domain does not rename the product; do not use “IngotStack” as the product name.

## Assets and palette

`apps/website/public/brand/` is the canonical source directory:

| File | Use |
|---|---|
| [`ingot-lockup.svg`](../apps/website/public/brand/ingot-lockup.svg) | Horizontal lockup for light backgrounds |
| [`ingot-lockup-dark.svg`](../apps/website/public/brand/ingot-lockup-dark.svg) | Horizontal lockup for dark backgrounds |
| [`ingot-mark-dark.svg`](../apps/website/public/brand/ingot-mark-dark.svg) | Mark source for dark backgrounds |

| Color | Value | Use |
|---|---|---|
| Evidence Gold | `#E8AD56` | recommendations, actions, primary emphasis |
| Trajectory Cyan | `#5FD4C8` | process, connectivity, trustworthy state |
| Deep Coal | `#07100E` | primary background |
| Process Panel | `#0E1D19` | cards and data panels |
| Fog | `#EEF5F1` | text on dark backgrounds |

## Usage rules

- Minimum display size is 16px; clear space is at least half one ingot's height.
- Do not change the proportions, layout, or colors, and do not add outlines, shadows, or skew.
- The wordmark uses an `Inter` / `Segoe UI` Bold fallback stack.
- Public visuals center on equipment signals, stage trajectories, inspection records, and engineering decisions; avoid flames, molten material, heated containers, and mystical imagery.
- Product-effect language must distinguish simulation, historical replay, shadow recommendations, and real online experiments.
- README files, documentation, website metadata, and product introductions must follow this file's core value, category, and claim boundaries.

## Related documents

- [Documentation home](index.en.md)
- [System design](design.en.md)
- [Real-scenario validation](rollout.en.md)
- [Strategy and rolling roadmap](project-plan.en.md)
