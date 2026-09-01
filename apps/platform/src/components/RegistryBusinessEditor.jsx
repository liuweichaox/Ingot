
import { extractRows, useApi } from "../hooks/useApi";
import { Alert, Button, Card, Field, Input, Select, Textarea } from "../ui/components";

// 编辑业务字典定义；页面只提交结构化注册表数据，不承担运行证据或优化决策流程。
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

const contextFieldCatalog = [
  ["execution_id", "运行标识", "平台在接收运行事件时生成", "标识一次生产运行；分析准入必需"],
  ["equipment_id", "设备", "事件主题或现场接入映射", "区分生产设备；常用于同类运行匹配"],
  ["product_family_code", "产品系列", "生产运行 → 生产上下文", "默认同类比较条件"],
  ["product_code", "产品", "生产运行 → 生产上下文", "区分具体产品"],
  ["process_specification_id", "工艺规范", "生产运行 → 生产上下文", "关联生效的工艺规范"],
  ["process_specification_version", "工艺规范版本", "生产运行 → 生产上下文", "防止跨版本误比较"],
  ["output_item_id", "产出物", "设备事件或上游系统", "关联本次运行产出"],
  ["tooling_assembly_id", "工装总成", "生产运行 → 工装装卸", "区分实际装机工装"],
  ["assembly_revision", "工装版本", "生产运行 → 工装装卸", "追踪工装配置变化"],
  ["tooling_usage_count", "工装累计运行次数", "平台按工装运行历史计算", "评估寿命和磨损"],
  ["material_lot_ref", "材料批次", "生产运行 → 生产上下文", "材料分层与追溯"],
  ["material_specification", "材料规格", "生产运行 → 生产上下文", "区分材料规格"],
  ["external_order_ref", "外部工单", "MES 同步或人工生产准备", "关联外部工单"],
  ["external_batch_ref", "外部批次", "MES 同步或人工生产准备", "关联外部生产批次"],
  ["maintenance_status", "维护状态", "生产运行 → 生产上下文", "识别维护状态影响"],
  ["calibration_status", "校准状态", "生产运行 → 生产上下文", "识别传感器校准风险"],
  ["calibration_ref", "校准记录", "生产运行 → 生产上下文", "追溯校准证据"],
  ["calibration_valid_until", "校准有效期", "生产运行 → 生产上下文", "判断运行时校准是否过期"],
].map(([fieldCode, name, source, purpose]) => ({ fieldCode, name, source, purpose }));

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
    displayName: value.displayName || "",
    dataType: value.dataType || "double",
    unit: value.unit || "",
    category: value.category || "process",
    nullable: value.nullable !== false,
  };
}

function controlParameter(value = {}) {
  return {
    code: value.code || "",
    displayName: value.displayName || "",
    dataType: value.dataType || "double",
    unit: value.unit || "",
    nullable: value.nullable !== false,
  };
}

const opticalMoldingStarter = {
  name: "精密模压工艺数据字典",
  description: "精密模压通用起始结构；发布前请按现场设备和产品补充、删减变量。",
  dataItems: [
    dataItem({ code: "mold.temperature", displayName: "模具温度", unit: "°C", nullable: false }),
    dataItem({ code: "press.force", displayName: "压制力", unit: "kN", nullable: false }),
    dataItem({ code: "plunger.position", displayName: "压头位移", unit: "mm" }),
    dataItem({ code: "process.stage", displayName: "工艺阶段", dataType: "integer", category: "stage", unit: "", nullable: false }),
    dataItem({ code: "surface.error", displayName: "面形误差", unit: "μm", category: "quality" }),
  ],
  controlParameters: [
    controlParameter({ code: "holding.temperature", displayName: "保压温度", unit: "°C", nullable: false }),
    controlParameter({ code: "holding.pressure", displayName: "保压压力", unit: "kN", nullable: false }),
  ],
};

