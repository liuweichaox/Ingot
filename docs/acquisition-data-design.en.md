# Reference design for site acquisition data

> Document status: **Current model explanation and design reference**. This page explains Ingot's generic sample tables and provides optional local `site_` table designs for field adapters. See [Data integration](data-connection.en.md) for the intake contract and [Data model overview](data-model.en.md) for Platform's formal relations.

## Objective and boundary

Field equipment, existing collectors, or business systems may use their own database or generate events directly. The adapter reads and converts data, identifies runs, retains provenance, and sends standard events to Edge. Edge uses SQLite to persist and deliver events, while Platform uses PostgreSQL for formal business data.

The design records source identity, read time, value and unit conversion, actual run ownership, and inspection links. Sites select reference tables as needed; planned settings and actual readbacks are stored separately, and runs link to inspections through verifiable identities.

## Data flow and responsibilities

```text
Equipment / existing collectors / inspection sources
  → Site adapter: source records, unit conversion, run identification, delivery status
  → Edge: authentication, event log, delivery, and failure isolation
  → Platform: formal run facts, process samples, and quality inspections
  → Admission checks → analysis and recommendations
```

The `site_` structures below show local information a field adapter can maintain. Edge uses its SQLite event log to persist and deliver events, while Platform uses PostgreSQL sample tables for formal process-data projection. Adapters track Edge receipt, Platform receipt, inspection review, and optimization admission as separate states. Equipment control, PLC heartbeats, and writes belong to the site system.

## Existing Ingot sample tables

The following three tables are **implemented in Ingot Platform** and store formal sample facts received by Platform. The `site_` tables below describe local storage for field adapters. Together the Platform tables describe which signals a device at a site reported at a given time and, when known, for which run:

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

An adapter can group ten valid values from one read into a frame. At ten frames per second for ten seconds, with every frame admitted, the result is about 100 frames and 1,000 value rows; actual counts follow the recorded reads and receipts. Each frame retains its actual sampling time and valid values.

The five recipe **settings** are stored on the relevant `process_specification_versions` record, while parameter definitions and units come from the associated process data model. Sample frames link to run facts through `execution_id`; recipe identity comes from run context or a recipe-application event. Equipment **actual parameter readbacks** store their own source and value. Real run records link to quality inspections before further analysis.

For example, the `process_specification_versions` record for `R001/v1` stores these five settings in its `payload`. Parameter units and bounds come from the associated process data model.

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

See the [Data Model Overview](data-model.en.md) for formal table responsibilities. [Database migrations](../src/platform/Ingot.Platform.Infrastructure/Migrations/sql) define the actual columns. Sites can organize points as a tree by site, equipment, and signal.

## Type and key conventions

Field types are database-independent logical types: `id` is a stable string, `instant` is a timezone-aware point in time, `number` is a numeric type suited to the range and precision, and `json` is structured data. Deployers select concrete types for their database. “Required” below is a recommendation for this local reference design; configure fields according to the site source and event contract. Store times in UTC and retain the source timezone or offset; preserve unknown source times while recording receipt time separately.

Points, equipment, and runs use stable identities within a site; include `site_id` in queries and uniqueness constraints across factories. One run can have many samples, parameter readbacks, and inspection records. When a source has no reliable event sequence, allocate and persist a stable identifier for each received fact in a local transaction; use source sequence and fact identity to distinguish observations from the same equipment at the same timestamp.

## Reference tables

### Point definition `site_point_definition`

When the site already has point configuration, reuse it after checking units, process codes, run associations, and provenance. For example, DataAcquisition JSON already contains channels, registers, data types, byte offsets, and conversion expressions. The adapter validates those semantics against the event contract and maps them to Platform points and events.

One row represents a version of a site point mapping and records its source address, unit, and conversion rule. The adapter uses this mapping to produce stable process codes.

| Field | Logical type | Recommendation | Meaning |
| --- | --- | --- | --- |
| `site_id`, `point_id`, `version` | id, id, integer | Required; composite unique | Stable point identity and version within the factory. |
| `equipment_id`, `source_ref` | id, string | Required | Equipment identity and site source reference, such as an address, tag, or existing system field. |
| `metric_code`, `value_type` | id, string | Required | Code and data type in the corresponding published process data model. |
| `source_unit`, `target_unit` | string | As appropriate | Original and standard units; distinguish dimensionless values from unknown units. |
| `conversion_ref`, `effective_from`, `effective_to` | string, instant, instant | Optional | Versioned conversion rule and effective period; an unknown end may be null. |

Consider queries by `(site_id, equipment_id, metric_code, effective_from)` and prevent indistinguishable overlapping mapping versions within the same applicability scope. Create a new version when addresses or conversion rules change; older records retain the version actually used.

Example, showing all ten point definitions for one machine. Each row also includes `site_id=S01`, `equipment_id=PRESS-01`, `version=1`, `value_type`, and `source_ref`, omitted here. The adapter maps the site's `point_id` to Platform's `point_key`.

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

