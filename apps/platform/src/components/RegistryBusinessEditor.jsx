import { useEffect, useMemo, useState } from "react";
import { postJson } from "../api/http";
import { extractRows, useApi } from "../hooks/useApi";
import { Alert, Button, Card, Field, Input, Select, Textarea } from "../ui/components";

const codePattern = /^[a-z0-9][a-z0-9._-]{0,127}$/;
const dataTypes = [
  ["double", "小数"],
  ["integer", "整数"],
  ["boolean", "是/否"],
  ["string", "文本"],
];
const featureOptions = [
  ["mean", "平均值"],
  ["min", "最小值"],
  ["max", "最大值"],
  ["range", "范围"],
  ["stddev", "标准差"],
  ["median", "中位数"],
  ["p05", "5% 分位"],
  ["p95", "95% 分位"],
  ["integral", "积分"],
  ["slope", "趋势斜率"],
];

const acquisitionProtocols = [
  ["http-polling", "HTTP 接口", "轮询设备或网关提供的 JSON 接口"],
  ["mqtt", "MQTT", "订阅设备或网关发布的 JSON 消息"],
  ["opc-ua", "OPC UA", "发现端点并浏览服务器变量节点"],
  ["modbus-tcp", "Modbus TCP", "按寄存器区、地址和数据格式读取"],
  ["melsec-a1e", "三菱 MC 1E", "面向 FX3U 等 A 兼容 1E 帧设备"],
];

const registerDataTypes = ["int16", "uint16", "int32", "uint32", "float32", "int64", "uint64", "float64", "string"];

function parseMelsecPath(path = "") {
  const [device = "D", address = "", dataType = "int16", length = ""] = path.split(":");
  return { device: device || "D", address, dataType: dataType || "int16", length };
}

function registerQuantity(dataType, stringLength, fallback = 1) {
  if (["int32", "uint32", "float32"].includes(dataType)) return 2;
  if (["int64", "uint64", "float64"].includes(dataType)) return 4;
  if (dataType === "string") return Math.max(1, Number(stringLength || fallback) || 1);
  return 1;
}

const blankPair = () => ({ key: "", value: "" });
const pairsFromObject = value => Object.entries(value || {}).map(([key, pairValue]) => ({ key, value: pairValue }));
const objectFromPairs = pairs => Object.fromEntries(
  pairs.filter(pair => pair.key.trim() && pair.value.trim()).map(pair => [pair.key.trim(), pair.value.trim()]),
);
const localDateTime = value => value ? new Date(value).toISOString().slice(0, 16) : "";
const apiDateTime = value => value ? new Date(value).toISOString() : null;
const numberOrNull = value => value === "" || value === null || value === undefined ? null : Number(value);
const modelValue = (id, version) => id ? `${id}::${version || 1}` : "";
const versionedStatus = (value, version) => version === undefined ? value.status || "draft" : "draft";
const parseModelValue = value => {
  const [id = "", version = "1"] = value.split("::");
  return { id, version: Number(version) || 1 };
};

function qualityItem(value = {}) {
  return {
    definition: modelValue(value.definitionCode, value.definitionVersion),
    sequence: value.sequence ?? 10,
    required: value.required !== false,
    requiresAttachment: Boolean(value.requiresAttachment),
    requiresReview: Boolean(value.requiresReview),
  };
}

function dataItem(value = {}) {
  return {
    code: value.code || "",
    sourceField: value.sourceField || "",
    dataType: value.dataType || "double",
    unit: value.unit || "",
    category: value.category || "process",
    nullable: value.nullable !== false,
  };
}

function recipeParameter(value = {}) {
  return {
    code: value.code || "",
    sourceField: value.sourceField || "",
    dataType: value.dataType || "double",
    unit: value.unit || "",
    nullable: value.nullable !== false,
  };
}

function recipeValue(value = {}) {
  return {
    code: value.code || "",
    value: value.value === undefined || value.value === null ? "" : String(value.value),
    dataType: value.dataType || (typeof value.value === "boolean" ? "boolean" : typeof value.value === "number" ? "double" : "string"),
  };
}

function analysisSignal(value = {}) {
  return {
    dataItemCode: value.dataItemCode || "",
    includeTrace: value.includeTrace !== false,
    features: value.features || [],
  };
}

function valueMapping(value = {}) {
  const melsec = parseMelsecPath(value.sourcePath);
  return {
    dataItemCode: value.dataItemCode || "",
    sourcePath: value.sourcePath || "",
    required: value.required !== false,
    sourceDataType: value.sourceDataType || "auto",
    scale: value.scale ?? 1,
    offset: value.offset ?? 0,
    modbusArea: value.modbusArea || "holding-register",
    modbusAddress: value.modbusAddress ?? "",
    modbusQuantity: value.modbusQuantity ?? 1,
    byteOrder: value.byteOrder || "big-endian",
    wordOrder: value.wordOrder || "high-low",
    melsecDevice: melsec.device,
    melsecAddress: melsec.address,
    melsecStringLength: melsec.length || "16",
  };
}

export function createRegistryBusinessForm(kind, value = {}, version) {
  switch (kind) {
    case "qualityPlan":
      return {
        planId: value.planId || "",
        version: version ?? value.version ?? 1,
        name: value.name || "",
        description: value.description || "",
        status: versionedStatus(value, version),
        priority: value.priority ?? 0,
        effectiveFrom: localDateTime(value.effectiveFrom),
        effectiveTo: localDateTime(value.effectiveTo),
        scope: {
          productSeries: value.scope?.productSeries || "",
          productCode: value.scope?.productCode || "",
          recipeId: value.scope?.recipeId || "",
          machineId: value.scope?.machineId || "",
        },
        contextPairs: pairsFromObject(value.scope?.contextSelector),
        items: (value.items || []).length ? value.items.map(qualityItem) : [qualityItem()],
      };
    case "processModel":
      return {
        modelId: value.modelId || "",
        version: version ?? value.version ?? 1,
        name: value.name || "",
        description: value.description || "",
        status: versionedStatus(value, version),
        dataItems: (value.acquisition?.dataItems || []).length ? value.acquisition.dataItems.map(dataItem) : [dataItem()],
        recipeParameters: (value.recipeParameters || []).map(recipeParameter),
      };
    case "recipeVersion":
      return {
        recipeId: value.recipeId || "",
        version: version ?? value.version ?? 1,
        name: value.name || "",
        basedOnVersion: value.basedOnVersion ?? "",
        dataModel: modelValue(value.dataModelId, value.dataModelVersion),
        status: versionedStatus(value, version),
        contextPairs: pairsFromObject(value.contextSelector),
        values: (value.values || []).map(recipeValue),
      };
    case "analysisPlan":
      return {
        planId: value.planId || "",
        version: version ?? value.version ?? 1,
        name: value.name || "",
        description: value.description || "",
        status: versionedStatus(value, version),
        dataModel: modelValue(value.dataModelId, value.dataModelVersion),
        analysisScope: value.analysisScope || "production-cycle",
        alignmentMode: value.alignmentMode || "stage-relative",
        cohortDimension: value.cohortDimension || "",
        comparisonKeys: (value.comparisonKeys || ["product_series"]).join(", "),
        contextPairs: pairsFromObject(value.contextSelector),
        signals: (value.signals || []).length ? value.signals.map(analysisSignal) : [analysisSignal()],
      };
    case "acquisitionProfile":
      return {
        profileId: value.profileId || "",
        version: version ?? value.version ?? 1,
        name: value.name || "",
        status: versionedStatus(value, version),
        edgeId: value.edgeId || "",
        protocol: value.protocol || "http-polling",
        dataModel: modelValue(value.dataModelId, value.dataModelVersion),
        source: value.source || "",
        subjectType: value.subjectType || "equipment",
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
          topics: (value.mqtt?.topics || []).length ? value.mqtt.topics.map(item => ({ topic: item.topic, qos: item.qos ?? 0 })) : [{ topic: "", qos: 0 }],
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
          monitoringTimer: value.melsecA1E?.monitoringTimer ?? 16,
          dataCode: value.melsecA1E?.dataCode || "binary",
          pcNumber: value.melsecA1E?.pcNumber ?? 255,
          wordOrderLayout: value.melsecA1E?.wordOrderLayout || "A",
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
        contextMappings: (value.contextMappings || []).map(item => ({ ...item })),
        valueMappings: (value.valueMappings || []).length ? value.valueMappings.map(valueMapping) : [valueMapping()],
        recipe: value.recipe ? {
          enabled: true,
          eventType: value.recipe.eventType || "recipe.applied",
          idPath: value.recipe.idPath || "",
          versionPath: value.recipe.versionPath || "",
          namePath: value.recipe.namePath || "",
          parametersPath: value.recipe.parametersPath || "",
          parameterMappings: (value.recipe.parameterMappings || []).map(valueMapping),
        } : { enabled: false, eventType: "recipe.applied", idPath: "", versionPath: "", namePath: "", parametersPath: "", parameterMappings: [] },
        lifecycle: value.lifecycle ? {
          enabled: true,
          ...value.lifecycle,
        } : {
          enabled: false,
          mode: "discrete-cycle",
          activeContextKey: "run_active",
          activeValue: "1",
          startedEventType: "cycle.started",
          completedEventType: "cycle.completed",
          stepChangedEventType: "process.stage_changed",
        },
      };
    default:
      return {};
  }
}

