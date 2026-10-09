# Brand guide

> Status: **v1 normative baseline**. This file is the single source of truth for product positioning, core value, and public language. Do not redefine the core value unless the product direction actually changes.

## Core value

> **Organize recipe versions, experiment records, and run evidence to support quality analysis, process diagnosis, and recipe optimization.**

Ingot serves process, quality, equipment, and R&D engineers across recipe versions, experiment records, run evidence, quality analysis, process diagnosis, and recipe optimization.

For product communication, prefer the more concrete action line:

> **From process data to evidence-based R&D decisions.**

The product summary covers R&D management, data and evidence, analysis, and optimization. Specific workflows depend on data-admission conditions; see [Current status](status.en.md) and the [Roadmap](project-plan.en.md) for implemented and planned capabilities.

## Product position

- **Category**: Open-source Process R&D and Optimization System / 开源工艺研发与优化系统
- **Primary users**: process, quality, equipment, and R&D engineers developing new products, materials, and processes
- **Unit of work**: process-experiment definition, actual settings, trajectory, result, engineering judgment, and follow-up experiment
- **Product responsibility**: organize recipe versions, records, and evidence; support quality analysis and process diagnosis; provide constrained next-recipe recommendations when data-admission conditions are met
- **Engineer responsibility**: define objectives, review data and constraints, confirm whether a recommendation enters normal production, and interpret field context
- **System boundary**: engineers review conclusions and recipe recommendations; the system respects safety constraints, approval responsibilities, and equipment-control boundaries

Ingot works on reviewable process-run evidence rather than isolated data points or a single algorithm. The system selects robust statistics, controlled comparison, candidate-coverage design, causal validation, machine learning, Bayesian optimization, physical models, or language models according to the specific problem.

*Recipe optimization* is a business capability of the system. It covers real-run observations, next-recipe recommendations, engineer decisions, and process knowledge. *Optimization* means continuously selecting a candidate next recipe around explicit objectives, allowed variables, safety boundaries, and observed coverage. It is not synonymous with automatic control, does not establish real-factory benefit by itself, and never bypasses engineering confirmation.

## Public commitments

Public material may state that the system can:

- manage recipe-version parameters through process configuration, retain experiment facts through run and quality records, and generate and review next-recipe recommendations by site and recipe;
- link actual production conditions, process trajectories, and inspection results;
- expose missingness, provenance, versions, and uncertainty;
- help engineers compare runs and narrow candidate causes;
- automatically turn admitted real recipe runs into optimization observations;
- recommend the next recipe within declared variables, safety boundaries, and observed coverage;
- preserve validated conclusions as process knowledge with an explicit scope.

Without evidence from real projects, public material must not claim that:

- the system has automatically discovered a definitive root cause;
- it has already reduced run cost or development time by a stated percentage;
- a model recommendation is a field guarantee;
- one successful setting proves a complete operating region;
- results from one scenario transfer unconditionally to another.

Observational data can support candidate causes, stable associations, confounded associations, or insufficient-evidence judgments. The system never automatically promotes those judgments to a root cause or an equipment-control command. Each run is an experiment, with run and quality records retaining experiment facts; next-recipe recommendations use admitted run evidence. Public descriptions must distinguish implemented server-side recommendations from planned Web workflow improvements; see [Current status](status.en.md).

## Canonical language

| Use | Chinese | English |
|---|---|---|
| Product category | 开源工艺研发与优化系统 | Open-source Process R&D and Optimization System |
| Core value | 组织配方版本、实验记录与运行证据，支持质量分析、工艺追因和配方优化 | Organize recipe versions, experiment records, and run evidence to support quality analysis, process diagnosis, and recipe optimization |
| Short tagline | 从工艺数据，到有依据的研发决策。 | From process data to evidence-based R&D decisions. |
| Business capabilities | 工艺追因、配方优化 | Process Diagnosis, Recipe Optimization |
| Capability terms | 运行比较、工艺追因、配方建议、工程师决定、受约束优化 | run comparison, process diagnosis, recipe recommendation, engineer decision, constrained optimization |
| Data unit | 实验记录；工艺运行证据 | experiment record; process-run evidence |
| Observational conclusion | 候选原因、稳定关联、混杂关联、证据不足 | candidate cause, stable association, confounded association, insufficient evidence |
| Evidence-based conclusion | 支持、否决、不确定、已验证原因 | supported, rejected, inconclusive, validated cause |
| Evidence level | 证据不足、探索性证据、证据稳定、证据充分 | insufficient, exploratory, stable, sufficient |
| Optimization result | 下一配方建议、候选工艺设置、已验证工艺操作域 | next-recipe recommendation, candidate process setting, validated operating region |

