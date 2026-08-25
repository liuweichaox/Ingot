# Controlled pilot guide

> Document status: **current operating guide**. This guide starts with one bounded engineering problem and aims to produce the first trustworthy run evidence and the first controlled validation experiment. The guide is not production-deployment acceptance and does not guarantee optimization benefit from a pilot.

This document provides a sequential pilot checklist. The initial pilot should be limited to one concrete engineering problem and complete one data chain and one validation experiment; its scope should not cover an entire line or use a broad objective such as “improve quality everywhere.”

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

## 2. Preregister the validation plan

Preregistration fixes measures, comparison methods, and pass or fail criteria before results are reviewed. This reduces the risk of selective interpretation after the experiment.

Freeze the following in “Phase 0: preregistration and data baseline” for the research project:

- data and time ranges, inclusion, and exclusion rules;
- comparison baselines and matching conditions;
- the most important outcome, metrics that must not get worse, and the smallest change that matters in practice;
- safety boundaries, when to stop, and which result would show that the original judgment was wrong;
- the steps, time, and experiments required by the current engineering process.

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

One stable identity must span the experiment plan, field execution, and inspection:

```text
Research ExecutionKey ←→ Platform ExecutionId
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

Observational analysis produces only candidate causes, stable associations, confounded associations, or insufficient evidence. An engineer reviews the evidence, counterevidence, coverage, and site limitations before creating a research hypothesis.

## 10. Design the validation experiment

An experiment draft defines at least:

- the candidate cause and controlled variables to test;
- controls, repeats, blocks, and execution order;
- objective, minimum meaningful effect, and safety boundaries;
- stopping, failure, and fallback conditions.

A classical DOE preview can produce an editable run table. A preview is not approval and writes nothing to equipment. Engineers modify and approve the plan before execution; source runs and inspections determine the result.

## 11. Generate a next-step recommendation when appropriate

Sequential advice begins only when trustworthy observations, controlled variables, a safety baseline, and current method admission are all present. The system shows candidate process settings, prediction intervals, outcome-safety probability, data range, model version, and rationale.

If method admission is missing, unreviewed, failed, or bound to another model version, the system must stop that recommendation and identify an applicable response-surface or classical-DOE fallback. One successful setting is only a candidate. An operating region also requires independent repeat, boundary or interaction validation, and review by another engineer.

## Completion criteria

The first pilot round is complete not when “the model produces a better setting,” but when all of the following hold:

- one representative run maps uniquely to actual conditions, trajectory, context, and reviewed outcome;
- every inclusion, exclusion, comparison, and recommendation has an explicit reason and version;
- one cause candidate has become an experiment with controls, safety, and stopping conditions;
- an engineer can approve, modify, or reject advice and record the reason;
- results can be recomputed from source data, with failures and inconclusive outcomes retained.

Continue historical, shadow, and online stages according to [Scenario validation](rollout.en.md). Use [Current status](status.en.md) as the unified capability and evidence-maturity page.
