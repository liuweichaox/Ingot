# Open-source dependencies

The Ingot device acquisition runtime adds only open-source components:

| Component | Purpose | Version | License | Upstream |
| --- | --- | --- | --- | --- |
| MQTTnet | MQTT 3.1.1/5.0 client, TLS, and subscriptions | 5.2.0.1603 | MIT | <https://github.com/dotnet/MQTTnet> |
| OPC Foundation UA .NET Standard | OPC UA discovery, sessions, client, and subscriptions | 1.5.378.156 | MIT | <https://github.com/OPCFoundation/UA-.NETStandard> |
| NModbus | Modbus TCP master | 3.0.83 | MIT | <https://github.com/NModbus/NModbus> |
| Aedes | Simulator MQTT broker | 1.1.1 | MIT | <https://github.com/moscajs/aedes> |
| MQTT.js | Simulator MQTT publisher | 5.15.2 | MIT | <https://github.com/mqttjs/MQTT.js> |
| node-opcua | Simulator OPC UA server | 2.175.2 | MIT | <https://github.com/node-opcua/node-opcua> |
| modbus-serial | Simulator Modbus TCP server | 8.0.25 | ISC | <https://github.com/yaacov/node-modbus-serial> |

Versions are pinned in the project, NuGet assets, and simulator `package-lock.json`. The protocol implementation does not depend on TDengine Enterprise connectors or closed-source SDKs.
