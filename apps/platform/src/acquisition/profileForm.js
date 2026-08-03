// 采集配置的表单模型：后端契约 ↔ 表单状态 ↔ 字段级校验。
//
// 与旧的 registryBusinessValidation 相比有两点不同：
//   1. 校验结果是 { 字段路径: 消息 } 而不是一整条字符串，界面可以把错误显示在出错的输入框上；
//   2. 每个协议自己的规则由描述符提供，这里只负责通用结构与装配。

import {
  ADDRESSING,
  isModbusBitArea,
  melsecDevice,
  protocolDescriptor,
  registerWordCount,
} from "./protocolRegistry";

const numberOrNull = value =>
  value === "" || value === null || value === undefined ? null : Number(value);

const pairsFromObject = value => Object.entries(value || {}).map(([key, pairValue]) => ({ key, value: pairValue }));

const objectFromPairs = pairs => Object.fromEntries(
  (pairs || []).filter(item => item.key.trim() && String(item.value).trim())
    .map(item => [item.key.trim(), String(item.value).trim()]),
);

export const modelValue = (id, version) => (id ? `${id}::${version || 1}` : "");

export function parseModelValue(value) {
  const [id = "", version = "1"] = (value || "").split("::");
  return { id, version: Number(version) || 1 };
}

/** 兼容历史配置：只有 sourcePath 的 MELSEC 点位反解成结构化字段。 */
function parseMelsecPath(path = "") {
  const [device = "D", address = "", dataType = "int16", length = ""] = String(path).split(":");
  const [plain = "", bit = ""] = String(address).split(".");
  return {
    device: (device || "D").toUpperCase(),
    address: plain,
    bitIndex: bit,
    dataType: dataType || "int16",
    length,
  };
}

/** 兼容历史配置：只有 sourcePath 的 Modbus 点位反解成结构化字段。 */
function parseModbusPath(path = "") {
  const [area = "", address = "", dataType = ""] = String(path).split(":");
  const [plain = "", bit = ""] = String(address).split(".");
  return { area, address: plain, bitIndex: bit, dataType };
}

export function createValueMapping(value = {}, protocol = "http-polling") {
  const descriptor = protocolDescriptor(protocol);
  const base = {
    dataItemCode: value.dataItemCode || "",
    sourcePath: value.sourcePath || "",
    required: value.required !== false,
    sourceDataType: value.sourceDataType || (descriptor.dataTypes.includes("auto") ? "auto" : "int16"),
    scale: value.scale ?? 1,
    offset: value.offset ?? 0,
    modbusArea: value.modbusArea || "holding-register",
    modbusAddress: value.modbusAddress ?? "",
    modbusQuantity: value.modbusQuantity ?? 1,
    byteOrder: value.byteOrder || "big-endian",
    wordOrder: value.wordOrder || "high-low",
    melsecDevice: value.melsecDevice || "D",
    melsecAddress: value.melsecAddress ?? "",
    melsecStringLength: value.melsecStringLength ?? 16,
    bitIndex: value.bitIndex ?? "",
    topic: value.topic || "",
  };

  if (descriptor.addressing === ADDRESSING.melsecDevice && !value.melsecDevice && value.sourcePath) {
    const parsed = parseMelsecPath(value.sourcePath);
    base.melsecDevice = parsed.device;
    base.melsecAddress = parsed.address;
    base.bitIndex = parsed.bitIndex;
    base.sourceDataType = parsed.dataType;
    if (parsed.length) base.melsecStringLength = Number(parsed.length) || 16;
  }
  if (descriptor.addressing === ADDRESSING.modbusRegister && value.modbusAddress === undefined && value.sourcePath) {
    const parsed = parseModbusPath(value.sourcePath);
    if (parsed.area) base.modbusArea = parsed.area;
    if (parsed.address !== "") base.modbusAddress = parsed.address;
    base.bitIndex = parsed.bitIndex;
    if (parsed.dataType) base.sourceDataType = parsed.dataType;
  }
  return base;
}

