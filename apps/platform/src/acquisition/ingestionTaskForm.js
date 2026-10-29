
// Convert acquisition form state without conflating source connections, logical subjects, and machine context.
import {
  ADDRESSING,
  isModbusBitArea,
  melsecDevice,
  melsecMc3eSelectorError,
  normalizeSnmpOid,
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

function protocolManufacturerFor(protocol) {
  if (["melsec-a1e", "melsec-mc-3e"].includes(protocol)) return "Mitsubishi";
  if (protocol === "siemens-s7") return "Siemens";
  if (protocol === "ethernet-ip") return "Rockwell Automation";
  if (["omron-fins-tcp", "omron-fins-udp"].includes(protocol)) return "Omron";
  return "";
}

function initialTimestampMode(value, protocol) {
  if (protocol !== "snmp") return value.timestampMode || "source";
  return value.timestampMode === "source" && normalizeSnmpOid(value.timestampPath)
    ? "source"
    : "edge-received";
}

function defaultDeviceKindForProtocol(protocol) {
  if (protocol === "mtconnect") return "cnc";
  if (protocol === "bacnet-ip") return "building-automation";
  if (protocol === "snmp") return "network-infrastructure";
  if (protocolManufacturerFor(protocol)) return "plc";
  return "other";
}

export function createValueMapping(value = {}, protocol = "http-polling") {
  const descriptor = protocolDescriptor(protocol);
  const base = {
    dataItemCode: value.dataItemCode || "",
    sourcePath: value.sourcePath || "",
    required: value.required !== false,
    sourceDataType: value.sourceDataType || descriptor.defaultDataType || (descriptor.dataTypes.includes("auto") ? "auto" : "int16"),
    sourceByteLength: value.sourceByteLength ?? 16,
    sourceUnit: value.sourceUnit || "",
    scale: value.scale ?? 1,
    offset: value.offset ?? 0,
    qualityPath: [ADDRESSING.mtConnectDataItem, ADDRESSING.snmpOid].includes(descriptor.addressing) ? "" : value.qualityPath || "",
    acceptedQualityValues: [ADDRESSING.mtConnectDataItem, ADDRESSING.snmpOid].includes(descriptor.addressing)
      ? "" : (value.acceptedQualityValues || []).join(", "),
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
  const deviceKind = value.deviceProfile?.kind || defaultDeviceKindForProtocol(protocol);
  const defaultManufacturer = deviceKind === "plc" ? protocolManufacturerFor(protocol) : "";
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
    deviceProfile: {
      kind: deviceKind,
      manufacturer: value.deviceProfile?.manufacturer || defaultManufacturer,
      model: value.deviceProfile?.model || "",
    },
    subjectType: value.subjectType || "equipment",
    subjectId: value.subjectId || "",
    equipmentId: value.equipmentId || "",
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
    mtConnect: {
      baseUrl: value.mtConnect?.baseUrl || "",
      deviceNameOrUuid: value.mtConnect?.deviceNameOrUuid || "",
      pollIntervalMs: value.mtConnect?.pollIntervalMs ?? 1000,
      maximumResponseBytes: value.mtConnect?.maximumResponseBytes ?? 4194304,
      headersText: headersToText(value.mtConnect?.headers),
      headerSecretRefsText: headersToText(value.mtConnect?.headerSecretRefs),
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
    modbusRtu: {
      portName: value.modbusRtu?.portName || "",
      baudRate: value.modbusRtu?.baudRate ?? 9600,
      dataBits: value.modbusRtu?.dataBits ?? 8,
      parity: value.modbusRtu?.parity || "even",
      stopBits: value.modbusRtu?.stopBits || "one",
      unitId: value.modbusRtu?.unitId ?? 1,
      addressBase: value.modbusRtu?.addressBase || "zero-based",
      pollIntervalMs: value.modbusRtu?.pollIntervalMs ?? 1000,
      maxMergeGap: value.modbusRtu?.maxMergeGap ?? 8,
    },
    modbusAscii: {
      portName: value.modbusAscii?.portName || "",
      baudRate: value.modbusAscii?.baudRate ?? 9600,
      dataBits: value.modbusAscii?.dataBits ?? 7,
      parity: value.modbusAscii?.parity || "even",
      stopBits: value.modbusAscii?.stopBits || "one",
      unitId: value.modbusAscii?.unitId ?? 1,
      addressBase: value.modbusAscii?.addressBase || "zero-based",
      pollIntervalMs: value.modbusAscii?.pollIntervalMs ?? 1000,
      maxMergeGap: value.modbusAscii?.maxMergeGap ?? 8,
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
    melsecMc3E: {
      host: value.melsecMc3E?.host || "",
      port: value.melsecMc3E?.port ?? "",
      family: value.melsecMc3E?.family || "",
      networkNumber: value.melsecMc3E?.networkNumber ?? 0,
      pcNumber: value.melsecMc3E?.pcNumber ?? 255,
      moduleIoNumber: value.melsecMc3E?.moduleIoNumber ?? 1023,
      stationNumber: value.melsecMc3E?.stationNumber ?? 0,
      monitoringTimer: value.melsecMc3E?.monitoringTimer ?? 16,
      pollIntervalMs: value.melsecMc3E?.pollIntervalMs ?? 1000,
      maxMergeGap: value.melsecMc3E?.maxMergeGap ?? 0,
    },
    siemensS7: {
      host: value.siemensS7?.host || "",
      port: value.siemensS7?.port ?? 102,
      cpuType: value.siemensS7?.cpuType || "S71200",
      rack: value.siemensS7?.rack ?? 0,
      slot: value.siemensS7?.slot ?? 0,
      pollIntervalMs: value.siemensS7?.pollIntervalMs ?? 1000,
      maxMergeGap: value.siemensS7?.maxMergeGap ?? 0,
    },
    ethernetIp: {
      host: value.ethernetIp?.host || "",
      port: value.ethernetIp?.port ?? 44818,
      controllerFamily: value.ethernetIp?.controllerFamily || "control-logix",
      path: value.ethernetIp?.path || "1,0",
      pollIntervalMs: value.ethernetIp?.pollIntervalMs ?? 1000,
    },
    omronFinsTcp: {
      host: value.omronFinsTcp?.host || "",
      port: value.omronFinsTcp?.port ?? 9600,
      pollIntervalMs: value.omronFinsTcp?.pollIntervalMs ?? 1000,
    },
    omronFinsUdp: {
      host: value.omronFinsUdp?.host || "",
      port: value.omronFinsUdp?.port ?? 9600,
      clientNodeAddress: value.omronFinsUdp?.clientNodeAddress ?? "",
      serverNodeAddress: value.omronFinsUdp?.serverNodeAddress ?? "",
      pollIntervalMs: value.omronFinsUdp?.pollIntervalMs ?? 1000,
    },
    bacnetIp: {
      host: value.bacnetIp?.host || "",
      port: value.bacnetIp?.port ?? 47808,
      deviceInstance: value.bacnetIp?.deviceInstance ?? "",
      pollIntervalMs: value.bacnetIp?.pollIntervalMs ?? 1000,
    },
    bacnetMstp: {
      portName: value.bacnetMstp?.portName || "",
      baudRate: value.bacnetMstp?.baudRate ?? 38400,
      localStationAddress: value.bacnetMstp?.localStationAddress ?? "",
      maxMaster: value.bacnetMstp?.maxMaster ?? 127,
      maxInfoFrames: value.bacnetMstp?.maxInfoFrames ?? 1,
      deviceInstance: value.bacnetMstp?.deviceInstance ?? "",
      pollIntervalMs: value.bacnetMstp?.pollIntervalMs ?? 1000,
    },
    snmp: {
      host: value.snmp?.host || "",
      port: value.snmp?.port ?? 161,
      version: value.snmp?.version || "v3",
      browseRootOid: value.snmp?.browseRootOid || "1.3.6.1.2.1.1",
      contextName: value.snmp?.contextName || "",
      communitySecretRef: value.snmp?.communitySecretRef || "",
      userName: value.snmp?.userName || "",
      securityLevel: value.snmp?.securityLevel || "authPriv",
      authenticationProtocol: value.snmp?.authenticationProtocol || "sha256",
      authenticationSecretRef: value.snmp?.authenticationSecretRef || "",
      privacyProtocol: value.snmp?.privacyProtocol || "aes128",
      privacySecretRef: value.snmp?.privacySecretRef || "",
      pollIntervalMs: value.snmp?.pollIntervalMs ?? 5000,
    },
    execution: {
      timeoutMs: value.execution?.timeoutMs ?? 10000,
      reconnectDelayMs: value.execution?.reconnectDelayMs ?? 5000,
      sourceIdentityStaleAfterMs: value.execution?.sourceIdentityStaleAfterMs ?? 60000,
      maximumFutureTimestampSkewMs: value.execution?.maximumFutureTimestampSkewMs ?? 300000,
    },
    timestampMode: initialTimestampMode(value, protocol),
    timestampPath: protocol === "snmp" ? value.timestampPath || "" : value.timestampPath || "timestamp",
    timestampEncoding: value.timestampEncoding || "auto",
    sequencePath: protocol === "snmp"
      ? normalizeSnmpOid(value.sequencePath) || value.sequencePath || ""
      : value.sequencePath ?? "",
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
      ? { enabled: true, ...value.lifecycle, activeCondition: value.lifecycle.activeCondition ?? "equals" }
      : {
        enabled: false, mode: "discrete", activeContextKey: "run_active", activeCondition: "equals", activeValue: "1",
        startedEventType: "process.execution.started", completedEventType: "process.execution.completed",
        stepChangedEventType: "process.stage_changed",
      },
  };
}

export function applyProtocolChange(form, protocol) {
  const descriptor = protocolDescriptor(protocol);
  const fallbackType = descriptor.defaultDataType || (descriptor.dataTypes.includes("auto") ? "auto" : "int16");
  const protocolManufacturer = protocolManufacturerFor(protocol);
  const selectedManufacturer = form.deviceProfile?.manufacturer || "";
  const knownProtocolManufacturers = ["Mitsubishi", "Siemens", "Rockwell Automation", "Rockwell", "Omron"];
  const useDefaultProfile = !form.deviceProfile || form.deviceProfile.kind === "other";
  const deviceProfile = protocol === "snmp" && useDefaultProfile
    ? { ...form.deviceProfile, kind: "network-infrastructure" }
    : protocol === "bacnet-ip" && (!form.deviceProfile || form.deviceProfile.kind === "other")
    ? { ...form.deviceProfile, kind: "building-automation" }
    : protocol === "mtconnect" && (!form.deviceProfile || form.deviceProfile.kind === "other")
    ? { ...form.deviceProfile, kind: "cnc" }
    : protocolManufacturer && useDefaultProfile
    ? { ...form.deviceProfile, kind: "plc", manufacturer: protocolManufacturer }
    : protocolManufacturer && form.deviceProfile.kind === "plc" &&
      (!selectedManufacturer || knownProtocolManufacturers.includes(selectedManufacturer))
    ? { ...form.deviceProfile, manufacturer: protocolManufacturer }
    : form.deviceProfile;
  const normalizeRow = row => {
    const next = { ...row };
    if (!descriptor.dataTypes.includes(next.sourceDataType)) next.sourceDataType = fallbackType;
    if (!descriptor.capabilities.bitAddressing) next.bitIndex = "";
    if (!descriptor.capabilities.perTopicMapping) next.topic = "";
    if ([ADDRESSING.mtConnectDataItem, ADDRESSING.snmpOid].includes(descriptor.addressing)) {
      next.qualityPath = "";
      next.acceptedQualityValues = "";
    }
    return next;
  };
  return {
    ...form,
    protocol,
    deviceProfile,
    timestampMode: descriptor.capabilities.intrinsicSourceTimestamp
      ? "source"
      : descriptor.capabilities.sourceTimestamp
        ? protocol === "snmp"
          ? form.timestampMode === "source" && normalizeSnmpOid(form.timestampPath) ? "source" : "edge-received"
          : protocol === "ethernet-ip" ? "edge-received" : form.timestampMode
        : "edge-received",
    sequencePath: descriptor.capabilities.sequencePath
      ? protocol === "snmp" ? normalizeSnmpOid(form.sequencePath) || "" : form.sequencePath
      : "",
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
    qualityPath: [ADDRESSING.mtConnectDataItem, ADDRESSING.snmpOid].includes(descriptor.addressing)
      ? null : (item.qualityPath || "").trim() || null,
    acceptedQualityValues: [ADDRESSING.mtConnectDataItem, ADDRESSING.snmpOid].includes(descriptor.addressing)
      ? [] : (item.acceptedQualityValues || "").split(",").map(value => value.trim()).filter(Boolean),
    missingValueBehavior: item.missingValueBehavior || "inherit",
    defaultValue: (item.defaultValue ?? "").trim() || null,
    bitIndex: [ADDRESSING.siemensS7Address, ADDRESSING.melsecMc3EDevice].includes(descriptor.addressing)
      ? null : descriptor.capabilities.bitAddressing ? bitIndex : null,
    topic: descriptor.capabilities.perTopicMapping ? (item.topic || "").trim() || null : null,
    modbusArea: null,
    modbusAddress: null,
    modbusQuantity: 1,
    sourceByteLength: null,
    byteOrder: item.byteOrder,
    wordOrder: item.wordOrder,
    melsecDevice: null,
    melsecAddress: null,
    sourcePath: descriptor.addressing === ADDRESSING.mtConnectDataItem
      ? (item.sourcePath ?? "")
      : descriptor.addressing === ADDRESSING.snmpOid
        ? normalizeSnmpOid(item.sourcePath) || (item.sourcePath || "").trim()
        : (item.sourcePath || "").trim(),
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

  if (descriptor.addressing === ADDRESSING.siemensS7Address) {
    return {
      ...base,
      sourcePath: (item.sourcePath || "").trim().toUpperCase(),
      sourceByteLength: item.sourceDataType === "string" ? Number(item.sourceByteLength) || null : null,
      bitIndex: null,
    };
  }

  return base;
}

export function toPayload(form) {
  const descriptor = protocolDescriptor(form.protocol);
  const selectedModel = parseModelValue(form.dataModel);
  const capabilities = descriptor.capabilities;
  const snmpTimestampOid = form.protocol === "snmp" && form.timestampMode === "source"
    ? normalizeSnmpOid(form.timestampPath)
    : null;
  const timestampMode = capabilities.intrinsicSourceTimestamp
    ? "source"
    : capabilities.sourceTimestamp
      ? form.protocol === "snmp" && !snmpTimestampOid ? "edge-received" : form.timestampMode
      : "edge-received";
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
    deviceProfile: form.deviceProfile?.kind ? {
      kind: form.deviceProfile.kind,
      manufacturer: (form.deviceProfile.manufacturer || "").trim() || null,
      model: (form.deviceProfile.model || "").trim() || null,
    } : null,
    subjectType: form.subjectType.trim() || "equipment",
    subjectId: form.subjectId.trim(),
    equipmentId: (form.equipmentId || "").trim() || null,
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
    mtConnect: form.protocol === "mtconnect" ? {
      baseUrl: form.mtConnect.baseUrl.trim(),
      deviceNameOrUuid: form.mtConnect.deviceNameOrUuid,
      pollIntervalMs: Number(form.mtConnect.pollIntervalMs),
      maximumResponseBytes: Number(form.mtConnect.maximumResponseBytes),
      headers: headersFromText(form.mtConnect.headersText),
      headerSecretRefs: headersFromText(form.mtConnect.headerSecretRefsText),
    } : null,
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
    modbusRtu: form.protocol === "modbus-rtu" ? {
      ...form.modbusRtu,
      portName: form.modbusRtu.portName.trim(),
      baudRate: Number(form.modbusRtu.baudRate),
      dataBits: Number(form.modbusRtu.dataBits),
      unitId: Number(form.modbusRtu.unitId),
      pollIntervalMs: Number(form.modbusRtu.pollIntervalMs),
      maxMergeGap: Number(form.modbusRtu.maxMergeGap),
    } : null,
    modbusAscii: form.protocol === "modbus-ascii" ? {
      ...form.modbusAscii,
      portName: form.modbusAscii.portName.trim(),
      baudRate: Number(form.modbusAscii.baudRate),
      dataBits: Number(form.modbusAscii.dataBits),
      unitId: Number(form.modbusAscii.unitId),
      pollIntervalMs: Number(form.modbusAscii.pollIntervalMs),
      maxMergeGap: Number(form.modbusAscii.maxMergeGap),
    } : null,
    melsecA1E: form.protocol === "melsec-a1e" ? {
      ...form.melsecA1E,
      port: Number(form.melsecA1E.port),
      pollIntervalMs: Number(form.melsecA1E.pollIntervalMs),
      pcNumber: Number(form.melsecA1E.pcNumber),
      monitoringTimer: Number(form.melsecA1E.monitoringTimer),
      maxMergeGap: Number(form.melsecA1E.maxMergeGap),
    } : null,
    melsecMc3E: form.protocol === "melsec-mc-3e" ? {
      ...form.melsecMc3E,
      host: form.melsecMc3E.host.trim(),
      port: Number(form.melsecMc3E.port),
      family: form.melsecMc3E.family,
      networkNumber: Number(form.melsecMc3E.networkNumber),
      pcNumber: Number(form.melsecMc3E.pcNumber),
      moduleIoNumber: Number(form.melsecMc3E.moduleIoNumber),
      stationNumber: Number(form.melsecMc3E.stationNumber),
      monitoringTimer: Number(form.melsecMc3E.monitoringTimer),
      pollIntervalMs: Number(form.melsecMc3E.pollIntervalMs),
      maxMergeGap: Number(form.melsecMc3E.maxMergeGap),
    } : null,
    siemensS7: form.protocol === "siemens-s7" ? {
      ...form.siemensS7,
      port: Number(form.siemensS7.port),
      rack: Number(form.siemensS7.rack),
      slot: Number(form.siemensS7.slot),
      pollIntervalMs: Number(form.siemensS7.pollIntervalMs),
      maxMergeGap: Number(form.siemensS7.maxMergeGap),
    } : null,
    ethernetIp: form.protocol === "ethernet-ip" ? {
      ...form.ethernetIp,
      host: form.ethernetIp.host.trim(),
      port: Number(form.ethernetIp.port),
      controllerFamily: form.ethernetIp.controllerFamily,
      path: form.ethernetIp.controllerFamily === "control-logix" ? form.ethernetIp.path.trim() : "",
      pollIntervalMs: Number(form.ethernetIp.pollIntervalMs),
    } : null,
    omronFinsTcp: form.protocol === "omron-fins-tcp" ? {
      ...form.omronFinsTcp,
      host: form.omronFinsTcp.host.trim(),
      port: Number(form.omronFinsTcp.port),
      pollIntervalMs: Number(form.omronFinsTcp.pollIntervalMs),
    } : null,
    omronFinsUdp: form.protocol === "omron-fins-udp" ? {
      ...form.omronFinsUdp,
      host: form.omronFinsUdp.host.trim(),
      port: Number(form.omronFinsUdp.port),
      clientNodeAddress: Number(form.omronFinsUdp.clientNodeAddress),
      serverNodeAddress: Number(form.omronFinsUdp.serverNodeAddress),
      pollIntervalMs: Number(form.omronFinsUdp.pollIntervalMs),
    } : null,
    bacnetIp: form.protocol === "bacnet-ip" ? {
      ...form.bacnetIp,
      host: form.bacnetIp.host.trim(),
      port: Number(form.bacnetIp.port),
      deviceInstance: Number(form.bacnetIp.deviceInstance),
      pollIntervalMs: Number(form.bacnetIp.pollIntervalMs),
    } : null,
    bacnetMstp: form.protocol === "bacnet-mstp" ? {
      ...form.bacnetMstp,
      portName: form.bacnetMstp.portName.trim(),
      baudRate: Number(form.bacnetMstp.baudRate),
      localStationAddress: form.bacnetMstp.localStationAddress === ""
        ? null : Number(form.bacnetMstp.localStationAddress),
      maxMaster: Number(form.bacnetMstp.maxMaster),
      maxInfoFrames: Number(form.bacnetMstp.maxInfoFrames),
      deviceInstance: Number(form.bacnetMstp.deviceInstance),
      pollIntervalMs: Number(form.bacnetMstp.pollIntervalMs),
    } : null,
    snmp: form.protocol === "snmp" ? {
      host: form.snmp.host.trim(),
      port: Number(form.snmp.port),
      version: form.snmp.version,
      browseRootOid: normalizeSnmpOid(form.snmp.browseRootOid) || form.snmp.browseRootOid.trim(),
      contextName: form.snmp.version === "v3" ? form.snmp.contextName.trim() || null : null,
      communitySecretRef: form.snmp.version === "v2c" ? form.snmp.communitySecretRef.trim() : null,
      userName: form.snmp.version === "v3" ? form.snmp.userName.trim() : null,
      securityLevel: form.snmp.version === "v3" ? form.snmp.securityLevel : null,
      authenticationProtocol: form.snmp.version === "v3" && ["authNoPriv", "authPriv"].includes(form.snmp.securityLevel)
        ? form.snmp.authenticationProtocol : null,
      authenticationSecretRef: form.snmp.version === "v3" && ["authNoPriv", "authPriv"].includes(form.snmp.securityLevel)
        ? form.snmp.authenticationSecretRef.trim() : null,
      privacyProtocol: form.snmp.version === "v3" && form.snmp.securityLevel === "authPriv"
        ? form.snmp.privacyProtocol : null,
      privacySecretRef: form.snmp.version === "v3" && form.snmp.securityLevel === "authPriv"
        ? form.snmp.privacySecretRef.trim() : null,
      pollIntervalMs: Number(form.snmp.pollIntervalMs),
    } : null,
    execution: {
      timeoutMs: Number(form.execution.timeoutMs),
      reconnectDelayMs: Number(form.execution.reconnectDelayMs),
      sourceIdentityStaleAfterMs: Number(form.execution.sourceIdentityStaleAfterMs),
      maximumFutureTimestampSkewMs: Number(form.execution.maximumFutureTimestampSkewMs),
    },
    timestampMode,
    timestampPath: capabilities.intrinsicSourceTimestamp
      ? ""
      : form.protocol === "snmp" ? timestampMode === "source" ? snmpTimestampOid || "" : "" : form.timestampPath.trim(),
    timestampEncoding: capabilities.sourceTimestamp ? form.timestampEncoding : "auto",
    sequencePath: capabilities.sequencePath
      ? form.protocol === "snmp"
        ? normalizeSnmpOid(form.sequencePath) || form.sequencePath.trim() || null
        : form.sequencePath.trim() || null
      : null,
    sampleEventType: form.sampleEventType.trim(),
    staticContext: objectFromPairs(form.staticContextPairs),
    contextMappings: form.contextMappings
      .filter(item => item.contextKey.trim() || item.sourcePath.trim())
      .map(item => ({
        contextKey: item.contextKey.trim(),
        sourcePath: descriptor.addressing === ADDRESSING.mtConnectDataItem
          ? item.sourcePath : item.sourcePath.trim(),
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
      activeCondition: form.lifecycle.activeCondition ?? "equals",
      ...(form.lifecycle.activeCondition === "nonzero" ? {} : { activeValue: form.lifecycle.activeValue || "" }),
      startedEventType: form.lifecycle.startedEventType || "process.execution.started",
      completedEventType: form.lifecycle.completedEventType || "process.execution.completed",
      stepChangedEventType: form.lifecycle.stepChangedEventType || "process.stage_changed",
    } : null,
  };
}

const CODE_PATTERN = /^[a-z0-9][a-z0-9._-]{0,127}$/;
const EVENT_TYPE_PATTERN = /^[a-z][a-z0-9]*(?:\.[a-z][a-z0-9_]*)+$/;

export function validateIngestionTask(form, context = {}) {
  const descriptor = protocolDescriptor(form.protocol);
  const errors = {};
  const set = (path, message) => { if (!errors[path]) errors[path] = message; };

  if (!CODE_PATTERN.test(form.taskId.trim().toLowerCase()))
    set("taskId", "只能包含小写字母、数字、点、下划线和短横线。");
  if (!form.name.trim()) set("name", "配置名称不能为空。");
  if (!form.edgeId.trim()) set("edgeId", "请选择执行采集的现场节点。");
  if (!form.dataModel) set("dataModel", "请选择工艺变量。");
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
  if (descriptor.addressing === ADDRESSING.snmpOid && form.sequencePath.trim() && !normalizeSnmpOid(form.sequencePath))
    set("sequencePath", "序号来源必须是有效的数字 OID，例如 1.3.6.1.2.1.1.5.0；不支持 MIB 符号名称。");
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
      if ([ADDRESSING.modbusRegister, ADDRESSING.melsecDevice, ADDRESSING.melsecMc3EDevice, ADDRESSING.siemensS7Address, ADDRESSING.ethernetIpTag, ADDRESSING.omronFinsAddress, ADDRESSING.snmpOid].includes(descriptor.addressing)) {
        const timestampRow = timestampProbeRow(form, descriptor);
        if (descriptor.id === "ethernet-ip") {
          const mappedTimestamp = form.valueMappings.find(item =>
            (item.sourcePath || "").trim() === form.timestampPath.trim());
          if (mappedTimestamp) timestampRow.sourceDataType = mappedTimestamp.sourceDataType;
        }
        const shouldValidateTimestampType = descriptor.id !== "snmp" ||
          (form.timestampEncoding !== "auto" && timestampRow.sourceDataType !== "auto");
        if (shouldValidateTimestampType) {
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
      if ((row.acceptedQualityValues || "").trim() && !(row.qualityPath || "").trim())
        set(`${prefix}[${index}].qualityPath`, "填写允许质量值时必须指定质量字段。");
      if (row.missingValueBehavior === "use-default" && !(row.defaultValue ?? "").trim())
        set(`${prefix}[${index}].defaultValue`, "使用默认值时必须填写默认值。");
      if (descriptor.addressing === ADDRESSING.melsecMc3EDevice && (row.qualityPath || "").trim()) {
        const selectorError = melsecMc3eSelectorError(row.qualityPath, null, form.melsecMc3E?.family);
        if (selectorError) set(`${prefix}[${index}].qualityPath`, selectorError);
      }
      const rowErrors = descriptor.validatePoint(row, form) || {};
      Object.entries(rowErrors).forEach(([field, message]) => set(`${prefix}[${index}].${field}`, message));
    });
  };
  validateRows(form.valueMappings, "valueMappings");
  if (!form.valueMappings.some(item => item.dataItemCode.trim()))
    set("valueMappings", "至少需要配置一个采集点位。");

  if (descriptor.id === "ethernet-ip") {
    const sourceTypes = new Map();
    form.valueMappings.forEach((item, index) => {
      const sourcePath = (item.sourcePath || "").trim();
      if (sourcePath) {
        const existingType = sourceTypes.get(sourcePath);
        if (existingType && existingType !== item.sourceDataType)
          set(`valueMappings[${index}].sourceDataType`, `同一 EtherNet/IP 标签不能配置多个类型（已配置 ${existingType}）。`);
        else
          sourceTypes.set(sourcePath, item.sourceDataType);
      }
      if ((item.qualityPath || "").trim())
        set(`valueMappings[${index}].qualityPath`, "EtherNet/IP 当前不支持未声明数据类型的质量标签映射。");
    });
    const configuredTags = new Set(form.valueMappings.map(item => (item.sourcePath || "").trim()).filter(Boolean));
    if (form.timestampMode === "source" && form.timestampPath.trim()) configuredTags.add(form.timestampPath.trim());
    if (configuredTags.size > 256)
      set("valueMappings", `EtherNet/IP 单任务最多读取 256 个不同标签，当前配置 ${configuredTags.size} 个。`);
  }

  if (descriptor.id === "mtconnect") {
    const selectors = [
      ...form.valueMappings.map(item => item.sourcePath),
      ...form.contextMappings.map(item => item.sourcePath),
    ].map(value => String(value ?? "")).filter(value => value.length > 0);
    const ids = [...new Set(selectors)];
    if (ids.length > 128)
      set("valueMappings", `MTConnect 单任务最多读取 128 个不同 DataItem ID，当前配置 ${ids.length} 个。`);
    const pathLength = ids.reduce((length, id) => length + encodeURIComponent(id).length + 18, 0);
    if (pathLength > 8192)
      set("valueMappings", "MTConnect 当前过滤路径超过 8 KiB，请减少 DataItem ID 或拆分采集任务。");
    if (form.processSpecification.enabled)
      set("processSpecification.enabled", "MTConnect 首期不支持把不同 DataItem 组合成设备配方参数对象，请关闭配方版本识别。");
  }

  form.contextMappings.forEach((item, index) => {
    if (!item.contextKey.trim() && !item.sourcePath.trim()) return;
    if (!CODE_PATTERN.test(item.contextKey.trim().toLowerCase()))
      set(`contextMappings[${index}].contextKey`, "上下文键格式无效。");
    if (!item.sourcePath.trim()) set(`contextMappings[${index}].sourcePath`, "设备来源不能为空。");
    if (descriptor.id === "ethernet-ip" && item.sourcePath.trim())
      set(`contextMappings[${index}].sourcePath`, "EtherNet/IP 当前不支持未声明数据类型的动态上下文标签。");
    if (descriptor.addressing === ADDRESSING.melsecMc3EDevice && item.sourcePath.trim()) {
      const selectorError = melsecMc3eSelectorError(item.sourcePath, null, form.melsecMc3E?.family);
      if (selectorError) set(`contextMappings[${index}].sourcePath`, selectorError);
    }
    if (descriptor.addressing === ADDRESSING.mtConnectDataItem && item.sourcePath.trim()) {
      const selectorError = descriptor.validatePoint({ sourcePath: item.sourcePath });
      if (selectorError.sourcePath) set(`contextMappings[${index}].sourcePath`, selectorError.sourcePath);
    }
  });

  if (form.processSpecification.enabled) {
    if (descriptor.id === "ethernet-ip")
      set("processSpecification.idPath", "EtherNet/IP 当前不支持源数据类型未显式声明的配方版本辅助标签。");
    if (!form.processSpecification.idPath.trim()) set("processSpecification.idPath", "配方版本编号来源不能为空。");
    if (!form.processSpecification.versionPath.trim()) set("processSpecification.versionPath", "配方版本号来源不能为空。");
    if (descriptor.addressing === ADDRESSING.melsecMc3EDevice) {
      for (const field of ["idPath", "versionPath", "namePath"]) {
        const selector = (form.processSpecification[field] || "").trim();
        if (!selector) continue;
        const selectorError = melsecMc3eSelectorError(selector, null, form.melsecMc3E?.family);
        if (selectorError) set(`processSpecification.${field}`, selectorError);
      }
    }
    if (!EVENT_TYPE_PATTERN.test(form.processSpecification.eventType.trim()))
      set("processSpecification.eventType", "事件类型格式无效，例如 process.specification.applied。");
    validateRows(form.processSpecification.parameterMappings, "processSpecification.parameterMappings");
  }

  if (form.lifecycle.enabled) {
    if (!(form.lifecycle.activeContextKey || "").trim())
      set("lifecycle.activeContextKey", "启用过程执行识别时必须指定生产状态来源。");
    const activeCondition = form.lifecycle.activeCondition ?? "equals";
    if (!["equals", "nonzero"].includes(activeCondition))
      set("lifecycle.activeCondition", "请选择等于指定值或非零生产序号。");
    if (activeCondition === "equals" && !(form.lifecycle.activeValue || "").trim())
      set("lifecycle.activeValue", "使用等于指定值时必须填写生产中的取值。");
  }

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
  if (descriptor.addressing === ADDRESSING.siemensS7Address) {
    const address = text.toUpperCase();
    const suffix = /(?:DB)?([XBWDL])\d+(?:\.([0-7]))?$/.exec(address)?.[1] || "";
    const bit = /\.([0-7])$/.test(address);
    const sourceDataType = bit ? "boolean" : ({ W: "uint16", D: "uint32", L: "uint64", B: "byte" }[suffix] || "uint64");
    return { sourcePath: address, sourceDataType, sourceByteLength: null };
  }
  if (descriptor.addressing === ADDRESSING.ethernetIpTag) {
    const sourceDataType = form.timestampEncoding === "unix-s"
      ? "int32"
      : form.timestampEncoding === "iso-8601" ? "string" : "int64";
    return { sourcePath: text, sourceDataType, sourceByteLength: null };
  }
  if (descriptor.addressing === ADDRESSING.omronFinsAddress) {
    const sourceDataType = text.split(":")[2]?.toLowerCase() || "int64";
    return { sourcePath: text, sourceDataType, sourceByteLength: null, wordOrder: "high-low" };
  }
  if (descriptor.addressing === ADDRESSING.melsecMc3EDevice) {
    const sourceDataType = text.split(":")[2]?.toLowerCase() ||
      form.valueMappings.find(row => (row.sourcePath || "").trim() === text)?.sourceDataType || "uint64";
    return { sourcePath: text, sourceDataType, sourceByteLength: null, wordOrder: "high-low" };
  }
  if (descriptor.addressing === ADDRESSING.snmpOid) {
    const sourcePath = normalizeSnmpOid(text) || text;
    const mappedTimestamp = form.valueMappings.find(row => normalizeSnmpOid(row.sourcePath) === sourcePath);
    const sourceDataType = mappedTimestamp?.sourceDataType || (form.timestampEncoding === "auto"
      ? "auto" : form.timestampEncoding === "iso-8601"
        ? "string" : form.timestampEncoding === "unix-s" ? "uint32" : "uint64");
    return { sourcePath, sourceDataType, sourceByteLength: null };
  }
  return { sourcePath: text };
}

export function patchFromProbePoint(point, dataItemCode, dataItems, descriptor, existingMappings = []) {
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
  if (descriptor.addressing === ADDRESSING.siemensS7Address) {
    const sourcePath = String(point.path || "").trim().toUpperCase();
    const sourceDataType = normalizeS7ProbeType(point.dataType) || rowTypeFromS7Address(point.path);
    const existingPoint = existingMappings.find(row => String(row.sourcePath || "").trim().toUpperCase() === sourcePath);
    return {
      ...patch,
      sourcePath,
      sourceDataType,
      sourceByteLength: sourceDataType === "string" ? existingPoint?.sourceByteLength || "" : null,
      sourceUnit: point.unit || "",
    };
  }
  if (descriptor.addressing === ADDRESSING.ethernetIpTag) {
    const sourcePath = String(point.path || "").trim();
    const sourceDataType = String(point.dataType || "").trim().toLowerCase();
    return {
      ...patch,
      sourcePath,
      sourceDataType: descriptor.dataTypes.includes(sourceDataType) ? sourceDataType : descriptor.defaultDataType,
      sourceUnit: point.unit || "",
    };
  }
  if (descriptor.addressing === ADDRESSING.omronFinsAddress) {
    const sourcePath = String(point.path || "").trim();
    const sourceDataType = sourcePath.split(":")[2]?.toLowerCase() || "int16";
    return {
      ...patch,
      sourcePath,
      sourceDataType: descriptor.dataTypes.includes(sourceDataType) ? sourceDataType : descriptor.defaultDataType,
      wordOrder: "high-low",
      sourceUnit: point.unit || "",
    };
  }
  if (descriptor.addressing === ADDRESSING.melsecMc3EDevice) {
    const sourcePath = String(point.path || "").trim();
    const embeddedType = sourcePath.split(":")[2]?.toLowerCase();
    const reportedType = String(point.dataType || "").trim().toLowerCase();
    const sourceDataType = embeddedType || (descriptor.dataTypes.includes(reportedType) ? reportedType : descriptor.defaultDataType);
    return { ...patch, sourcePath, sourceDataType, wordOrder: "high-low", sourceUnit: point.unit || "" };
  }
  if (descriptor.addressing === ADDRESSING.mtConnectDataItem) {
    return {
      ...patch,
      sourcePath: String(point.path ?? ""),
      sourceDataType: "auto",
      sourceUnit: point.unit || "",
    };
  }
  if (descriptor.addressing === ADDRESSING.bacnetObject) {
    return {
      ...patch,
      sourcePath: String(point.path || "").trim().toLowerCase(),
      sourceDataType: "auto",
      sourceUnit: point.unit || "",
    };
  }
  if (descriptor.addressing === ADDRESSING.snmpOid) {
    const sourcePath = normalizeSnmpOid(point.path) || String(point.path || "").trim();
    const reportedType = String(point.dataType || "").trim().toLowerCase();
    return {
      ...patch,
      sourcePath,
      sourceDataType: descriptor.dataTypes.includes(reportedType) ? reportedType : "auto",
      sourceUnit: point.unit || "",
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

function rowTypeFromS7Address(value) {
  const address = String(value || "").trim().toUpperCase();
  if (/\.([0-7])$/.test(address)) return "boolean";
  const suffix = /(?:DB)?([BWDL])\d+$/.exec(address)?.[1];
  return ({ B: "byte", W: "uint16", D: "uint32", L: "uint64" })[suffix] || "int16";
}

function normalizeS7ProbeType(value) {
  return ({
    boolean: "boolean", byte: "byte", int16: "int16", uint16: "uint16",
    int32: "int32", uint32: "uint32", single: "float32", float32: "float32",
    int64: "int64", uint64: "uint64", double: "float64", float64: "float64", string: "string",
  })[String(value || "").trim().toLowerCase()] || "";
}
