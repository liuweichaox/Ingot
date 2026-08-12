import { useState } from "react";
import { Link } from "react-router";
import { deleteJson, postJson } from "../api/http";
import { createRegistryBusinessForm, RegistryBusinessEditor, registryBusinessPayload, registryBusinessValidation } from "../components/RegistryBusinessEditor";
import { extractRows, useApi } from "../hooks/useApi";
import { Alert, Button, Card, DataTable, Drawer, Field, Input, Page, Select, StatusBadge, Textarea, WorkflowGuide, notify, useConfirmDialog } from "../ui/components";
import { formatTime, emptyInspectionCharacteristic, inspectionDefinitionForm, inspectionDefinitionPayload, inspectionDefinitionValidation, inspectionInputTypes, LoadingCard } from "./shared";

const configurationJourney = [
  {
    number: "1", title: "定义数据标准", description: "先说明设备数据代表什么，再维护允许使用的工艺参数版本。",
    links: [["/configuration/process-data-models", "工艺数据模型"], ["/configuration/process-specifications", "工艺规范"]],
  },
  {
    number: "2", title: "连接现场数据", description: "登记现场节点，把 PLC、仪器或系统点位映射到标准数据项。",
    links: [["/edges", "现场节点"], ["/configuration/ingestion-tasks", "设备接入"]],
  },
  {
    number: "3", title: "定义判断规则", description: "决定哪些运行可以比较、质量如何判定，以及缺什么数据时应拒绝分析。",
    links: [["/configuration/process-analysis-plans", "运行分析方案"], ["/configuration/inspection-definitions", "检测定义"], ["/configuration/quality-plans", "质量方案"]],
  },
  {
    number: "4", title: "建立工装结构", description: "需要区分工装差异时，再定义组件、装配模板和实际工装总成。",
    links: [["/configuration/component-types", "组件分类"], ["/configuration/tooling-assemblies", "工装总成"]],
  },
  {
    number: "5", title: "组合并发布", description: "最后把已准备好的数据、接入、分析、质量和上下文策略锁定为可追溯版本。",
    links: [["/configuration/scenario-packages", "工艺配置方案"]],
  },
];