export function createProfileForm(value = {}, version) {
  const protocol = value.protocol || "http-polling";
  const mappings = (value.valueMappings || []).length
    ? value.valueMappings.map(item => createValueMapping(item, protocol))
    : [createValueMapping({}, protocol)];
  return {
    profileId: value.profileId || "",
    version: version ?? value.version ?? 1,
    name: value.name || "",
    status: version === undefined ? value.status || "draft" : "draft",
    edgeId: value.edgeId || "",
    protocol,
    dataModel: modelValue(value.dataModelId, value.dataModelVersion),
    source: value.source || "",
    subjectType: "equipment",
    subjectId: value.subjectId || "",
    connection: {
      baseUrl: value.connection?.baseUrl || "",
      snapshotPath: value.connection?.snapshotPath || "/api/v1/snapshot",
      pollIntervalMs: value.connection?.pollIntervalMs ?? 1000,
    },
    mqtt: {
      host: value.mqtt?.host || "",
      port: value.mqtt?.port ?? 1883,
      protocolVersion: value.mqtt?.protocolVersion || "5.0",
      clientId: value.mqtt?.clientId || "",
      username: value.mqtt?.username || "",
      passwordSecretRef: value.mqtt?.passwordSecretRef || "",
      useTls: Boolean(value.mqtt?.useTls),
      caCertificatePath: value.mqtt?.caCertificatePath || "",
      clientCertificatePath: value.mqtt?.clientCertificatePath || "",
      clientCertificatePasswordSecretRef: value.mqtt?.clientCertificatePasswordSecretRef || "",
      cleanSession: value.mqtt?.cleanSession !== false,
      keepAliveSeconds: value.mqtt?.keepAliveSeconds ?? 30,
      snapshotMaxAgeSeconds: value.mqtt?.snapshotMaxAgeSeconds ?? 0,
      topics: (value.mqtt?.topics || []).length
        ? value.mqtt.topics.map(item => ({ topic: item.topic, qos: item.qos ?? 0, payloadRoot: item.payloadRoot || "" }))
        : [{ topic: "", qos: 0, payloadRoot: "" }],
    },
    opcUa: {
      endpointUrl: value.opcUa?.endpointUrl || "",
      securityMode: value.opcUa?.securityMode || "none",
      securityPolicy: value.opcUa?.securityPolicy || "None",
      authenticationType: value.opcUa?.authenticationType || "anonymous",
      username: value.opcUa?.username || "",
      passwordSecretRef: value.opcUa?.passwordSecretRef || "",
      clientCertificatePath: value.opcUa?.clientCertificatePath || "",
      clientCertificatePasswordSecretRef: value.opcUa?.clientCertificatePasswordSecretRef || "",
      trustServerCertificate: Boolean(value.opcUa?.trustServerCertificate),
      publishingIntervalMs: value.opcUa?.publishingIntervalMs ?? 1000,
      samplingIntervalMs: value.opcUa?.samplingIntervalMs ?? 1000,
    },
    modbusTcp: {
      host: value.modbusTcp?.host || "",
      port: value.modbusTcp?.port ?? 502,
      unitId: value.modbusTcp?.unitId ?? 1,
      addressBase: value.modbusTcp?.addressBase || "zero-based",
      pollIntervalMs: value.modbusTcp?.pollIntervalMs ?? 1000,
    },
    melsecA1E: {
      host: value.melsecA1E?.host || "",
      port: value.melsecA1E?.port ?? 5551,
      pollIntervalMs: value.melsecA1E?.pollIntervalMs ?? 1000,
      dataCode: value.melsecA1E?.dataCode || "binary",
      pcNumber: value.melsecA1E?.pcNumber ?? 255,
      monitoringTimer: value.melsecA1E?.monitoringTimer ?? 16,
      wordOrderLayout: value.melsecA1E?.wordOrderLayout || "A",
      maxMergeGap: value.melsecA1E?.maxMergeGap ?? 8,
    },
    execution: {
      timeoutMs: value.execution?.timeoutMs ?? 10000,
      reconnectDelayMs: value.execution?.reconnectDelayMs ?? 5000,
    },
    timestampMode: value.timestampMode || "source",
    timestampPath: value.timestampPath || "timestamp",
    sequencePath: value.sequencePath ?? "",
    sampleEventType: value.sampleEventType || "process.sample",
    staticContextPairs: pairsFromObject(value.staticContext),
    contextMappings: (value.contextMappings || []).map(item => ({
      contextKey: item.contextKey || "",
      sourcePath: item.sourcePath || "",
      required: Boolean(item.required),
      topic: item.topic || "",
    })),
    valueMappings: mappings,
    recipe: value.recipe
      ? {
        enabled: true,
        eventType: value.recipe.eventType || "recipe.applied",
        idPath: value.recipe.idPath || "",
        versionPath: value.recipe.versionPath || "",
        namePath: value.recipe.namePath || "",
        parametersPath: value.recipe.parametersPath || ".",
        parameterMappings: (value.recipe.parameterMappings || []).map(item => createValueMapping(item, protocol)),
      }
      : {
        enabled: false, eventType: "recipe.applied", idPath: "", versionPath: "",
        namePath: "", parametersPath: ".", parameterMappings: [],
      },
    lifecycle: value.lifecycle
      ? { enabled: true, ...value.lifecycle }
      : {
        enabled: false, mode: "discrete-cycle", activeContextKey: "run_active", activeValue: "1",
        startedEventType: "cycle.started", completedEventType: "cycle.completed",
        stepChangedEventType: "process.stage_changed",
      },
  };
}

