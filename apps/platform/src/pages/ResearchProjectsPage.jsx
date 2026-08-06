import { useCallback, useEffect, useMemo, useState } from "react";
import { useNavigate, useParams } from "react-router";
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
  scenarioPackageKey: "",
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
  accepted: "采用建议",
  modified: "修改建议",
  beneficial: "迁移有收益",
  neutral: "迁移无实质收益",
  "negative-transfer": "检测到负迁移",
  "insufficient-evidence": "迁移证据不足",
  recorded: "待复核",
};

const taskTitles = {
  member: "添加项目成员",
  hypothesis: "提出研发假设",
  experiment: "设计验证实验",
  history: "导入历史运行",
  claim: "沉淀工艺知识",
  "rollback-drill": "记录停止与回退演练",
  transfer: "评估工艺知识迁移",
};

const shadowDecisionLabels = {
  accepted: "采用建议",
  modified: "修改建议",
  rejected: "不采用建议",
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
    baselineRunKeys: [],
    name: "",
    low: variable?.lowerLimit ?? "",
    high: variable?.upperLimit ?? "",
    stopRule: "触发安全约束或设备异常时立即停止。",
    rollbackPlan: "恢复项目基线配方并保留本次运行数据。",
    applicability: "",
    processWindowId: workspace?.processWindows?.find(item =>
      item.status === "validated" &&
      ["laboratory", "production"].includes(item.validationLevel))?.windowId || "",
    knowledgeSourceType: "window",
    transferAssessmentId: workspace?.transferAssessments?.find(item =>
      item.status === "reviewed" && item.outcome === "beneficial")?.assessmentId || "",
    drillName: "受控在线停止与回退演练",
    drillScenario: "模拟安全约束触发或优化器不可用",
    drillStopTrigger: "安全约束触发、数据失效或优化器不可用时停止下一条建议",
    drillRollbackTarget: "恢复上一组经现场确认的安全参数",
    drillExpectedActions: "停止下一条建议\n恢复安全参数\n保留运行与操作日志",
    drillObservedActions: "",
    drillPassed: "false",
    drillEvidenceReference: "",
    drillEvidenceContentHash: "",
    sourceWindowId: workspace?.transferSources?.[0]?.windowId || "",
    transferResultId: workspace?.experimentResults?.[0]?.resultId || "",
    coldStartResultId: workspace?.experimentResults?.[1]?.resultId || "",
    transferNotes: "",
  };
}

