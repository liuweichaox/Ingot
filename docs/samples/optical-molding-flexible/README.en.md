# Optical glass molding: configurable sample

This sample keeps machine-specific fields in versioned industry profiles. The Ingot core only understands the event envelope, profile references, stable codes, and values; it does not add the supplied Chinese field names as platform-wide database columns.

The sample covers a 600-second cycle, one atomic group of 13 sensor values per second, 35 recipe parameters, five mapped molding phases, machine-vision inspection with a retained original image, manual inspection, complete-cycle retrieval, and same-product-series historical comparison.

Profiles retain only information needed for ingestion and analysis: a stable `code`, the machine `sourceField`, its `unit`, and `useInComparison` for explicitly selected historical-comparison signals. Numeric type and nullability are profile-level defaults. This sample omits `quantityKind`, general `enabled`, identity `transform` rules, and duplicate source/canonical units when no conversion exists; those properties should be introduced only for a real requirement.

Run it with the repository's Node.js 22 runtime:

```powershell
node docs/samples/optical-molding-flexible/generate-cycle.mjs
```

The ignored `generated/` directory contains two transport batches of 500 and 108 events. The 500-event value is only the Edge-to-Platform batch size. Both batches share one correlation ID, so complete-cycle retrieval must return all 608 events and the Web timeline must not silently truncate them.

Files in this sample:

- `acquisition-profile.v1.json`: maps 13 source sensor fields to stable codes and requires a 1 Hz atomic scan;
- `recipe-profile.v1.json`: defines 35 recipe parameters, types, and unit status;
- `recipe-instance.example.json`: copies v6, overrides three values, and resolves the complete v7 snapshot;
- `phase-mapping.v1.json`: maps controller steps to five business phases;
- `vision-inspection.example.json`: references the retained original image by controlled URI and SHA-256;
- `manual-inspection.example.json`: records manual final inspection;
- `generate-cycle.mjs`: generates a deterministic full cycle;
- `generate-factory-day.mjs`: generates a full eight-hour day for two machines and three product series;
- `import-factory-day.mjs`: idempotently imports the optional industry template, day, original images, and inspections into the Platform API.

## Full factory-day simulation

The default simulation date is 2026-07-20. Two machines run in parallel from 08:00 through 16:00:

```powershell
node docs/samples/optical-molding-flexible/generate-factory-day.mjs
```

The default `generated/factory-day-2026-07-20/` output contains 96 complete cycles, 48 per machine; 32 historical cycles for each of LENS-A, LENS-B, and LENS-C; 57,600 atomic second samples; 58,368 production events; one vision inspection, one manual inspection, and one reviewable BMP original per cycle; plus deterministic process anomalies and quality failures. It also contains explicit inspection-plan, inspection-definition, phase-definition, phase-mapping, and feature-definition files so required checks and comparison signals are configuration rather than Platform code. Events remain split into transport pages of at most 500 without imposing an analytical limit.

The output directory must be empty so a changed date or duration cannot leave stale batches. Override the defaults with `--date`, `--hours`, and `--out`. To import into a running Platform API:

```powershell
$env:INGOT_EDGE_TOKEN='your-edge-service-token'
$env:INGOT_PLATFORM_TOKEN='your-platform-identity-token'
node docs/samples/optical-molding-flexible/import-factory-day.mjs
```

The Edge token is a machine-ingestion credential; the Platform token comes from unified authentication, with no additional Web username or access password. The importer publishes the optional optical-molding quality and analysis template before importing records; Platform never creates those industry rules automatically. Stable identifiers make reruns safe. Use `--api`, `--dir`, `--skip-events`, or `--skip-inspections` to narrow the import.

Recipe change reasons are intentionally not required. `basedOn` preserves lineage, `overrides` supports UI display, and `resolvedParameters` is the authoritative immutable snapshot used by the cycle. Historical queries must never reconstruct an applied recipe by dynamically inheriting from the current recipe master.

Fields with explicit Celsius, second, millimetre, or millimetres-per-second suffixes use UCUM units. Pressure values labelled `kg`, fields without unit suffixes, PID coefficients, and the vacuum reference basis remain `needs-confirmation`. Confirming them requires a new profile version rather than an in-place rewrite of v1.

Historical comparison first requires the same `context.product_series`, then uses product code, recipe version, mold components, and inspection outcomes as comparison dimensions. Curves should align by phase and relative time within that phase. All 600 groups × 13 values per cycle participate in computation even when transport or response presentation is paged or summarized.