function acquisitionPayload(form) {
  const selectedModel = parseModelValue(form.dataModel);
  const mappingPayload = item => ({
    dataItemCode: item.dataItemCode.trim(),
    sourcePath: form.protocol === "modbus-tcp"
      ? ""
      : form.protocol === "melsec-a1e"
        ? `${item.melsecDevice}:${item.melsecAddress}:${item.sourceDataType}${item.sourceDataType === "string" ? `:${item.melsecStringLength}` : ""}`
        : item.sourcePath.trim(),
    required: item.required,
    sourceDataType: item.sourceDataType,
    scale: Number(item.scale),
    offset: Number(item.offset),
    modbusArea: form.protocol === "modbus-tcp" ? item.modbusArea : null,
    modbusAddress: form.protocol === "modbus-tcp" ? numberOrNull(item.modbusAddress) : null,
    modbusQuantity: form.protocol === "modbus-tcp"
      ? registerQuantity(item.sourceDataType, item.modbusQuantity, item.modbusQuantity)
      : Number(item.modbusQuantity) || 1,
    byteOrder: item.byteOrder,
    wordOrder: item.wordOrder,
  });
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
      username: form.mqtt.username.trim() || null,
      passwordSecretRef: form.mqtt.passwordSecretRef.trim() || null,
      caCertificatePath: form.mqtt.caCertificatePath.trim() || null,
      clientCertificatePath: form.mqtt.clientCertificatePath.trim() || null,
      clientCertificatePasswordSecretRef: form.mqtt.clientCertificatePasswordSecretRef.trim() || null,
      topics: form.mqtt.topics.filter(item => item.topic.trim()).map(item => ({ topic: item.topic.trim(), qos: Number(item.qos) })),
    } : null,
    opcUa: form.protocol === "opc-ua" ? {
      ...form.opcUa,
      username: form.opcUa.username.trim() || null,
      passwordSecretRef: form.opcUa.passwordSecretRef.trim() || null,
      clientCertificatePath: form.opcUa.clientCertificatePath.trim() || null,
      clientCertificatePasswordSecretRef: form.opcUa.clientCertificatePasswordSecretRef.trim() || null,
      publishingIntervalMs: Number(form.opcUa.publishingIntervalMs),
      samplingIntervalMs: Number(form.opcUa.samplingIntervalMs),
    } : null,
    modbusTcp: form.protocol === "modbus-tcp" ? {
      ...form.modbusTcp,
      port: Number(form.modbusTcp.port),
      unitId: Number(form.modbusTcp.unitId),
      addressBase: form.modbusTcp.addressBase,
      pollIntervalMs: Number(form.modbusTcp.pollIntervalMs),
    } : null,
    melsecA1E: form.protocol === "melsec-a1e" ? {
      ...form.melsecA1E,
      port: Number(form.melsecA1E.port),
      pollIntervalMs: Number(form.melsecA1E.pollIntervalMs),
      monitoringTimer: Number(form.melsecA1E.monitoringTimer),
      pcNumber: Number(form.melsecA1E.pcNumber),
      dataCode: form.melsecA1E.dataCode,
    } : null,
    execution: {
      timeoutMs: Number(form.execution.timeoutMs),
      reconnectDelayMs: Number(form.execution.reconnectDelayMs),
    },
    timestampMode: form.protocol === "melsec-a1e" ? "edge-received" : form.protocol === "opc-ua" ? "source" : form.timestampMode,
    timestampPath: form.timestampPath.trim(),
    sequencePath: form.sequencePath.trim() || null,
    sampleEventType: "process.sample",
    staticContext: objectFromPairs(form.staticContextPairs),
    contextMappings: form.contextMappings.filter(item => item.contextKey.trim() || item.sourcePath.trim()).map(item => ({
      contextKey: item.contextKey.trim(),
      sourcePath: item.sourcePath.trim(),
      required: Boolean(item.required),
    })),
    valueMappings: form.valueMappings.map(mappingPayload),
    recipe: form.recipe.enabled ? {
      eventType: "recipe.applied",
      idPath: form.recipe.idPath.trim(),
      versionPath: form.recipe.versionPath.trim(),
      namePath: form.recipe.namePath.trim() || null,
      parametersPath: form.recipe.parametersPath.trim() || ".",
      parameterMappings: form.recipe.parameterMappings.map(mappingPayload),
    } : null,
    lifecycle: form.lifecycle.enabled ? {
      mode: form.lifecycle.mode,
      activeContextKey: (form.lifecycle.activeContextKey || "").trim() || null,
      activeValue: (form.lifecycle.activeValue || "").trim(),
      startedEventType: "cycle.started",
      completedEventType: "cycle.completed",
      stepChangedEventType: "process.stage_changed",
    } : null,
  };
}

export function registryBusinessPayload(kind, form) {
  if (kind === "qualityPlan") {
    return {
      planId: form.planId.trim(),
      version: Number(form.version),
      name: form.name.trim(),
      description: form.description.trim() || null,
      status: form.status,
      priority: Number(form.priority),
      effectiveFrom: apiDateTime(form.effectiveFrom),
      effectiveTo: apiDateTime(form.effectiveTo),
      scope: {
        productSeries: form.scope.productSeries.trim() || null,
        productCode: form.scope.productCode.trim() || null,
        recipeId: form.scope.recipeId.trim() || null,
        machineId: form.scope.machineId.trim() || null,
        contextSelector: objectFromPairs(form.contextPairs),
      },
      items: form.items.map(item => {
        const definition = parseModelValue(item.definition);
        return {
          definitionCode: definition.id,
          definitionVersion: definition.version,
          sequence: Number(item.sequence),
          required: item.required,
          requiresAttachment: item.requiresAttachment || item.requiresReview,
          requiresReview: item.requiresReview,
        };
      }),
    };
  }
  if (kind === "processModel") {
    return {
      modelId: form.modelId.trim(),
      version: Number(form.version),
      name: form.name.trim(),
      description: form.description.trim() || null,
      status: form.status,
      acquisition: {
        dataItems: form.dataItems.map(item => ({
          ...item,
          code: item.code.trim(),
          sourceField: item.sourceField.trim(),
          unit: item.unit.trim() || null,
        })),
      },
      recipeParameters: form.recipeParameters.map(item => ({
        ...item,
        code: item.code.trim(),
        sourceField: item.sourceField.trim(),
        unit: item.unit.trim() || null,
      })),
    };
  }
  if (kind === "recipeVersion") {
    const selectedModel = parseModelValue(form.dataModel);
    return {
      recipeId: form.recipeId.trim(),
      version: Number(form.version),
      name: form.name.trim(),
      basedOnVersion: numberOrNull(form.basedOnVersion),
      dataModelId: selectedModel.id,
      dataModelVersion: selectedModel.version,
      status: form.status,
      contextSelector: objectFromPairs(form.contextPairs),
      values: form.values.map(item => {
        const value = item.dataType === "boolean" ? item.value === "true"
          : item.dataType === "integer" || item.dataType === "double" ? Number(item.value)
            : item.value;
        return { code: item.code.trim(), value };
      }),
    };
  }
  if (kind === "analysisPlan") {
    const selectedModel = parseModelValue(form.dataModel);
    return {
      planId: form.planId.trim(),
      version: Number(form.version),
      name: form.name.trim(),
      description: form.description.trim() || null,
      status: form.status,
      dataModelId: selectedModel.id,
      dataModelVersion: selectedModel.version,
      analysisScope: form.analysisScope,
      alignmentMode: form.alignmentMode,
      cohortDimension: form.cohortDimension.trim() || null,
      comparisonKeys: form.comparisonKeys.split(/[,，\s]+/).map(value => value.trim()).filter(Boolean),
      contextSelector: objectFromPairs(form.contextPairs),
      signals: form.signals.map(item => ({
        dataItemCode: item.dataItemCode.trim(),
        includeTrace: item.includeTrace,
        features: item.features,
      })),
    };
  }
  return acquisitionPayload(form);
}