/** 切换协议时，把不属于新协议的取值收敛到合法范围，避免把死值提交给后端。 */
export function applyProtocolChange(form, protocol) {
  const descriptor = protocolDescriptor(protocol);
  const fallbackType = descriptor.dataTypes.includes("auto") ? "auto" : "int16";
  const normalizeRow = row => {
    const next = { ...row };
    if (!descriptor.dataTypes.includes(next.sourceDataType)) next.sourceDataType = fallbackType;
    if (!descriptor.capabilities.bitAddressing) next.bitIndex = "";
    if (!descriptor.capabilities.perTopicMapping) next.topic = "";
    return next;
  };
  return {
    ...form,
    protocol,
    timestampMode: descriptor.capabilities.sourceTimestamp ? form.timestampMode : "edge-received",
    sequencePath: descriptor.capabilities.sequencePath ? form.sequencePath : "",
    valueMappings: form.valueMappings.map(normalizeRow),
    recipe: { ...form.recipe, parameterMappings: form.recipe.parameterMappings.map(normalizeRow) },
  };
}

function mappingPayload(item, descriptor) {
  const bitIndex = numberOrNull(item.bitIndex);
  const base = {
    dataItemCode: (item.dataItemCode || "").trim(),
    required: item.required,
    sourceDataType: item.sourceDataType,
    scale: Number(item.scale),
    offset: Number(item.offset),
    bitIndex: descriptor.capabilities.bitAddressing ? bitIndex : null,
    topic: descriptor.capabilities.perTopicMapping ? (item.topic || "").trim() || null : null,
    modbusArea: null,
    modbusAddress: null,
    modbusQuantity: 1,
    byteOrder: item.byteOrder,
    wordOrder: item.wordOrder,
    melsecDevice: null,
    melsecAddress: null,
    sourcePath: (item.sourcePath || "").trim(),
  };

  if (descriptor.addressing === ADDRESSING.modbusRegister) {
    const address = numberOrNull(item.modbusAddress);
    const quantity = item.sourceDataType === "string"
      ? Number(item.modbusQuantity) || 1
      : registerWordCount(item.sourceDataType);
    return {
      ...base,
      sourcePath: "",
      modbusArea: item.modbusArea,
      modbusAddress: address,
      modbusQuantity: quantity,
      bitIndex: isModbusBitArea(item.modbusArea) ? null : bitIndex,
    };
  }

  if (descriptor.addressing === ADDRESSING.melsecDevice) {
    const device = melsecDevice(item.melsecDevice);
    const quantity = item.sourceDataType === "string"
      ? Math.max(1, Math.ceil((Number(item.melsecStringLength) || 2) / 2))
      : registerWordCount(item.sourceDataType);
    return {
      ...base,
      sourcePath: "",
      melsecDevice: device?.code || item.melsecDevice,
      melsecAddress: String(item.melsecAddress ?? "").trim(),
      modbusQuantity: quantity,
      bitIndex: device?.bit ? null : bitIndex,
    };
  }

  return base;
}