Choose terms by object:

- Describe the complete product as an “Open-source Process R&D and Optimization System”; use “Process R&D and Optimization” as the short interface label.
- Use “Recipe optimization” for the workspace where engineers review real runs, next recipes, and engineer decisions.
- Use “constrained optimization” or “sequential optimization” for numerical capabilities, together with objectives, safety boundaries, and method admission.
- Call system outputs a “next-recipe recommendation” or “candidate process setting,” never an “optimal process” or “automatically dispatched parameter.”
- Call a completed, supported outcome a “validated operating region”; one successful setting is not a process window.

“Smart process” is incomplete and can imply an autonomous process, so it is not a product category, menu, or capability name. Formal product descriptions prefer “Recipe optimization,” “next-recipe recommendation,” “engineer decision,” and “constrained optimization.” Current recipe runs and their quality outcomes can become optimization observations after admission. The system neither repackages production runs as another business record nor dispatches recipes automatically.

An evidence level answers “how strong is the current support?”, an observational conclusion answers “what relationship was observed?”, and an evidence-based conclusion answers “did the intervention support the hypothesis?” These concepts are not interchangeable. *Robust screening only* (`screening`) and *limited evidence* (`limited`) are degraded labels at levels one and two; they do not introduce additional conclusion categories.

Use *root cause* only when the validating evidence is stated. *AI recipe optimization* may describe the interaction model but does not replace the product category. Algorithm names belong in technical explanations, not in the product value itself.

Do not present long-term automation ambitions, specification candidates without external adoption, or future controlled-action capabilities as a current product category, industry standard, or demonstrated benefit.

The short tagline is a communication shorthand for the core value, not a separate product definition.

## Documentation voice

Public documentation uses a formal, direct, and verifiable engineering voice:

- Home, getting-started, FAQ, and interface copy first state what the system recommends, why, and with what risk; they do not require engineers to understand model names. For example, say “continue along the stable observed trend” before introducing “linear response surface.”
- Public entry pages answer “what is this, which problem does it solve, and what works today?” before architecture names, API fields, or statistical terms. A string of abbreviations is not a product explanation.
- Expand an abbreviation on first use, such as “candidate-coverage design (DOE),” “large language model (LLM),” or “Model Context Protocol (MCP).” Keep fields needed only by developers in technical sections.
- Algorithm, architecture, validation-protocol, and development documents retain precise terminology, formal conditions, and statistical gates. Explain a term in plain language on first use rather than replacing technical precision with vague copy.
- The same fact may have layered wording: user documentation explains business meaning, while technical documentation supplies model names and decision rules. Both layers retain the same claim strength.
- State the object, capability, and result before implementation detail. Do not replace a concrete calculation or workflow with anthropomorphic terms such as *thinking*, *understanding*, or *brain*.
- Product definitions state the user, unit of work, capabilities, and outputs directly. Do not continue a definition with vague pronouns or explain internal marketing choices to readers.
- Distinguish implemented capability, test result, development-stage evidence, external validation, and roadmap work. Planned work is never written as current behavior.
- Use `AI`, `LLM`, Gaussian process, and Bayesian optimization only to identify a concrete technical responsibility, not as effect adjectives.
- Quantitative results include the evaluated population, comparator, metric, confidence interval, and applicability boundary. Without those elements, do not present a number as a benefit claim.
- Procedures use explicit commands and expected results. Product explanations avoid slogan stacking, rhetorical questions, self-assessment, and promotional second-person language.
- Chinese and English documents retain the same information hierarchy and claim strength. Translation may change sentence structure but must not add capability, benefit, or assurance.

Terms such as *help*, *recommendation*, and *candidate* are appropriate when engineer review, safety boundaries, and evidence level remain explicit. Interface labels, API fields, and commands remain unchanged for stylistic reasons.

## Mark meaning

The Ingot mark stacks three ingot cross-sections:

- the two steel ingots represent accumulated run data, execution records, and engineering knowledge;
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
- Product-effect language must distinguish simulation, automated tests, and deployer evaluation on its own production data.
- README files, documentation, website metadata, and product introductions must follow this file's core value, category, and claim boundaries.

## Related documents

- [Documentation home](index.en.md)
- [System design](design.en.md)
- [Roadmap](project-plan.en.md)