function controlParameterValue(value = {}) {
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

function knownUnmeasuredConfounder(value = {}) {
  return {
    code: value.code || "",
    name: value.name || "",
    description: value.description || "",
  };
}

function versionedReference(value = {}) {
  return { reference: modelValue(value.id, value.version) };
}

function scenarioContextField(value = {}) {
  return {
    fieldCode: value.fieldCode || "",
    name: value.name || "",
    mode: value.mode || "record-when-available",
    minimumCoverage: value.minimumCoverage ?? "",
    minimumFactorOverlap: value.minimumFactorOverlap ?? "",
  };
}

function scenarioConstraint(value = {}) {
  return {
    code: value.code || "",
    name: value.name || "",
    severity: value.severity || "hard",
    unit: value.unit || "",
    minimum: value.minimum ?? "",
    maximum: value.maximum ?? "",
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
          productFamilyCode: value.scope?.productFamilyCode || "",
          productCode: value.scope?.productCode || "",
          processSpecificationId: value.scope?.processSpecificationId || "",
          equipmentId: value.scope?.equipmentId || "",
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
        controlParameters: (value.controlParameters || []).map(controlParameter),
      };
    case "processSpecificationVersion":
      return {
        processSpecificationId: value.processSpecificationId || "",
        version: version ?? value.version ?? 1,
        name: value.name || "",
        basedOnVersion: value.basedOnVersion ?? "",
        dataModel: modelValue(value.dataModelId, value.dataModelVersion),
        status: versionedStatus(value, version),
        contextPairs: pairsFromObject(value.contextSelector),
        values: (value.values || []).map(controlParameterValue),
      };
    case "analysisPlan":
      return {
        planId: value.planId || "",
        version: version ?? value.version ?? 1,
        name: value.name || "",
        description: value.description || "",
        status: versionedStatus(value, version),
        dataModel: modelValue(value.dataModelId, value.dataModelVersion),
        analysisScope: value.analysisScope || "production-execution",
        alignmentMode: value.alignmentMode || "stage-relative",
        cohortDimension: value.cohortDimension || "",
        comparisonKeys: (value.comparisonKeys || ["product_family_code"]).join(", "),
        contextPairs: pairsFromObject(value.contextSelector),
        knownUnmeasuredConfounders: (value.knownUnmeasuredConfounders || []).map(knownUnmeasuredConfounder),
        signals: (value.signals || []).length ? value.signals.map(analysisSignal) : [analysisSignal()],
      };
    case "scenarioPackage":
      return {
        packageId: value.packageId || "",
        version: version ?? value.version ?? 1,
        name: value.name || "",
        description: value.description || "",
        status: versionedStatus(value, version),
        dataModel: modelValue(value.dataModelId, value.dataModelVersion),
        analysisPlan: modelValue(value.analysisPlanId, value.analysisPlanVersion),
        ingestionTasks: (value.ingestionTasks || []).map(versionedReference),
        qualityPlan: value.qualityPlan ? modelValue(value.qualityPlan.id, value.qualityPlan.version) : "",
        contextFields: (value.contextFields || []).map(scenarioContextField),
        constraints: (value.constraints || []).map(scenarioConstraint),
        knowledgeAssets: (value.knowledgeAssets || []).map(versionedReference),
        terminologyPairs: pairsFromObject(value.terminology),
      };
    default:
      return {};
  }
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
        productFamilyCode: form.scope.productFamilyCode.trim() || null,
        productCode: form.scope.productCode.trim() || null,
        processSpecificationId: form.scope.processSpecificationId.trim() || null,
        equipmentId: form.scope.equipmentId.trim() || null,
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
          displayName: item.displayName.trim(),
          unit: item.unit.trim() || null,
        })),
      },
      controlParameters: form.controlParameters.map(item => ({
        ...item,
        code: item.code.trim(),
        displayName: item.displayName.trim(),
        unit: item.unit.trim() || null,
      })),
    };
  }
  if (kind === "processSpecificationVersion") {
    const selectedModel = parseModelValue(form.dataModel);
    return {
      processSpecificationId: form.processSpecificationId.trim(),
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
      knownUnmeasuredConfounders: form.knownUnmeasuredConfounders
        .filter(item => item.code.trim() && item.name.trim())
        .map(item => ({
          code: item.code.trim(),
          name: item.name.trim(),
          description: item.description.trim() || null,
        })),
      signals: form.signals.map(item => ({
        dataItemCode: item.dataItemCode.trim(),
        includeTrace: item.includeTrace,
        features: item.features,
      })),
    };
  }
  if (kind === "scenarioPackage") {
    const dataModel = parseModelValue(form.dataModel);
    const analysisPlan = parseModelValue(form.analysisPlan);
    const qualityPlan = parseModelValue(form.qualityPlan);
    const references = rows => rows.filter(item => item.reference).map(item => {
      const parsed = parseModelValue(item.reference);
      return { id: parsed.id, version: parsed.version };
    });
    return {
      packageId: form.packageId.trim(),
      version: Number(form.version),
      name: form.name.trim(),
      description: form.description.trim() || null,
      status: form.status,
      dataModelId: dataModel.id,
      dataModelVersion: dataModel.version,
      analysisPlanId: analysisPlan.id,
      analysisPlanVersion: analysisPlan.version,
      ingestionTasks: references(form.ingestionTasks),
      qualityPlan: qualityPlan.id ? { id: qualityPlan.id, version: qualityPlan.version } : null,
      contextFields: form.contextFields.map(item => ({
        fieldCode: item.fieldCode.trim(),
        name: item.name.trim(),
        mode: item.mode,
        minimumCoverage: numberOrNull(item.minimumCoverage),
        minimumFactorOverlap: numberOrNull(item.minimumFactorOverlap),
      })),
      constraints: form.constraints.map(item => ({
        code: item.code.trim(),
        name: item.name.trim(),
        severity: item.severity,
        unit: item.unit.trim() || null,
        minimum: numberOrNull(item.minimum),
        maximum: numberOrNull(item.maximum),
      })),
      knowledgeAssets: references(form.knowledgeAssets),
      terminology: objectFromPairs(form.terminologyPairs),
    };
  }
  throw new Error(`未知的配置类型：${kind}`);
}