One row represents an identifiable actual run.

| Field | Logical type | Recommendation | Meaning |
| --- | --- | --- | --- |
| `site_id`, `execution_id` | id, id | Required; composite unique | Factory and stable run identity, emitted as the event's `ExecutionId`. |
| `equipment_id`, `source_run_ref` | id, string | Required; optional | Actual equipment and source-side run number, mapped to the stable global run identity after source validation. |
| `started_at`, `completed_at` | instant | Optional | Source-confirmed start and completion times; unknown times are null. |
| `boundary_status`, `context` | string, json | Required; optional | Boundary completeness and traceable context such as product, recipe version, and lot. |

Use `(site_id, equipment_id, started_at)` for lookup and preserve run identity across restarts. When start events are missing, signals overlap, or identities conflict, record the boundary status from available evidence and retain the related evidence.

Example, showing selected fields for one ten-second run; recipe identity is shown in `context` as an illustrative site convention.

```text
site_id  execution_id  equipment_id  started_at                completed_at              boundary_status  context
S01      RUN-001       PRESS-01      2026-01-01T02:00:00Z      2026-01-01T02:00:10Z    complete         {"process_specification_id":"R001","process_specification_version":"1"}
```

### Raw sample `site_sample`

One row represents one point value in a source observation. High-frequency sites can use batches or columnar storage while retaining source, time, point, quality, and version.

| Field | Logical type | Recommendation | Meaning |
| --- | --- | --- | --- |
| `site_id`, `sample_id` | id, id | Required; composite unique | Stable local sample identity. |
| `equipment_id`, `point_id`, `point_version` | id, id, integer | Required | Actual equipment and point mapping version used. |
| `execution_id` | id | Optional | Run identity from a reliable source; keep null while the association is being verified. |
| `occurred_at`, `received_at` | instant | First may be null if unknown; second required | Equipment occurrence time and adapter receipt time, with their respective time sources retained. |
| `source_sequence`, `raw_value`, `source_unit` | string, json, string | Optional | Source sequence, raw value, and unit, retained in their source types. |
| `converted_value`, `target_unit`, `quality` | json, string, string | As provided | Converted value, standard unit, and traceable quality state; unknown quality uses the uncertain state. |

Use `(site_id, equipment_id, occurred_at)` and `(site_id, execution_id, occurred_at)` for queries, and size partitions and retention for actual throughput. `sample_id` distinguishes observations at the same instant. Apply source-sequence deduplication according to its scope, rollover, and restart rules. Keep raw and converted values traceable; corrections create a new version or explicit correction record.

Example, showing two points in each of two 10 Hz frames. The remaining eight points in each frame use the same structure. These example values require no conversion.

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
| `observed_at`, `source_ref`, `quality` | instant, string, string | As provided | Readback time, provenance, and quality; record missing when the source has not provided or verified a readback. |
| `recipe_ref`, `recipe_version` | id, integer | Optional | Traceable recipe reference; store recipe settings separately from actual readbacks. |

Use `(site_id, execution_id, parameter_code, observed_at)` for lookup. Store each readback of the same parameter by its observation time, and apply a defined business rule when presenting a current value. Adapters follow the published site process-variable definitions.

Example, showing five actual readbacks and selected fields, stored separately from the setpoints in `R001/v1`.

```text
site_id  readback_id  execution_id  parameter_code  actual_value  unit  observed_at                source_ref  quality  recipe_ref  recipe_version
S01      RB-001       RUN-001       temperature_set 179.8         °C    2026-01-01T02:00:00Z       readback    good     R001        1
S01      RB-002       RUN-001       pressure_set    11.9          MPa   2026-01-01T02:00:00Z       readback    good     R001        1
S01      RB-003       RUN-001       hold_time       30            s     2026-01-01T02:00:00Z       readback    good     R001        1
S01      RB-004       RUN-001       speed_set       20            mm/s  2026-01-01T02:00:00Z       readback    good     R001        1
S01      RB-005       RUN-001       position_set    50            mm    2026-01-01T02:00:00Z       readback    good     R001        1
```

### Inspection link `site_quality_link`

One row represents a reviewable association between a source inspection or sample and a run. Ingot's inspection workflow manages Platform inspection records, reviews, and quality plans.

| Field | Logical type | Recommendation | Meaning |
| --- | --- | --- | --- |
| `site_id`, `link_id`, `execution_id` | id | Required; first two composite unique | Link identity and verified run. |
| `quality_source`, `quality_record_ref` | string | Required | Inspection system and original inspection record or sample reference. |
| `method`, `linked_at`, `review_status` | string, instant, string | Required | Link basis, time, and review state; ambiguities require human resolution. |

Use `(site_id, execution_id)` for lookup and constrain duplicate links by source identity. Register and review inspection results through Ingot's inspection workflow; the site `review_status` records source-side state, and Platform review state comes from Ingot records.