export function ConfigurationHubPage() {
  const modelResponse = useApi("/api/v1/process-data-models");
  const specificationResponse = useApi("/api/v1/process-specifications");
  const ingestionResponse = useApi("/api/v1/ingestion-tasks");
  const analysisResponse = useApi("/api/v1/process-analysis-plans");
  const definitionResponse = useApi("/api/v1/inspection-definitions");
  const qualityResponse = useApi("/api/v1/inspection-plans");
  const scenarioResponse = useApi("/api/v1/scenario-packages");
  const readiness = [
    ["数据标准", extractRows(modelResponse.data).some(item => item.status === "published") && extractRows(specificationResponse.data).some(item => item.status === "published"), "数据模型和工艺规范已发布", "发布数据模型和工艺规范"],
    ["现场接入", extractRows(ingestionResponse.data).some(item => item.status === "published"), "设备接入任务已发布", "发布至少一个设备接入任务"],
    ["分析规则", extractRows(analysisResponse.data).some(item => item.status === "published"), "运行分析方案已发布", "发布运行分析方案"],
    ["质量规则", extractRows(definitionResponse.data).length > 0 && extractRows(qualityResponse.data).some(item => item.status === "published"), "检测定义和质量方案已就绪", "建立检测定义并发布质量方案"],
    ["组合发布", extractRows(scenarioResponse.data).some(item => item.status === "published"), "工艺配置方案已发布", "发布工艺配置方案"],
  ];
  const readinessLoading = [modelResponse, specificationResponse, ingestionResponse, analysisResponse, definitionResponse, qualityResponse, scenarioResponse].some(item => item.loading && !item.data);
  const readinessError = [modelResponse, specificationResponse, ingestionResponse, analysisResponse, definitionResponse, qualityResponse, scenarioResponse].find(item => item.error)?.error;
  return (
    <Page title="配置总览" description="查看数据、接入、分析、质量和工装的准备状态。">
      <Card title="当前准备度" description="汇总当前可用于生产运行和分析的配置。">
        {readinessError && <Alert tone="warning">部分准备度暂时无法读取：{readinessError}</Alert>}
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
          {readiness.map(([title, ready, readyHint, pendingHint]) => (
            <div key={title} className={`rounded-xl border p-4 ${ready ? "border-emerald-200 bg-emerald-50" : "border-amber-200 bg-amber-50"}`}>
              <p className="flex items-center justify-between gap-2 font-semibold text-slate-950"><span>{title}</span><span className={`text-xs ${ready ? "text-emerald-700" : "text-amber-700"}`}>{readinessLoading ? "检查中" : ready ? "已准备" : "待完成"}</span></p>
              <p className="mt-2 text-xs leading-5 text-slate-600">{ready ? readyHint : pendingHint}</p>
            </div>
          ))}
        </div>
      </Card>
      <Card title="配置路径" description="按业务依赖完成新工艺配置。">
        <ol className="grid gap-3 md:grid-cols-2 2xl:grid-cols-5">
          {configurationJourney.map(step => (
            <li key={step.number} className="flex flex-col rounded-xl border border-slate-200 bg-white p-4">
              <span className="grid size-7 place-items-center rounded-full bg-blue-600 text-sm font-semibold text-white">{step.number}</span>
              <h3 className="mt-3 font-semibold text-slate-950">{step.title}</h3>
              <p className="mt-1 flex-1 text-sm leading-6 text-slate-600">{step.description}</p>
              <div className="mt-4 flex flex-wrap gap-2">
                {step.links.map(([to, label]) => <Link key={to} to={to} className="text-sm font-medium text-blue-700 hover:text-blue-900">{label} →</Link>)}
              </div>
            </li>
          ))}
        </ol>
      </Card>
      <Card title="运行数据来源" description="确认分析所需身份、生产、工装和覆盖率数据。">
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          {[
            ["设备与运行身份", "由设备事件、现场节点和设备接入映射提供。", "/configuration/ingestion-tasks", "检查设备接入"],
            ["产品、工艺、材料与批次", "由生产准备或 MES 写入不可变生产上下文。", "/production/changeover", "检查生产上下文"],
            ["实际装机工装", "由工装装卸记录在运行开始时绑定。", "/production/tooling-installations", "检查工装装卸"],
            ["字段覆盖率", "由历史已完成运行计算；覆盖不足时可禁止分析或建模。", "/data-quality", "检查数据可信度"],
          ].map(([title, description, to, action]) => (
            <div key={title} className="rounded-xl border border-slate-200 bg-slate-50 p-4">
              <h3 className="font-semibold text-slate-900">{title}</h3>
              <p className="mt-1 min-h-12 text-sm leading-6 text-slate-600">{description}</p>
              <Link to={to} className="mt-3 inline-flex text-sm font-medium text-blue-700 hover:text-blue-900">{action} →</Link>
            </div>
          ))}
        </div>
      </Card>
    </Page>
  );
}

