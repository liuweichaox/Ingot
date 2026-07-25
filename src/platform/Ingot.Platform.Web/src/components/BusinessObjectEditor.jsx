import { Alert, Button, Card, Field, Input, Select, Textarea } from "../ui/components";

const labels = {
  title: "标题", problemCode: "问题代码", description: "问题说明", contextSelector: "适用条件",
  cycleIds: "生产周期编号", modelId: "模型代码", version: "版本", name: "名称",
  modelKind: "模型用途", status: "状态", algorithm: "算法名称", datasetId: "训练数据集",
  datasetVersion: "数据集版本", artifactRef: "模型文件位置", artifactSha256: "模型文件校验值",
  inputFeatureCodes: "输入特征", outputCode: "输出指标", uncertaintyMethod: "不确定性方法",
  changeNote: "变更说明", equationKind: "方程类型", inputs: "输入变量", output: "输出变量",
  intercept: "常数项", coefficients: "变量系数", applicabilityContext: "适用条件",
  scientificBasis: "科学依据", sourceReference: "来源引用", fusionId: "融合方案代码",
  mode: "融合方式", mechanismModelId: "机理模型", mechanismModelVersion: "机理模型版本",
  dataModelId: "数据模型", dataModelVersion: "数据模型版本", calibrationScale: "校准倍率",
  calibrationOffset: "校准偏移", postProcessingGain: "后处理增益", mechanismReference: "机理参考值",
  mechanismWeight: "机理权重", mechanismFeatureCode: "机理特征代码", analysisPlanId: "分析方案",
  analysisPlanVersion: "分析方案版本", windowStart: "数据开始时间", windowEnd: "数据结束时间",
  featureCodes: "特征代码", targetCode: "目标指标", rowCount: "数据行数", contentHash: "内容校验值",
  investigationId: "调查编号", conclusionId: "结论编号", applicableContext: "适用条件",
  modelVersion: "模型版本",
  parameterSettings: "参数调整", constraints: "运行约束", expectedOutcomes: "预期结果",
  valueEstimate: "价值测算", riskSummary: "风险说明", stopRule: "停止条件", rollbackPlan: "回退方案",
  industry: "行业", process: "工艺", dataKind: "数据类型", isMeasuredData: "实测数据",
  sourceUri: "来源地址", retrievalUri: "下载地址", license: "数据许可", citation: "引用信息",
  archiveMemberPath: "压缩包内文件路径", doi: "DOI", sheetName: "工作表名称",
  matVariableName: "MAT 变量名称", timestampColumn: "时间戳列", phaseColumn: "阶段列",
  expectedSha256: "文件校验值", headerRowCount: "表头行数", cycleColumn: "周期列",
  signalColumns: "信号列", outcomeColumns: "结果列", minimumSignalNumericCoverage: "信号数值覆盖率",
  minimumOutcomeNumericCoverage: "结果数值覆盖率", units: "字段单位", validSignalRanges: "有效范围",
  from: "开始时间", to: "结束时间", correlationIds: "关联编号",
  fusionVersion: "融合方案版本", mechanismInputs: "机理输入", dataPrediction: "数据模型预测值",
  operatingContext: "运行条件",
  code: "代码", unit: "单位", validMinimum: "有效下限", validMaximum: "有效上限",
  parameterCode: "参数代码", phaseCode: "工艺阶段", currentValue: "当前值",
  recommendedValue: "建议值", allowedMinimum: "允许下限", allowedMaximum: "允许上限",
  operator: "判断符", limit: "限值", metricCode: "指标代码", baselineValue: "基准值",
  expectedValue: "预期值", lowerBound: "预期下限", upperBound: "预期上限",
  expectedAnnualValue: "预期年价值", trialCost: "试验成本", implementationCost: "实施成本",
  downsideAtRisk: "风险金额", currency: "币种", minimum: "下限", maximum: "上限", basis: "范围依据",
};

const selectOptions = {
  status: [["draft", "草稿"], ["validated", "已验证"], ["active", "使用中"], ["retired", "已停用"]],
  mode: [["calibration", "校准"], ["post-processing", "后处理"], ["mechanism-as-feature", "机理特征"], ["ensemble", "融合预测"]],
  dataKind: [["measured-experiment", "实测实验"], ["measured-production", "实测生产"], ["reference", "参考数据"]],
  modelKind: [["quality-risk", "质量风险"], ["regression", "回归预测"], ["classification", "分类判断"]],
  uncertaintyMethod: [["none", "不计算"], ["bootstrap", "自助法"], ["conformal", "保形预测"]],
  operator: [["<=", "不大于"], [">=", "不小于"], ["=", "等于"]],
};

