const sensors = [
  ["upper_mold.ir_temperature", "上模红外温度℃", "Cel", "upper_mold_ir_temperature"],
  ["upper_mold.current", "上模电流A", "A", "upper_mold_current"],
  ["upper_mold.voltage", "上模电压V", "V", "upper_mold_voltage"],
  ["lower_mold.ir_temperature", "下模红外温度℃", "Cel", "lower_mold_ir_temperature"],
  ["lower_mold.current", "下模电流A", "A", "lower_mold_current"],
  ["lower_mold.voltage", "下模电压V", "V", "lower_mold_voltage"],
  ["press.load", "压力kg", "kg", "press_load"],
  ["grating.position", "光栅位置mm", "mm", "grating_position"],
  ["servo.speed", "伺服速度mm/s", "mm/s", "servo_speed"],
  ["chamber.vacuum", "真空度kPa", "kPa", "chamber_vacuum"],
  ["servo.position", "伺服位置mm", "mm", "servo_position"],
  ["upper_mold.power", "上模功率W", "W", "upper_mold_power"],
  ["lower_mold.power", "下模功率W", "W", "lower_mold_power"],
];

const operationItems = [
  ["operation.elapsed_seconds", "周期已运行时间", "integer", "s", "cycle.elapsedSeconds", "Operation.ElapsedSeconds"],
  ["operation.heating_enabled", "加热输出状态", "boolean", null, "machine.heatingEnabled", "Operation.HeatingEnabled"],
  ["operation.mode", "设备运行模式", "string", null, "machine.mode", "Operation.Mode"],
  ["operation.alarm_code", "设备报警代码", "integer", null, "machine.alarmCode", "Operation.AlarmCode"],
];

const recipeParameters = [
  ["position.heat", "HEAT位置mm", "mm", "heat_position"],
  ["position.work", "WORK位置mm", "mm", "work_position"],
  ["position.host", "HOST位置mm", "mm", "host_position"],
  ["upper_mold.set_temperature", "上模设置温度℃", "Cel", "upper_set_temperature"],
  ["lower_mold.set_temperature", "下模设置温度℃", "Cel", "lower_set_temperature"],
  ["nitrogen.charge_temperature", "充氮气温度", "Cel", "nitrogen_fill_temperature"],
  ["preheat.soak_delay", "预热保温延时s", "s", "preheat_soak_delay"],
  ["press.differential_upper_limit", "压力差上限kg", "kg", "pressure_difference_limit"],
  ["upper_mold.temperature_upper_limit", "上模温度上限℃", "Cel", "upper_temperature_limit"],
  ["lower_mold.temperature_upper_limit", "下模温度上限℃", "Cel", "lower_temperature_limit"],
  ["press.upper_limit", "压力上限kg", "kg", "pressure_limit"],
  ["work.set_pressure", "WORK位设定压力kg", "kg", "work_pressure"],
  ["upper_mold.pid.p", "上模P", null, "upper_pid_p"],
  ["upper_mold.pid.i", "上模I", null, "upper_pid_i"],
  ["upper_mold.pid.d", "上模D", null, "upper_pid_d"],
  ["lower_mold.pid.p", "下模P", null, "lower_pid_p"],
  ["lower_mold.pid.i", "下模I", null, "lower_pid_i"],
  ["lower_mold.pid.d", "下模D", null, "lower_pid_d"],
  ["glass.tg_temperature", "TG温度℃", "Cel", "tg_temperature"],
  ["work.upper_mold.heating_delay", "Work位上模加热延时s", "s", "work_upper_heat_delay"],
  ["work.lower_mold.heating_delay", "Work位下模加热延时s", "s", "work_lower_heat_delay"],
  ["work.speed", "Work位速度mm/s", "mm/s", "work_speed"],
  ["hold.upper_mold.temperature", "上模保压温度", "Cel", "upper_hold_temperature"],
  ["hold.lower_mold.temperature", "下模保压温度", "Cel", "lower_hold_temperature"],
  ["upper_mold.power_off_delay", "上模断电延时", "s", "upper_power_off_delay"],
  ["lower_mold.power_off_delay", "下模断电延时", "s", "lower_power_off_delay"],
  ["hold.pressure", "保压压力", "kg", "holding_pressure"],
  ["work.upper_mold.temperature", "WORK上模温度", "Cel", "work_upper_temperature"],
  ["work.lower_mold.temperature", "WORK下模温度", "Cel", "work_lower_temperature"],
  ["molding.temperature", "模压温度", "Cel", "molding_temperature"],
  ["hold.speed", "保压速度", "mm/s", "holding_speed"],
];

