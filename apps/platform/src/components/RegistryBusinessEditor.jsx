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
  throw new Error(`未知的配置类型：${kind}`);
}

export function registryBusinessValidation(kind, form) {
  const identity = kind === "processModel" ? form.modelId
    : kind === "recipeVersion" ? form.recipeId
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

export function RegistryBusinessEditor({ kind, form, onChange, readOnly, lockIdentity, validation }) {
  return (
    <div className="grid gap-5">
      {!readOnly && validation && <Alert tone="warning">{validation}</Alert>}
      {kind === "qualityPlan" && <QualityPlanEditor form={form} onChange={onChange} readOnly={readOnly} lockIdentity={lockIdentity} />}
      {kind === "processModel" && <ProcessModelEditor form={form} onChange={onChange} readOnly={readOnly} lockIdentity={lockIdentity} />}
      {kind === "recipeVersion" && <RecipeEditor form={form} onChange={onChange} readOnly={readOnly} lockIdentity={lockIdentity} />}
      {kind === "analysisPlan" && <AnalysisPlanEditor form={form} onChange={onChange} readOnly={readOnly} lockIdentity={lockIdentity} />}
    </div>
  );
}