const arraySchemas = {
  inputs: { code: "", unit: "1", validMinimum: "", validMaximum: "" },
  parameterSettings: { parameterCode: "", phaseCode: "", currentValue: 0, recommendedValue: 0, allowedMinimum: 0, allowedMaximum: 0, unit: "" },
  constraints: { code: "", description: "", operator: "<=", limit: 0, unit: "" },
  expectedOutcomes: { metricCode: "", baselineValue: 0, expectedValue: 0, unit: "", lowerBound: "", upperBound: "" },
};

const multilineFields = new Set(["description", "scientificBasis", "citation", "riskSummary", "stopRule", "rollbackPlan", "changeNote"]);
const dateFields = new Set(["windowStart", "windowEnd", "from", "to"]);
const numericMapFields = new Set(["coefficients", "mechanismInputs"]);
const rangeMapFields = new Set(["validSignalRanges"]);

function displayLabel(key) {
  return labels[key] || key.replace(/([A-Z])/g, " $1").trim();
}

function updateObject(value, onChange, key, nextValue) {
  onChange({ ...value, [key]: nextValue });
}

function ScalarField({ fieldKey, value, onChange }) {
  if (typeof value === "boolean") {
    return <label className="flex items-center gap-2 text-sm font-medium text-slate-700"><input type="checkbox" checked={value} onChange={event => onChange(event.target.checked)} />{displayLabel(fieldKey)}</label>;
  }
  if (selectOptions[fieldKey]) {
    return <Field label={displayLabel(fieldKey)}><Select value={value} onChange={event => onChange(event.target.value)}>{selectOptions[fieldKey].map(([optionValue, label]) => <option key={optionValue} value={optionValue}>{label}</option>)}</Select></Field>;
  }
  if (multilineFields.has(fieldKey)) {
    return <Field label={displayLabel(fieldKey)}><Textarea value={value ?? ""} onChange={event => onChange(event.target.value)} /></Field>;
  }
  return (
    <Field label={displayLabel(fieldKey)}>
      <Input
        type={dateFields.has(fieldKey) ? "datetime-local" : typeof value === "number" ? "number" : "text"}
        step={typeof value === "number" ? "any" : undefined}
        value={value ?? ""}
        onChange={event => onChange(typeof value === "number" ? Number(event.target.value) : event.target.value)}
      />
    </Field>
  );
}

function PrimitiveList({ fieldKey, value, onChange }) {
  return (
    <Field label={displayLabel(fieldKey)} hint="每行填写一项。">
      <Textarea
        className="min-h-24"
        value={value.join("\n")}
        onChange={event => onChange(event.target.value.split(/\r?\n/).map(item => item.trim()).filter(Boolean))}
      />
    </Field>
  );
}

function ObjectList({ fieldKey, value, onChange, schema }) {
  function update(index, key, nextValue) {
    onChange(value.map((item, rowIndex) => rowIndex === index ? { ...item, [key]: nextValue } : item));
  }
  return (
    <Card title={displayLabel(fieldKey)} actions={<Button onClick={() => onChange([...value, { ...schema }])}>添加一项</Button>}>
      <div className="grid gap-4">
        {value.length === 0 && <p className="text-sm text-slate-500">尚未添加。</p>}
        {value.map((item, index) => (
          <div key={index} className="grid gap-3 rounded-xl border border-slate-200 p-4 sm:grid-cols-2">
            {Object.entries(schema).map(([key, initial]) => (
              <ScalarField key={key} fieldKey={key} value={item[key] ?? initial} onChange={nextValue => update(index, key, nextValue)} />
            ))}
            <Button variant="ghost" className="justify-self-start text-rose-700" onClick={() => onChange(value.filter((_item, rowIndex) => rowIndex !== index))}>移除</Button>
          </div>
        ))}
      </div>
    </Card>
  );
}

