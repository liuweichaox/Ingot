# Reference design for site acquisition data

> Document status: **Current model explanation and optional design reference**. This page first explains Ingot's existing sample tables, then presents optional `site_` table designs for adapters. The reference tables are not tables created by Ingot, mandatory deployment requirements, or equipment compatibility claims. See [Data integration](data-connection.en.md) for the actual intake contract and [Data model overview](data-model.en.md) for formal relational tables.

## Objective and boundary

Field equipment, existing collectors, or business systems may use their own database or generate events directly. The adapter reads and converts data, identifies runs, retains provenance, and sends standard events to Edge. Edge receives and persists events locally and delivers them to Platform. An adapter database is distinct from the Edge event log and Platform's formal business database.

The design should answer who read what, when, and from which source; how values and units were converted; whether the data belongs to a real run; and how subsequent inspections can be reliably linked. A site does not need every reference table, but planned settings must not be represented as actual readbacks, and run-to-inspection links must not be inferred from timestamp proximity alone.

## Data flow and responsibilities

```text
Equipment / existing collectors / inspection sources
  → Site adapter: source records, unit conversion, run identification, delivery status
  → Edge: authentication, event log, delivery, and failure isolation
  → Platform: formal run facts, process samples, and quality inspections
  → Admission checks → analysis and recommendations
```

The `site_` structures below describe only information an adapter might maintain locally. Edge accepting an event does not mean Platform has received it; linking an inspection does not mean it has passed review or optimization admission. Equipment control, PLC heartbeats, and writes remain the responsibility of the site system; these tables do not grant Ingot equipment-control capability.

## Existing Ingot sample tables

The following three tables are **implemented in Ingot Platform** and are distinct from the optional `site_` reference tables below. Together they describe which signals a device at a site reported at a given time and, when known, for which run:

- `collection_points` is the point catalogue. Each point records site, Edge, subject, and signal code and is identified by `point_key`; different machines may have different point sets.
- `process_sample_frames` stores the sample frame, including `frame_id`, `occurred_at`, `site_id`, `edge_id`, subject, and optional `execution_id`. Each `process.sample` event admitted by Platform forms one frame.
- `process_sample_values` stores each value's `frame_id`, `occurred_at`, point's `point_key`, quality information, and one typed value. Join a value to its frame using both `frame_id` and `occurred_at`, then join the point catalogue by `point_key`.

For example, run `RUN-001` uses recipe `R001/v1` and equipment `PRESS-01` has ten sensors. Only two sensors and two frames are shown below; times and values are illustrative.

```text
collection_points (point catalogue)
point_key  site_id  subject_id  signal_code
101        S01      PRESS-01    temperature
102        S01      PRESS-01    pressure
…          …        …           …

process_sample_frames (sample frames)
frame_id  occurred_at              site_id  subject_id  execution_id
1001      2026-01-01T02:00:00.000Z S01      PRESS-01    RUN-001
1002      2026-01-01T02:00:00.100Z S01      PRESS-01    RUN-001

process_sample_values (numeric values shown)
frame_id  occurred_at              point_key  numeric_value
1001      2026-01-01T02:00:00.000Z 101        178.5
1001      2026-01-01T02:00:00.000Z 102        10.8
1002      2026-01-01T02:00:00.100Z 101        178.6
1002      2026-01-01T02:00:00.100Z 102        10.9
```

If the adapter groups ten valid values from each read into a frame, sends ten frames per second for ten seconds, and all pass admission, the result is about 100 frames and 1,000 value rows. Actual counts depend on real reads and receipts; missing values must not be fabricated as valid values, and different sampling times must not be presented as simultaneous.

The five recipe **settings** are not copied into every sample value: the recipe version and settings are stored in the version record in `process_specification_versions`, while parameter definitions and units come from the associated process data model. Sample frames relate to run facts by `execution_id`; recipe identity must be verified against run context or a recipe application event. `process_sample_frames` has no dedicated recipe identifier column. Equipment **actual parameter readbacks** also need provenance and must not be substituted with setpoints. Real run records and quality inspections must be reliably associated before further analysis.

For example, one `process_specification_versions` row for `R001/v1` contains the five settings below in its `payload`. This is a fragment, not the entire row or a complete API request:

```json
{
  "values": [
    { "code": "temperature_set", "value": 180 },
    { "code": "pressure_set", "value": 12 },
    { "code": "hold_time", "value": 30 },
    { "code": "speed_set", "value": 20 },
    { "code": "position_set", "value": 50 }
  ]
}
```

See the [Data Model Overview](data-model.en.md) for current table responsibilities; [database migrations](../src/platform/Ingot.Platform.Infrastructure/Migrations/sql) define the actual columns and their evolution. Equipment and points can be displayed as a site, equipment, and signal tree, but this does not make the current database IoTDB or change the existing intake contract.