Example, showing a site association after verifying that the inspection record belongs to this run; Ingot then records and reviews the inspection result.

```text
site_id  link_id  execution_id  quality_source  quality_record_ref  method                linked_at                 review_status
S01      QL-001   RUN-001       lab-system      INSPECT-001         barcode               2026-01-01T02:05:00Z      reviewed
```

### Adapter delivery record `site_event_outbox`

One row represents an immutable event prepared for submission to Edge. Sites with a reliable delivery component can use its existing persistence structure.

| Field | Logical type | Recommendation | Meaning |
| --- | --- | --- | --- |
| `event_id` | UUIDv7 | Required; unique | Standard event identity, persisted before the first send and reused on retry. |
| `site_id`, `execution_id` | id | Required; optional | Site ownership and confirmed run identity when available. |
| `event_type`, `payload`, `created_at` | string, json, instant | Required | Type, frozen event content, and creation time. |
| `delivery_state`, `attempt_count`, `last_error` | string, integer, string | Required; required; optional | Local delivery state, attempt count, and redacted error detail. |
| `edge_accepted_at`, `edge_seq` | instant, integer | Optional | Edge acceptance time and returned sequence; record Platform receipt separately. |

Scan pending events by `(delivery_state, created_at)`. Commit business facts and outbox records in one reliable transaction and retain the original ID and content after delivery failure. During local retention, Edge returns the original sequence for identical retries and 409 for conflicting content. Reconcile resends beyond retention and drops when the queue is full; use at-least-once transport with idempotent business effects. See [Data integration](data-connection.en.md#delivery-and-failure-handling).

Example, showing one pending first-frame event and selected fields. Complete requests include the remaining signals and event fields required by the contract.

```text
event_id                              site_id  execution_id  event_type                 created_at                delivery_state  attempt_count  payload
019b76da-a800-7000-8000-000000000001  S01      RUN-001       process.sample             2026-01-01T02:00:00Z      pending         0              {"data":{"values":{"temperature":178.5,"pressure":10.8}}}
```

Track Edge acceptance and Platform admission as separate states. A real adapter persists the complete event and its ID before the first send, then reuses both for identical retries.

## Mapping to Ingot events

| Site reference information | Ingot intake meaning | Intake requirement |
| --- | --- | --- |
| Run identity, equipment, and time | `ExecutionId`, `Subject`, and `OccurredAt` | Use a consistent identity for each real execution and retain its occurrence time. |
| Point code and converted value | `data.values` in `process.sample` | Keys and types match a resolvable published process data model, with all required items present. |
| Event identity and source | `EventId`, `EventType`, and `Source` | `EventId` is UUIDv7; Edge normalizes source and sets recording time, sequence, and hash. |
| Actual parameters and process context | Run evidence and event context | Retain verifiable provenance; store recipe settings separately from actual readbacks. |
| External inspection reference | Reliable association with an Ingot inspection record | Register inspection results under a quality plan, complete review, and meet analysis admission. |

Event fields, the batch interface, and source constraints are defined by [Data integration](data-connection.en.md) and the code contract. Use the [external adapter example](../examples/external-adapter/README.md) to verify basic event intake.

JSON requests include the contract-required `RecordedAt`; Edge sets the platform recording time on receipt. Run start and completion use `process.execution.started` and `process.execution.completed`, created by the external adapter from actual signals. Process-sample context can include `data_model_id` / `data_model_version` or `process_specification_id` / `process_specification_version`. Platform admits samples against the applicable published model and analysis plan; adapters validate values and quality separately.

Provide sample quality according to the current event contract. Platform reads versioned quality when `data.quality.qualityVersion=1` and `valueFlags` is an object; missing or unrecognized versions are marked uncertain. Within valid versioned quality, points without a separate flag use the contract default. Provide an uncertain quality code or flag for every point with unknown quality and verify Platform's projection. Null values and provenance both affect admission. The adapter maps the site reference table's `quality` field to Platform quality facts.

## Implementation and acceptance

1. Confirm the identity sources for factory, equipment, run, point, and inspection; record authorization boundaries, field dictionaries, and units.
2. Check site protocol information or an existing collector to confirm sampling, trigger, time, and quality semantics, then adapt the reference tables accordingly.
3. Define codes, types, and units against the published process model; retain mapping and conversion versions, and distinguish settings, actual readbacks, and results.
4. In a test environment, verify valid and missing values, duplicates, conflicts, equipment restarts, outage retries, full queues, and clock offsets; check Edge acceptance and Platform receipt separately.
5. Use authorized real runs and inspections to verify associations and reviews, then accept data admission, analytical traceability, and site safety. Record the adapter version and validation evidence.

The site defines and rehearses retention, capacity, access control, backup and recovery, and correction policies according to its needs. Include compatibility, safety, and process outcomes in site acceptance evidence.
