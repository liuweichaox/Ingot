# Process Configuration Model

Ingot separates process configuration into three independent, versioned subjects so definitions, operating values, and analytical choices are not mixed in one profile.

## Process data model

A process data model describes how the platform interprets source data:

- acquisition data items: source field, stable item code, data type, unit, and category;
- recipe parameter definitions: allowed parameter code, source field, type, unit, and nullability;
- process stages: controller step, stable stage code, display name, and expected duration.

The model contains neither recipe values nor analysis-specific signal selections. Published versions are immutable.

API: `/api/v1/process-data-models`

## Recipe version

A recipe version references one process-data-model version and stores the complete effective parameter values. `basedOnVersion` preserves lineage; Ingot does not require a parameter-change reason.

Applicability uses generic context key/value pairs such as `product.series=LENS-A`. At production start, the production-condition snapshot freezes the applied recipe identifier, version, and effective values. Historical queries never dynamically inherit from the current recipe.

API: `/api/v1/recipe-versions`

## Process analysis plan

An analysis plan references a process data model and independently defines:

- analysis scope: production cycle, production run, or analysis window;
- alignment: process-stage relative time, elapsed time, or normalized completion;
- production-context filters;
- comparability keys such as product series, material grade, or tooling assembly;
- a cohort dimension such as `quality.outcome`;
- selected data items, full-trace inclusion, and derived features.

Quality outcomes remain first-class quality data. The analysis plan references a quality cohort dimension without copying or degrading inspection evidence into ordinary tags.

An empty context selector means “all contexts of the referenced process data model”; it never matches across models or industries. At runtime, the model is resolved from the frozen `data_model_id/data_model_version` context or from the recipe version before a published plan in that model is selected. Every configured comparability key must be present in every candidate context. Missing keys reject comparison instead of treating two null values as equivalent.

Cycle comparison reads each complete event chain. Continuously operating equipment uses production runs or user-defined time windows. APIs may page internally, but page size is not an analytical limit.

API: `/api/v1/process-analysis-plans`

## Analysis presentation

Platform Web uses Plotly as its shared chart-rendering layer, but chart configuration is not a new business-data subject. The analysis plan determines scope, comparability, alignment, and selected data items; the page then presents the result as a trend, distribution, quality cohort, or statistical-detail view.

- Cycle and time-window trends share an elapsed-time axis, with the baseline emphasized as a solid line.
- Pages read complete sample traces through cursors; a 500-row API page is not treated as an analytical limit.
- Quality cohort charts, summary cards, and detail tables use the same filter scope. Charts reveal differences, while tables support numerical review and record traceability.
- Charts support unified hover, zoom, reset, responsive layout, and image export. Structured charts returned by AI use the same renderer and retain an expandable data table.

## Lifecycle

All three subjects support `draft`, `published`, and `retired`. Drafts can be edited or deleted. A published version can only be retired; any other change requires a new version.

The legacy `/profiles` entry redirects to `/configuration/process-data-models`.