export function toPayload(form) {
  const descriptor = protocolDescriptor(form.protocol);
  const selectedModel = parseModelValue(form.dataModel);
  const capabilities = descriptor.capabilities;
  return {
    profileId: form.profileId.trim(),
    version: Number(form.version),
    name: form.name.trim(),
    status: form.status,
    edgeId: form.edgeId.trim(),
    protocol: form.protocol,
    dataModelId: selectedModel.id,
    dataModelVersion: selectedModel.version,
    source: form.source.trim() || `connector/${form.protocol}/${form.subjectId.trim()}`,
    subjectType: "equipment",
    subjectId: form.subjectId.trim(),
    connection: {
      baseUrl: form.connection.baseUrl.trim(),
      snapshotPath: form.connection.snapshotPath.trim(),
      pollIntervalMs: Number(form.connection.pollIntervalMs),
    },
    mqtt: form.protocol === "mqtt" ? {
      ...form.mqtt,
      port: Number(form.mqtt.port),
      keepAliveSeconds: Number(form.mqtt.keepAliveSeconds),
      snapshotMaxAgeSeconds: Number(form.mqtt.snapshotMaxAgeSeconds) || 0,
      username: form.mqtt.username.trim() || null,
      passwordSecretRef: form.mqtt.passwordSecretRef.trim() || null,
      caCertificatePath: form.mqtt.caCertificatePath.trim() || null,
      clientCertificatePath: form.mqtt.clientCertificatePath.trim() || null,
      clientCertificatePasswordSecretRef: form.mqtt.clientCertificatePasswordSecretRef.trim() || null,
      topics: form.mqtt.topics.filter(item => item.topic.trim()).map(item => ({
        topic: item.topic.trim(),
        qos: Number(item.qos),
        payloadRoot: (item.payloadRoot || "").trim() || null,
      })),
    } : null,
    opcUa: form.protocol === "opc-ua" ? {
      ...form.opcUa,
      publishingIntervalMs: Number(form.opcUa.publishingIntervalMs),
      samplingIntervalMs: Number(form.opcUa.samplingIntervalMs),
      username: form.opcUa.username.trim() || null,
      passwordSecretRef: form.opcUa.passwordSecretRef.trim() || null,
      clientCertificatePath: form.opcUa.clientCertificatePath.trim() || null,
      clientCertificatePasswordSecretRef: form.opcUa.clientCertificatePasswordSecretRef.trim() || null,
    } : null,
    modbusTcp: form.protocol === "modbus-tcp" ? {
      ...form.modbusTcp,
      port: Number(form.modbusTcp.port),
      unitId: Number(form.modbusTcp.unitId),
      pollIntervalMs: Number(form.modbusTcp.pollIntervalMs),
    } : null,
    melsecA1E: form.protocol === "melsec-a1e" ? {
      ...form.melsecA1E,
      port: Number(form.melsecA1E.port),
      pollIntervalMs: Number(form.melsecA1E.pollIntervalMs),
      pcNumber: Number(form.melsecA1E.pcNumber),
      monitoringTimer: Number(form.melsecA1E.monitoringTimer),
      maxMergeGap: Number(form.melsecA1E.maxMergeGap),
    } : null,
    execution: {
      timeoutMs: Number(form.execution.timeoutMs),
      reconnectDelayMs: Number(form.execution.reconnectDelayMs),
    },
    timestampMode: capabilities.sourceTimestamp ? form.timestampMode : "edge-received",
    timestampPath: form.timestampPath.trim(),
    sequencePath: capabilities.sequencePath ? form.sequencePath.trim() || null : null,
    sampleEventType: form.sampleEventType.trim(),
    staticContext: objectFromPairs(form.staticContextPairs),
    contextMappings: form.contextMappings
      .filter(item => item.contextKey.trim() || item.sourcePath.trim())
      .map(item => ({
        contextKey: item.contextKey.trim(),
        sourcePath: item.sourcePath.trim(),
        required: item.required,
        topic: capabilities.perTopicMapping ? (item.topic || "").trim() || null : null,
      })),
    valueMappings: form.valueMappings
      .filter(item => item.dataItemCode.trim())
      .map(item => mappingPayload(item, descriptor)),
    recipe: form.recipe.enabled ? {
      eventType: form.recipe.eventType.trim(),
      idPath: form.recipe.idPath.trim(),
      versionPath: form.recipe.versionPath.trim(),
      namePath: form.recipe.namePath.trim() || null,
      parametersPath: capabilities.recipeParametersPath ? form.recipe.parametersPath.trim() || "." : ".",
      parameterMappings: form.recipe.parameterMappings
        .filter(item => item.dataItemCode.trim())
        .map(item => mappingPayload(item, descriptor)),
    } : null,
    lifecycle: form.lifecycle.enabled ? {
      mode: "discrete-cycle",
      correlationIdContextKey: form.lifecycle.correlationIdContextKey || null,
      activeContextKey: form.lifecycle.activeContextKey || null,
      activeValue: form.lifecycle.activeValue || "",
      startedEventType: form.lifecycle.startedEventType || "cycle.started",
      completedEventType: form.lifecycle.completedEventType || "cycle.completed",
      stepChangedEventType: form.lifecycle.stepChangedEventType || "process.stage_changed",
    } : null,
  };
}

