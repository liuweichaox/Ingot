import { createServer as createHttpServer } from "node:http";
import { createServer as createTcpServer } from "node:net";
import { Aedes } from "aedes";
import ModbusRTU from "modbus-serial";
import mqtt from "mqtt";
import {
  DataType,
  OPCUAServer,
  StatusCodes,
  Variant,
  VariantArrayType,
} from "node-opcua";

const host = process.env.SIMULATOR_HOST || "127.0.0.1";
const httpPort = Number(process.env.SIMULATOR_HTTP_PORT || 8101);
const mqttPort = Number(process.env.SIMULATOR_MQTT_PORT || 1883);
const opcUaPort = Number(process.env.SIMULATOR_OPCUA_PORT || 4840);
const modbusPort = Number(process.env.SIMULATOR_MODBUS_PORT || 1502);
const tickMs = Math.max(100, Number(process.env.SIMULATOR_TICK_MS || 1000));
const mqttTopic = "ingot/simulator/optical-molding/telemetry";
const cycleDurationSeconds = 600;

const stages = [
  { code: "preheat", name: "预热", sourceStep: 10, start: 0, end: 90 },
  { code: "soak", name: "均热", sourceStep: 20, start: 90, end: 240 },
  { code: "press", name: "模压", sourceStep: 30, start: 240, end: 360 },
  { code: "anneal", name: "退火", sourceStep: 40, start: 360, end: 480 },
  { code: "cool", name: "冷却", sourceStep: 50, start: 480, end: 600 },
];

const recipe = {
  id: "lens-a-std",
  version: 4,
  name: "LENS-A 标准模压工艺",
  parameters: {
    heat_position: 25,
    work_position: 12.5,
    host_position: 18,
    upper_set_temperature: 620,
    lower_set_temperature: 615,
    nitrogen_fill_temperature: 480,
    preheat_soak_delay: 90,
    pressure_difference_limit: 5,
    upper_temperature_limit: 635,
    lower_temperature_limit: 630,
    pressure_limit: 135,
    work_pressure: 120,
    upper_pid_p: 2.4,
    upper_pid_i: 0.8,
    upper_pid_d: 0.12,
    lower_pid_p: 2.35,
    lower_pid_i: 0.78,
    lower_pid_d: 0.11,
    tg_temperature: 510,
    work_upper_heat_delay: 4,
    work_lower_heat_delay: 4,
    work_speed: 1.2,
    upper_hold_temperature: 620,
    lower_hold_temperature: 615,
    upper_power_off_delay: 8,
    lower_power_off_delay: 8,
    holding_pressure: 120,
    work_upper_temperature: 620,
    work_lower_temperature: 615,
    molding_temperature: 618,
    holding_speed: 0.35,
  },
};

const sensorDefinitions = [
  ["upper_mold.ir_temperature", "upper_mold_ir_temperature"],
  ["upper_mold.current", "upper_mold_current"],
  ["upper_mold.voltage", "upper_mold_voltage"],
  ["lower_mold.ir_temperature", "lower_mold_ir_temperature"],
  ["lower_mold.current", "lower_mold_current"],
  ["lower_mold.voltage", "lower_mold_voltage"],
  ["press.load", "press_load"],
  ["grating.position", "grating_position"],
  ["servo.speed", "servo_speed"],
  ["chamber.vacuum", "chamber_vacuum"],
  ["servo.position", "servo_position"],
  ["upper_mold.power", "upper_mold_power"],
  ["lower_mold.power", "lower_mold_power"],
];

let cycleNumber = 1;
let elapsedSecond = 0;
let sequence = 0;
const logicalEpoch = Date.now();
const simulatorRunId = new Date(logicalEpoch)
  .toISOString()
  .replaceAll(/[-:.]/g, "")
  .slice(2, 15);
let state = createState();
let opcVariables = [];
const holdingRegisters = new Uint16Array(400);

function round(value, digits = 3) {
  return Number(value.toFixed(digits));
}

function currentStage(second) {
  return stages.find((item) => second >= item.start && second < item.end) || stages.at(-1);
}

