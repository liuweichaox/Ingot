import ModbusRTU from "modbus-serial";
import mqtt from "mqtt";
import {
  AttributeIds,
  MessageSecurityMode,
  OPCUAClient,
  SecurityPolicy,
} from "node-opcua";

const httpSnapshot = await fetch("http://127.0.0.1:8101/api/v1/snapshot").then((response) => {
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  return response.json();
});

const mqttSnapshot = await new Promise((resolve, reject) => {
  const client = mqtt.connect("mqtt://127.0.0.1:1883", { protocolVersion: 4 });
  const timer = setTimeout(() => reject(new Error("MQTT timeout")), 5000);
  client.on("connect", () => client.subscribe("ingot/simulator/optical-molding/telemetry"));
  client.on("message", (_topic, payload) => {
    clearTimeout(timer);
    const value = JSON.parse(payload.toString("utf8"));
    client.end(true);
    resolve(value);
  });
  client.on("error", reject);
});

const opcClient = OPCUAClient.create({
  endpointMustExist: false,
  securityMode: MessageSecurityMode.None,
  securityPolicy: SecurityPolicy.None,
});
await opcClient.connect("opc.tcp://127.0.0.1:4840/UA/IngotOpticalMolding");
const opcSession = await opcClient.createSession();
const opcValues = await opcSession.read(
  [
    "ns=1;s=Telemetry.upper_mold.ir_temperature",
    "ns=1;s=Context.CorrelationId",
    "ns=1;s=Context.RecipeStep",
  ].map((nodeId) => ({ nodeId, attributeId: AttributeIds.Value })),
  0,
);
await opcSession.close();
await opcClient.disconnect();

const modbus = new ModbusRTU();
await modbus.connectTCP("127.0.0.1", { port: 1502 });
modbus.setID(1);
const modbusTemperature = await modbus.readHoldingRegisters(0, 2);
const modbusCycle = await modbus.readHoldingRegisters(100, 12);
modbus.close();
const temperatureBytes = Buffer.alloc(4);
temperatureBytes.writeUInt16BE(modbusTemperature.data[0], 0);
temperatureBytes.writeUInt16BE(modbusTemperature.data[1], 2);
const cycleBytes = Buffer.alloc(24);
modbusCycle.data.forEach((value, index) => cycleBytes.writeUInt16BE(value, index * 2));

const result = {
  http: {
    cycleId: httpSnapshot.cycle.id,
    sensorCount: Object.keys(httpSnapshot.sensors).length,
    upperTemperature: httpSnapshot.sensors.upper_mold_ir_temperature,
  },
  mqtt: {
    cycleId: mqttSnapshot.cycle.id,
    sensorCount: Object.keys(mqttSnapshot.sensors).length,
    upperTemperature: mqttSnapshot.sensors.upper_mold_ir_temperature,
  },
  opcUa: {
    upperTemperature: opcValues[0].value.value,
    cycleId: opcValues[1].value.value,
    recipeStep: opcValues[2].value.value,
  },
  modbusTcp: {
    upperTemperature: temperatureBytes.readFloatBE(0),
    cycleId: cycleBytes.toString("utf8").replace(/\0+$/, ""),
  },
};
if (Object.values(result).some((item) => !item.cycleId && item !== result.opcUa))
  throw new Error("Protocol smoke test did not read a cycle identifier.");
console.log(JSON.stringify(result, null, 2));