const registryPages = {
  scenarios: {
    kind: "scenarioPackage",
    title: "工艺配置方案", description: "版本化组合工艺数据、设备接入、运行分析、质量规则、上下文策略和安全约束。", endpoint: "/api/v1/scenario-packages", key: "packageId",
    columns: [["packageId", "场景"], ["version", "版本"], ["name", "名称"], ["status", "状态"], ["updatedAt", "更新时间"]],
    createLabel: "创建工艺配置方案",
    template: { packageId: "", version: 1, name: "", description: "", status: "draft", dataModelId: "", dataModelVersion: 1, analysisPlanId: "", analysisPlanVersion: 1, ingestionTasks: [], qualityPlan: null, contextFields: [], constraints: [], knowledgeAssets: [], terminology: {}, updatedAt: "" },
    deleteUrl: value => `/api/v1/scenario-packages/${encodeURIComponent(value.packageId)}/${value.version}`,
  },
  processModels: {
    kind: "processModel",
    title: "工艺数据模型", description: "定义工艺变量、阶段号和控制参数结构，不包含 PLC 地址和采集频率。", endpoint: "/api/v1/process-data-models", key: "modelId",
    columns: [["modelId", "模型"], ["version", "版本"], ["name", "名称"], ["status", "状态"], ["updatedAt", "更新时间"]],
    createLabel: "创建工艺数据模型",
    template: { modelId: "", version: 1, name: "", description: "", status: "draft", acquisition: { dataItems: [] }, controlParameters: [], updatedAt: "" },
    deleteUrl: value => `/api/v1/process-data-models/${encodeURIComponent(value.modelId)}/${value.version}`,
  },
  processSpecifications: {
    kind: "processSpecificationVersion",
    title: "工艺规范", description: "维护引用工艺数据模型的完整参数版本。", endpoint: "/api/v1/process-specifications", key: "processSpecificationId",
    columns: [["processSpecificationId", "工艺规范"], ["version", "版本"], ["name", "名称"], ["status", "状态"], ["updatedAt", "更新时间"]],
    createLabel: "创建工艺规范",
    template: { processSpecificationId: "", version: 1, name: "", basedOnVersion: null, dataModelId: "", dataModelVersion: 1, status: "draft", contextSelector: {}, values: [], updatedAt: "" },
    deleteUrl: value => `/api/v1/process-specifications/${encodeURIComponent(value.processSpecificationId)}/${value.version}`,
  },
  plans: {
    kind: "analysisPlan",
    title: "运行分析方案", description: "版本化定义同类比较条件、阶段对齐、质量分组和分析数据项。", endpoint: "/api/v1/process-analysis-plans", key: "planId",
    columns: [["planId", "模型"], ["version", "版本"], ["name", "名称"], ["status", "状态"], ["updatedAt", "更新时间"]],
    createLabel: "创建运行分析方案",
    template: { planId: "", version: 1, name: "", description: "", status: "draft", dataModelId: "", dataModelVersion: 1, analysisScope: "production-execution", alignmentMode: "stage-relative", cohortDimension: "", comparisonKeys: ["product_family_code"], contextSelector: {}, signals: [], updatedAt: "" },
    deleteUrl: value => `/api/v1/process-analysis-plans/${encodeURIComponent(value.planId)}/${value.version}`,
  },
  definitions: {
    kind: "inspectionDefinition",
    title: "检测定义", description: "定义检测项目、录入方式、单位和判定范围。", endpoint: "/api/v1/inspection-definitions", key: "code",
    createLabel: "创建检测定义",
    columns: [["code", "代码"], ["version", "版本"], ["name", "名称"], ["characteristics", "录入类型"], ["updatedAt", "更新时间"]],
    render: { characteristics: inspectionInputTypes },
    template: { code: "", version: 1, name: "", description: "", characteristics: [] },
    deleteUrl: value => `/api/v1/inspection-definitions/${encodeURIComponent(value.code)}/${value.version}`,
  },
  plansQuality: {
    kind: "qualityPlan",
    title: "质量方案", description: "将检测定义组成适用于产品的版本化质量方案。", endpoint: "/api/v1/inspection-plans", key: "planId",
    columns: [["planId", "方案"], ["version", "版本"], ["name", "名称"], ["status", "状态"], ["updatedAt", "更新时间"]],
    createLabel: "创建质量方案",
    template: { planId: "", version: 1, name: "", description: "", status: "draft", priority: 0, effectiveFrom: null, effectiveTo: null, scope: {}, items: [], updatedAt: "" },
    deleteUrl: value => `/api/v1/inspection-plans/${encodeURIComponent(value.planId)}/${value.version}`,
  },
};

