// 收集配方优化范围、目标、变量与安全边界，并复用现有工艺配置目录。
import { useEffect, useState } from "react";
import { getJson } from "../../api/http";
import { Alert, Button, Card, Drawer, Field, Input, Select, Textarea } from "../../ui/components";

export function CreateProjectDrawer({ open, saving, form, setForm, onClose, onSubmit }) {
  const [catalog, setCatalog] = useState({ executions: [], definitions: [], models: [], scenarios: [] });
  const [catalogLoading, setCatalogLoading] = useState(false);
  const [catalogError, setCatalogError] = useState("");

  useEffect(() => {
    if (!open) return;
    let mounted = true;
    setCatalogLoading(true);
    setCatalogError("");
    Promise.all([
      getJson("/api/v1/process-executions?status=completed&limit=200"),
      getJson("/api/v1/inspection-definitions"),
      getJson("/api/v1/process-data-models"),
      getJson("/api/v1/scenario-packages"),
    ]).then(([executions, definitions, models, scenarios]) => {
      if (!mounted) return;
      setCatalog({
        executions: executions?.data || [],
        definitions: definitions?.data || [],
        models: models?.data || [],
        scenarios: scenarios?.data || [],
      });
    }).catch(requestError => {
      if (!mounted) return;
      setCatalogError(requestError.message || "无法读取可选的工艺和质量定义。");
    }).finally(() => {
      if (mounted) setCatalogLoading(false);
    });
    return () => { mounted = false; };
  }, [open]);

  const field = (name, value) => event => setForm({ ...form, [name]: event.target[value || "value"] });
  const selectableModels = catalog.models.filter(item => item.status !== "retired");
  const selectableScenarios = catalog.scenarios.filter(item => item.status === "published");
  const selectedModel = selectableModels.find(item => `${item.modelId}:${item.version}` === form.dataModelKey);
  const numericObjectiveOptions = catalog.definitions.flatMap(definition =>
    (definition.characteristics || [])
      .filter(item => ["numeric", "number"].includes(String(item.inputType).toLowerCase()))
      .map(item => ({
        key: `${definition.code}:${definition.version}:${item.code}`,
        kind: "measurement",
        definition,
        characteristic: item,
      })),
  );
  const objectiveOptions = catalog.definitions.flatMap(definition => [
    {
      key: `${definition.code}:${definition.version}:$outcome`,
      kind: "outcome",
      definition,
      characteristic: null,
    },
    ...numericObjectiveOptions.filter(option =>
      option.definition.code === definition.code && option.definition.version === definition.version),
  ]);
  const selectedObjective = objectiveOptions.find(item => item.key === form.objectiveKey);

  function updateForm(values) {
    setForm(current => ({ ...current, ...values }));
  }

  function chooseReferenceProcessExecution(executionId) {
    const execution = catalog.executions.find(item => item.executionId === executionId);
    updateForm({
      referenceProcessExecutionId: executionId,
      productName: execution?.productCode || form.productName,
      materialName: execution?.materialLotRef || form.materialName,
      referenceContext: execution ? {
        ...(execution.equipmentId ? { equipment: execution.equipmentId } : {}),
        ...(execution.toolingAssemblyId ? { tooling: execution.toolingAssemblyId } : {}),
        ...(execution.processSpecificationId ? {
          "process-specification": execution.processSpecificationVersion
            ? `${execution.processSpecificationId}@${execution.processSpecificationVersion}`
            : execution.processSpecificationId,
        } : {}),
        ...(execution.siteId ? { site: execution.siteId } : {}),
      } : {},
    });
  }

  function chooseDataModel(key) {
    const model = selectableModels.find(item => `${item.modelId}:${item.version}` === key);
    updateForm({
      dataModelKey: key,
      scenarioPackageKey: catalog.scenarios.some(item => `${item.packageId}:${item.version}` === form.scenarioPackageKey && `${item.dataModelId}:${item.dataModelVersion}` === key) ? form.scenarioPackageKey : "",
      processName: model?.name || "",
      variableCode: "",
      variableName: "",
      variableUnit: "",
      variableDataSource: "",
    });
  }

  function chooseScenarioPackage(key) {
    const scenario = selectableScenarios.find(item => `${item.packageId}:${item.version}` === key);
    if (!scenario) {
      updateForm({ scenarioPackageKey: "" });
      return;
    }
    const modelKey = `${scenario.dataModelId}:${scenario.dataModelVersion}`;
    const model = selectableModels.find(item => `${item.modelId}:${item.version}` === modelKey);
    updateForm({
      scenarioPackageKey: key,
      dataModelKey: modelKey,
      processName: model?.name || scenario.name,
      variableCode: "",
      variableName: "",
      variableUnit: "",
      variableDataSource: "",
    });
  }

  function chooseObjective(key) {
    const option = objectiveOptions.find(item => item.key === key);
    if (option?.kind === "outcome") {
      updateForm({
        objectiveKey: key,
        objectiveCode: `${option.definition.code}-pass-rate`,
        objectiveName: `${option.definition.name}合格率`,
        objectiveUnit: "1",
        objectiveDataSource: `inspection-outcome:${option.definition.code}`,
        objectiveDirection: "maximize",
        objectiveTarget: "1",
      });
      return;
    }
    const characteristic = option?.characteristic;
    const target = form.objectiveTarget || (
      form.objectiveDirection === "maximize" ? characteristic?.lowerLimit : characteristic?.upperLimit
    );
    updateForm({
      objectiveKey: key,
      objectiveCode: characteristic?.code || "",
      objectiveName: characteristic?.name || "",
      objectiveUnit: characteristic?.unit || "",
      objectiveDataSource: characteristic ? `inspection:${characteristic.code}` : "",
      objectiveTarget: target ?? "",
    });
  }

  function chooseVariable(code) {
    const parameter = (selectedModel?.controlParameters || []).find(item => item.code === code);
    updateForm({
      variableCode: parameter?.code || "",
      variableName: parameter?.displayName || parameter?.code || "",
      variableUnit: parameter?.unit || "",
      variableDataSource: parameter ? `control-parameter:${parameter.code}` : "",
    });
  }

  function chooseConstraint(key) {
    const option = objectiveOptions.find(item => item.key === key);
    const characteristic = option?.characteristic;
    updateForm({
      outcomeConstraintKey: key,
      outcomeConstraintCode: characteristic ? `${characteristic.code}-safety` : "",
      outcomeConstraintName: characteristic?.name ? `${characteristic.name} 安全边界` : "",
      outcomeConstraintMetric: characteristic?.code || "",
      outcomeConstraintUnit: characteristic?.unit || "",
      outcomeConstraintLimit: characteristic?.upperLimit ?? "",
    });
  }

  const executionLabel = execution => [
    execution.executionId,
    execution.productFamilyCode || execution.productCode || "未标注产品",
    execution.equipmentId || "未标注设备",
    execution.completedAt ? new Date(execution.completedAt).toLocaleString("zh-CN") : "",
  ].filter(Boolean).join(" · ");

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title="创建配方优化任务"
      description="确定配方运行范围、质量目标和安全边界；后续证据直接来自真实生产运行。"
      size="xl"
      footer={<><Button disabled={saving} onClick={onClose}>取消</Button><Button variant="primary" disabled={saving} type="submit" form="research-project-form">{saving ? "正在创建…" : "创建项目"}</Button></>}
    >
      <form id="research-project-form" className="space-y-6" onSubmit={onSubmit}>
        {catalogError && <Alert tone="warning" title="部分选项暂不可用">{catalogError}</Alert>}
        {catalogLoading && <Alert tone="info">正在读取已完成运行、工艺配置、检测定义和工艺数据字典…</Alert>}
        <Card title="1. 优化范围" description="先确定要持续比较的工艺、产品和设备范围。">
          <div className="grid gap-4 md:grid-cols-2">
            <Field label="任务名称"><Input required value={form.name} onChange={field("name")} placeholder="光学模压配方优化" /></Field>
            <Field label="参考运行" hint="选择后自动带入产品范围；不影响后续用更多运行形成证据。"><Select value={form.referenceProcessExecutionId} onChange={event => chooseReferenceProcessExecution(event.target.value)}><option value="">暂不关联历史运行</option>{catalog.executions.map(execution => <option key={execution.executionId} value={execution.executionId}>{executionLabel(execution)}</option>)}</Select></Field>
            <Field label="工艺配置（推荐）" hint="只允许选择不可变的已发布版本；其中 required-for-analysis 字段会成为优化准入条件。"><Select value={form.scenarioPackageKey} onChange={event => chooseScenarioPackage(event.target.value)}><option value="">暂不使用工艺配置</option>{selectableScenarios.map(item => <option key={`${item.packageId}:${item.version}`} value={`${item.packageId}:${item.version}`}>{item.name} · v{item.version}</option>)}</Select></Field>
            <Field label="工艺数据字典" hint="决定可选的控制参数与实际数据来源。"><Select required value={form.dataModelKey} onChange={event => chooseDataModel(event.target.value)}><option value="">选择已配置的工艺数据字典</option>{selectableModels.map(model => <option key={`${model.modelId}:${model.version}`} value={`${model.modelId}:${model.version}`}>{model.name} · v{model.version}</option>)}</Select></Field>
            <Field label="目标产品" hint="来自参考运行；未关联时可补充产品编号。"><Input value={form.productName} onChange={field("productName")} placeholder="产品编号（可选）" /></Field>
            <Field label="材料"><Input value={form.materialName} onChange={field("materialName")} /></Field>
            <Field label="任务说明" className="md:col-span-2"><Textarea value={form.description} onChange={field("description")} rows={3} /></Field>
          </div>
        </Card>
        <Card title="2. 首要优化目标" description="选择要改善的质量指标及判定方向。">
          <div className="grid gap-4 md:grid-cols-2">
            <Field label="质量目标" hint="可直接优化正式检验合格率，也可选择检测数值；代码、单位和数据来源会自动带入。"><Select required value={form.objectiveKey} onChange={event => chooseObjective(event.target.value)}><option value="">选择正式质检结论或数值指标</option>{objectiveOptions.map(option => <option key={option.key} value={option.key}>{option.kind === "outcome" ? `${option.definition.name} · 合格率` : `${option.definition.name} · ${option.characteristic.name}${option.characteristic.unit ? ` (${option.characteristic.unit})` : ""}`}</option>)}</Select></Field>
            <Field label="数据来源"><Input readOnly value={form.objectiveDataSource} placeholder="选择质量指标后自动带入" className="bg-slate-50 text-slate-600" /></Field>
            <Field label="优化方向"><Select value={form.objectiveDirection} onChange={field("objectiveDirection")}><option value="minimize">越低越好</option><option value="maximize">越高越好</option><option value="target">接近目标</option><option value="range">保持范围</option></Select></Field>
            <Field label="指标单位"><Input readOnly required value={form.objectiveUnit} placeholder="自动带入" className="bg-slate-50 text-slate-600" /></Field>
            <Field label="目标值" hint="来自检测上下限的建议值，可按研发规格调整。"><Input required type="number" step="any" value={form.objectiveTarget} onChange={field("objectiveTarget")} /></Field>
            <Field label="目标权重"><Input required type="number" min="0.01" step="any" value={form.objectiveWeight} onChange={field("objectiveWeight")} /></Field>
          </div>
        </Card>
        <Card title="3. 首个可控变量" description="定义下一配方建议允许调整的参数范围。">
          <div className="grid gap-4 md:grid-cols-2">
            <Field label="控制参数" hint={selectedModel ? "从所选工艺数据字典中选择。" : "请先选择工艺数据字典。"}><Select required disabled={!selectedModel} value={form.variableCode} onChange={event => chooseVariable(event.target.value)}><option value="">选择控制参数</option>{(selectedModel?.controlParameters || []).map(parameter => <option key={parameter.code} value={parameter.code}>{parameter.displayName || parameter.code}{parameter.unit ? ` (${parameter.unit})` : ""}</option>)}</Select></Field>
            <Field label="实际数据来源"><Input readOnly value={form.variableDataSource} placeholder="选择控制参数后自动带入" className="bg-slate-50 text-slate-600" /></Field>
            <Field label="变量单位"><Input readOnly required value={form.variableUnit} placeholder="自动带入" className="bg-slate-50 text-slate-600" /></Field>
            <Field label="允许下限" hint="这是配方建议允许范围，请按设备/安全规范确认。"><Input required type="number" step="any" value={form.variableLower} onChange={field("variableLower")} /></Field>
            <Field label="允许上限" hint="这是配方建议允许范围，请按设备/安全规范确认。"><Input required type="number" step="any" value={form.variableUpper} onChange={field("variableUpper")} /></Field>
          </div>
        </Card>
        <Card title="4. 结果安全边界（可选）" description="例如裂纹率、破损率或粘模指标；优化器只推荐达到最低安全概率的工艺规范。">
          <div className="grid gap-4 md:grid-cols-2">
            <Field label="安全指标" hint="选择后自动带入检测特性、单位和建议安全限值。"><Select value={form.outcomeConstraintKey} onChange={event => chooseConstraint(event.target.value)}><option value="">不设置额外结果安全边界</option>{numericObjectiveOptions.filter(item => item.key !== selectedObjective?.key).map(option => <option key={option.key} value={option.key}>{option.definition.name} · {option.characteristic.name}</option>)}</Select></Field>
            <Field label="安全约束说明"><Input readOnly value={form.outcomeConstraintName} placeholder="选择安全指标后自动带入" className="bg-slate-50 text-slate-600" /></Field>
            <Field label="操作符"><Select value={form.outcomeConstraintOperator} onChange={field("outcomeConstraintOperator")}><option value="<=">不高于</option><option value=">=">不低于</option></Select></Field>
            <Field label="安全限值"><Input type="number" step="any" value={form.outcomeConstraintLimit} onChange={field("outcomeConstraintLimit")} /></Field>
            <Field label="单位"><Input readOnly value={form.outcomeConstraintUnit} placeholder="自动带入" className="bg-slate-50 text-slate-600" /></Field>
            <Field label="最低安全概率"><Input type="number" min="0.01" max="1" step="0.01" value={form.outcomeConstraintProbability} onChange={field("outcomeConstraintProbability")} /></Field>
          </div>
        </Card>
      </form>
    </Drawer>
  );
}
