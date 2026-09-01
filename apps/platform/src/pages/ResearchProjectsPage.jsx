// 编排真实生产证据、下一配方建议和工程师闭环的页面工作流。
import { useCallback, useEffect, useMemo, useState } from "react";
import { useNavigate, useParams, useSearchParams } from "react-router";
import { getJson, postJson } from "../api/http";
import {
  buildRecipeRecommendationDecisionPayload,
  canArchiveProject,
  nextProjectAction,
  projectFormInitial,
  statusLabels,
} from "../research/researchProjectModel";
import {
  RecipeExecutionLinkDrawer,
  RecipeRecommendationDecisionDrawer,
} from "../research/components/ResearchProjectDrawers";
import { CreateProjectDrawer } from "../research/components/CreateResearchProjectDrawer";
import { WorkspaceContent } from "../research/components/ResearchWorkspaceContent";
import {
  Button,
  Card,
  DataTable,
  EmptyState,
  Page,
  RequestError,
  Select,
  StatusBadge,
  notify,
} from "../ui/components";

export function ResearchProjectsPage({ identity }) {
  const navigate = useNavigate();
  const { projectId } = useParams();
  const [searchParams] = useSearchParams();
  const [projects, setProjects] = useState([]);
  const [statusFilter, setStatusFilter] = useState("open");
  const [loading, setLoading] = useState(true);
  const [detailLoading, setDetailLoading] = useState(false);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [error, setError] = useState("");
  const [createOpen, setCreateOpen] = useState(false);
  const [projectForm, setProjectForm] = useState(projectFormInitial);
  const [workspace, setWorkspace] = useState(null);
  const [saving, setSaving] = useState(false);
  const [recipeRecommendationTarget, setRecipeRecommendationTarget] = useState(null);
  const [recipeRecommendationForm, setRecipeRecommendationForm] = useState({});
  const [recipeExecutionLinkTarget, setRecipeExecutionLinkTarget] = useState(null);
  const [recipeExecutionLinkForm, setRecipeExecutionLinkForm] = useState({});

  const loadProjects = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const response = await getJson("/api/v1/research-projects?limit=100");
      setProjects(response?.data || []);
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { loadProjects(); }, [loadProjects]);

  useEffect(() => {
    if (projectId || searchParams.get("create") !== "1") return;
    setProjectForm(current => ({
      ...current,
      referenceProcessExecutionId: searchParams.get("executionId") || "",
    }));
    setCreateOpen(true);
  }, [projectId, searchParams]);

  useEffect(() => {
    if (!projectId) {
      setWorkspace(null);
      return;
    }
    setWorkspace(null);
    refreshWorkspace(projectId);
  }, [projectId]);

  const metrics = useMemo(() => ({
    completed: projects.filter(project => project.status === "completed").length,
    open: projects.filter(project => !["completed", "archived"].includes(project.status)).length,
  }), [projects]);

  const filteredProjects = useMemo(() => projects.filter(project => {
    if (statusFilter === "all") return true;
    if (statusFilter === "open") return !["completed", "archived"].includes(project.status);
    return project.status === statusFilter;
  }), [projects, statusFilter]);

  async function refreshWorkspace(targetProjectId = workspace?.project?.projectId) {
    if (!targetProjectId) return;
    setDetailLoading(true);
    setError("");
    try {
      const next = await getJson(`/api/v1/research-projects/${targetProjectId}`);
      if (!next?.project?.projectId) throw new Error("未找到该优化任务，任务可能已删除或尚未同步。");
      setWorkspace(current => ({
        ...next,
        optimizationObservationSummary: current?.project?.projectId === targetProjectId
          ? current.optimizationObservationSummary
          : null,
      }));
      setProjects(current => current.map(item =>
        item.projectId === next.project.projectId ? next.project : item));
      try {
        const observationSummary = await getJson(
          `/api/v1/research-projects/${targetProjectId}/optimization-readiness`,
        );
        setWorkspace(current => current?.project?.projectId === targetProjectId
          ? { ...current, optimizationObservationSummary: observationSummary }
          : current);
      } catch (requestError) {
        notify(`真实运行证据摘要暂不可用：${requestError.message}`, "warning");
      }
    } catch (requestError) {
      setError(requestError.message);
      notify(requestError.message, "danger");
    } finally {
      setDetailLoading(false);
    }
  }

  async function loadOlderWorkspaceHistory() {
    const currentProjectId = workspace?.project?.projectId;
    const cursors = workspace?.nextCursors || {};
    if (!currentProjectId || historyLoading) return;
    const collections = [
      ["recipeRecommendationFlows", "recipe-recommendation-flows", "recipe-recommendation-flows"],
      ["audit", "audit", "audit"],
    ].filter(([, cursorKey]) => cursors[cursorKey]);
    if (!collections.length) return;

    setHistoryLoading(true);
    try {
      const pages = await Promise.all(collections.map(async ([property, cursorKey, endpoint]) => {
        const cursor = encodeURIComponent(cursors[cursorKey]);
        const page = await getJson(
          `/api/v1/research-projects/${currentProjectId}/${endpoint}?limit=100&cursor=${cursor}`,
        );
        return [property, cursorKey, page];
      }));
      setWorkspace(current => {
        if (current?.project?.projectId !== currentProjectId) return current;
        const next = { ...current, nextCursors: { ...(current.nextCursors || {}) } };
        for (const [property, cursorKey, page] of pages) {
          next[property] = [...(current[property] || []), ...(page?.items || [])];
          next.nextCursors[cursorKey] = page?.nextCursor || null;
        }
        return next;
      });
    } catch (requestError) {
      notify(requestError.message, "danger");
    } finally {
      setHistoryLoading(false);
    }
  }

  function openProject(project) {
    navigate(`/research-projects/${encodeURIComponent(project.projectId)}`);
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
        context: {
          data_model: projectForm.dataModelKey,
          ...(projectForm.scenarioPackageKey ? { scenario_package: projectForm.scenarioPackageKey } : {}),
          ...projectForm.referenceContext,
        },
      });
      setProjects(current => [project, ...current]);
      setProjectForm(projectFormInitial);
      setCreateOpen(false);
      notify("优化任务已创建。", "success");
      openProject(project);
    } catch (requestError) {
      notify(requestError.message, "danger");
    } finally {
      setSaving(false);
    }
  }

  async function changeProjectStatus(targetStatus) {
    if (!workspace?.project) return;
    try {
      await postJson(
        `/api/v1/research-projects/${workspace.project.projectId}/status`,
        { targetStatus, revision: workspace.project.revision },
      );
      await refreshWorkspace();
      notify("项目阶段已更新。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    }
  }

  async function generateRecipeRecommendation() {
    if (!workspace?.project) return;
    try {
      const recommendation = await postJson(
        `/api/v1/research-projects/${workspace.project.projectId}/recipe-recommendations`,
        { seed: 0 },
      );
      await refreshWorkspace();
      notify(
        `已基于 ${Number(recommendation.observationCount || 0)} 条真实生产运行生成下一配方建议；建议不会自动下发，需由工程师确认。`,
        "success",
      );
    } catch (requestError) {
      notify(requestError.message, "danger");
    }
  }

  function startRecipeRecommendationDecision(recommendation, item) {
    setRecipeRecommendationTarget({ recommendation, item });
    setRecipeRecommendationForm({
      decision: "accepted",
      usefulnessRating: "",
      factors: Object.fromEntries((item.parameters || []).map(parameter => [parameter.variableCode, parameter.value])),
      reason: "",
    });
  }

  async function submitRecipeRecommendationDecision(event) {
    event.preventDefault();
    if (!recipeRecommendationTarget) return;
    setSaving(true);
    try {
      const { recommendation, item } = recipeRecommendationTarget;
      const rejected = recipeRecommendationForm.decision === "rejected";
      await postJson(
        `/api/v1/research-projects/recipe-recommendations/${recommendation.recommendationId}/items/${encodeURIComponent(item.recommendationKey)}/decision`,
        buildRecipeRecommendationDecisionPayload(item, recipeRecommendationForm),
      );
      setRecipeRecommendationTarget(null);
      await refreshWorkspace();
      notify(rejected
        ? "不采用决定已冻结；该建议项不再等待运行或结果。"
        : "工程师决定已冻结；请在此后启动实际生产运行，再单独关联。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    } finally {
      setSaving(false);
    }
  }

  function startRecipeExecutionLink(decision) {
    setRecipeExecutionLinkTarget(decision);
    setRecipeExecutionLinkForm({ actualExecutionKey: "" });
  }

  async function submitRecipeExecutionLink(event) {
    event.preventDefault();
    if (!recipeExecutionLinkTarget) return;
    setSaving(true);
    try {
      await postJson(
        `/api/v1/research-projects/recipe-recommendation-decisions/${recipeExecutionLinkTarget.decisionId}/execution-link`,
        { actualExecutionKey: recipeExecutionLinkForm.actualExecutionKey },
      );
      setRecipeExecutionLinkTarget(null);
      await refreshWorkspace();
      notify("实际生产运行关联已冻结。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    } finally {
      setSaving(false);
    }
  }

  async function materializeRecipeRecommendationOutcome(decision) {
    try {
      await postJson(
        `/api/v1/research-projects/recipe-recommendation-decisions/${decision.decisionId}/materialize-outcome`,
        {},
      );
      await refreshWorkspace();
      notify("已从实际运行、参数回读和检验记录冻结建议结果。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    }
  }

  if (projectId) {
    const project = workspace?.project;
    const projectAction = project ? nextProjectAction(project.status) : null;
    const canChangeProjectStatus = project && (
      project.ownerUserId === identity?.userId || (identity?.roles || []).includes("platform.admin")
    );
    return (
      <Page
        title={project?.name || "配方优化工作区"}
        description={project?.description || undefined}
        actions={<>
          <Button onClick={() => navigate("/research-projects")}>返回优化任务</Button>
          {projectAction && canChangeProjectStatus && <Button variant="primary" disabled={detailLoading} onClick={() => changeProjectStatus(projectAction[1])}>{projectAction[0]}</Button>}
          {canChangeProjectStatus && canArchiveProject(project.status) && <Button onClick={() => changeProjectStatus("archived")}>归档项目</Button>}
        </>}
      >
        <RequestError error={error} onRetry={() => refreshWorkspace(projectId)} />
        {!project ? (
          <Card><p className="py-16 text-center text-sm text-slate-500">{detailLoading ? "正在读取优化工作区…" : "未找到可显示的优化任务。"}</p></Card>
        ) : (
          <WorkspaceContent
            workspace={workspace}
            loading={detailLoading}
            historyLoading={historyLoading}
            onLoadOlderHistory={loadOlderWorkspaceHistory}
            onGenerateRecipeRecommendation={generateRecipeRecommendation}
            onRecipeRecommendationDecision={startRecipeRecommendationDecision}
            onLinkRecipeRecommendationExecution={startRecipeExecutionLink}
            onMaterializeRecipeRecommendationOutcome={materializeRecipeRecommendationOutcome}
            onAskAi={currentProjectId => navigate(`/chat?projectId=${encodeURIComponent(currentProjectId)}`)}
          />
        )}
        <RecipeRecommendationDecisionDrawer
          target={recipeRecommendationTarget}
          form={recipeRecommendationForm}
          setForm={setRecipeRecommendationForm}
          saving={saving}
          variables={workspace?.project?.variables || []}
          onClose={() => !saving && setRecipeRecommendationTarget(null)}
          onSubmit={submitRecipeRecommendationDecision}
        />
        <RecipeExecutionLinkDrawer
          decision={recipeExecutionLinkTarget}
          form={recipeExecutionLinkForm}
          setForm={setRecipeExecutionLinkForm}
          saving={saving}
          onClose={() => !saving && setRecipeExecutionLinkTarget(null)}
          onSubmit={submitRecipeExecutionLink}
        />
      </Page>
    );
  }

  return (
    <Page title="配方优化" actions={<Button variant="primary" onClick={() => setCreateOpen(true)}>新建优化任务</Button>}>
      <RequestError error={error} onRetry={loadProjects} />
      <section className="flex flex-col gap-3 border border-slate-200 bg-white px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
        <label className="flex items-center gap-3 text-sm font-medium text-slate-700">
          状态
          <Select className="w-44" value={statusFilter} onChange={event => setStatusFilter(event.target.value)}>
            <option value="open">进行中与待处理</option>
            <option value="all">全部</option>
            <option value="active">优化中</option>
            <option value="completed">已完成</option>
            <option value="archived">已归档</option>
          </Select>
        </label>
        <p className="text-[13px] text-slate-500">共 {projects.length} 项 · 待处理 {metrics.open} · 已完成 {metrics.completed}</p>
      </section>
      <Card title="优化任务">
        {loading ? <p className="py-12 text-center text-sm text-slate-500">正在读取优化任务…</p>
          : projects.length === 0 ? <EmptyState title="从一个配方优化目标开始" description="确定产品范围、质量目标、可控变量和安全边界；系统会直接吸收后续真实配方运行。" />
            : filteredProjects.length === 0 ? <EmptyState title="当前筛选条件下没有任务" description="请选择其他状态查看优化任务。" />
              : <DataTable rows={filteredProjects} keyField="projectId" onRowClick={openProject} columns={[
                { key: "name", label: "优化任务" },
                { key: "processName", label: "工艺" },
                { key: "productName", label: "产品", render: value => value || "—" },
                { key: "status", label: "阶段", render: value => <StatusBadge value={value} label={statusLabels[value] || value} /> },
                { key: "ownerUserId", label: "负责人", render: value => value === identity?.userId ? (identity.displayName || identity.username || value) : value || "—" },
                { key: "updatedAt", label: "最近更新", render: value => value ? new Date(value).toLocaleString("zh-CN") : "—" },
                { key: "open", label: "操作", render: (_, project) => <Button onClick={event => { event.stopPropagation(); openProject(project); }}>进入工作区</Button> },
              ]} />}
      </Card>
      <CreateProjectDrawer open={createOpen} saving={saving} form={projectForm} setForm={setProjectForm} onClose={() => !saving && setCreateOpen(false)} onSubmit={createProject} />
    </Page>
  );
}
