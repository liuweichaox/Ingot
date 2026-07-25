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
  notify,
} from "../ui/components";

const projectFormInitial = {
  name: "",
  processName: "",
  productName: "",
  materialName: "",
  description: "",
  objectiveName: "",
  objectiveUnit: "",
  objectiveDirection: "minimize",
  objectiveTarget: "",
  variableName: "",
  variableUnit: "",
  variableLower: "",
  variableUpper: "",
};

const statusLabels = {
  active: "研发中",
  validating: "验证中",
  completed: "已完成",
  archived: "已归档",
  proposed: "待选择",
  selected: "已选择",
  supported: "已支持",
  rejected: "已否定",
  inconclusive: "无定论",
  planned: "待批准",
  approved: "已批准",
  running: "执行中",
  cancelled: "已取消",
  candidate: "候选",
  validated: "已验证",
  draft: "草稿",
  reviewed: "已复核",
};

const taskTitles = {
  member: "添加项目成员",
  hypothesis: "提出研发假设",
  experiment: "设计验证实验",
  result: "记录实验计算结果",
  window: "形成候选工艺窗口",
  claim: "沉淀工艺知识",
};

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
    hypothesisId: workspace?.hypotheses?.[0]?.hypothesisId || "",
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
    processWindowId: workspace?.processWindows?.find(item => item.status === "validated")?.windowId || "",
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
      const next = await getJson(`/api/v1/research-projects/${projectId}`);
      setWorkspace(next);
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
          code: "objective-1",
          name: projectForm.objectiveName,
          unit: projectForm.objectiveUnit,
          direction: projectForm.objectiveDirection,
          target: Number(projectForm.objectiveTarget),
        }],
        variables: [{
          code: "control-1",
          name: projectForm.variableName,
          role: "control",
          unit: projectForm.variableUnit,
          lowerLimit: Number(projectForm.variableLower),
          upperLimit: Number(projectForm.variableUpper),
        }],
        constraints: [],
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

  async function validateWindow(window) {
    try {
      await postJson(`/api/v1/research-projects/process-windows/${window.windowId}/validate`, {});
      await refreshWorkspace();
      notify("工艺窗口已通过独立验证。", "success");
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
      title="工艺研发项目"
      description="以目标、假设、实验结果和工艺窗口组织完整研发证据链。"
      actions={<Button variant="primary" onClick={() => setCreateOpen(true)}>创建研发项目</Button>}
    >
      {error && <Alert tone="danger">{error}</Alert>}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Metric label="全部项目" value={projects.length} hint="当前可访问项目" />
        <Metric label="研发中" value={metrics.active} hint="正在设计和执行实验" />
        <Metric label="验证中" value={metrics.validating} hint="独立复核工艺窗口" />
        <Metric label="已完成" value={metrics.completed} hint="已形成验证结论" />
      </div>
      <Card title="研发项目" description="打开项目后，系统会根据真实数据状态提示下一步。">
        {loading ? (
          <p className="py-8 text-center text-sm text-slate-500">正在读取研发项目…</p>
        ) : projects.length === 0 ? (
          <EmptyState title="还没有研发项目" description="从一个清晰的目标和可控变量开始。" />
        ) : (
          <DataTable
            rows={projects}
            keyField="projectId"
            onRowClick={openProject}
            columns={[
              { key: "name", label: "研发项目" },
              { key: "processName", label: "工艺" },
              { key: "productName", label: "产品", render: value => value || "—" },
              { key: "status", label: "阶段", render: value => <StatusBadge value={statusLabels[value] || value} /> },
              { key: "objectives", label: "目标", render: value => `${value?.length || 0} 项` },
              { key: "updatedAt", label: "最近更新", render: value => value ? new Date(value).toLocaleString("zh-CN") : "—" },
            ]}
          />
        )}
      </Card>

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
        onValidateWindow={validateWindow}
        onReviewClaim={reviewClaim}
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
  const field = (name, value) => event => setForm({ ...form, [name]: event.target[value || "value"] });
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
        <Card title="项目范围">
          <div className="grid gap-4 md:grid-cols-2">
            <Field label="项目名称"><Input required value={form.name} onChange={field("name")} placeholder="光学模压工艺窗口研发" /></Field>
            <Field label="工艺名称"><Input required value={form.processName} onChange={field("processName")} /></Field>
            <Field label="目标产品"><Input value={form.productName} onChange={field("productName")} /></Field>
            <Field label="材料"><Input value={form.materialName} onChange={field("materialName")} /></Field>
            <Field label="项目说明" className="md:col-span-2"><Textarea value={form.description} onChange={field("description")} rows={3} /></Field>
          </div>
        </Card>
        <Card title="首要研发目标">
          <div className="grid gap-4 md:grid-cols-2">
            <Field label="指标名称"><Input required value={form.objectiveName} onChange={field("objectiveName")} placeholder="面形误差" /></Field>
            <Field label="优化方向"><Select value={form.objectiveDirection} onChange={field("objectiveDirection")}><option value="minimize">越低越好</option><option value="maximize">越高越好</option><option value="target">接近目标</option><option value="range">保持范围</option></Select></Field>
            <Field label="指标单位"><Input required value={form.objectiveUnit} onChange={field("objectiveUnit")} /></Field>
            <Field label="目标值"><Input required type="number" step="any" value={form.objectiveTarget} onChange={field("objectiveTarget")} /></Field>
          </div>
        </Card>
        <Card title="首个可控变量">
          <div className="grid gap-4 md:grid-cols-2">
            <Field label="变量名称"><Input required value={form.variableName} onChange={field("variableName")} placeholder="保压温度" /></Field>
            <Field label="变量单位"><Input required value={form.variableUnit} onChange={field("variableUnit")} /></Field>
            <Field label="允许下限"><Input required type="number" step="any" value={form.variableLower} onChange={field("variableLower")} /></Field>
            <Field label="允许上限"><Input required type="number" step="any" value={form.variableUpper} onChange={field("variableUpper")} /></Field>
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
  onValidateWindow,
  onReviewClaim,
  onAskAi,
  currentUserId,
}) {
  if (!workspace) return null;
  const { project, hypotheses = [], experiments = [], experimentResults = [], processWindows = [], knowledgeClaims = [] } = workspace;
  const projectAction = nextProjectAction(project.status);
  const completedExperiments = experiments.filter(item => item.status === "completed");
  const validatedWindows = processWindows.filter(item => item.status === "validated");
  const canEdit = !["completed", "archived"].includes(project.status);
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
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-5">
          <Metric label="研发假设" value={hypotheses.length} hint="明确要验证的规律" />
          <Metric label="实验计划" value={experiments.length} hint="含审批与运行计划" />
          <Metric label="计算结果" value={experimentResults.length} hint="来自数据快照" />
          <Metric label="候选窗口" value={processWindows.length} hint="统计边界与适用范围" />
          <Metric label="已验证窗口" value={validatedWindows.length} hint="独立复核通过" />
        </div>

        <Card title="下一步" description="进度由项目真实记录决定，不由页面切换决定。">
          <div className="flex flex-wrap gap-2">
            <Button onClick={() => onAskAi(project.projectId)}>让 AI 协助分析</Button>
            {canEdit && <Button onClick={() => onTask("member")}>添加协作成员</Button>}
            {canEdit && <Button onClick={() => onTask("hypothesis")}>提出假设</Button>}
            {project.status !== "draft" && canEdit && hypotheses.length > 0 && <Button onClick={() => onTask("experiment")}>设计实验</Button>}
            {project.status !== "draft" && canEdit && experiments.some(item => item.status === "running") && <Button onClick={() => onTask("result")}>记录计算结果</Button>}
            {project.status !== "draft" && canEdit && completedExperiments.length > 0 && experimentResults.length > 0 && <Button onClick={() => onTask("window")}>形成候选窗口</Button>}
            {canEdit && validatedWindows.length > 0 && <Button onClick={() => onTask("claim")}>沉淀工艺知识</Button>}
          </div>
        </Card>

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
            ]} />
          )}
        </Card>

        <Card title="实验">
          {experiments.length === 0 ? <EmptyState title="尚未设计实验" description="实验必须包含至少两个不同运行条件。" /> : (
            <DataTable rows={experiments} keyField="experimentId" columns={[
              { key: "name", label: "实验" },
              { key: "designMethod", label: "设计" },
              { key: "runPlan", label: "运行", render: value => `${value?.length || 0} 个条件` },
              { key: "resultIds", label: "结果", render: value => `${value?.length || 0} 份` },
              { key: "status", label: "状态", render: value => <StatusBadge value={statusLabels[value] || value} /> },
              {
                key: "actions",
                label: "操作",
                render: (_, row) => (
                  <div className="flex gap-2">
                    {row.status === "planned" && row.createdBy !== currentUserId && <Button onClick={event => { event.stopPropagation(); onExperimentStatus(row, "approved"); }}>批准</Button>}
                    {row.status === "planned" && row.createdBy === currentUserId && <span className="text-xs text-slate-500">等待其他成员批准</span>}
                    {row.status === "approved" && <Button onClick={event => { event.stopPropagation(); onExperimentStatus(row, "running"); }}>开始</Button>}
                    {row.status === "running" && <Button onClick={event => { event.stopPropagation(); onExperimentStatus(row, "completed"); }}>完成</Button>}
                  </div>
                ),
              },
            ]} />
          )}
        </Card>

        <Card title="工艺窗口">
          {processWindows.length === 0 ? <EmptyState title="尚未形成工艺窗口" description="先完成实验并记录由源数据计算的结果。" /> : (
            <DataTable rows={processWindows} keyField="windowId" columns={[
              { key: "name", label: "窗口" },
              { key: "confidence", label: "置信度", render: value => `${Math.round(value * 100)}%` },
              { key: "confidenceMethod", label: "方法" },
              { key: "applicability", label: "适用范围" },
              { key: "status", label: "状态", render: value => <StatusBadge value={statusLabels[value] || value} /> },
              {
                key: "actions",
                label: "操作",
                render: (_, row) => row.status === "candidate" && row.createdBy !== currentUserId
                  ? <Button onClick={event => { event.stopPropagation(); onValidateWindow(row); }}>独立验证</Button>
                  : row.status === "candidate" ? <span className="text-xs text-slate-500">等待其他成员验证</span> : "—",
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
  const validatedWindows = workspace.processWindows.filter(item => item.status === "validated");
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