export function registryBusinessValidation(kind, form) {
  const identity = kind === "processModel" ? form.modelId
    : kind === "recipeVersion" ? form.recipeId
      : kind === "acquisitionProfile" ? form.profileId : form.planId;
  if (!codePattern.test(identity.trim())) return "代码只能使用小写字母、数字、点、下划线和连字符。";
  if (!Number.isInteger(Number(form.version)) || Number(form.version) < 1) return "版本必须是大于 0 的整数。";
  if (!form.name.trim()) return "请填写名称。";

  if (kind === "qualityPlan") {
    if (form.items.length === 0 || form.items.some(item => !item.definition)) return "请至少选择一个检测定义。";
    if (new Set(form.items.map(item => item.definition)).size !== form.items.length) return "检测定义不能重复。";
    if (form.status === "retired" && !form.effectiveTo) return "停用方案需要填写结束时间。";
  }
  if (kind === "processModel") {
    if (form.dataItems.length === 0) return "请至少添加一个工艺变量。";
    const allItems = [...form.dataItems, ...form.recipeParameters];
    if (allItems.some(item => !codePattern.test(item.code.trim()) || !item.sourceField.trim())) return "工艺变量和配方参数需填写有效代码与显示名称。";
  }
  if (kind === "recipeVersion") {
    if (!form.dataModel) return "请选择工艺数据模型。";
    if (form.values.some(item => !item.code || item.value === "")) return "配方参数需选择参数并填写值。";
  }
  if (kind === "analysisPlan") {
    if (!form.dataModel) return "请选择工艺数据模型。";
    if (!form.comparisonKeys.trim()) return "请至少填写一个同类比较字段。";
    if (form.signals.length === 0 || form.signals.some(item => !item.dataItemCode)) return "请至少选择一个分析数据项。";
  }
  if (kind === "acquisitionProfile") {
    if (!form.edgeId || !form.dataModel || !form.subjectId.trim()) return "请选择采集节点和数据模型，并填写设备编号。";
    if (form.valueMappings.length === 0 || form.valueMappings.some(item =>
      !item.dataItemCode ||
      (form.protocol === "modbus-tcp" && item.modbusAddress === "") ||
      (form.protocol === "melsec-a1e" && (!item.melsecDevice || item.melsecAddress === "")) ||
      (!["modbus-tcp", "melsec-a1e"].includes(form.protocol) && !item.sourcePath.trim()))) {
      return "请至少完成一个采集点位映射。";
    }
    if (form.protocol === "http-polling" && (!/^https?:\/\//.test(form.connection.baseUrl) || !form.connection.snapshotPath.trim())) return "请填写有效的设备 HTTP 地址和快照路径。";
    if (form.protocol === "mqtt" && (!form.mqtt.host.trim() || !form.mqtt.topics.some(item => item.topic.trim()))) return "请填写 MQTT 主机和至少一个订阅主题。";
    if (form.protocol === "opc-ua" && !/^(opc\.tcp|https):\/\//.test(form.opcUa.endpointUrl)) return "请填写有效的 OPC UA 端点。";
    if (["modbus-tcp", "melsec-a1e"].includes(form.protocol) && !form[form.protocol === "modbus-tcp" ? "modbusTcp" : "melsecA1E"].host.trim()) return "请填写设备主机地址。";
    if (["modbus-tcp", "melsec-a1e"].includes(form.protocol) && form.valueMappings.some(item => !registerDataTypes.includes(item.sourceDataType))) return "寄存器点位需要明确选择设备数据类型。";
    if (form.protocol === "modbus-tcp" && form.modbusTcp.addressBase === "one-based" && form.valueMappings.some(item => Number(item.modbusAddress) < 1)) return "使用 1 基地址时，寄存器地址必须从 1 开始。";
  }
  return "";
}

function updateAt(form, onChange, field, value) {
  onChange({ ...form, [field]: value });
}

function updateNested(form, onChange, group, field, value) {
  onChange({ ...form, [group]: { ...form[group], [field]: value } });
}

function updateRow(form, onChange, field, index, patch) {
  onChange({
    ...form,
    [field]: form[field].map((item, rowIndex) => rowIndex === index ? { ...item, ...patch } : item),
  });
}

function addRow(form, onChange, field, value) {
  onChange({ ...form, [field]: [...form[field], value] });
}

function removeRow(form, onChange, field, index) {
  onChange({ ...form, [field]: form[field].filter((_item, rowIndex) => rowIndex !== index) });
}

function IdentityFields({ form, onChange, idField, idLabel, readOnly, lockIdentity, description = true }) {
  return (
    <div className="grid gap-4 md:grid-cols-2">
      <Field label={idLabel}>
        <Input required value={form[idField]} disabled={readOnly || lockIdentity} onChange={event => updateAt(form, onChange, idField, event.target.value)} />
      </Field>
      <Field label="版本">
        <Input required type="number" min="1" step="1" value={form.version} disabled={readOnly || lockIdentity} onChange={event => updateAt(form, onChange, "version", event.target.value)} />
      </Field>
      <Field label="名称">
        <Input required value={form.name} disabled={readOnly} onChange={event => updateAt(form, onChange, "name", event.target.value)} />
      </Field>
      <Field label="状态">
        <Select value={form.status} disabled={readOnly} onChange={event => updateAt(form, onChange, "status", event.target.value)}>
          <option value="draft">草稿</option>
          <option value="published">已发布</option>
          <option value="retired">已停用</option>
        </Select>
      </Field>
      {description && <Field label="说明" className="md:col-span-2"><Textarea className="min-h-20" value={form.description} disabled={readOnly} onChange={event => updateAt(form, onChange, "description", event.target.value)} /></Field>}
    </div>
  );
}

function PairEditor({ title, description, pairs, onChange, readOnly }) {
  const values = pairs.length ? pairs : [blankPair()];
  function update(index, field, value) {
    const base = pairs.length ? pairs : [blankPair()];
    onChange(base.map((pair, rowIndex) => rowIndex === index ? { ...pair, [field]: value } : pair));
  }
  return (
    <Card title={title} description={description} actions={!readOnly ? <Button onClick={() => onChange([...pairs, blankPair()])}>添加条件</Button> : undefined}>
      <div className="grid gap-3">
        {values.map((pair, index) => (
          <div key={index} className="grid gap-2 md:grid-cols-[1fr_1fr_auto]">
            <Input value={pair.key} disabled={readOnly} aria-label={`${title}字段 ${index + 1}`} placeholder="字段" onChange={event => update(index, "key", event.target.value)} />
            <Input value={pair.value} disabled={readOnly} aria-label={`${title}内容 ${index + 1}`} placeholder="内容" onChange={event => update(index, "value", event.target.value)} />
            {!readOnly && pairs.length > 0 && <Button variant="ghost" className="text-rose-700" onClick={() => onChange(pairs.filter((_item, rowIndex) => rowIndex !== index))}>移除</Button>}
          </div>
        ))}
      </div>
    </Card>
  );
}

function QualityPlanEditor({ form, onChange, readOnly, lockIdentity }) {
  const { data, error } = useApi("/api/v1/inspection-definitions");
  const definitions = extractRows(data);
  return (
    <div className="grid gap-5">
      {error && <Alert tone="danger">检测定义读取失败：{error}</Alert>}
      <IdentityFields form={form} onChange={onChange} idField="planId" idLabel="方案代码" readOnly={readOnly} lockIdentity={lockIdentity} />
      <Card title="生效规则" description="优先级越高，多个方案同时匹配时越先采用。">
        <div className="grid gap-4 md:grid-cols-3">
          <Field label="优先级"><Input type="number" value={form.priority} disabled={readOnly} onChange={event => updateAt(form, onChange, "priority", event.target.value)} /></Field>
          <Field label="生效时间"><Input type="datetime-local" value={form.effectiveFrom} disabled={readOnly} onChange={event => updateAt(form, onChange, "effectiveFrom", event.target.value)} /></Field>
          <Field label="结束时间"><Input type="datetime-local" value={form.effectiveTo} disabled={readOnly} onChange={event => updateAt(form, onChange, "effectiveTo", event.target.value)} /></Field>
        </div>
      </Card>
      <Card title="适用范围" description="只填写需要限制的条件；全部留空表示不限定。">
        <div className="grid gap-4 md:grid-cols-2">
          {[["productSeries", "产品系列"], ["productCode", "产品编号"], ["recipeId", "配方编号"], ["machineId", "设备编号"]].map(([key, label]) => (
            <Field key={key} label={label}><Input value={form.scope[key]} disabled={readOnly} onChange={event => updateNested(form, onChange, "scope", key, event.target.value)} /></Field>
          ))}
        </div>
      </Card>
      <PairEditor title="其他适用条件" description="需要额外限定时再添加。" pairs={form.contextPairs} readOnly={readOnly} onChange={value => updateAt(form, onChange, "contextPairs", value)} />
      <Card title="检测项目" description="按执行顺序选择本方案包含的检测定义。" actions={!readOnly ? <Button onClick={() => addRow(form, onChange, "items", qualityItem({ sequence: (form.items.length + 1) * 10 }))}>添加检测项目</Button> : undefined}>
        <div className="grid gap-4">
          {form.items.map((item, index) => (
            <div key={index} className="grid gap-3 rounded-xl border border-slate-200 p-4 md:grid-cols-2">
              <Field label={`检测定义 ${index + 1}`}>
                <Select value={item.definition} disabled={readOnly} onChange={event => updateRow(form, onChange, "items", index, { definition: event.target.value })}>
                  <option value="">请选择</option>
                  {definitions.map(definition => <option key={`${definition.code}:${definition.version}`} value={modelValue(definition.code, definition.version)}>{definition.name}（v{definition.version}）</option>)}
                </Select>
              </Field>
              <Field label="执行顺序"><Input type="number" min="0" value={item.sequence} disabled={readOnly} onChange={event => updateRow(form, onChange, "items", index, { sequence: event.target.value })} /></Field>
              {[["required", "必须执行"], ["requiresAttachment", "要求附件"], ["requiresReview", "要求人工复核"]].map(([key, label]) => (
                <label key={key} className="flex items-center gap-2 text-sm text-slate-700"><input type="checkbox" checked={item[key]} disabled={readOnly} onChange={event => updateRow(form, onChange, "items", index, { [key]: event.target.checked })} />{label}</label>
              ))}
              {!readOnly && form.items.length > 1 && <Button variant="ghost" className="justify-self-start text-rose-700" onClick={() => removeRow(form, onChange, "items", index)}>移除</Button>}
            </div>
          ))}
        </div>
      </Card>
    </div>
  );
}

function DataTypeSelect({ value, disabled, onChange }) {
  return <Select value={value} disabled={disabled} onChange={onChange}>{dataTypes.map(([key, label]) => <option key={key} value={key}>{label}</option>)}</Select>;
}

function ProcessModelEditor({ form, onChange, readOnly, lockIdentity }) {
  return (
    <div className="grid gap-5">
      <IdentityFields form={form} onChange={onChange} idField="modelId" idLabel="模型代码" readOnly={readOnly} lockIdentity={lockIdentity} />
      <Card title="模型职责" description="这里只定义平台如何理解数据，不填写 PLC 地址、设备点位或采集频率。">
        <p className="text-sm leading-6 text-slate-600">同一模型可以复用于多台设备；每台设备的协议、寄存器、原始类型和换算规则在“设备接入与映射”中配置。</p>
      </Card>
      <ItemDefinitions form={form} onChange={onChange} field="dataItems" title="工艺变量" readOnly={readOnly} includeCategory />
      <ItemDefinitions form={form} onChange={onChange} field="recipeParameters" title="配方参数结构" readOnly={readOnly} />
    </div>
  );
}

function ItemDefinitions({ form, onChange, field, title, readOnly, includeCategory = false }) {
  const factory = field === "dataItems" ? dataItem : recipeParameter;
  return (
    <Card
      title={title}
      description={field === "dataItems" ? "定义稳定业务代码、显示名称、平台类型和标准单位；阶段号的用途分类设为“阶段号”。" : "只定义配方参数的业务结构，具体寄存器在设备接入中映射。"}
      actions={!readOnly ? <Button onClick={() => addRow(form, onChange, field, factory())}>添加{title}</Button> : undefined}
    >
      <div className="grid gap-4">
        {form[field].length === 0 && <p className="text-sm text-slate-500">尚未添加。</p>}
        {form[field].map((item, index) => (
          <div key={index} className="grid gap-3 rounded-xl border border-slate-200 p-4 md:grid-cols-2 xl:grid-cols-3">
            <Field label="数据代码"><Input value={item.code} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { code: event.target.value })} /></Field>
            <Field label="显示名称"><Input value={item.sourceField} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { sourceField: event.target.value })} /></Field>
            <Field label="数据类型"><DataTypeSelect value={item.dataType} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { dataType: event.target.value })} /></Field>
            <Field label="单位"><Input value={item.unit} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { unit: event.target.value })} /></Field>
            {includeCategory && <Field label="用途分类"><Select value={item.category} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { category: event.target.value })}><option value="process">过程值</option><option value="stage">阶段号</option><option value="setpoint">设定值</option><option value="state">状态</option><option value="quality">质量</option></Select></Field>}
            <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={item.nullable} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { nullable: event.target.checked })} />允许空值</label>
            {!readOnly && (field !== "dataItems" || form[field].length > 1) && <Button variant="ghost" className="justify-self-start text-rose-700" onClick={() => removeRow(form, onChange, field, index)}>移除</Button>}
          </div>
        ))}
      </div>
    </Card>
  );
}

