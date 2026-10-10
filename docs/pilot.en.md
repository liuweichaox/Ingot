# Recipe-Optimization Pilot Guide

> Document status: **current operating guide**. This guide validates the implemented production-evidence recommendation workflow. Next-recipe recommendations require admitted production-run evidence; each run is an experiment, with actual parameters and quality outcomes retained in the run-evidence chain. See [Current status](status.en.md) for the full capability boundary.

## Pilot Scope

Limit the pilot to one product or process scope and one quality objective. Provide real recipe runs, actual settings, process context, and quality outcomes for the current production-evidence workflow; data may come from manual operations or connectors configured by the deployer. First verify that identity, units, provenance, and quality review are reliable.

## Operating Sequence

1. Open a published recipe version in process configuration. The page derives the site, product scope, quality objective, and adjustable parameters from that version, its process variables, published quality plans, and existing runs. Check the source configuration and displayed scope instead of entering a separate set of experiment variables.
2. Select “检查数据” (Check data) to review admitted completed runs and exclusion reasons. The recipe needs adjustable parameters with units and bounds, a numeric quality characteristic covering the recipe, and published process analysis containing process curves.
3. After at least three valid runs cover two distinct actual recipes, select “生成下一轮校正” (Generate next-run correction). These are minimum data requirements; failed coverage, constraint, or method checks still stop the recommendation with an explanation.
4. Choose “采用为下一轮校正” (Adopt as next-run correction), “修改后作为下一轮校正” (Modify and adopt), or “拒绝” (Reject). Modification and rejection require a reason; direct adoption keeps the suggested parameters and may include a reason. Adoption or modification leaves the published recipe version unchanged. Use “作为显著变更” (Treat as significant change) to open a revision draft for a significant change.
5. After adoption or modification, use that recipe version for a subsequent real run. The recipe page attempts to link the next completed same-version run that started after the decision; the server still validates the site and frozen context. Rejection requires no linked run.
6. The page reads parameter readback and inspection facts from the linked run and requests outcome freezing. If this fails, repair the data and select “再次读取结果” (Read result again). Only runs passing outcome admission become later optimization observations.
7. Complete the current decision and outcome before generating another correction. Recommendations are never dispatched automatically; field parameter changes follow existing operating procedures.

## Check Every Recommendation

- Are input runs, quality outcomes, and context traceable?
- Does the recommendation stay within declared safety boundaries and observed coverage?
- Does the engineer decision record adoption, modification, or rejection, with a reason for modification or rejection?
- Does an adoption or modification link to an actual execution?
- Is the outcome frozen only after parameter readback and inspection facts are complete?

## Insufficient Samples or Bad Data

When samples are insufficient, continue normal production and wait for new real runs, or repair the data chain. Missing data, incomparable runs, conflicting constraints, or unmet model conditions must not be bypassed to generate a recommendation.

## Completion Criterion

A pilot completes at least one auditable “recommendation → decision → actual run → quality outcome” chain. It demonstrates the system flow and the scenario's data chain; it does not establish a general benefit or causal conclusion.

## Related Documents

- [Analysis and optimization](optimization.en.md)
- [Data integration](data-connection.en.md)
- [Current status](status.en.md)