const CODE_PATTERN = /^[a-z0-9][a-z0-9._-]{0,127}$/;
const EVENT_TYPE_PATTERN = /^[a-z][a-z0-9]*(?:\.[a-z][a-z0-9_]*)+$/;

/**
 * 字段级校验。返回 { errors, count }，errors 的键与输入框的 name 一致。
 * @param context.dataItems 所选工艺数据模型的数据项，用于发布前的完整性检查。
 */
export function validateProfile(form, context = {}) {
  const descriptor = protocolDescriptor(form.protocol);
  const errors = {};
  const set = (path, message) => { if (!errors[path]) errors[path] = message; };

  if (!CODE_PATTERN.test(form.profileId.trim().toLowerCase()))
    set("profileId", "只能包含小写字母、数字、点、下划线和短横线。");
  if (!form.name.trim()) set("name", "配置名称不能为空。");
  if (!form.edgeId.trim()) set("edgeId", "请选择执行采集的现场节点。");
  if (!form.dataModel) set("dataModel", "请选择工艺数据模型。");
  if (!form.subjectId.trim()) set("subjectId", "设备编号不能为空。");
  if (!EVENT_TYPE_PATTERN.test(form.sampleEventType.trim()))
    set("sampleEventType", "事件类型格式无效，例如 process.sample。");

  const section = descriptor.section;
  const connectionErrors = descriptor.validateConnection(form[section] || {}) || {};
  Object.entries(connectionErrors).forEach(([field, message]) => set(`${section}.${field}`, message));

  if (descriptor.capabilities.connectTimeout && !(Number(form.execution.timeoutMs) >= 100))
    set("execution.timeoutMs", "连接超时不能小于 100ms。");
  if (descriptor.capabilities.reconnectDelay && !(Number(form.execution.reconnectDelayMs) >= 100))
    set("execution.reconnectDelayMs", "重连间隔不能小于 100ms。");

  if (descriptor.capabilities.sourceTimestamp && form.timestampMode === "source") {
    if (!form.timestampPath.trim()) set("timestampPath", "使用设备时间时必须指定时间来源。");
    else {
      const rowErrors = descriptor.addressing === ADDRESSING.jsonPath
        ? {}
        : descriptor.validatePoint(timestampProbeRow(form, descriptor), form) || {};
      const first = Object.values(rowErrors)[0];
      if (first) set("timestampPath", first);
    }
  }

  const seenCodes = new Set();
  const validateRows = (rows, prefix) => {
    rows.forEach((row, index) => {
      if (!row.dataItemCode.trim() && !hasAnyAddress(row, descriptor)) return;
      if (!row.dataItemCode.trim()) set(`${prefix}[${index}].dataItemCode`, "请选择平台数据项。");
      else if (seenCodes.has(`${prefix}:${row.dataItemCode}`))
        set(`${prefix}[${index}].dataItemCode`, "同一个数据项只能映射一次。");
      seenCodes.add(`${prefix}:${row.dataItemCode}`);
      if (!Number.isFinite(Number(row.scale))) set(`${prefix}[${index}].scale`, "换算倍率必须是数字。");
      if (!Number.isFinite(Number(row.offset))) set(`${prefix}[${index}].offset`, "换算偏移必须是数字。");
      const rowErrors = descriptor.validatePoint(row, form) || {};
      Object.entries(rowErrors).forEach(([field, message]) => set(`${prefix}[${index}].${field}`, message));
    });
  };
  validateRows(form.valueMappings, "valueMappings");
  if (!form.valueMappings.some(item => item.dataItemCode.trim()))
    set("valueMappings", "至少需要配置一个采集点位。");

  form.contextMappings.forEach((item, index) => {
    if (!item.contextKey.trim() && !item.sourcePath.trim()) return;
    if (!CODE_PATTERN.test(item.contextKey.trim().toLowerCase()))
      set(`contextMappings[${index}].contextKey`, "上下文键格式无效。");
    if (!item.sourcePath.trim()) set(`contextMappings[${index}].sourcePath`, "设备来源不能为空。");
  });

  if (form.recipe.enabled) {
    if (!form.recipe.idPath.trim()) set("recipe.idPath", "配方编号来源不能为空。");
    if (!form.recipe.versionPath.trim()) set("recipe.versionPath", "配方版本来源不能为空。");
    if (!EVENT_TYPE_PATTERN.test(form.recipe.eventType.trim()))
      set("recipe.eventType", "事件类型格式无效，例如 recipe.applied。");
    validateRows(form.recipe.parameterMappings, "recipe.parameterMappings");
  }

  if (form.lifecycle.enabled && !(form.lifecycle.activeContextKey || "").trim())
    set("lifecycle.activeContextKey", "启用周期识别时必须指定生产状态来源。");

  if (form.status === "published" && Array.isArray(context.dataItems)) {
    const mapped = new Set(form.valueMappings.map(item => item.dataItemCode));
    const missing = context.dataItems.filter(item => !item.nullable && !mapped.has(item.code));
    if (missing.length)
      set("valueMappings", `发布前必须映射周期必需的数据项：${missing.map(item => item.code).join("、")}。`);
  }

  return { errors, count: Object.keys(errors).length };
}