function temperatures(second) {
  if (second < 90) return [25 + second * 5.5, 24 + second * 5.42];
  if (second < 240) return [520 + (second - 90) * 0.66, 512 + (second - 90) * 0.65];
  if (second < 360) return [620 + Math.sin(second / 7) * 1.8, 615 + Math.sin(second / 8) * 1.6];
  if (second < 480) return [620 - (second - 360) * 1.15, 615 - (second - 360) * 1.12];
  return [482 - (second - 480) * 3.35, 481 - (second - 480) * 3.32];
}

function sensorValues(second, stage) {
  const [upperTemperature, lowerTemperature] = temperatures(second);
  const heating = stage.code === "preheat" || stage.code === "soak";
  const holding = stage.code === "press";
  const upperVoltage = heating ? 218 + Math.sin(second / 11) * 2 : holding ? 82 : 0;
  const lowerVoltage = heating ? 217 + Math.sin(second / 13) * 2 : holding ? 78 : 0;
  const upperCurrent = heating ? 7.8 + Math.sin(second / 9) * 0.4 : holding ? 2.1 : 0;
  const lowerCurrent = heating ? 7.5 + Math.sin(second / 10) * 0.4 : holding ? 2.0 : 0;
  const load = stage.code === "press" ? 120 + Math.sin(second / 6) * 1.5 : stage.code === "anneal" ? 35 : 0;
  const servoPosition = stage.code === "press" ? 12.5 : stage.code === "cool" ? 18 + (second - 480) * 0.058 : 25;
  const servoSpeed = second === 240 ? 1.2 : second === 480 ? 0.35 : 0;
  return {
    upper_mold_ir_temperature: round(upperTemperature),
    upper_mold_current: round(upperCurrent),
    upper_mold_voltage: round(upperVoltage),
    lower_mold_ir_temperature: round(lowerTemperature),
    lower_mold_current: round(lowerCurrent),
    lower_mold_voltage: round(lowerVoltage),
    press_load: round(load),
    grating_position: round(servoPosition + 0.006),
    servo_speed: round(servoSpeed),
    chamber_vacuum: round(stage.code === "cool" ? 65 : 12),
    servo_position: round(servoPosition),
    upper_mold_power: round(upperCurrent * upperVoltage),
    lower_mold_power: round(lowerCurrent * lowerVoltage),
  };
}

function createState() {
  const stage = currentStage(elapsedSecond);
  const timestamp = new Date(logicalEpoch + sequence * 1000);
  const sensors = sensorValues(elapsedSecond, stage);
  return {
    timestamp,
    sequence,
    device: {
      id: "GLASS-PRESS-01",
      name: "光学玻璃精密模压机 1#",
      type: "optical-glass-precision-molding-press",
      controller: "Siemens S7-1500",
    },
    product: { series: "LENS-A", code: "LENS-A-42" },
    cycle: {
      id: `CYCLE-${simulatorRunId}-${String(cycleNumber).padStart(4, "0")}`,
      number: cycleNumber,
      active: true,
      elapsedSeconds: elapsedSecond,
    },
    stage: {
      code: stage.code,
      name: stage.name,
      sourceStep: stage.sourceStep,
      sourceStepName: `STEP-${stage.sourceStep}`,
    },
    machine: {
      mode: "automatic",
      heatingEnabled: stage.code === "preheat" || stage.code === "soak",
      alarmCode: 0,
    },
    recipe,
    sensors,
  };
}

function snapshot() {
  return {
    timestamp: state.timestamp.toISOString(),
    sequence: state.sequence,
    device: state.device,
    product: state.product,
    cycle: state.cycle,
    stage: state.stage,
    machine: state.machine,
    recipe: state.recipe,
    sensors: state.sensors,
  };
}

function advance() {
  sequence += 1;
  elapsedSecond += 1;
  if (elapsedSecond >= cycleDurationSeconds) {
    elapsedSecond = 0;
    cycleNumber += 1;
  }
  state = createState();
  updateOpcUa();
  updateModbus();
  if (mqttClient?.connected) {
    mqttClient.publish(mqttTopic, JSON.stringify(snapshot()), { qos: 1 });
  }
}

