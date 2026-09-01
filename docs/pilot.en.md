# Recipe-Optimization Pilot Guide

> Document status: **current operating guide**. The first pilot follows one real-production evidence loop.

## Pilot Scope

Limit the pilot to one product, equipment scope, and quality objective. Connect real recipe runs, actual settings, process context, and quality outcomes; first verify that identity, units, provenance, and quality review are reliable.

## Operating Sequence

1. Define product scope, quality objectives, controllable variables, and safety boundaries in an R&D project.
2. Let completed real recipe runs in scope enter admission checks automatically.
3. Generate one next-recipe recommendation after at least three valid runs cover two distinct actual recipes.
4. Record engineer adoption, modification, or rejection, with the reason and final actual recipe.
5. Link the later real production run to that decision; a rejection requires no invented execution.
6. Freeze the quality outcome from source data after parameter readback and inspection facts are complete.
7. Let new real runs and outcomes return independently as the next observations and recommendations.

## Check Every Recommendation

- Are input runs, quality outcomes, and context traceable?
- Does the recommendation stay within declared safety boundaries and observed coverage?
- Does the engineer decision include adoption, modification, or rejection and a reason?
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