## Type and key conventions

Field types are database-independent logical types: `id` is a stable string, `instant` is a timezone-aware point in time, `number` is a numeric type suited to the range and precision, and `json` is structured data. Deployers select concrete types for their database. “Required” below is a recommendation for this reference design, not a requirement that Platform or every site source uses the same field. Store times in UTC where possible and retain the source timezone or offset; preserve unknown source times as unknown rather than disguising receipt time as source time.

Points, equipment, and runs need stable identities within a site; include `site_id` in queries and uniqueness constraints across factories. One run can have many samples, parameter readbacks, and inspection records. If the source has no reliable event sequence, allocate and persist a stable identifier for each received fact in a local transaction. Do not assume observations from the same equipment at the same timestamp are duplicates.

## Reference tables

### Point definition `site_point_definition`

When the site already has point configuration, reuse it after checking its actual semantics; Ingot does not require this table. For example, DataAcquisition JSON already contains channels, registers, data types, byte offsets, and conversion expressions. The adapter must still verify units, stable process codes, run associations, and provenance; these fields alone do not satisfy the Ingot event contract. The configuration does not automatically become Platform points or events.

One row represents a version of a site point mapping. It explains the source address, unit, and conversion; Ingot is not required to store or use site protocol addresses.

| Field | Logical type | Recommendation | Meaning |
| --- | --- | --- | --- |
| `site_id`, `point_id`, `version` | id, id, integer | Required; composite unique | Stable point identity and version within the factory. |
| `equipment_id`, `source_ref` | id, string | Required | Equipment identity and site source reference, such as an address, tag, or existing system field. |
| `metric_code`, `value_type` | id, string | Required | Code and data type in the corresponding published process data model. |
| `source_unit`, `target_unit` | string | As appropriate | Original and standard units; distinguish dimensionless values from unknown units. |
| `conversion_ref`, `effective_from`, `effective_to` | string, instant, instant | Optional | Versioned conversion rule and effective period; an unknown end may be null. |

Consider queries by `(site_id, equipment_id, metric_code, effective_from)` and prevent indistinguishable overlapping mapping versions within the same applicability scope. Create a new version when addresses or conversion rules change; older records retain the version actually used.

Example, showing all ten point definitions for one machine. Each row also includes `site_id=S01`, `equipment_id=PRESS-01`, `version=1`, `value_type`, and `source_ref`, omitted here. The site's `point_id` and Platform's `point_key` are different identifiers and must be mapped by the adapter.

```text
point_id  metric_code   target_unit
P01       temperature   °C
P02       pressure      MPa
P03       position      mm
P04       speed         mm/s
P05       vacuum        kPa
P06       current_1     A
P07       current_2     A
P08       voltage_1     V
P09       voltage_2     V
P10       temperature_2 °C
```

### Run `site_run`

One row represents an identifiable actual run, not a recipe recommendation or planned work order.

| Field | Logical type | Recommendation | Meaning |
| --- | --- | --- | --- |
| `site_id`, `execution_id` | id, id | Required; composite unique | Factory and stable run identity, emitted as the event's `ExecutionId`. |
| `equipment_id`, `source_run_ref` | id, string | Required; optional | Actual equipment and source-side run number. A source number must not substitute for an unverified global run identity. |
| `started_at`, `completed_at` | instant | Optional | Source-confirmed start and completion times; null when not yet occurred or unknown. |
| `boundary_status`, `context` | string, json | Required; optional | Boundary completeness and traceable context such as product, recipe version, and lot. |

Consider lookup by `(site_id, equipment_id, started_at)`; run identity must be recoverable across restarts. Mark a run incomplete and retain evidence when its start event is missing, signals overlap, or identity conflicts occur. Do not fabricate complete boundaries.

Example, showing selected fields for one ten-second run; recipe identity is shown in `context` as an illustrative site convention.

```text
site_id  execution_id  equipment_id  started_at                completed_at              boundary_status  context
S01      RUN-001       PRESS-01      2026-01-01T02:00:00Z      2026-01-01T02:00:10Z    complete         {"process_specification_id":"R001","process_specification_version":"1"}
```

### Raw sample `site_sample`

One row represents one point value in a source observation. High-frequency sites may instead use batches or columnar storage, provided the same source, time, point, quality, and version can be recovered.

| Field | Logical type | Recommendation | Meaning |
| --- | --- | --- | --- |
| `site_id`, `sample_id` | id, id | Required; composite unique | Stable local sample identity. |
| `equipment_id`, `point_id`, `point_version` | id, id, integer | Required | Actual equipment and point mapping version used. |
| `execution_id` | id | Optional | Run linked after reliable identification; null if unknown, not guessed from nearby timestamps. |
| `occurred_at`, `received_at` | instant | First may be null if unknown; second required | Equipment occurrence time and adapter receipt time, with their respective time sources retained. |
| `source_sequence`, `raw_value`, `source_unit` | string, json, string | Optional | Source sequence, raw value, and unit; unsupported types must not silently become numeric values. |
| `converted_value`, `target_unit`, `quality` | json, string, string | As provided | Converted value, standard unit, and traceable quality state; unknown quality is not good quality. |

