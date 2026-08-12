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
const headersToText = value => Object.entries(value || {}).map(([key, item]) => `${key}: ${item}`).join("\n");
const headersFromText = value => Object.fromEntries((value || "").split(/\r?\n/)
  .map(line => line.trim()).filter(Boolean).map(line => {
    const separator = line.indexOf(":");
    return [line.slice(0, separator).trim(), line.slice(separator + 1).trim()];
  }));

export const modelValue = (id, version) => (id ? `${id}::${version || 1}` : "");

export function parseModelValue(value) {
  const [id = "", version = "1"] = (value || "").split("::");
  return { id, version: Number(version) || 1 };
}

export function createValueMapping(value = {}, protocol = "http-polling") {
  const descriptor = protocolDescriptor(protocol);
  const base = {
    dataItemCode: value.dataItemCode || "",
    sourcePath: value.sourcePath || "",
    required: value.required !== false,
    sourceDataType: value.sourceDataType || (descriptor.dataTypes.includes("auto") ? "auto" : "int16"),
    sourceByteLength: value.sourceByteLength ?? 16,
    sourceUnit: value.sourceUnit || "",
    scale: value.scale ?? 1,
    offset: value.offset ?? 0,
    qualityPath: value.qualityPath || "",
    acceptedQualityValues: (value.acceptedQualityValues || []).join(", "),
    minimum: value.minimum ?? "",
    maximum: value.maximum ?? "",
    outOfRangeBehavior: value.outOfRangeBehavior || "reject",
    missingValueBehavior: value.missingValueBehavior || "inherit",
    defaultValue: value.defaultValue ?? "",
    modbusArea: value.modbusArea || "holding-register",
    modbusAddress: value.modbusAddress ?? "",
    modbusQuantity: value.modbusQuantity ?? 1,
    byteOrder: value.byteOrder || "big-endian",
    wordOrder: value.wordOrder || "high-low",
    melsecDevice: value.melsecDevice || "D",
    melsecAddress: value.melsecAddress ?? "",
    bitIndex: value.bitIndex ?? "",
    topic: value.topic || "",
  };

  return base;
}