function MapFields({ fieldKey, value, onChange }) {
  const entries = Object.entries(value || {});
  const rows = entries.length ? entries : [["", rangeMapFields.has(fieldKey) ? { minimum: "", maximum: "", basis: "" } : ""]];
  function replace(index, nextKey, nextValue) {
    const source = entries.length ? entries : rows;
    onChange(Object.fromEntries(source.map(([key, mapValue], rowIndex) => rowIndex === index ? [nextKey, nextValue] : [key, mapValue]).filter(([key]) => key)));
  }
  return (
    <Card title={displayLabel(fieldKey)} actions={<Button onClick={() => onChange({ ...value, [`new_${entries.length + 1}`]: rangeMapFields.has(fieldKey) ? { minimum: "", maximum: "", basis: "" } : "" })}>添加一项</Button>}>
      <div className="grid gap-3">
        {rows.map(([key, mapValue], index) => (
          <div key={`${key}:${index}`} className="grid gap-2 rounded-lg border border-slate-200 p-3 md:grid-cols-[1fr_2fr_auto]">
            <Input aria-label={`${displayLabel(fieldKey)}名称 ${index + 1}`} value={key} placeholder="名称" onChange={event => replace(index, event.target.value, mapValue)} />
            {rangeMapFields.has(fieldKey) ? (
              <div className="grid gap-2 sm:grid-cols-3">
                <Input type="number" step="any" value={mapValue.minimum ?? ""} placeholder="下限" onChange={event => replace(index, key, { ...mapValue, minimum: event.target.value === "" ? null : Number(event.target.value) })} />
                <Input type="number" step="any" value={mapValue.maximum ?? ""} placeholder="上限" onChange={event => replace(index, key, { ...mapValue, maximum: event.target.value === "" ? null : Number(event.target.value) })} />
                <Input value={mapValue.basis || ""} placeholder="范围依据" onChange={event => replace(index, key, { ...mapValue, basis: event.target.value })} />
              </div>
            ) : (
              <Input
                type={numericMapFields.has(fieldKey) ? "number" : "text"}
                step={numericMapFields.has(fieldKey) ? "any" : undefined}
                value={mapValue}
                placeholder="内容"
                onChange={event => replace(index, key, numericMapFields.has(fieldKey) ? Number(event.target.value) : event.target.value)}
              />
            )}
            {entries.length > 0 && <Button variant="ghost" className="text-rose-700" onClick={() => onChange(Object.fromEntries(entries.filter((_entry, rowIndex) => rowIndex !== index)))}>移除</Button>}
          </div>
        ))}
      </div>
    </Card>
  );
}

function NestedObject({ fieldKey, value, onChange }) {
  return (
    <Card title={displayLabel(fieldKey)}>
      <div className="grid gap-4 sm:grid-cols-2">
        {Object.entries(value).map(([key, nestedValue]) => <ScalarField key={key} fieldKey={key} value={nestedValue} onChange={nextValue => onChange({ ...value, [key]: nextValue })} />)}
      </div>
    </Card>
  );
}

export function BusinessObjectEditor({ value, onChange, error }) {
  return (
    <div className="grid gap-5">
      {error && <Alert tone="danger">{error}</Alert>}
      <div className="grid gap-4 sm:grid-cols-2">
        {Object.entries(value).map(([key, fieldValue]) => {
          if (Array.isArray(fieldValue)) {
            const schema = arraySchemas[key];
            return <div key={key} className="sm:col-span-2">{schema
              ? <ObjectList fieldKey={key} value={fieldValue} onChange={nextValue => updateObject(value, onChange, key, nextValue)} schema={schema} />
              : <PrimitiveList fieldKey={key} value={fieldValue} onChange={nextValue => updateObject(value, onChange, key, nextValue)} />}</div>;
          }
          if (fieldValue && typeof fieldValue === "object") {
            const isNested = key === "output" || key === "valueEstimate";
            return <div key={key} className="sm:col-span-2">{isNested
              ? <NestedObject fieldKey={key} value={fieldValue} onChange={nextValue => updateObject(value, onChange, key, nextValue)} />
              : <MapFields fieldKey={key} value={fieldValue} onChange={nextValue => updateObject(value, onChange, key, nextValue)} />}</div>;
          }
          return <ScalarField key={key} fieldKey={key} value={fieldValue} onChange={nextValue => updateObject(value, onChange, key, nextValue)} />;
        })}
      </div>
    </div>
  );
}
