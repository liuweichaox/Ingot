import { useMemo } from "react";
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

function processStage(value = {}) {
  return {
    sourceStep: value.sourceStep || "",
    code: value.code || "",
    name: value.name || "",
    expectedDurationSeconds: value.expectedDurationSeconds ?? 0,
    required: value.required !== false,
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
        samplePeriodMs: value.acquisition?.samplePeriodMs ?? 1000,
        stepSourceKey: value.acquisition?.stepSourceKey || "",
        dataItems: (value.acquisition?.dataItems || []).length ? value.acquisition.dataItems.map(dataItem) : [dataItem()],
        recipeParameters: (value.recipeParameters || []).map(recipeParameter),
        stages: (value.stages || []).map(processStage),
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
          trustServerCertificate: Boolean(value.opcUa?.trustServerCertificate),
          publishingIntervalMs: value.opcUa?.publishingIntervalMs ?? 1000,
          samplingIntervalMs: value.opcUa?.samplingIntervalMs ?? 1000,
        },
        modbusTcp: {
          host: value.modbusTcp?.host || "",
          port: value.modbusTcp?.port ?? 502,
          unitId: value.modbusTcp?.unitId ?? 1,
          pollIntervalMs: value.modbusTcp?.pollIntervalMs ?? 1000,
        },
        melsecA1E: {
          host: value.melsecA1E?.host || "",
          port: value.melsecA1E?.port ?? 5551,
          pollIntervalMs: value.melsecA1E?.pollIntervalMs ?? 1000,
          monitoringTimer: value.melsecA1E?.monitoringTimer ?? 16,
          wordOrderLayout: value.melsecA1E?.wordOrderLayout || "A",
        },
        execution: {
          timeoutMs: value.execution?.timeoutMs ?? 10000,
          reconnectDelayMs: value.execution?.reconnectDelayMs ?? 5000,
        },
        timestampMode: value.timestampMode || "source",
        timestampPath: value.timestampPath || "timestamp",
        sequencePath: value.sequencePath || "sequence",
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
          expectedDurationMs: value.lifecycle.expectedDurationMs ?? "",
        } : {
          enabled: false,
          mode: "discrete-cycle",
          correlationIdContextKey: "correlation_id",
          stepContextKey: "recipe_step",
          stepNameContextKey: "recipe_step_name",
          startedEventType: "cycle.started",
          completedEventType: "cycle.completed",
          stepChangedEventType: "recipe.step_changed",
          expectedDurationMs: "",
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
    sourcePath: form.protocol === "modbus-tcp" ? "" : item.sourcePath.trim(),
    required: item.required,
    sourceDataType: item.sourceDataType,
    scale: Number(item.scale),
    offset: Number(item.offset),
    modbusArea: form.protocol === "modbus-tcp" ? item.modbusArea : null,
    modbusAddress: form.protocol === "modbus-tcp" ? numberOrNull(item.modbusAddress) : null,
    modbusQuantity: Number(item.modbusQuantity) || 1,
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
    source: form.source.trim(),
    subjectType: form.subjectType.trim(),
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
      topics: form.mqtt.topics.filter(item => item.topic.trim()).map(item => ({ topic: item.topic.trim(), qos: Number(item.qos) })),
    } : null,
    opcUa: form.protocol === "opc-ua" ? {
      ...form.opcUa,
      username: form.opcUa.username.trim() || null,
      passwordSecretRef: form.opcUa.passwordSecretRef.trim() || null,
      clientCertificatePath: form.opcUa.clientCertificatePath.trim() || null,
      publishingIntervalMs: Number(form.opcUa.publishingIntervalMs),
      samplingIntervalMs: Number(form.opcUa.samplingIntervalMs),
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
      monitoringTimer: Number(form.melsecA1E.monitoringTimer),
    } : null,
    execution: {
      timeoutMs: Number(form.execution.timeoutMs),
      reconnectDelayMs: Number(form.execution.reconnectDelayMs),
    },
    timestampMode: form.timestampMode,
    timestampPath: form.timestampPath.trim(),
    sequencePath: form.sequencePath.trim() || null,
    sampleEventType: form.sampleEventType.trim(),
    staticContext: objectFromPairs(form.staticContextPairs),
    contextMappings: form.contextMappings.filter(item => item.contextKey.trim() || item.sourcePath.trim()).map(item => ({
      contextKey: item.contextKey.trim(),
      sourcePath: item.sourcePath.trim(),
      required: Boolean(item.required),
    })),
    valueMappings: form.valueMappings.map(mappingPayload),
    recipe: form.recipe.enabled ? {
      eventType: form.recipe.eventType.trim(),
      idPath: form.recipe.idPath.trim(),
      versionPath: form.recipe.versionPath.trim(),
      namePath: form.recipe.namePath.trim() || null,
      parametersPath: form.recipe.parametersPath.trim(),
      parameterMappings: form.recipe.parameterMappings.map(mappingPayload),
    } : null,
    lifecycle: form.lifecycle.enabled ? {
      mode: form.lifecycle.mode,
      correlationIdContextKey: form.lifecycle.correlationIdContextKey.trim(),
      stepContextKey: form.lifecycle.stepContextKey.trim() || null,
      stepNameContextKey: form.lifecycle.stepNameContextKey.trim() || null,
      startedEventType: form.lifecycle.startedEventType.trim(),
      completedEventType: form.lifecycle.completedEventType.trim(),
      stepChangedEventType: form.lifecycle.stepChangedEventType.trim(),
      expectedDurationMs: numberOrNull(form.lifecycle.expectedDurationMs),
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
        samplePeriodMs: Number(form.samplePeriodMs),
        stepSourceKey: form.stepSourceKey.trim() || null,
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
      stages: form.stages.map(item => ({
        ...item,
        sourceStep: item.sourceStep.trim(),
        code: item.code.trim(),
        name: item.name.trim(),
        expectedDurationSeconds: Number(item.expectedDurationSeconds),
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
    if (Number(form.samplePeriodMs) < 1) return "采样间隔必须大于 0 毫秒。";
    if (form.dataItems.length === 0) return "请至少添加一个采集数据项。";
    const allItems = [...form.dataItems, ...form.recipeParameters];
    if (allItems.some(item => !codePattern.test(item.code.trim()) || !item.sourceField.trim())) return "数据项和配方参数需填写有效代码与来源字段。";
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
    if (!form.edgeId || !form.dataModel || !form.source.trim() || !form.subjectId.trim()) return "请填写节点、数据模型、来源和设备编号。";
    if (form.valueMappings.length === 0 || form.valueMappings.some(item =>
      !item.dataItemCode || (form.protocol === "modbus-tcp" ? item.modbusAddress === "" : !item.sourcePath.trim()))) {
      return "请至少配置一个完整的采集数据项映射。";
    }
    if (form.protocol === "http-polling" && (!/^https?:\/\//.test(form.connection.baseUrl) || !form.connection.snapshotPath.trim())) return "请填写有效的设备 HTTP 地址和快照路径。";
    if (form.protocol === "mqtt" && (!form.mqtt.host.trim() || !form.mqtt.topics.some(item => item.topic.trim()))) return "请填写 MQTT 主机和至少一个订阅主题。";
    if (form.protocol === "opc-ua" && !/^(opc\.tcp|https):\/\//.test(form.opcUa.endpointUrl)) return "请填写有效的 OPC UA 端点。";
    if (["modbus-tcp", "melsec-a1e"].includes(form.protocol) && !form[form.protocol === "modbus-tcp" ? "modbusTcp" : "melsecA1E"].host.trim()) return "请填写设备主机地址。";
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
      <Card title="采集基础设置">
        <div className="grid gap-4 md:grid-cols-2">
          <Field label="采样间隔（毫秒）"><Input type="number" min="1" value={form.samplePeriodMs} disabled={readOnly} onChange={event => updateAt(form, onChange, "samplePeriodMs", event.target.value)} /></Field>
          <Field label="工艺步骤来源字段"><Input value={form.stepSourceKey} disabled={readOnly} onChange={event => updateAt(form, onChange, "stepSourceKey", event.target.value)} placeholder="没有可留空" /></Field>
        </div>
      </Card>
      <ItemDefinitions form={form} onChange={onChange} field="dataItems" title="采集数据项" readOnly={readOnly} includeCategory />
      <ItemDefinitions form={form} onChange={onChange} field="recipeParameters" title="配方参数" readOnly={readOnly} />
      <Card title="工艺阶段" actions={!readOnly ? <Button onClick={() => addRow(form, onChange, "stages", processStage())}>添加阶段</Button> : undefined}>
        <div className="grid gap-4">
          {form.stages.length === 0 && <p className="text-sm text-slate-500">当前模型不划分工艺阶段。</p>}
          {form.stages.map((stage, index) => (
            <div key={index} className="grid gap-3 rounded-xl border border-slate-200 p-4 md:grid-cols-2">
              <Field label="来源步骤"><Input value={stage.sourceStep} disabled={readOnly} onChange={event => updateRow(form, onChange, "stages", index, { sourceStep: event.target.value })} /></Field>
              <Field label="阶段代码"><Input value={stage.code} disabled={readOnly} onChange={event => updateRow(form, onChange, "stages", index, { code: event.target.value })} /></Field>
              <Field label="阶段名称"><Input value={stage.name} disabled={readOnly} onChange={event => updateRow(form, onChange, "stages", index, { name: event.target.value })} /></Field>
              <Field label="预计时长（秒）"><Input type="number" min="0" value={stage.expectedDurationSeconds} disabled={readOnly} onChange={event => updateRow(form, onChange, "stages", index, { expectedDurationSeconds: event.target.value })} /></Field>
              <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={stage.required} disabled={readOnly} onChange={event => updateRow(form, onChange, "stages", index, { required: event.target.checked })} />必须经过</label>
              {!readOnly && <Button variant="ghost" className="justify-self-start text-rose-700" onClick={() => removeRow(form, onChange, "stages", index)}>移除</Button>}
            </div>
          ))}
        </div>
      </Card>
    </div>
  );
}

function ItemDefinitions({ form, onChange, field, title, readOnly, includeCategory = false }) {
  const factory = field === "dataItems" ? dataItem : recipeParameter;
  return (
    <Card title={title} actions={!readOnly ? <Button onClick={() => addRow(form, onChange, field, factory())}>添加{title}</Button> : undefined}>
      <div className="grid gap-4">
        {form[field].length === 0 && <p className="text-sm text-slate-500">尚未添加。</p>}
        {form[field].map((item, index) => (
          <div key={index} className="grid gap-3 rounded-xl border border-slate-200 p-4 md:grid-cols-2 xl:grid-cols-3">
            <Field label="数据代码"><Input value={item.code} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { code: event.target.value })} /></Field>
            <Field label="来源字段"><Input value={item.sourceField} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { sourceField: event.target.value })} /></Field>
            <Field label="数据类型"><DataTypeSelect value={item.dataType} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { dataType: event.target.value })} /></Field>
            <Field label="单位"><Input value={item.unit} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { unit: event.target.value })} /></Field>
            {includeCategory && <Field label="用途分类"><Select value={item.category} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { category: event.target.value })}><option value="process">过程值</option><option value="setpoint">设定值</option><option value="state">状态</option><option value="quality">质量</option></Select></Field>}
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
      <option value="">请选择工艺数据模型</option>
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

