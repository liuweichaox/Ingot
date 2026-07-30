import { useCallback, useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { getJson, postJson, putJson } from "../api/http";
import {
  Alert,
  Button,
  Card,
  DataTable,
  Drawer,
  EmptyState,
  Field,
  Input,
  Metric,
  Page,
  Select,
  StatusBadge,
  Textarea,
  WorkflowGuide,
  notify,
} from "../ui/components";

const projectFormInitial = {
  name: "",
  referenceCycleId: "",
  dataModelKey: "",
  processName: "",
  productName: "",
  materialName: "",
  description: "",
  objectiveKey: "",
  objectiveCode: "",
  objectiveName: "",
  objectiveUnit: "",
  objectiveDirection: "minimize",
  objectiveTarget: "",
  objectiveWeight: "1",
  objectiveDataSource: "",
  variableCode: "",
  variableName: "",
  variableUnit: "",
  variableLower: "",
  variableUpper: "",
  variableDataSource: "",
  outcomeConstraintKey: "",
  outcomeConstraintCode: "",
  outcomeConstraintName: "",
  outcomeConstraintMetric: "",
  outcomeConstraintOperator: "<=",
  outcomeConstraintLimit: "",
  outcomeConstraintUnit: "",
  outcomeConstraintProbability: "0.95",
};

const statusLabels = {
  active: "研发中",
  validating: "验证中",
  completed: "已完成",
  archived: "已归档",
  proposed: "待选择",
  selected: "已选择",
  supported: "已支持",
  rejected: "本轮未支持",
  inconclusive: "无定论",
  planned: "待批准",
  approved: "已批准",
  running: "执行中",
  cancelled: "已取消",
  candidate: "候选",
  validated: "已验证",
  evidence: "候选证据",
  replay: "回放已复核",
  laboratory: "实验室重复验证",
  production: "已发布生产",
  "awaiting-approval": "等待批准",
  ready: "待执行",
  dispatched: "已下发",
  draft: "草稿",
  reviewed: "已复核",
};

const taskTitles = {
  member: "添加项目成员",
  hypothesis: "提出研发假设",
  experiment: "设计验证实验",
  history: "导入历史运行",
  result: "记录实验计算结果",
  window: "形成候选工艺窗口",
  claim: "沉淀工艺知识",
};

function formatResearchNumber(value) {
  if (!Number.isFinite(Number(value))) return "—";
  return Number(value).toLocaleString("zh-CN", { maximumFractionDigits: 4 });
}

function experimentScale(experiment) {
  const runs = experiment.runPlan || [];
  const signatures = new Set(runs.map(run =>
    (run.factors || [])
      .map(factor => `${factor.variableCode}:${Number(factor.value)}`)
      .sort()
      .join("|")));
  const distinctConditions = experiment.optimization?.distinctConditionCount ||
    Math.max(1, signatures.size);
  return {
    distinctConditions,
    replicates: experiment.optimization?.replicatesPerCondition ||
      Math.max(1, Math.floor(runs.length / distinctConditions)),
  };
}

function nextProjectAction(status) {
  if (status === "draft") return ["开始研发", "active"];
  if (status === "active") return ["进入验证", "validating"];
  if (status === "validating") return ["完成项目", "completed"];
  return null;
}

function createTaskForm(task, workspace) {
  const variable = workspace?.project?.variables?.find(item => item.role === "control");
  const objective = workspace?.project?.objectives?.[0];
  const experiment = workspace?.experiments?.find(item => item.status === "running");
  const result = workspace?.experimentResults?.[0];
  return {
    member: "",
    statement: "",
    rationale: "",
    variableCode: variable?.code || "",
    validationOutcomeCode: "",
    expectedEffectDirection: "",
    minimumEffect: "",
    hypothesisId: workspace?.hypotheses?.[0]?.hypothesisId || "",
    cycleIds: [],
    experimentId: experiment?.experimentId || "",
    name: "",
    low: variable?.lowerLimit ?? "",
    high: variable?.upperLimit ?? "",
    stopRule: "触发安全约束或设备异常时立即停止。",
    rollbackPlan: "恢复项目基线配方并保留本次运行数据。",
    datasetSnapshotId: "",
    baseline: objective?.baseline ?? "",
    observed: "",
    baselineN: "",
    experimentN: "",
    runCount: experiment?.runPlan?.length || 2,
    replicateCount: 1,
    safetyPassed: "true",
    resultId: result?.resultId || "",
    lower: variable?.lowerLimit ?? "",
    upper: variable?.upperLimit ?? "",
    confidence: "0.95",
    confidenceMethod: "bootstrap",
    applicability: "",
    processWindowId: workspace?.processWindows?.find(item =>
      item.status === "validated" &&
      ["laboratory", "production"].includes(item.validationLevel))?.windowId || "",
  };
}

export function ResearchProjectsPage() {
  const navigate = useNavigate();
  const [projects, setProjects] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [identity, setIdentity] = useState(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [projectForm, setProjectForm] = useState(projectFormInitial);
  const [workspace, setWorkspace] = useState(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [task, setTask] = useState("");
  const [taskForm, setTaskForm] = useState({});

  const load = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const [response, currentIdentity] = await Promise.all([
        getJson("/api/v1/research-projects?limit=100"),
        getJson("/api/v1/auth/me"),
      ]);
      setProjects(response?.data || []);
      setIdentity(currentIdentity);
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  const metrics = useMemo(() => ({
    active: projects.filter(project => project.status === "active").length,
    validating: projects.filter(project => project.status === "validating").length,
    completed: projects.filter(project => project.status === "completed").length,
  }), [projects]);

  async function refreshWorkspace(projectId = workspace?.project?.projectId) {
    if (!projectId) return;
    setDetailLoading(true);
    try {
      const [next, observationSummary] = await Promise.all([
        getJson(`/api/v1/research-projects/${projectId}`),
        getJson(`/api/v1/research-projects/${projectId}/experiment-readiness`),
      ]);
      setWorkspace({ ...next, optimizationObservationSummary: observationSummary });
      setProjects(current => current.map(item =>
        item.projectId === next.project.projectId ? next.project : item));
    } catch (requestError) {
      notify(requestError.message, "danger");
    } finally {
      setDetailLoading(false);
    }
  }

  async function openProject(project) {
    setWorkspace({
      project,
      hypotheses: [],
      experiments: [],
      experimentResults: [],
      processWindows: [],
      knowledgeClaims: [],
      audit: [],
      optimizationObservationSummary: null,
    });
    await refreshWorkspace(project.projectId);
  }

  async function createProject(event) {
    event.preventDefault();
    setSaving(true);
    try {
      const suffix = Date.now().toString(36);
      const project = await postJson("/api/v1/research-projects", {
        code: `research-${suffix}`,
        name: projectForm.name,
        processName: projectForm.processName,
        productName: projectForm.productName || null,
        materialName: projectForm.materialName || null,
        description: projectForm.description || null,
        objectives: [{
          code: projectForm.objectiveCode,
          name: projectForm.objectiveName,
          unit: projectForm.objectiveUnit,
          direction: projectForm.objectiveDirection,
          target: Number(projectForm.objectiveTarget),
          weight: Number(projectForm.objectiveWeight),
          dataSource: projectForm.objectiveDataSource || null,
        }],
        variables: [{
          code: projectForm.variableCode,
          name: projectForm.variableName,
          role: "control",
          unit: projectForm.variableUnit,
          lowerLimit: Number(projectForm.variableLower),
          upperLimit: Number(projectForm.variableUpper),
          dataSource: projectForm.variableDataSource || null,
        }],
        constraints: [],
        outcomeConstraints: projectForm.outcomeConstraintCode.trim() ? [{
          code: projectForm.outcomeConstraintCode,
          description: projectForm.outcomeConstraintName,
          outcomeCode: projectForm.outcomeConstraintMetric,
          operator: projectForm.outcomeConstraintOperator,
          limit: Number(projectForm.outcomeConstraintLimit),
          unit: projectForm.outcomeConstraintUnit,
          safetyCritical: true,
          minimumProbability: Number(projectForm.outcomeConstraintProbability),
          dataSource: `inspection:${projectForm.outcomeConstraintMetric}`,
        }] : [],
      });
      setProjects(current => [project, ...current]);
      setProjectForm(projectFormInitial);
      setCreateOpen(false);
      notify("研发项目已创建。", "success");
      await openProject(project);
    } catch (requestError) {
      notify(requestError.message, "danger");
    } finally {
      setSaving(false);
    }
  }

  async function changeProjectStatus(targetStatus) {
    try {
      await postJson(
        `/api/v1/research-projects/${workspace.project.projectId}/status`,
        { targetStatus },
      );
      await refreshWorkspace();
      notify("项目阶段已更新。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    }
  }

  async function changeExperimentStatus(experiment, targetStatus) {
    try {
      await postJson(
        `/api/v1/research-projects/experiments/${experiment.experimentId}/status`,
        { targetStatus },
      );
      await refreshWorkspace();
      notify("实验状态已更新。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    }
  }

  async function materializeExperimentResult(experiment) {
    try {
      await postJson(`/api/v1/research-projects/experiments/${experiment.experimentId}/materialize-result`, {});
      await refreshWorkspace();
      notify("已从冻结的配方、过程与检验数据自动计算实验结果。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    }
  }

  async function validateWindow(window) {
    try {
      await postJson(`/api/v1/research-projects/process-windows/${window.windowId}/validate`, {});
      await refreshWorkspace();
      notify("已完成独立复核；系统已按重复组和区组证据判定验证等级。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    }
  }

  async function releaseWindow(window) {
    try {
      await postJson(`/api/v1/research-projects/process-windows/${window.windowId}/release`, {});
      await refreshWorkspace();
      notify("工艺窗口已审核并发布生产。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    }
  }

  async function reviewClaim(claim) {
    try {
      await postJson(`/api/v1/research-projects/knowledge-claims/${claim.claimId}/review`, {});
      await refreshWorkspace();
      notify("工艺知识已完成复核。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    }
  }

  async function generateOptimizationSuggestions(intent = "reach-specification", hypothesisId = null) {
    try {
      const experiment = await postJson(
        `/api/v1/research-projects/${workspace.project.projectId}/optimize`,
        {
          // A single point cannot distinguish a process effect from run noise.
          // Keep the smallest useful industrial experiment as a two-condition batch.
          batchSize: 2,
          seed: 0,
          processProfile: /光学|镜片|lens|molding|模压/i.test(workspace.project.processName || "")
            ? "optical-lens-molding-v1"
            : "generic",
          intent,
          hypothesisId,
          autoAssembleObservations: true,
          replicatesPerCondition: 2,
        },
      );
      const alreadyActive = workspace.experiments.some(
        item => item.experimentId === experiment.experimentId,
      );
      await refreshWorkspace();
      notify(
        alreadyActive
          ? "上一批优化实验尚未形成完整观察，已返回原实验，系统没有重复生成配方。"
          : intent === "validate-hypothesis"
            ? "已设计安全的假设验证实验；完成检验后，证据和假设状态会自动更新。"
            : "已用真实运行和检验结果生成下一组优化实验，请按现有流程审核后执行。",
        "success",
      );
    } catch (requestError) {
      notify(requestError.message, "danger");
    }
  }

  function startTask(name) {
    setTask(name);
    setTaskForm(createTaskForm(name, workspace));
  }

  async function submitTask(event) {
    event.preventDefault();
    const project = workspace.project;
    const variable = project.variables.find(item => item.code === taskForm.variableCode);
    const objective = project.objectives[0];
    setSaving(true);
    try {
      if (task === "member") {
        const member = taskForm.member.trim().toLowerCase();
        await putJson(`/api/v1/research-projects/${project.projectId}`, {
          ...project,
          memberUserIds: [...new Set([...(project.memberUserIds || []), member])],
        });
      } else if (task === "hypothesis") {
        await postJson(`/api/v1/research-projects/${project.projectId}/hypotheses`, {
          statement: taskForm.statement,
          rationale: taskForm.rationale,
          variableCodes: [taskForm.variableCode],
          validationOutcomeCode: taskForm.validationOutcomeCode || null,
          expectedEffectDirection: taskForm.expectedEffectDirection || null,
          minimumEffect: taskForm.minimumEffect ? Number(taskForm.minimumEffect) : null,
          applicability: taskForm.applicability || null,
          confidence: 0,
        });
      } else if (task === "experiment") {
        const low = Number(taskForm.low);
        const high = Number(taskForm.high);
        await postJson(`/api/v1/research-projects/${project.projectId}/experiments`, {
          hypothesisId: taskForm.hypothesisId || null,
          name: taskForm.name,
          designMethod: "engineer-defined",
          factors: [{ variableCode: variable.code, value: low, unit: variable.unit }],
          runPlan: [
            {
              runKey: "condition-low",
              sequence: 1,
              replicateKey: "replicate-1",
              factors: [{ variableCode: variable.code, value: low, unit: variable.unit }],
            },
            {
              runKey: "condition-high",
              sequence: 2,
              replicateKey: "replicate-1",
              factors: [{ variableCode: variable.code, value: high, unit: variable.unit }],
            },
          ],
          objectiveCodes: [objective.code],
          replicateKeys: ["replicate-1"],
          stopRule: taskForm.stopRule,
          rollbackPlan: taskForm.rollbackPlan,
        });
      } else if (task === "history") {
        await postJson(`/api/v1/research-projects/${project.projectId}/experiments/import-history`, {
          cycleIds: taskForm.cycleIds,
        });
      } else if (task === "result") {
        const experiment = workspace.experiments.find(item => item.experimentId === taskForm.experimentId);
        await postJson(`/api/v1/research-projects/experiments/${experiment.experimentId}/results`, {
          datasetSnapshotId: taskForm.datasetSnapshotId,
          metrics: [{
            objectiveCode: objective.code,
            baselineValue: Number(taskForm.baseline),
            observedValue: Number(taskForm.observed),
            effectValue: Number(taskForm.observed) - Number(taskForm.baseline),
            unit: objective.unit,
            baselineSampleCount: Number(taskForm.baselineN),
            experimentSampleCount: Number(taskForm.experimentN),
            computationMethod: "source-snapshot comparison",
          }],
          runCount: Number(taskForm.runCount),
          replicateCount: Number(taskForm.replicateCount),
          distinctMaterialLotCount: 1,
          distinctEquipmentCount: 1,
          safetyPassed: taskForm.safetyPassed === "true",
          calculatedFromSource: true,
        });
      } else if (task === "window") {
        const result = workspace.experimentResults.find(item => item.resultId === taskForm.resultId);
        await postJson(`/api/v1/research-projects/${project.projectId}/process-windows`, {
          name: taskForm.name,
          variables: [{
            variableCode: variable.code,
            lowerBound: Number(taskForm.lower),
            upperBound: Number(taskForm.upper),
            unit: variable.unit,
          }],
          objectiveCodes: [objective.code],
          supportingExperimentIds: [result.experimentId],
          supportingResultIds: [result.resultId],
          confidence: Number(taskForm.confidence),
          confidenceMethod: taskForm.confidenceMethod,
          analysisRunId: result.analysisRunId,
          analysisHash: result.analysisHash,
          applicability: taskForm.applicability,
        });
      } else if (task === "claim") {
        await postJson(`/api/v1/research-projects/${project.projectId}/knowledge-claims`, {
          processWindowId: taskForm.processWindowId,
          statement: taskForm.statement,
          applicability: taskForm.applicability,
        });
      }
      setTask("");
      await refreshWorkspace();
      notify("研发记录已保存。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Page
      title="工艺优化工作台"
      description="用最少的有效实验，把生产问题追溯为可验证证据，再形成可复用的工艺窗口。"
      actions={<Button variant="primary" onClick={() => setCreateOpen(true)}>新建优化项目</Button>}
    >
      {error && <Alert tone="danger">{error}</Alert>}
      <section className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="rounded-2xl border border-blue-100 bg-gradient-to-br from-blue-50 via-white to-white p-5 shadow-sm sm:p-6">
          <p className="text-sm font-semibold text-blue-700">以问题为起点，而不是以数据录入为起点</p>
          <h2 className="mt-2 text-xl font-semibold tracking-tight text-slate-950">发现偏差 → 找到原因 → 设计最有价值的下一组实验 → 验证并固化窗口</h2>
          <p className="mt-2 max-w-3xl text-sm leading-6 text-slate-600">这里承载优化闭环。运行、质量和设备数据仍然是证据来源，但不会替代工程判断；系统会明确告诉你当前证据是否足以推荐下一步。</p>
          <div className="mt-5 flex flex-wrap gap-2">
            <Button onClick={() => navigate("/comparisons")}>从周期对比开始</Button>
            <Button onClick={() => navigate("/quality-analysis")}>查看质量偏差</Button>
            <Button variant="primary" onClick={() => setCreateOpen(true)}>围绕问题创建项目</Button>
          </div>
        </div>
        <div className="grid gap-3 sm:grid-cols-3 xl:grid-cols-1">
          <Metric label="进行中的优化" value={metrics.active + metrics.validating} hint="需要工程决策或独立验证" />
          <Metric label="已验证结论" value={metrics.completed} hint="已完成项目" />
          <Metric label="项目组合" value={projects.length} hint="当前可访问项目" />
        </div>
      </section>

      <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_20rem]">
        <Card
          title="优化项目"
          description="每个项目对应一个明确问题、受控变量、证据链和可验证的工艺窗口。"
          actions={<Button variant="ghost" onClick={() => setCreateOpen(true)}>新建项目</Button>}
        >
          {loading ? (
            <p className="py-12 text-center text-sm text-slate-500">正在读取优化项目…</p>
          ) : projects.length === 0 ? (
            <EmptyState title="从一个待解决的工艺问题开始" description="填写目标、首个可控变量和安全边界；其余证据会在推进过程中逐步补齐。" />
          ) : (
            <DataTable
              rows={projects}
              keyField="projectId"
              onRowClick={openProject}
              columns={[
                { key: "name", label: "优化项目" },
                { key: "processName", label: "工艺" },
                { key: "productName", label: "产品", render: value => value || "—" },
                { key: "status", label: "阶段", render: value => <StatusBadge value={statusLabels[value] || value} /> },
                { key: "objectives", label: "目标", render: value => `${value?.length || 0} 项` },
                { key: "updatedAt", label: "最近更新", render: value => value ? new Date(value).toLocaleString("zh-CN") : "—" },
                { key: "open", label: "操作", render: (_, project) => <Button onClick={event => { event.stopPropagation(); openProject(project); }}>进入工作区</Button> },
              ]}
            />
          )}
        </Card>

        <aside className="space-y-4">
          <WorkflowGuide
            title="第一次使用：只需走完这四步"
            description="不需要先配置所有数据和模型；先围绕一个真实问题建立闭环。"
            compact
            steps={[
              { title: "明确偏差", description: "从质量分析或历史对比确认问题和范围。", state: projects.length ? "done" : "current" },
              { title: "设定边界", description: "写下优化目标、一个可控变量和安全限制。", state: projects.length ? "current" : "upcoming" },
              { title: "执行建议", description: "系统依据已有观察推荐下一组实验。", state: "upcoming" },
              { title: "验证窗口", description: "独立验证后才成为可复用结论。", state: "upcoming" },
            ]}
          />
          <Card title="不同角色如何进入" description="同一闭环，不同视角。">
            <div className="space-y-3 text-sm leading-5">
              <p><strong className="text-slate-900">工艺工程师：</strong><span className="text-slate-600">创建项目，审核建议，决定实验与窗口。</span></p>
              <p><strong className="text-slate-900">质量人员：</strong><span className="text-slate-600">确认检测定义和结果，参与独立验证。</span></p>
              <p><strong className="text-slate-900">生产人员：</strong><span className="text-slate-600">按批准计划执行，保证运行上下文完整。</span></p>
              <p><strong className="text-slate-900">管理者：</strong><span className="text-slate-600">关注进行中项目、验证状态和已固化窗口。</span></p>
            </div>
          </Card>
        </aside>
      </div>

      <CreateProjectDrawer
        open={createOpen}
        saving={saving}
        form={projectForm}
        setForm={setProjectForm}
        onClose={() => !saving && setCreateOpen(false)}
        onSubmit={createProject}
      />

      <WorkspaceDrawer
        workspace={workspace}
        loading={detailLoading}
        onClose={() => setWorkspace(null)}
        onTask={startTask}
        onProjectStatus={changeProjectStatus}
        onExperimentStatus={changeExperimentStatus}
        onMaterializeExperimentResult={materializeExperimentResult}
        onValidateWindow={validateWindow}
        onReleaseWindow={releaseWindow}
        onReviewClaim={reviewClaim}
        onGenerateOptimizationSuggestions={generateOptimizationSuggestions}
        onAskAi={projectId => navigate(`/chat?projectId=${encodeURIComponent(projectId)}`)}
        currentUserId={identity?.username || identity?.userId || ""}
      />

      <TaskDrawer
        task={task}
        form={taskForm}
        setForm={setTaskForm}
        workspace={workspace}
        saving={saving}
        onClose={() => !saving && setTask("")}
        onSubmit={submitTask}
      />
    </Page>
  );
}

function CreateProjectDrawer({ open, saving, form, setForm, onClose, onSubmit }) {
  const [catalog, setCatalog] = useState({ cycles: [], definitions: [], models: [] });
  const [catalogLoading, setCatalogLoading] = useState(false);
  const [catalogError, setCatalogError] = useState("");

  useEffect(() => {
    if (!open) return;
    let mounted = true;
    setCatalogLoading(true);
    setCatalogError("");
    Promise.all([
      getJson("/api/v1/cycles?status=completed&limit=200"),
      getJson("/api/v1/inspection-definitions"),
      getJson("/api/v1/process-data-models"),
    ]).then(([cycles, definitions, models]) => {
      if (!mounted) return;
      setCatalog({
        cycles: cycles?.data || [],
        definitions: definitions?.data || [],
        models: models?.data || [],
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
  const selectedModel = selectableModels.find(item => `${item.modelId}:${item.version}` === form.dataModelKey);
  const objectiveOptions = catalog.definitions.flatMap(definition =>
    (definition.characteristics || [])
      .filter(item => ["numeric", "number"].includes(String(item.inputType).toLowerCase()))
      .map(item => ({
        key: `${definition.code}:${definition.version}:${item.code}`,
        definition,
        characteristic: item,
      })),
  );
  const selectedObjective = objectiveOptions.find(item => item.key === form.objectiveKey);

  function updateForm(values) {
    setForm(current => ({ ...current, ...values }));
  }

  function chooseReferenceCycle(correlationId) {
    const cycle = catalog.cycles.find(item => item.correlationId === correlationId);
    updateForm({
      referenceCycleId: correlationId,
      productName: cycle?.productCode || form.productName,
    });
  }

  function chooseDataModel(key) {
    const model = selectableModels.find(item => `${item.modelId}:${item.version}` === key);
    updateForm({
      dataModelKey: key,
      processName: model?.name || "",
      variableCode: "",
      variableName: "",
      variableUnit: "",
      variableDataSource: "",
    });
  }

  function chooseObjective(key) {
    const option = objectiveOptions.find(item => item.key === key);
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
    const parameter = (selectedModel?.recipeParameters || []).find(item => item.code === code);
    updateForm({
      variableCode: parameter?.code || "",
      variableName: parameter?.sourceField || parameter?.code || "",
      variableUnit: parameter?.unit || "",
      variableDataSource: parameter ? `recipe:${parameter.code}` : "",
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

  const cycleLabel = cycle => [
    cycle.correlationId,
    cycle.productSeries || cycle.productCode || "未标注产品",
    cycle.machineId || "未标注设备",
    cycle.completedAt ? new Date(cycle.completedAt).toLocaleString("zh-CN") : "",
  ].filter(Boolean).join(" · ");

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title="创建工艺研发项目"
      description="填写工艺范围、研发目标和首个可控变量。"
      size="xl"
      footer={<><Button disabled={saving} onClick={onClose}>取消</Button><Button variant="primary" disabled={saving} type="submit" form="research-project-form">{saving ? "正在创建…" : "创建项目"}</Button></>}
    >
      <form id="research-project-form" className="space-y-6" onSubmit={onSubmit}>
        {catalogError && <Alert tone="warning" title="部分选项暂不可用">{catalogError}</Alert>}
        {catalogLoading && <Alert tone="info">正在读取已完成运行、检测定义和工艺数据模型…</Alert>}
        <Card title="项目范围">
          <div className="grid gap-4 md:grid-cols-2">
            <Field label="项目名称"><Input required value={form.name} onChange={field("name")} placeholder="光学模压工艺窗口研发" /></Field>
            <Field label="参考运行" hint="选择后自动带入产品范围；不影响后续用更多运行形成证据。"><Select value={form.referenceCycleId} onChange={event => chooseReferenceCycle(event.target.value)}><option value="">暂不关联历史运行</option>{catalog.cycles.map(cycle => <option key={cycle.correlationId} value={cycle.correlationId}>{cycleLabel(cycle)}</option>)}</Select></Field>
            <Field label="工艺数据模型" hint="决定可选的配方参数与实际数据来源。"><Select required value={form.dataModelKey} onChange={event => chooseDataModel(event.target.value)}><option value="">选择已配置的工艺数据模型</option>{selectableModels.map(model => <option key={`${model.modelId}:${model.version}`} value={`${model.modelId}:${model.version}`}>{model.name} · v{model.version}</option>)}</Select></Field>
            <Field label="目标产品" hint="来自参考运行；未关联时可补充产品编号。"><Input value={form.productName} onChange={field("productName")} placeholder="产品编号（可选）" /></Field>
            <Field label="材料"><Input value={form.materialName} onChange={field("materialName")} /></Field>
            <Field label="项目说明" className="md:col-span-2"><Textarea value={form.description} onChange={field("description")} rows={3} /></Field>
          </div>
        </Card>
        <Card title="首要研发目标">
          <div className="grid gap-4 md:grid-cols-2">
            <Field label="质量指标" hint="从已发布的检测定义中选择，代码、单位和数据来源会自动带入。"><Select required value={form.objectiveKey} onChange={event => chooseObjective(event.target.value)}><option value="">选择数值型检测指标</option>{objectiveOptions.map(option => <option key={option.key} value={option.key}>{option.definition.name} · {option.characteristic.name}{option.characteristic.unit ? ` (${option.characteristic.unit})` : ""}</option>)}</Select></Field>
            <Field label="数据来源"><Input readOnly value={form.objectiveDataSource} placeholder="选择质量指标后自动带入" className="bg-slate-50 text-slate-600" /></Field>
            <Field label="优化方向"><Select value={form.objectiveDirection} onChange={field("objectiveDirection")}><option value="minimize">越低越好</option><option value="maximize">越高越好</option><option value="target">接近目标</option><option value="range">保持范围</option></Select></Field>
            <Field label="指标单位"><Input readOnly required value={form.objectiveUnit} placeholder="自动带入" className="bg-slate-50 text-slate-600" /></Field>
            <Field label="目标值" hint="来自检测上下限的建议值，可按研发规格调整。"><Input required type="number" step="any" value={form.objectiveTarget} onChange={field("objectiveTarget")} /></Field>
            <Field label="目标权重"><Input required type="number" min="0.01" step="any" value={form.objectiveWeight} onChange={field("objectiveWeight")} /></Field>
          </div>
        </Card>
        <Card title="首个可控变量">
          <div className="grid gap-4 md:grid-cols-2">
            <Field label="可控配方参数" hint={selectedModel ? "从所选工艺数据模型中选择。" : "请先选择工艺数据模型。"}><Select required disabled={!selectedModel} value={form.variableCode} onChange={event => chooseVariable(event.target.value)}><option value="">选择可控配方参数</option>{(selectedModel?.recipeParameters || []).map(parameter => <option key={parameter.code} value={parameter.code}>{parameter.sourceField || parameter.code}{parameter.unit ? ` (${parameter.unit})` : ""}</option>)}</Select></Field>
            <Field label="实际数据来源"><Input readOnly value={form.variableDataSource} placeholder="选择配方参数后自动带入" className="bg-slate-50 text-slate-600" /></Field>
            <Field label="变量单位"><Input readOnly required value={form.variableUnit} placeholder="自动带入" className="bg-slate-50 text-slate-600" /></Field>
            <Field label="允许下限" hint="这是实验允许范围，请按设备/安全规范确认。"><Input required type="number" step="any" value={form.variableLower} onChange={field("variableLower")} /></Field>
            <Field label="允许上限" hint="这是实验允许范围，请按设备/安全规范确认。"><Input required type="number" step="any" value={form.variableUpper} onChange={field("variableUpper")} /></Field>
          </div>
        </Card>
        <Card title="结果安全边界（可选）" description="例如裂纹率、破损率或粘模指标；优化器只推荐达到最低安全概率的配方。">
          <div className="grid gap-4 md:grid-cols-2">
            <Field label="安全指标" hint="选择后自动带入检测特性、单位和建议安全限值。"><Select value={form.outcomeConstraintKey} onChange={event => chooseConstraint(event.target.value)}><option value="">不设置额外结果安全边界</option>{objectiveOptions.filter(item => item.key !== selectedObjective?.key).map(option => <option key={option.key} value={option.key}>{option.definition.name} · {option.characteristic.name}</option>)}</Select></Field>
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

function WorkspaceDrawer({
  workspace,
  loading,
  onClose,
  onTask,
  onProjectStatus,
  onExperimentStatus,
  onMaterializeExperimentResult,
  onValidateWindow,
  onReleaseWindow,
  onReviewClaim,
  onGenerateOptimizationSuggestions,
  onAskAi,
  currentUserId,
}) {
  if (!workspace) return null;
  const {
    project,
    hypotheses = [],
    experiments = [],
    experimentResults = [],
    processWindows = [],
    knowledgeClaims = [],
  } = workspace;
  const projectAction = nextProjectAction(project.status);
  const completedExperiments = experiments.filter(item => item.status === "completed");
  const reviewedWindows = processWindows.filter(item => item.status === "validated");
  const validatedWindows = reviewedWindows.filter(item =>
    ["laboratory", "production"].includes(item.validationLevel));
  const observationSummary = workspace.optimizationObservationSummary;
  const canEdit = !["completed", "archived"].includes(project.status);
  const hasObservation = Number(observationSummary?.validObservationCount || 0) > 0;
  const hasRunningExperiment = experiments.some(item => item.status === "running");
  const observedRunKeys = new Set(
    (observationSummary?.observations || []).map(item => item.runKey),
  );
  const variableByCode = new Map(project.variables.map(item => [item.code, item]));
  const objectiveByCode = new Map(project.objectives.map(item => [item.code, item]));
  const constraintByCode = new Map(
    (project.outcomeConstraints || []).map(item => [item.code, item]),
  );
  const currentStage = project.status === "completed" && validatedWindows.length === 0
    ? ["历史项目待复验", "该项目按旧规则完成，但工艺窗口缺少跨区组重复证据；请新建复现实验完成实验室验证后再发布生产。"]
    : project.status === "completed"
      ? ["研究已闭环", "工艺窗口已完成实验室验证或生产发布，可沉淀并复用于相似工艺。"]
      : project.status === "draft"
    ? ["定义问题", "先明确目标、可控变量和安全边界。"]
    : hypotheses.length === 0
      ? ["建立假设", "把经验或异常转为可验证的因果判断。"]
      : experiments.length === 0
        ? ["设计实验", "优先使用智能建议，以最少实验获取最大信息量。"]
        : hasRunningExperiment
          ? ["收集证据", "等待运行和检验完成，再让系统更新模型。"]
          : experimentResults.length === 0
            ? ["计算结果", "把冻结的数据快照转成可追溯的实验结果。"]
            : processWindows.length === 0
              ? ["形成窗口", "将有证据支持的范围提交为候选工艺窗口。"]
              : validatedWindows.length === 0
                ? ["独立验证", "由其他成员验证窗口，避免把偶然结果当作规律。"]
                : ["沉淀知识", "已具备可复用结论，可复核后服务下一个项目。"];
  const workflowSteps = [
    { title: "定义", description: "目标、变量与安全边界", state: project.status === "draft" ? "current" : "done" },
    { title: "追因", description: "假设与历史证据", state: hypotheses.length ? "done" : project.status === "draft" ? "upcoming" : "current" },
    { title: "实验", description: "建议、批准与执行", state: experiments.length ? "done" : hypotheses.length ? "current" : "upcoming" },
    { title: "验证", description: "结果与候选窗口", state: validatedWindows.length ? "done" : experimentResults.length ? "current" : "upcoming" },
    { title: "复用", description: "知识与下一项目", state: knowledgeClaims.some(item => item.status === "reviewed") ? "done" : validatedWindows.length ? "current" : "upcoming" },
  ];
  return (
    <Drawer
      open
      onClose={onClose}
      title={project.name}
      description={project.description || `${project.processName}的工艺研发证据链`}
      size="xl"
      footer={<><Button onClick={onClose}>关闭</Button>{projectAction && <Button variant="primary" disabled={loading} onClick={() => onProjectStatus(projectAction[1])}>{projectAction[0]}</Button>}</>}
    >
      <div className="space-y-5">
        <section className="rounded-2xl border border-blue-100 bg-gradient-to-br from-blue-50 to-white p-5">
          <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <p className="text-xs font-semibold tracking-wide text-blue-700">当前决策</p>
              <h3 className="mt-1 text-xl font-semibold text-slate-950">{currentStage[0]}</h3>
              <p className="mt-1 max-w-2xl text-sm leading-6 text-slate-600">{currentStage[1]}</p>
            </div>
            <StatusBadge value={statusLabels[project.status] || project.status} />
          </div>
          <div className="mt-5 grid gap-4 lg:grid-cols-[minmax(0,1fr)_18rem]">
            <div className="flex flex-wrap content-start gap-2">
              <Button onClick={() => onAskAi(project.projectId)}>让 AI 协助分析</Button>
              {canEdit && <Button onClick={() => onTask("member")}>添加协作成员</Button>}
              {canEdit && hypotheses.length === 0 && <Button variant="primary" onClick={() => onTask("hypothesis")}>提出第一个假设</Button>}
              {project.status !== "draft" && canEdit && <Button onClick={() => onTask("history")}>导入历史运行</Button>}
              {project.status !== "draft" && canEdit && hypotheses.length > 0 && !hasRunningExperiment && <Button variant="primary" onClick={() => onGenerateOptimizationSuggestions()}>智能设计下一组实验</Button>}
              {project.status !== "draft" && canEdit && hypotheses.length > 0 && <Button onClick={() => onTask("experiment")}>手动设计实验</Button>}
              {project.status !== "draft" && canEdit && hasRunningExperiment && <Button onClick={() => onMaterializeExperimentResult(experiments.find(item => item.status === "running"))}>立即检查数据回收</Button>}
              {project.status !== "draft" && canEdit && completedExperiments.length > 0 && experimentResults.length > 0 && <Button variant="primary" onClick={() => onTask("window")}>形成候选窗口</Button>}
              {canEdit && validatedWindows.length > 0 && <Button variant="primary" onClick={() => onTask("claim")}>沉淀工艺知识</Button>}
            </div>
            <div className="rounded-xl border border-white/80 bg-white/80 p-4">
              <p className="text-sm font-semibold text-slate-900">优化模型准备度</p>
              <p className="mt-1 text-2xl font-semibold text-slate-950">{observationSummary?.validObservationCount ?? 0}<span className="ml-1 text-sm font-normal text-slate-500">条有效观察</span></p>
              <p className="mt-1 text-xs leading-5 text-slate-500">{hasObservation ? `已匹配 ${observationSummary?.candidateRunCount ?? 0} 个实验运行，可用于生成下一组建议。` : "尚无可用观察；完成运行、过程特征和检验结果的关联后自动具备条件。"}</p>
            </div>
          </div>
        </section>

        <WorkflowGuide title="项目推进路径" description="每一步都基于真实研发事实推进；不需要为了“走流程”填无价值的数据。" steps={workflowSteps} />

        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-7">
          <Metric label="研发假设" value={hypotheses.length} hint="待验证的规律" />
          <Metric label="实验计划" value={experiments.length} hint="设计与执行记录" />
          <Metric label="计算结果" value={experimentResults.length} hint="冻结快照得出" />
          <Metric label="候选窗口" value={processWindows.length} hint="有证据支持的范围" />
          <Metric label="已复核窗口" value={reviewedWindows.length} hint="包含回放证据复核" />
          <Metric label="实验室验证" value={validatedWindows.length} hint="跨区组重复实验通过" />
          <Metric label="可用于优化" value={observationSummary?.validObservationCount ?? 0} hint="参数、过程与结果已关联" />
        </div>
        {observationSummary?.excludedObservationCount > 0 && (
          <Alert tone="warning">
            有 {observationSummary.excludedObservationCount} 个周期因缺少检验值、过程特征或完整周期边界而未进入优化模型。
          </Alert>
        )}

        <Card title="研发目标与变量">
          <div className="grid gap-5 lg:grid-cols-2">
            <DataTable rows={project.objectives || []} keyField="code" columns={[
              { key: "name", label: "目标" },
              { key: "target", label: "目标值" },
              { key: "unit", label: "单位" },
            ]} />
            <DataTable rows={(project.variables || []).filter(item => item.role === "control")} keyField="code" columns={[
              { key: "name", label: "可控变量" },
              { key: "lowerLimit", label: "下限" },
              { key: "upperLimit", label: "上限" },
              { key: "unit", label: "单位" },
            ]} />
          </div>
        </Card>

        <Card title="假设">
          {hypotheses.length === 0 ? <EmptyState title="尚未提出假设" description="先说明变量为什么可能影响研发目标。" /> : (
            <DataTable rows={hypotheses} keyField="hypothesisId" columns={[
              { key: "statement", label: "假设" },
              { key: "rationale", label: "依据" },
              { key: "status", label: "状态", render: value => <StatusBadge value={statusLabels[value] || value} /> },
              {
                key: "actions",
                label: "下一步",
                render: (_, row) => row.validationOutcomeCode && row.expectedEffectDirection && row.minimumEffect > 0 && canEdit && project.status !== "draft"
                  ? <Button onClick={event => { event.stopPropagation(); onGenerateOptimizationSuggestions("validate-hypothesis", row.hypothesisId); }}>用优化器验证</Button>
                  : "补充验证标准后可自动设计实验",
              },
            ]} />
          )}
        </Card>

        <Card title="实验">
          {experiments.length === 0 ? <EmptyState title="尚未设计实验" description="实验必须包含至少两个不同运行条件。" /> : (
            <DataTable rows={experiments} keyField="experimentId" columns={[
              { key: "name", label: "实验" },
              { key: "designMethod", label: "设计" },
              {
                key: "runPlan",
                label: "实验规模",
                render: (value, row) => {
                  if (!row.optimization) return `${value?.length || 0} 次运行`;
                  const scale = experimentScale(row);
                  return `${scale.distinctConditions} 个条件 × ${scale.replicates} 次重复`;
                },
              },
              {
                key: "runKeys",
                label: "建议执行条件",
                render: (_, row) => {
                  const isHistorical = row.designMethod === "historical-observation";
                  const runs = isHistorical ? (row.runPlan || []).slice(0, 3) : row.runPlan || [];
                  return (
                    <div className="space-y-2">
                    {runs.map(run => (
                      <div key={run.runKey} className="rounded-lg border border-slate-200 bg-slate-50 p-2">
                        <div className="flex items-center justify-between gap-2">
                          <code className="block text-xs font-semibold text-slate-700">{run.runKey}</code>
                          {!isHistorical && (
                            <StatusBadge value={observedRunKeys.has(run.runKey) ? "数据已回收" : "等待运行"} />
                          )}
                        </div>
                        {(run.blockKey || run.replicateKey) && (
                          <div className="mt-1 text-[11px] text-slate-500">
                            {run.blockKey ? `区组 ${run.blockKey}` : ""}
                            {run.blockKey && run.replicateKey ? " · " : ""}
                            {run.replicateKey ? `重复条件 ${run.replicateKey}` : ""}
                          </div>
                        )}
                        {(run.factors || []).map(factor => {
                          const variable = variableByCode.get(factor.variableCode);
                          return (
                            <div key={factor.variableCode} className="mt-1 text-xs text-slate-600">
                              {variable?.name || factor.variableCode}：
                              <strong className="ml-1 text-slate-900">
                                {formatResearchNumber(factor.value)} {factor.unit || variable?.unit || ""}
                              </strong>
                            </div>
                          );
                        })}
                      </div>
                    ))}
                    {isHistorical && row.runPlan.length > runs.length && (
                      <div className="text-xs text-slate-500">
                        另有 {row.runPlan.length - runs.length} 条只读历史运行
                      </div>
                    )}
                  </div>
                  );
                },
              },
              {
                key: "execution",
                label: "执行交接",
                render: (value, row) => row.designMethod === "historical-observation"
                  ? "—"
                  : (
                    <div className="space-y-1 text-xs">
                      <StatusBadge value={
                        statusLabels[value?.state] ||
                        statusLabels[row.status] ||
                        value?.state ||
                        row.status
                      } />
                      <div className="text-slate-500">
                        {value?.commands?.length || row.runPlan?.length || 0} 条设备无关执行指令
                      </div>
                    </div>
                  ),
              },
              {
                key: "optimization",
                label: "预测与可信度",
                render: value => {
                  if (!value) return "—";
                  return (
                    <div className="min-w-72 space-y-2 text-xs text-slate-600">
                      <div>
                        贝叶斯优化基于 <strong className="text-slate-900">{value.observationCount}</strong> 条观察和{" "}
                        <strong className="text-slate-900">{value.processFeatureCount || 0}</strong> 个共同轨迹特征
                      </div>
                      {(value.runPredictions || []).map(prediction => (
                        <div key={prediction.runKey} className="rounded-lg border border-slate-200 bg-white p-2">
                          <div className="flex flex-wrap items-center justify-between gap-2">
                            <code>{prediction.runKey}</code>
                            <span>
                              安全可行概率{" "}
                              <strong className="text-slate-900">
                                {Math.round(Number(prediction.feasibilityProbability || 0) * 100)}%
                              </strong>
                            </span>
                          </div>
                          {Object.entries(prediction.objectives || {}).map(([code, estimate]) => {
                            const objective = objectiveByCode.get(code);
                            return (
                              <div key={code} className="mt-1">
                                {objective?.name || code}预测：
                                <strong className="ml-1 text-slate-900">
                                  {formatResearchNumber(estimate.mean)} {estimate.unit || objective?.unit || ""}
                                </strong>
                                <span className="ml-1 text-slate-500">
                                  （95% 区间 {formatResearchNumber(estimate.lower95)} ～ {formatResearchNumber(estimate.upper95)}）
                                </span>
                              </div>
                            );
                          })}
                          {Object.entries(prediction.constraints || {}).map(([code, estimate]) => {
                            const constraint = constraintByCode.get(code);
                            return (
                              <div key={code} className="mt-1">
                                {constraint?.description || code}预测：
                                <strong className="ml-1 text-slate-900">
                                  {formatResearchNumber(estimate.mean)} {estimate.unit || constraint?.unit || ""}
                                </strong>
                              </div>
                            );
                          })}
                        </div>
                      ))}
                    </div>
                  );
                },
              },
              { key: "resultIds", label: "结果", render: value => `${value?.length || 0} 份` },
              {
                key: "status",
                label: "状态",
                render: (value, row) => (
                  <StatusBadge value={row.designMethod === "historical-observation" ? "已导入" : statusLabels[value] || value} />
                ),
              },
              {
                key: "actions",
                label: "操作",
                render: (_, row) => (
                  <div className="flex gap-2">
                    {row.designMethod === "historical-observation" && <span className="text-xs text-slate-500">只读证据</span>}
                    {row.designMethod !== "historical-observation" && row.status === "planned" && row.createdBy !== currentUserId && <Button onClick={event => { event.stopPropagation(); onExperimentStatus(row, "approved"); }}>批准</Button>}
                    {row.designMethod !== "historical-observation" && row.status === "planned" && row.createdBy === currentUserId && <span className="text-xs text-slate-500">等待其他成员批准</span>}
                    {row.designMethod !== "historical-observation" && row.status === "approved" && <Button onClick={event => { event.stopPropagation(); onExperimentStatus(row, "running"); }}>下发并开始</Button>}
                    {row.designMethod !== "historical-observation" && row.status === "running" && (
                      <span className="text-xs text-slate-500">
                        采集和检验齐全后自动完成
                      </span>
                    )}
                  </div>
                ),
              },
            ]} />
          )}
        </Card>

        <Card title="实验结果" description="只接受由冻结数据快照计算的结果；它们是追因结论和优化建议的共同证据。">
          {experimentResults.length === 0 ? <EmptyState title="尚无可用结果" description="运行完成后，关联过程数据与检验结果并记录计算结果。" /> : (
            <DataTable rows={experimentResults} keyField="resultId" columns={[
              {
                key: "experimentId",
                label: "来源实验",
                render: value => experiments.find(item => item.experimentId === value)?.name || value,
              },
              { key: "runCount", label: "运行数" },
              { key: "replicateCount", label: "每条件重复" },
              {
                key: "distinctBlockCount",
                label: "独立区组",
                render: value => Number(value) > 0 ? value : "未记录",
              },
              {
                key: "metrics",
                label: "目标结果",
                render: value => (
                  <div className="min-w-72 space-y-2">
                    {(value || []).map(metric => {
                      const objective = objectiveByCode.get(metric.objectiveCode);
                      const target = objective?.target;
                      const reached = Number.isFinite(Number(target)) &&
                        (objective?.direction === "maximize"
                          ? metric.lowerConfidenceBound >= target
                          : metric.upperConfidenceBound <= target);
                      return (
                        <div key={metric.objectiveCode} className="rounded-lg border border-slate-200 bg-slate-50 p-2 text-xs">
                          <div className="flex flex-wrap items-center justify-between gap-2">
                            <strong>{objective?.name || metric.objectiveCode}</strong>
                            {Number.isFinite(Number(target)) && (
                              <StatusBadge value={reached ? "达到目标" : "需继续验证"} />
                            )}
                          </div>
                          <div className="mt-1 text-slate-600">
                            实测均值{" "}
                            <strong className="text-slate-900">
                              {formatResearchNumber(metric.observedValue)} {metric.unit || objective?.unit || ""}
                            </strong>
                            <span className="ml-1">
                              （95% 区间 {formatResearchNumber(metric.lowerConfidenceBound)} ～ {formatResearchNumber(metric.upperConfidenceBound)}）
                            </span>
                          </div>
                          <div className="mt-1 text-slate-500">
                            历史基线 {formatResearchNumber(metric.baselineValue)}，变化 {formatResearchNumber(metric.effectValue)}
                          </div>
                        </div>
                      );
                    })}
                  </div>
                ),
              },
              { key: "safetyPassed", label: "安全约束", render: value => <StatusBadge value={value ? "passed" : "failed"} /> },
              { key: "analysisHash", label: "分析快照", render: value => value ? <code className="text-xs text-slate-600">{String(value).slice(0, 12)}…</code> : "—" },
            ]} />
          )}
        </Card>

        <Card title="工艺窗口">
          {processWindows.length === 0 ? <EmptyState title="尚未形成工艺窗口" description="先完成实验并记录由源数据计算的结果。" /> : (
            <DataTable rows={processWindows} keyField="windowId" columns={[
              { key: "name", label: "窗口" },
              {
                key: "variables",
                label: "变量范围",
                render: value => (
                  <div className="space-y-1">
                    {(value || []).map(variable => {
                      const definition = variableByCode.get(variable.variableCode);
                      return (
                        <div key={variable.variableCode} className="text-xs">
                          {definition?.name || variable.variableCode}：
                          <strong className="ml-1">
                            {formatResearchNumber(variable.lowerBound)} ～ {formatResearchNumber(variable.upperBound)} {variable.unit || definition?.unit || ""}
                          </strong>
                        </div>
                      );
                    })}
                  </div>
                ),
              },
              { key: "confidence", label: "置信度", render: value => `${Math.round(value * 100)}%` },
              { key: "confidenceMethod", label: "方法" },
              { key: "applicability", label: "适用范围" },
              {
                key: "validationLevel",
                label: "验证等级",
                render: (value, row) => (
                  <StatusBadge value={
                    row.status === "validated" && (!value || value === "evidence")
                      ? "历史复核（等级未记录）"
                      : statusLabels[value] || value || "候选证据"
                  } />
                ),
              },
              {
                key: "actions",
                label: "下一步",
                render: (_, row) => {
                  if (row.status === "candidate" && row.createdBy !== currentUserId) {
                    return <Button onClick={event => { event.stopPropagation(); onValidateWindow(row); }}>独立复核</Button>;
                  }
                  if (row.status === "candidate") {
                    return <span className="text-xs text-slate-500">等待其他成员复核</span>;
                  }
                  if (row.validationLevel === "laboratory" && row.validatedBy !== currentUserId) {
                    return <Button onClick={event => { event.stopPropagation(); onReleaseWindow(row); }}>发布生产</Button>;
                  }
                  if (row.validationLevel === "replay") {
                    return <span className="text-xs text-amber-700">需跨区组重复实验</span>;
                  }
                  return "—";
                },
              },
            ]} />
          )}
        </Card>

        <Card title="可复用工艺知识">
          {knowledgeClaims.length === 0 ? <EmptyState title="尚未沉淀知识" description="只有经过验证的工艺窗口才能转化为工艺知识。" /> : (
            <DataTable rows={knowledgeClaims} keyField="claimId" columns={[
              { key: "statement", label: "知识声明" },
              { key: "applicability", label: "适用范围" },
              { key: "status", label: "状态", render: value => <StatusBadge value={statusLabels[value] || value} /> },
              {
                key: "actions",
                label: "操作",
                render: (_, row) => row.status === "draft" && row.createdBy !== currentUserId
                  ? <Button onClick={event => { event.stopPropagation(); onReviewClaim(row); }}>复核</Button>
                  : row.status === "draft" ? <span className="text-xs text-slate-500">等待其他成员复核</span> : "—",
              },
            ]} />
          )}
        </Card>
      </div>
    </Drawer>
  );
}

function TaskDrawer({ task, form, setForm, workspace, saving, onClose, onSubmit }) {
  if (!task || !workspace) return null;
  const update = name => event => setForm({ ...form, [name]: event.target.value });
  const variables = workspace.project.variables.filter(item => item.role === "control");
  const runningExperiments = workspace.experiments.filter(item => item.status === "running");
  const validatedWindows = workspace.processWindows.filter(item =>
    item.status === "validated" &&
    ["laboratory", "production"].includes(item.validationLevel));
  const [historicalCycles, setHistoricalCycles] = useState([]);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyError, setHistoryError] = useState("");

  useEffect(() => {
    if (task !== "history") return;
    let mounted = true;
    setHistoryLoading(true);
    setHistoryError("");
    const productCode = workspace.project.productName ? `&productCode=${encodeURIComponent(workspace.project.productName)}` : "";
    getJson(`/api/v1/cycles?status=completed&limit=200${productCode}`)
      .then(response => {
        if (!mounted) return;
        const values = response?.data || [];
        setHistoricalCycles(values);
        setForm(current => ({ ...current, cycleIds: values.map(item => item.correlationId) }));
      })
      .catch(requestError => {
        if (!mounted) return;
        setHistoryError(requestError.message || "无法读取已完成运行。");
      })
      .finally(() => { if (mounted) setHistoryLoading(false); });
    return () => { mounted = false; };
  }, [task, workspace.project.productName, setForm]);

  const historicalCycleLabel = cycle => [
    cycle.correlationId,
    cycle.productSeries || cycle.productCode || "未标注产品",
    cycle.recipeId ? `配方 ${cycle.recipeId}` : "",
    cycle.completedAt ? new Date(cycle.completedAt).toLocaleString("zh-CN") : "",
  ].filter(Boolean).join(" · ");
  return (
    <Drawer
      open
      onClose={onClose}
      title={taskTitles[task]}
      description="按研发事实填写，保存后进入项目证据链。"
      size="lg"
      footer={<><Button disabled={saving} onClick={onClose}>取消</Button><Button variant="primary" disabled={saving} type="submit" form="research-task-form">{saving ? "正在保存…" : "保存"}</Button></>}
    >
      <form id="research-task-form" className="space-y-4" onSubmit={onSubmit}>
        {task === "member" && <Field label="成员用户名" hint="成员可以查看和参与该项目。"><Input required value={form.member} onChange={update("member")} /></Field>}
        {task === "hypothesis" && <>
          <Field label="假设"><Textarea required rows={4} value={form.statement} onChange={update("statement")} placeholder="说明哪个变量通过什么机制影响目标。" /></Field>
          <Field label="提出依据"><Textarea required rows={4} value={form.rationale} onChange={update("rationale")} placeholder="填写历史数据、物理机理或专家经验。" /></Field>
          <VariableSelect variables={variables} value={form.variableCode} onChange={update("variableCode")} />
          <Field label="验证目标（可选）" hint="定义后可让优化器设计最有信息量的验证实验。"><Select value={form.validationOutcomeCode} onChange={update("validationOutcomeCode")}><option value="">暂不定义</option>{workspace.project.objectives.map(item => <option key={item.code} value={item.code}>{item.name}</option>)}</Select></Field>
          {form.validationOutcomeCode && <div className="grid gap-4 sm:grid-cols-2">
            <Field label="预期效应方向"><Select required value={form.expectedEffectDirection} onChange={update("expectedEffectDirection")}><option value="">请选择</option><option value="increase">指标增加</option><option value="decrease">指标降低</option></Select></Field>
            <Field label="最小可辨别效应"><Input required type="number" min="0.0000001" step="any" value={form.minimumEffect} onChange={update("minimumEffect")} /></Field>
          </div>}
          <Field label="适用范围（可选）"><Textarea rows={3} value={form.applicability} onChange={update("applicability")} placeholder="说明产品、材料、设备或环境边界。" /></Field>
        </>}
        {task === "experiment" && <>
          <Field label="实验名称"><Input required value={form.name} onChange={update("name")} /></Field>
          <Field label="验证的假设"><Select value={form.hypothesisId} onChange={update("hypothesisId")}><option value="">不关联具体假设</option>{workspace.hypotheses.map(item => <option key={item.hypothesisId} value={item.hypothesisId}>{item.statement}</option>)}</Select></Field>
          <VariableSelect variables={variables} value={form.variableCode} onChange={update("variableCode")} />
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="低水平"><Input required type="number" step="any" value={form.low} onChange={update("low")} /></Field>
            <Field label="高水平"><Input required type="number" step="any" value={form.high} onChange={update("high")} /></Field>
          </div>
          <Field label="停止规则"><Textarea required rows={3} value={form.stopRule} onChange={update("stopRule")} /></Field>
          <Field label="回退方案"><Textarea required rows={3} value={form.rollbackPlan} onChange={update("rollbackPlan")} /></Field>
        </>}
        {task === "history" && <>
          <Alert tone="info" title="把已有数据变成优化观察">
            系统只读取已完成运行的实际配方回读、过程特征和检验记录；不会向设备写入参数。至少选择两种实际配方条件，导入后优化器才能使用这些观察。
          </Alert>
          {historyError && <Alert tone="danger">{historyError}</Alert>}
          {historyLoading ? <Alert tone="info">正在读取可导入的已完成运行…</Alert> : (
            <Field label="已完成运行" hint={`已默认选中 ${form.cycleIds?.length || 0} 个与项目产品匹配的运行；可按 Ctrl 或 Shift 调整。`}>
              <Select multiple required size="12" value={form.cycleIds || []} onChange={event => setForm({ ...form, cycleIds: Array.from(event.target.selectedOptions, option => option.value) })}>
                {historicalCycles.map(cycle => <option key={cycle.correlationId} value={cycle.correlationId}>{historicalCycleLabel(cycle)}</option>)}
              </Select>
            </Field>
          )}
        </>}
        {task === "result" && <>
          <Field label="执行中的实验"><Select required value={form.experimentId} onChange={update("experimentId")}>{runningExperiments.map(item => <option key={item.experimentId} value={item.experimentId}>{item.name}</option>)}</Select></Field>
          <Field label="数据快照"><Input required value={form.datasetSnapshotId} onChange={update("datasetSnapshotId")} placeholder="选择或填写本次实验冻结的数据快照" /></Field>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="基线值"><Input required type="number" step="any" value={form.baseline} onChange={update("baseline")} /></Field>
            <Field label="实验值"><Input required type="number" step="any" value={form.observed} onChange={update("observed")} /></Field>
            <Field label="基线样本数"><Input required type="number" min="1" value={form.baselineN} onChange={update("baselineN")} /></Field>
            <Field label="实验样本数"><Input required type="number" min="1" value={form.experimentN} onChange={update("experimentN")} /></Field>
            <Field label="运行记录数"><Input required type="number" min="2" value={form.runCount} onChange={update("runCount")} /></Field>
            <Field label="重复组数"><Input required type="number" min="1" value={form.replicateCount} onChange={update("replicateCount")} /></Field>
          </div>
          <Field label="安全约束"><Select value={form.safetyPassed} onChange={update("safetyPassed")}><option value="true">全部通过</option><option value="false">存在失败</option></Select></Field>
        </>}
        {task === "window" && <>
          <Field label="窗口名称"><Input required value={form.name} onChange={update("name")} /></Field>
          <Field label="支持结果"><Select required value={form.resultId} onChange={update("resultId")}>{workspace.experimentResults.map(item => <option key={item.resultId} value={item.resultId}>{new Date(item.recordedAt).toLocaleString("zh-CN")} · {item.safetyPassed ? "安全检查通过" : "安全检查失败"}</option>)}</Select></Field>
          <VariableSelect variables={variables} value={form.variableCode} onChange={update("variableCode")} />
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="窗口下限"><Input required type="number" step="any" value={form.lower} onChange={update("lower")} /></Field>
            <Field label="窗口上限"><Input required type="number" step="any" value={form.upper} onChange={update("upper")} /></Field>
            <Field label="统计置信度"><Input required type="number" min="0.01" max="1" step="0.01" value={form.confidence} onChange={update("confidence")} /></Field>
            <Field label="计算方法"><Select value={form.confidenceMethod} onChange={update("confidenceMethod")}><option value="bootstrap">Bootstrap</option><option value="conformal">保形推断</option><option value="bayesian">贝叶斯</option><option value="frequentist">频率学派</option></Select></Field>
          </div>
          <Field label="适用范围"><Textarea required rows={4} value={form.applicability} onChange={update("applicability")} placeholder="说明产品、材料批次、设备和环境边界。" /></Field>
        </>}
        {task === "claim" && <>
          <Field label="来源工艺窗口"><Select required value={form.processWindowId} onChange={update("processWindowId")}>{validatedWindows.map(item => <option key={item.windowId} value={item.windowId}>{item.name}</option>)}</Select></Field>
          <Field label="知识声明"><Textarea required rows={4} value={form.statement} onChange={update("statement")} /></Field>
          <Field label="适用范围"><Textarea required rows={4} value={form.applicability} onChange={update("applicability")} /></Field>
        </>}
      </form>
    </Drawer>
  );
}

function VariableSelect({ variables, value, onChange }) {
  return (
    <Field label="可控变量">
      <Select required value={value} onChange={onChange}>
        {variables.map(item => <option key={item.code} value={item.code}>{item.name}（{item.unit}）</option>)}
      </Select>
    </Field>
  );
}