function RegistryPage({ definition, canWrite = true }) {
  const { data, loading, error, reload } = useApi(definition.endpoint);
  const rows = extractRows(data);
  const [open, setOpen] = useState(false);
  const [mode, setMode] = useState("create");
  const [inspectionForm, setInspectionForm] = useState(() => inspectionDefinitionForm());
  const [businessForm, setBusinessForm] = useState(() => createRegistryBusinessForm(definition.kind));
  const [editorError, setEditorError] = useState("");
  const [saving, setSaving] = useState(false);
  const { confirm, confirmationDialog } = useConfirmDialog();
  const isInspectionDefinition = definition.kind === "inspectionDefinition";
  const hasBusinessEditor = Boolean(definition.kind) && !isInspectionDefinition;
  const inspectionValidation = isInspectionDefinition ? inspectionDefinitionValidation(inspectionForm) : "";
  const businessValidation = hasBusinessEditor ? registryBusinessValidation(definition.kind, businessForm) : "";
  const editorValidation = inspectionValidation || businessValidation;

  function openCreate() {
    setMode("create");
    if (isInspectionDefinition) {
      setInspectionForm(inspectionDefinitionForm());
    } else {
      setBusinessForm(createRegistryBusinessForm(definition.kind));
    }
    setEditorError("");
    setOpen(true);
  }
  function openMaintain(row) {
    setMode("maintain");
    if (isInspectionDefinition) {
      setInspectionForm(inspectionDefinitionForm(row));
    } else {
      setBusinessForm(createRegistryBusinessForm(definition.kind, row));
    }
    setEditorError("");
    setOpen(true);
  }

  function openNewVersion(row) {
    setMode("version");
    if (isInspectionDefinition) {
      setInspectionForm(inspectionDefinitionForm(row, Number(row.version || 0) + 1));
    } else {
      setBusinessForm(createRegistryBusinessForm(definition.kind, row, Number(row.version || 0) + 1));
    }
    setEditorError("");
    setOpen(true);
  }

  async function save() {
    setSaving(true);
    setEditorError("");
    try {
      const payload = isInspectionDefinition
        ? inspectionDefinitionPayload(inspectionForm)
        : registryBusinessPayload(definition.kind, businessForm);
      if (payload.updatedAt !== undefined) payload.updatedAt = new Date().toISOString();
      await postJson(definition.endpoint, payload);
      setOpen(false);
      await reload();
      notify(`${definition.title}已保存。`);
    } catch (saveError) {
      setEditorError(saveError.message);
    } finally {
      setSaving(false);
    }
  }

  async function retire(row) {
    if (!await confirm({
      title: `停用${definition.title}`,
      description: "该版本将不再用于新的业务记录，既有历史引用仍会保留。",
      confirmLabel: "确认停用",
      tone: "danger",
    })) return;
    try {
      await postJson(definition.endpoint, {
        ...row,
        status: "retired",
        effectiveTo: definition.kind === "qualityPlan" ? new Date().toISOString() : row.effectiveTo,
        updatedAt: row.updatedAt !== undefined ? new Date().toISOString() : undefined,
      });
      await reload();
      notify(`${definition.title}已停用，历史版本仍会保留。`);
    } catch (requestError) {
      setEditorError(requestError.message);
    }
  }

  async function remove(row) {
    if (!await confirm({
      title: `${isInspectionDefinition ? "删除未引用版本" : "删除草稿"} ${row[definition.key]} v${row.version ?? 1}`,
      description: isInspectionDefinition
        ? "仅未被质量方案引用的检测定义版本可以删除；若已有引用，系统会拒绝并保留数据。"
        : "草稿删除后无法恢复；已发布版本不会在这里被删除。",
      confirmLabel: "确认删除",
      tone: "danger",
    })) return;
    try {
      await deleteJson(definition.deleteUrl(row));
      await reload();
      notify(isInspectionDefinition ? "未引用的检测定义版本已删除。" : `${definition.title}草稿已删除。`);
    } catch (requestError) {
      setEditorError(requestError.message);
    }
  }

  const columns = [
    ...definition.columns.map(([key, label]) => ({
      key,
      label,
      render: definition.render?.[key] || (key === "status" ? value => <StatusBadge value={value} /> : key.endsWith("At") ? formatTime : undefined),
    })),
    {
      key: "_actions",
      label: "操作",
      render: (_value, row) => (
        <div className="flex min-w-max flex-wrap gap-1" onClick={event => event.stopPropagation()}>
          <Button variant="ghost" className="px-2" onClick={() => openMaintain(row)}>
            {!canWrite || isInspectionDefinition || (hasBusinessEditor && row.status !== "draft") ? "查看" : "维护"}
          </Button>
          {canWrite && <Button variant="ghost" className="px-2" onClick={() => openNewVersion(row)}>沿用为新版本</Button>}
          {canWrite && !isInspectionDefinition && row.status === "published" && <Button variant="ghost" className="px-2 text-amber-700" onClick={() => retire(row)}>停用</Button>}
          {canWrite && (isInspectionDefinition || row.status === "draft") && <Button variant="ghost" className="px-2 text-rose-700" onClick={() => remove(row)}>{isInspectionDefinition ? "删除未引用版本" : "删除草稿"}</Button>}
        </div>
      ),
    },
  ];
  const businessReadOnly = hasBusinessEditor && mode === "maintain" && businessForm.status !== "draft";
  const editorReadOnly = !canWrite || (mode === "maintain" && (isInspectionDefinition || businessReadOnly));

  return (
    <Page
      title={definition.title}
      description={definition.description}
      actions={canWrite ? <Button variant="primary" onClick={openCreate}>{definition.createLabel || "创建新版本"}</Button> : undefined}
    >
      {definition.kind === "inspectionDefinition" && (
        <WorkflowGuide
          title="先定义检测内容，再组成质量方案"
          steps={[
            { title: "创建检测定义", description: "设置要填写的检测项、单位、上下限或选项。", state: rows.length ? "done" : "current" },
            { title: "加入质量方案", description: "决定哪些产品需要使用这些检测项目。", state: rows.length ? "current" : "upcoming" },
            { title: "按任务录入", description: "生产运行完成后，平台自动生成质量待办。", state: "upcoming" },
          ]}
        />
      )}
      {definition.kind === "qualityPlan" && (
        <WorkflowGuide
          title="质量方案决定什么时候检测什么"
          steps={[
            { title: "准备检测定义", description: "先确认需要的检测项目已经建立。", state: rows.length ? "done" : "current" },
            { title: "配置产品适用范围", description: "选择检测定义并设置原图、复核等要求。", state: rows.length ? "done" : "current" },
            { title: "发布后自动生成任务", description: "新生产运行会按适用范围进入质量队列。", state: rows.some(row => row.status === "published") ? "done" : "upcoming" },
          ]}
        />
      )}
      {(error || (!open && editorError)) && <Alert tone="danger">{error || editorError}</Alert>}
      {loading && !data ? <LoadingCard /> : (
        <Card title={`${definition.title}列表`} description={`共 ${data?.total ?? rows.length} 条记录`}>
          <DataTable
            rows={rows}
            keyField={definition.key}
            getRowKey={row => `${row[definition.key]}:${row.version ?? 1}`}
            columns={columns}
          />
        </Card>
      )}
      <Drawer
        open={open}
        onClose={() => setOpen(false)}
        closeOnBackdrop={false}
        title={mode === "create"
          ? `创建${definition.title}`
          : mode === "version" ? "沿用为新版本"
            : editorReadOnly ? `查看${definition.title}` : `维护${definition.title}`}
        description={isInspectionDefinition
          ? mode === "maintain" ? "查看该版本的基本信息和检测特性。" : "填写基本信息并配置一个或多个检测特性。"
          : hasBusinessEditor
            ? editorReadOnly ? "查看该版本的业务配置。" : "按业务字段完成配置，保存前会检查必填项和引用。"
          : "编辑完整版本内容。保存前会由平台执行结构、引用与状态校验。"}
        footer={editorReadOnly
          ? <Button onClick={() => setOpen(false)}>关闭</Button>
          : <><Button onClick={() => setOpen(false)}>取消</Button><Button variant="primary" onClick={save} disabled={saving || Boolean(editorValidation)}>{saving ? "保存中" : "保存"}</Button></>}
        size="xl"
      >
        {editorError && <Alert tone="danger">{editorError}</Alert>}
        {isInspectionDefinition ? (
          <InspectionDefinitionEditor
            form={inspectionForm}
            onChange={setInspectionForm}
            readOnly={editorReadOnly}
            validation={inspectionValidation}
            lockIdentity={mode !== "create"}
          />
        ) : (
          <RegistryBusinessEditor
            kind={definition.kind}
            form={businessForm}
            onChange={setBusinessForm}
            readOnly={editorReadOnly}
            validation={businessValidation}
            lockIdentity={mode !== "create"}
          />
        )}
      </Drawer>
      {confirmationDialog}
    </Page>
  );
}