function sendJson(response, status, value) {
  const body = JSON.stringify(value);
  response.writeHead(status, {
    "content-type": "application/json; charset=utf-8",
    "content-length": Buffer.byteLength(body),
  });
  response.end(body);
}

const httpServer = createHttpServer((request, response) => {
  if (request.method === "GET" && request.url === "/api/v1/snapshot") {
    sendJson(response, 200, snapshot());
    return;
  }
  if (request.method === "GET" && request.url === "/api/v1/status") {
    sendJson(response, 200, {
      device: state.device,
      cycle: state.cycle,
      stage: state.stage,
      tickMs,
      logicalSamplePeriodMs: 1000,
      protocols: {
        http: `http://${host}:${httpPort}/api/v1/snapshot`,
        mqtt: `mqtt://${host}:${mqttPort}/${mqttTopic}`,
        opcUa: `opc.tcp://${host}:${opcUaPort}/UA/IngotOpticalMolding`,
        modbusTcp: `${host}:${modbusPort}/unit/1`,
      },
    });
    return;
  }
  sendJson(response, 404, { error: "not_found" });
});
await new Promise((resolve) => httpServer.listen(httpPort, host, resolve));

const mqttBroker = await Aedes.createBroker();
const mqttServer = createTcpServer(mqttBroker.handle);
await new Promise((resolve) => mqttServer.listen(mqttPort, host, resolve));
const mqttClient = mqtt.connect(`mqtt://${host}:${mqttPort}`, {
  clientId: "ingot-optical-molding-simulator",
  protocolVersion: 4,
});
await new Promise((resolve, reject) => {
  mqttClient.once("connect", resolve);
  mqttClient.once("error", reject);
});

const opcUaServer = new OPCUAServer({
  port: opcUaPort,
  resourcePath: "/UA/IngotOpticalMolding",
  hostname: host,
  buildInfo: {
    productName: "Ingot Optical Molding Simulator",
    buildNumber: "1.0.0",
    buildDate: new Date(),
  },
  allowAnonymous: true,
});
await opcUaServer.initialize();
{
  const addressSpace = opcUaServer.engine.addressSpace;
  const namespace = addressSpace.getOwnNamespace();
  const device = namespace.addObject({
    organizedBy: addressSpace.rootFolder.objects,
    browseName: "OpticalMoldingPress",
    nodeId: "ns=1;s=OpticalMoldingPress",
  });
  const add = (nodeId, browseName, dataType, value) => {
    const variable = namespace.addVariable({
      componentOf: device,
      browseName,
      nodeId,
      dataType,
    });
    opcVariables.push({ variable, dataType, value });
  };
  for (const [code, field] of sensorDefinitions) {
    add(`ns=1;s=Telemetry.${code}`, code, DataType.Double, () => state.sensors[field]);
  }
  add("ns=1;s=Operation.ElapsedSeconds", "ElapsedSeconds", DataType.UInt16, () => state.cycle.elapsedSeconds);
  add("ns=1;s=Operation.HeatingEnabled", "HeatingEnabled", DataType.Boolean, () => state.machine.heatingEnabled);
  add("ns=1;s=Operation.Mode", "Mode", DataType.String, () => state.machine.mode);
  add("ns=1;s=Operation.AlarmCode", "AlarmCode", DataType.UInt16, () => state.machine.alarmCode);
  add("ns=1;s=Context.CorrelationId", "CorrelationId", DataType.String, () => state.cycle.id);
  add("ns=1;s=Context.RecipeStep", "RecipeStep", DataType.UInt16, () => state.stage.sourceStep);
  add("ns=1;s=Context.RecipeStepName", "RecipeStepName", DataType.String, () => `STEP-${state.stage.sourceStep}`);
  add("ns=1;s=Recipe.Id", "RecipeId", DataType.String, () => state.recipe.id);
  add("ns=1;s=Recipe.Version", "RecipeVersion", DataType.UInt16, () => state.recipe.version);
  add("ns=1;s=Recipe.Name", "RecipeName", DataType.String, () => state.recipe.name);
  for (const [code] of Object.entries(recipe.parameters)) {
    add(`ns=1;s=Recipe.Parameter.${code}`, code, DataType.Double, () => state.recipe.parameters[code]);
  }
  updateOpcUa();
}
await opcUaServer.start();

