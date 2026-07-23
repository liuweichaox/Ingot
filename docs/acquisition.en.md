# Configurable acquisition

Ingot models acquisition as versioned tasks instead of embedding equipment protocols in the process model. A published task is loaded and executed by its assigned edge node. The edge outbox handles disconnected delivery, while the platform retains task versions, runtime status, and normalized events.

## Configuration boundary

- The process data model defines stable data items, target types, and units.
- An acquisition task defines protocol connectivity, credential references, sampling behavior, point mappings, scale/offset, data subject, and event source.
- Secret values are never stored in the platform database. The UI stores only an `env:VARIABLE_NAME` reference whose value is provisioned on the assigned edge node.
- A task uses one protocol. Multiple tasks can connect telemetry, recipes, or other feeds from one device to the same data subject.
- HTTP, MQTT, and Modbus TCP can use source or edge-received timestamps; Modbus source time uses a configurable register selector. OPC UA uses node source timestamps.
- Continuous equipment has no lifecycle mapping. Discrete equipment configures correlation, step keys, and boundary event types. A correlation change emits `cycle.completed` / `cycle.started`; a step change emits `recipe.step_changed`.
- All four protocols support operating-context and recipe mapping. HTTP/MQTT use JSON paths, OPC UA uses NodeIds, and Modbus scalar context uses `area:address:type` selectors.

## Protocols

| Protocol | Execution | Configurable connection | Point selector |
| --- | --- | --- | --- |
| HTTP polling | Reads a JSON snapshot on an interval | URL, path, interval, timeout, reconnect | JSON field path |
| MQTT | Emits one value group per subscribed message | Broker, port, 3.1.1/5.0, client ID, user credentials, TLS, topics, QoS, keepalive, and session | JSON field path |
| OPC UA | Subscribes to nodes and groups values by publishing interval | Endpoint, security mode/policy, anonymous/user/certificate identity, certificate, publishing and sampling intervals | NodeId |
| Modbus TCP | Coalesces points by register area, batch-reads them, and emits one sample group | Host, port, Unit ID, interval, timeout, reconnect | Area, address, source type, quantity, byte order, and word order |

All four protocols emit the same `ProductionEvent`. Scale and offset are deterministic acquisition conversions only; process meaning, quality labels, and analysis selections remain separate first-class configuration and records.

The runnable optical glass molding sample is in `tools/Ingot.OpticalMoldingSimulator`. One device state serves all four protocols, and publishing a new version of the same acquisition task demonstrates source switching.

## Relationship to TDengine

The product flow follows the proven separation of data source/task/agent, protocol connectivity, point selection, mapping, and runtime status found in TDengine. Ingot does not copy its entity definitions. Ingot instead targets normalized events and manufacturing context for process analysis, with disconnected delivery handled by the edge outbox.