function ModelSelect({ value, models, disabled, onChange }) {
  return (
    <Select value={value} disabled={disabled} onChange={onChange}>
      <option value="">请选择数据模型</option>
      {models.map(model => <option key={`${model.modelId}:${model.version}`} value={modelValue(model.modelId, model.version)}>{model.name}（v{model.version}）</option>)}
    </Select>
  );
}

function RecipeEditor({ form, onChange, readOnly, lockIdentity }) {
  const { data, error } = useApi("/api/v1/process-data-models");
  const models = extractRows(data);
  const selected = parseModelValue(form.dataModel);
  const model = models.find(item => item.modelId === selected.id && item.version === selected.version);
  const parameters = model?.recipeParameters || [];
  return (
    <div className="grid gap-5">
      {error && <Alert tone="danger">工艺数据模型读取失败：{error}</Alert>}
      <IdentityFields form={form} onChange={onChange} idField="recipeId" idLabel="配方代码" readOnly={readOnly} lockIdentity={lockIdentity} description={false} />
      <Card title="配方来源">
        <div className="grid gap-4 md:grid-cols-2">
          <Field label="工艺数据模型"><ModelSelect value={form.dataModel} models={models} disabled={readOnly} onChange={event => updateAt(form, onChange, "dataModel", event.target.value)} /></Field>
          <Field label="沿用自版本"><Input type="number" min="1" value={form.basedOnVersion} disabled={readOnly} onChange={event => updateAt(form, onChange, "basedOnVersion", event.target.value)} placeholder="没有可留空" /></Field>
        </div>
      </Card>
      <PairEditor title="适用条件" description="例如产品系列或设备范围。" pairs={form.contextPairs} readOnly={readOnly} onChange={value => updateAt(form, onChange, "contextPairs", value)} />
      <Card title="配方参数" actions={!readOnly ? <Button onClick={() => addRow(form, onChange, "values", recipeValue())}>添加参数</Button> : undefined}>
        <div className="grid gap-3">
          {form.values.length === 0 && <p className="text-sm text-slate-500">尚未设置参数。</p>}
          {form.values.map((item, index) => {
            const parameter = parameters.find(value => value.code === item.code);
            return (
              <div key={index} className="grid gap-2 md:grid-cols-[1fr_1fr_auto]">
                <Select value={item.code} disabled={readOnly} aria-label={`配方参数 ${index + 1}`} onChange={event => {
                  const next = parameters.find(value => value.code === event.target.value);
                  updateRow(form, onChange, "values", index, { code: event.target.value, value: "", dataType: next?.dataType || "string" });
                }}>
                  <option value="">请选择参数</option>
                  {parameters.map(value => <option key={value.code} value={value.code}>{value.sourceField || value.code}{value.unit ? `（${value.unit}）` : ""}</option>)}
                </Select>
                {parameter?.dataType === "boolean" ? (
                  <Select value={item.value} disabled={readOnly} aria-label={`参数值 ${index + 1}`} onChange={event => updateRow(form, onChange, "values", index, { value: event.target.value })}><option value="">请选择</option><option value="true">是</option><option value="false">否</option></Select>
                ) : <Input type={["double", "integer"].includes(parameter?.dataType) ? "number" : "text"} step={parameter?.dataType === "double" ? "any" : undefined} value={item.value} disabled={readOnly} aria-label={`参数值 ${index + 1}`} onChange={event => updateRow(form, onChange, "values", index, { value: event.target.value })} />}
                {!readOnly && <Button variant="ghost" className="text-rose-700" onClick={() => removeRow(form, onChange, "values", index)}>移除</Button>}
              </div>
            );
          })}
        </div>
      </Card>
    </div>
  );
}

function AnalysisPlanEditor({ form, onChange, readOnly, lockIdentity }) {
  const { data, error } = useApi("/api/v1/process-data-models");
  const models = extractRows(data);
  const selected = parseModelValue(form.dataModel);
  const model = models.find(item => item.modelId === selected.id && item.version === selected.version);
  const dataItems = model?.acquisition?.dataItems || [];
  return (
    <div className="grid gap-5">
      {error && <Alert tone="danger">工艺数据模型读取失败：{error}</Alert>}
      <IdentityFields form={form} onChange={onChange} idField="planId" idLabel="方案代码" readOnly={readOnly} lockIdentity={lockIdentity} />
      <Card title="分析方式">
        <div className="grid gap-4 md:grid-cols-2">
          <Field label="工艺数据模型"><ModelSelect value={form.dataModel} models={models} disabled={readOnly} onChange={event => updateAt(form, onChange, "dataModel", event.target.value)} /></Field>
          <Field label="分析范围"><Select value={form.analysisScope} disabled={readOnly} onChange={event => updateAt(form, onChange, "analysisScope", event.target.value)}><option value="production-cycle">生产周期</option><option value="production-run">生产运行段</option><option value="analysis-window">自定义时间窗口</option></Select></Field>
          <Field label="曲线对齐方式"><Select value={form.alignmentMode} disabled={readOnly} onChange={event => updateAt(form, onChange, "alignmentMode", event.target.value)}><option value="stage-relative">按工艺阶段</option><option value="elapsed">按经过时间</option><option value="normalized">按归一化进度</option></Select></Field>
          <Field label="质量分组字段"><Input value={form.cohortDimension} disabled={readOnly} onChange={event => updateAt(form, onChange, "cohortDimension", event.target.value)} placeholder="例如 quality.outcome" /></Field>
          <Field label="同类比较字段" hint="多个字段用逗号分隔。" className="md:col-span-2"><Input value={form.comparisonKeys} disabled={readOnly} onChange={event => updateAt(form, onChange, "comparisonKeys", event.target.value)} placeholder="product_series, recipe_id" /></Field>
        </div>
      </Card>
      <PairEditor title="分析对象筛选" description="只分析符合这些条件的生产记录。" pairs={form.contextPairs} readOnly={readOnly} onChange={value => updateAt(form, onChange, "contextPairs", value)} />
      <Card title="分析数据项" actions={!readOnly ? <Button onClick={() => addRow(form, onChange, "signals", analysisSignal())}>添加数据项</Button> : undefined}>
        <div className="grid gap-4">
          {form.signals.map((signal, index) => (
            <div key={index} className="grid gap-3 rounded-xl border border-slate-200 p-4">
              <div className="grid gap-3 md:grid-cols-[1fr_auto_auto]">
                <Field label={`数据项 ${index + 1}`}><Select value={signal.dataItemCode} disabled={readOnly} onChange={event => updateRow(form, onChange, "signals", index, { dataItemCode: event.target.value })}><option value="">请选择</option>{dataItems.map(item => <option key={item.code} value={item.code}>{item.sourceField || item.code}</option>)}</Select></Field>
                <label className="flex items-center gap-2 self-end pb-2 text-sm"><input type="checkbox" checked={signal.includeTrace} disabled={readOnly} onChange={event => updateRow(form, onChange, "signals", index, { includeTrace: event.target.checked })} />保留曲线</label>
                {!readOnly && form.signals.length > 1 && <Button variant="ghost" className="self-end text-rose-700" onClick={() => removeRow(form, onChange, "signals", index)}>移除</Button>}
              </div>
              <div>
                <p className="mb-2 text-sm font-medium text-slate-700">计算指标</p>
                <div className="flex flex-wrap gap-3">
                  {featureOptions.map(([key, label]) => <label key={key} className="flex items-center gap-1.5 text-sm"><input type="checkbox" checked={signal.features.includes(key)} disabled={readOnly} onChange={event => updateRow(form, onChange, "signals", index, { features: event.target.checked ? [...signal.features, key] : signal.features.filter(value => value !== key) })} />{label}</label>)}
                </div>
              </div>
            </div>
          ))}
        </div>
      </Card>
    </div>
  );
}