Consider queries by `(site_id, equipment_id, occurred_at)` and `(site_id, execution_id, occurred_at)` and size partitions and retention for actual throughput. `sample_id` distinguishes separate observations at the same instant. Before deduplicating by source sequence, verify its scope, rollover, and behavior after equipment restarts. Raw and converted values should remain traceable; corrections create a new version or explicit correction record.

Example, showing two points in each of two 10 Hz frames. Other points and fields are omitted. These example values require no conversion.

```text
site_id  sample_id  execution_id  equipment_id  point_id  point_version  occurred_at               raw_value  converted_value  target_unit  quality
S01      SAMP-001   RUN-001       PRESS-01      P01       1              2026-01-01T02:00:00.000Z  178.5      178.5            °C           good
S01      SAMP-002   RUN-001       PRESS-01      P02       1              2026-01-01T02:00:00.000Z  10.8       10.8             MPa          good
S01      SAMP-003   RUN-001       PRESS-01      P01       1              2026-01-01T02:00:00.100Z  178.6      178.6            °C           good
S01      SAMP-004   RUN-001       PRESS-01      P02       1              2026-01-01T02:00:00.100Z  10.9       10.9             MPa          good
```

Each row also retains its required `received_at`; it is omitted from the display above. One valid value per point at ten times per second for ten seconds means approximately 1,000 rows for all ten points.

### Actual parameter `site_parameter_readback`

One row represents an actual readback or explicit source result for one parameter in a run. It may be combined with the sample table if needed, but its semantics must remain distinguishable.

| Field | Logical type | Recommendation | Meaning |
| --- | --- | --- | --- |
| `site_id`, `readback_id`, `execution_id` | id | Required; first two composite unique | Record identity and associated real run. |
| `parameter_code`, `actual_value`, `unit` | id, json, string | Required; unit per parameter definition | Process-variable code, actual value, and unit. |
| `observed_at`, `source_ref`, `quality` | instant, string, string | As provided | Readback time, provenance, and quality; show missing when a readback cannot be verified. |
| `recipe_ref`, `recipe_version` | id, integer | Optional | Traceable recipe reference; store recipe settings separately from actual readbacks. |

Consider lookup by `(site_id, execution_id, parameter_code, observed_at)`. The same parameter may be read back more than once; do not overwrite it without a defined business rule. Adapters should follow the published site process-variable definitions.

Example, showing five actual readbacks and selected fields. These are not the setpoints in `R001/v1`.

```text
site_id  readback_id  execution_id  parameter_code  actual_value  unit  observed_at                source_ref  quality  recipe_ref  recipe_version
S01      RB-001       RUN-001       temperature_set 179.8         °C    2026-01-01T02:00:00Z       readback    good     R001        1
S01      RB-002       RUN-001       pressure_set    11.9          MPa   2026-01-01T02:00:00Z       readback    good     R001        1
S01      RB-003       RUN-001       hold_time       30            s     2026-01-01T02:00:00Z       readback    good     R001        1
S01      RB-004       RUN-001       speed_set       20            mm/s  2026-01-01T02:00:00Z       readback    good     R001        1
S01      RB-005       RUN-001       position_set    50            mm    2026-01-01T02:00:00Z       readback    good     R001        1
```

### Inspection link `site_quality_link`

One row represents a reviewable association between a source inspection or sample and a run. It is not a Platform inspection record and does not replace inspection, review, or a quality plan.

| Field | Logical type | Recommendation | Meaning |
| --- | --- | --- | --- |
| `site_id`, `link_id`, `execution_id` | id | Required; first two composite unique | Link identity and verified run. |
| `quality_source`, `quality_record_ref` | string | Required | Inspection system and original inspection record or sample reference. |
| `method`, `linked_at`, `review_status` | string, instant, string | Required | Link basis, time, and review state; ambiguities require human resolution. |

Consider lookup by `(site_id, execution_id)` and prevent duplicate links according to source identity. Register and review the inspection result through Ingot's inspection workflow. A site table's `review_status` is not proof that Ingot review has been completed.

Example, assuming a site has verified that its inspection record belongs to this run. This row is not an Ingot inspection record or approval.

```text
site_id  link_id  execution_id  quality_source  quality_record_ref  method                linked_at                 review_status
S01      QL-001   RUN-001       lab-system      INSPECT-001         barcode               2026-01-01T02:05:00Z      reviewed
```

### Adapter delivery record `site_event_outbox`