function updateOpcUa() {
  for (const item of opcVariables) {
    const value = item.value();
    item.variable.setValueFromSource(
      new Variant({
        dataType: item.dataType,
        arrayType: VariantArrayType.Scalar,
        value,
      }),
      StatusCodes.Good,
      state.timestamp,
    );
  }
}

function writeBytes(address, bytes) {
  for (let index = 0; index < bytes.length; index += 2) {
    holdingRegisters[address + index / 2] =
      (bytes[index] << 8) | (bytes[index + 1] || 0);
  }
}

function writeFloat32(address, value) {
  const bytes = Buffer.alloc(4);
  bytes.writeFloatBE(value);
  writeBytes(address, bytes);
}

function writeFloat64(address, value) {
  const bytes = Buffer.alloc(8);
  bytes.writeDoubleBE(value);
  writeBytes(address, bytes);
}

function writeUInt64(address, value) {
  const bytes = Buffer.alloc(8);
  bytes.writeBigUInt64BE(BigInt(value));
  writeBytes(address, bytes);
}

function writeString(address, length, value) {
  const bytes = Buffer.alloc(length);
  bytes.write(String(value), 0, length, "utf8");
  writeBytes(address, bytes);
}

function updateModbus() {
  sensorDefinitions.forEach(([, field], index) => writeFloat32(index * 2, state.sensors[field]));
  holdingRegisters[40] = state.cycle.elapsedSeconds;
  holdingRegisters[41] = state.machine.heatingEnabled ? 1 : 0;
  holdingRegisters[42] = state.machine.alarmCode;
  writeString(50, 16, state.machine.mode);
  writeString(100, 24, state.cycle.id);
  holdingRegisters[112] = state.stage.sourceStep;
  writeString(113, 8, `STEP-${state.stage.sourceStep}`);
  writeString(120, 16, state.recipe.id);
  holdingRegisters[128] = state.recipe.version;
  writeString(129, 32, state.recipe.name);
  writeUInt64(145, state.timestamp.getTime());
  Object.values(state.recipe.parameters).forEach((value, index) => writeFloat64(160 + index * 4, value));
}
updateModbus();

const modbusServer = new ModbusRTU.ServerTCP(
  {
    getHoldingRegister(address) {
      return holdingRegisters[address] || 0;
    },
    getInputRegister(address) {
      return holdingRegisters[address] || 0;
    },
    getCoil(address) {
      return address === 0 ? state.machine.heatingEnabled : false;
    },
    getDiscreteInput(address) {
      return address === 0 ? state.machine.heatingEnabled : false;
    },
  },
  { host, port: modbusPort, unitID: 1, debug: false },
);

const timer = setInterval(advance, tickMs);
console.log(JSON.stringify({
  simulator: state.device.name,
  controller: state.device.controller,
  tickMs,
  logicalCycleSeconds: cycleDurationSeconds,
  endpoints: {
    http: `http://${host}:${httpPort}/api/v1/snapshot`,
    mqtt: `mqtt://${host}:${mqttPort}/${mqttTopic}`,
    opcUa: `opc.tcp://${host}:${opcUaPort}/UA/IngotOpticalMolding`,
    modbusTcp: `${host}:${modbusPort} (unit 1)`,
  },
}, null, 2));

async function shutdown() {
  clearInterval(timer);
  mqttClient.end(true);
  await mqttBroker.close();
  mqttServer.close();
  httpServer.close();
  modbusServer.close();
  await opcUaServer.shutdown(100);
}
process.once("SIGINT", () => void shutdown().finally(() => process.exit(0)));
process.once("SIGTERM", () => void shutdown().finally(() => process.exit(0)));