function AcquisitionEditor({ form, onChange, readOnly, lockIdentity, onProbeStateChange }) {
  const { data: modelData, error: modelError } = useApi("/api/v1/process-data-models");
  const { data: edgeData, error: edgeError } = useApi("/api/edges");
  const [probe, setProbe] = useState(null);
  const [probeError, setProbeError] = useState("");
  const [probing, setProbing] = useState(false);
  const models = extractRows(modelData);
  const edges = extractRows(edgeData);
  const selected = parseModelValue(form.dataModel);
  const model = models.find(item => item.modelId === selected.id && item.version === selected.version);
  const dataItems = model?.acquisition?.dataItems || [];
  const recipeParameters = model?.recipeParameters || [];
  const probeFingerprint = useMemo(() => JSON.stringify({
    edgeId: form.edgeId,
    protocol: form.protocol,
    dataModel: form.dataModel,
    connection: form.connection,
    mqtt: form.mqtt,
    opcUa: form.opcUa,
    modbusTcp: form.modbusTcp,
    melsecA1E: form.melsecA1E,
  }), [form]);
  const mappingFingerprint = useMemo(() => JSON.stringify({
    valueMappings: form.valueMappings,
    contextMappings: form.contextMappings,
  }), [form.valueMappings, form.contextMappings]);
  useEffect(() => {
    setProbe(null);
    setProbeError("");
    onProbeStateChange?.(false);
  }, [probeFingerprint]);
  useEffect(() => {
    onProbeStateChange?.(false);
  }, [mappingFingerprint, onProbeStateChange]);

  async function testConnection() {
    setProbing(true);
    setProbeError("");
    try {
      const payload = { ...registryBusinessPayload("acquisitionProfile", form), status: "draft" };
      const result = await postJson("/api/v1/acquisition-profiles/probe", payload);
      setProbe(result);
      onProbeStateChange?.(result.success === true && result.mappingsValidated === true);
    } catch (error) {
      setProbe(null);
      setProbeError(error.message);
      onProbeStateChange?.(false);
    } finally {
      setProbing(false);
    }
  }

  function mapPoint(point, dataItemCode) {
    if (!dataItemCode) return;
    const existing = form.valueMappings.findIndex(item => item.dataItemCode === dataItemCode);
    const definition = dataItems.find(item => item.code === dataItemCode);
    const patch = { dataItemCode, sourcePath: point.path, required: definition ? !definition.nullable : true };
    if (existing >= 0) {
      updateRow(form, onChange, "valueMappings", existing, patch);
      return;
    }
    const blank = form.valueMappings.findIndex(item => !item.dataItemCode);
    if (blank >= 0) {
      updateRow(form, onChange, "valueMappings", blank, patch);
      return;
    }
    addRow(form, onChange, "valueMappings", { ...valueMapping(), ...patch });
  }

  function changeProtocol(protocol) {
    const registerProtocol = ["modbus-tcp", "melsec-a1e"].includes(protocol);
    onChange({
      ...form,
      protocol,
      timestampMode: protocol === "melsec-a1e" ? "edge-received" : protocol === "opc-ua" ? "source" : form.timestampMode,
      valueMappings: form.valueMappings.map(item => registerProtocol && !registerDataTypes.includes(item.sourceDataType)
        ? { ...item, sourceDataType: "int16" }
        : item),
    });
  }
  return (
    <div className="grid gap-5">
      {(modelError || edgeError) && <Alert tone="danger">{modelError || edgeError}</Alert>}
      <IdentityFields form={form} onChange={onChange} idField="profileId" idLabel="接入配置代码" readOnly={readOnly} lockIdentity={lockIdentity} description={false} />
      <Card title="基本信息" description="选择在哪里采集、采哪台设备，以及采集结果采用哪套工艺定义。">
        <div className="grid gap-4 md:grid-cols-2">
          <Field label="采集节点" hint="运行在设备所在网络、负责执行采集的边缘节点。">
            <Select value={form.edgeId} disabled={readOnly} onChange={event => updateAt(form, onChange, "edgeId", event.target.value)}>
              <option value="">请选择采集节点</option>
              {form.edgeId && !edges.some(edge => edge.edgeId === form.edgeId) && <option value={form.edgeId}>{form.edgeId}（历史值）</option>}
              {edges.map(edge => <option key={edge.edgeId} value={edge.edgeId}>{edge.hostname || edge.displayName || edge.edgeId} · {edge.edgeId}</option>)}
            </Select>
          </Field>
          <Field label="数据模型" hint="规定平台中的工艺变量和单位，不包含 PLC 地址。"><ModelSelect value={form.dataModel} models={models} disabled={readOnly} onChange={event => updateAt(form, onChange, "dataModel", event.target.value)} /></Field>
          <Field label="设备编号" hint="设备在平台中的唯一编号，例如 PRESS-01。"><Input value={form.subjectId} disabled={readOnly} onChange={event => updateAt(form, onChange, "subjectId", event.target.value)} /></Field>
          <Field label="通信驱动" hint={acquisitionProtocols.find(item => item[0] === form.protocol)?.[2]}><Select value={form.protocol} disabled={readOnly} onChange={event => changeProtocol(event.target.value)}>{acquisitionProtocols.map(([value, label]) => <option key={value} value={value}>{label}</option>)}</Select></Field>
        </div>
      </Card>
      <ConnectionFields form={form} onChange={onChange} readOnly={readOnly} />
      <AcquisitionProbePanel
        form={form}
        dataItems={dataItems}
        probe={probe}
        error={probeError}
        loading={probing}
        readOnly={readOnly}
        onProbe={testConnection}
        onMap={mapPoint}
      />
      <Card title="采集策略">
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          <Field label="连接超时（毫秒）"><Input type="number" min="100" value={form.execution.timeoutMs} disabled={readOnly} onChange={event => updateNested(form, onChange, "execution", "timeoutMs", event.target.value)} /></Field>
          <Field label="重连间隔（毫秒）"><Input type="number" min="100" value={form.execution.reconnectDelayMs} disabled={readOnly} onChange={event => updateNested(form, onChange, "execution", "reconnectDelayMs", event.target.value)} /></Field>
          {form.protocol === "opc-ua"
            ? <Field label="数据时间"><Input value="OPC UA 源时间" disabled /></Field>
            : form.protocol === "melsec-a1e"
              ? <Field label="数据时间"><Input value="采集节点接收时间" disabled /></Field>
              : <Field label="数据时间"><Select value={form.timestampMode} disabled={readOnly} onChange={event => updateAt(form, onChange, "timestampMode", event.target.value)}><option value="source">使用设备时间</option><option value="edge-received">使用采集时间</option></Select></Field>}
          {form.timestampMode === "source" && ["http-polling", "mqtt"].includes(form.protocol) && <Field label="时间字段"><Input value={form.timestampPath} disabled={readOnly} onChange={event => updateAt(form, onChange, "timestampPath", event.target.value)} /></Field>}
          {form.timestampMode === "source" && form.protocol === "modbus-tcp" && <Field label="时间寄存器" hint="格式：寄存器区:地址:类型。"><Input value={form.timestampPath} disabled={readOnly} onChange={event => updateAt(form, onChange, "timestampPath", event.target.value)} placeholder="holding-register:100:uint64" /></Field>}
          {["http-polling", "mqtt"].includes(form.protocol) && <Field label="序号字段"><Input value={form.sequencePath} disabled={readOnly} onChange={event => updateAt(form, onChange, "sequencePath", event.target.value)} placeholder="没有可留空" /></Field>}
        </div>
      </Card>
      <PairEditor title="固定连接元数据" description="只填写设备接入后始终不变的信息；产品、工单、模具和材料批次应从设备或 MES 动态读取。" pairs={form.staticContextPairs} readOnly={readOnly} onChange={value => updateAt(form, onChange, "staticContextPairs", value)} />
      <MappingRows form={form} onChange={onChange} field="valueMappings" title="工艺变量映射" options={dataItems} protocol={form.protocol} readOnly={readOnly} probe={probe} />
      <Card title="运行上下文与控制信号" description="从设备读取生产状态、循环计数、产品、批次等上下文；阶段号已经由工艺变量的“阶段号”用途自动识别。" actions={!readOnly ? <Button onClick={() => addRow(form, onChange, "contextMappings", { contextKey: "", sourcePath: "", required: false })}>添加信息</Button> : undefined}>
        <div className="grid gap-3">
          {form.contextMappings.length === 0 && <p className="text-sm text-slate-500">没有动态生产信息。</p>}
          {form.contextMappings.map((item, index) => (
            <div key={index} className="grid gap-2 md:grid-cols-[1fr_1fr_auto_auto]">
              <Input value={item.contextKey} disabled={readOnly} aria-label={`生产信息字段 ${index + 1}`} placeholder="例如 product_series" onChange={event => updateRow(form, onChange, "contextMappings", index, { contextKey: event.target.value })} />
              <Input value={item.sourcePath} disabled={readOnly} aria-label={`设备字段路径 ${index + 1}`} placeholder="例如 productSeries" onChange={event => updateRow(form, onChange, "contextMappings", index, { sourcePath: event.target.value })} />
              <label className="flex items-center gap-1.5 text-sm"><input type="checkbox" checked={item.required} disabled={readOnly} onChange={event => updateRow(form, onChange, "contextMappings", index, { required: event.target.checked })} />必需</label>
              {!readOnly && <Button variant="ghost" className="text-rose-700" onClick={() => removeRow(form, onChange, "contextMappings", index)}>移除</Button>}
            </div>
          ))}
        </div>
      </Card>
      <OptionalRecipe form={form} onChange={onChange} parameters={recipeParameters} protocol={form.protocol} readOnly={readOnly} />
      <OptionalLifecycle form={form} onChange={onChange} readOnly={readOnly} />
    </div>
  );
}