export function registryBusinessValidation(kind, form) {
  const identity = kind === "processModel" ? form.modelId
    : kind === "processSpecificationVersion" ? form.processSpecificationId
      : kind === "scenarioPackage" ? form.packageId
        : form.planId;
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
    const allItems = [...form.dataItems, ...form.controlParameters];
    if (allItems.some(item => !codePattern.test(item.code.trim()) || !item.displayName.trim())) return "工艺变量和控制参数需填写有效代码与显示名称。";
  }
  if (kind === "processSpecificationVersion") {
    if (!form.dataModel) return "请选择工艺数据字典。";
    if (form.values.some(item => !item.code || item.value === "")) return "控制参数需选择参数并填写值。";
  }
  if (kind === "analysisPlan") {
    if (!form.dataModel) return "请选择工艺数据字典。";
    if (!form.comparisonKeys.trim()) return "请至少填写一个同类比较字段。";
    if (form.signals.length === 0 || form.signals.some(item => !item.dataItemCode)) return "请至少选择一个分析数据项。";
    if (form.knownUnmeasuredConfounders.some(item => !codePattern.test(item.code.trim()) || !item.name.trim())) return "潜在未测量混杂因素需填写有效代码和名称。";
  }
  if (kind === "scenarioPackage") {
    if (!form.dataModel || !form.analysisPlan) return "请选择工艺数据字典和分析规则。";
    if (form.contextFields.some(item => !codePattern.test(item.fieldCode.trim()) || !item.name.trim())) return "上下文字段需填写有效代码和名称。";
    if (form.contextFields.some(item => item.mode !== "record-when-available" && item.minimumCoverage === "")) return "进入分析或建模的上下文字段必须填写最低覆盖率。";
    if (form.constraints.some(item => !codePattern.test(item.code.trim()) || !item.name.trim() || (item.minimum === "" && item.maximum === ""))) return "安全约束需填写有效代码、名称和至少一个边界。";
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
          {[["productFamilyCode", "产品系列"], ["productCode", "产品编号"], ["processSpecificationId", "工艺规范编号"], ["equipmentId", "设备编号"]].map(([key, label]) => (
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
  const canApplyStarter = !readOnly && form.dataItems.length === 1 && !form.dataItems[0].code.trim() && form.controlParameters.length === 0;
  function applyStarter() {
    onChange({
      ...form,
      name: form.name || opticalMoldingStarter.name,
      description: form.description || opticalMoldingStarter.description,
      dataItems: opticalMoldingStarter.dataItems.map(item => ({ ...item })),
      controlParameters: opticalMoldingStarter.controlParameters.map(item => ({ ...item })),
    });
  }
  return (
    <div className="grid gap-5">
      <IdentityFields form={form} onChange={onChange} idField="modelId" idLabel="数据字典代码" readOnly={readOnly} lockIdentity={lockIdentity} />
      <Card
        title="数据字典职责"
        description="这里只定义平台如何理解数据，不填写 PLC 地址、设备点位或采集频率。"
        actions={canApplyStarter ? <Button variant="ghost" onClick={applyStarter}>应用精密模压示例</Button> : undefined}
      >
        <p className="text-sm leading-6 text-slate-600">同一数据字典可以复用于多台设备；每个来源的协议、地址、原始类型和换算规则在“现场接入”中配置。</p>
      </Card>
      <ItemDefinitions form={form} onChange={onChange} field="dataItems" title="工艺变量" readOnly={readOnly} includeCategory />
      <ItemDefinitions form={form} onChange={onChange} field="controlParameters" title="控制参数结构" readOnly={readOnly} />
    </div>
  );
}

function ItemDefinitions({ form, onChange, field, title, readOnly, includeCategory = false }) {
  const factory = field === "dataItems" ? dataItem : controlParameter;
  return (
    <Card
      title={title}
      description={field === "dataItems" ? "定义稳定业务代码、显示名称、平台类型和标准单位；阶段号的用途分类设为“阶段号”。" : "只定义控制参数的业务结构，具体来源地址在现场接入中映射。"}
      actions={!readOnly ? <Button onClick={() => addRow(form, onChange, field, factory())}>添加{title}</Button> : undefined}
    >
      <div className="grid gap-4">
        {form[field].length === 0 && <p className="text-sm text-slate-500">尚未添加。</p>}
        {form[field].map((item, index) => (
          <div key={index} className="grid gap-3 rounded-xl border border-slate-200 p-4 md:grid-cols-2 xl:grid-cols-3">
            <Field label="数据代码"><Input value={item.code} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { code: event.target.value })} /></Field>
            <Field label="显示名称"><Input value={item.displayName} disabled={readOnly} onChange={event => updateRow(form, onChange, field, index, { displayName: event.target.value })} /></Field>
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
      <option value="">请选择数据字典</option>
      {models.map(model => <option key={`${model.modelId}:${model.version}`} value={modelValue(model.modelId, model.version)}>{model.name}（v{model.version}）</option>)}
    </Select>
  );
}

function ProcessSpecificationEditor({ form, onChange, readOnly, lockIdentity }) {
  const { data, error } = useApi("/api/v1/process-data-models");
  const models = extractRows(data);
  const selected = parseModelValue(form.dataModel);
  const model = models.find(item => item.modelId === selected.id && item.version === selected.version);
  const parameters = model?.controlParameters || [];
  return (
    <div className="grid gap-5">
      {error && <Alert tone="danger">工艺数据字典读取失败：{error}</Alert>}
      <IdentityFields form={form} onChange={onChange} idField="processSpecificationId" idLabel="工艺规范代码" readOnly={readOnly} lockIdentity={lockIdentity} description={false} />
      <Card title="工艺规范来源">
        <div className="grid gap-4 md:grid-cols-2">
          <Field label="工艺数据字典"><ModelSelect value={form.dataModel} models={models} disabled={readOnly} onChange={event => updateAt(form, onChange, "dataModel", event.target.value)} /></Field>
          <Field label="沿用自版本"><Input type="number" min="1" value={form.basedOnVersion} disabled={readOnly} onChange={event => updateAt(form, onChange, "basedOnVersion", event.target.value)} placeholder="没有可留空" /></Field>
        </div>
      </Card>
      <PairEditor title="适用条件" description="例如产品系列或设备范围。" pairs={form.contextPairs} readOnly={readOnly} onChange={value => updateAt(form, onChange, "contextPairs", value)} />
      <Card title="控制参数" actions={!readOnly ? <Button onClick={() => addRow(form, onChange, "values", controlParameterValue())}>添加参数</Button> : undefined}>
        <div className="grid gap-3">
          {form.values.length === 0 && <p className="text-sm text-slate-500">尚未设置参数。</p>}
          {form.values.map((item, index) => {
            const parameter = parameters.find(value => value.code === item.code);
            return (
              <div key={index} className="grid gap-2 md:grid-cols-[1fr_1fr_auto]">
                <Select value={item.code} disabled={readOnly} aria-label={`控制参数 ${index + 1}`} onChange={event => {
                  const next = parameters.find(value => value.code === event.target.value);
                  updateRow(form, onChange, "values", index, { code: event.target.value, value: "", dataType: next?.dataType || "string" });
                }}>
                  <option value="">请选择参数</option>
                  {parameters.map(value => <option key={value.code} value={value.code}>{value.displayName || value.code}{value.unit ? `（${value.unit}）` : ""}</option>)}
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
      {error && <Alert tone="danger">工艺数据字典读取失败：{error}</Alert>}
      <IdentityFields form={form} onChange={onChange} idField="planId" idLabel="方案代码" readOnly={readOnly} lockIdentity={lockIdentity} />
      <Card title="分析方式">
        <div className="grid gap-4 md:grid-cols-2">
          <Field label="工艺数据字典"><ModelSelect value={form.dataModel} models={models} disabled={readOnly} onChange={event => updateAt(form, onChange, "dataModel", event.target.value)} /></Field>
          <Field label="分析范围"><Select value={form.analysisScope} disabled={readOnly} onChange={event => updateAt(form, onChange, "analysisScope", event.target.value)}><option value="production-execution">单次生产运行</option><option value="production-run">生产运行段</option><option value="analysis-window">自定义时间窗口</option></Select></Field>
          <Field label="曲线对齐方式"><Select value={form.alignmentMode} disabled={readOnly} onChange={event => updateAt(form, onChange, "alignmentMode", event.target.value)}><option value="stage-relative">按工艺阶段</option><option value="elapsed">按经过时间</option><option value="normalized">按归一化进度</option></Select></Field>
          <Field label="质量分组字段"><Input value={form.cohortDimension} disabled={readOnly} onChange={event => updateAt(form, onChange, "cohortDimension", event.target.value)} placeholder="例如 quality.outcome" /></Field>
          <Field label="同类比较字段" hint="多个字段用逗号分隔。" className="md:col-span-2"><Input value={form.comparisonKeys} disabled={readOnly} onChange={event => updateAt(form, onChange, "comparisonKeys", event.target.value)} placeholder="product_family_code, process_specification_id" /></Field>
        </div>
      </Card>
      <PairEditor title="分析对象筛选" description="只分析符合这些条件的生产记录。" pairs={form.contextPairs} readOnly={readOnly} onChange={value => updateAt(form, onChange, "contextPairs", value)} />
      <Card title="已知但尚未记录的潜在混杂因素" description="例如操作员经验或环境波动；系统会在分析结果中披露，但不会假装已经完成校正。" actions={!readOnly ? <Button onClick={() => addRow(form, onChange, "knownUnmeasuredConfounders", knownUnmeasuredConfounder())}>添加因素</Button> : undefined}>
        <div className="grid gap-3">
          {form.knownUnmeasuredConfounders.map((item, index) => (
            <div key={index} className="grid gap-3 rounded-xl border border-slate-200 p-4 md:grid-cols-[.7fr_1fr_1.4fr_auto]">
              <Field label="代码"><Input value={item.code} disabled={readOnly} onChange={event => updateRow(form, onChange, "knownUnmeasuredConfounders", index, { code: event.target.value })} placeholder="operator_experience" /></Field>
              <Field label="名称"><Input value={item.name} disabled={readOnly} onChange={event => updateRow(form, onChange, "knownUnmeasuredConfounders", index, { name: event.target.value })} placeholder="操作员经验" /></Field>
              <Field label="说明"><Input value={item.description} disabled={readOnly} onChange={event => updateRow(form, onChange, "knownUnmeasuredConfounders", index, { description: event.target.value })} /></Field>
              {!readOnly && <Button variant="ghost" className="self-end text-rose-700" onClick={() => removeRow(form, onChange, "knownUnmeasuredConfounders", index)}>移除</Button>}
            </div>
          ))}
          {form.knownUnmeasuredConfounders.length === 0 && <p className="text-sm text-slate-500">当前未登记；这不表示不存在未测量混杂。</p>}
        </div>
      </Card>
      <Card title="分析数据项" actions={!readOnly ? <Button onClick={() => addRow(form, onChange, "signals", analysisSignal())}>添加数据项</Button> : undefined}>
        <div className="grid gap-4">
          {form.signals.map((signal, index) => (
            <div key={index} className="grid gap-3 rounded-xl border border-slate-200 p-4">
              <div className="grid gap-3 md:grid-cols-[1fr_auto_auto]">
                <Field label={`数据项 ${index + 1}`}><Select value={signal.dataItemCode} disabled={readOnly} onChange={event => updateRow(form, onChange, "signals", index, { dataItemCode: event.target.value })}><option value="">请选择</option>{dataItems.map(item => <option key={item.code} value={item.code}>{item.displayName || item.code}</option>)}</Select></Field>
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

function ReferenceSelect({ value, options, idKey, label, disabled, onChange }) {
  return (
    <Select value={value} disabled={disabled} onChange={onChange}>
      <option value="">请选择{label}</option>
      {options.map(item => <option key={`${item[idKey]}:${item.version}`} value={modelValue(item[idKey], item.version)}>{item.name || item[idKey]}（v{item.version}）</option>)}
    </Select>
  );
}

function ScenarioPackageEditor({ form, onChange, readOnly, lockIdentity }) {
  const { data: modelData, error: modelError } = useApi("/api/v1/process-data-models");
  const { data: planData, error: planError } = useApi("/api/v1/process-analysis-plans");
  const { data: profileData, error: profileError } = useApi("/api/v1/ingestion-tasks");
  const { data: qualityData, error: qualityError } = useApi("/api/v1/inspection-plans");
  const { data: reliabilityData, error: reliabilityError } = useApi("/api/v1/data-reliability/baseline?maximumRuns=2000");
  const models = extractRows(modelData);
  const plans = extractRows(planData);
  const profiles = extractRows(profileData);
  const qualityPlans = extractRows(qualityData);
  const selectedModel = parseModelValue(form.dataModel);
  const matchingPlans = plans.filter(item => !selectedModel.id || (item.dataModelId === selectedModel.id && item.dataModelVersion === selectedModel.version));
  const matchingProfiles = profiles.filter(item => !selectedModel.id || (item.dataModelId === selectedModel.id && item.dataModelVersion === selectedModel.version));
  const referenceError = modelError || planError || profileError || qualityError;
  const coverageByField = Object.fromEntries((reliabilityData?.contextFields || []).map(item => [item.field, item]));
  const selectedContextCodes = new Set(form.contextFields.map(item => item.fieldCode));
  const addContextField = definition => {
    if (selectedContextCodes.has(definition.fieldCode)) return;
    addRow(form, onChange, "contextFields", scenarioContextField({
      fieldCode: definition.fieldCode,
      name: definition.name,
      mode: "record-when-available",
    }));
  };

  return (
    <div className="grid gap-5">
      {referenceError && <Alert tone="danger">场景依赖配置读取失败：{referenceError}</Alert>}
      <IdentityFields form={form} onChange={onChange} idField="packageId" idLabel="工艺配置代码" readOnly={readOnly} lockIdentity={lockIdentity} />
      <Card title="版本化配置组合" description="工艺配置只引用已定义资产；设备地址和业务数据仍由各自配置管理。">
        <div className="grid gap-4 md:grid-cols-2">
          <Field label="工艺数据字典"><ModelSelect value={form.dataModel} models={models} disabled={readOnly} onChange={event => updateAt(form, onChange, "dataModel", event.target.value)} /></Field>
          <Field label="分析方案"><ReferenceSelect value={form.analysisPlan} options={matchingPlans} idKey="planId" label="分析方案" disabled={readOnly} onChange={event => updateAt(form, onChange, "analysisPlan", event.target.value)} /></Field>
          <Field label="质量方案（可选）"><ReferenceSelect value={form.qualityPlan} options={qualityPlans} idKey="planId" label="质量方案" disabled={readOnly} onChange={event => updateAt(form, onChange, "qualityPlan", event.target.value)} /></Field>
        </div>
      </Card>
      <Card title="数据摄取任务" description="一个场景可以组合多数据源、多现场节点的已发布任务版本。" actions={!readOnly ? <Button onClick={() => addRow(form, onChange, "ingestionTasks", versionedReference())}>添加任务</Button> : undefined}>
        <div className="grid gap-3">
          {form.ingestionTasks.length === 0 && <p className="text-sm text-slate-500">尚未绑定数据摄取任务。</p>}
          {form.ingestionTasks.map((item, index) => <div key={index} className="grid gap-2 md:grid-cols-[1fr_auto]"><ReferenceSelect value={item.reference} options={matchingProfiles} idKey="taskId" label="摄取任务" disabled={readOnly} onChange={event => updateRow(form, onChange, "ingestionTasks", index, { reference: event.target.value })} />{!readOnly && <Button variant="ghost" className="text-rose-700" onClick={() => removeRow(form, onChange, "ingestionTasks", index)}>移除</Button>}</div>)}
        </div>
      </Card>
      <Card title="分析上下文" description="选择分析使用的运行字段，并设置覆盖率和准入方式。">
        <div className="grid gap-4">
          <div className="rounded-xl border border-blue-100 bg-blue-50/70 p-4 text-sm text-slate-700">
            <p className="font-semibold text-slate-950">数据链路</p>
            <p className="mt-1 leading-6">生产准备 / MES → 不可变运行上下文 → 数据可信度计算覆盖率 → 本策略决定是否仅追溯、分析必需或允许进入建模。</p>
            <p className="mt-1 text-xs text-slate-500">覆盖率基于当前 {reliabilityData?.analyzedRunCount ?? 0} 条已完成运行。</p>
          </div>
          {reliabilityError && <Alert tone="warning">暂时无法读取现场覆盖率：{reliabilityError}</Alert>}
          {!readOnly && (
            <div>
              <div className="mb-2 flex items-center justify-between gap-3"><p className="text-sm font-semibold text-slate-900">从字段目录添加</p><Button variant="ghost" onClick={() => addRow(form, onChange, "contextFields", scenarioContextField())}>自定义字段</Button></div>
              <div className="grid gap-2 lg:grid-cols-2">
                {contextFieldCatalog.map(definition => {
                  const coverage = coverageByField[definition.fieldCode];
                  const selected = selectedContextCodes.has(definition.fieldCode);
                  return <button key={definition.fieldCode} type="button" disabled={selected} onClick={() => addContextField(definition)} className="rounded-xl border border-slate-200 bg-white p-3 text-left transition hover:border-blue-300 hover:bg-blue-50 disabled:cursor-default disabled:border-emerald-200 disabled:bg-emerald-50/60">
                    <span className="flex items-start justify-between gap-2"><span className="font-medium text-slate-900">{definition.name}</span><span className="text-xs font-medium text-slate-500">{selected ? "已添加" : coverage?.coverage == null ? "暂无样本" : `覆盖 ${Math.round(coverage.coverage * 100)}%`}</span></span>
                    <span className="mt-1 block font-mono text-xs text-slate-500">{definition.fieldCode}</span>
                    <span className="mt-2 block text-xs leading-5 text-slate-600">来源：{definition.source}；用途：{definition.purpose}</span>
                  </button>;
                })}
              </div>
            </div>
          )}
          {form.contextFields.length === 0 && <Alert tone="warning" title="尚未选择上下文字段">先从上方目录添加字段。建议至少选择产品系列、设备和工装，以避免把不同生产条件直接混在一起。</Alert>}
          {form.contextFields.map((item, index) => <div key={index} className="grid gap-3 rounded-xl border border-slate-200 p-4 md:grid-cols-2 xl:grid-cols-3">
            <Field label="字段代码" hint={contextFieldCatalog.find(field => field.fieldCode === item.fieldCode)?.source ? `来源：${contextFieldCatalog.find(field => field.fieldCode === item.fieldCode).source}` : "自定义字段必须由现场接入或上游系统实际上报。"}><Input value={item.fieldCode} disabled={readOnly} onChange={event => updateRow(form, onChange, "contextFields", index, { fieldCode: event.target.value })} /></Field>
            <Field label="业务名称"><Input value={item.name} disabled={readOnly} onChange={event => updateRow(form, onChange, "contextFields", index, { name: event.target.value })} /></Field>
            <Field label="如何使用" hint="分析必需会排除缺失该字段的运行；进入建模还要求经过因素重叠验证。"><Select value={item.mode} disabled={readOnly} onChange={event => updateRow(form, onChange, "contextFields", index, { mode: event.target.value })}><option value="record-when-available">仅用于追溯</option><option value="required-for-analysis">缺失时禁止分析</option><option value="validated-for-modeling">验证后允许建模</option></Select></Field>
            <Field label="最低覆盖率" hint={`当前覆盖：${coverageByField[item.fieldCode]?.coverage == null ? "暂无样本" : `${Math.round(coverageByField[item.fieldCode].coverage * 100)}%（${coverageByField[item.fieldCode].presentRunCount}/${coverageByField[item.fieldCode].runCount}）`}`}><Input type="number" min="0" max="1" step="0.01" value={item.minimumCoverage} disabled={readOnly} onChange={event => updateRow(form, onChange, "contextFields", index, { minimumCoverage: event.target.value })} placeholder="例如 0.95" /></Field>
            <Field label="最低因素重叠" hint="只有把该字段作为分层/混杂因素时才填写；0.5 表示至少覆盖一半组合。"><Input type="number" min="0" max="1" step="0.01" value={item.minimumFactorOverlap} disabled={readOnly} onChange={event => updateRow(form, onChange, "contextFields", index, { minimumFactorOverlap: event.target.value })} placeholder="例如 0.5" /></Field>
            {!readOnly && <Button variant="ghost" className="justify-self-start text-rose-700" onClick={() => removeRow(form, onChange, "contextFields", index)}>移除</Button>}
          </div>)}
        </div>
      </Card>
      <Card title="场景安全约束" description="这里只记录场景默认边界；项目中的实际运行仍需工程师确认。" actions={!readOnly ? <Button onClick={() => addRow(form, onChange, "constraints", scenarioConstraint())}>添加约束</Button> : undefined}>
        <div className="grid gap-4">
          {form.constraints.length === 0 && <p className="text-sm text-slate-500">尚未配置默认约束。</p>}
          {form.constraints.map((item, index) => <div key={index} className="grid gap-3 rounded-xl border border-slate-200 p-4 md:grid-cols-2 xl:grid-cols-3">
            <Field label="约束代码"><Input value={item.code} disabled={readOnly} onChange={event => updateRow(form, onChange, "constraints", index, { code: event.target.value })} /></Field>
            <Field label="名称"><Input value={item.name} disabled={readOnly} onChange={event => updateRow(form, onChange, "constraints", index, { name: event.target.value })} /></Field>
            <Field label="级别"><Select value={item.severity} disabled={readOnly} onChange={event => updateRow(form, onChange, "constraints", index, { severity: event.target.value })}><option value="hard">硬约束</option><option value="soft">软约束</option></Select></Field>
            <Field label="下限"><Input type="number" step="any" value={item.minimum} disabled={readOnly} onChange={event => updateRow(form, onChange, "constraints", index, { minimum: event.target.value })} /></Field>
            <Field label="上限"><Input type="number" step="any" value={item.maximum} disabled={readOnly} onChange={event => updateRow(form, onChange, "constraints", index, { maximum: event.target.value })} /></Field>
            <Field label="单位"><Input value={item.unit} disabled={readOnly} onChange={event => updateRow(form, onChange, "constraints", index, { unit: event.target.value })} /></Field>
            {!readOnly && <Button variant="ghost" className="justify-self-start text-rose-700" onClick={() => removeRow(form, onChange, "constraints", index)}>移除</Button>}
          </div>)}
        </div>
      </Card>
      <PairEditor title="场景术语" description="只覆盖该场景需要不同显示名称的通用概念。" pairs={form.terminologyPairs} readOnly={readOnly} onChange={value => updateAt(form, onChange, "terminologyPairs", value)} />
    </div>
  );
}

export function RegistryBusinessEditor({ kind, form, onChange, readOnly, lockIdentity, validation }) {
  return (
    <div className="grid gap-5">
      {!readOnly && validation && <Alert tone="warning">{validation}</Alert>}
      {kind === "qualityPlan" && <QualityPlanEditor form={form} onChange={onChange} readOnly={readOnly} lockIdentity={lockIdentity} />}
      {kind === "processModel" && <ProcessModelEditor form={form} onChange={onChange} readOnly={readOnly} lockIdentity={lockIdentity} />}
      {kind === "processSpecificationVersion" && <ProcessSpecificationEditor form={form} onChange={onChange} readOnly={readOnly} lockIdentity={lockIdentity} />}
      {kind === "analysisPlan" && <AnalysisPlanEditor form={form} onChange={onChange} readOnly={readOnly} lockIdentity={lockIdentity} />}
      {kind === "scenarioPackage" && <ScenarioPackageEditor form={form} onChange={onChange} readOnly={readOnly} lockIdentity={lockIdentity} />}
    </div>
  );
}