export const processDataModel = {
  modelId: "optical-glass-molding",
  version: 2,
  name: "光学玻璃精密模压工艺",
  description: "13 路模压传感器、4 项运行状态、31 项配方参数和 5 个控制器步序的通用工艺数据模型。",
  status: "published",
  acquisition: {
    samplePeriodMs: 1000,
    stepSourceKey: "recipe_step",
    dataItems: [
      ...sensors.map(([code, sourceField, unit]) => ({
        code, sourceField, dataType: "double", unit, category: "process", nullable: false,
      })),
      ...operationItems.map(([code, sourceField, dataType, unit]) => ({
        code, sourceField, dataType, unit, category: "state", nullable: false,
      })),
    ],
  },
  recipeParameters: recipeParameters.map(([code, sourceField, unit]) => ({
    code, sourceField, dataType: "double", unit, nullable: false,
  })),
  stages: [
    { sourceStep: "10", code: "preheat", name: "预热", expectedDurationSeconds: 90, required: true },
    { sourceStep: "20", code: "soak", name: "均热保温", expectedDurationSeconds: 150, required: true },
    { sourceStep: "30", code: "press", name: "模压保压", expectedDurationSeconds: 120, required: true },
    { sourceStep: "40", code: "anneal", name: "退火", expectedDurationSeconds: 120, required: true },
    { sourceStep: "50", code: "cool", name: "冷却脱模", expectedDurationSeconds: 120, required: true },
  ],
};

const recipeValues = [
  25, 12.5, 18, 620, 615, 480, 90, 5, 635, 630, 135, 120, 2.4, 0.8, 0.12,
  2.35, 0.78, 0.11, 510, 4, 4, 1.2, 620, 615, 8, 8, 120, 620, 615, 618, 0.35,
];

export const recipeVersion = {
  recipeId: "lens-a-std",
  version: 4,
  name: "LENS-A 标准模压工艺",
  dataModelId: processDataModel.modelId,
  dataModelVersion: processDataModel.version,
  status: "published",
  contextSelector: { product_series: "LENS-A" },
  values: recipeParameters.map(([code], index) => ({ code, value: recipeValues[index] })),
};

export const analysisPlan = {
  planId: "optical-glass-molding.cycle-comparison",
  version: 2,
  name: "光学模压同系列周期对比",
  description: "按产品系列对齐五个控制器阶段，比较完整传感器曲线与统计特征。",
  status: "published",
  dataModelId: processDataModel.modelId,
  dataModelVersion: processDataModel.version,
  analysisScope: "production-cycle",
  alignmentMode: "stage-relative",
  cohortDimension: "product_series",
  comparisonKeys: ["product_series"],
  contextSelector: {},
  signals: sensors.map(([dataItemCode]) => ({
    dataItemCode,
    includeTrace: true,
    features: ["min", "max", "mean", "stddev"],
  })),
};

const lifecycle = {
  mode: "discrete-cycle",
  correlationIdContextKey: "correlation_id",
  stepContextKey: "recipe_step",
  stepNameContextKey: "recipe_step_name",
  startedEventType: "cycle.started",
  completedEventType: "cycle.completed",
  stepChangedEventType: "recipe.step_changed",
  expectedDurationMs: 600000,
};

function jsonMappings() {
  return [
    ...sensors.map(([code, , , field]) => ({
      dataItemCode: code, sourcePath: `sensors.${field}`, required: true,
    })),
    ...operationItems.map(([code, , , , path]) => ({
      dataItemCode: code, sourcePath: path, required: true,
    })),
  ];
}

function opcUaMappings() {
  return [
    ...sensors.map(([code]) => ({
      dataItemCode: code, sourcePath: `ns=1;s=Telemetry.${code}`, required: true,
    })),
    ...operationItems.map(([code, , , , , node]) => ({
      dataItemCode: code, sourcePath: `ns=1;s=${node}`, required: true,
    })),
  ];
}

function modbusMapping(dataItemCode, address, sourceDataType, quantity, area = "holding-register") {
  return {
    dataItemCode,
    sourcePath: `${area}:${address}`,
    required: true,
    sourceDataType,
    scale: 1,
    offset: 0,
    modbusArea: area,
    modbusAddress: address,
    modbusQuantity: quantity,
    byteOrder: "big-endian",
    wordOrder: "high-low",
  };
}

