# Recipe-optimization pilot guide

> Document status: **current operating guide**. This guide starts with one bounded recipe-optimization objective and aims to turn normal production runs into the first qualified observations and one engineer-confirmed next-recipe recommendation. It is not production-deployment acceptance and does not guarantee optimization benefit from a pilot.

This document provides a sequential pilot checklist. Limit the first pilot to one product, equipment scope, and quality objective, then connect real recipe runs, actual settings, process context, and quality outcomes. Daily optimization requires neither experiment setup nor manual reclassification of existing recipes. Controlled validation is optional and separate for causal confirmation, extrapolation, or operating-region validation.

## Entry conditions

Before starting, confirm that:

- the complete system is running according to [Getting started](getting-started.en.md);
- the project has a named owner and an independent reviewer;
- equipment, inspection, and business data may be used in the target environment;
- parameter and outcome safety boundaries are agreed by engineers and site safety rules;
- the pilot will not bypass PLC, DCS, equipment interlocks, or existing approval processes.

Direct production use also requires the site acceptance described in [Production architecture](production-architecture.en.md) and [Deployment](deployment.en.md).

## 1. Define the engineering problem

Select one bounded problem at a time:

| Content | Question to answer |
|---|---|
| Industrial object | Which product, material, or process? |
| Equipment scope | Which machine or comparable equipment group? |
| Run boundary | Where does one run start and end? |
| Controlled variables | Which settings can an engineer actually change? |
| Quality objective | Which inspection decides the result, with what direction, specification, and unit? |
| Safety boundary | Which settings or outcomes must never be crossed? |
| Manufacturing context | Which material, tooling, batch, calibration, or maintenance facts must be traced? |

Do not begin by assigning an entire factory or a vague “improve quality” objective to the system.

## 2. Freeze the pilot baseline

Before evaluating system recommendations, fix measures, comparison methods, and pass or fail criteria. This reduces selective interpretation and makes any reduction in manual operating cost measurable.

Freeze the following in “Phase 0: preregistration and data baseline” for the research project:

- data and time ranges, inclusion, and exclusion rules;
- comparison baselines and matching conditions;
- the most important outcome, metrics that must not get worse, and the smallest change that matters in practice;
- safety boundaries, when to stop, and which result would show that the original judgment was wrong;
- the steps and time engineers currently spend collecting data, organizing recipes, analyzing results, and choosing the next recipe.

Another project member reviews the plan. A project cannot enter formal R&D without a current reviewed version.

## 3. Publish the process configuration

The process configuration freezes the project's data and analysis semantics:

- process variables, standard units, and actual-value sources;
- run boundaries, stages, and process features;
- inspection definitions, quality rules, and review requirements;
- controlled variables, allowed ranges, objectives, and safety constraints;
- analysis-required and record-if-available context fields.

Publish a version before research projects and acquisition tasks reference it. Display names may change; stable codes and historical semantics may not be rewritten afterward.

## 4. Connect data sources

Complete these steps under “Field integration → Data-source configuration”:

1. register the field node and equipment identity;
2. select a protocol and enter connection details;
3. map raw points to published process variables;
4. probe and read real values on the target Edge;
5. verify data type, scale, offset, unit, and time;
6. publish the acquisition configuration and confirm that Edge applied it;
7. configure manual inspection, instrument, or quality-system sources.

PLC, instrument, and gateway addresses belong to field-integration configuration. A research project references only stable business codes. See [Data integration](data-connection.en.md) for protocol and mapping rules.

## 5. Freeze manufacturing context

Before a run begins, confirm the equipment's product or process object, published specification, installed tooling, material batch, calibration, and maintenance state. The system freezes an immutable context snapshot at run start.

A run with missing analysis-required context is retained but cannot enter that analysis. Do not guess which material or tooling was used after a quality exception appears.

## 6. Complete a representative run

One stable identity must link field execution and inspection:

```text
actual recipe + process context + quality outcome ←→ Platform ExecutionId
```

After completion, verify under “Production runs → Run records” that:

- start and completion events are present;
- actual settings were acquired and not silently replaced with planned values;
- process trajectories and stages are available;
- context, configuration, and source versions are explicit;
- restarts, disconnections, late events, and duplicates did not break run identity.

