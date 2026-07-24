# Ingot Chat

Ingot Chat is the investigation entry point for process, quality, and equipment teams. A user describes a problem and selects the relevant objects. The system turns the question into reviewable data steps and returns a summary, metrics, charts, and related records.

## Questions to ask

- Why is this batch's first-pass yield below the previous batch?
- When did cycle time at station 07 begin to increase?
- Which machine has a different dimension distribution on the same recipe?
- What process curves and inspections belong to this abnormal workpiece?
- Which metrics changed across the last 500 uses of this tool?
- Is the current data sufficient to compare these two production cycles?

## The investigation flow

### Establish scope

Chat uses the current page and question to identify the batch, machine, workpiece, recipe, tooling, metric, and time range. When the scope needs more detail, the interface asks the user to add a selection.

### Check data

The system first checks record counts, required context, cycle completeness, and inspection coverage, then determines which samples can be compared.

### Calculate and compare

Platform data capabilities perform metrics, aggregation, cycle alignment, and difference comparison. Chat organizes the investigation steps and explains the result.

### Present the result

The answer includes a conclusion summary, key numbers, charts, data-coverage notes, and related production records. The user can ask a follow-up or open records to review timelines and original curves.

## Make questions easier to answer

A clear question usually includes a target, scope, and comparison:

> Compare LOT-0716 with the previous batch of the same product, identify the station where the first-pass-yield difference is concentrated, and list the three process parameters that changed with it.

You can also start simply:

> What happened in this cycle?

Then add detail:

> Compared with the 20 most recent normal cycles on the same recipe, which stage differs most?

## Review the evidence

The result shows the objects, time range, sample count, and related records used in the investigation. Metrics and charts open the corresponding cycle, stage, or inspection detail.

## Reuse an investigation method

Common questions can be preserved as analysis plans with their target metrics, comparison dimensions, filters, and presentation. Teams can reuse the same scope and definitions in later investigations.

Continue with [Rollout](rollout.en.md) to plan first use at a manufacturing site.