function InspectionDefinitionEditor({ form, onChange, readOnly, validation, lockIdentity }) {
  function update(field, value) {
    onChange({ ...form, [field]: value });
  }

  function updateCharacteristic(index, field, value) {
    onChange({
      ...form,
      characteristics: form.characteristics.map((characteristic, characteristicIndex) =>
        characteristicIndex === index ? { ...characteristic, [field]: value } : characteristic),
    });
  }

  function addCharacteristic() {
    onChange({ ...form, characteristics: [...form.characteristics, emptyInspectionCharacteristic()] });
  }

  function removeCharacteristic(index) {
    onChange({ ...form, characteristics: form.characteristics.filter((_item, characteristicIndex) => characteristicIndex !== index) });
  }

  return (
    <div className="grid gap-5">
      {!readOnly && validation && <Alert tone="warning">{validation}</Alert>}
      <div className="grid gap-4 md:grid-cols-2">
        <Field label="定义代码" hint="使用小写字母开头的点分格式，例如 hardness.final。">
          <Input required value={form.code} disabled={readOnly || lockIdentity} onChange={event => update("code", event.target.value)} placeholder="hardness.final" />
        </Field>
        <Field label="版本">
          <Input required type="number" min="1" step="1" value={form.version} disabled={readOnly || lockIdentity} onChange={event => update("version", event.target.value)} />
        </Field>
        <Field label="定义名称">
          <Input required value={form.name} disabled={readOnly} onChange={event => update("name", event.target.value)} placeholder="成品硬度检测" />
        </Field>
        <Field label="说明" className="md:col-span-2">
          <Textarea className="min-h-20" value={form.description} disabled={readOnly} onChange={event => update("description", event.target.value)} placeholder="说明检测场景和目的" />
        </Field>
      </div>

      <div className="flex items-center justify-between gap-3">
        <div>
          <h3 className="font-semibold text-slate-900">检测特性</h3>
          <p className="mt-1 text-sm text-slate-500">每个特性对应一次具体录入，例如硬度、外观结论或是否合格。</p>
        </div>
        {!readOnly && <Button onClick={addCharacteristic}>添加检测特性</Button>}
      </div>

      <div className="grid gap-4">
        {form.characteristics.map((characteristic, index) => (
          <Card
            key={index}
            title={`检测特性 ${index + 1}`}
            actions={!readOnly && form.characteristics.length > 1
              ? <Button variant="ghost" className="text-rose-700" onClick={() => removeCharacteristic(index)}>移除</Button>
              : undefined}
          >
            <div className="grid gap-4 md:grid-cols-2">
              <Field label="特性代码" hint="同一定义内不可重复。">
                <Input required value={characteristic.code} disabled={readOnly} onChange={event => updateCharacteristic(index, "code", event.target.value)} placeholder="hardness.hrc" />
              </Field>
              <Field label="特性名称">
                <Input required value={characteristic.name} disabled={readOnly} onChange={event => updateCharacteristic(index, "name", event.target.value)} placeholder="洛氏硬度" />
              </Field>
              <Field label="录入类型">
                <Select value={characteristic.inputType} disabled={readOnly} onChange={event => updateCharacteristic(index, "inputType", event.target.value)}>
                  <option value="numeric">数值</option>
                  <option value="text">文本</option>
                  <option value="select">选项</option>
                  <option value="boolean">是/否</option>
                </Select>
              </Field>
              {characteristic.inputType === "numeric" && (
                <Field label="单位">
                  <Input value={characteristic.unit} disabled={readOnly} onChange={event => updateCharacteristic(index, "unit", event.target.value)} placeholder="例如 HRC、mm、℃" />
                </Field>
              )}
              {characteristic.inputType === "numeric" && (
                <>
                  <Field label="下限" hint="不限制可留空。">
                    <Input type="number" step="any" value={characteristic.lowerLimit} disabled={readOnly} onChange={event => updateCharacteristic(index, "lowerLimit", event.target.value)} />
                  </Field>
                  <Field label="上限" hint="不限制可留空。">
                    <Input type="number" step="any" value={characteristic.upperLimit} disabled={readOnly} onChange={event => updateCharacteristic(index, "upperLimit", event.target.value)} />
                  </Field>
                </>
              )}
              {characteristic.inputType === "select" && (
                <Field label="可选值" hint="每行填写一个选项。" className="md:col-span-2">
                  <Textarea value={characteristic.allowedValuesText} disabled={readOnly} onChange={event => updateCharacteristic(index, "allowedValuesText", event.target.value)} placeholder={"合格\n不合格"} />
                </Field>
              )}
              <label className="flex items-center gap-2 text-sm font-medium text-slate-700 md:col-span-2">
                <input type="checkbox" checked={characteristic.required} disabled={readOnly} onChange={event => updateCharacteristic(index, "required", event.target.checked)} />
                必须录入
              </label>
            </div>
          </Card>
        ))}
      </div>
    </div>
  );
}

export const ProcessDataModelsPage = ({ canWrite = true }) => <RegistryPage definition={registryPages.processModels} canWrite={canWrite} />;
export const ScenarioPackagesPage = ({ canWrite = true }) => <RegistryPage definition={registryPages.scenarios} canWrite={canWrite} />;
export const ProcessSpecificationsPage = ({ canWrite = true }) => <RegistryPage definition={registryPages.processSpecifications} canWrite={canWrite} />;
export const ProcessAnalysisPlansPage = ({ canWrite = true }) => <RegistryPage definition={registryPages.plans} canWrite={canWrite} />;
export const InspectionDefinitionsPage = ({ canWrite = true }) => <RegistryPage definition={registryPages.definitions} canWrite={canWrite} />;
export const QualityPlansPage = ({ canWrite = true }) => <RegistryPage definition={registryPages.plansQuality} canWrite={canWrite} />;
// 设备接入已迁移到 src/acquisition/IngestionTaskPage.jsx：
// 协议差异由描述符注册表承载，配置页是独立的左右分栏页面而不是通用注册表抽屉。
