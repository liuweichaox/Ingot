# Recipe-Optimization Pilot Guide

> Document status: **current operating guide**. This guide validates the implemented production-evidence recommendation workflow. Next-recipe recommendations require admitted production-run evidence; each run is an experiment, with actual parameters and quality outcomes retained in the run-evidence chain. See [Current status](status.en.md) for the full capability boundary.

## Pilot Scope

Limit the pilot to one product or process scope and one quality objective. Provide real recipe runs, actual settings, process context, and quality outcomes for the current production-evidence workflow; data may come from manual operations or connectors configured by the deployer. First verify that identity, units, provenance, and quality review are reliable.

## Operating Sequence

Before starting, confirm that the account can read the target site's run and inspection records, that the recipe version, quality plan, and process analysis are published, and that variable codes, units, and bounds agree. Assign responsibility for field changes, quality entry, and review; when independent review is required, the recorder and reviewer must be different people. Retain existing safety procedures and approvals. Repair source configuration first when these prerequisites fail.

1. Open a published recipe version in process configuration. The page derives the site, product scope, quality objective, and adjustable parameters from that version, its process variables, published quality plans, and existing runs. Check the source configuration and displayed scope instead of entering a separate set of experiment variables.
2. Select “检查数据” (Check data) to review admitted completed runs and exclusion reasons. The recipe needs adjustable parameters with units and bounds, a numeric quality characteristic covering the recipe, and published process analysis containing process curves.
3. After at least three valid runs cover two distinct actual recipes, select “生成下一轮校正” (Generate next-run correction). These are minimum data requirements; failed coverage, constraint, or method checks still stop the recommendation with an explanation.
4. Choose “采用为下一轮校正” (Adopt as next-run correction), “修改后作为下一轮校正” (Modify and adopt), or “拒绝” (Reject). Modification and rejection require a reason; direct adoption keeps the suggested parameters and may include a reason. Adoption or modification leaves the published recipe version unchanged. Use “作为显著变更” (Treat as significant change) to open a revision draft for a significant change.
5. After adoption or modification, use that recipe version for a subsequent real run. The recipe page attempts to link the next completed same-version run that started after the decision; the server still validates the site and frozen context. Rejection requires no linked run.
6. The page reads parameter readback and inspection facts from the linked run and requests outcome freezing. If this fails, repair the data and select “再次读取结果” (Read result again). Only runs passing outcome admission become later optimization observations.
7. Complete the current decision and outcome before generating another correction. Recommendations are never dispatched automatically; field parameter changes follow existing operating procedures.

## Check Every Recommendation

Check completion in the same order as the operating steps:

1. **Scope confirmed**: the displayed scope agrees with published source configuration, with objectives and adjustable parameters individually verifiable.
2. **Data confirmed**: admitted and excluded runs are identifiable; the minimum count is met and actual settings include at least two combinations.
3. **Recommendation generated**: a retrievable recommendation identifies its inputs, predictions, and constraints; a stopped request retains its reason.
4. **Decision recorded**: adoption, modification, or rejection is saved; modified settings and reasons are inspectable, and a significant change opens a revision draft.
5. **Run linked**: adoption or modification links a real same-version run starting after the decision and passing server-side context validation.
6. **Outcome frozen**: parameter readback and valid inspection facts come from the linked run, and freezing succeeds; failed reads retain the record while data is repaired.
7. **Round closed**: the decision and required outcome are complete before another round; rejection does not manufacture an execution outcome.

- Are input runs, quality outcomes, and context traceable?
- Does the recommendation stay within declared safety boundaries and observed coverage?
- Does the engineer decision record adoption, modification, or rejection, with a reason for modification or rejection?
- Does an adoption or modification link to an actual execution?
- Is the outcome frozen only after parameter readback and inspection facts are complete?

## Insufficient Samples or Bad Data

When samples are insufficient, continue normal production and wait for new real runs, or repair the data chain. Missing data, incomparable runs, conflicting constraints, or unmet model conditions must not be bypassed to generate a recommendation.

## Completion Criterion

Retain the recipe version, input execution keys, recommendation identifier, engineer decision, linked run, and any exclusion or stop reasons. A reviewer should be able to follow these identifiers back to actual parameters and quality facts. Predictions without retrievable actual-run evidence do not meet the completion criterion.

A pilot completes at least one auditable “recommendation → decision → actual run → quality outcome” chain. It demonstrates the system flow and the scenario's data chain; it does not establish a general benefit or causal conclusion.

## Related Documents

- [Analysis and optimization](optimization.en.md)
- [Data integration](data-connection.en.md)
- [Current status](status.en.md)
