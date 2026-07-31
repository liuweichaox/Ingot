# Equipment and Data Wiring

## Position in the product loop

Equipment connectivity is not the first step. Define the platform variables, types, and normalized units in the process model first; then select a protocol and map live equipment points to those variables:

```text
define process → connect equipment → collect production data → close the data loop → diagnose → process optimization
```

Equipment onboarding is complete only when required points have been read, converted, and validated; production start and end can form a run; and actual recipes and process signals enter the same run context. A successful socket connection alone is not completion.

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

## Boundary between process semantics and device connectivity

A process semantic model defines only the platform variable code, display name, normalized type, normalized unit, role, and requiredness. It does not contain device addresses, registers, or sampling frequency. A device connection references one semantic-model version and supplies the protocol, point selector, raw type, and conversion. The same semantic model can therefore be reused by FX3U, OPC UA, or other equipment.

The stage number is a regular process variable whose role is `stage`; it is mapped once, and Edge derives stage-change events from it. Production start/end is a device control signal configured separately in the connection.

## Driver-specific configuration, not a generic address

Device onboarding starts by selecting a communication driver. The UI then shows only the settings that the selected driver actually requires:

| Driver | Connection settings | Point settings | Discovery or validation |
| --- | --- | --- | --- |
| HTTP API | base URL, JSON path, polling delay | JSON field path, raw type, conversion | read one JSON document and show its field tree |
| MQTT | broker, port, version, client, credentials, TLS, topics, QoS, session behavior | JSON field path, raw type, conversion | wait for a real message and show its field tree |
| OPC UA | endpoint, security mode and policy, identity, certificates, publishing/sampling intervals | NodeId and conversion | discover endpoints, browse nodes, and read current values |
| Modbus TCP | host, port, Unit ID, zero/one-based addressing, polling delay | area, address, type, byte order, word order | test only configured addresses; never blind-scan registers |
| Mitsubishi MC 1E | PLC address, open port, binary/ASCII data code, target station, monitoring timer | device type, number, type, text length | test only configured devices; never blind-scan the PLC |

Connection and point interpretation are separate layers. A profile cannot be published until a real connection and every required mapping have been validated.

## FX3U connection example

FX3U is one supported field-controller example, using the Edge MELSEC A1E runner. It is not the product boundary. This scenario includes:

- PLC address and port;
- A-compatible 1E framing, TCP transport, and the binary/ASCII data code matching the PLC open settings;
- target station and monitoring timer;
- poll interval;
- production start/end state and an optional controller cycle counter;
- recipe registers;
- temperature, pressure, position, and other process points;
- device type, address, data type, scaling, and unit.

Do not put equipment addresses in research projects. Equipment profiles own addresses; research variables reference stable business codes.

## Cycle correlation

Platform generates a RunKey such as:

```text
bo-7d2f6a3e1c2d-01
```

The PLC does not need to provide a `CorrelationId`. Edge generates one when the production state changes from stopped to active and closes that cycle when the state returns to stopped. This internal identifier joins cycle start, process samples, phase changes, recipe snapshots, and cycle completion events.

Controller cycle counters, workpiece IDs, work orders, and batch IDs are collected as business context such as `source_cycle_no` or `workpiece_id`; they do not masquerade as `CorrelationId`. MES, barcode, or inspection records can use that context to associate the cycle with an experiment `RunKey`.

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
