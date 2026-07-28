# Equipment and Data Wiring

## Goal

The optimizer needs a coherent run, not isolated tags:

- What was planned?
- What settings and conditions were actually used?
- What trajectory occurred?
- What quality and safety outcomes resulted?

## Data sources and adapters

Ingot does not treat PLCs as the only source of evidence. A run may combine any of:

- recipes, state, and process signals from control systems;
- measurements from instruments, sensors, or edge gateways;
- machine-vision, laboratory, or inline-inspection results;
- run context from MES, QMS, genealogy, or other business systems.

An adapter maps raw inputs to stable business codes, units, quality state, and `RunKey`; a research project never binds to a vendor address or protocol.

## FX3U connection example

FX3U is one supported field-controller example, using the Edge MELSEC A1E runner. It is not the product boundary. This scenario includes:

- PLC address and port;
- poll interval;
- cycle start, completion, and correlation;
- recipe registers;
- temperature, pressure, position, and other process points;
- data type, byte order, scaling, and unit.

Do not put equipment addresses in research projects. Equipment profiles own addresses; research variables reference stable business codes.

## Cycle correlation

Platform generates a RunKey such as:

```text
bo-7d2f6a3e1c2d-01
```

Where a controller exposes a cycle-correlation register, write this value or a reversible mapping to it. Acquisition creates a cycle `CorrelationId`; inspection stores it as `OperationRunId`. Without a PLC, a MES order, barcode, instrument sample ID, or Edge mapping can join records to the same `RunKey`.

If a controller can only store numbers, Edge may maintain a deterministic mapping from a short numeric ID to RunKey.

## Source expressions

### Controls

```text
recipe:<recipe-code>
signal:<signal-code>:<feature-code>
signal:<signal-code>:<feature-code>:<phase-code>
```

Examples:

```text
recipe:holding-temperature
signal:mold-temperature:mean:holding
```

### Objectives and safety outcomes

```text
inspection:<characteristic-code>
```

Examples:

```text
inspection:form-error
inspection:crack-rate
```

## Process features

Versioned cycle analysis emits signal- and phase-level features:

- mean, minimum, maximum, and deviation;
- slope, integral, peak, and overshoot;
- arrival time, dwell time, and phase coverage;
- realized heating rate and pressure stability.

Training uses only features common to every valid observation. Feature-definition changes produce a new version and content hash.

## Valid observation

A run enters the model only when:

- the cycle is complete;
- process status is not unavailable;
- at least one process feature exists;
- every control has an actual value;
- every objective has a numeric inspection;
- every outcome constraint has a numeric inspection;
- units match;
- values are finite.

Excluded runs retain a reason and source hash for wiring repair.

## Adapting another process

Prefer configuration over duplicated code:

1. choose or implement a protocol driver;
2. define equipment profile and points;
3. define cycle and phases;
4. map research variables;
5. define inspection characteristics;
6. provide a safe baseline;
7. verify one complete end-to-end run.
