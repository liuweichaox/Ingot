// 提供版本化业务配置注册表及显式创建、发布和退役操作。
import { useState } from "react";
import { Link } from "react-router";
import { deleteJson, postJson } from "../api/http";
import { createRegistryBusinessForm, RegistryBusinessEditor, registryBusinessPayload, registryBusinessValidation } from "../components/RegistryBusinessEditor";
import { extractRows, useApi } from "../hooks/useApi";
import { Alert, Button, Card, DataTable, Drawer, EmptyState, Field, Input, Page, RequestError, Select, StatusBadge, Textarea, notify, useConfirmDialog } from "../ui/components";
import { formatTime, emptyInspectionCharacteristic, inspectionDefinitionForm, inspectionDefinitionPayload, inspectionDefinitionValidation, inspectionInputTypes, LoadingCard } from "./shared";

export function ConfigurationHubPage() {
  const modelResponse = useApi("/api/v1/process-data-models");
  const specificationResponse = useApi("/api/v1/process-specifications");
  const ingestionResponse = useApi("/api/v1/ingestion-tasks");
  const analysisResponse = useApi("/api/v1/process-analysis-plans");
  const definitionResponse = useApi("/api/v1/inspection-definitions");
  const qualityResponse = useApi("/api/v1/inspection-plans");
  const readiness = [
    { title: "数据标准", ready: extractRows(modelResponse.data).some(item => item.status === "published") && extractRows(specificationResponse.data).some(item => item.status === "published"), readyHint: "数据字典和工艺规范已发布", pendingHint: "发布数据字典和工艺规范", to: "/configuration/process-data-models", action: "检查数据标准", responses: [modelResponse, specificationResponse] },
    { title: "现场接入", ready: extractRows(ingestionResponse.data).some(item => item.status === "published"), readyHint: "数据源配置已发布", pendingHint: "发布至少一个数据源配置", to: "/configuration/ingestion-tasks", action: "配置数据来源", responses: [ingestionResponse] },
    { title: "分析规则", ready: extractRows(analysisResponse.data).some(item => item.status === "published"), readyHint: "运行分析规则已发布", pendingHint: "发布运行分析规则", to: "/configuration/process-analysis-plans", action: "配置分析规则", responses: [analysisResponse] },
    { title: "质量规则", ready: extractRows(definitionResponse.data).length > 0 && extractRows(qualityResponse.data).some(item => item.status === "published"), readyHint: "检测定义和质量方案已就绪", pendingHint: "建立检测定义并发布质量方案", to: "/configuration/quality-plans", action: "配置质量规则", responses: [definitionResponse, qualityResponse] },
  ].map(item => ({
    ...item,
    loading: item.responses.some(response => response.loading && !response.data),
    error: item.responses.find(response => response.error)?.error || "",
  }));
  const readinessLoading = readiness.some(item => item.loading);
  const readinessError = readiness.find(item => item.error)?.error;
  const readyCount = readiness.filter(item => item.ready).length;
  const nextReadiness = readiness.find(item => !item.ready);
  return (
    <Page
      title="配置总览"
      actions={!readinessLoading && !readinessError && (
        readyCount === readiness.length
          ? <Link className="inline-flex min-h-10 items-center rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white hover:bg-blue-700" to="/production/changeover">进入生产切换</Link>
          : <Link className="inline-flex min-h-10 items-center rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white hover:bg-blue-700" to={nextReadiness?.to || "/configuration"}>继续：{nextReadiness?.action || "完善配置"}</Link>
      )}
    >
      <Card
        title="配置准备度"
        actions={!readinessLoading && <span className={`text-sm font-semibold ${readinessError ? "text-rose-700" : readyCount === readiness.length ? "text-emerald-700" : "text-amber-700"}`}>{readinessError ? "检查未完成" : readyCount === readiness.length ? "生产配置已就绪" : `还需完成 ${readiness.length - readyCount} 项`}</span>}
      >
        {readinessError && <Alert tone="warning">部分准备度暂时无法读取：{readinessError}</Alert>}
        <div className="mb-4 h-2 overflow-hidden rounded-full bg-slate-100" role="progressbar" aria-label="配置准备进度" aria-valuemin="0" aria-valuemax={readiness.length} aria-valuenow={readyCount}>
          <div className={`h-full rounded-full transition-[width] ${readyCount === readiness.length ? "bg-emerald-600" : "bg-amber-500"}`} style={{ width: `${readinessLoading ? 0 : readyCount / readiness.length * 100}%` }} />
        </div>
        <DataTable
          rows={readiness.map((item, index) => ({ ...item, order: index + 1 }))}
          keyField="title"
          columns={[
            { key: "order", label: "序号", render: value => String(value).padStart(2, "0") },
            { key: "title", label: "模块" },
            {
              key: "ready",
              label: "状态",
              render: (_, item) => <StatusBadge
                value={item.loading ? "pending" : item.error ? "unavailable" : item.ready ? "ready" : "incomplete"}
                label={item.loading ? "检查中" : item.error ? "无法检查" : item.ready ? "已准备" : "待完成"}
              />,
            },
            { key: "pendingHint", label: "当前情况", render: (_, item) => item.error ? "状态接口暂时不可用" : item.ready ? item.readyHint : item.pendingHint },
            { key: "action", label: "操作", render: (_, item) => <Link to={item.to} className="font-medium text-blue-700 hover:text-blue-900">{item.action}</Link> },
          ]}
        />
      </Card>
      <details className="group rounded-lg border border-slate-200 bg-white">
        <summary className="flex cursor-pointer list-none items-center justify-between gap-4 px-5 py-4 marker:content-none">
          <p className="font-semibold text-slate-950">运行数据来源与追溯要求</p>
          <span className="text-sm font-medium text-blue-700 group-open:hidden">查看详情</span>
          <span className="hidden text-sm font-medium text-blue-700 group-open:inline">收起</span>
        </summary>
        <div className="grid border-t border-slate-200 md:grid-cols-2 xl:grid-cols-4 xl:divide-x xl:divide-slate-200">
          {[
            ["设备与运行身份", "由设备事件、现场节点和数据源配置映射提供。", "/configuration/ingestion-tasks", "检查数据源配置"],
            ["产品、工艺、材料与批次", "由生产准备或 MES 写入不可变生产上下文。", "/production/changeover", "检查生产上下文"],
            ["实际装机工装", "由工装装卸记录在运行开始时绑定。", "/production/tooling-installations", "检查工装装卸"],
            ["字段覆盖率", "由历史已完成运行计算；覆盖不足时可禁止分析或建模。", "/data-quality", "检查数据可信度"],
          ].map(([title, description, to, action]) => (
            <div key={title} className="border-b border-slate-200 p-4 md:[&:nth-last-child(-n+2)]:border-b-0 xl:border-b-0">
              <h3 className="font-semibold text-slate-900">{title}</h3>
              <p className="mt-1 min-h-12 text-sm leading-6 text-slate-600">{description}</p>
              <Link to={to} className="mt-3 inline-flex text-sm font-medium text-blue-700 hover:text-blue-900">{action} →</Link>
            </div>
          ))}
        </div>
      </details>
    </Page>
  );
}

