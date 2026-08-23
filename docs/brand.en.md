# Brand guide

> Status: **v1 normative baseline**. This file is the single source of truth for product positioning, core value, and public language. Do not redefine the core value unless the product direction actually changes.

## Core value

> **Turn every real run into comparable, testable engineering evidence so process engineers can avoid unproductive experiments and reach target process conditions faster.**

Avoiding unproductive experiments is the outcome the product is built to deliver, not a synonym for one algorithm. Data acquisition, process diagnosis, experimental design, linear or quadratic response surfaces, Bayesian optimization, mechanism fusion, and model interaction are means to that outcome.

## Product position

- **Category**: Open-source Process Diagnosis & Optimization / 开源工艺追因与优化系统
- **Primary users**: process, quality, equipment, and R&D engineers developing new products, materials, and processes
- **Unit of work**: the conditions, trajectory, quality result, engineering judgment, and next experiment for a real run
- **Product responsibility**: organize trustworthy facts, compare runs, form testable candidates, help select the next experiment, and preserve the limits of every conclusion
- **Engineer responsibility**: frame the problem, review data and constraints, judge executability, approve experiments, and interpret field context
- **System boundary**: Ingot does not replace engineers or bypass safety constraints, approvals, or equipment control systems

Ingot is not built merely to collect more points or showcase one algorithm. It turns collected data into usable engineering evidence and then selects robust statistics, controlled comparison, experimental design, causal validation, machine learning, Bayesian optimization, physical models, or language models according to the problem.

*Process R&D* is the business entry in the product information architecture. It covers candidate validation, experiment design, safe optimization, and R&D outcomes. *Optimization* summarizes the capability to select the next experiment in the product category; it is not a standalone primary menu and does not imply automatic writes to equipment.

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
| Core value | 少做无效实验，更快找到达标工艺 | Avoid unproductive experiments and reach target process conditions faster |
| Short tagline | 看清这次运行，做对下一项实验。 | Understand this run. Choose the right next experiment. |
| Data unit | 运行 / 过程执行 | run / process execution |
| Observational conclusion | 候选原因、稳定关联、混杂关联、证据不足 | candidate cause, stable association, confounded association, insufficient evidence |
| Experimental conclusion | 支持、否决、不确定、已验证原因 | supported, rejected, inconclusive, validated cause |
| Evidence level | 证据不足、探索性证据、证据稳定、证据充分 | insufficient, exploratory, stable, sufficient |
| Optimization result | 下一步实验建议、候选设置、已验证工艺操作域 | next-experiment recommendation, candidate setting, validated operating region |

An evidence level answers “how strong is the current support?”, an observational conclusion answers “what relationship was observed?”, and an experimental conclusion answers “did the intervention support the hypothesis?” These concepts are not interchangeable. *Robust screening only* (`screening`) and *limited evidence* (`limited`) are degraded labels at levels one and two; they do not introduce additional conclusion categories.

Use *root cause* only when the validating evidence is stated. *AI process R&D* may describe the interaction model but does not replace the product category. Algorithm names belong in technical explanations, not in the product value itself.

Do not present long-term automation ambitions, specification candidates without external adoption, or future controlled-action capabilities as a current product category, industry standard, or demonstrated benefit.

The short tagline is a communication shorthand for the core value, not a separate product definition.

## Documentation voice

Public documentation uses a formal, direct, and verifiable engineering voice:

- Home, getting-started, FAQ, and interface copy first state what the system recommends, why, and with what risk; they do not require engineers to understand model names. For example, say “continue along the stable observed trend” before introducing “linear response surface.”
- Algorithm, architecture, validation-protocol, and development documents retain precise terminology, formal conditions, and statistical gates. Explain a term in plain language on first use rather than replacing technical precision with vague copy.
- The same fact may have layered wording: user documentation explains business meaning, while technical documentation supplies model names and decision rules. Both layers retain the same claim strength.
- State the object, capability, and result before implementation detail. Do not replace a concrete calculation or workflow with anthropomorphic terms such as *thinking*, *understanding*, or *brain*.
- Distinguish implemented capability, test result, development-stage evidence, external validation, and roadmap work. Planned work is never written as current behavior.
- Use `AI`, `LLM`, Gaussian process, and Bayesian optimization only to identify a concrete technical responsibility, not as effect adjectives.
- Quantitative results include the evaluated population, comparator, metric, confidence interval, and applicability boundary. Without those elements, do not present a number as a benefit claim.
- Procedures use explicit commands and expected results. Product explanations avoid slogan stacking, rhetorical questions, self-assessment, and promotional second-person language.
- Chinese and English documents retain the same information hierarchy and claim strength. Translation may change sentence structure but must not add capability, benefit, or assurance.

Terms such as *help*, *recommendation*, and *candidate* are appropriate when engineer review, safety boundaries, and evidence level remain explicit. Interface labels, API fields, and commands remain unchanged for stylistic reasons.

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
- [Scenario validation](rollout.en.md)
- [Roadmap](project-plan.en.md)