export function createIngestionTaskForm(value = {}, version) {
  const protocol = value.protocol || "http-polling";
  const mappings = (value.valueMappings || []).length
    ? value.valueMappings.map(item => createValueMapping(item, protocol))
    : [createValueMapping({}, protocol)];
  return {
    taskId: value.taskId || "",
    version: version ?? value.version ?? 1,
    templateId: version === undefined ? value.templateId || "" : "",
    templateVersion: version === undefined ? value.templateVersion ?? null : null,
    dataSourceId: version === undefined ? value.dataSourceId || "" : "",
    dataSourceVersion: version === undefined ? value.dataSourceVersion ?? null : null,
    name: value.name || "",
    status: version === undefined ? value.status || "draft" : "draft",
    edgeId: value.edgeId || "",
    protocol,
    dataModel: modelValue(value.dataModelId, value.dataModelVersion),
    source: value.source || "",
    subjectType: value.subjectType || "equipment",
    subjectId: value.subjectId || "",
    httpPolling: {
      baseUrl: value.httpPolling?.baseUrl || "",
      snapshotPath: value.httpPolling?.snapshotPath || "/api/v1/snapshot",
      pollIntervalMs: value.httpPolling?.pollIntervalMs ?? 1000,
      method: value.httpPolling?.method || "get",
      contentType: value.httpPolling?.contentType || "application/json",
      requestBody: value.httpPolling?.requestBody || "",
      headersText: headersToText(value.httpPolling?.headers),
      headerSecretRefsText: headersToText(value.httpPolling?.headerSecretRefs),
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
      resetSessionOnConnect: value.mqtt?.resetSessionOnConnect !== false,
      keepAliveSeconds: value.mqtt?.keepAliveSeconds ?? 30,
      payloadCompression: value.mqtt?.payloadCompression || "none",
      payloadEncoding: value.mqtt?.payloadEncoding || "utf-8",
      snapshotMaxAgeSeconds: value.mqtt?.snapshotMaxAgeSeconds ?? 0,
      topics: (value.mqtt?.topics || []).length
        ? value.mqtt.topics.map(item => ({
            channel: item.channel || "", topic: item.topic, qos: item.qos ?? 0,
            payloadRoot: item.payloadRoot || "",
            topicVariables: Object.entries(item.topicVariables || {}).map(([key, level]) => `${key}:${level}`).join(", "),
          }))
        : [{ channel: "", topic: "", qos: 0, payloadRoot: "", topicVariables: "" }],
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
      maximumValueAgeMs: value.opcUa?.maximumValueAgeMs ?? 30000,
      maximumTimestampSkewMs: value.opcUa?.maximumTimestampSkewMs ?? 10000,
    },
    modbusTcp: {
      host: value.modbusTcp?.host || "",
      port: value.modbusTcp?.port ?? 502,
      unitId: value.modbusTcp?.unitId ?? 1,
      addressBase: value.modbusTcp?.addressBase || "zero-based",
      pollIntervalMs: value.modbusTcp?.pollIntervalMs ?? 1000,
      maxMergeGap: value.modbusTcp?.maxMergeGap ?? 8,
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
      sourceIdentityStaleAfterMs: value.execution?.sourceIdentityStaleAfterMs ?? 60000,
      maximumFutureTimestampSkewMs: value.execution?.maximumFutureTimestampSkewMs ?? 300000,
    },
    timestampMode: value.timestampMode || "source",
    timestampPath: value.timestampPath || "timestamp",
    timestampEncoding: value.timestampEncoding || "auto",
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
    processSpecification: value.processSpecification
      ? {
        enabled: true,
        eventType: value.processSpecification.eventType || "process.specification.applied",
        idPath: value.processSpecification.idPath || "",
        versionPath: value.processSpecification.versionPath || "",
        namePath: value.processSpecification.namePath || "",
        parametersPath: value.processSpecification.parametersPath || ".",
        parameterMappings: (value.processSpecification.parameterMappings || []).map(item => createValueMapping(item, protocol)),
      }
      : {
        enabled: false, eventType: "process.specification.applied", idPath: "", versionPath: "",
        namePath: "", parametersPath: ".", parameterMappings: [],
      },
    lifecycle: value.lifecycle
      ? { enabled: true, ...value.lifecycle }
      : {
        enabled: false, mode: "discrete", activeContextKey: "run_active", activeValue: "1",
        startedEventType: "process.execution.started", completedEventType: "process.execution.completed",
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
    timestampMode: descriptor.capabilities.intrinsicSourceTimestamp
      ? "source"
      : descriptor.capabilities.sourceTimestamp ? form.timestampMode : "edge-received",
    sequencePath: descriptor.capabilities.sequencePath ? form.sequencePath : "",
    valueMappings: form.valueMappings.map(normalizeRow),
    processSpecification: { ...form.processSpecification, parameterMappings: form.processSpecification.parameterMappings.map(normalizeRow) },
  };
}

function mappingPayload(item, descriptor) {
  const bitIndex = numberOrNull(item.bitIndex);
  const base = {
    dataItemCode: (item.dataItemCode || "").trim(),
    required: item.required,
    sourceDataType: item.sourceDataType,
    sourceUnit: item.sourceUnit.trim() || null,
    scale: Number(item.scale),
    offset: Number(item.offset),
    qualityPath: (item.qualityPath || "").trim() || null,
    acceptedQualityValues: (item.acceptedQualityValues || "").split(",").map(value => value.trim()).filter(Boolean),
    minimum: numberOrNull(item.minimum),
    maximum: numberOrNull(item.maximum),
    outOfRangeBehavior: item.outOfRangeBehavior || "reject",
    missingValueBehavior: item.missingValueBehavior || "inherit",
    defaultValue: (item.defaultValue ?? "").trim() || null,
    bitIndex: descriptor.capabilities.bitAddressing ? bitIndex : null,
    topic: descriptor.capabilities.perTopicMapping ? (item.topic || "").trim() || null : null,
    modbusArea: null,
    modbusAddress: null,
    modbusQuantity: 1,
    sourceByteLength: null,
    byteOrder: item.byteOrder,
    wordOrder: item.wordOrder,
    melsecDevice: null,
    melsecAddress: null,
    sourcePath: (item.sourcePath || "").trim(),
  };

  if (descriptor.addressing === ADDRESSING.modbusRegister) {
    const address = numberOrNull(item.modbusAddress);
    const quantity = item.sourceDataType === "string"
      ? Math.max(1, Math.ceil((Number(item.sourceByteLength) || 1) / 2))
      : registerWordCount(item.sourceDataType);
    return {
      ...base,
      sourcePath: "",
      modbusArea: item.modbusArea,
      modbusAddress: address,
      modbusQuantity: quantity,
      sourceByteLength: item.sourceDataType === "string" ? Number(item.sourceByteLength) || null : null,
      bitIndex: isModbusBitArea(item.modbusArea) ? null : bitIndex,
    };
  }

  if (descriptor.addressing === ADDRESSING.melsecDevice) {
    const device = melsecDevice(item.melsecDevice);
    const quantity = item.sourceDataType === "string"
      ? Math.max(1, Math.ceil((Number(item.sourceByteLength) || 1) / 2))
      : registerWordCount(item.sourceDataType);
    return {
      ...base,
      sourcePath: "",
      melsecDevice: device?.code || item.melsecDevice,
      melsecAddress: String(item.melsecAddress ?? "").trim(),
      modbusQuantity: quantity,
      sourceByteLength: item.sourceDataType === "string" ? Number(item.sourceByteLength) || null : null,
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
    taskId: form.taskId.trim(),
    version: Number(form.version),
    templateId: form.templateId || null,
    templateVersion: form.templateVersion === null ? null : Number(form.templateVersion),
    dataSourceId: form.dataSourceId || null,
    dataSourceVersion: form.dataSourceVersion === null ? null : Number(form.dataSourceVersion),
    name: form.name.trim(),
    status: form.status,
    edgeId: form.edgeId.trim(),
    protocol: form.protocol,
    dataModelId: selectedModel.id,
    dataModelVersion: selectedModel.version,
    source: form.source.trim() || `connector/${form.protocol}/${form.subjectId.trim()}`,
    subjectType: form.subjectType.trim() || "equipment",
    subjectId: form.subjectId.trim(),
    httpPolling: {
      baseUrl: form.httpPolling.baseUrl.trim(),
      snapshotPath: form.httpPolling.snapshotPath.trim(),
      pollIntervalMs: Number(form.httpPolling.pollIntervalMs),
      method: form.httpPolling.method,
      contentType: form.httpPolling.contentType.trim() || null,
      requestBody: form.httpPolling.method === "post" ? form.httpPolling.requestBody || null : null,
      headers: headersFromText(form.httpPolling.headersText),
      headerSecretRefs: headersFromText(form.httpPolling.headerSecretRefsText),
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
        channel: (item.channel || "").trim() || null,
        topic: item.topic.trim(),
        qos: Number(item.qos),
        payloadRoot: (item.payloadRoot || "").trim() || null,
        topicVariables: Object.fromEntries((item.topicVariables || "").split(",")
          .map(value => value.trim()).filter(Boolean).map(value => {
            const separator = value.lastIndexOf(":");
            return [value.slice(0, separator).trim(), Number(value.slice(separator + 1))];
          })),
      })),
    } : null,
    opcUa: form.protocol === "opc-ua" ? {
      ...form.opcUa,
      publishingIntervalMs: Number(form.opcUa.publishingIntervalMs),
      samplingIntervalMs: Number(form.opcUa.samplingIntervalMs),
      maximumValueAgeMs: Number(form.opcUa.maximumValueAgeMs),
      maximumTimestampSkewMs: Number(form.opcUa.maximumTimestampSkewMs),
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
      maxMergeGap: Number(form.modbusTcp.maxMergeGap),
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
      sourceIdentityStaleAfterMs: Number(form.execution.sourceIdentityStaleAfterMs),
      maximumFutureTimestampSkewMs: Number(form.execution.maximumFutureTimestampSkewMs),
    },
    timestampMode: capabilities.intrinsicSourceTimestamp
      ? "source"
      : capabilities.sourceTimestamp ? form.timestampMode : "edge-received",
    timestampPath: capabilities.intrinsicSourceTimestamp ? "" : form.timestampPath.trim(),
    timestampEncoding: capabilities.sourceTimestamp ? form.timestampEncoding : "auto",
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
    processSpecification: form.processSpecification.enabled ? {
      eventType: form.processSpecification.eventType.trim(),
      idPath: form.processSpecification.idPath.trim(),
      versionPath: form.processSpecification.versionPath.trim(),
      namePath: form.processSpecification.namePath.trim() || null,
      parametersPath: capabilities.parameterObjectPath ? form.processSpecification.parametersPath.trim() || "." : ".",
      parameterMappings: form.processSpecification.parameterMappings
        .filter(item => item.dataItemCode.trim())
        .map(item => mappingPayload(item, descriptor)),
    } : null,
    lifecycle: form.lifecycle.enabled ? {
      mode: "discrete",
      activeContextKey: form.lifecycle.activeContextKey || null,
      activeValue: form.lifecycle.activeValue || "",
      startedEventType: form.lifecycle.startedEventType || "process.execution.started",
      completedEventType: form.lifecycle.completedEventType || "process.execution.completed",
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
export function validateIngestionTask(form, context = {}) {
  const descriptor = protocolDescriptor(form.protocol);
  const errors = {};
  const set = (path, message) => { if (!errors[path]) errors[path] = message; };

  if (!CODE_PATTERN.test(form.taskId.trim().toLowerCase()))
    set("taskId", "只能包含小写字母、数字、点、下划线和短横线。");
  if (!form.name.trim()) set("name", "配置名称不能为空。");
  if (!form.edgeId.trim()) set("edgeId", "请选择执行采集的现场节点。");
  if (!form.dataModel) set("dataModel", "请选择工艺数据模型。");
  if (!form.subjectId.trim()) set("subjectId", "设备编号不能为空。");
  if (!EVENT_TYPE_PATTERN.test(form.sampleEventType.trim()))
    set("sampleEventType", "事件类型格式无效，例如 process.sample。");

  const section = descriptor.section;
  const connectionErrors = descriptor.validateConnection(form[section] || {}) || {};
  Object.entries(connectionErrors).forEach(([field, message]) => set(`${section}.${field}`, message));

  if (descriptor.capabilities.connectTimeout &&
      !(Number(form.execution.timeoutMs) >= 1000 && Number(form.execution.timeoutMs) <= 300000))
    set("execution.timeoutMs", "连接与单次读取超时必须是 1000-300000ms。");
  if (descriptor.capabilities.reconnectDelay &&
      !(Number(form.execution.reconnectDelayMs) >= 100 && Number(form.execution.reconnectDelayMs) <= 300000))
    set("execution.reconnectDelayMs", "重连间隔必须是 100-300000ms。");
  if (form.protocol === "opc-ua" && form.status === "published" && form.opcUa.trustServerCertificate)
    set("opcUa.trustServerCertificate", "自动信任服务器证书只允许用于草稿探查，发布前必须建立信任链。");
  const staleAfter = Number(form.execution.sourceIdentityStaleAfterMs);
  if (!(staleAfter >= 0 && staleAfter <= 86400000))
    set("execution.sourceIdentityStaleAfterMs", "源身份停滞阈值必须为 0-86400000ms。");
  const futureSkew = Number(form.execution.maximumFutureTimestampSkewMs);
  if (!(futureSkew >= 0 && futureSkew <= 86400000))
    set("execution.maximumFutureTimestampSkewMs", "设备时间戳最大超前量必须为 0-86400000ms。");

  if (descriptor.capabilities.sourceTimestamp &&
      !descriptor.capabilities.intrinsicSourceTimestamp &&
      form.timestampMode === "source") {
    if (!form.timestampPath.trim()) set("timestampPath", "使用设备时间时必须指定时间来源。");
    else {
      const rowErrors = descriptor.addressing === ADDRESSING.jsonPath
        ? {}
        : descriptor.validatePoint(timestampProbeRow(form, descriptor), form) || {};
      const first = Object.values(rowErrors)[0];
      if (first) set("timestampPath", first);
      if ([ADDRESSING.modbusRegister, ADDRESSING.melsecDevice].includes(descriptor.addressing)) {
        const timestampRow = timestampProbeRow(form, descriptor);
        const encoding = form.timestampEncoding === "auto" ? "unix-ms" : form.timestampEncoding;
        if (encoding === "unix-ms" && !["int64", "uint64"].includes(timestampRow.sourceDataType))
          set("timestampPath", `unix-ms 时间戳需要 int64 或 uint64 点位，当前为 ${timestampRow.sourceDataType}。`);
        if (encoding === "unix-s" && !["int32", "uint32", "int64", "uint64"].includes(timestampRow.sourceDataType))
          set("timestampPath", `unix-s 时间戳需要 32/64 位整数点位，当前为 ${timestampRow.sourceDataType}。`);
        if (encoding === "iso-8601" && timestampRow.sourceDataType !== "string")
          set("timestampPath", `iso-8601 时间戳需要 string 点位，当前为 ${timestampRow.sourceDataType}。`);
      }
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
      const minimum = numberOrNull(row.minimum);
      const maximum = numberOrNull(row.maximum);
      if (minimum !== null && !Number.isFinite(minimum)) set(`${prefix}[${index}].minimum`, "有效下限必须是数字。");
      if (maximum !== null && !Number.isFinite(maximum)) set(`${prefix}[${index}].maximum`, "有效上限必须是数字。");
      if (minimum !== null && maximum !== null && minimum > maximum)
        set(`${prefix}[${index}].maximum`, "有效上限不能小于下限。");
      if ((row.acceptedQualityValues || "").trim() && !(row.qualityPath || "").trim())
        set(`${prefix}[${index}].qualityPath`, "填写允许质量值时必须指定质量字段。");
      if (row.missingValueBehavior === "use-default" && !(row.defaultValue ?? "").trim())
        set(`${prefix}[${index}].defaultValue`, "使用默认值时必须填写默认值。");
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

  if (form.processSpecification.enabled) {
    if (!form.processSpecification.idPath.trim()) set("processSpecification.idPath", "工艺规范编号来源不能为空。");
    if (!form.processSpecification.versionPath.trim()) set("processSpecification.versionPath", "工艺规范版本来源不能为空。");
    if (!EVENT_TYPE_PATTERN.test(form.processSpecification.eventType.trim()))
      set("processSpecification.eventType", "事件类型格式无效，例如 process.specification.applied。");
    validateRows(form.processSpecification.parameterMappings, "processSpecification.parameterMappings");
  }

  if (form.lifecycle.enabled && !(form.lifecycle.activeContextKey || "").trim())
    set("lifecycle.activeContextKey", "启用过程执行识别时必须指定生产状态来源。");

  if (form.status === "published" && Array.isArray(context.dataItems)) {
    const mapped = new Set(form.valueMappings.map(item => item.dataItemCode));
    const missing = context.dataItems.filter(item => !item.nullable && !mapped.has(item.code));
    if (missing.length)
      set("valueMappings", `发布前必须映射过程执行必需的数据项：${missing.map(item => item.code).join("、")}。`);
    form.valueMappings.forEach((mapping, index) => {
      const definition = context.dataItems.find(item => item.code === mapping.dataItemCode);
      if (!definition?.unit) return;
      if (!(mapping.sourceUnit || "").trim())
        set(`valueMappings[${index}].sourceUnit`, `发布前必须声明设备值单位；平台目标单位为 ${definition.unit}。`);
      else if (mapping.sourceUnit.trim() !== definition.unit && Number(mapping.scale) === 1 && Number(mapping.offset) === 0)
        set(`valueMappings[${index}].sourceUnit`, `设备单位与 ${definition.unit} 不同，必须配置明确换算。`);
    });
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
    const [device = "D", address = "", dataType = "int16"] = String(point.path).split(":");
    const [plainAddress = "", bitIndex = ""] = address.split(".");
    return {
      ...patch,
      melsecDevice: device.toUpperCase(),
      melsecAddress: plainAddress,
      bitIndex,
      sourceDataType: dataType || "int16",
    };
  }
  return {
    ...patch,
    sourcePath: point.path,
    sourceUnit: point.unit || "",
    ...(descriptor.addressing === ADDRESSING.nodeId
      ? { qualityPath: "$status", acceptedQualityValues: "Good" }
      : {}),
  };
}