One row represents an immutable event prepared for submission to Edge. This table is optional if the site already has a reliable delivery component.

| Field | Logical type | Recommendation | Meaning |
| --- | --- | --- | --- |
| `event_id` | UUIDv7 | Required; unique | Standard event identity, persisted before the first send and reused on retry. |
| `site_id`, `execution_id` | id | Required; optional | Site ownership and confirmed run identity when available. |
| `event_type`, `payload`, `created_at` | string, json, instant | Required | Type, frozen event content, and creation time. |
| `delivery_state`, `attempt_count`, `last_error` | string, integer, string | Required; required; optional | Local delivery state, attempt count, and error detail; do not include credentials. |
| `edge_accepted_at`, `edge_seq` | instant, integer | Optional | Edge acceptance time and returned sequence; neither proves Platform receipt. |

Consider scanning pending events by `(delivery_state, created_at)`. Commit business facts and outbox records in one reliable transaction; retain the original ID and content after delivery failure. For events still in its local log, Edge returns the original sequence on identical retries and returns 409 for conflicting content. Reconcile resends beyond local retention and drops when the queue is full; do not assume permanent end-to-end idempotency. See [Data integration](data-connection.en.md#delivery-and-failure-handling).

Example, showing one pending first-frame event and selected fields. The `payload` shown is an excerpt, not a complete request that can be submitted as-is.

```text
event_id                              site_id  execution_id  event_type                 created_at                delivery_state  attempt_count  payload
019b76da-a800-7000-8000-000000000001  S01      RUN-001       process.sample             2026-01-01T02:00:00Z      pending         0              {"data":{"values":{"temperature":178.5,"pressure":10.8}}}
```

An accepted Edge sequence only confirms local acceptance; it does not confirm Platform admission. In a real adapter, persist the complete event and the same ID before its first send, and reuse both for an identical retry.

## Mapping to Ingot events

| Site reference information | Ingot intake meaning | Boundary |
| --- | --- | --- |
| Run identity, equipment, and time | `ExecutionId`, `Subject`, and `OccurredAt` | Use a consistent identity for the same real execution; do not replace occurrence time with replay time. |
| Point code and converted value | `data.values` in `process.sample` | Keys and types must match a resolvable published process data model; required items must be complete. |
| Event identity and source | `EventId`, `EventType`, and `Source` | `EventId` is UUIDv7; Edge normalizes source and sets recording time, sequence, and hash. |
| Actual parameters and process context | Run evidence and event context | Provenance must be explicit and verifiable; settings do not replace actual readbacks. |
| External inspection reference | Reliable association with an Ingot inspection record | Quality plan, review, and analysis admission still apply; a reference alone is not valid inspection evidence. |

This table is not a complete JSON example. Event required fields, the batch interface, and source constraints are defined by [Data integration](data-connection.en.md) and the code contract. The [external adapter example](../examples/external-adapter/README.md) verifies basic event intake.

JSON submissions must still include the contract-required `RecordedAt`, even though Edge overwrites it on receipt. Run start and completion use `process.execution.started` and `process.execution.completed` respectively; the external adapter creates these events from actual signals. Process-sample context can include `data_model_id` / `data_model_version` or `process_specification_id` / `process_specification_version`, but these identifiers alone do not guarantee admission: Platform must still resolve an applicable published model and analysis plan. Verify non-null values and quality separately; a field's presence does not establish a valid measurement.

Provide sample quality according to the current event contract. Platform can recognize versioned quality information when `data.quality.qualityVersion=1` and `valueFlags` is an object. Missing or unrecognized versioned quality is treated as uncertain, but a point without its own quality mark may be treated as good when versioned quality is present. Never fabricate an empty `valueFlags` object for unknown quality: explicitly provide an uncertain quality code or flag for each unknown point and verify Platform's actual projection. Null values and source evidence still affect the result. `quality` in a site reference table is a suggested field and is not automatically converted into a Platform quality fact.

## Implementation and acceptance

1. Confirm the identity sources for factory, equipment, run, point, and inspection; record authorization boundaries, field dictionaries, and units.
2. Use site protocol information or an existing collector to determine actual sampling, trigger, time, and quality semantics. Adapt the reference tables as needed without relying on unverified equipment assumptions.
3. Define codes, types, and units against the published process model; retain mapping and conversion versions, and distinguish settings, actual readbacks, and results.
4. In a test environment, verify valid and missing values, duplicates, conflicts, equipment restarts, outage retries, full queues, and clock offsets; check Edge acceptance and Platform receipt separately.
5. Use authorized real runs and inspections to confirm association and review, then check data admission, analytical traceability, and site safety. Record adapter version, evidence, and limitations.

The site defines and rehearses retention, capacity, access control, backup and recovery, and correction policies according to its needs. This reference design alone proves neither compatibility, safety, nor process benefit.
