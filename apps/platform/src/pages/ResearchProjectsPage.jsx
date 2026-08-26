// 编排研发项目从预注册、假设和实验到独立验证与受控生产发布的页面工作流。
import { useCallback, useEffect, useMemo, useState } from "react";
import { useNavigate, useParams, useSearchParams } from "react-router";
import { getJson, patchJson, postJson } from "../api/http";
import {
  createTaskForm,
  nextProjectAction,
  projectFormInitial,
  statusLabels,
} from "../research/researchProjectModel";
import {
  ControlledDecisionDrawer,
  ShadowDecisionDrawer,
  TaskDrawer,
} from "../research/components/ResearchProjectDrawers";
import { CreateProjectDrawer } from "../research/components/CreateResearchProjectDrawer";
import { WorkspaceContent } from "../research/components/ResearchWorkspaceContent";
import {
  lines,
  parseCausalChain,
  parseFailureConditions,
  parseInteractions,
  parseTemporalFeatures,
  parseWorkflowSteps,
} from "../research/researchProjectPresentation";
import {
  Alert,
  Button,
  Card,
  DataTable,
  EmptyState,
  Page,
  RequestError,
  Select,
  StatusBadge,
  notify,
  useConfirmDialog,
} from "../ui/components";

export function ResearchProjectsPage({ identity }) {
  const navigate = useNavigate();
  const { projectId } = useParams();
  const [searchParams] = useSearchParams();
  const [projects, setProjects] = useState([]);
  const [statusFilter, setStatusFilter] = useState("open");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [createOpen, setCreateOpen] = useState(false);
  const [projectForm, setProjectForm] = useState(projectFormInitial);
  const [workspace, setWorkspace] = useState(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [evidenceLoading, setEvidenceLoading] = useState(false);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [evidenceError, setEvidenceError] = useState("");
  const [saving, setSaving] = useState(false);
  const [task, setTask] = useState("");
  const [taskForm, setTaskForm] = useState({});
  const [experimentPreview, setExperimentPreview] = useState(null);
  const [experimentValidation, setExperimentValidation] = useState(null);
  const [shadowTarget, setShadowTarget] = useState(null);
  const [shadowForm, setShadowForm] = useState({});
  const [controlledTarget, setControlledTarget] = useState(null);
  const [controlledForm, setControlledForm] = useState({});
  const [memberCandidates, setMemberCandidates] = useState([]);
  const { confirm, confirmationDialog } = useConfirmDialog();

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
    if (projectId || searchParams.get("create") !== "1") return;
    const executionId = searchParams.get("executionId") || "";
    setProjectForm(current => ({ ...current, referenceProcessExecutionId: executionId }));
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

  async function refreshWorkspace(projectId = workspace?.project?.projectId) {
    if (!projectId) return;
    setDetailLoading(true);
    setEvidenceLoading(true);
    setEvidenceError("");
    setError("");
    try {
      const next = await getJson(`/api/v1/research-projects/${projectId}`);
      if (!next?.project?.projectId) {
        throw new Error("未找到该研发项目，项目可能已删除或尚未同步。");
      }
      setWorkspace(current => ({
        ...next,
        optimizationObservationSummary: current?.project?.projectId === projectId
          ? current.optimizationObservationSummary
          : null,
        onlineAdmission: current?.project?.projectId === projectId
          ? current.onlineAdmission
          : null,
        methodAdmission: current?.project?.projectId === projectId
          ? current.methodAdmission
          : null,
        transferSources: current?.project?.projectId === projectId
          ? current.transferSources
          : [],
      }));
      setProjects(current => current.map(item =>
        item.projectId === next.project.projectId ? next.project : item));
      setDetailLoading(false);

      try {
        const [observationSummary, methodAdmission, onlineAdmission, transferSources] = await Promise.all([
          getJson(`/api/v1/research-projects/${projectId}/experiment-readiness`),
          getJson(`/api/v1/research-projects/${projectId}/method-admission`),
          getJson(`/api/v1/research-projects/${projectId}/online-admission`),
          getJson(`/api/v1/research-projects/${projectId}/transfer-sources`),
        ]);
        setWorkspace(current => current?.project?.projectId === projectId
          ? {
              ...current,
              optimizationObservationSummary: observationSummary,
              methodAdmission,
              onlineAdmission,
              transferSources: transferSources?.data || [],
            }
          : current);
      } catch (requestError) {
        setEvidenceError(requestError.message);
      }
    } catch (requestError) {
      setError(requestError.message);
      notify(requestError.message, "danger");
    } finally {
      setDetailLoading(false);
      setEvidenceLoading(false);
    }
  }

  async function loadOlderWorkspaceHistory() {
    const currentProjectId = workspace?.project?.projectId;
    const cursors = workspace?.nextCursors || {};
    if (!currentProjectId || historyLoading) return;

    const collections = [
      ["experiments", "experiments", "experiments"],
      ["experimentResults", "experiment-results", "experiment-results"],
      ["shadowRecommendations", "shadow-recommendations", "shadow-recommendations"],
      ["historicalReplayReports", "historical-replays", "historical-replays"],
      ["audit", "audit", "audit"],
    ].filter(([, cursorKey]) => cursors[cursorKey]);
    if (collections.length === 0) return;

    setHistoryLoading(true);
    setError("");
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
      setError(requestError.message);
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
      const comparisonExecutionIds = (searchParams.get("comparisonExecutionIds") || "").split(",").filter(Boolean);
      let comparisonImported = false;
      if (projectForm.referenceProcessExecutionId && comparisonExecutionIds.length > 1) {
        try {
          await postJson(`/api/v1/research-projects/${project.projectId}/hypotheses/from-execution-comparison`, {
            baselineProcessExecutionId: projectForm.referenceProcessExecutionId,
            executionIds: comparisonExecutionIds,
            maximumHypotheses: 3,
          });
          comparisonImported = true;
        } catch (requestError) {
          notify(`研发项目已创建，但运行对比未能带入：${requestError.message}。可在项目内重新添加候选假设。`, "warning");
        }
      }
      setProjects(current => [project, ...current]);
      setProjectForm(projectFormInitial);
      setCreateOpen(false);
      if (comparisonExecutionIds.length <= 1 || comparisonImported) {
        notify(comparisonImported ? "研发项目和候选假设已从运行对比创建。" : "研发项目已创建。", "success");
      }
      openProject(project);
    } catch (requestError) {
      if (task === "member" && requestError.status === 409) {
        await refreshWorkspace();
        notify("项目成员已被其他人更新，工作区已刷新；请核对后重新提交。", "danger");
      } else {
        notify(requestError.message, "danger");
      }
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

  async function cloneExperiment(experiment) {
    try {
      await postJson(
        `/api/v1/research-projects/experiments/${experiment.experimentId}/clone`,
        { name: `${experiment.name}（副本）` },
      );
      await refreshWorkspace();
      notify("已基于该实验创建新计划；结果、审批与执行状态均未复制。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    }
  }

  async function materializeExperimentResult(experiment) {
    try {
      await postJson(`/api/v1/research-projects/experiments/${experiment.experimentId}/materialize-result`, {});
      await refreshWorkspace();
      notify("已从冻结的工艺规范、过程与检验数据自动计算实验结果。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    }
  }

  function startShadowDecision(experiment, run) {
    setShadowTarget({ experiment, run });
    setShadowForm({
      decision: "accepted",
      usefulnessRating: "useful",
      actualExecutionKey: "",
      factors: Object.fromEntries((run.factors || []).map(factor => [factor.variableCode, factor.value])),
      rejectionReason: "",
      siteLimitations: "",
      contextSnapshot: "equipment_id=\nmaterial_lot_ref=\ntooling_assembly_id=",
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
        `/api/v1/research-projects/experiments/${shadowTarget.experiment.experimentId}/runs/${encodeURIComponent(shadowTarget.run.executionKey)}/shadow-decision`,
        {
          decision: shadowForm.decision,
          actualExecutionKey: shadowForm.actualExecutionKey,
          engineerSelectedFactors: shadowTarget.run.factors.map(factor => ({
            ...factor,
            value: Number(shadowForm.factors[factor.variableCode]),
          })),
          rejectionReason: shadowForm.rejectionReason || null,
          siteLimitations: shadowForm.siteLimitations.split("\n").map(value => value.trim()).filter(Boolean),
          contextSnapshot,
          usefulnessRating: shadowForm.usefulnessRating,
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

  async function reviewValidationPreregistration(preregistration) {
    try {
      await postJson(
        `/api/v1/research-projects/validation-preregistrations/${preregistration.preregistrationId}/review`,
        {},
      );
      await refreshWorkspace();
      notify("阶段 0 预注册已由独立复核人确认并冻结。", "success");
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
      await postJson(`/api/v1/research-projects/operating-regions/${window.operatingRegionId}/validate`, {});
      await refreshWorkspace();
      notify("已完成独立复核；系统已按重复组和区组证据判定验证等级。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    }
  }

  async function designWindowValidation(window) {
    try {
      await postJson(
        `/api/v1/research-projects/operating-regions/${window.operatingRegionId}/design-validation`,
        {},
      );
      await refreshWorkspace();
      notify("已生成三个跨区组重复运行的独立验证实验，请先审核，再按计划执行。", "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    }
  }

  async function releaseWindow(window) {
    const accepted = await confirm({
      title: "确认发布生产工艺操作域",
      description: "系统将再次校验受控在线源数据、目标、安全边界和职责分离。发布后该操作域作为生产级证据使用，不能把它当作未经验证的候选设置。",
      confirmLabel: "审核并发布",
      tone: "danger",
    });
    if (!accepted) return;
    try {
      await postJson(`/api/v1/research-projects/operating-regions/${window.operatingRegionId}/release`, {});
      await refreshWorkspace();
      notify("工艺操作域已审核并发布生产。", "success");
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
      const observationCount = Number(experiment.optimization?.observationCount || 0);
      await refreshWorkspace();
      notify(
        alreadyActive
          ? mode === "shadow"
            ? "已返回尚未登记完的影子建议，系统没有重复生成建议。"
            : mode === "controlled"
              ? "已返回尚未决策的受控在线建议，没有生成第二条。"
            : "上一批优化实验尚未形成完整观察，已返回原实验，系统没有重复生成工艺规范。"
          : mode === "shadow"
            ? "已生成旁路影子建议；它不能批准或下发，请登记工程师实际选择。"
            : mode === "controlled"
              ? "已生成一条受控在线建议；必须先由现场工程师接受、修改或拒绝。"
          : intent === "validate-hypothesis"
            ? "已设计安全的假设验证实验；完成检验后，证据和假设状态会自动更新。"
            : observationCount > 0
              ? `已基于 ${observationCount} 条冻结观察生成下一组优化实验，请按现有流程审核后执行。`
              : "当前没有可用的冻结观察，已生成首组先验探索实验；结果回传前不形成工艺结论。",
        "success",
      );
    } catch (requestError) {
      notify(requestError.message, "danger");
    }
  }

  async function startTask(name) {
    if (name === "member") {
      try {
        const response = await getJson("/api/v1/users");
        setMemberCandidates(response?.data || []);
      } catch (requestError) {
        setMemberCandidates([]);
        notify(requestError.message, "danger");
      }
    }
    setExperimentPreview(null);
    setExperimentValidation(null);
    setTask(name);
    setTaskForm(createTaskForm(name, workspace));
  }

  async function previewExperimentDesign() {
    const project = workspace?.project;
    if (!project) return;
    try {
      const preview = await postJson(
        `/api/v1/research-projects/${project.projectId}/experiment-designs/preview`,
        {
          designMethod: taskForm.designMethod,
          variableCodes: taskForm.designVariableCodes || [],
          levels: Number(taskForm.designLevels || 2),
          replicatesPerCondition: Number(taskForm.designReplicates || 1),
          blockCount: Number(taskForm.designBlocks || 1),
          sampleCount: Number(taskForm.designSampleCount || 0),
          responseSurfaceFamily: taskForm.responseSurfaceFamily || null,
          randomizationSeed: Number(taskForm.randomizationSeed || 0),
        },
      );
      setExperimentPreview(preview);
      setExperimentValidation(null);
      setTaskForm(current => ({
        ...current,
        generatedRunPlan: preview.runPlan,
        randomizationSeed: preview.randomizationSeed,
      }));
      notify(`已生成 ${preview.runPlan?.length || 0} 条可编辑运行计划。`, "success");
    } catch (requestError) {
      notify(requestError.message, "danger");
    }
  }

  async function submitTask(event) {
    event.preventDefault();
    const project = workspace.project;
    const variable = project.variables.find(item => item.code === taskForm.variableCode);
    const objective = project.objectives[0];
    setSaving(true);
    try {
      if (task === "member") {
        const member = taskForm.member.trim();
        const candidateUserIds = new Set(memberCandidates.map(user => user.userId));
        const currentMemberUserIds = (project.memberUserIds || [])
          .filter(userId => userId === project.ownerUserId || candidateUserIds.has(userId));
        await patchJson(`/api/v1/research-projects/${project.projectId}/members`, {
          revision: project.revision,
          memberUserIds: [...new Set([...currentMemberUserIds, member])],
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
          causalChain: parseCausalChain(taskForm.causalChain),
          temporalFeatures: parseTemporalFeatures(taskForm.temporalFeatures),
          interactions: parseInteractions(taskForm.interactions),
          failureConditions: parseFailureConditions(taskForm.failureConditions),
          falsificationConditions: lines(taskForm.falsificationConditions),
          confidence: 0,
        });
      } else if (task === "experiment") {
        const low = Number(taskForm.low);
        const high = Number(taskForm.high);
        const generatedRunPlan = taskForm.generatedRunPlan || [];
        const runPlan = generatedRunPlan.length > 0 ? generatedRunPlan : [
          {
            executionKey: "condition-low",
            sequence: 1,
            replicateKey: "replicate-1",
            factors: [{ variableCode: variable.code, value: low, unit: variable.unit }],
          },
          {
            executionKey: "condition-high",
            sequence: 2,
            replicateKey: "replicate-1",
            factors: [{ variableCode: variable.code, value: high, unit: variable.unit }],
          },
        ];
        const payload = {
          hypothesisId: taskForm.hypothesisId || null,
          name: taskForm.name,
          designMethod: generatedRunPlan.length > 0 ? taskForm.designMethod : "engineer-defined",
          randomizationSeed: Number(taskForm.randomizationSeed || 0),
          factors: generatedRunPlan.length > 0 ? [] : [{ variableCode: variable.code, value: low, unit: variable.unit }],
          runPlan,
          baselineExecutionKeys: taskForm.baselineExecutionKeys,
          objectiveCodes: [objective.code],
          replicateKeys: [...new Set(runPlan.map(item => item.replicateKey).filter(Boolean))],
          stopRule: taskForm.stopRule,
          rollbackPlan: taskForm.rollbackPlan,
        };
        const validation = await postJson(
          `/api/v1/research-projects/${project.projectId}/experiments/validate`, payload,
        );
        setExperimentValidation(validation);
        if (!validation.isValid) {
          notify("实验计划还未满足全部要求，请查看校验清单。", "danger");
          return;
        }
        await postJson(`/api/v1/research-projects/${project.projectId}/experiments`, payload);
      } else if (task === "history") {
        await postJson(`/api/v1/research-projects/${project.projectId}/experiments/import-history`, {
          executionIds: taskForm.executionIds,
        });
      } else if (task === "claim") {
        await postJson(`/api/v1/research-projects/${project.projectId}/knowledge-claims`, {
          operatingRegionId: taskForm.knowledgeSourceType === "window" ? taskForm.operatingRegionId : null,
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
          sourceOperatingRegionId: taskForm.sourceOperatingRegionId,
          transferResultId: taskForm.transferResultId,
          coldStartResultId: taskForm.coldStartResultId,
          notes: taskForm.transferNotes || null,
        });
      } else if (task === "preregistration") {
        await postJson(`/api/v1/research-projects/${project.projectId}/validation-preregistrations`, {
          dataScope: taskForm.preregDataScope,
          dataFrom: new Date(taskForm.preregDataFrom).toISOString(),
          dataTo: new Date(taskForm.preregDataTo).toISOString(),
          edgeId: taskForm.preregEdgeId || null,
          equipmentId: taskForm.preregEquipmentId || null,
          maximumRuns: Number(taskForm.preregMaximumRuns),
          inclusionMethod: taskForm.preregInclusionMethod,
          inclusionRules: lines(taskForm.preregInclusionRules),
          exclusionRules: lines(taskForm.preregExclusionRules),
          matchingRules: lines(taskForm.preregMatchingRules),
          baselineMethods: lines(taskForm.preregBaselineMethods),
          primaryMetrics: lines(taskForm.preregPrimaryMetrics),
          guardrailMetrics: lines(taskForm.preregGuardrailMetrics),
          stopConditions: lines(taskForm.preregStopConditions),
          falsificationConditions: lines(taskForm.preregFalsificationConditions),
          engineerWorkflowBaselines: [{
            name: taskForm.preregWorkflowName,
            startedAt: new Date(taskForm.preregWorkflowStart).toISOString(),
            completedAt: new Date(taskForm.preregWorkflowEnd).toISOString(),
            steps: parseWorkflowSteps(taskForm.preregWorkflowSteps),
            notes: taskForm.preregWorkflowNotes || null,
          }],
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
        title={project?.name || "研发项目工作区"}
        description={project?.description || undefined}
        actions={(
          <>
            <Button onClick={() => navigate("/research-projects")}>返回项目列表</Button>
            {projectAction && (
              <Button
                variant="primary"
                disabled={detailLoading || (project.status === "draft" && !workspace?.stageZeroAdmission?.eligible)}
                title={project.status === "draft" && !workspace?.stageZeroAdmission?.eligible
                  ? (workspace?.stageZeroAdmission?.failures || ["先冻结并独立复核阶段 0 预注册"]).join("；")
                  : undefined}
                onClick={() => changeProjectStatus(projectAction[1])}
              >
                {projectAction[0]}
              </Button>
            )}
          </>
        )}
      >
        <RequestError error={error} onRetry={() => refreshWorkspace(projectId)} />
        {evidenceError && workspace && (
          <Alert tone="warning" title="项目证据准备度暂不可用">{evidenceError}</Alert>
        )}
        {!project ? (
          <Card>
            <p className="py-16 text-center text-sm text-slate-500">
              {detailLoading ? "正在读取项目工作区…" : "未找到可显示的研发项目。"}
            </p>
          </Card>
        ) : (
          <WorkspaceContent
            workspace={workspace}
            loading={evidenceLoading}
            historyLoading={historyLoading}
            onLoadOlderHistory={loadOlderWorkspaceHistory}
            onTask={startTask}
            onExperimentStatus={changeExperimentStatus}
            onCloneExperiment={cloneExperiment}
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
            onReviewValidationPreregistration={reviewValidationPreregistration}
            onAskAi={currentProjectId => navigate(`/chat?projectId=${encodeURIComponent(currentProjectId)}`)}
            currentUserId={identity?.userId || ""}
            isPlatformAdmin={(identity?.roles || []).includes("platform.admin")}
          />
        )}
        <TaskDrawer
          task={task}
          form={taskForm}
          setForm={setTaskForm}
          workspace={workspace}
          memberCandidates={memberCandidates}
          saving={saving}
          experimentPreview={experimentPreview}
          experimentValidation={experimentValidation}
          onPreviewExperimentDesign={previewExperimentDesign}
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
        {confirmationDialog}
      </Page>
    );
  }

  return (
    <Page
      title="研发项目"
      actions={<Button variant="primary" onClick={() => setCreateOpen(true)}>新建研发项目</Button>}
    >
      <RequestError error={error} onRetry={load} />
      <section className="flex flex-col gap-3 rounded-lg border border-slate-200 bg-white px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
        <label className="flex items-center gap-3 text-sm font-medium text-slate-700">
          状态
          <Select className="w-44" value={statusFilter} onChange={event => setStatusFilter(event.target.value)}>
            <option value="open">进行中与待处理</option>
            <option value="all">全部</option>
            <option value="active">研发中</option>
            <option value="validating">验证中</option>
            <option value="completed">已完成</option>
            <option value="archived">已归档</option>
          </Select>
        </label>
        <p className="text-[13px] text-slate-500">
          共 {projects.length} 项 · 待处理 {metrics.open} · 已完成 {metrics.completed}
        </p>
      </section>

      <Card title="项目列表">
        {loading ? (
          <p className="py-12 text-center text-sm text-slate-500">正在读取研发项目…</p>
        ) : projects.length === 0 ? (
          <EmptyState title="从一个待解决的工艺问题开始" description="填写目标、首个可控变量和安全边界；其余证据会在推进过程中逐步补齐。" />
        ) : filteredProjects.length === 0 ? (
          <EmptyState title="当前筛选条件下没有项目" description="请选择其他状态查看项目。" />
        ) : (
          <DataTable
            rows={filteredProjects}
            keyField="projectId"
            onRowClick={openProject}
            columns={[
              { key: "name", label: "研发项目" },
              { key: "processName", label: "工艺" },
              { key: "productName", label: "产品", render: value => value || "—" },
              { key: "status", label: "阶段", render: value => <StatusBadge value={value} label={statusLabels[value] || value} /> },
              { key: "ownerUserId", label: "负责人", render: value => value === identity?.userId ? (identity.displayName || identity.username || value) : value || "—" },
              { key: "updatedAt", label: "最近更新", render: value => value ? new Date(value).toLocaleString("zh-CN") : "—" },
              { key: "open", label: "操作", render: (_, project) => <Button onClick={event => { event.stopPropagation(); openProject(project); }}>进入工作区</Button> },
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
      {confirmationDialog}
    </Page>
  );
}
