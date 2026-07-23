# Optical Glass Molding Multi-Protocol Device Simulator

This tool simulates an optical glass precision molding press controlled by a Siemens S7-1500 architecture. The same device state is exposed over HTTP, MQTT, OPC UA, and Modbus TCP. Ingot switches sources by publishing a new acquisition-task version; the process model and connector code remain unchanged.

## Simulated data

- A ten-minute logical cycle sampled once per logical second across five controller steps;
- The 13 sensor signals supplied for the optical molding scenario;
- Integer, Boolean, and string operating states;
- 31 resolved recipe parameters;
- Cycle, workpiece, product-series, recipe-version, and controller-step context;
- One in-memory state shared by all four protocol servers.

By default, one logical second advances every 1000 milliseconds: the simulator produces one sensor value group per second and completes a 10-minute cycle in real time. Automated tests can opt into `SIMULATOR_TICK_MS=100`; accelerated data is intended for protocol and acquisition verification, not long-running production-like operation.

## Run

```powershell
npm install
npm start
npm run smoke
```

| Protocol | Endpoint |
|---|---|
| HTTP | `http://127.0.0.1:8101/api/v1/snapshot` |
| MQTT 3.1.1 | `mqtt://127.0.0.1:1883`, topic `ingot/simulator/optical-molding/telemetry` |
| OPC UA | `opc.tcp://127.0.0.1:4840/UA/IngotOpticalMolding` |
| Modbus TCP | `127.0.0.1:1502`, unit ID `1` |

## Register and switch

The first command registers process model v2, recipe, and analysis plan. Every later command publishes a new version of the same acquisition task and retires the previous version automatically.

```powershell
node register-platform.mjs --protocol=http-polling
node register-platform.mjs --protocol=mqtt
node register-platform.mjs --protocol=opc-ua
node register-platform.mjs --protocol=modbus-tcp
```

Use `--api=http://host:port` and `--edge=edge-id` when needed. Connections, selectors, context, recipe, lifecycle boundaries, scaling, and register byte/word order all live in the versioned acquisition configuration.

## Open-source dependencies

- Aedes 1.1.1, MIT, MQTT broker;
- MQTT.js 5.15.2, MIT, simulator publisher;
- node-opcua 2.175.2, MIT, OPC UA server;
- modbus-serial 8.0.25, ISC, Modbus TCP server.

Versions are locked in `package-lock.json`; the sample is expected to pass `npm install` with zero known vulnerabilities.