function AcquisitionEditor({ form, onChange, readOnly, lockIdentity }) {
  const { data: modelData, error: modelError } = useApi("/api/v1/process-data-models");
  const { data: edgeData, error: edgeError } = useApi("/api/edges");
  const models = extractRows(modelData);
  const edges = extractRows(edgeData);
  const selected = parseModelValue(form.dataModel);
  const model = models.find(item => item.modelId === selected.id && item.version === selected.version);
  const dataItems = model?.acquisition?.dataItems || [];
  const recipeParameters = model?.recipeParameters || [];
  return (
    <div className="grid gap-5">
      {(modelError || edgeError) && <Alert tone="danger">{modelError || edgeError}</Alert>}
      <IdentityFields form={form} onChange={onChange} idField="profileId" idLabel="采集任务代码" readOnly={readOnly} lockIdentity={lockIdentity} description={false} />
      <Card title="采集对象">
        <div className="grid gap-4 md:grid-cols-2">
          <Field label="现场节点" hint="只显示已经登记并上报过心跳的节点。">
            <Select value={form.edgeId} disabled={readOnly} onChange={event => updateAt(form, onChange, "edgeId", event.target.value)}>
              <option value="">请选择现场节点</option>
              {form.edgeId && !edges.some(edge => edge.edgeId === form.edgeId) && <option value={form.edgeId}>{form.edgeId}（历史值）</option>}
              {edges.map(edge => <option key={edge.edgeId} value={edge.edgeId}>{edge.hostname || edge.displayName || edge.edgeId} · {edge.edgeId}</option>)}
            </Select>
          </Field>
          <Field label="工艺数据模型"><ModelSelect value={form.dataModel} models={models} disabled={readOnly} onChange={event => updateAt(form, onChange, "dataModel", event.target.value)} /></Field>
          <Field label="协议"><Select value={form.protocol} disabled={readOnly} onChange={event => updateAt(form, onChange, "protocol", event.target.value)}><option value="http-polling">HTTP 轮询</option><option value="mqtt">MQTT</option><option value="opc-ua">OPC UA</option><option value="modbus-tcp">Modbus TCP</option><option value="melsec-a1e">三菱 MELSEC 1E</option></Select></Field>
          <Field label="设备编号"><Input value={form.subjectId} disabled={readOnly} onChange={event => updateAt(form, onChange, "subjectId", event.target.value)} /></Field>
          <Field label="对象类型"><Select value={form.subjectType} disabled={readOnly} onChange={event => updateAt(form, onChange, "subjectType", event.target.value)}><option value="equipment">生产设备</option><option value="machine">设备</option></Select></Field>
          <Field label="事件来源"><Input value={form.source} disabled={readOnly} onChange={event => updateAt(form, onChange, "source", event.target.value)} placeholder={`connector/${form.protocol || "http-polling"}/device-01`} /></Field>
        </div>
      </Card>
      <ConnectionFields form={form} onChange={onChange} readOnly={readOnly} />
      <Card title="运行设置">
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          <Field label="连接超时（毫秒）"><Input type="number" min="100" value={form.execution.timeoutMs} disabled={readOnly} onChange={event => updateNested(form, onChange, "execution", "timeoutMs", event.target.value)} /></Field>
          <Field label="重连间隔（毫秒）"><Input type="number" min="100" value={form.execution.reconnectDelayMs} disabled={readOnly} onChange={event => updateNested(form, onChange, "execution", "reconnectDelayMs", event.target.value)} /></Field>
          <Field label="时间来源"><Select value={form.timestampMode} disabled={readOnly} onChange={event => updateAt(form, onChange, "timestampMode", event.target.value)}><option value="source">设备数据时间</option><option value="edge-received">边缘接收时间</option></Select></Field>
          {form.timestampMode === "source" && <Field label="时间字段"><Input value={form.timestampPath} disabled={readOnly} onChange={event => updateAt(form, onChange, "timestampPath", event.target.value)} /></Field>}
          <Field label="序号字段"><Input value={form.sequencePath} disabled={readOnly} onChange={event => updateAt(form, onChange, "sequencePath", event.target.value)} placeholder="没有可留空" /></Field>
          <Field label="采样事件类型"><Input value={form.sampleEventType} disabled={readOnly} onChange={event => updateAt(form, onChange, "sampleEventType", event.target.value)} /></Field>
        </div>
      </Card>
      <PairEditor title="固定生产信息" description="每条采样都附带的固定信息。" pairs={form.staticContextPairs} readOnly={readOnly} onChange={value => updateAt(form, onChange, "staticContextPairs", value)} />
      <MappingRows form={form} onChange={onChange} field="valueMappings" title="采集数据项" options={dataItems} protocol={form.protocol} readOnly={readOnly} />
      <Card title="动态生产信息" description="从设备数据中提取产品、批次或运行状态。" actions={!readOnly ? <Button onClick={() => addRow(form, onChange, "contextMappings", { contextKey: "", sourcePath: "", required: false })}>添加信息</Button> : undefined}>
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

function ConnectionFields({ form, onChange, readOnly }) {
  const group = form.protocol === "http-polling" ? "connection" : form.protocol === "mqtt" ? "mqtt" : form.protocol === "opc-ua" ? "opcUa" : form.protocol === "modbus-tcp" ? "modbusTcp" : "melsecA1E";
  const update = (field, value) => updateNested(form, onChange, group, field, value);
  return (
    <Card title="设备连接">
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        {form.protocol === "http-polling" && <>
          <Field label="设备地址"><Input value={form.connection.baseUrl} disabled={readOnly} onChange={event => update("baseUrl", event.target.value)} placeholder="http://192.168.1.10" /></Field>
          <Field label="快照路径"><Input value={form.connection.snapshotPath} disabled={readOnly} onChange={event => update("snapshotPath", event.target.value)} /></Field>
          <Field label="读取间隔（毫秒）"><Input type="number" min="1" value={form.connection.pollIntervalMs} disabled={readOnly} onChange={event => update("pollIntervalMs", event.target.value)} /></Field>
        </>}
        {form.protocol === "mqtt" && <>
          <Field label="MQTT 主机"><Input value={form.mqtt.host} disabled={readOnly} onChange={event => update("host", event.target.value)} /></Field>
          <Field label="端口"><Input type="number" min="1" max="65535" value={form.mqtt.port} disabled={readOnly} onChange={event => update("port", event.target.value)} /></Field>
          <Field label="协议版本"><Select value={form.mqtt.protocolVersion} disabled={readOnly} onChange={event => update("protocolVersion", event.target.value)}><option value="5.0">5.0</option><option value="3.1.1">3.1.1</option></Select></Field>
          <Field label="客户端编号"><Input value={form.mqtt.clientId} disabled={readOnly} onChange={event => update("clientId", event.target.value)} /></Field>
          <Field label="用户名"><Input value={form.mqtt.username} disabled={readOnly} onChange={event => update("username", event.target.value)} /></Field>
          <Field label="密码密钥引用"><Input value={form.mqtt.passwordSecretRef} disabled={readOnly} onChange={event => update("passwordSecretRef", event.target.value)} /></Field>
          <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={form.mqtt.useTls} disabled={readOnly} onChange={event => update("useTls", event.target.checked)} />启用 TLS</label>
          <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={form.mqtt.cleanSession} disabled={readOnly} onChange={event => update("cleanSession", event.target.checked)} />使用全新会话</label>
          <Field label="保活时间（秒）"><Input type="number" min="1" value={form.mqtt.keepAliveSeconds} disabled={readOnly} onChange={event => update("keepAliveSeconds", event.target.value)} /></Field>
          <div className="grid gap-2 md:col-span-2 xl:col-span-3">
            <div className="flex items-center justify-between"><p className="text-sm font-medium">订阅主题</p>{!readOnly && <Button onClick={() => update("topics", [...form.mqtt.topics, { topic: "", qos: 0 }])}>添加主题</Button>}</div>
            {form.mqtt.topics.map((topic, index) => <div key={index} className="grid gap-2 md:grid-cols-[1fr_8rem_auto]"><Input value={topic.topic} disabled={readOnly} aria-label={`MQTT 主题 ${index + 1}`} onChange={event => update("topics", form.mqtt.topics.map((item, rowIndex) => rowIndex === index ? { ...item, topic: event.target.value } : item))} /><Select value={topic.qos} disabled={readOnly} aria-label={`QoS ${index + 1}`} onChange={event => update("topics", form.mqtt.topics.map((item, rowIndex) => rowIndex === index ? { ...item, qos: Number(event.target.value) } : item))}><option value="0">QoS 0</option><option value="1">QoS 1</option><option value="2">QoS 2</option></Select>{!readOnly && form.mqtt.topics.length > 1 && <Button variant="ghost" className="text-rose-700" onClick={() => update("topics", form.mqtt.topics.filter((_item, rowIndex) => rowIndex !== index))}>移除</Button>}</div>)}
          </div>
        </>}
        {form.protocol === "opc-ua" && <>
          <Field label="OPC UA 端点"><Input value={form.opcUa.endpointUrl} disabled={readOnly} onChange={event => update("endpointUrl", event.target.value)} placeholder="opc.tcp://192.168.1.10:4840" /></Field>
          <Field label="安全模式"><Select value={form.opcUa.securityMode} disabled={readOnly} onChange={event => update("securityMode", event.target.value)}><option value="none">无</option><option value="sign">签名</option><option value="sign-and-encrypt">签名并加密</option></Select></Field>
          <Field label="安全策略"><Select value={form.opcUa.securityPolicy} disabled={readOnly} onChange={event => update("securityPolicy", event.target.value)}><option value="None">无</option><option value="Basic256Sha256">Basic256Sha256</option><option value="Aes128_Sha256_RsaOaep">AES128</option><option value="Aes256_Sha256_RsaPss">AES256</option></Select></Field>
          <Field label="身份认证"><Select value={form.opcUa.authenticationType} disabled={readOnly} onChange={event => update("authenticationType", event.target.value)}><option value="anonymous">匿名</option><option value="username">用户名</option><option value="certificate">证书</option></Select></Field>
          {form.opcUa.authenticationType === "username" && <><Field label="用户名"><Input value={form.opcUa.username} disabled={readOnly} onChange={event => update("username", event.target.value)} /></Field><Field label="密码密钥引用"><Input value={form.opcUa.passwordSecretRef} disabled={readOnly} onChange={event => update("passwordSecretRef", event.target.value)} /></Field></>}
          {(form.opcUa.securityMode !== "none" || form.opcUa.authenticationType === "certificate") && <Field label="客户端证书路径"><Input value={form.opcUa.clientCertificatePath} disabled={readOnly} onChange={event => update("clientCertificatePath", event.target.value)} /></Field>}
          <Field label="发布间隔（毫秒）"><Input type="number" min="1" value={form.opcUa.publishingIntervalMs} disabled={readOnly} onChange={event => update("publishingIntervalMs", event.target.value)} /></Field>
          <Field label="采样间隔（毫秒）"><Input type="number" min="1" value={form.opcUa.samplingIntervalMs} disabled={readOnly} onChange={event => update("samplingIntervalMs", event.target.value)} /></Field>
          <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={form.opcUa.trustServerCertificate} disabled={readOnly} onChange={event => update("trustServerCertificate", event.target.checked)} />信任服务器证书</label>
        </>}
        {form.protocol === "modbus-tcp" && <>
          <Field label="设备主机"><Input value={form.modbusTcp.host} disabled={readOnly} onChange={event => update("host", event.target.value)} /></Field>
          <Field label="端口"><Input type="number" min="1" max="65535" value={form.modbusTcp.port} disabled={readOnly} onChange={event => update("port", event.target.value)} /></Field>
          <Field label="从站编号"><Input type="number" min="0" max="255" value={form.modbusTcp.unitId} disabled={readOnly} onChange={event => update("unitId", event.target.value)} /></Field>
          <Field label="读取间隔（毫秒）"><Input type="number" min="1" value={form.modbusTcp.pollIntervalMs} disabled={readOnly} onChange={event => update("pollIntervalMs", event.target.value)} /></Field>
        </>}
        {form.protocol === "melsec-a1e" && <>
          <Field label="设备主机"><Input value={form.melsecA1E.host} disabled={readOnly} onChange={event => update("host", event.target.value)} /></Field>
          <Field label="MC 端口"><Input type="number" min="1" max="65535" value={form.melsecA1E.port} disabled={readOnly} onChange={event => update("port", event.target.value)} /></Field>
          <Field label="读取间隔（毫秒）"><Input type="number" min="1" value={form.melsecA1E.pollIntervalMs} disabled={readOnly} onChange={event => update("pollIntervalMs", event.target.value)} /></Field>
          <Field label="监视定时器"><Input type="number" min="0" max="65535" value={form.melsecA1E.monitoringTimer} disabled={readOnly} onChange={event => update("monitoringTimer", event.target.value)} /></Field>
          <Field label="软元件字段顺序"><Select value={form.melsecA1E.wordOrderLayout} disabled><option value="A">A（FX3U 标准）</option></Select></Field>
        </>}
      </div>
    </Card>
  );
}

function MappingRows({ form, onChange, field, title, options, protocol, readOnly }) {
  return (
    <Card title={title} actions={!readOnly ? <Button onClick={() => addRow(form, onChange, field, valueMapping())}>添加数据项</Button> : undefined}>
      <div className="grid gap-4">
        {form[field].map((item, index) => (
          <div key={index} className="grid gap-3 rounded-xl border border-slate-200 p-4 md:grid-cols-2 xl:grid-cols-3">
            <Field label="平台数据项"><Select value={item.dataItemCode} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { dataItemCode: event.target.value })}><option value="">请选择</option>{options.map(option => <option key={option.code} value={option.code}>{option.sourceField || option.code}</option>)}</Select></Field>
            {protocol === "modbus-tcp" ? <>
              <Field label="寄存器区"><Select value={item.modbusArea} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { modbusArea: event.target.value })}><option value="holding-register">保持寄存器</option><option value="input-register">输入寄存器</option><option value="coil">线圈</option><option value="discrete-input">离散输入</option></Select></Field>
              <Field label="寄存器地址"><Input type="number" min="0" value={item.modbusAddress} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { modbusAddress: event.target.value })} /></Field>
              <Field label="寄存器数量"><Input type="number" min="1" max="64" value={item.modbusQuantity} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { modbusQuantity: event.target.value })} /></Field>
            </> : <Field label="设备字段路径"><Input value={item.sourcePath} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { sourcePath: event.target.value })} /></Field>}
            <Field label="来源数据类型"><Select value={item.sourceDataType} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { sourceDataType: event.target.value })}>{["auto", "int16", "uint16", "int32", "uint32", "float32", "int64", "uint64", "float64", "string"].map(value => <option key={value} value={value}>{value === "auto" ? "自动识别" : value}</option>)}</Select></Field>
            <Field label="换算倍率"><Input type="number" step="any" value={item.scale} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { scale: event.target.value })} /></Field>
            <Field label="换算偏移"><Input type="number" step="any" value={item.offset} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { offset: event.target.value })} /></Field>
            <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={item.required} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { required: event.target.checked })} />必须采集</label>
            {!readOnly && form[field].length > 1 && <Button variant="ghost" className="justify-self-start text-rose-700" onClick={() => removeRow(form, onChange, field, index)}>移除</Button>}
          </div>
        ))}
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
          <Field label="配方事件类型"><Input value={form.recipe.eventType} disabled={readOnly} onChange={event => update("eventType", event.target.value)} /></Field>
          <Field label="配方编号字段"><Input value={form.recipe.idPath} disabled={readOnly} onChange={event => update("idPath", event.target.value)} /></Field>
          <Field label="配方版本字段"><Input value={form.recipe.versionPath} disabled={readOnly} onChange={event => update("versionPath", event.target.value)} /></Field>
          <Field label="配方名称字段"><Input value={form.recipe.namePath} disabled={readOnly} onChange={event => update("namePath", event.target.value)} /></Field>
          <Field label="参数集合字段"><Input value={form.recipe.parametersPath} disabled={readOnly} onChange={event => update("parametersPath", event.target.value)} /></Field>
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
      {form.lifecycle.enabled ? <div className="grid gap-4 md:grid-cols-2">
        <Field label="关联号字段"><Input value={form.lifecycle.correlationIdContextKey} disabled={readOnly} onChange={event => update("correlationIdContextKey", event.target.value)} /></Field>
        <Field label="步骤字段"><Input value={form.lifecycle.stepContextKey} disabled={readOnly} onChange={event => update("stepContextKey", event.target.value)} /></Field>
        <Field label="步骤名称字段"><Input value={form.lifecycle.stepNameContextKey} disabled={readOnly} onChange={event => update("stepNameContextKey", event.target.value)} /></Field>
        <Field label="预计周期时长（毫秒）"><Input type="number" min="1" value={form.lifecycle.expectedDurationMs} disabled={readOnly} onChange={event => update("expectedDurationMs", event.target.value)} /></Field>
      </div> : <p className="text-sm text-slate-500">适用于连续设备或不需要自动识别周期边界的场景。</p>}
    </Card>
  );
}

export function RegistryBusinessEditor({ kind, form, onChange, readOnly, lockIdentity, validation }) {
  return (
    <div className="grid gap-5">
      {!readOnly && validation && <Alert tone="warning">{validation}</Alert>}
      {kind === "qualityPlan" && <QualityPlanEditor form={form} onChange={onChange} readOnly={readOnly} lockIdentity={lockIdentity} />}
      {kind === "processModel" && <ProcessModelEditor form={form} onChange={onChange} readOnly={readOnly} lockIdentity={lockIdentity} />}
      {kind === "recipeVersion" && <RecipeEditor form={form} onChange={onChange} readOnly={readOnly} lockIdentity={lockIdentity} />}
      {kind === "analysisPlan" && <AnalysisPlanEditor form={form} onChange={onChange} readOnly={readOnly} lockIdentity={lockIdentity} />}
      {kind === "acquisitionProfile" && <AcquisitionEditor form={form} onChange={onChange} readOnly={readOnly} lockIdentity={lockIdentity} />}
    </div>
  );
}