function hasAnyAddress(row, descriptor) {
  if (descriptor.addressing === ADDRESSING.modbusRegister) return String(row.modbusAddress ?? "").trim() !== "";
  if (descriptor.addressing === ADDRESSING.melsecDevice) return String(row.melsecAddress ?? "").trim() !== "";
  return (row.sourcePath || "").trim() !== "";
}

/** 把时间戳来源伪装成一个点位行，复用协议描述符的点位校验规则。 */
function timestampProbeRow(form, descriptor) {
  const text = form.timestampPath.trim();
  if (descriptor.addressing === ADDRESSING.modbusRegister) {
    const [area = "", address = "", dataType = ""] = text.split(":");
    return { modbusArea: area, modbusAddress: address, sourceDataType: dataType || "uint64", bitIndex: "" };
  }
  if (descriptor.addressing === ADDRESSING.melsecDevice) {
    const [device = "", address = "", dataType = ""] = text.split(":");
    return { melsecDevice: device, melsecAddress: address, sourceDataType: dataType || "uint64", bitIndex: "" };
  }
  return { sourcePath: text };
}

/** 从探查结果的一个设备点位生成映射补丁。 */
export function patchFromProbePoint(point, dataItemCode, dataItems, descriptor) {
  const definition = (dataItems || []).find(item => item.code === dataItemCode);
  const patch = { dataItemCode, required: definition ? !definition.nullable : true };
  if (point.topic) patch.topic = point.topic;
  if (descriptor.addressing === ADDRESSING.modbusRegister) {
    const [area = "", address = "", dataType = ""] = String(point.path).split(":");
    const [plain = "", bit = ""] = String(address).split(".");
    return { ...patch, modbusArea: area || "holding-register", modbusAddress: plain, bitIndex: bit, sourceDataType: dataType || "int16" };
  }
  if (descriptor.addressing === ADDRESSING.melsecDevice) {
    const parsed = parseMelsecPath(point.path);
    return { ...patch, melsecDevice: parsed.device, melsecAddress: parsed.address, bitIndex: parsed.bitIndex, sourceDataType: parsed.dataType };
  }
  return { ...patch, sourcePath: point.path };
}
