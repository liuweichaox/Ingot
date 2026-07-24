# ADR-0007: Research-grade time series and confirmatory trials

Status: accepted; core vertical path implemented

## Decision

Ingot retains two complementary record types:

1. `production_events` is the immutable business record, ingest-idempotency boundary, and audit source.
2. `time_series_samples` is the typed scientific-computation record; one collection point and signal form a stable series.

Both are written in the same database transaction. Measurements are separate from static tags, while run context remains an immutable snapshot. Upper layers depend only on the internal `ITimeSeriesStore`. TimescaleDB is the sole production storage implementation; a second time-series database is not planned.

A feature code is not a formula. A versioned registry defines every feature, and each result stores the definition hash, input-point count, and computation hash. A formula-behavior change requires a new definition identity and analysis algorithm version.

Trials are exploratory or confirmatory. A confirmatory trial requires a preregistered protocol, and its outcomes and safety checks must be computed from versioned cycle features. Manual results remain valid for exploration but cannot be labeled confirmatory scientific findings.

## Rationale

- Preserve the audit and business expressiveness of events while adding collection-point locality and typed time-series computation.
- Stop treating JSONB event payloads as the long-term scientific-computation interface.
- Make identical definitions, inputs, and windows reproducible and hash-verifiable.
- Upgrade from recording a confidence interval to calculating it from traceable source records.
- Preserve one computation contract for future streaming windows, mechanism models, and offline scientific runners.

## Current boundaries

- TimescaleDB is the sole production database implementation. `ITimeSeriesStore` isolates internal computation and testing boundaries; it is not a multi-database product commitment.
- The current confirmatory estimator is Welch difference in means. Additional estimators require separately versioned definitions.
- Ingot does not write equipment. Site execution, interlocks, and emergency stops remain in existing control systems.
