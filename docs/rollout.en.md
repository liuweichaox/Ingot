# Rollout

An Ingot rollout starts with one specific production question. A focused first scope makes it easier to validate data completeness, prove the investigation workflow, and create a template for expansion.

## Step 1: choose the first question

A strong first question usually:

- already affects yield, rework, cycle time, or maintenance cost;
- has a clear product, machine, batch, or process stage;
- has some process and inspection data available;
- currently requires repeated exports or manual comparison;
- can be reviewed against existing records and site knowledge.

Keep the first scope to one line, one product family, and a small set of key metrics.

## Step 2: confirm the data checklist

The implementation team and process engineers confirm:

| Data | What to confirm |
|---|---|
| Production objects | Product, workpiece, batch, and production-cycle identifiers |
| Equipment process | Machine, station, time, stage, and key parameters |
| Process configuration | Recipe version, parameter units, and stage meaning |
| Tooling | Tool, mold, or cutter identity and usage relationships |
| Quality outcomes | Inspection item, result, unit, time, and workpiece relationship |
| Comparison definition | Normal baseline, target metrics, and valid comparison scope |

## Step 3: connect and validate

Edge connects plant data sources and Platform receives and organizes production records. The implementation first uses a small number of real cycles to confirm time, units, object relationships, and stage boundaries, then expands to complete shifts or historical batches.

Data validation shows cycle coverage, inspection coverage, missing context, and time anomalies so site teams can refine mapping and configuration.

## Step 4: complete the first investigation

A process engineer starts from a batch, cycle, or Chat question, reviews the selected scope and baseline, and checks the result against original records.

The first investigation should produce:

1. a repeatable plant-data configuration;
2. a confirmed analysis definition;
3. an investigation workflow the site team can reuse.

## Step 5: expand the scope

Once the first question is stable, expand according to value across more machines, products, recipes, tooling, and quality metrics. Existing data mappings, process stages, and analysis plans become a starting point for similar lines.

## Operating preparation

Before regular use, the team confirms:

- where Edge and Platform will run;
- data retention, backup, and recovery arrangements;
- users, roles, and visible data scope;
- model service and access credentials;
- ownership for logs, metrics, and alerts;
- ownership for process configuration and analysis plans.

Continue with the [FAQ](faq.en.md) for common questions about scope, data, and everyday use.
