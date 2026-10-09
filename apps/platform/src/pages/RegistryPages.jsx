// 提供版本化业务配置注册表及显式创建、发布和退役操作。
import { useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { deleteJson, postJson } from "../api/http";
import { createRegistryBusinessForm, RegistryBusinessEditor, registryBusinessPayload, registryBusinessValidation } from "../components/RegistryBusinessEditor";
import { extractRows, registeredSiteIds, useApi } from "../hooks/useApi";
import { recipeLaunchChecks } from "../recipe/launchReadiness";
import { RecipeVersionProposal } from "../recipe/RecipeVersionProposal";
import { Alert, Button, Card, DataTable, Drawer, EmptyState, Field, Input, LinkButton, Page, RequestError, Select, StatusBadge, Textarea, notify, useConfirmDialog } from "../ui/components";
import { formatTime, emptyInspectionCharacteristic, inspectionDefinitionForm, inspectionDefinitionPayload, inspectionDefinitionValidation, inspectionInputTypes, LoadingCard } from "./shared";

const registryPages = {
  processModels: {
    kind: "processModel",
    title: "工艺变量", description: "定义过程量和控制参数的代码、名称、单位和边界，不包含来源地址和采集频率。", endpoint: "/api/v1/process-data-models", key: "modelId",
    columns: [["modelId", "模型"], ["version", "版本"], ["name", "名称"], ["status", "状态"], ["updatedAt", "更新时间"]],
    createLabel: "创建工艺变量",
    template: { modelId: "", version: 1, name: "", description: "", status: "draft", acquisition: { dataItems: [] }, controlParameters: [], updatedAt: "" },
    deleteUrl: value => `/api/v1/process-data-models/${encodeURIComponent(value.modelId)}/${value.version}`,
  },
  processSpecifications: {
    kind: "processSpecificationVersion",
    title: "配方版本", description: "维护引用工艺变量的完整参数版本。", endpoint: "/api/v1/process-specifications", key: "processSpecificationId",
    columns: [["processSpecificationId", "配方版本"], ["version", "版本"], ["name", "名称"], ["status", "状态"], ["updatedAt", "更新时间"]],
    createLabel: "创建配方版本",
    template: { processSpecificationId: "", version: 1, name: "", basedOnVersion: null, dataModelId: "", dataModelVersion: 1, status: "draft", contextSelector: {}, values: [], updatedAt: "" },
    deleteUrl: value => `/api/v1/process-specifications/${encodeURIComponent(value.processSpecificationId)}/${value.version}`,
  },
  plans: {
    kind: "analysisPlan",
    title: "过程分析", description: "规定参与比较的过程曲线，以及均值、最小值和最大值。", endpoint: "/api/v1/process-analysis-plans", key: "planId",
    columns: [["planId", "模型"], ["version", "版本"], ["name", "名称"], ["status", "状态"], ["updatedAt", "更新时间"]],
    createLabel: "创建过程分析",
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
  const ingestionResponse = useApi(isProcessSpecification ? "/api/v1/ingestion-tasks" : "", { enabled: isProcessSpecification });
  const analysisPlansResponse = useApi(isProcessSpecification ? "/api/v1/process-analysis-plans" : "", { enabled: isProcessSpecification });
  const qualityPlansResponse = useApi(isProcessSpecification ? "/api/v1/inspection-plans" : "", { enabled: isProcessSpecification });
  const definitionsResponse = useApi(isProcessSpecification ? "/api/v1/inspection-definitions" : "", { enabled: isProcessSpecification });
  const launchResponses = [processModelsResponse, ingestionResponse, analysisPlansResponse, qualityPlansResponse, definitionsResponse];
  const launchLoading = launchResponses.some(response => response.loading && !response.data);
  const launchError = launchResponses.find(response => response.error)?.error || "";
  const [launchSource, setLaunchSource] = useState(null);
  const launchChecksFor = specification => recipeLaunchChecks({
    specification,
    models: extractRows(processModelsResponse.data),
    tasks: extractRows(ingestionResponse.data),
    analysisPlans: extractRows(analysisPlansResponse.data),
    qualityPlans: extractRows(qualityPlansResponse.data),
    definitions: extractRows(definitionsResponse.data),
  });
  const edgesResponse = useApi(isProcessSpecification ? "/api/edges" : "", { enabled: isProcessSpecification });
  const registeredSiteId = registeredSiteIds(edgesResponse.data)[0] || "";
  const executionsResponse = useApi(
    isProcessSpecification && registeredSiteId
      ? `/api/v1/process-executions?status=completed&limit=200&siteId=${encodeURIComponent(registeredSiteId)}`
      : "",
    { enabled: isProcessSpecification && Boolean(registeredSiteId) },
  );
  const [open, setOpen] = useState(false);
  const [searchParams] = useSearchParams();
  const openedProposalFromQuery = useRef(false);
  const [proposalSource, setProposalSource] = useState(null);
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

  useEffect(() => {
    if (!isProcessSpecification || openedProposalFromQuery.current) return;
    const recipe = searchParams.get("recipe");
    const version = searchParams.get("version");
    if (!recipe || !version) return;
    const row = rows.find(item =>
      item.processSpecificationId === recipe &&
      String(item.version) === String(version) &&
      item.status === "published");
    if (!row) return;
    openedProposalFromQuery.current = true;
    setProposalSource(row);
  }, [isProcessSpecification, searchParams, rows]);

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

  function promoteCorrection(draft) {
    if (!proposalSource) return;
    setNextDraftSource(proposalSource);
    setNextDraftForm({
      changeReason: draft.changeReason || "",
      mechanismNotes: "",
      evidenceReferences: draft.evidenceReferences || [],
      parameterOverrides: draft.parameterOverrides || [],
    });
    setProposalSource(null);
    setEditorError("");
    setShowValidation(false);
    setNextDraftOpen(true);
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
    ...(isProcessSpecification && launchLoading ? [{ key: "_launch", label: "上线检查", sortable: false, render: () => <span className="text-sm text-slate-500">检查中</span> }] : []),
    ...(isProcessSpecification && !launchLoading ? [{
      key: "_launch",
      label: "上线检查",
      sortable: false,
      render: (_value, row) => {
        if (launchError) return <StatusBadge value="unavailable" label="无法检查" />;
        const missing = launchChecksFor(row).filter(check => !check.ready).length;
        return (
          <button type="button" className="rounded-md hover:ring-2 hover:ring-slate-200" onClick={event => { event.stopPropagation(); setLaunchSource(row); }} aria-label={`查看 ${row.processSpecificationId} V${row.version} 的上线检查`}>
            <StatusBadge value={missing ? "incomplete" : "ready"} label={missing ? `缺 ${missing} 项` : "可以上线"} />
          </button>
        );
      },
    }] : []),
    {
      key: "_actions",
      label: "操作",
      render: (_value, row) => (
        <div className="flex min-w-max flex-wrap gap-1" onClick={event => event.stopPropagation()}>
          <Button variant="ghost" className="px-2" onClick={() => openMaintain(row)}>
            {!canWrite || isInspectionDefinition || (hasBusinessEditor && row.status !== "draft") ? "查看版本" : "编辑草稿"}
          </Button>
          {isProcessSpecification && row.status === "published" && (
            <Button variant="ghost" className="px-2 text-trajectory-700" onClick={() => setProposalSource(row)}>下一轮校正</Button>
          )}
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
        open={Boolean(launchSource)}
        onClose={() => setLaunchSource(null)}
        title={launchSource ? `${launchSource.processSpecificationId} V${launchSource.version} 上线检查` : "上线检查"}
        description="只检查这一版配方自己的依赖。全部通过后，生产切换选择这一版，新运行就会按它采集、分析和判定。"
        footer={<Button onClick={() => setLaunchSource(null)}>关闭</Button>}
        size="lg"
      >
        {launchSource && <RecipeLaunchChecklist checks={launchChecksFor(launchSource)} />}
      </Drawer>
      <Drawer
        open={Boolean(proposalSource)}
        onClose={() => setProposalSource(null)}
        closeOnBackdrop={false}
        title="这一版的下一轮校正"
        description="校正留在当前已发布版本上。只有认定为显著变更时，才创建下一版草稿。"
        footer={<Button onClick={() => setProposalSource(null)}>关闭</Button>}
        size="xl"
      >
        {proposalSource && (
          <RecipeVersionProposal
            specification={proposalSource}
            canWrite={canWrite}
            onPromote={promoteCorrection}
          />
        )}
      </Drawer>
      <Drawer
        open={nextDraftOpen}
        onClose={() => setNextDraftOpen(false)}
        closeOnBackdrop={false}
        title="修订配方版本"
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

function RecipeLaunchChecklist({ checks }) {
  const missing = checks.filter(check => !check.ready);
  return (
    <div className="grid gap-4">
      <ol className="grid divide-y divide-slate-200 rounded-lg border border-slate-200">
        {checks.map((check, index) => (
          <li key={check.key} className="flex items-start justify-between gap-4 p-4">
            <div className="min-w-0">
              <p className="flex items-center gap-2 font-semibold text-slate-900">
                <span className="text-xs text-slate-400">{String(index + 1).padStart(2, "0")}</span>
                {check.title}
                <StatusBadge value={check.ready ? "ready" : "incomplete"} label={check.ready ? "通过" : "待完成"} />
              </p>
              <p className="mt-1 text-sm leading-6 text-slate-600">{check.detail}</p>
            </div>
            {!check.ready && <Link to={check.to} className="shrink-0 text-sm font-medium text-blue-700 hover:text-blue-900">{check.action}</Link>}
          </li>
        ))}
      </ol>
      {missing.length
        ? <Alert tone="warning" title={`还缺 ${missing.length} 项`}>先完成上面标为待完成的项目，再到生产切换选择这一版。</Alert>
        : <div className="flex justify-end"><LinkButton to="/production/changeover">去做生产切换</LinkButton></div>}
    </div>
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
      <section className="grid gap-3 border-b border-slate-200 pb-5 sm:grid-cols-2" aria-label="配方版本修订基准">
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
        {!executionsLoading && matchingExecutions.length === 0 && <p className="text-sm text-slate-500">尚无该配方版本的已完成运行，暂不能从这里创建修订草稿。</p>}
      </Card>
      <Card title="修订说明" description="把工程判断和机理依据写进配方版本，供后续运行追溯。">
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