function AcquisitionProbePanel({ form, dataItems, probe, error, loading, readOnly, onProbe, onMap }) {
  const [search, setSearch] = useState("");
  const [selections, setSelections] = useState({});
  const points = (probe?.points || []).filter(point =>
    !search.trim() || `${point.name} ${point.path}`.toLowerCase().includes(search.trim().toLowerCase()));
  const protocolView = form.protocol === "opc-ua"
    ? "节点浏览器"
    : ["modbus-tcp", "melsec-a1e"].includes(form.protocol) ? "寄存器读取结果" : "JSON 字段树";
  const configurationError = !form.edgeId || !form.dataModel
    ? "请先选择采集节点和数据模型。"
    : form.protocol === "http-polling" && (!/^https?:\/\//.test(form.connection.baseUrl) || !form.connection.snapshotPath.trim())
      ? "请填写有效的设备 HTTP 地址和快照路径。"
      : form.protocol === "mqtt" && (!form.mqtt.host.trim() || !form.mqtt.topics.some(item => item.topic.trim()))
        ? "请填写 MQTT 主机和至少一个订阅主题。"
        : form.protocol === "opc-ua" && !/^(opc\.tcp|https):\/\//.test(form.opcUa.endpointUrl)
          ? "请填写有效的 OPC UA 端点。"
          : ["modbus-tcp", "melsec-a1e"].includes(form.protocol) &&
            (!form[form.protocol === "modbus-tcp" ? "modbusTcp" : "melsecA1E"].host.trim() ||
             form.valueMappings.every(item => form.protocol === "modbus-tcp" ? item.modbusAddress === "" : item.melsecAddress === ""))
            ? "寄存器协议需先填写设备地址和至少一个寄存器点位，再执行读取。"
            : "";
  return (
    <Card
      title="测试连接与设备点位"
      description={`由所选采集节点真实连接设备；成功后显示${protocolView}，并验证点位换算。`}
      actions={!readOnly
        ? <Button variant="primary" disabled={loading || Boolean(configurationError)} onClick={onProbe}>
          {loading ? "正在读取样本…" : "测试连接并读取样本"}
        </Button>
        : undefined}
    >
      {configurationError && !readOnly && <Alert tone="warning">{configurationError}</Alert>}
      {error && <Alert tone="danger">{error}</Alert>}
      {!probe && !error && (
        <p className="text-sm leading-6 text-slate-500">
          HTTP/MQTT 会读取一份 JSON；OPC UA 会浏览变量节点；Modbus/三菱 MC 不会盲扫地址，只读取下方已配置的寄存器。
        </p>
      )}
      {probe && (
        <div className="grid gap-4">
          <Alert tone={probe.success ? "success" : "warning"}>{probe.message}</Alert>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <p className="font-medium text-slate-800">{protocolView}</p>
              <p className="text-sm text-slate-500">共读取 {probe.points?.length || 0} 个点位；选择平台语义即可建立映射。</p>
            </div>
            <Input className="max-w-xs" value={search} onChange={event => setSearch(event.target.value)} placeholder="搜索路径或节点名称" />
          </div>
          <div className="max-h-80 overflow-auto rounded-xl border border-slate-200">
            <table className="w-full min-w-[760px] text-left text-sm">
              <thead className="sticky top-0 bg-slate-50 text-slate-600">
                <tr><th className="px-3 py-2">设备点位</th><th className="px-3 py-2">原始值</th><th className="px-3 py-2">类型</th><th className="px-3 py-2">映射到平台</th></tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {points.slice(0, 200).map(point => (
                  <tr key={point.path}>
                    <td className="px-3 py-2"><p className="font-medium text-slate-700">{point.name}</p><code className="text-xs text-slate-500">{point.path}</code></td>
                    <td className="px-3 py-2 text-slate-700">{point.rawValue ?? "—"}</td>
                    <td className="px-3 py-2 text-slate-500">{point.dataType}</td>
                    <td className="px-3 py-2">
                      {!readOnly ? <div className="flex gap-2">
                        <Select value={selections[point.path] || ""} aria-label={`映射 ${point.path}`} onChange={event => setSelections(value => ({ ...value, [point.path]: event.target.value }))}>
                          <option value="">选择数据项</option>
                          {dataItems.map(item => <option key={item.code} value={item.code}>{item.sourceField || item.code}{item.unit ? `（${item.unit}）` : ""}</option>)}
                        </Select>
                        <Button disabled={!selections[point.path]} onClick={() => onMap(point, selections[point.path])}>映射</Button>
                      </div> : "—"}
                    </td>
                  </tr>
                ))}
                {points.length === 0 && <tr><td colSpan="4" className="px-3 py-8 text-center text-slate-500">没有匹配的设备点位。</td></tr>}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </Card>
  );
}

function ConnectionFields({ form, onChange, readOnly }) {
  const group = form.protocol === "http-polling" ? "connection" : form.protocol === "mqtt" ? "mqtt" : form.protocol === "opc-ua" ? "opcUa" : form.protocol === "modbus-tcp" ? "modbusTcp" : "melsecA1E";
  const update = (field, value) => updateNested(form, onChange, group, field, value);
  const protocol = acquisitionProtocols.find(item => item[0] === form.protocol);
  return (
    <Card title="连接参数" description={protocol?.[2]}>
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        {form.protocol === "http-polling" && <>
          <Field label="服务地址" hint="设备或网关提供 HTTP 服务的根地址。"><Input value={form.connection.baseUrl} disabled={readOnly} onChange={event => update("baseUrl", event.target.value)} placeholder="http://192.168.1.10" /></Field>
          <Field label="数据路径" hint="当前驱动使用 GET 读取 JSON。"><Input value={form.connection.snapshotPath} disabled={readOnly} onChange={event => update("snapshotPath", event.target.value)} placeholder="/api/v1/snapshot" /></Field>
          <Field label="轮询间隔（ms）" hint="一次请求完成后等待多久再发起下一次。"><Input type="number" min="1" value={form.connection.pollIntervalMs} disabled={readOnly} onChange={event => update("pollIntervalMs", event.target.value)} /></Field>
        </>}
        {form.protocol === "mqtt" && <>
          <Field label="服务器"><Input value={form.mqtt.host} disabled={readOnly} onChange={event => update("host", event.target.value)} placeholder="192.168.1.20" /></Field>
          <Field label="端口"><Input type="number" min="1" max="65535" value={form.mqtt.port} disabled={readOnly} onChange={event => update("port", event.target.value)} /></Field>
          <Field label="协议版本"><Select value={form.mqtt.protocolVersion} disabled={readOnly} onChange={event => update("protocolVersion", event.target.value)}><option value="5.0">5.0</option><option value="3.1.1">3.1.1</option></Select></Field>
          <Field label="客户端编号" hint="留空时由采集节点生成唯一编号。"><Input value={form.mqtt.clientId} disabled={readOnly} onChange={event => update("clientId", event.target.value)} /></Field>
          <Field label="用户名"><Input value={form.mqtt.username} disabled={readOnly} onChange={event => update("username", event.target.value)} /></Field>
          <Field label="密码凭据" hint="填写采集节点密钥库中的名称，不在配置中保存明文。"><Input value={form.mqtt.passwordSecretRef} disabled={readOnly} onChange={event => update("passwordSecretRef", event.target.value)} /></Field>
          <Field label="保活时间（秒）"><Input type="number" min="1" value={form.mqtt.keepAliveSeconds} disabled={readOnly} onChange={event => update("keepAliveSeconds", event.target.value)} /></Field>
          <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={form.mqtt.cleanSession} disabled={readOnly} onChange={event => update("cleanSession", event.target.checked)} />{form.mqtt.protocolVersion === "5.0" ? "重新开始会话（Clean Start）" : "清理旧会话（Clean Session）"}</label>
          <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={form.mqtt.useTls} disabled={readOnly} onChange={event => update("useTls", event.target.checked)} />使用 TLS 加密</label>
          {form.mqtt.useTls && <>
            <Field label="CA 证书" hint="采集节点上的证书文件路径。"><Input value={form.mqtt.caCertificatePath} disabled={readOnly} onChange={event => update("caCertificatePath", event.target.value)} /></Field>
            <Field label="客户端证书"><Input value={form.mqtt.clientCertificatePath} disabled={readOnly} onChange={event => update("clientCertificatePath", event.target.value)} /></Field>
            <Field label="证书密码"><Input value={form.mqtt.clientCertificatePasswordSecretRef} disabled={readOnly} onChange={event => update("clientCertificatePasswordSecretRef", event.target.value)} /></Field>
          </>}
          <div className="grid gap-2 md:col-span-2 xl:col-span-3">
            <div className="flex items-center justify-between"><div><p className="text-sm font-medium">订阅主题</p><p className="text-xs text-slate-500">支持 + 和 # 通配符；每个主题单独选择 QoS。</p></div>{!readOnly && <Button onClick={() => update("topics", [...form.mqtt.topics, { topic: "", qos: 0 }])}>添加主题</Button>}</div>
            {form.mqtt.topics.map((topic, index) => <div key={index} className="grid gap-2 md:grid-cols-[1fr_8rem_auto]"><Input value={topic.topic} disabled={readOnly} aria-label={`MQTT 主题 ${index + 1}`} onChange={event => update("topics", form.mqtt.topics.map((item, rowIndex) => rowIndex === index ? { ...item, topic: event.target.value } : item))} /><Select value={topic.qos} disabled={readOnly} aria-label={`QoS ${index + 1}`} onChange={event => update("topics", form.mqtt.topics.map((item, rowIndex) => rowIndex === index ? { ...item, qos: Number(event.target.value) } : item))}><option value="0">QoS 0</option><option value="1">QoS 1</option><option value="2">QoS 2</option></Select>{!readOnly && form.mqtt.topics.length > 1 && <Button variant="ghost" className="text-rose-700" onClick={() => update("topics", form.mqtt.topics.filter((_item, rowIndex) => rowIndex !== index))}>移除</Button>}</div>)}
          </div>
        </>}
        {form.protocol === "opc-ua" && <>
          <Field label="服务器端点" hint="测试连接时会发现并校验服务器实际提供的安全组合。"><Input value={form.opcUa.endpointUrl} disabled={readOnly} onChange={event => update("endpointUrl", event.target.value)} placeholder="opc.tcp://192.168.1.10:4840" /></Field>
          <Field label="安全模式"><Select value={form.opcUa.securityMode} disabled={readOnly} onChange={event => update("securityMode", event.target.value)}><option value="none">无</option><option value="sign">签名</option><option value="sign-and-encrypt">签名并加密</option></Select></Field>
          <Field label="安全策略"><Select value={form.opcUa.securityPolicy} disabled={readOnly} onChange={event => update("securityPolicy", event.target.value)}><option value="None">无</option><option value="Basic256Sha256">Basic256Sha256</option><option value="Aes128_Sha256_RsaOaep">AES128</option><option value="Aes256_Sha256_RsaPss">AES256</option></Select></Field>
          <Field label="登录方式"><Select value={form.opcUa.authenticationType} disabled={readOnly} onChange={event => update("authenticationType", event.target.value)}><option value="anonymous">匿名</option><option value="username">用户名密码</option><option value="certificate">用户证书</option></Select></Field>
          {form.opcUa.authenticationType === "username" && <><Field label="用户名"><Input value={form.opcUa.username} disabled={readOnly} onChange={event => update("username", event.target.value)} /></Field><Field label="密码凭据"><Input value={form.opcUa.passwordSecretRef} disabled={readOnly} onChange={event => update("passwordSecretRef", event.target.value)} /></Field></>}
          {(form.opcUa.securityMode !== "none" || form.opcUa.authenticationType === "certificate") && <><Field label="客户端证书"><Input value={form.opcUa.clientCertificatePath} disabled={readOnly} onChange={event => update("clientCertificatePath", event.target.value)} /></Field><Field label="证书密码"><Input value={form.opcUa.clientCertificatePasswordSecretRef} disabled={readOnly} onChange={event => update("clientCertificatePasswordSecretRef", event.target.value)} /></Field></>}
          <Field label="发布间隔（ms）" hint="服务器向客户端发送订阅通知的节奏。"><Input type="number" min="1" value={form.opcUa.publishingIntervalMs} disabled={readOnly} onChange={event => update("publishingIntervalMs", event.target.value)} /></Field>
          <Field label="采样间隔（ms）" hint="服务器检查变量变化的最快节奏。"><Input type="number" min="1" value={form.opcUa.samplingIntervalMs} disabled={readOnly} onChange={event => update("samplingIntervalMs", event.target.value)} /></Field>
          <label className="flex items-center gap-2 text-sm text-amber-700"><input type="checkbox" checked={form.opcUa.trustServerCertificate} disabled={readOnly} onChange={event => update("trustServerCertificate", event.target.checked)} />自动信任未登记证书（仅调试）</label>
        </>}
        {form.protocol === "modbus-tcp" && <>
          <Field label="设备地址"><Input value={form.modbusTcp.host} disabled={readOnly} onChange={event => update("host", event.target.value)} placeholder="192.168.1.30" /></Field>
          <Field label="端口"><Input type="number" min="1" max="65535" value={form.modbusTcp.port} disabled={readOnly} onChange={event => update("port", event.target.value)} /></Field>
          <Field label="单元编号" hint="直连设备通常为 1；经网关访问多个从站时必须按现场设置填写。"><Input type="number" min="0" max="255" value={form.modbusTcp.unitId} disabled={readOnly} onChange={event => update("unitId", event.target.value)} /></Field>
          <Field label="地址起点" hint="必须与设备手册的寄存器编号方式一致。"><Select value={form.modbusTcp.addressBase} disabled={readOnly} onChange={event => update("addressBase", event.target.value)}><option value="zero-based">从 0 开始（线缆地址）</option><option value="one-based">从 1 开始（手册地址）</option></Select></Field>
          <Field label="轮询间隔（ms）"><Input type="number" min="1" value={form.modbusTcp.pollIntervalMs} disabled={readOnly} onChange={event => update("pollIntervalMs", event.target.value)} /></Field>
        </>}
        {form.protocol === "melsec-a1e" && <>
          <Field label="PLC 地址"><Input value={form.melsecA1E.host} disabled={readOnly} onChange={event => update("host", event.target.value)} placeholder="192.168.1.40" /></Field>
          <Field label="开放端口" hint="填写 FX 参数中设置为 MC 协议的端口，不是 GX Works/MELSOFT 端口。"><Input type="number" min="1" max="65535" value={form.melsecA1E.port} disabled={readOnly} onChange={event => update("port", event.target.value)} /></Field>
          <Field label="帧格式"><Select value="a-1e" disabled><option value="a-1e">A 兼容 1E 帧</option></Select></Field>
          <Field label="数据编码" hint="必须与 PLC 以太网模块中的通信数据码设置一致。"><Select value={form.melsecA1E.dataCode} disabled={readOnly} onChange={event => update("dataCode", event.target.value)}><option value="binary">二进制</option><option value="ascii">ASCII 码</option></Select></Field>
          <Field label="目标站号" hint="直连 FX3U/FX3UC 通常为 255（FFH）。"><Input type="number" min="0" max="255" value={form.melsecA1E.pcNumber} disabled={readOnly} onChange={event => update("pcNumber", event.target.value)} /></Field>
          <Field label="监视定时器" hint="单位为 250ms；16 表示约 4 秒。"><Input type="number" min="0" max="65535" value={form.melsecA1E.monitoringTimer} disabled={readOnly} onChange={event => update("monitoringTimer", event.target.value)} /></Field>
          <Field label="轮询间隔（ms）"><Input type="number" min="1" value={form.melsecA1E.pollIntervalMs} disabled={readOnly} onChange={event => update("pollIntervalMs", event.target.value)} /></Field>
        </>}
      </div>
    </Card>
  );
}

function MappingRows({ form, onChange, field, title, options, protocol, readOnly, probe }) {
  return (
    <Card title={title} actions={!readOnly ? <Button onClick={() => addRow(form, onChange, field, { ...valueMapping(), sourceDataType: ["modbus-tcp", "melsec-a1e"].includes(protocol) ? "int16" : "auto" })}>添加点位</Button> : undefined}>
      <div className="grid gap-4">
        {form[field].map((item, index) => {
          const preview = probe?.mappings?.find(value => value.dataItemCode === item.dataItemCode);
          const definition = options.find(option => option.code === item.dataItemCode);
          const pathListId = `${field}-device-points-${index}`;
          const registerProtocol = ["modbus-tcp", "melsec-a1e"].includes(protocol);
          const sourceType = registerProtocol && !registerDataTypes.includes(item.sourceDataType) ? "int16" : item.sourceDataType;
          const wordCount = registerQuantity(sourceType, item.modbusQuantity, item.modbusQuantity);
          return (
          <div key={index} className="grid gap-3 rounded-xl border border-slate-200 p-4 md:grid-cols-2 xl:grid-cols-3">
            <Field label="平台变量"><Select value={item.dataItemCode} disabled={readOnly} onChange={event => {
              const selected = options.find(option => option.code === event.target.value);
              updateRow(form, onChange, field, index, { dataItemCode: event.target.value, required: selected ? !selected.nullable : item.required });
            }}><option value="">请选择</option>{options.map(option => <option key={option.code} value={option.code}>{option.sourceField || option.code}</option>)}</Select></Field>
            {protocol === "modbus-tcp" ? <>
              <Field label="寄存器区"><Select value={item.modbusArea} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { modbusArea: event.target.value })}><option value="holding-register">保持寄存器</option><option value="input-register">输入寄存器</option><option value="coil">线圈</option><option value="discrete-input">离散输入</option></Select></Field>
              <Field label="地址" hint={`当前按${form.modbusTcp.addressBase === "one-based" ? "从 1 开始的手册地址" : "从 0 开始的线缆地址"}填写。`}><Input type="number" min={form.modbusTcp.addressBase === "one-based" ? "1" : "0"} value={item.modbusAddress} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { modbusAddress: event.target.value })} /></Field>
            </> : protocol === "melsec-a1e" ? <>
              <Field label="软元件"><Select value={item.melsecDevice} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { melsecDevice: event.target.value })}>{["D", "R", "M", "X", "Y", "S", "T", "C"].map(value => <option key={value} value={value}>{value}</option>)}</Select></Field>
              <Field label="软元件编号"><Input type="number" min="0" value={item.melsecAddress} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { melsecAddress: event.target.value })} placeholder="例如 100" /></Field>
            </> : <Field label={protocol === "opc-ua" ? "节点编号" : "JSON 字段"} hint={probe ? "可从刚读取的设备点位中选择。" : "先测试连接后可直接选择设备点位。"}><Input list={pathListId} value={item.sourcePath} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { sourcePath: event.target.value })} /><datalist id={pathListId}>{(probe?.points || []).map(point => <option key={point.path} value={point.path}>{point.name}</option>)}</datalist></Field>}
            <Field label="设备数据类型"><Select value={sourceType} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { sourceDataType: event.target.value, modbusQuantity: registerQuantity(event.target.value, item.modbusQuantity, item.modbusQuantity) })}>{(registerProtocol ? registerDataTypes : ["auto", ...registerDataTypes]).map(value => <option key={value} value={value}>{value === "auto" ? "按样本识别" : value}</option>)}</Select></Field>
            {protocol === "modbus-tcp" && <>
              {sourceType === "string" ? <Field label="寄存器数量"><Input type="number" min="1" max="64" value={item.modbusQuantity} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { modbusQuantity: event.target.value })} /></Field> : <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600"><p className="text-xs text-slate-500">占用寄存器</p><p>{wordCount} 个</p></div>}
              <Field label="字节序" hint="单个 16 位寄存器内两个字节的顺序。"><Select value={item.byteOrder} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { byteOrder: event.target.value })}><option value="big-endian">高字节在前（AB）</option><option value="little-endian">低字节在前（BA）</option></Select></Field>
              {wordCount > 1 && <Field label="字序" hint="32/64 位值跨多个寄存器时的顺序。"><Select value={item.wordOrder} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { wordOrder: event.target.value })}><option value="high-low">高字在前（ABCD）</option><option value="low-high">低字在前（CDAB）</option></Select></Field>}
            </>}
            {protocol === "melsec-a1e" && sourceType === "string" && <Field label="文本长度（字节）"><Input type="number" min="1" max="128" value={item.melsecStringLength} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { melsecStringLength: event.target.value })} /></Field>}
            <Field label="换算倍率"><Input type="number" step="any" value={item.scale} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { scale: event.target.value })} /></Field>
            <Field label="换算偏移"><Input type="number" step="any" value={item.offset} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { offset: event.target.value })} /></Field>
            <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
              <p className="text-xs text-slate-500">平台目标</p>
              <p>{definition ? `${definition.dataType} · ${definition.unit || "无单位"} · ${definition.nullable ? "允许缺失" : "周期必需"}` : "请先选择平台语义"}</p>
            </div>
            {!readOnly && form[field].length > 1 && <Button variant="ghost" className="justify-self-start text-rose-700" onClick={() => removeRow(form, onChange, field, index)}>移除</Button>}
            {preview && <div className="rounded-lg bg-slate-50 p-3 text-sm md:col-span-2 xl:col-span-3">
              <div className="grid gap-2 sm:grid-cols-4">
                <div><p className="text-xs text-slate-500">原始值</p><p className="font-medium text-slate-800">{preview.rawValue ?? "未读取"}</p></div>
                <div><p className="text-xs text-slate-500">换算值</p><p className="font-medium text-slate-800">{preview.convertedValue ?? "—"}</p></div>
                <div><p className="text-xs text-slate-500">类型</p><p className="font-medium text-slate-800">{preview.dataType || "—"}</p></div>
                <div><p className="text-xs text-slate-500">单位</p><p className="font-medium text-slate-800">{preview.unit || "—"}</p></div>
              </div>
              {preview.error && <p className="mt-2 text-rose-700">{preview.error}</p>}
            </div>}
          </div>
        );})}
      </div>
    </Card>
  );
}

