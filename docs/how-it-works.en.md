# How Ingot works

Ingot organizes data and analysis around the production history. A production history is more than an isolated table: it connects equipment, workpieces, batches, recipes, tooling, stage parameters, and inspection outcomes for one production process.

## 1. Collect plant data

Edge connects to equipment, instruments, or plant data services and collects key parameters, state changes, and inspection data. Each record receives its time, source, and production context before entering the central platform.

Sites can use continuous collection, cycle snapshots, or batch imports according to their network conditions. Buffering and replay keep the history continuous.

## 2. Establish production context

Platform maintains the information needed for analysis, including:

- products and workpieces;
- equipment and production units;
- recipes and versions;
- tooling, molds, and cutters;
- process stages and parameter meaning;
- inspection items, units, and quality plans.

This context gives every value a clear object, time, stage, and unit, and allows batches to be compared with consistent definitions.

## 3. Build the production history

Events, time-series parameters, and inspections from the same run are linked around the production cycle. The system preserves original records and produces cycle, stage, and feature views for investigation.

Engineers can move from a batch into its cycles or trace an abnormal inspection result back to the matching process.

## 4. Complete a process investigation

An investigation normally completes four activities:

1. confirm the target, time range, and comparison baseline;
2. check completeness and sample comparability;
3. calculate metrics and compare batches, machines, stages, or parameters;
4. summarize the main differences, coverage, and related production records.

Ingot Chat organizes this flow as an everyday-language interaction. Cycle, batch, and analysis pages provide charts and progressive drill-down.

## 5. Preserve analysis methods

Teams can save commonly used process stages, parameter features, comparison dimensions, and evaluation metrics as versioned configuration. Later investigations reuse the same definitions, making results easier to compare across people and time.

## 6. Return to original records

Metrics and charts in the analysis summary retain their related production records. Teams can inspect the data range, sample count, curves, inspections, and context to continue engineering review.

Continue with [Ingot Chat](ingot-chat.en.md) for the everyday investigation workflow.