const registryPages = {
  processModels: {
    kind: "processModel",
    title: "工艺数据字典", description: "定义工艺变量、阶段号和控制参数结构，不包含来源地址和采集频率。", endpoint: "/api/v1/process-data-models", key: "modelId",
    columns: [["modelId", "模型"], ["version", "版本"], ["name", "名称"], ["status", "状态"], ["updatedAt", "更新时间"]],
    createLabel: "创建工艺数据字典",
    template: { modelId: "", version: 1, name: "", description: "", status: "draft", acquisition: { dataItems: [] }, controlParameters: [], updatedAt: "" },
    deleteUrl: value => `/api/v1/process-data-models/${encodeURIComponent(value.modelId)}/${value.version}`,
  },
  processSpecifications: {
    kind: "processSpecificationVersion",
    title: "工艺规范", description: "维护引用工艺数据字典的完整参数版本。", endpoint: "/api/v1/process-specifications", key: "processSpecificationId",
    columns: [["processSpecificationId", "工艺规范"], ["version", "版本"], ["name", "名称"], ["status", "状态"], ["updatedAt", "更新时间"]],
    createLabel: "创建工艺规范",
    template: { processSpecificationId: "", version: 1, name: "", basedOnVersion: null, dataModelId: "", dataModelVersion: 1, status: "draft", contextSelector: {}, values: [], updatedAt: "" },
    deleteUrl: value => `/api/v1/process-specifications/${encodeURIComponent(value.processSpecificationId)}/${value.version}`,
  },
  plans: {
    kind: "analysisPlan",
    title: "运行分析规则", description: "版本化定义同类比较条件、阶段对齐、质量分组和分析数据项。", endpoint: "/api/v1/process-analysis-plans", key: "planId",
    columns: [["planId", "模型"], ["version", "版本"], ["name", "名称"], ["status", "状态"], ["updatedAt", "更新时间"]],
    createLabel: "创建运行分析规则",
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
  const isProcessSpecification = definition.kind === "processSpecificationVersion";
  const processModelsResponse = useApi(
    isProcessSpecification ? "/api/v1/process-data-models" : "",
    { enabled: isProcessSpecification },
  );
  const executionsResponse = useApi(
    isProcessSpecification ? "/api/v1/process-executions?status=completed&limit=200" : "",
    { enabled: isProcessSpecification },
  );
  const [open, setOpen] = useState(false);
  const [nextDraftOpen, setNextDraftOpen] = useState(false);
  const [nextDraftSource, setNextDraftSource] = useState(null);
  const [nextDraftForm, setNextDraftForm] = useState(createNextSpecificationDraftForm);
  const [mode, setMode] = useState("create");
  const [inspectionForm, setInspectionForm] = useState(() => inspectionDefinitionForm());
  const [businessForm, setBusinessForm] = useState(() => createRegistryBusinessForm(definition.kind));
  const [editorError, setEditorError] = useState("");
  const [showValidation, setShowValidation] = useState(false);
  const [saving, setSaving] = useState(false);
  const { confirm, confirmationDialog } = useConfirmDialog();
  const isInspectionDefinition = definition.kind === "inspectionDefinition";
  const hasBusinessEditor = Boolean(definition.kind) && !isInspectionDefinition;
  const inspectionValidation = isInspectionDefinition ? inspectionDefinitionValidation(inspectionForm) : "";
  const businessValidation = hasBusinessEditor ? registryBusinessValidation(definition.kind, businessForm) : "";
  const nextDraftValidation = validateNextSpecificationDraft(nextDraftForm);
  const editorValidation = inspectionValidation || businessValidation;

  function openCreate() {
    setMode("create");
    if (isInspectionDefinition) {
      setInspectionForm(inspectionDefinitionForm());
    } else {
      setBusinessForm(createRegistryBusinessForm(definition.kind));
    }
    setEditorError("");
    setShowValidation(false);
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
    setShowValidation(false);
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
    setShowValidation(false);
    setOpen(true);
  }

  function openNextDraft(row) {
    setNextDraftSource(row);
    setNextDraftForm(createNextSpecificationDraftForm());
    setEditorError("");
    setShowValidation(false);
    setNextDraftOpen(true);
  }

  async function save() {
    if (editorValidation) {
      setShowValidation(true);
      return;
    }
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

  async function createNextDraft() {
    if (nextDraftValidation || !nextDraftSource) {
      setShowValidation(true);
      return;
    }
    setSaving(true);
    setEditorError("");
    try {
      const result = await postJson(
        `/api/v1/process-specifications/${encodeURIComponent(nextDraftSource.processSpecificationId)}/${nextDraftSource.version}/drafts`,
        nextSpecificationDraftPayload(nextDraftForm),
      );
      const draft = result?.draft || result;
      setNextDraftOpen(false);
      await reload();
      notify(`已创建 ${draft.processSpecificationId} V${draft.version} 修订草稿。`);
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
            {!canWrite || isInspectionDefinition || (hasBusinessEditor && row.status !== "draft") ? "查看版本" : "编辑草稿"}
          </Button>
          {canWrite && isProcessSpecification && row.status === "published" && (
            <Button variant="ghost" className="px-2 text-trajectory-700" onClick={() => openNextDraft(row)}>创建修订草稿</Button>
          )}
          {canWrite && !isProcessSpecification && (isInspectionDefinition || row.status !== "draft") && (
            <Button variant="ghost" className="px-2" onClick={() => openNewVersion(row)}>创建修订版本</Button>
          )}
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
      actions={canWrite ? <Button variant="primary" onClick={openCreate}>{definition.createLabel || "创建新版本"}</Button> : undefined}
    >
      <RequestError error={error} onRetry={reload} />
      {!open && editorError && <Alert tone="danger">{editorError}</Alert>}
      {loading && !data ? <LoadingCard /> : (
        <Card title={`${definition.title}（${data?.total ?? rows.length}）`}>
          {rows.length ? <DataTable
            rows={rows}
            keyField={definition.key}
            getRowKey={row => `${row[definition.key]}:${row.version ?? 1}`}
            columns={columns}
          /> : <EmptyState
            title={`还没有${definition.title}`}
            description={canWrite ? `创建第一个${definition.title}后，即可在后续配置和生产流程中引用。` : "当前岗位只有查看权限，请联系工艺工程师或平台管理员完成配置。"}
            actions={canWrite && <Button variant="primary" onClick={openCreate}>{definition.createLabel}</Button>}
          />}
        </Card>
      )}
      <Drawer
        open={open}
        onClose={() => setOpen(false)}
        closeOnBackdrop={false}
        title={mode === "create"
          ? `创建${definition.title}`
          : mode === "version" ? "创建修订版本"
            : editorReadOnly ? `查看${definition.title}` : `维护${definition.title}`}
        description={isInspectionDefinition
          ? mode === "maintain" ? "检测定义版本一经保存即不可覆盖；需要变更时创建修订版本。" : "填写基本信息并配置一个或多个检测特性。"
          : hasBusinessEditor
            ? editorReadOnly ? "查看该版本的业务配置。" : "按业务字段完成配置，保存前会检查必填项和引用。"
          : "编辑完整版本内容。保存前会由平台执行结构、引用与状态校验。"}
        footer={editorReadOnly
          ? <Button onClick={() => setOpen(false)}>关闭</Button>
          : <><Button onClick={() => setOpen(false)}>取消</Button><Button variant="primary" onClick={save} disabled={saving}>{saving ? "保存中" : "保存"}</Button></>}
        size="xl"
      >
        {editorError && <Alert tone="danger">{editorError}</Alert>}
        {isInspectionDefinition ? (
          <InspectionDefinitionEditor
            form={inspectionForm}
            onChange={setInspectionForm}
            readOnly={editorReadOnly}
            validation={showValidation ? inspectionValidation : ""}
            lockIdentity={mode !== "create"}
          />
        ) : (
          <RegistryBusinessEditor
            kind={definition.kind}
            form={businessForm}
            onChange={setBusinessForm}
            readOnly={editorReadOnly}
            validation={showValidation ? businessValidation : ""}
            lockIdentity={mode !== "create"}
          />
        )}
      </Drawer>
      <Drawer
        open={nextDraftOpen}
        onClose={() => setNextDraftOpen(false)}
        closeOnBackdrop={false}
        title="修订工艺规范"
        description="以已发布规范为唯一基准，引用实际运行证据后只提交发生变化的控制参数。"
        footer={<><Button onClick={() => setNextDraftOpen(false)}>取消</Button><Button variant="primary" onClick={createNextDraft} disabled={saving || Boolean(nextDraftValidation)}>{saving ? "创建中" : "创建修订草稿"}</Button></>}
        size="xl"
      >
        {editorError && <Alert tone="danger">{editorError}</Alert>}
        {nextDraftSource && <NextSpecificationDraftEditor
          source={nextDraftSource}
          form={nextDraftForm}
          onChange={setNextDraftForm}
          models={extractRows(processModelsResponse.data)}
          modelError={processModelsResponse.error}
          executions={extractRows(executionsResponse.data)}
          executionsLoading={executionsResponse.loading}
          validation={showValidation ? nextDraftValidation : ""}
        />}
      </Drawer>
      {confirmationDialog}
    </Page>
  );
}

function createNextSpecificationDraftForm() {
  return { changeReason: "", mechanismNotes: "", evidenceReferences: [], parameterOverrides: [] };
}

function validateNextSpecificationDraft(form) {
  if (!form.changeReason.trim()) return "请说明本次修订理由。";
  if (form.evidenceReferences.length === 0) return "创建修订草稿前必须引用至少一条实际运行证据。";
  if (form.parameterOverrides.some(item => !item.code || item.value === "")) return "参数修订值不能为空。";
  return "";
}

function nextSpecificationDraftPayload(form) {
  return {
    changeReason: form.changeReason.trim(),
    mechanismNotes: form.mechanismNotes.trim() || null,
    evidenceReferences: form.evidenceReferences,
    parameterOverrides: form.parameterOverrides.map(item => ({
      code: item.code,
      value: item.dataType === "boolean" ? item.value === "true"
        : ["double", "integer"].includes(item.dataType) ? Number(item.value)
          : item.value,
    })),
  };
}

function NextSpecificationDraftEditor({ source, form, onChange, models, modelError, executions, executionsLoading, validation }) {
  const modelId = source.dataModelId;
  const modelVersion = Number(source.dataModelVersion);
  const model = models.find(item => item.modelId === modelId && Number(item.version) === modelVersion);
  const parameters = model?.controlParameters || [];
  const sourceValues = new Map((source.values || []).map(item => [item.code, item.value]));
  const parameterEntries = [...new Map([
    ...[...sourceValues.keys()].map(code => [code, { code, displayName: code, dataType: typeof sourceValues.get(code) === "number" ? "double" : "string" }]),
    ...parameters.map(parameter => [parameter.code, parameter]),
  ]).values()];
  const matchingExecutions = executions.filter(item =>
    item.processSpecificationId === source.processSpecificationId &&
    String(item.processSpecificationVersion) === String(source.version));
  const passCount = matchingExecutions.filter(item => String(item.qualityStatus).toUpperCase() === "COMPLETE").length;
  const failCount = matchingExecutions.filter(item => String(item.qualityStatus).toUpperCase() === "FAILED").length;
  const evidenceExecutions = matchingExecutions.filter(item => ["COMPLETE", "FAILED"].includes(String(item.qualityStatus).toUpperCase()));
  const pendingQualityCount = matchingExecutions.length - evidenceExecutions.length;
  const selectedEvidenceIds = new Set(form.evidenceReferences.map(item => item.referenceId));

  function updateParameterOverride(parameter, value) {
    const current = sourceValues.get(parameter.code);
    const isUnchanged = String(current ?? "") === String(value ?? "");
    onChange({
      ...form,
      parameterOverrides: isUnchanged
        ? form.parameterOverrides.filter(item => item.code !== parameter.code)
        : [...form.parameterOverrides.filter(item => item.code !== parameter.code), { code: parameter.code, value, dataType: parameter.dataType || "string" }],
    });
  }

  function update(field, value) {
    onChange({ ...form, [field]: value });
  }

  function toggleEvidence(executionId, checked) {
    update("evidenceReferences", checked
      ? [...form.evidenceReferences, { kind: "process-execution", referenceId: executionId }]
      : form.evidenceReferences.filter(item => item.referenceId !== executionId));
  }

  return (
    <div className="grid gap-5">
      {validation && <Alert tone="warning">{validation}</Alert>}
      {modelError && <Alert tone="warning">无法读取控制参数定义：{modelError}</Alert>}
      <section className="grid gap-3 border-b border-slate-200 pb-5 sm:grid-cols-2" aria-label="工艺规范修订基准">
        <div><p className="data-label">基准版本</p><p className="mt-1 text-sm font-semibold text-slate-900">{source.processSpecificationId} · V{source.version}</p></div>
        <div><p className="data-label">适用条件</p><p className="mt-1 text-sm font-semibold text-slate-900">{Object.values(source.contextSelector || {}).filter(Boolean).join(" · ") || "未限定"}</p></div>
      </section>
      <Card title="运行依据" description="仅质量结论明确的实际运行会作为证据引用保存；待确认运行单独保留，不混入修订依据。">
        {executionsLoading ? <p className="text-sm text-slate-500">正在读取运行记录…</p> : (
          <div className="grid gap-3 sm:grid-cols-4">
            <div><p className="data-label">已完成运行</p><p className="mt-1 text-2xl font-semibold text-slate-950">{matchingExecutions.length}</p></div>
            <div><p className="data-label">质量完成</p><p className="mt-1 text-2xl font-semibold text-emerald-700">{passCount}</p></div>
            <div><p className="data-label">质量失败</p><p className="mt-1 text-2xl font-semibold text-rose-700">{failCount}</p></div>
            <div><p className="data-label">待确认</p><p className="mt-1 text-2xl font-semibold text-amber-700">{pendingQualityCount}</p></div>
          </div>
        )}
        {!executionsLoading && matchingExecutions.length > 0 && <p className="mt-4 text-sm text-slate-600">{matchingExecutions.map(item => item.executionId).join(" · ")}</p>}
        {!executionsLoading && matchingExecutions.length === 0 && <p className="text-sm text-slate-500">尚无该规范版本的已完成运行，暂不能从这里创建修订草稿。</p>}
      </Card>
      <Card title="修订说明" description="把工程判断和机理依据写进规范版本，供后续运行追溯。">
        <div className="grid gap-4">
          <Field label="修订理由" required><Textarea value={form.changeReason} onChange={event => update("changeReason", event.target.value)} placeholder="例如：针对保压阶段引起的面形偏差修订" /></Field>
          <Field label="机理依据"><Textarea value={form.mechanismNotes} onChange={event => update("mechanismNotes", event.target.value)} placeholder="记录参数作用、已知边界和工程判断" /></Field>
          <p className="text-sm text-slate-600">至少引用一条质量结论明确的实际运行。选择的运行会随修订草稿固化。</p>
          {evidenceExecutions.length > 1 && <label className="flex items-start gap-2 text-sm font-medium text-slate-700"><input type="checkbox" checked={form.evidenceReferences.length === evidenceExecutions.length} onChange={event => update("evidenceReferences", event.target.checked ? evidenceExecutions.map(item => ({ kind: "process-execution", referenceId: item.executionId })) : [])} />引用全部 {evidenceExecutions.length} 条可用运行</label>}
          <div className="grid gap-2">
            {evidenceExecutions.map(item => <label key={item.executionId} className="flex items-center justify-between gap-3 rounded-md border border-slate-200 px-3 py-2 text-sm text-slate-700"><span className="flex items-center gap-2"><input aria-label={`引用运行 ${item.executionId}`} type="checkbox" checked={selectedEvidenceIds.has(item.executionId)} onChange={event => toggleEvidence(item.executionId, event.target.checked)} />{item.executionId}</span><StatusBadge value={item.qualityStatus} /></label>)}
            {!executionsLoading && evidenceExecutions.length === 0 && <p className="text-sm text-amber-700">没有可引用的质量结论明确运行。</p>}
          </div>
        </div>
      </Card>
      <Card title="参数调整" description="只提交发生变化且允许修订的控制参数；其余参数由服务端从基准规范继承。">
        <div className="grid gap-3">
          {parameterEntries.map(parameter => {
            const current = sourceValues.get(parameter.code);
            const override = form.parameterOverrides.find(item => item.code === parameter.code);
            const nextValue = override?.value ?? String(current ?? "");
            const changed = Boolean(override);
            const isBoolean = parameter.dataType === "boolean";
            const numeric = ["double", "integer"].includes(parameter.dataType);
            const changeAllowed = parameter.changeAllowed !== false;
            const bounds = [parameter.minimum != null ? `下限 ${parameter.minimum}` : "", parameter.maximum != null ? `上限 ${parameter.maximum}` : "", parameter.step != null ? `步长 ${parameter.step}` : ""].filter(Boolean).join(" · ");
            return (
              <div key={parameter.code} className="grid gap-2 border-b border-slate-100 pb-3 last:border-b-0 last:pb-0 md:grid-cols-[minmax(0,1fr)_10rem_11rem] md:items-end">
                <div><p className="text-sm font-medium text-slate-900">{parameter.displayName || parameter.code}</p><p className="mt-0.5 text-xs text-slate-500">当前：{String(current ?? "未设置")}{parameter.unit ? ` ${parameter.unit}` : ""}{bounds ? `；${bounds}` : ""}</p></div>
                <Field label="修订值">
                  {isBoolean
                    ? <Select aria-label={`修订值 ${parameter.displayName || parameter.code}`} value={nextValue} disabled={!changeAllowed} onChange={event => updateParameterOverride(parameter, event.target.value)}><option value="true">是</option><option value="false">否</option></Select>
                    : <Input aria-label={`修订值 ${parameter.displayName || parameter.code}`} type={numeric ? "number" : "text"} min={numeric ? parameter.minimum : undefined} max={numeric ? parameter.maximum : undefined} step={numeric ? (parameter.step ?? (parameter.dataType === "integer" ? 1 : "any")) : undefined} value={nextValue} disabled={!changeAllowed} onChange={event => updateParameterOverride(parameter, event.target.value)} />}
                </Field>
                <p className={`pb-2 text-sm font-medium ${changed ? "text-trajectory-700" : "text-slate-400"}`}>{!changeAllowed ? "基准固定" : changed ? "已调整" : "保持不变"}</p>
              </div>
            );
          })}
        </div>
      </Card>
    </div>
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
              {characteristic.inputType !== "numeric" && (
                <Field label="合格值" hint={characteristic.inputType === "boolean" ? "填写 true 或 false。" : "每行填写一个；自由文本不配置时结果为待确认。"} className="md:col-span-2">
                  <Textarea value={characteristic.passingValuesText} disabled={readOnly} onChange={event => updateCharacteristic(index, "passingValuesText", event.target.value)} placeholder={characteristic.inputType === "boolean" ? "true" : "合格"} />
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
export const ProcessSpecificationsPage = ({ canWrite = true }) => <RegistryPage definition={registryPages.processSpecifications} canWrite={canWrite} />;
export const ProcessAnalysisPlansPage = ({ canWrite = true }) => <RegistryPage definition={registryPages.plans} canWrite={canWrite} />;
export const InspectionDefinitionsPage = ({ canWrite = true }) => <RegistryPage definition={registryPages.definitions} canWrite={canWrite} />;
export const QualityPlansPage = ({ canWrite = true }) => <RegistryPage definition={registryPages.plansQuality} canWrite={canWrite} />;