function OptionalRecipe({ form, onChange, parameters, protocol, readOnly }) {
  function update(field, value) {
    updateNested(form, onChange, "recipe", field, value);
  }
  const recipeForm = useMemo(() => ({ ...form, recipeMappings: form.recipe.parameterMappings }), [form]);
  function setRecipeForm(value) {
    onChange({ ...form, recipe: { ...form.recipe, parameterMappings: value.recipeMappings } });
  }
  return (
    <Card title="设备配方识别" actions={<label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={form.recipe.enabled} disabled={readOnly} onChange={event => update("enabled", event.target.checked)} />启用</label>}>
      {form.recipe.enabled ? <div className="grid gap-4">
        <div className="grid gap-4 md:grid-cols-2">
          <Field label="配方编号字段"><Input value={form.recipe.idPath} disabled={readOnly} onChange={event => update("idPath", event.target.value)} /></Field>
          <Field label="配方版本字段"><Input value={form.recipe.versionPath} disabled={readOnly} onChange={event => update("versionPath", event.target.value)} /></Field>
          <Field label="配方名称字段"><Input value={form.recipe.namePath} disabled={readOnly} onChange={event => update("namePath", event.target.value)} /></Field>
          {protocol === "http-polling" && <Field label="参数集合字段"><Input value={form.recipe.parametersPath} disabled={readOnly} onChange={event => update("parametersPath", event.target.value)} /></Field>}
        </div>
        <MappingRows form={recipeForm} onChange={setRecipeForm} field="recipeMappings" title="配方参数映射" options={parameters} protocol={protocol} readOnly={readOnly} />
      </div> : <p className="text-sm text-slate-500">当前采集任务不从设备数据识别配方。</p>}
    </Card>
  );
}

