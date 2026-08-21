import { useCallback, useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { getJson, patchJson, postJson } from "../api/http";
import {
  createTaskForm,
  experimentScale,
  formatResearchNumber,
  nextProjectAction,
  projectFormInitial,
  shadowDecisionLabels,
  statusLabels,
  taskTitles,
} from "../research/researchProjectModel";
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
  RequestError,
  Select,
  StatusBadge,
  Textarea,
  WorkflowGuide,
  notify,
} from "../ui/components";

export function ResearchProjectsPage({ identity }) {
  const navigate = useNavigate();
  const { projectId } = useParams();
  const [searchParams] = useSearchParams();
  const [projects, setProjects] = useState([]);
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
    active: projects.filter(project => project.status === "active").length,
    validating: projects.filter(project => project.status === "validating").length,
    completed: projects.filter(project => project.status === "completed").length,
  }), [projects]);

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
        transferSources: current?.project?.projectId === projectId
          ? current.transferSources
          : [],
      }));
      setProjects(current => current.map(item =>
        item.projectId === next.project.projectId ? next.project : item));
      setDetailLoading(false);

      try {
        const [observationSummary, onlineAdmission, transferSources] = await Promise.all([
        getJson(`/api/v1/research-projects/${projectId}/experiment-readiness`),
        getJson(`/api/v1/research-projects/${projectId}/online-admission`),
        getJson(`/api/v1/research-projects/${projectId}/transfer-sources`),
        ]);
        setWorkspace(current => current?.project?.projectId === projectId
          ? {
              ...current,
              optimizationObservationSummary: observationSummary,
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
        description={project?.description || "围绕当前问题推进假设、实验、验证和知识复用。"}
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
      </Page>
    );
  }

  return (
    <Page
      title="研发项目"
      description="用最少的有效实验，把生产问题追溯为可验证证据，再形成可复用的工艺操作域。"
      actions={<Button variant="primary" onClick={() => setCreateOpen(true)}>新建研发项目</Button>}
    >
      <RequestError error={error} onRetry={load} />
      <section className="grid gap-3 sm:grid-cols-3">
        <Metric label="进行中的研发" value={metrics.active + metrics.validating} hint="需要工程决策或独立验证" />
        <Metric label="已验证结论" value={metrics.completed} hint="已完成项目" />
        <Metric label="项目组合" value={projects.length} hint="当前可访问项目" />
      </section>

      <Card
        title="研发项目"
        description="先处理需要决策或验证的项目；每个工作区保留完整证据链。"
      >
        {loading ? (
          <p className="py-12 text-center text-sm text-slate-500">正在读取研发项目…</p>
        ) : projects.length === 0 ? (
          <EmptyState title="从一个待解决的工艺问题开始" description="填写目标、首个可控变量和安全边界；其余证据会在推进过程中逐步补齐。" />
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
              { key: "open", label: "操作", render: (_, project) => <Button onClick={event => { event.stopPropagation(); openProject(project); }}>进入工作区</Button> },
            ]}
          />
        )}
      </Card>

      <section className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="rounded-2xl border border-blue-100 bg-gradient-to-br from-blue-50 via-white to-white p-5 shadow-sm">
          <p className="text-sm font-semibold text-blue-700">从真实偏差进入研发闭环</p>
          <h2 className="mt-2 text-lg font-semibold tracking-tight text-slate-950">
            发现偏差 → 缩小候选原因 → 设计实验 → 验证并固化窗口
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
            title="研发路径"
            description="围绕真实问题推进证据、实验和验证。"
            compact
            steps={[
              { title: "明确偏差", description: "从质量追因或历史对比确认问题和范围。", state: projects.length ? "done" : "current" },
              { title: "设定边界", description: "写下研发目标、一个可控变量和安全限制。", state: projects.length ? "current" : "upcoming" },
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
            <Field label="项目名称"><Input required value={form.name} onChange={field("name")} placeholder="光学模压工艺操作域研发" /></Field>
            <Field label="参考运行" hint="选择后自动带入产品范围；不影响后续用更多运行形成证据。"><Select value={form.referenceProcessExecutionId} onChange={event => chooseReferenceProcessExecution(event.target.value)}><option value="">暂不关联历史运行</option>{catalog.executions.map(execution => <option key={execution.executionId} value={execution.executionId}>{executionLabel(execution)}</option>)}</Select></Field>
            <Field label="工艺配置（推荐）" hint="只允许选择不可变的已发布版本；其中 required-for-analysis 字段会成为优化准入条件。"><Select value={form.scenarioPackageKey} onChange={event => chooseScenarioPackage(event.target.value)}><option value="">暂不使用工艺配置</option>{selectableScenarios.map(item => <option key={`${item.packageId}:${item.version}`} value={`${item.packageId}:${item.version}`}>{item.name} · v{item.version}</option>)}</Select></Field>
            <Field label="工艺数据模型" hint="决定可选的控制参数与实际数据来源。"><Select required value={form.dataModelKey} onChange={event => chooseDataModel(event.target.value)}><option value="">选择已配置的工艺数据模型</option>{selectableModels.map(model => <option key={`${model.modelId}:${model.version}`} value={`${model.modelId}:${model.version}`}>{model.name} · v{model.version}</option>)}</Select></Field>
            <Field label="目标产品" hint="来自参考运行；未关联时可补充产品编号。"><Input value={form.productName} onChange={field("productName")} placeholder="产品编号（可选）" /></Field>
            <Field label="材料"><Input value={form.materialName} onChange={field("materialName")} /></Field>
            <Field label="项目说明" className="md:col-span-2"><Textarea value={form.description} onChange={field("description")} rows={3} /></Field>
          </div>
        </Card>
        <Card title="2. 首要研发目标" description="选择要改善的质量指标及判定方向。">
          <div className="grid gap-4 md:grid-cols-2">
            <Field label="质量目标" hint="可直接优化正式检验合格率，也可选择检测数值；代码、单位和数据来源会自动带入。"><Select required value={form.objectiveKey} onChange={event => chooseObjective(event.target.value)}><option value="">选择正式质检结论或数值指标</option>{objectiveOptions.map(option => <option key={option.key} value={option.key}>{option.kind === "outcome" ? `${option.definition.name} · 合格率` : `${option.definition.name} · ${option.characteristic.name}${option.characteristic.unit ? ` (${option.characteristic.unit})` : ""}`}</option>)}</Select></Field>
            <Field label="数据来源"><Input readOnly value={form.objectiveDataSource} placeholder="选择质量指标后自动带入" className="bg-slate-50 text-slate-600" /></Field>
            <Field label="优化方向"><Select value={form.objectiveDirection} onChange={field("objectiveDirection")}><option value="minimize">越低越好</option><option value="maximize">越高越好</option><option value="target">接近目标</option><option value="range">保持范围</option></Select></Field>
            <Field label="指标单位"><Input readOnly required value={form.objectiveUnit} placeholder="自动带入" className="bg-slate-50 text-slate-600" /></Field>
            <Field label="目标值" hint="来自检测上下限的建议值，可按研发规格调整。"><Input required type="number" step="any" value={form.objectiveTarget} onChange={field("objectiveTarget")} /></Field>
            <Field label="目标权重"><Input required type="number" min="0.01" step="any" value={form.objectiveWeight} onChange={field("objectiveWeight")} /></Field>
          </div>
        </Card>
        <Card title="3. 首个可控变量" description="定义第一轮实验允许调整的参数范围。">
          <div className="grid gap-4 md:grid-cols-2">
            <Field label="控制参数" hint={selectedModel ? "从所选工艺数据模型中选择。" : "请先选择工艺数据模型。"}><Select required disabled={!selectedModel} value={form.variableCode} onChange={event => chooseVariable(event.target.value)}><option value="">选择控制参数</option>{(selectedModel?.controlParameters || []).map(parameter => <option key={parameter.code} value={parameter.code}>{parameter.displayName || parameter.code}{parameter.unit ? ` (${parameter.unit})` : ""}</option>)}</Select></Field>
            <Field label="实际数据来源"><Input readOnly value={form.variableDataSource} placeholder="选择控制参数后自动带入" className="bg-slate-50 text-slate-600" /></Field>
            <Field label="变量单位"><Input readOnly required value={form.variableUnit} placeholder="自动带入" className="bg-slate-50 text-slate-600" /></Field>
            <Field label="允许下限" hint="这是实验允许范围，请按设备/安全规范确认。"><Input required type="number" step="any" value={form.variableLower} onChange={field("variableLower")} /></Field>
            <Field label="允许上限" hint="这是实验允许范围，请按设备/安全规范确认。"><Input required type="number" step="any" value={form.variableUpper} onChange={field("variableUpper")} /></Field>
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

export function MemberManagementButton({ allowed, onClick }) {
  return allowed ? <Button onClick={onClick}>添加协作成员</Button> : null;
}

function WorkspaceContent({
  workspace,
  loading,
  historyLoading,
  onLoadOlderHistory,
  onTask,
  onExperimentStatus,
  onCloneExperiment,
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
  onReviewValidationPreregistration,
  onAskAi,
  currentUserId,
  isPlatformAdmin,
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
    operatingRegions = [],
    knowledgeClaims = [],
    transferAssessments = [],
    validationPreregistrations = [],
  } = workspace;
  const onlineAdmission = workspace.onlineAdmission;
  const stageZeroAdmission = workspace.stageZeroAdmission;
  const latestPreregistration = validationPreregistrations[0];
  const reliabilityBaseline = latestPreregistration?.reliabilityBaseline;
  const analysisAdmissionRate = reliabilityBaseline?.rates?.find(
    item => item.code === "analysis_admission",
  )?.rate;
  const reviewedOperatingRegions = operatingRegions.filter(item => item.status === "validated");
  const validatedOperatingRegions = reviewedOperatingRegions.filter(item =>
    ["laboratory", "production"].includes(item.validationLevel));
  const observationSummary = workspace.optimizationObservationSummary;
  const canEdit = !["completed", "archived"].includes(project.status);
  const canManageMembers = canEdit && (project.ownerUserId === currentUserId || isPlatformAdmin);
  const hasObservation = Number(observationSummary?.validObservationCount || 0) > 0;
  const executionExperiments = experiments.filter(item =>
    item.status !== "cancelled" && item.designMethod !== "historical-observation");
  const hasRunningExperiment = executionExperiments.some(item => item.status === "running");
  const observedExecutionKeys = new Set(
    observationSummary?.observedExecutionKeys ||
      (observationSummary?.observations || []).map(item => item.executionKey),
  );
  const variableByCode = new Map(project.variables.map(item => [item.code, item]));
  const objectiveByCode = new Map(project.objectives.map(item => [item.code, item]));
  const constraintByCode = new Map(
    (project.outcomeConstraints || []).map(item => [item.code, item]),
  );
  const currentStage = project.status === "completed" && validatedOperatingRegions.length === 0
    ? ["历史项目待复验", "该项目按旧规则完成，但工艺操作域缺少跨区组重复证据；请新建复现实验完成实验室验证后再发布生产。"]
    : project.status === "completed"
      ? ["研究已闭环", "工艺操作域已完成实验室验证或生产发布，可沉淀并复用于相似工艺。"]
      : project.status === "draft"
    ? ["定义问题", "先明确目标、可控变量和安全边界。"]
    : hypotheses.length === 0
      ? ["建立假设", "把经验或异常转为可验证的因果判断。"]
      : executionExperiments.length === 0
        ? ["设计实验", "优先使用智能建议，以最少实验获取最大信息量。"]
        : hasRunningExperiment
          ? ["收集证据", "等待运行和检验完成，再让系统更新模型。"]
          : experimentResults.length === 0
            ? ["计算结果", "把冻结的数据快照转成可追溯的实验结果。"]
            : operatingRegions.length === 0
              ? ["形成操作域", "将有证据支持的范围提交为候选工艺操作域。"]
              : validatedOperatingRegions.length === 0
                ? ["独立验证", "由其他成员验证窗口，避免把偶然结果当作规律。"]
                : ["沉淀知识", "已具备可复用结论，可复核后服务下一个项目。"];
  const workflowSteps = [
    { id: "project-definition", title: "定义", description: "目标与边界", state: project.status === "draft" ? "current" : "done" },
    { id: "project-diagnosis", title: "追因", description: "假设与证据", state: hypotheses.length ? "done" : project.status === "draft" ? "upcoming" : "current" },
    { id: "project-experiments", title: "实验", description: "建议与执行", state: executionExperiments.length ? "done" : hypotheses.length ? "current" : "upcoming" },
    { id: "project-validation", title: "验证", description: "结果与窗口", state: validatedOperatingRegions.length ? "done" : experimentResults.length ? "current" : "upcoming" },
    { id: "project-reuse", title: "复用", description: "知识与迁移", state: knowledgeClaims.some(item => item.status === "reviewed") ? "done" : validatedOperatingRegions.length ? "current" : "upcoming" },
  ];
  return (
    <div className="space-y-5">
        {loading && <Alert tone="info">正在更新项目证据与准备度…</Alert>}
        {Object.values(workspace.nextCursors || {}).some(Boolean) && (
          <Alert tone="info" title="当前先显示最近 100 条记录">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <span>实验、结果、建议、回放和审计记录已按游标分页，可继续读取更早历史。</span>
              <Button disabled={historyLoading} onClick={onLoadOlderHistory}>
                {historyLoading ? "正在加载…" : "加载更早记录"}
              </Button>
            </div>
          </Alert>
        )}
        <Card
          title="阶段 0：预注册与数据基线"
          description="在查看验证结果前冻结范围、纳入排除、比较方法、指标、停止与否证条件，并由另一名成员复核。"
        >
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
            <Metric label="准入结论" value={stageZeroAdmission?.eligible ? "允许开始研发" : "尚未通过"} />
            <Metric label="预注册版本" value={latestPreregistration ? `v${latestPreregistration.version}` : "—"} hint={latestPreregistration?.status === "reviewed" ? "已独立复核" : "等待独立复核"} />
            <Metric label="当前流程耗时" value={latestPreregistration?.plan?.engineerWorkflowBaselines?.[0]?.totalMinutes == null ? "—" : `${formatResearchNumber(latestPreregistration.plan.engineerWorkflowBaselines[0].totalMinutes)} 分钟`} hint={`${latestPreregistration?.plan?.engineerWorkflowBaselines?.[0]?.steps?.length || 0} 个步骤`} />
            <Metric label="数据基线运行" value={reliabilityBaseline ? formatResearchNumber(reliabilityBaseline.analyzedRunCount) : "—"} hint={reliabilityBaseline?.truncated ? "已达到最大运行数" : "已固化到当前版本"} />
            <Metric label="正式分析准入率" value={analysisAdmissionRate == null ? "—" : `${formatResearchNumber(analysisAdmissionRate * 100)}%`} hint="不替代场景预注册阈值" />
            <Metric label="内容哈希" value={latestPreregistration ? `${String(latestPreregistration.contentHash).slice(0, 12)}…` : "—"} />
          </div>
          {(stageZeroAdmission?.failures || []).length > 0 && <Alert tone="warning" title="阶段 0 门禁未通过">{stageZeroAdmission.failures.map(item => <div key={item}>{item}</div>)}</Alert>}
          {(stageZeroAdmission?.warnings || []).length > 0 && <Alert tone="warning" title="数据基线提醒">{stageZeroAdmission.warnings.map(item => <div key={item}>{item}</div>)}</Alert>}
          <div className="mt-4 flex flex-wrap gap-2">
            {project.status === "draft" && <Button variant="primary" onClick={() => onTask("preregistration")}>{latestPreregistration ? "冻结新版本" : "填写并冻结预注册"}</Button>}
            {latestPreregistration?.status === "frozen" && <Button onClick={() => onReviewValidationPreregistration(latestPreregistration)}>独立复核当前版本</Button>}
            <Link className="inline-flex min-h-9 items-center rounded-lg px-3 py-2 text-sm font-medium text-blue-700 hover:bg-blue-50" to="/data-quality">查看数据健康与正式分析准入</Link>
          </div>
        </Card>
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
              <MemberManagementButton allowed={canManageMembers} onClick={() => onTask("member")} />
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
              {canEdit && validatedOperatingRegions.length > 0 && <Button variant="primary" onClick={() => onTask("claim")}>沉淀工艺知识</Button>}
            </div>
            <div className="rounded-xl border border-white/80 bg-white/80 p-4">
              <p className="text-sm font-semibold text-slate-900">实验建议准备度</p>
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
          <Metric label="有效实验计划" value={executionExperiments.length} hint="不含历史证据和取消记录" />
          <Metric label="可用于研发" value={observationSummary?.validObservationCount ?? 0} hint="参数、过程与结果已关联" />
          <Metric label="已验证窗口" value={validatedOperatingRegions.length} hint={`${reviewedOperatingRegions.length} 个窗口已完成复核`} />
        </div>
        {observationSummary?.excludedObservationCount > 0 && (
          <Alert tone="warning">
            有 {observationSummary.excludedObservationCount} 条运行因缺少检验值、过程特征或完整运行边界而未进入实验建议模型。
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
                  ? <Button onClick={event => { event.stopPropagation(); onGenerateOptimizationSuggestions("validate-hypothesis", row.hypothesisId); }}>让优化器设计验证实验</Button>
                  : "补充验证标准后可自动设计实验",
              },
            ]} />
          )}
        </Card>
        </div>

        <div id="project-experiments" className="scroll-mt-60 space-y-5">
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
                key: "executionKeys",
                label: "建议执行条件",
                render: (_, row) => {
                  const isHistorical = row.designMethod === "historical-observation";
                  const runs = isHistorical ? (row.runPlan || []).slice(0, 3) : row.runPlan || [];
                  return (
                    <div className="space-y-2">
                    {runs.map(run => (
                      <div key={run.executionKey} className="rounded-lg border border-slate-200 bg-slate-50 p-2">
                        <div className="flex items-center justify-between gap-2">
                          <code className="block text-xs font-semibold text-slate-700">{run.executionKey}</code>
                          {!isHistorical && (
                            <StatusBadge value={observedExecutionKeys.has(run.executionKey) ? "数据已回收" : "等待运行"} />
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
                        {row.status !== "cancelled" && row.optimization?.mode === "shadow" && !shadowRecommendations.some(item =>
                          item.experimentId === row.experimentId && item.suggestionExecutionKey === run.executionKey) && canEdit && (
                          <Button onClick={event => { event.stopPropagation(); onShadowDecision(row, run); }}>
                            登记影子选择
                          </Button>
                        )}
                        {shadowRecommendations.some(item =>
                          item.experimentId === row.experimentId && item.suggestionExecutionKey === run.executionKey) && (
                          <div className="mt-2">
                            <StatusBadge value={shadowDecisionLabels[shadowRecommendations.find(item =>
                              item.experimentId === row.experimentId && item.suggestionExecutionKey === run.executionKey)?.decision] || "已登记影子决策"} />
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
                render: (value, row) => {
                  if (!value) return "—";
                  const mechanismUsages = (workspace.mechanismKnowledgeUsages || [])
                    .filter(item => item.recommendationId === row.experimentId);
                  const appliedMechanismClaims = groupMechanismUsages(mechanismUsages);
                  return (
                    <div className="min-w-72 space-y-2 text-xs text-slate-600">
                      <div>
                        贝叶斯优化基于 <strong className="text-slate-900">{value.observationCount}</strong> 条观察和{" "}
                        <strong className="text-slate-900">{value.processFeatureCount || 0}</strong> 个共同轨迹特征
                      </div>
                      {appliedMechanismClaims.length > 0 && (
                        <details className="rounded-lg border border-indigo-200 bg-indigo-50 p-2 text-indigo-950">
                          <summary className="cursor-pointer font-semibold marker:text-indigo-500">
                            本次采用的机理知识 · {appliedMechanismClaims.length} 条
                          </summary>
                          <p className="mt-1 text-[11px] text-indigo-700">
                            冻结知识快照 <code>{String(value.mechanismKnowledgeSnapshotHash).slice(0, 12)}</code>
                          </p>
                          <div className="mt-2 space-y-2">
                            {appliedMechanismClaims.map(item => {
                              const claim = item.appliedClaim;
                              return (
                                <article className="rounded-lg border border-indigo-100 bg-white p-2 text-slate-700" key={`${item.claimId}-${item.claimVersion}`}>
                                  <div className="flex flex-wrap items-center justify-between gap-2">
                                    <strong className="text-slate-900">{claim?.name || item.claimName || item.claimId} v{item.claimVersion}</strong>
                                    <code title={item.contentHash}>{String(item.contentHash).slice(0, 12)}</code>
                                  </div>
                                  <p className="mt-1 text-[11px] text-indigo-700">{item.usageTypes.map(mechanismUsageLabel).join(" · ")}</p>
                                  {claim?.statement && <p className="mt-2 leading-5">{claim.statement}</p>}
                                  {(claim?.constraints || []).length > 0 && (
                                    <div className="mt-2 space-y-1">
                                      <strong className="text-[11px] text-slate-500">实际采用的边界与偏好</strong>
                                      {claim.constraints.map(constraint => (
                                        <div className="rounded-md bg-slate-50 px-2 py-1" key={constraint.constraintId}>
                                          <span className={constraint.severity === "hard" ? "font-semibold text-red-700" : "font-semibold text-amber-700"}>
                                            {constraint.severity === "hard" ? "硬边界" : "候选偏好"}
                                          </span>
                                          {" · "}{variableByCode.get(constraint.variableCode)?.name || constraint.variableCode}：{formatMechanismConstraint(constraint)}
                                        </div>
                                      ))}
                                    </div>
                                  )}
                                  {claim?.falsificationCondition && (
                                    <p className="mt-2 rounded-md border border-amber-200 bg-amber-50 px-2 py-1 text-amber-900">
                                      <strong>反证条件：</strong>{claim.falsificationCondition}
                                    </p>
                                  )}
                                  {(claim?.evidence || []).length > 0 && (
                                    <div className="mt-2 space-y-1">
                                      <strong className="text-[11px] text-slate-500">冻结证据引用</strong>
                                      {claim.evidence.map(evidence => (
                                        <div className="break-all rounded-md bg-slate-50 px-2 py-1" key={evidence.evidenceLinkId}>
                                          {mechanismEvidenceLabel(evidence.evidenceKind)} · {evidence.polarity === "opposing" ? "反对证据" : "支持证据"}<br />
                                          <code>{evidence.referenceId}</code> · 哈希 <code>{String(evidence.contentHash).slice(0, 12)}</code>
                                        </div>
                                      ))}
                                    </div>
                                  )}
                                </article>
                              );
                            })}
                          </div>
                        </details>
                      )}
                      {(value.runPredictions || []).map(prediction => (
                        <div key={prediction.executionKey} className="rounded-lg border border-slate-200 bg-white p-2">
                          <div className="flex flex-wrap items-center justify-between gap-2">
                            <code>{prediction.executionKey}</code>
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
                    {row.status === "cancelled" && <span className="text-xs text-slate-500">已取消，仅保留审计记录</span>}
                    {row.status !== "cancelled" && row.designMethod === "historical-observation" && <span className="text-xs text-slate-500">只读证据</span>}
                    {row.designMethod !== "historical-observation" && row.designMethod !== "bayesian-optimization" && row.status !== "cancelled" && canEdit && <Button onClick={event => { event.stopPropagation(); onCloneExperiment(row); }}>基于此实验新建</Button>}
                    {row.status !== "cancelled" && row.optimization?.mode === "shadow" && <span className="text-xs text-slate-500">旁路评估，不可下发</span>}
                    {row.optimization?.mode === "controlled" && row.status === "planned" && !row.controlledDecision && row.createdBy !== currentUserId && <Button onClick={event => { event.stopPropagation(); onControlledDecision(row); }}>接受 / 修改 / 拒绝</Button>}
                    {row.optimization?.mode === "controlled" && row.status === "planned" && !row.controlledDecision && row.createdBy === currentUserId && <span className="text-xs text-slate-500">等待现场工程师决策</span>}
                    {row.designMethod !== "historical-observation" && row.optimization?.mode !== "shadow" && row.optimization?.mode !== "controlled" && row.status === "planned" && row.createdBy !== currentUserId && <Button onClick={event => { event.stopPropagation(); onExperimentStatus(row, "approved"); }}>批准</Button>}
                    {row.designMethod !== "historical-observation" && row.optimization?.mode !== "shadow" && row.optimization?.mode !== "controlled" && row.status === "planned" && row.createdBy === currentUserId && <span className="text-xs text-slate-500">等待其他成员批准</span>}
                    {row.optimization?.mode === "controlled" && row.status === "planned" && row.controlledDecision && row.createdBy !== currentUserId && <Button onClick={event => { event.stopPropagation(); onExperimentStatus(row, "approved"); }}>批准本次运行</Button>}
                    {row.optimization?.mode === "controlled" && row.controlledDecision && <span className="text-xs text-slate-500">{row.controlledDecision.decision === "modified" ? "已修改" : row.controlledDecision.decision === "rejected" ? "已拒绝" : "已接受"}，决策已冻结</span>}
                    {row.designMethod !== "historical-observation" && row.status === "approved" && <Button onClick={event => { event.stopPropagation(); onExperimentStatus(row, "running"); }}>记录下发</Button>}
                    {row.designMethod !== "historical-observation" && row.status === "running" && (
                      <span className="text-xs text-slate-500">
                        已记录下发意图，等待现场执行、采集和检验结果
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
          {operatingRegions.length === 0 ? <EmptyState title="尚未形成候选设置" description="完成优化实验后，系统会从同一条件的重复源数据中自动形成候选设置。" /> : (
            <DataTable rows={operatingRegions} keyField="operatingRegionId" columns={[
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
                      experiment => experiment.validationOperatingRegionId === row.operatingRegionId
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
          description="将源工艺操作域在当前项目的实测结果，与当前项目从零建立的独立对照比较；这里只形成证据，不自动套用参数。"
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
          {knowledgeClaims.length === 0 ? <EmptyState title="尚未沉淀知识" description="只有经过验证的工艺操作域才能转化为工艺知识。" /> : (
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
      description="只在真实跑过的唯一工艺规范候选池内逐次选择；完整保留原顺序、优化器、随机对照、校准、安全事件和失败闸门。"
    >
      {reports.length === 0 ? (
        <EmptyState title="尚未生成历史回放报告" description="至少积累 3 种不同的完整实际工艺规范条件；5 种以上才具备通过探索性闸门的可能。" />
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
            render: (_, row) => <div className="text-xs leading-5">{row.sourceRunCount} 条运行<br />{row.uniqueConditionCount} 种唯一条件<br />预算 {row.budget} · {row.seedCount} 个随机种子<br /><code title={row.preregistrationHash}>预注册 {String(row.preregistrationHash || "not-registered").slice(0, 12)}…</code></div>,
          },
          {
            key: "comparison",
            label: "达到规格试验数",
            render: (_, row) => <div className="min-w-52 text-xs leading-5">历史原顺序：<strong>{row.originalOrderTrials ?? "未达到"}</strong><br />优化器中位数：<strong>{row.optimizer?.medianTrials ?? "未达到"}</strong>（成功率 {Math.round(Number(row.optimizer?.successRate || 0) * 100)}%）<br />随机中位数：<strong>{row.random?.medianTrials ?? "未达到"}</strong>（成功率 {Math.round(Number(row.random?.successRate || 0) * 100)}%）<br />二次响应面：<strong>{row.responseSurface?.medianTrials ?? "不适用或未达到"}</strong>{row.responseSurface ? `（成功率 ${Math.round(Number(row.responseSurface.successRate || 0) * 100)}%）` : ""}{row.mechanismComparison && <div className="mt-2 border-t border-slate-200 pt-2">知识 vs 纯数据：成功率差 <strong>{signedPercent(row.mechanismComparison.successRateDelta)}</strong><br />中位试验数差：<strong>{signedNumber(row.mechanismComparison.medianTrialsDelta)}</strong><br />安全违规差：<strong>{signedNumber(row.mechanismComparison.safetyViolationDelta)}</strong><br /><code title={row.mechanismComparison.pairingHash}>配对 {String(row.mechanismComparison.pairingHash).slice(0, 12)}…</code></div>}</div>,
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

function signedPercent(value) { return value === null || value === undefined ? "不可比较" : `${value > 0 ? "+" : ""}${Math.round(Number(value) * 100)}%`; }
function signedNumber(value) { return value === null || value === undefined ? "不可比较" : `${value > 0 ? "+" : ""}${Number(value).toFixed(2).replace(/\.00$/, "")}`; }

function OnlineAdmissionCard({ evidence }) {
  if (!evidence) return null;
  return (
    <Card
      title="受控在线准入"
      description="通过只代表系统可以提出一条候选建议；它不授权自动写设备，仍须现场工程师逐条确认。"
    >
      <div className="space-y-4">
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
      </div>
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
        <div className="mb-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
          <Metric label="建议采用率" value={`${Math.round(Number(report.adoptionRate || 0) * 100)}%`} hint={`${report.acceptedCount} 采用 / ${report.modifiedCount} 修改 / ${report.rejectedCount} 拒绝`} />
          <Metric label="结果回收" value={`${report.completedOutcomeCount}/${report.totalRecommendations}`} hint={`${report.invalidOutcomeCount} 条数据不可用`} />
          <Metric label="适用域变化" value={report.contextShiftCount + report.parameterExtrapolationCount} hint="上下文新组合与参数外推" />
          <Metric label="工程师有用性" value={`${report.usefulCount || 0} / ${report.partlyUsefulCount || 0} / ${report.notUsefulCount || 0}`} hint={`有用 / 部分有用 / 无用；${report.unratedUsefulnessCount || 0} 条未评分`} />
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
            key: "suggestionExecutionKey",
            label: "模型建议 / 实际运行",
            render: (value, row) => <div className="space-y-1 text-xs"><code>{value}</code><div>实际：<code>{row.actualExecutionKey}</code></div><div>模型：{row.modelVersion}</div><StatusBadge value={row.applicability?.status === "in-domain" ? "适用域内" : row.applicability?.status === "context-shift" ? "上下文变化" : row.applicability?.status === "parameter-extrapolation" ? "参数外推" : "历史不足"} /><div className="max-w-64 text-slate-500">{row.applicability?.summary}</div></div>,
          },
          {
            key: "decision",
            label: "工程师选择",
            render: (value, row) => <div className="space-y-2 text-xs"><StatusBadge value={shadowDecisionLabels[value] || value} /><div>有用性：{{ useful: "有用", "partly-useful": "部分有用", "not-useful": "无用" }[row.usefulnessRating] || "未评分"}</div>{(row.engineerSelectedFactors || []).map(factor => <div key={factor.variableCode}>{variableByCode.get(factor.variableCode)?.name || factor.variableCode}：<strong>{formatResearchNumber(factor.value)} {factor.unit}</strong></div>)}</div>,
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
        <Alert tone="info">模型建议 <code>{target.run.executionKey}</code>；请在知道检验结果之前登记实际选择。</Alert>
        <Field label="决策"><Select value={form.decision} onChange={updateDecision}><option value="accepted">采用模型建议</option><option value="modified">修改后采用</option><option value="rejected">不采用建议</option></Select></Field>
        <Field label="对工程判断是否有用" hint="与是否采用分开评价；有用但受现场约束的建议仍可标为有用。"><Select required value={form.usefulnessRating} onChange={update("usefulnessRating")}><option value="useful">有用</option><option value="partly-useful">部分有用</option><option value="not-useful">无用</option></Select></Field>
        <Field label="实际生产运行号" hint="必须与采集周期 ExecutionId 完全一致，结果将通过它自动关联。"><Input required value={form.actualExecutionKey} onChange={update("actualExecutionKey")} /></Field>
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

function TaskDrawer({ task, form, setForm, workspace, memberCandidates, saving, experimentPreview, experimentValidation, onPreviewExperimentDesign, onClose, onSubmit }) {
  const [historicalProcessExecutions, setHistoricalProcessExecutions] = useState([]);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyError, setHistoryError] = useState("");
  const [historyFilter, setHistoryFilter] = useState("");

  useEffect(() => {
    if (task !== "history" || !workspace) return;
    let mounted = true;
    setHistoryLoading(true);
    setHistoryError("");
    const productCode = workspace.project.productName ? `&productCode=${encodeURIComponent(workspace.project.productName)}` : "";
    getJson(`/api/v1/process-executions?status=completed&limit=200${productCode}`)
      .then(response => {
        if (!mounted) return;
        const values = response?.data || [];
        setHistoricalProcessExecutions(values);
        setForm(current => ({ ...current, executionIds: values.map(item => item.executionId) }));
      })
      .catch(requestError => {
        if (!mounted) return;
        setHistoryError(requestError.message || "无法读取已完成运行。");
      })
      .finally(() => { if (mounted) setHistoryLoading(false); });
    return () => { mounted = false; };
  }, [task, workspace?.project?.productName, setForm]);

  if (!task || !workspace) return null;
  const update = name => event => setForm({ ...form, [name]: event.target.value });
  const variables = workspace.project.variables.filter(item => item.role === "control");
  const validatedOperatingRegions = workspace.operatingRegions.filter(item =>
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

  const selectedHistoryExecutionIds = new Set(form.executionIds || []);
  const normalizedHistoryFilter = historyFilter.trim().toLowerCase();
  const visibleHistoricalProcessExecutions = historicalProcessExecutions.filter(execution =>
    !normalizedHistoryFilter || [
      execution.executionId,
      execution.productFamilyCode,
      execution.productCode,
      execution.equipmentId,
      ...(execution.edgeIds || []),
      execution.externalBatchRef,
      execution.outputItemId,
      execution.processSpecificationId,
    ].some(value => String(value || "").toLowerCase().includes(normalizedHistoryFilter)));
  const updateHistoryExecution = (executionId, checked) => {
    const nextIds = new Set(form.executionIds || []);
    if (checked) nextIds.add(executionId);
    else nextIds.delete(executionId);
    setForm({ ...form, executionIds: Array.from(nextIds) });
  };
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
      size={task === "history" ? "xl" : "lg"}
      footer={<><Button disabled={saving} onClick={onClose}>取消</Button><Button variant="primary" disabled={saving || (task === "history" && (form.executionIds?.length || 0) < 2)} type="submit" form="research-task-form">{saving ? "正在保存…" : "保存"}</Button></>}
    >
      <form id="research-task-form" className="space-y-4" onSubmit={onSubmit}>
        {task === "member" && (
          <Field label="成员账户" hint="选择平台账户；项目权限使用不可变用户 ID 关联。">
            <Select required value={form.member} onChange={update("member")}>
              <option value="">请选择账户</option>
              {(memberCandidates || [])
                .filter(user => !workspace.project.memberUserIds?.includes(user.userId) && user.userId !== workspace.project.ownerUserId)
                .map(user => (
                  <option key={user.userId} value={user.userId}>
                    {user.displayName || user.username} · {user.username}{user.disabled ? "（已停用）" : ""}
                  </option>
                ))}
            </Select>
          </Field>
        )}
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
          <Field label="作用链（每行一条）" hint="格式：起点变量 -> 终点变量 | 作用机制 | increase/decrease/nonlinear"><Textarea rows={4} value={form.causalChain} onChange={update("causalChain")} placeholder="melt.temperature -> defect.rate | 黏度下降改善充填 | decrease" /></Field>
          <Field label="时间特征（每行一条）" hint="格式：变量 | 特征代码 | 阶段代码 | 时滞毫秒 | 窗口毫秒"><Textarea rows={4} value={form.temporalFeatures} onChange={update("temporalFeatures")} placeholder="cavity.pressure | pressure.rise-rate | holding | 500 | 3000" /></Field>
          <Field label="交互作用（每行一条）" hint="格式：变量1,变量2 | 交互说明"><Textarea rows={3} value={form.interactions} onChange={update("interactions")} placeholder="melt.temperature,holding.pressure | 高温会放大保压压力对收缩的影响" /></Field>
          <Field label="失效条件（每行一条）" hint="格式：触发条件 | 可观测征兆 | 必须采取的处置"><Textarea rows={3} value={form.failureConditions} onChange={update("failureConditions")} placeholder="材料温度超过降解阈值 | 挥发物或颜色异常 | 停止实验并恢复基线" /></Field>
          <Field label="反证条件（每行一条）" hint="写出出现什么结果时应否定或收缩该假设。"><Textarea required rows={3} value={form.falsificationConditions} onChange={update("falsificationConditions")} /></Field>
        </>}
        {task === "experiment" && <>
          <Field label="实验名称"><Input required value={form.name} onChange={update("name")} /></Field>
          <Field label="验证的假设"><Select value={form.hypothesisId} onChange={update("hypothesisId")}><option value="">不关联具体假设</option>{workspace.hypotheses.map(item => <option key={item.hypothesisId} value={item.hypothesisId}>{item.statement}</option>)}</Select></Field>
          <div className="rounded-xl border border-indigo-200 bg-indigo-50 p-4 space-y-4">
            <div>
              <p className="text-sm font-semibold text-indigo-950">生成实验设计</p>
              <p className="mt-1 text-xs leading-5 text-indigo-800">系统只生成可编辑运行表；保存后仍执行全部安全、目标与对照校验。</p>
            </div>
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="设计方法"><Select value={form.designMethod} onChange={event => setForm({ ...form, designMethod: event.target.value, generatedRunPlan: [] })}><option value="full-factorial">全因子设计</option><option value="fractional-factorial">部分因子设计</option><option value="response-surface">响应面设计</option><option value="latin-hypercube">拉丁超立方</option></Select></Field>
              <Field label="设计变量" hint="按 Ctrl 或 Shift 选择多个可控变量。"><Select multiple size={Math.min(5, Math.max(2, variables.length))} value={form.designVariableCodes || []} onChange={event => setForm({ ...form, designVariableCodes: Array.from(event.target.selectedOptions, option => option.value), generatedRunPlan: [] })}>{variables.map(item => <option key={item.code} value={item.code}>{item.name}（{item.unit}）</option>)}</Select></Field>
            </div>
            <div className="grid gap-4 sm:grid-cols-3">
              <Field label="水平数"><Input disabled={form.designMethod === "fractional-factorial" || form.designMethod === "latin-hypercube"} type="number" min="2" max="5" value={form.designLevels} onChange={update("designLevels")} /></Field>
              <Field label="每条件重复"><Input type="number" min="1" max="5" value={form.designReplicates} onChange={update("designReplicates")} /></Field>
              <Field label="区组数"><Input type="number" min="1" max="5" value={form.designBlocks} onChange={update("designBlocks")} /></Field>
            </div>
            {form.designMethod === "latin-hypercube" && <Field label="样本数"><Input type="number" min="2" max="40" value={form.designSampleCount} onChange={update("designSampleCount")} /></Field>}
            {form.designMethod === "response-surface" && <Field label="响应面族"><Select value={form.responseSurfaceFamily} onChange={update("responseSurfaceFamily")}><option value="central-composite">中心复合设计（CCD）</option><option value="box-behnken">Box–Behnken</option></Select></Field>}
            <Button type="button" onClick={onPreviewExperimentDesign}>生成并预览运行表</Button>
            {experimentPreview && <div className="space-y-2 rounded-lg border border-indigo-200 bg-white p-3 text-xs text-slate-700">
              <div><strong>已生成 {experimentPreview.runPlan?.length || 0} 条运行</strong>{experimentPreview.aliasStructure ? ` · ${experimentPreview.aliasStructure}` : ""}</div>
              {(experimentPreview.warnings || []).map(item => <Alert key={item} tone="warning">{item}</Alert>)}
              <div className="max-h-48 overflow-auto rounded border border-slate-200">
                <table className="w-full text-left"><thead><tr className="bg-slate-50"><th className="p-2">顺序</th><th className="p-2">区组/重复</th><th className="p-2">变量设置</th></tr></thead><tbody>{(form.generatedRunPlan || []).map(run => <tr key={run.executionKey} className="border-t border-slate-100"><td className="p-2">{run.sequence}</td><td className="p-2">{run.blockKey || "—"} / {run.replicateKey || "—"}</td><td className="p-2">{(run.factors || []).map(factor => `${factor.variableCode}=${formatResearchNumber(factor.value)} ${factor.unit}`).join("；")}</td></tr>)}</tbody></table>
              </div>
            </div>}
          </div>
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
                value={form.baselineExecutionKeys || []}
                onChange={event => setForm({
                  ...form,
                  baselineExecutionKeys: Array.from(
                    event.target.selectedOptions,
                    option => option.value,
                  ),
                })}
              >
                {baselineRuns.map(run => (
                  <option key={`${run.experimentName}:${run.executionKey}`} value={run.executionKey}>
                    {run.experimentName} · {run.executionKey}
                  </option>
                ))}
              </Select>
            </Field>
          )}
          <Field label="停止规则"><Textarea required rows={3} value={form.stopRule} onChange={update("stopRule")} /></Field>
          <Field label="回退方案"><Textarea required rows={3} value={form.rollbackPlan} onChange={update("rollbackPlan")} /></Field>
          {experimentValidation && <div className="rounded-xl border border-amber-200 bg-amber-50 p-4">
            <p className="text-sm font-semibold text-amber-950">本实验还差什么</p>
            <ul className="mt-2 space-y-1 text-xs text-amber-900">
              {experimentValidation.isValid ? <li>✓ 当前预检已通过；提交时仍会校验最新项目版本。</li> : (experimentValidation.errors || []).map(issue => <li key={`${issue.field}-${issue.code}`}>✗ {issue.message}{issue.fixHint ? ` ${issue.fixHint}` : ""}</li>)}
            </ul>
          </div>}
        </>}
        {task === "history" && <>
          <Alert tone="info" title="把已有数据变成优化观察">
            系统只读取已完成运行的实际控制参数回读、过程特征和检验记录；不会向设备写入参数。至少选择两种实际工艺规范条件，导入后优化器才能使用这些观察。
          </Alert>
          {historyError && <Alert tone="danger">{historyError}</Alert>}
          {historyLoading ? <Alert tone="info">正在读取可导入的已完成运行…</Alert> : (
            <section className="overflow-hidden rounded-2xl border border-slate-200 bg-slate-50/70" aria-labelledby="history-execution-heading">
              <div className="border-b border-slate-200 bg-white p-4">
                <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                  <div>
                    <h3 id="history-execution-heading" className="font-semibold text-slate-900">选择已完成运行</h3>
                    <p className="mt-1 text-sm text-slate-500">
                      已选 <strong className="font-semibold text-blue-700">{form.executionIds?.length || 0}</strong> / {historicalProcessExecutions.length} 条
                    </p>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    <Button
                      onClick={() => setForm({
                        ...form,
                        executionIds: Array.from(new Set([
                          ...(form.executionIds || []),
                          ...visibleHistoricalProcessExecutions.map(item => item.executionId),
                        ])),
                      })}
                      disabled={visibleHistoricalProcessExecutions.length === 0}
                    >
                      选择当前结果
                    </Button>
                    <Button variant="ghost" onClick={() => setForm({ ...form, executionIds: [] })} disabled={!form.executionIds?.length}>清空</Button>
                  </div>
                </div>
                <Input
                  className="mt-4"
                  type="search"
                  value={historyFilter}
                  onChange={event => setHistoryFilter(event.target.value)}
                  placeholder="搜索运行号、产品、设备、Edge、批次或工艺规范"
                  aria-label="搜索已完成运行"
                />
                <p className="mt-2 text-xs leading-5 text-slate-500">
                  默认选中与项目产品匹配的运行。可跨节点多选；导入前请确认至少包含两种实际工艺规范条件。
                </p>
              </div>
              {visibleHistoricalProcessExecutions.length > 0 ? (
                <div className="grid max-h-[52vh] gap-3 overflow-y-auto p-3 md:grid-cols-2">
                  {visibleHistoricalProcessExecutions.map(execution => {
                    const selected = selectedHistoryExecutionIds.has(execution.executionId);
                    const product = execution.productFamilyCode || execution.productCode || "未标注产品";
                    return (
                      <label
                        key={execution.executionId}
                        className={`flex cursor-pointer gap-3 rounded-xl border p-4 transition ${selected ? "border-blue-400 bg-blue-50 shadow-sm" : "border-slate-200 bg-white hover:border-blue-300 hover:bg-blue-50/40"}`}
                      >
                        <input
                          type="checkbox"
                          className="mt-1 size-4 shrink-0 accent-blue-600"
                          checked={selected}
                          onChange={event => updateHistoryExecution(execution.executionId, event.target.checked)}
                        />
                        <span className="min-w-0 flex-1">
                          <span className="flex items-start justify-between gap-3">
                            <strong className="truncate text-sm font-semibold text-slate-900" title={execution.executionId}>{product}</strong>
                            <span className="shrink-0 text-xs text-slate-500">{execution.completedAt ? new Date(execution.completedAt).toLocaleString("zh-CN") : "完成时间未知"}</span>
                          </span>
                          <span className="mt-2 flex flex-wrap gap-1.5 text-xs">
                            <span className="rounded-md bg-slate-100 px-2 py-1 text-slate-700">设备 {execution.equipmentId || "未标注"}</span>
                            <span className="rounded-md bg-slate-100 px-2 py-1 text-slate-700">Edge {execution.edgeIds?.join(" / ") || "未标注"}</span>
                          </span>
                          <span className="mt-3 grid gap-1 text-xs leading-5 text-slate-600">
                            <span><span className="text-slate-400">工艺规范</span> {execution.processSpecificationId || "未标注"}</span>
                            {(execution.externalBatchRef || execution.outputItemId) && <span><span className="text-slate-400">追溯</span> {[execution.externalBatchRef && `批次 ${execution.externalBatchRef}`, execution.outputItemId && `工件 ${execution.outputItemId}`].filter(Boolean).join(" · ")}</span>}
                            <span className="truncate font-mono text-[11px] text-slate-400" title={execution.executionId}>{execution.executionId}</span>
                          </span>
                        </span>
                      </label>
                    );
                  })}
                </div>
              ) : (
                <div className="p-8 text-center text-sm text-slate-500">{historicalProcessExecutions.length ? "没有匹配的运行，请调整搜索条件。" : "当前没有可导入的已完成运行。"}</div>
              )}
              {(form.executionIds?.length || 0) < 2 && (
                <div className="border-t border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">至少选择 2 条运行后才能保存。</div>
              )}
            </section>
          )}
        </>}
        {task === "claim" && <>
          {beneficialTransfers.length > 0 && <Field label="知识证据类型"><Select value={form.knowledgeSourceType} onChange={update("knowledgeSourceType")}><option value="window">当前项目已验证工艺操作域</option><option value="transfer">经复核的迁移收益</option></Select></Field>}
          {form.knowledgeSourceType !== "transfer" ? (
            <Field label="来源工艺操作域"><Select required value={form.operatingRegionId} onChange={update("operatingRegionId")}>{validatedOperatingRegions.map(item => <option key={item.operatingRegionId} value={item.operatingRegionId}>{item.name}</option>)}</Select></Field>
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
          <Alert tone="warning" title="迁移不是复制工艺规范">
            这里只比较已经完成的两组目标现场实测：一组按源窗口执行，一组从目标条件独立起步。系统不会向设备下发源参数；单次有收益也不能直接沉淀为通用知识。
          </Alert>
          <Field label="源生产工艺操作域" hint="仅列出当前用户可访问且已经生产发布的窗口。">
            <Select required value={form.sourceOperatingRegionId} onChange={update("sourceOperatingRegionId")}>
              {(workspace.transferSources || []).map(item => <option key={item.operatingRegionId} value={item.operatingRegionId}>{item.sourceProjectName} · {item.operatingRegionName} · {item.sourceMaterialName || "材料未声明"}</option>)}
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
        {task === "preregistration" && <>
          <Alert tone="warning" title="冻结前确认">保存后不能覆盖；项目定义变化时必须创建并独立复核新版本。这里记录的是验证协议和当前流程基线，不用于员工绩效评价。</Alert>
          <Field label="数据范围"><Textarea required rows={3} value={form.preregDataScope} onChange={update("preregDataScope")} /></Field>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="数据开始"><Input required type="datetime-local" value={form.preregDataFrom} onChange={update("preregDataFrom")} /></Field>
            <Field label="数据结束"><Input required type="datetime-local" value={form.preregDataTo} onChange={update("preregDataTo")} /></Field>
            <Field label="Edge 编号（可选）"><Input value={form.preregEdgeId} onChange={update("preregEdgeId")} /></Field>
            <Field label="设备编号（可选）"><Input value={form.preregEquipmentId} onChange={update("preregEquipmentId")} /></Field>
            <Field label="数据基线最大运行数"><Input required type="number" min="1" max="5000" value={form.preregMaximumRuns} onChange={update("preregMaximumRuns")} /></Field>
          </div>
          <Field label="纳入方式"><Textarea required rows={3} value={form.preregInclusionMethod} onChange={update("preregInclusionMethod")} /></Field>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="纳入规则（每行一项）"><Textarea required rows={5} value={form.preregInclusionRules} onChange={update("preregInclusionRules")} /></Field>
            <Field label="排除规则（每行一项）"><Textarea required rows={5} value={form.preregExclusionRules} onChange={update("preregExclusionRules")} /></Field>
            <Field label="匹配与分层规则（每行一项）"><Textarea required rows={5} value={form.preregMatchingRules} onChange={update("preregMatchingRules")} /></Field>
            <Field label="比较基线（每行一项）"><Textarea required rows={5} value={form.preregBaselineMethods} onChange={update("preregBaselineMethods")} /></Field>
            <Field label="主要指标（每行一项）"><Textarea required rows={5} value={form.preregPrimaryMetrics} onChange={update("preregPrimaryMetrics")} /></Field>
            <Field label="守门指标（每行一项）"><Textarea required rows={5} value={form.preregGuardrailMetrics} onChange={update("preregGuardrailMetrics")} /></Field>
            <Field label="停止条件（每行一项）"><Textarea required rows={5} value={form.preregStopConditions} onChange={update("preregStopConditions")} /></Field>
            <Field label="否证条件（每行一项）"><Textarea required rows={5} value={form.preregFalsificationConditions} onChange={update("preregFalsificationConditions")} /></Field>
          </div>
          <Card title="工程师当前流程基线" description="记录使用 Ingot 前完成同类任务实际需要的时间和步骤。">
            <Field label="流程名称"><Input required value={form.preregWorkflowName} onChange={update("preregWorkflowName")} /></Field>
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="开始时间"><Input required type="datetime-local" value={form.preregWorkflowStart} onChange={update("preregWorkflowStart")} /></Field>
              <Field label="结束时间"><Input required type="datetime-local" value={form.preregWorkflowEnd} onChange={update("preregWorkflowEnd")} /></Field>
            </div>
            <Field label="步骤与耗时" hint="每行填写：步骤名称|分钟"><Textarea required rows={6} value={form.preregWorkflowSteps} onChange={update("preregWorkflowSteps")} /></Field>
            <Field label="说明（可选）"><Textarea rows={3} value={form.preregWorkflowNotes} onChange={update("preregWorkflowNotes")} /></Field>
          </Card>
        </>}
      </form>
    </Drawer>
  );
}

function groupMechanismUsages(usages) {
  const grouped = new Map();
  for (const usage of usages) {
    const key = `${usage.claimId}:${usage.claimVersion}`;
    const current = grouped.get(key) || { ...usage, usageTypes: [] };
    if (!current.usageTypes.includes(usage.usageType)) current.usageTypes.push(usage.usageType);
    if (!current.appliedClaim && usage.appliedClaim) current.appliedClaim = usage.appliedClaim;
    grouped.set(key, current);
  }
  return [...grouped.values()];
}

function mechanismUsageLabel(value) {
  return ({
    "hard-constraint": "缩窄硬边界",
    "candidate-ranking": "候选偏好排序",
    "knowledge-context": "上下文与解释",
  })[value] || value;
}

function mechanismEvidenceLabel(value) {
  return ({
    "knowledge-source": "原始知识来源",
    "knowledge-fragment": "可定位知识片段",
    "experiment-result": "正式实验结果",
  })[value] || value;
}

function formatMechanismConstraint(constraint) {
  const lower = constraint.minimum == null ? "−∞" : formatResearchNumber(constraint.minimum);
  const upper = constraint.maximum == null ? "+∞" : formatResearchNumber(constraint.maximum);
  return `${lower} ～ ${upper} ${constraint.unit || ""}`.trim();
}

function lines(value) {
  return String(value || "").split("\n").map(item => item.trim()).filter(Boolean);
}

function parseWorkflowSteps(value) {
  return lines(value).map((item, index) => {
    const separator = item.lastIndexOf("|");
    const name = separator < 0 ? "" : item.slice(0, separator).trim();
    const minutes = separator < 0 ? Number.NaN : Number(item.slice(separator + 1).trim());
    if (!name || !Number.isFinite(minutes) || minutes < 0) throw new Error("流程步骤必须逐行填写为：步骤名称|分钟。");
    return { sequence: index + 1, name, minutes };
  });
}

function parseCausalChain(value) {
  return lines(value).map(item => {
    const [edge, mechanism, direction = "unknown"] = item.split("|").map(part => part.trim());
    const [fromVariableCode, toVariableCode] = edge.split("->").map(part => part.trim());
    if (!fromVariableCode || !toVariableCode || !mechanism) throw new Error("作用链格式不完整。");
    return { fromVariableCode, toVariableCode, mechanism, direction };
  });
}

function parseTemporalFeatures(value) {
  return lines(value).map(item => {
    const [variableCode, featureCode, phaseCode, delay, window] = item.split("|").map(part => part.trim());
    if (!variableCode || !featureCode) throw new Error("时间特征格式不完整。");
    return { variableCode, featureCode, phaseCode: phaseCode || null, delayMilliseconds: delay ? Number(delay) : null, windowMilliseconds: window ? Number(window) : null };
  });
}

function parseInteractions(value) {
  return lines(value).map(item => {
    const [codes, description] = item.split("|").map(part => part.trim());
    if (!description) throw new Error("交互作用格式不完整。");
    return { variableCodes: codes.split(",").map(code => code.trim()).filter(Boolean), description };
  });
}

function parseFailureConditions(value) {
  return lines(value).map(item => {
    const [condition, observableSignal, requiredResponse] = item.split("|").map(part => part.trim());
    if (!condition || !observableSignal || !requiredResponse) throw new Error("失效条件格式不完整。");
    return { condition, observableSignal, requiredResponse };
  });
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
