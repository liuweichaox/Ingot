# Configurable acquisition

Ingot models acquisition as versioned tasks instead of embedding equipment protocols in the process model. A published task is loaded and executed by its assigned edge node. The edge outbox handles disconnected delivery, while the platform retains task versions, runtime status, and normalized events.

## Configuration boundary

- The process data model defines stable data items, target types, and units.
- An acquisition task defines protocol connectivity, credential references, read pacing, point mappings, scale/offset, data subject, and event source.
- Secret values are never stored in the platform database. The UI stores only an `env:VARIABLE_NAME` reference whose value is provisioned on the assigned edge node.
- A task uses one protocol. Multiple tasks can connect telemetry, recipes, or other feeds from one device to the same data subject.
- HTTP, MQTT, and Modbus TCP can use source or edge-received timestamps; Modbus source time uses a configurable register selector. OPC UA uses node source timestamps.
- Continuous equipment has no lifecycle mapping. Discrete equipment configures correlation, step keys, and boundary event types. A correlation change emits `cycle.completed` / `cycle.started`; a step change emits `recipe.step_changed`.
- All four protocols support operating-context and recipe mapping. HTTP/MQTT use JSON paths, OPC UA uses NodeIds, and Modbus scalar context uses `area:address:type` selectors.

## Protocols

| Protocol | Execution | Configurable connection | Point selector |
| --- | --- | --- | --- |
| HTTP polling | Waits for the configured delay after each completed read before requesting the next JSON snapshot | URL, path, post-read delay, timeout, reconnect | JSON field path |
| MQTT | Emits one value group per subscribed message | Broker, port, 3.1.1/5.0, client ID, user credentials, TLS, topics, QoS, keepalive, and session | JSON field path |
| OPC UA | Subscribes to nodes and groups values by publishing interval | Endpoint, security mode/policy, anonymous/user/certificate identity, certificate, publishing and sampling intervals | NodeId |
| Modbus TCP | Coalesces points by register area and waits for the configured delay after each completed read | Host, port, Unit ID, post-read delay, timeout, reconnect | Area, address, source type, quantity, byte order, and word order |

All four protocols emit the same `ProductionEvent`. Scale and offset are deterministic acquisition conversions only; process meaning, quality labels, and analysis selections remain separate first-class configuration and records.

For HTTP and Modbus, the delay is not a fixed sample period. The observed interval includes device read time, mapping and local persistence time, plus the configured post-read delay. A task never piles up concurrent reads. Cycle completion depends only on its start and end events. Process-data usability is evaluated separately from actual timestamps, gaps, source sequence, duplicate timestamps, and signal coverage; it is never inferred from a theoretical row count.

The runnable optical glass molding sample is in `tools/Ingot.OpticalMoldingSimulator`. One device state serves all four protocols, and publishing a new version of the same acquisition task demonstrates source switching.