function OptionalLifecycle({ form, onChange, readOnly }) {
  const update = (field, value) => updateNested(form, onChange, "lifecycle", field, value);
  return (
    <Card title="周期边界识别" actions={<label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={form.lifecycle.enabled} disabled={readOnly} onChange={event => update("enabled", event.target.checked)} />启用</label>}>
      {form.lifecycle.enabled ? <div className="grid gap-4">
        <p className="text-sm text-slate-500">设备只提供生产状态；Edge 在生产开始时自动生成周期关联号，在生产结束时关闭周期。阶段号从工艺语义模型中用途为“阶段号”的变量自动识别。</p>
        <div className="grid gap-4 md:grid-cols-2">
          <Field label="生产状态上下文字段"><Input value={form.lifecycle.activeContextKey || ""} disabled={readOnly} onChange={event => update("activeContextKey", event.target.value)} /></Field>
          <Field label="生产中状态值"><Input value={form.lifecycle.activeValue || ""} disabled={readOnly} onChange={event => update("activeValue", event.target.value)} /></Field>
        </div>
      </div> : <p className="text-sm text-slate-500">适用于连续设备或不需要自动识别周期边界的场景。</p>}
    </Card>
  );
}

export function RegistryBusinessEditor({ kind, form, onChange, readOnly, lockIdentity, validation, onAcquisitionProbeStateChange }) {
  return (
    <div className="grid gap-5">
      {!readOnly && validation && <Alert tone="warning">{validation}</Alert>}
      {kind === "qualityPlan" && <QualityPlanEditor form={form} onChange={onChange} readOnly={readOnly} lockIdentity={lockIdentity} />}
      {kind === "processModel" && <ProcessModelEditor form={form} onChange={onChange} readOnly={readOnly} lockIdentity={lockIdentity} />}
      {kind === "recipeVersion" && <RecipeEditor form={form} onChange={onChange} readOnly={readOnly} lockIdentity={lockIdentity} />}
      {kind === "analysisPlan" && <AnalysisPlanEditor form={form} onChange={onChange} readOnly={readOnly} lockIdentity={lockIdentity} />}
      {kind === "acquisitionProfile" && <AcquisitionEditor form={form} onChange={onChange} readOnly={readOnly} lockIdentity={lockIdentity} onProbeStateChange={onAcquisitionProbeStateChange} />}
    </div>
  );
}