export function ResearchProjectsPage() {
  const navigate = useNavigate();
  const { projectId } = useParams();
  const [projects, setProjects] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const identity = { username: "operator", userId: "operator" };
  const [createOpen, setCreateOpen] = useState(false);
  const [projectForm, setProjectForm] = useState(projectFormInitial);
  const [workspace, setWorkspace] = useState(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [task, setTask] = useState("");
  const [taskForm, setTaskForm] = useState({});
  const [shadowTarget, setShadowTarget] = useState(null);
  const [shadowForm, setShadowForm] = useState({});
  const [controlledTarget, setControlledTarget] = useState(null);
  const [controlledForm, setControlledForm] = useState({});

  const load = useCallback(async () => {
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

  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    if (!projectId) {
      setWorkspace(null);
      return;
    }
    setWorkspace(null);
    refreshWorkspace(projectId);
  }, [projectId]);

  const metrics = useMemo(() => ({
    active: projects.filter(project => project.status === "active").length,
    validating: projects.filter(project => project.status === "validating").length,
    completed: projects.filter(project => project.status === "completed").length,
  }), [projects]);

  async function refreshWorkspace(projectId = workspace?.project?.projectId) {
    if (!projectId) return;
    setDetailLoading(true);
    setError("");
    try {
      const [next, observationSummary, onlineAdmission, transferSources] = await Promise.all([
        getJson(`/api/v1/research-projects/${projectId}`),
        getJson(`/api/v1/research-projects/${projectId}/experiment-readiness`),
        getJson(`/api/v1/research-projects/${projectId}/online-admission`),
        getJson(`/api/v1/research-projects/${projectId}/transfer-sources`),
      ]);
      setWorkspace({
        ...next,
        optimizationObservationSummary: observationSummary,
        onlineAdmission,
        transferSources: transferSources?.data || [],
      });
      setProjects(current => current.map(item =>
        item.projectId === next.project.projectId ? next.project : item));
    } catch (requestError) {
      setError(requestError.message);
      notify(requestError.message, "danger");
    } finally {
      setDetailLoading(false);
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
        },
      });
      setProjects(current => [project, ...current]);
      setProjectForm(projectFormInitial);
      setCreateOpen(false);
      notify("研发项目已创建。", "success");
      openProject(project);
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

  function startShadowDecision(experiment, run) {
    setShadowTarget({ experiment, run });
    setShadowForm({
      decision: "accepted",
      actualRunKey: "",
      factors: Object.fromEntries((run.factors || []).map(factor => [factor.variableCode, factor.value])),
      rejectionReason: "",
      siteLimitations: "",
      contextSnapshot: "machine_id=\nmaterial_lot_ref=\ntooling_id=",
    });
  }

  async function submitShadowDecision(event) {
    event.preventDefault();
    if (!shadowTarget) return;
    setSaving(true);
    try {
      const contextSnapshot = Object.fromEntries(
        shadowForm.contextSnapshot.split("\n")
          .map(line => line.trim())
          .filter(Boolean)
          .map(line => {
            const separator = line.indexOf("=");
            if (separator < 1 || !line.slice(separator + 1).trim()) {
              throw new Error("上下文必须逐行填写为 key=value，且值不能为空。");
            }
            return [line.slice(0, separator).trim(), line.slice(separator + 1).trim()];
          }),
      );
      await postJson(
        `/api/v1/research-projects/experiments/${shadowTarget.experiment.experimentId}/runs/${encodeURIComponent(shadowTarget.run.runKey)}/shadow-decision`,
        {
          decision: shadowForm.decision,
          actualRunKey: shadowForm.actualRunKey,
          engineerSelectedFactors: shadowTarget.run.factors.map(factor => ({
            ...factor,
            value: Number(shadowForm.factors[factor.variableCode]),
          })),
          rejectionReason: shadowForm.rejectionReason || null,
          siteLimitations: shadowForm.siteLimitations.split("\n").map(value => value.trim()).filter(Boolean),
          contextSnapshot,
        },
      );
      setShadowTarget(null);
      await refreshWorkspace();
      notify("影子决策已预登记；模型建议不会下发设备。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    } finally {
      setSaving(false);
    }
  }

  async function materializeShadowOutcome(recommendation) {
    try {
      await postJson(
        `/api/v1/research-projects/shadow-recommendations/${recommendation.recommendationId}/materialize-outcome`,
        {},
      );
      await refreshWorkspace();
      notify("已从实际运行、参数回读和检验记录冻结影子结果。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    }
  }

  function startControlledDecision(experiment) {
    const run = experiment.runPlan?.[0];
    if (!run) return;
    setControlledTarget({ experiment, run });
    setControlledForm({
      decision: "accepted",
      reason: "",
      factors: Object.fromEntries((run.factors || []).map(factor => [factor.variableCode, factor.value])),
    });
  }

  async function submitControlledDecision(event) {
    event.preventDefault();
    if (!controlledTarget) return;
    setSaving(true);
    try {
      await postJson(
        `/api/v1/research-projects/experiments/${controlledTarget.experiment.experimentId}/controlled-decision`,
        {
          decision: controlledForm.decision,
          approvedFactors: controlledForm.decision === "rejected" ? [] :
            controlledTarget.run.factors.map(factor => ({
              ...factor,
              value: Number(controlledForm.factors[factor.variableCode]),
            })),
          reason: controlledForm.reason || null,
        },
      );
      setControlledTarget(null);
      await refreshWorkspace();
      notify("受控在线决策已冻结；建议值和工程师批准值均已保留。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    } finally {
      setSaving(false);
    }
  }

  async function runHistoricalReplay() {
    try {
      await postJson(
        `/api/v1/research-projects/${workspace.project.projectId}/historical-replays`,
        { seedCount: 30, initialObservationCount: 3 },
      );
      await refreshWorkspace();
      notify("历史项目已按生产等价模型路径完成逐次回放，报告等待独立审核。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    }
  }

  async function reviewHistoricalReplay(report) {
    try {
      await postJson(
        `/api/v1/research-projects/historical-replays/${report.reportId}/review`,
        {},
      );
      await refreshWorkspace();
      notify("历史回放原始轨迹、失败项和闸门结论已完成独立审核。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    }
  }

  async function reviewRollbackDrill(drill) {
    try {
      await postJson(
        `/api/v1/research-projects/rollback-drills/${drill.drillId}/review`,
        {},
      );
      await refreshWorkspace();
      notify("停止与回退演练已由另一名工程师复核并冻结。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    }
  }

  async function reviewTransferAssessment(assessment) {
    try {
      await postJson(
        `/api/v1/research-projects/transfer-assessments/${assessment.assessmentId}/review`,
        {},
      );
      await refreshWorkspace();
      notify("迁移评估已由另一名工程师复核；复核不等于允许自动套用源参数。", "success");
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

  async function designWindowValidation(window) {
    try {
      await postJson(
        `/api/v1/research-projects/process-windows/${window.windowId}/design-validation`,
        {},
      );
      await refreshWorkspace();
      notify("已生成三个跨区组重复运行的独立验证实验，请先审核，再按计划执行。", "success");
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

  async function generateOptimizationSuggestions(intent = "reach-specification", hypothesisId = null, mode = "experiment") {
    try {
      const optimizationShape = mode === "controlled"
        ? { batchSize: 1, replicatesPerCondition: 1 }
        : { batchSize: 2, replicatesPerCondition: 2 };
      const experiment = await postJson(
        `/api/v1/research-projects/${workspace.project.projectId}/optimize`,
        {
          // A single point cannot distinguish a process effect from run noise.
          // Keep the smallest useful industrial experiment as a two-condition batch.
          ...optimizationShape,
          seed: 0,
          intent,
          mode,
          hypothesisId,
          autoAssembleObservations: true,
        },
      );
      const alreadyActive = workspace.experiments.some(
        item => item.experimentId === experiment.experimentId,
      );
      await refreshWorkspace();
      notify(
        alreadyActive
          ? mode === "shadow"
            ? "已返回尚未登记完的影子建议，系统没有重复生成建议。"
            : mode === "controlled"
              ? "已返回尚未决策的受控在线建议，没有生成第二条。"
            : "上一批优化实验尚未形成完整观察，已返回原实验，系统没有重复生成配方。"
          : mode === "shadow"
            ? "已生成旁路影子建议；它不能批准或下发，请登记工程师实际选择。"
            : mode === "controlled"
              ? "已生成一条受控在线建议；必须先由现场工程师接受、修改或拒绝。"
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
          baselineRunKeys: taskForm.baselineRunKeys,
          objectiveCodes: [objective.code],
          replicateKeys: ["replicate-1"],
          stopRule: taskForm.stopRule,
          rollbackPlan: taskForm.rollbackPlan,
        });
      } else if (task === "history") {
        await postJson(`/api/v1/research-projects/${project.projectId}/experiments/import-history`, {
          cycleIds: taskForm.cycleIds,
        });
      } else if (task === "claim") {
        await postJson(`/api/v1/research-projects/${project.projectId}/knowledge-claims`, {
          processWindowId: taskForm.knowledgeSourceType === "window" ? taskForm.processWindowId : null,
          transferAssessmentId: taskForm.knowledgeSourceType === "transfer" ? taskForm.transferAssessmentId : null,
          statement: taskForm.statement,
          applicability: taskForm.applicability,
        });
      } else if (task === "rollback-drill") {
        await postJson(`/api/v1/research-projects/${project.projectId}/rollback-drills`, {
          name: taskForm.drillName,
          scenario: taskForm.drillScenario,
          stopTrigger: taskForm.drillStopTrigger,
          rollbackTarget: taskForm.drillRollbackTarget,
          expectedActions: taskForm.drillExpectedActions.split("\n").map(value => value.trim()).filter(Boolean),
          observedActions: taskForm.drillObservedActions.split("\n").map(value => value.trim()).filter(Boolean),
          passed: taskForm.drillPassed === "true",
          evidenceReference: taskForm.drillEvidenceReference,
          evidenceContentHash: taskForm.drillEvidenceContentHash,
          conductedAt: new Date().toISOString(),
        });
      } else if (task === "transfer") {
        await postJson(`/api/v1/research-projects/${project.projectId}/transfer-assessments`, {
          sourceWindowId: taskForm.sourceWindowId,
          transferResultId: taskForm.transferResultId,
          coldStartResultId: taskForm.coldStartResultId,
          notes: taskForm.transferNotes || null,
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

  if (projectId) {
    const project = workspace?.project;
    const projectAction = project ? nextProjectAction(project.status) : null;
    return (
      <Page
        title={project?.name || "优化项目工作区"}
        description={project?.description || "围绕当前问题推进假设、实验、验证和知识复用。"}
        actions={(
          <>
            <Button onClick={() => navigate("/research-projects")}>返回项目列表</Button>
            {projectAction && (
              <Button
                variant="primary"
                disabled={detailLoading}
                onClick={() => changeProjectStatus(projectAction[1])}
              >
                {projectAction[0]}
              </Button>
            )}
          </>
        )}
      >
        {error && <Alert tone="danger">{error}</Alert>}
        {!workspace ? (
          <Card>
            <p className="py-16 text-center text-sm text-slate-500">
              {detailLoading ? "正在读取项目工作区…" : "无法读取当前优化项目。"}
            </p>
          </Card>
        ) : (
          <WorkspaceContent
            workspace={workspace}
            loading={detailLoading}
            onTask={startTask}
            onExperimentStatus={changeExperimentStatus}
            onMaterializeExperimentResult={materializeExperimentResult}
            onDesignWindowValidation={designWindowValidation}
            onValidateWindow={validateWindow}
            onReleaseWindow={releaseWindow}
            onReviewClaim={reviewClaim}
            onGenerateOptimizationSuggestions={generateOptimizationSuggestions}
            onShadowDecision={startShadowDecision}
            onControlledDecision={startControlledDecision}
            onMaterializeShadowOutcome={materializeShadowOutcome}
            onRunHistoricalReplay={runHistoricalReplay}
            onReviewHistoricalReplay={reviewHistoricalReplay}
            onReviewRollbackDrill={reviewRollbackDrill}
            onReviewTransferAssessment={reviewTransferAssessment}
            onAskAi={currentProjectId => navigate(`/chat?projectId=${encodeURIComponent(currentProjectId)}`)}
            currentUserId={identity?.username || identity?.userId || ""}
          />
        )}
        <TaskDrawer
          task={task}
          form={taskForm}
          setForm={setTaskForm}
          workspace={workspace}
          saving={saving}
          onClose={() => !saving && setTask("")}
          onSubmit={submitTask}
        />
        <ShadowDecisionDrawer
          target={shadowTarget}
          form={shadowForm}
          setForm={setShadowForm}
          saving={saving}
          variables={workspace?.project?.variables || []}
          onClose={() => !saving && setShadowTarget(null)}
          onSubmit={submitShadowDecision}
        />
        <ControlledDecisionDrawer
          target={controlledTarget}
          form={controlledForm}
          setForm={setControlledForm}
          saving={saving}
          variables={workspace?.project?.variables || []}
          onClose={() => !saving && setControlledTarget(null)}
          onSubmit={submitControlledDecision}
        />
      </Page>
    );
  }

  return (
    <Page
      title="优化项目"
      description="用最少的有效实验，把生产问题追溯为可验证证据，再形成可复用的工艺窗口。"
      actions={<Button variant="primary" onClick={() => setCreateOpen(true)}>新建优化项目</Button>}
    >
      {error && <Alert tone="danger">{error}</Alert>}
      <section className="grid gap-3 sm:grid-cols-3">
        <Metric label="进行中的优化" value={metrics.active + metrics.validating} hint="需要工程决策或独立验证" />
        <Metric label="已验证结论" value={metrics.completed} hint="已完成项目" />
        <Metric label="项目组合" value={projects.length} hint="当前可访问项目" />
      </section>

      <Card
        title="优化项目"
        description="先处理需要决策或验证的项目；每个工作区保留完整证据链。"
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

      <section className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="rounded-2xl border border-blue-100 bg-gradient-to-br from-blue-50 via-white to-white p-5 shadow-sm">
          <p className="text-sm font-semibold text-blue-700">从真实偏差进入优化闭环</p>
          <h2 className="mt-2 text-lg font-semibold tracking-tight text-slate-950">
            发现偏差 → 找到原因 → 设计实验 → 验证并固化窗口
          </h2>
          <p className="mt-2 max-w-3xl text-sm leading-6 text-slate-600">
            运行、质量和设备数据是证据来源；系统负责整理证据与下一步，工程人员负责审核和决策。
          </p>
          <div className="mt-4 flex flex-wrap gap-2">
            <Button onClick={() => navigate("/comparisons")}>从运行对比开始</Button>
            <Button onClick={() => navigate("/quality-analysis")}>查看质量偏差</Button>
          </div>
        </div>
        <aside>
          <WorkflowGuide
            title={projects.length ? "优化闭环" : "第一次使用：只需走完这四步"}
            description={projects.length ? "当前项目会沿同一证据路径推进。" : "不需要先配置所有数据和模型；先围绕一个真实问题建立闭环。"}
            compact
            steps={[
              { title: "明确偏差", description: "从质量追因或历史对比确认问题和范围。", state: projects.length ? "done" : "current" },
              { title: "设定边界", description: "写下优化目标、一个可控变量和安全限制。", state: projects.length ? "current" : "upcoming" },
              { title: "执行建议", description: "系统依据已有观察推荐下一组实验。", state: "upcoming" },
              { title: "验证窗口", description: "独立验证后才成为可复用结论。", state: "upcoming" },
            ]}
          />
        </aside>
      </section>

      <CreateProjectDrawer
        open={createOpen}
        saving={saving}
        form={projectForm}
        setForm={setProjectForm}
        onClose={() => !saving && setCreateOpen(false)}
        onSubmit={createProject}
      />

    </Page>
  );
}

function CreateProjectDrawer({ open, saving, form, setForm, onClose, onSubmit }) {
  const [catalog, setCatalog] = useState({ cycles: [], definitions: [], models: [], scenarios: [] });
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
      getJson("/api/v1/scenario-packages"),
    ]).then(([cycles, definitions, models, scenarios]) => {
      if (!mounted) return;
      setCatalog({
        cycles: cycles?.data || [],
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
        {catalogLoading && <Alert tone="info">正在读取已完成运行、工艺配置、检测定义和工艺数据模型…</Alert>}
        <Card title="1. 项目范围" description="先确定问题属于哪个工艺和产品范围。">
          <div className="grid gap-4 md:grid-cols-2">
            <Field label="项目名称"><Input required value={form.name} onChange={field("name")} placeholder="光学模压工艺窗口研发" /></Field>
            <Field label="参考运行" hint="选择后自动带入产品范围；不影响后续用更多运行形成证据。"><Select value={form.referenceCycleId} onChange={event => chooseReferenceCycle(event.target.value)}><option value="">暂不关联历史运行</option>{catalog.cycles.map(cycle => <option key={cycle.correlationId} value={cycle.correlationId}>{cycleLabel(cycle)}</option>)}</Select></Field>
            <Field label="工艺配置（推荐）" hint="只允许选择不可变的已发布版本；其中 required-for-analysis 字段会成为优化准入条件。"><Select value={form.scenarioPackageKey} onChange={event => chooseScenarioPackage(event.target.value)}><option value="">暂不使用工艺配置</option>{selectableScenarios.map(item => <option key={`${item.packageId}:${item.version}`} value={`${item.packageId}:${item.version}`}>{item.name} · v{item.version}</option>)}</Select></Field>
            <Field label="工艺数据模型" hint="决定可选的配方参数与实际数据来源。"><Select required value={form.dataModelKey} onChange={event => chooseDataModel(event.target.value)}><option value="">选择已配置的工艺数据模型</option>{selectableModels.map(model => <option key={`${model.modelId}:${model.version}`} value={`${model.modelId}:${model.version}`}>{model.name} · v{model.version}</option>)}</Select></Field>
            <Field label="目标产品" hint="来自参考运行；未关联时可补充产品编号。"><Input value={form.productName} onChange={field("productName")} placeholder="产品编号（可选）" /></Field>
            <Field label="材料"><Input value={form.materialName} onChange={field("materialName")} /></Field>
            <Field label="项目说明" className="md:col-span-2"><Textarea value={form.description} onChange={field("description")} rows={3} /></Field>
          </div>
        </Card>
        <Card title="2. 首要研发目标" description="选择要改善的质量指标及判定方向。">
          <div className="grid gap-4 md:grid-cols-2">
            <Field label="质量指标" hint="从已发布的检测定义中选择，代码、单位和数据来源会自动带入。"><Select required value={form.objectiveKey} onChange={event => chooseObjective(event.target.value)}><option value="">选择数值型检测指标</option>{objectiveOptions.map(option => <option key={option.key} value={option.key}>{option.definition.name} · {option.characteristic.name}{option.characteristic.unit ? ` (${option.characteristic.unit})` : ""}</option>)}</Select></Field>
            <Field label="数据来源"><Input readOnly value={form.objectiveDataSource} placeholder="选择质量指标后自动带入" className="bg-slate-50 text-slate-600" /></Field>
            <Field label="优化方向"><Select value={form.objectiveDirection} onChange={field("objectiveDirection")}><option value="minimize">越低越好</option><option value="maximize">越高越好</option><option value="target">接近目标</option><option value="range">保持范围</option></Select></Field>
            <Field label="指标单位"><Input readOnly required value={form.objectiveUnit} placeholder="自动带入" className="bg-slate-50 text-slate-600" /></Field>
            <Field label="目标值" hint="来自检测上下限的建议值，可按研发规格调整。"><Input required type="number" step="any" value={form.objectiveTarget} onChange={field("objectiveTarget")} /></Field>
            <Field label="目标权重"><Input required type="number" min="0.01" step="any" value={form.objectiveWeight} onChange={field("objectiveWeight")} /></Field>
          </div>
        </Card>
        <Card title="3. 首个可控变量" description="定义第一轮实验允许调整的参数范围。">
          <div className="grid gap-4 md:grid-cols-2">
            <Field label="可控配方参数" hint={selectedModel ? "从所选工艺数据模型中选择。" : "请先选择工艺数据模型。"}><Select required disabled={!selectedModel} value={form.variableCode} onChange={event => chooseVariable(event.target.value)}><option value="">选择可控配方参数</option>{(selectedModel?.recipeParameters || []).map(parameter => <option key={parameter.code} value={parameter.code}>{parameter.sourceField || parameter.code}{parameter.unit ? ` (${parameter.unit})` : ""}</option>)}</Select></Field>
            <Field label="实际数据来源"><Input readOnly value={form.variableDataSource} placeholder="选择配方参数后自动带入" className="bg-slate-50 text-slate-600" /></Field>
            <Field label="变量单位"><Input readOnly required value={form.variableUnit} placeholder="自动带入" className="bg-slate-50 text-slate-600" /></Field>
            <Field label="允许下限" hint="这是实验允许范围，请按设备/安全规范确认。"><Input required type="number" step="any" value={form.variableLower} onChange={field("variableLower")} /></Field>
            <Field label="允许上限" hint="这是实验允许范围，请按设备/安全规范确认。"><Input required type="number" step="any" value={form.variableUpper} onChange={field("variableUpper")} /></Field>
          </div>
        </Card>
        <Card title="4. 结果安全边界（可选）" description="例如裂纹率、破损率或粘模指标；优化器只推荐达到最低安全概率的配方。">
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

function WorkspaceContent({
  workspace,
  loading,
  onTask,
  onExperimentStatus,
  onMaterializeExperimentResult,
  onDesignWindowValidation,
  onValidateWindow,
  onReleaseWindow,
  onReviewClaim,
  onGenerateOptimizationSuggestions,
  onShadowDecision,
  onControlledDecision,
  onMaterializeShadowOutcome,
  onRunHistoricalReplay,
  onReviewHistoricalReplay,
  onReviewRollbackDrill,
  onReviewTransferAssessment,
  onAskAi,
  currentUserId,
}) {
  if (!workspace) return null;
  const {
    project,
    hypotheses = [],
    experiments = [],
    experimentResults = [],
    shadowRecommendations = [],
    shadowReport,
    historicalReplayReports = [],
    rollbackDrills = [],
    onlineReport,
    processWindows = [],
    knowledgeClaims = [],
    transferAssessments = [],
  } = workspace;
  const onlineAdmission = workspace.onlineAdmission;
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
    { id: "project-definition", title: "定义", description: "目标与边界", state: project.status === "draft" ? "current" : "done" },
    { id: "project-diagnosis", title: "追因", description: "假设与证据", state: hypotheses.length ? "done" : project.status === "draft" ? "upcoming" : "current" },
    { id: "project-experiments", title: "实验", description: "建议与执行", state: experiments.length ? "done" : hypotheses.length ? "current" : "upcoming" },
    { id: "project-validation", title: "验证", description: "结果与窗口", state: validatedWindows.length ? "done" : experimentResults.length ? "current" : "upcoming" },
    { id: "project-reuse", title: "复用", description: "知识与迁移", state: knowledgeClaims.some(item => item.status === "reviewed") ? "done" : validatedWindows.length ? "current" : "upcoming" },
  ];
  return (
    <div className="space-y-5">
        {loading && <Alert tone="info">正在更新项目证据与准备度…</Alert>}
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
              {project.status !== "draft" && canEdit && hypotheses.length > 0 && <Button onClick={() => onGenerateOptimizationSuggestions("reach-specification", null, "shadow")}>生成影子建议</Button>}
              {project.status !== "draft" && canEdit && hypotheses.length > 0 && (
                <Button
                  variant="primary"
                  disabled={!onlineAdmission?.eligible || hasRunningExperiment}
                  title={!onlineAdmission?.eligible ? (onlineAdmission?.failures || []).join("；") : "每次只生成一条，仍需工程师逐条确认"}
                  onClick={() => onGenerateOptimizationSuggestions("reach-specification", null, "controlled")}
                >生成一条受控在线建议</Button>
              )}
              {project.status !== "draft" && canEdit && Number(observationSummary?.validObservationCount || 0) >= 3 && <Button onClick={onRunHistoricalReplay}>运行历史回放</Button>}
              {project.status !== "draft" && canEdit && <Button onClick={() => onTask("rollback-drill")}>记录停止与回退演练</Button>}
              {project.status !== "draft" && canEdit && (workspace.transferSources || []).length > 0 && experimentResults.length >= 2 && <Button onClick={() => onTask("transfer")}>评估迁移收益</Button>}
              {project.status !== "draft" && canEdit && hypotheses.length > 0 && <Button onClick={() => onTask("experiment")}>手动设计实验</Button>}
              {project.status !== "draft" && canEdit && hasRunningExperiment && <Button onClick={() => onMaterializeExperimentResult(experiments.find(item => item.status === "running"))}>立即检查数据回收</Button>}
              {canEdit && validatedWindows.length > 0 && <Button variant="primary" onClick={() => onTask("claim")}>沉淀工艺知识</Button>}
            </div>
            <div className="rounded-xl border border-white/80 bg-white/80 p-4">
              <p className="text-sm font-semibold text-slate-900">优化模型准备度</p>
              <p className="mt-1 text-2xl font-semibold text-slate-950">{observationSummary?.validObservationCount ?? 0}<span className="ml-1 text-sm font-normal text-slate-500">条有效观察</span></p>
              <p className="mt-1 text-xs leading-5 text-slate-500">{hasObservation ? `已匹配 ${observationSummary?.candidateRunCount ?? 0} 个实验运行，可用于生成下一组建议。` : "尚无可用观察；完成运行、过程特征和检验结果的关联后自动具备条件。"}</p>
            </div>
          </div>
        </section>

        <nav className="sticky top-34 z-10 grid gap-2 rounded-2xl border border-slate-200 bg-white/95 p-2 shadow-sm backdrop-blur sm:grid-cols-5" aria-label="项目推进阶段">
          {workflowSteps.map((step, index) => (
            <a
              key={step.id}
              href={`#${step.id}`}
              className={[
                "flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm transition hover:bg-slate-50",
                step.state === "current" ? "bg-blue-50 text-blue-800 ring-1 ring-blue-200" : "text-slate-600",
              ].join(" ")}
            >
              <span className={[
                "grid size-7 shrink-0 place-items-center rounded-full text-xs font-semibold",
                step.state === "done"
                  ? "bg-emerald-100 text-emerald-700"
                  : step.state === "current"
                    ? "bg-blue-600 text-white"
                    : "bg-slate-100 text-slate-500",
              ].join(" ")}>
                {step.state === "done" ? "✓" : index + 1}
              </span>
              <span className="min-w-0">
                <strong className="block text-slate-900">{step.title}</strong>
                <span className="block truncate text-xs">{step.description}</span>
              </span>
            </a>
          ))}
        </nav>

        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <Metric label="研发假设" value={hypotheses.length} hint="待验证的规律" />
          <Metric label="实验计划" value={experiments.length} hint="设计与执行记录" />
          <Metric label="可用于优化" value={observationSummary?.validObservationCount ?? 0} hint="参数、过程与结果已关联" />
          <Metric label="已验证窗口" value={validatedWindows.length} hint={`${reviewedWindows.length} 个窗口已完成复核`} />
        </div>
        {observationSummary?.excludedObservationCount > 0 && (
          <Alert tone="warning">
            有 {observationSummary.excludedObservationCount} 条运行因缺少检验值、过程特征或完整运行边界而未进入优化模型。
          </Alert>
        )}

        <div id="project-definition" className="scroll-mt-60">
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
        </div>

        <div id="project-diagnosis" className="scroll-mt-60">
        <Card title="假设">
          {hypotheses.length === 0 ? <EmptyState title="尚未提出假设" description="先说明变量为什么可能影响研发目标。" /> : (
            <DataTable rows={hypotheses} keyField="hypothesisId" columns={[
              { key: "statement", label: "假设" },
              { key: "rationale", label: "依据" },
              { key: "status", label: "状态", render: value => <StatusBadge value={value === "validated" ? "已验证原因" : statusLabels[value] || value} /> },
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
        </div>

        <div id="project-experiments" className="scroll-mt-60">
        <Card title="实验">
          {experiments.length === 0 ? <EmptyState title="尚未设计实验" description="实验必须包含至少两个不同运行条件。" /> : (
            <DataTable rows={experiments} keyField="experimentId" columns={[
              {
                key: "name",
                label: "实验",
                render: (value, row) => {
                  const scale = row.optimization ? experimentScale(row) : null;
                  const size = scale
                    ? `${scale.distinctConditions} 个条件 × ${scale.replicates} 次重复`
                    : `${row.runPlan?.length || 0} 次运行`;
                  return (
                    <div className="min-w-44">
                      <strong className="text-slate-900">{value}</strong>
                      <p className="mt-1 text-xs text-slate-500">{row.designMethod} · {size}</p>
                    </div>
                  );
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
                        {row.optimization?.mode === "shadow" && !shadowRecommendations.some(item =>
                          item.experimentId === row.experimentId && item.suggestionRunKey === run.runKey) && canEdit && (
                          <Button onClick={event => { event.stopPropagation(); onShadowDecision(row, run); }}>
                            登记影子选择
                          </Button>
                        )}
                        {shadowRecommendations.some(item =>
                          item.experimentId === row.experimentId && item.suggestionRunKey === run.runKey) && (
                          <div className="mt-2">
                            <StatusBadge value={shadowDecisionLabels[shadowRecommendations.find(item =>
                              item.experimentId === row.experimentId && item.suggestionRunKey === run.runKey)?.decision] || "已登记影子决策"} />
                          </div>
                        )}
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
              {
                key: "status",
                label: "进展",
                render: (value, row) => {
                  const isHistorical = row.designMethod === "historical-observation";
                  return (
                    <div className="min-w-32 space-y-1 text-xs">
                      <StatusBadge value={isHistorical ? "已导入" : statusLabels[row.execution?.state] || statusLabels[value] || row.execution?.state || value} />
                      {!isHistorical && (
                        <p className="text-slate-500">
                          {row.execution?.commands?.length || row.runPlan?.length || 0} 条设备无关执行指令
                        </p>
                      )}
                      <p className="text-slate-500">{row.resultIds?.length || 0} 份结果</p>
                    </div>
                  );
                },
              },
              {
                key: "actions",
                label: "操作",
                render: (_, row) => (
                  <div className="flex gap-2">
                    {row.designMethod === "historical-observation" && <span className="text-xs text-slate-500">只读证据</span>}
                    {row.optimization?.mode === "shadow" && <span className="text-xs text-slate-500">旁路评估，不可下发</span>}
                    {row.optimization?.mode === "controlled" && row.status === "planned" && !row.controlledDecision && row.createdBy !== currentUserId && <Button onClick={event => { event.stopPropagation(); onControlledDecision(row); }}>接受 / 修改 / 拒绝</Button>}
                    {row.optimization?.mode === "controlled" && row.status === "planned" && !row.controlledDecision && row.createdBy === currentUserId && <span className="text-xs text-slate-500">等待现场工程师决策</span>}
                    {row.designMethod !== "historical-observation" && row.optimization?.mode !== "shadow" && row.optimization?.mode !== "controlled" && row.status === "planned" && row.createdBy !== currentUserId && <Button onClick={event => { event.stopPropagation(); onExperimentStatus(row, "approved"); }}>批准</Button>}
                    {row.designMethod !== "historical-observation" && row.optimization?.mode !== "shadow" && row.optimization?.mode !== "controlled" && row.status === "planned" && row.createdBy === currentUserId && <span className="text-xs text-slate-500">等待其他成员批准</span>}
                    {row.optimization?.mode === "controlled" && row.status === "planned" && row.controlledDecision && row.createdBy !== currentUserId && <Button onClick={event => { event.stopPropagation(); onExperimentStatus(row, "approved"); }}>批准本次运行</Button>}
                    {row.optimization?.mode === "controlled" && row.controlledDecision && <span className="text-xs text-slate-500">{row.controlledDecision.decision === "modified" ? "已修改" : row.controlledDecision.decision === "rejected" ? "已拒绝" : "已接受"}，决策已冻结</span>}
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
        <ShadowEvidenceCard
          recommendations={shadowRecommendations}
          report={shadowReport}
          variableByCode={variableByCode}
          objectiveByCode={objectiveByCode}
          onMaterialize={onMaterializeShadowOutcome}
        />
        <HistoricalReplayCard
          reports={historicalReplayReports}
          currentUserId={currentUserId}
          onReview={onReviewHistoricalReplay}
        />
        <OnlineAdmissionCard evidence={onlineAdmission} />
        <OnlineCampaignCard report={onlineReport} objectiveByCode={objectiveByCode} />
        <RollbackDrillCard
          drills={rollbackDrills}
          currentUserId={currentUserId}
          onReview={onReviewRollbackDrill}
        />
        </div>

        <section id="project-validation" className="scroll-mt-60 space-y-5">
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
                      const reached = objective?.direction === "range"
                        || objective?.direction === "target"
                        ? objective?.lowerLimit != null && objective?.upperLimit != null
                          && metric.observedValue >= objective.lowerLimit
                          && metric.observedValue <= objective.upperLimit
                        : Number.isFinite(Number(target))
                          && (objective?.direction === "maximize"
                            ? metric.observedValue >= target
                            : metric.observedValue <= target);
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
                          </div>
                          <div className="mt-1 text-slate-500">
                            基线 {formatResearchNumber(metric.baselineValue)}，效果量 {formatResearchNumber(metric.effectValue)}
                            {metric.lowerConfidenceBound != null && metric.upperConfidenceBound != null
                              ? `（95% 效果区间 ${formatResearchNumber(metric.lowerConfidenceBound)} ～ ${formatResearchNumber(metric.upperConfidenceBound)}）`
                              : "（未声明至少两个独立对照运行，暂不计算效果区间）"}
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

        <Card
          title="候选设置与已验证窗口"
          description="优化先产生经重复实测的候选设置点；连续范围必须另做边界和交互作用实验。"
        >
          {processWindows.length === 0 ? <EmptyState title="尚未形成候选设置" description="完成优化实验后，系统会从同一条件的重复源数据中自动形成候选设置。" /> : (
            <DataTable rows={processWindows} keyField="windowId" columns={[
              { key: "name", label: "窗口" },
              {
                key: "variables",
                label: "设置 / 范围",
                render: value => (
                  <div className="space-y-1">
                    {(value || []).map(variable => {
                      const definition = variableByCode.get(variable.variableCode);
                      return (
                        <div key={variable.variableCode} className="text-xs">
                          {definition?.name || variable.variableCode}：
                          <strong className="ml-1">
                            {variable.lowerBound === variable.upperBound
                              ? formatResearchNumber(variable.lowerBound)
                              : `${formatResearchNumber(variable.lowerBound)} ～ ${formatResearchNumber(variable.upperBound)}`}{" "}
                            {variable.unit || definition?.unit || ""}
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
                  if (row.status === "candidate") {
                    if (project.status !== "validating") {
                      return <span className="text-xs text-slate-500">先将项目推进到验证阶段</span>;
                    }
                    const validationExperiment = experiments.find(
                      experiment => experiment.validationWindowId === row.windowId
                        && experiment.status !== "cancelled",
                    );
                    if (!validationExperiment) {
                      return (
                        <Button onClick={event => {
                          event.stopPropagation();
                          onDesignWindowValidation(row);
                        }}>
                          设计独立验证实验
                        </Button>
                      );
                    }
                    if (validationExperiment.status !== "completed") {
                      return (
                        <span className="text-xs text-blue-700">
                          验证实验：{statusLabels[validationExperiment.status] || validationExperiment.status}
                        </span>
                      );
                    }
                    const resultAttached = (row.supportingExperimentIds || [])
                      .includes(validationExperiment.experimentId);
                    if (!resultAttached) {
                      return <span className="text-xs text-amber-700">等待验证结果计算</span>;
                    }
                    if (row.createdBy !== currentUserId) {
                      return (
                        <Button onClick={event => {
                          event.stopPropagation();
                          onValidateWindow(row);
                        }}>
                          审核独立验证结果
                        </Button>
                      );
                    }
                    return <span className="text-xs text-slate-500">等待其他成员审核验证结果</span>;
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
        </section>

        <div id="project-reuse" className="scroll-mt-60 space-y-5">
        <Card
          title="跨条件迁移评估"
          description="将源工艺窗口在当前项目的实测结果，与当前项目从零建立的独立对照比较；这里只形成证据，不自动套用参数。"
        >
          {transferAssessments.length === 0 ? (
            <EmptyState title="尚未评估迁移" description="先在目标项目中完成迁移组和从零对照组，且每组至少三个重复、两个区组。" />
          ) : (
            <DataTable rows={transferAssessments} keyField="assessmentId" columns={[
              {
                key: "outcome",
                label: "结论",
                render: (value, row) => <div className="space-y-1"><StatusBadge value={statusLabels[value] || value} /><div className="text-xs text-slate-500">{statusLabels[row.status] || row.status}</div></div>,
              },
              {
                key: "relativeGain",
                label: "相对从零收益",
                render: value => value == null ? "—" : `${formatResearchNumber(Number(value) * 100)}%`,
              },
              {
                key: "evidence",
                label: "证据门禁",
                render: (_, row) => <div className="text-xs leading-5">结构与单位：{row.schemaCompatible ? "通过" : "失败"}<br />重复与区组：{row.evidenceSufficient ? "通过" : "不足"}<br />安全：{row.safetyPassed ? "通过" : "失败"}</div>,
              },
              {
                key: "contextDifferences",
                label: "变化条件",
                render: value => <div className="max-w-72 text-xs leading-5">{(value || []).map(item => <div key={item.field}>{item.field}：{item.sourceValue || "未声明"} → {item.targetValue || "未声明"}</div>)}</div>,
              },
              {
                key: "failures",
                label: "失败与边界",
                render: (value, row) => <div className="max-w-96 text-xs leading-5 text-slate-600">{(value || []).map(item => <div key={item}>失败：{item}</div>)}{(row.warnings || []).map(item => <div key={item}>提示：{item}</div>)}</div>,
              },
              {
                key: "actions",
                label: "操作",
                render: (_, row) => row.status === "recorded" && row.createdBy !== currentUserId
                  ? <Button onClick={event => { event.stopPropagation(); onReviewTransferAssessment(row); }}>独立复核</Button>
                  : row.status === "recorded" ? <span className="text-xs text-slate-500">等待其他成员复核</span> : "—",
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
    </div>
  );
}

function HistoricalReplayCard({ reports, currentUserId, onReview }) {
  return (
    <Card
      title="生产等价历史回放"
      description="只在真实跑过的唯一配方候选池内逐次选择；完整保留原顺序、优化器、随机对照、校准、安全事件和失败闸门。"
    >
      {reports.length === 0 ? (
        <EmptyState title="尚未生成历史回放报告" description="至少积累 3 种不同的完整实际配方条件；5 种以上才具备通过探索性闸门的可能。" />
      ) : (
        <DataTable rows={reports} keyField="reportId" columns={[
          {
            key: "status",
            label: "状态",
            render: (value, row) => <div className="space-y-2 text-xs"><StatusBadge value={value === "reviewed" ? "已独立审核" : "待独立审核"} /><StatusBadge value={row.gatePassed ? "回放闸门通过" : "回放闸门未通过"} /><code title={row.reportHash}>{String(row.reportHash).slice(0, 12)}…</code></div>,
          },
          {
            key: "conditions",
            label: "冻结数据",
            render: (_, row) => <div className="text-xs leading-5">{row.sourceRunCount} 条运行<br />{row.uniqueConditionCount} 种唯一条件<br />预算 {row.budget} · {row.seedCount} 个随机种子</div>,
          },
          {
            key: "comparison",
            label: "达到规格试验数",
            render: (_, row) => <div className="min-w-52 text-xs leading-5">历史原顺序：<strong>{row.originalOrderTrials ?? "未达到"}</strong><br />优化器中位数：<strong>{row.optimizer?.medianTrials ?? "未达到"}</strong>（成功率 {Math.round(Number(row.optimizer?.successRate || 0) * 100)}%）<br />随机中位数：<strong>{row.random?.medianTrials ?? "未达到"}</strong>（成功率 {Math.round(Number(row.random?.successRate || 0) * 100)}%）</div>,
          },
          {
            key: "calibration",
            label: "校准 / 安全",
            render: (_, row) => <div className="text-xs leading-5">区间覆盖：<strong>{row.predictionIntervalChecks ? `${Math.round(Number(row.predictionIntervalCoverage || 0) * 100)}%` : "无检查"}</strong><br />覆盖检查：{row.predictionIntervalChecks}<br />优化器安全违规：<strong>{row.optimizerSafetyViolationCount}</strong></div>,
          },
          {
            key: "gateFailures",
            label: "失败与限制",
            render: (value, row) => <div className="max-w-96 text-xs leading-5 text-slate-600">{(value || []).map(item => <div key={item}>失败：{item}</div>)}<div>限制：{row.limitations}</div></div>,
          },
          {
            key: "actions",
            label: "操作",
            render: (_, row) => row.status === "generated" && row.generatedBy !== currentUserId
              ? <Button onClick={event => { event.stopPropagation(); onReview(row); }}>审核完整报告</Button>
              : row.status === "generated" ? <span className="text-xs text-slate-500">等待其他工程师审核</span> : "已冻结",
          },
        ]} />
      )}
    </Card>
  );
}

function OnlineAdmissionCard({ evidence }) {
  if (!evidence) return null;
  return (
    <Card
      title="受控在线准入"
      description="通过只代表系统可以提出一条候选建议；它不授权自动写设备，仍须现场工程师逐条确认。"
    >
      <div className="grid gap-3 sm:grid-cols-3">
        <Metric label="当前结论" value={evidence.eligible ? "允许单条建议" : "禁止进入在线"} hint="任何门禁失败均按失败关闭" />
        <Metric label="有效影子结果" value={evidence.validShadowOutcomeCount || 0} hint={`共 ${evidence.shadowRecommendationCount || 0} 条影子建议，最低要求 5 条有效结果`} />
        <Metric label="证据快照" value={evidence.historicalReplayReportId && evidence.rollbackDrillId ? "回放与演练已审核" : "前置证据未通过"} hint={evidence.shadowReportHash ? `影子报告 ${String(evidence.shadowReportHash).slice(0, 12)}…` : "尚无影子报告"} />
      </div>
      {(evidence.failures || []).length > 0 && (
        <Alert tone="danger" title="在线门禁未通过">
          {(evidence.failures || []).map(item => <div key={item}>{item}</div>)}
        </Alert>
      )}
      {(evidence.warnings || []).length > 0 && (
        <Alert tone="warning" title="运行前必须确认">
          {(evidence.warnings || []).map(item => <div key={item}>{item}</div>)}
        </Alert>
      )}
    </Card>
  );
}

function RollbackDrillCard({ drills, currentUserId, onReview }) {
  return (
    <Card
      title="停止与回退演练"
      description="受控在线前必须实际演练停止建议、恢复安全参数和保留证据；提交后不可修改，并由另一名工程师复核。"
    >
      {drills.length === 0 ? (
        <EmptyState title="尚无回退演练证据" description="纸面回退方案不能放行受控在线；请执行一次可复核的现场或等价环境演练。" />
      ) : (
        <DataTable rows={drills} keyField="drillId" columns={[
          { key: "name", label: "演练", render: (value, row) => <div className="max-w-72 text-xs leading-5"><strong>{value}</strong><div>{row.scenario}</div><code>{String(row.recordHash).slice(0, 12)}…</code></div> },
          { key: "trigger", label: "停止 / 回退", render: (_, row) => <div className="max-w-80 text-xs leading-5">触发：{row.stopTrigger}<br />回退：{row.rollbackTarget}</div> },
          { key: "evidence", label: "实际证据", render: (_, row) => <div className="max-w-72 text-xs leading-5">{(row.observedActions || []).map(value => <div key={value}>· {value}</div>)}<div>{row.evidenceReference}</div></div> },
          { key: "status", label: "结论", render: (value, row) => <div className="space-y-1 text-xs"><StatusBadge value={row.passed ? "演练通过" : "演练失败"} /><StatusBadge value={value === "reviewed" ? "已独立复核" : "待独立复核"} /></div> },
          { key: "actions", label: "操作", render: (_, row) => row.status === "recorded" && row.conductedBy !== currentUserId ? <Button onClick={event => { event.stopPropagation(); onReview(row); }}>复核演练证据</Button> : row.status === "recorded" ? <span className="text-xs text-slate-500">等待其他工程师复核</span> : "已冻结" },
        ]} />
      )}
    </Card>
  );
}

function OnlineCampaignCard({ report, objectiveByCode }) {
  if (!report || report.totalSuggestions === 0) return null;
  return (
    <Card
      title="受控在线监控"
      description="持续比较建议、批准值、实际设置和实测结果；在线与影子残差的差异只作为停止与复核信号，不解释为因果。"
    >
      {report.stopRecommended && (
        <Alert tone="danger" title="已停止生成下一条建议">
          {(report.stopSignals || []).filter(item => item.severity === "stop").map(item => <div key={item.code}>{item.reason}</div>)}
        </Alert>
      )}
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Metric label="在线建议" value={report.totalSuggestions} hint={`${report.acceptedCount} 接受 / ${report.modifiedCount} 修改 / ${report.rejectedCount} 拒绝`} />
        <Metric label="有效结果" value={report.validOutcomeCount} hint={`${report.completedResultCount} 份结果 · ${report.runningCount} 条执行中`} />
        <Metric label="安全违规" value={report.safetyViolationCount} hint="任何一次都会阻止下一条建议" />
        <Metric label="实际设置偏差" value={report.settingDeviationCount} hint={`报告 ${String(report.reportHash).slice(0, 12)}…`} />
      </div>
      <DataTable rows={report.shadowComparisons || []} keyField="objectiveCode" columns={[
        { key: "objectiveCode", label: "目标", render: value => objectiveByCode.get(value)?.name || value },
        { key: "shadow", label: "影子残差", render: (_, row) => <span className="text-xs">n={row.shadowCount} · 均值 {formatResearchNumber(row.shadowMeanResidual)}</span> },
        { key: "online", label: "在线残差", render: (_, row) => <span className="text-xs">n={row.onlineCount} · 均值 {formatResearchNumber(row.onlineMeanResidual)}</span> },
        { key: "shift", label: "残差均值变化", render: (_, row) => <div className="text-xs"><strong>{formatResearchNumber(row.meanResidualShift)}</strong><div>95% 区间 {formatResearchNumber(row.shiftLower95)} ～ {formatResearchNumber(row.shiftUpper95)}</div><StatusBadge value={row.systematicShiftDetected ? "系统性偏移" : row.onlineCount >= 5 && row.shadowCount >= 5 ? "未检出系统性偏移" : "样本不足"} /></div> },
      ]} />
    </Card>
  );
}

function ShadowEvidenceCard({ recommendations, report, variableByCode, objectiveByCode, onMaterialize }) {
  return (
    <Card
      title="影子推荐证据"
      description="建议不下发设备；工程师选择在结果产生前冻结，随后只从实际运行、参数回读和检验记录补齐结果。"
    >
      {report?.stopRecommended && (
        <Alert tone="danger" title="影子评估触发停止信号">
          {(report.stopSignals || []).filter(item => item.severity === "stop").map(item => item.reason).join("；")}
        </Alert>
      )}
      {report && report.totalRecommendations > 0 && (
        <div className="mb-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <Metric label="建议采用率" value={`${Math.round(Number(report.adoptionRate || 0) * 100)}%`} hint={`${report.acceptedCount} 采用 / ${report.modifiedCount} 修改 / ${report.rejectedCount} 拒绝`} />
          <Metric label="结果回收" value={`${report.completedOutcomeCount}/${report.totalRecommendations}`} hint={`${report.invalidOutcomeCount} 条数据不可用`} />
          <Metric label="适用域变化" value={report.contextShiftCount + report.parameterExtrapolationCount} hint="上下文新组合与参数外推" />
          <Metric label="安全事件" value={report.safetyEvents?.length || 0} hint={`${report.settingDeviationCount} 次实际设置偏差`} />
        </div>
      )}
      {(report?.calibration || []).some(item => item.checkedCount > 0) && (
        <div className="mb-4 rounded-xl border border-slate-200 bg-slate-50 p-3 text-xs text-slate-600">
          预测区间覆盖：{report.calibration.filter(item => item.checkedCount > 0).map(item => `${objectiveByCode.get(item.objectiveCode)?.name || item.objectiveCode} ${item.coveredCount}/${item.checkedCount}`).join("；")}。报告哈希 <code>{String(report.reportHash).slice(0, 12)}…</code>
        </div>
      )}
      {recommendations.length === 0 ? (
        <EmptyState title="尚无影子决策" description="在优化建议的运行条件中登记工程师实际选择，开始旁路评估。" />
      ) : (
        <DataTable rows={recommendations} keyField="recommendationId" columns={[
          {
            key: "suggestionRunKey",
            label: "模型建议 / 实际运行",
            render: (value, row) => <div className="space-y-1 text-xs"><code>{value}</code><div>实际：<code>{row.actualRunKey}</code></div><div>模型：{row.modelVersion}</div><StatusBadge value={row.applicability?.status === "in-domain" ? "适用域内" : row.applicability?.status === "context-shift" ? "上下文变化" : row.applicability?.status === "parameter-extrapolation" ? "参数外推" : "历史不足"} /><div className="max-w-64 text-slate-500">{row.applicability?.summary}</div></div>,
          },
          {
            key: "decision",
            label: "工程师选择",
            render: (value, row) => <div className="space-y-2 text-xs"><StatusBadge value={shadowDecisionLabels[value] || value} />{(row.engineerSelectedFactors || []).map(factor => <div key={factor.variableCode}>{variableByCode.get(factor.variableCode)?.name || factor.variableCode}：<strong>{formatResearchNumber(factor.value)} {factor.unit}</strong></div>)}</div>,
          },
          {
            key: "reason",
            label: "拒绝原因 / 现场限制",
            render: (_, row) => <div className="max-w-72 text-xs leading-5 text-slate-600"><div>{row.rejectionReason || "采用建议，无拒绝原因"}</div>{(row.siteLimitations || []).map(value => <div key={value}>限制：{value}</div>)}</div>,
          },
          {
            key: "outcome",
            label: "源数据结果",
            render: value => value ? <div className="space-y-1 text-xs"><StatusBadge value={value.validForOptimization ? "数据完整" : "数据不足"} />{Object.entries(value.outcomes || {}).map(([code, number]) => <div key={code}>{objectiveByCode.get(code)?.name || code}：<strong>{formatResearchNumber(number)}</strong></div>)}{Object.entries(value.settingDeviationFromEngineerSelection || {}).map(([code, number]) => <div key={code}>实际设置偏差 {variableByCode.get(code)?.name || code}：<strong>{formatResearchNumber(number)}</strong></div>)}<code title={value.sourceContentHash}>{String(value.sourceContentHash).slice(0, 12)}…</code></div> : <span className="text-xs text-slate-500">等待实际运行与检验</span>,
          },
          {
            key: "actions",
            label: "操作",
            render: (_, row) => row.outcome ? "已冻结" : <Button onClick={event => { event.stopPropagation(); onMaterialize(row); }}>检查结果</Button>,
          },
        ]} />
      )}
    </Card>
  );
}

function ShadowDecisionDrawer({ target, form, setForm, saving, variables, onClose, onSubmit }) {
  if (!target) return null;
  const variableByCode = new Map(variables.map(item => [item.code, item]));
  const update = name => event => setForm({ ...form, [name]: event.target.value });
  const updateDecision = event => setForm({
    ...form,
    decision: event.target.value,
    factors: event.target.value === "accepted"
      ? Object.fromEntries((target.run.factors || []).map(factor => [factor.variableCode, factor.value]))
      : form.factors,
  });
  return (
    <Drawer
      open
      onClose={onClose}
      title="登记影子选择"
      description="该记录只用于旁路比较，不批准实验，也不向设备写入参数。保存后不能修改。"
      size="lg"
      footer={<><Button disabled={saving} onClick={onClose}>取消</Button><Button variant="primary" disabled={saving} type="submit" form="shadow-decision-form">{saving ? "正在冻结…" : "冻结影子决策"}</Button></>}
    >
      <form id="shadow-decision-form" className="space-y-4" onSubmit={onSubmit}>
        <Alert tone="info">模型建议 <code>{target.run.runKey}</code>；请在知道检验结果之前登记实际选择。</Alert>
        <Field label="决策"><Select value={form.decision} onChange={updateDecision}><option value="accepted">采用模型建议</option><option value="modified">修改后采用</option><option value="rejected">不采用建议</option></Select></Field>
        <Field label="实际生产运行号" hint="必须与采集周期 CorrelationId 完全一致，结果将通过它自动关联。"><Input required value={form.actualRunKey} onChange={update("actualRunKey")} /></Field>
        <div className="grid gap-4 sm:grid-cols-2">
          {(target.run.factors || []).map(factor => (
            <Field key={factor.variableCode} label={variableByCode.get(factor.variableCode)?.name || factor.variableCode} hint={`模型建议 ${formatResearchNumber(factor.value)} ${factor.unit}`}>
              <Input
                required
                disabled={form.decision === "accepted"}
                type="number"
                step="any"
                value={form.factors?.[factor.variableCode] ?? ""}
                onChange={event => setForm({ ...form, factors: { ...form.factors, [factor.variableCode]: event.target.value } })}
              />
            </Field>
          ))}
        </div>
        {form.decision !== "accepted" && <Field label="修改或拒绝原因"><Textarea required rows={3} value={form.rejectionReason} onChange={update("rejectionReason")} placeholder="例如：夹具干涉、材料批次限制、设备升温能力不足。" /></Field>}
        <Field label="现场限制（每行一条，可选）"><Textarea rows={3} value={form.siteLimitations} onChange={update("siteLimitations")} /></Field>
        <Field label="决策时上下文快照" hint="每行 key=value；至少填写一个当时已知的设备、材料、工装或生产上下文。"><Textarea required rows={5} value={form.contextSnapshot} onChange={update("contextSnapshot")} /></Field>
      </form>
    </Drawer>
  );
}

function ControlledDecisionDrawer({ target, form, setForm, saving, variables, onClose, onSubmit }) {
  if (!target) return null;
  const variableByCode = new Map(variables.map(item => [item.code, item]));
  const update = name => event => setForm({ ...form, [name]: event.target.value });
  const updateDecision = event => setForm({
    ...form,
    decision: event.target.value,
    factors: event.target.value === "accepted"
      ? Object.fromEntries((target.run.factors || []).map(factor => [factor.variableCode, factor.value]))
      : form.factors,
  });
  return (
    <Drawer
      open
      onClose={onClose}
      title="受控在线工程师决策"
      description="本次只处理一条建议。建议值、修改后的批准值、理由和决策人保存后均不可覆盖。"
      size="lg"
      footer={<><Button disabled={saving} onClick={onClose}>取消</Button><Button variant="primary" disabled={saving} type="submit" form="controlled-decision-form">{saving ? "正在冻结…" : "冻结本次决策"}</Button></>}
    >
      <form id="controlled-decision-form" className="space-y-4" onSubmit={onSubmit}>
        <Alert tone="warning">这不是自动控制命令。确认后仍需独立批准，Platform 只生成设备无关的执行交接单。</Alert>
        <Field label="决策"><Select value={form.decision} onChange={updateDecision}><option value="accepted">接受原建议</option><option value="modified">修改后接受</option><option value="rejected">拒绝并停止本次建议</option></Select></Field>
        {form.decision !== "rejected" && (
          <div className="grid gap-4 sm:grid-cols-2">
            {(target.run.factors || []).map(factor => (
              <Field key={factor.variableCode} label={variableByCode.get(factor.variableCode)?.name || factor.variableCode} hint={`模型建议 ${formatResearchNumber(factor.value)} ${factor.unit}`}>
                <Input
                  required
                  disabled={form.decision === "accepted"}
                  type="number"
                  step="any"
                  value={form.factors?.[factor.variableCode] ?? ""}
                  onChange={event => setForm({ ...form, factors: { ...form.factors, [factor.variableCode]: event.target.value } })}
                />
              </Field>
            ))}
          </div>
        )}
        {form.decision !== "accepted" && <Field label="修改或拒绝原因"><Textarea required rows={4} value={form.reason} onChange={update("reason")} placeholder="例如：当前工装热负荷限制、材料批次不在适用范围、设备状态不允许。" /></Field>}
      </form>
    </Drawer>
  );
}

function TaskDrawer({ task, form, setForm, workspace, saving, onClose, onSubmit }) {
  if (!task || !workspace) return null;
  const update = name => event => setForm({ ...form, [name]: event.target.value });
  const variables = workspace.project.variables.filter(item => item.role === "control");
  const validatedWindows = workspace.processWindows.filter(item =>
    item.status === "validated" &&
    ["laboratory", "production"].includes(item.validationLevel));
  const beneficialTransfers = (workspace.transferAssessments || []).filter(item =>
    item.status === "reviewed" && item.outcome === "beneficial");
  const baselineRuns = workspace.experiments
    .filter(item => item.designMethod === "historical-observation" || item.status === "completed")
    .flatMap(experiment => (experiment.runPlan || []).map(run => ({
      ...run,
      experimentName: experiment.name,
    })));
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
    cycle.machineId ? `设备 ${cycle.machineId}` : "",
    cycle.edgeIds?.length ? `Edge ${cycle.edgeIds.join("/")}` : "",
    cycle.externalBatchRef ? `批次 ${cycle.externalBatchRef}` : "",
    cycle.workpieceId ? `工件 ${cycle.workpieceId}` : "",
    cycle.recipeId ? `配方 ${cycle.recipeId}` : "",
    cycle.completedAt ? new Date(cycle.completedAt).toLocaleString("zh-CN") : "",
  ].filter(Boolean).join(" · ");
  const resultLabel = result => {
    const experiment = workspace.experiments.find(item => item.experimentId === result.experimentId);
    const metrics = (result.metrics || []).map(item =>
      `${item.objectiveCode} ${formatResearchNumber(item.observedValue)} ${item.unit}`).join("；");
    return `${experiment?.name || "已计算实验"} · ${result.runCount || 0} 个运行${metrics ? ` · ${metrics}` : ""}`;
  };
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
          {baselineRuns.length > 0 && (
            <Field
              label="独立对照运行（可选）"
              hint="按 Ctrl 或 Shift 选择至少两个同条件重复运行；不选择时只计算描述性差值，不生成效果置信区间。"
            >
              <Select
                multiple
                size={Math.min(8, Math.max(3, baselineRuns.length))}
                value={form.baselineRunKeys || []}
                onChange={event => setForm({
                  ...form,
                  baselineRunKeys: Array.from(
                    event.target.selectedOptions,
                    option => option.value,
                  ),
                })}
              >
                {baselineRuns.map(run => (
                  <option key={`${run.experimentName}:${run.runKey}`} value={run.runKey}>
                    {run.experimentName} · {run.runKey}
                  </option>
                ))}
              </Select>
            </Field>
          )}
          <Field label="停止规则"><Textarea required rows={3} value={form.stopRule} onChange={update("stopRule")} /></Field>
          <Field label="回退方案"><Textarea required rows={3} value={form.rollbackPlan} onChange={update("rollbackPlan")} /></Field>
        </>}
        {task === "history" && <>
          <Alert tone="info" title="把已有数据变成优化观察">
            系统只读取已完成运行的实际配方回读、过程特征和检验记录；不会向设备写入参数。至少选择两种实际配方条件，导入后优化器才能使用这些观察。
          </Alert>
          {historyError && <Alert tone="danger">{historyError}</Alert>}
          {historyLoading ? <Alert tone="info">正在读取可导入的已完成运行…</Alert> : (
            <Field label="已完成运行" hint={`已默认选中 ${form.cycleIds?.length || 0} 个与项目产品匹配的运行；列表明确显示设备与 Edge，可跨节点多选。`}>
              <Select multiple required size="12" value={form.cycleIds || []} onChange={event => setForm({ ...form, cycleIds: Array.from(event.target.selectedOptions, option => option.value) })}>
                {historicalCycles.map(cycle => <option key={cycle.correlationId} value={cycle.correlationId}>{historicalCycleLabel(cycle)}</option>)}
              </Select>
            </Field>
          )}
        </>}
        {task === "claim" && <>
          {beneficialTransfers.length > 0 && <Field label="知识证据类型"><Select value={form.knowledgeSourceType} onChange={update("knowledgeSourceType")}><option value="window">当前项目已验证工艺窗口</option><option value="transfer">经复核的迁移收益</option></Select></Field>}
          {form.knowledgeSourceType !== "transfer" ? (
            <Field label="来源工艺窗口"><Select required value={form.processWindowId} onChange={update("processWindowId")}>{validatedWindows.map(item => <option key={item.windowId} value={item.windowId}>{item.name}</option>)}</Select></Field>
          ) : (
            <Field label="来源迁移评估" hint="系统还会校验同一源窗口是否至少两次相对从零对照取得经复核收益。"><Select required value={form.transferAssessmentId} onChange={update("transferAssessmentId")}>{beneficialTransfers.map(item => <option key={item.assessmentId} value={item.assessmentId}>相对从零收益 {formatResearchNumber(Number(item.relativeGain) * 100)}% · {item.contextDifferences?.length || 0} 项条件变化</option>)}</Select></Field>
          )}
          <Field label="知识声明"><Textarea required rows={4} value={form.statement} onChange={update("statement")} /></Field>
          <Field label="适用范围"><Textarea required rows={4} value={form.applicability} onChange={update("applicability")} /></Field>
        </>}
        {task === "rollback-drill" && <>
          <Alert tone="warning">请填写已经执行的演练，不要把计划动作当成实际结果。失败演练同样应如实保存，但不会通过在线门禁。</Alert>
          <Field label="演练名称"><Input required value={form.drillName} onChange={update("drillName")} /></Field>
          <Field label="演练场景"><Textarea required rows={3} value={form.drillScenario} onChange={update("drillScenario")} /></Field>
          <Field label="停止触发条件"><Textarea required rows={3} value={form.drillStopTrigger} onChange={update("drillStopTrigger")} /></Field>
          <Field label="回退目标"><Textarea required rows={3} value={form.drillRollbackTarget} onChange={update("drillRollbackTarget")} /></Field>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="预期动作（每行一项）"><Textarea required rows={5} value={form.drillExpectedActions} onChange={update("drillExpectedActions")} /></Field>
            <Field label="实际完成动作（每行一项）"><Textarea required rows={5} value={form.drillObservedActions} onChange={update("drillObservedActions")} /></Field>
          </div>
          <Field label="演练结论"><Select value={form.drillPassed} onChange={update("drillPassed")}><option value="false">未通过</option><option value="true">通过</option></Select></Field>
          <Field label="证据引用" hint="填写运行号、日志归档号、演练记录编号或其他可定位引用。"><Input required value={form.drillEvidenceReference} onChange={update("drillEvidenceReference")} /></Field>
          <Field label="证据 SHA-256" hint="对原始演练日志或记录文件计算 SHA-256，防止复核后内容被替换。"><Input required minLength="64" maxLength="64" pattern="[a-fA-F0-9]{64}" value={form.drillEvidenceContentHash} onChange={update("drillEvidenceContentHash")} /></Field>
        </>}
        {task === "transfer" && <>
          <Alert tone="warning" title="迁移不是复制配方">
            这里只比较已经完成的两组目标现场实测：一组按源窗口执行，一组从目标条件独立起步。系统不会向设备下发源参数；单次有收益也不能直接沉淀为通用知识。
          </Alert>
          <Field label="源生产工艺窗口" hint="仅列出当前用户可访问且已经生产发布的窗口。">
            <Select required value={form.sourceWindowId} onChange={update("sourceWindowId")}>
              {(workspace.transferSources || []).map(item => <option key={item.windowId} value={item.windowId}>{item.sourceProjectName} · {item.windowName} · {item.sourceMaterialName || "材料未声明"}</option>)}
            </Select>
          </Field>
          <Field label="迁移组实测结果" hint="实际设置必须全部位于源窗口内，且至少三个重复、两个区组。">
            <Select required value={form.transferResultId} onChange={update("transferResultId")}>
              {workspace.experimentResults.map(item => <option key={item.resultId} value={item.resultId}>{resultLabel(item)}</option>)}
            </Select>
          </Field>
          <Field label="从零对照组实测结果" hint="必须是当前目标项目中的另一组独立结果，不能与迁移组相同。">
            <Select required value={form.coldStartResultId} onChange={update("coldStartResultId")}>
              {workspace.experimentResults.map(item => <option key={item.resultId} value={item.resultId}>{resultLabel(item)}</option>)}
            </Select>
          </Field>
          <Field label="现场说明（可选）" hint="记录设备、材料、工装、产品或环境差异以及未纳入模型的边界。">
            <Textarea rows={4} value={form.transferNotes} onChange={update("transferNotes")} />
          </Field>
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