## 7. Link and review inspection

Each result includes at least a characteristic code and version, value or conclusion, unit, sample or run identity, time, source, and required attachments. Results requiring independent review enter formal comparison and optimization observations only after review passes.

An inspection that cannot be linked uniquely remains pending; the system does not guess from timestamp proximity. Attachments and downloads remain constrained by site and role authorization.

## 8. Pass data admission

Review the following under “Process diagnosis → Data trust”:

- complete-run, actual-setting, and process-feature coverage;
- unique run-to-inspection linkage;
- analysis-required context coverage;
- unit, clock, source, and configuration-version anomalies;
- the specific reason for every excluded run.

When analysis conditions are not met, repair the data chain instead of training a more complex model.

## 9. Compare runs and form candidates

Choose one nonconforming run and a conforming or historical baseline with explicit matching conditions. Review first deviation, planned-versus-actual differences, trajectory features, materials, tooling, equipment, and time factors.

Observational analysis produces only candidate causes, stable associations, confounded associations, or insufficient evidence. An engineer reviews evidence, counterevidence, coverage, and site limitations before using it in recipe optimization. A separate validation hypothesis is created only when causal confirmation is required.

## 10. Form optimization observations

Create a task under “Recipe optimization” and define product scope, quality objectives, controllable variables, and safety boundaries. The system reads completed real recipe runs in scope automatically; no experiment or manual run-to-experiment classification is required.

Verify that:

- at least three runs pass completion, actual-setting, outcome, and context admission;
- at least two distinct actual recipes are represented;
- every excluded run has an explicit reason;
- each observation retains its source run, configuration, quality rule, and content snapshot for recomputation.

If samples are insufficient, continue normal production and wait for new runs, or repair the data chain. Do not create nominal experiments merely to satisfy a count threshold.

## 11. Generate and confirm the next recipe

The system generates one next-recipe recommendation only when trustworthy observations, controllable variables, a safety baseline, and current method admission are all present. The interface shows candidate settings, prediction intervals, outcome-safety probability, data range, model version, and rationale.

The recommendation is an independent append-only record with no experiment identifier, run plan, experiment approval state, or equipment-dispatch command. Engineers adopt, modify, or reject it through the existing production-preparation, MES, or process-specification flow. New runs and outcomes create new observations and allow a new recommendation. If method admission is missing, unreviewed, failed, or bound to another model version, the system stops and explains the recommendation.

## 12. Design controlled validation only when needed

Do not rely directly on daily recipe recommendations when the task must establish a cause, exceed the observed parameter envelope, introduce new material or equipment, or extend one successful point into a releasable operating region. Create separate controlled validation and define at least:

- the candidate cause or coverage boundary to test;
- controls, repeats, blocks, and execution order;
- objective, minimum meaningful effect, and safety boundaries;
- stopping, failure, and fallback conditions.

Controlled validation has its own plan, approval, and state machine. A classical DOE preview may produce an editable run table, but it is not approval and writes nothing to equipment. One successful setting remains only a candidate; an operating region requires independent repeats, boundary or interaction validation, and review by another engineer.

## Completion criteria

The first pilot round is complete not when “the model produces a better setting,” but when all of the following hold:

- one representative run maps uniquely to actual conditions, trajectory, context, and reviewed outcome;
- every inclusion, exclusion, comparison, and recommendation has an explicit reason and version;
- at least three valid runs covering two actual recipes automatically form observations without manual classification;
- the system creates one independent next-recipe recommendation inside safety boundaries and observed coverage;
- an engineer can adopt, modify, or reject the recommendation and record the reason, with no automatic dispatch;
- results can be recomputed from source data, with failures and inconclusive outcomes retained.

If the round includes causal confirmation or extrapolation, add “controlled validation has controls, safety boundaries, and stopping conditions” to the completion criteria. It is not mandatory for a daily recipe-optimization pilot.

Continue historical, shadow, and online stages according to [Scenario validation](rollout.en.md). Use [Current status](status.en.md) as the unified capability and evidence-maturity page.