function recipeMapping(protocol) {
  if (protocol === "opc-ua") {
    return {
      eventType: "recipe.applied",
      idPath: "ns=1;s=Recipe.Id",
      versionPath: "ns=1;s=Recipe.Version",
      namePath: "ns=1;s=Recipe.Name",
      parametersPath: ".",
      parameterMappings: recipeParameters.map(([code, , , source]) => ({
        dataItemCode: code,
        sourcePath: `ns=1;s=Recipe.Parameter.${source}`,
        required: true,
      })),
    };
  }
  if (protocol === "modbus-tcp") {
    return {
      eventType: "recipe.applied",
      idPath: "holding-register:120:string:16",
      versionPath: "holding-register:128:uint16",
      namePath: "holding-register:129:string:32",
      parametersPath: ".",
      parameterMappings: recipeParameters.map(([code], index) =>
        modbusMapping(code, 160 + index * 4, "float64", 4)),
    };
  }
  return {
    eventType: "recipe.applied",
    idPath: "recipe.id",
    versionPath: "recipe.version",
    namePath: "recipe.name",
    parametersPath: "recipe.parameters",
    parameterMappings: recipeParameters.map(([code, , , source]) => ({
      dataItemCode: code, sourcePath: source, required: true,
    })),
  };
}

function contextMappings(protocol) {
  const source = protocol === "opc-ua"
    ? {
        correlation_id: "ns=1;s=Context.CorrelationId",
        workpiece_id: "ns=1;s=Context.CorrelationId",
        recipe_step: "ns=1;s=Context.RecipeStep",
        recipe_step_name: "ns=1;s=Context.RecipeStepName",
      }
    : protocol === "modbus-tcp"
      ? {
          correlation_id: "holding-register:100:string:24",
          workpiece_id: "holding-register:100:string:24",
          recipe_step: "holding-register:112:uint16",
          recipe_step_name: "holding-register:113:string:8",
        }
      : {
          correlation_id: "cycle.id",
          workpiece_id: "cycle.id",
          recipe_step: "stage.sourceStep",
          recipe_step_name: "stage.sourceStepName",
        };
  return Object.entries(source).map(([contextKey, sourcePath]) => ({
    contextKey, sourcePath, required: true,
  }));
}

export function acquisitionProfile(protocol, version, edgeId = "EDGE-DEMO-001") {
  const base = {
    profileId: "optical-molding-simulator",
    version,
    name: `光学模压模拟设备 · ${protocol}`,
    status: "published",
    edgeId,
    protocol,
    dataModelId: processDataModel.modelId,
    dataModelVersion: processDataModel.version,
    source: `connector/${protocol}/glass-press-01`,
    subjectType: "optical-molding-machine",
    subjectId: "GLASS-PRESS-01",
    execution: { timeoutMs: 10000, reconnectDelayMs: 1000 },
    timestampMode: "source",
    timestampPath: protocol === "modbus-tcp" ? "holding-register:145:uint64" : "timestamp",
    sequencePath: protocol === "http-polling" || protocol === "mqtt" ? "sequence" : null,
    sampleEventType: "process.sample",
    staticContext: {
      product_series: "LENS-A",
      product_code: "LENS-A-42",
      device_type: "optical-glass-precision-molding-press",
      controller_family: "siemens-s7-1500",
    },
    contextMappings: contextMappings(protocol),
    recipe: recipeMapping(protocol),
    lifecycle,
  };
  if (protocol === "http-polling") {
    return {
      ...base,
      connection: {
        baseUrl: "http://127.0.0.1:8101",
        snapshotPath: "/api/v1/snapshot",
        pollIntervalMs: 100,
      },
      valueMappings: jsonMappings(),
    };
  }
  if (protocol === "mqtt") {
    return {
      ...base,
      mqtt: {
        host: "127.0.0.1",
        port: 1883,
        protocolVersion: "3.1.1",
        clientId: "ingot-edge-optical-molding",
        useTls: false,
        cleanSession: true,
        keepAliveSeconds: 30,
        topics: [{ topic: "ingot/simulator/optical-molding/telemetry", qos: 1 }],
      },
      valueMappings: jsonMappings(),
    };
  }
  if (protocol === "opc-ua") {
    return {
      ...base,
      opcUa: {
        endpointUrl: "opc.tcp://127.0.0.1:4840/UA/IngotOpticalMolding",
        securityMode: "none",
        securityPolicy: "None",
        authenticationType: "anonymous",
        trustServerCertificate: true,
        publishingIntervalMs: 100,
        samplingIntervalMs: 100,
      },
      valueMappings: opcUaMappings(),
    };
  }
  if (protocol === "modbus-tcp") {
    return {
      ...base,
      modbusTcp: { host: "127.0.0.1", port: 1502, unitId: 1, pollIntervalMs: 100 },
      valueMappings: [
        ...sensors.map(([code], index) => modbusMapping(code, index * 2, "float32", 2)),
        modbusMapping("operation.elapsed_seconds", 40, "uint16", 1),
        modbusMapping("operation.heating_enabled", 41, "uint16", 1),
        modbusMapping("operation.mode", 50, "string", 8),
        modbusMapping("operation.alarm_code", 42, "uint16", 1),
      ],
    };
  }
  throw new Error(`Unsupported protocol: ${protocol}`);
}
