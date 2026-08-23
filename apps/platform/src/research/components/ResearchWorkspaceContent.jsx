// 展示研发项目工作区的证据、实验、验证与复用状态，通过回调交还所有写操作。
import { Link } from "react-router";
import {
  experimentScale,
  formatResearchNumber,
  shadowDecisionLabels,
  statusLabels,
} from "../researchProjectModel";
import {
  formatMechanismConstraint,
  groupMechanismUsages,
  mechanismEvidenceLabel,
  mechanismUsageLabel,
} from "../researchProjectPresentation";
import {
  HistoricalReplayCard,
  OnlineAdmissionCard,
  OnlineCampaignCard,
  RollbackDrillCard,
  ShadowEvidenceCard,
} from "./ResearchEvidenceCards";
import {
  Alert,
  Button,
  Card,
  DataTable,
  EmptyState,
  Metric,
  StatusBadge,
} from "../../ui/components";

export function MemberManagementButton({ allowed, onClick }) {
  return allowed ? <Button onClick={onClick}>添加协作成员</Button> : null;
}

export function WorkspaceContent({
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
  const methodAdmission = workspace.methodAdmission;
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
  const methodEligible = methodAdmission?.eligible === true;
  const methodAdmissionReason = (methodAdmission?.failures || []).join("；") ||
    "正在核对当前策略、机理快照和历史回放。";
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
          {!methodEligible && (
            <Alert tone="warning" title="序贯优化已暂停">
              <p>{methodAdmissionReason}</p>
              <p className="mt-1">
                当前仍可手动设计正则化响应面或适用的传统 DOE；完成当前版本历史回放并由另一名工程师审核后，再重新评估准入。
              </p>
              <div className="mt-3 flex flex-wrap gap-2">
                {project.status !== "draft" && canEdit && <Button onClick={() => onTask("experiment")}>手动设计实验</Button>}
                {project.status !== "draft" && canEdit && Number(observationSummary?.validObservationCount || 0) >= 3 && <Button onClick={onRunHistoricalReplay}>运行当前版本历史回放</Button>}
              </div>
            </Alert>
          )}
          <div className="mt-5 grid gap-4 lg:grid-cols-[minmax(0,1fr)_18rem]">
            <div className="flex flex-wrap content-start gap-2">
              <Button onClick={() => onAskAi(project.projectId)}>让 AI 协助分析</Button>
              <MemberManagementButton allowed={canManageMembers} onClick={() => onTask("member")} />
              {canEdit && hypotheses.length === 0 && <Button variant="primary" onClick={() => onTask("hypothesis")}>提出第一个假设</Button>}
              {project.status !== "draft" && canEdit && <Button onClick={() => onTask("history")}>导入历史运行</Button>}
              {project.status !== "draft" && canEdit && hypotheses.length > 0 && !hasRunningExperiment && <Button variant="primary" disabled={!methodEligible} title={!methodEligible ? methodAdmissionReason : "已通过当前方法准入"} onClick={() => onGenerateOptimizationSuggestions()}>智能设计下一组实验</Button>}
              {project.status !== "draft" && canEdit && hypotheses.length > 0 && <Button disabled={!methodEligible} title={!methodEligible ? methodAdmissionReason : "仅生成旁路建议"} onClick={() => onGenerateOptimizationSuggestions("reach-specification", null, "shadow")}>生成影子建议</Button>}
              {project.status !== "draft" && canEdit && hypotheses.length > 0 && (
                <Button
                  variant="primary"
                  disabled={!methodEligible || !onlineAdmission?.eligible || hasRunningExperiment}
                  title={!methodEligible ? methodAdmissionReason : !onlineAdmission?.eligible ? (onlineAdmission?.failures || []).join("；") : "每次只生成一条，仍需工程师逐条确认"}
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
              <p className={`mt-2 text-xs font-medium ${methodEligible ? "text-emerald-700" : "text-amber-700"}`}>
                {methodEligible ? "方法准入已通过" : "方法准入未通过，已切换到工程师设计路径"}
              </p>
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
                    {runs.map((run, runIndex) => {
                      const runIdentifier = run.executionKey || run.runKey || `run-${runIndex + 1}`;
                      return (
                      <div key={`${row.experimentId}:${runIdentifier}:${runIndex}`} className="rounded-lg border border-slate-200 bg-slate-50 p-2">
                        <div className="flex items-center justify-between gap-2">
                          <code className="block text-xs font-semibold text-slate-700">{runIdentifier}</code>
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
                        {(run.factors || []).map((factor, factorIndex) => {
                          const variable = variableByCode.get(factor.variableCode);
                          return (
                            <div key={`${factor.variableCode}:${factorIndex}`} className="mt-1 text-xs text-slate-600">
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
                      );
                    })}
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
                                  {(claim?.forbiddenCombinations || []).length > 0 && (
                                    <div className="mt-2 space-y-1">
                                      <strong className="text-[11px] text-slate-500">禁止参数组合</strong>
                                      {claim.forbiddenCombinations.map(combination => (
                                        <div className="rounded-md border border-red-100 bg-red-50 px-2 py-1 text-red-900" key={combination.combinationId}>
                                          <strong>{combination.name}</strong>
                                          <span className="ml-1">
                                            {combination.factors.map(factor => `${variableByCode.get(factor.variableCode)?.name || factor.variableCode} ${formatMechanismConstraint(factor)}`).join(" 且 ")}
                                          </span>
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
                    {(value || []).map((metric, metricIndex) => {
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
                        <div key={`${metric.objectiveCode}:${metricIndex}`} className="rounded-lg border border-slate-200 bg-slate-50 p-2 text-xs">
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
                    {(value || []).map((variable, variableIndex) => {
                      const definition = variableByCode.get(variable.variableCode);
                      return (
                        <div key={`${variable.variableCode}:${variableIndex}`} className="text-xs">
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
                    const controlledExperimentIds = new Set(experiments
                      .filter(experiment => experiment.executionCategory === "controlled-online"
                        && experiment.optimization?.mode === "controlled"
                        && experiment.status === "completed"
                        && ["accepted", "modified"].includes(experiment.controlledDecision?.decision))
                      .map(experiment => experiment.experimentId));
                    const controlledRunCount = experimentResults
                      .filter(result => controlledExperimentIds.has(result.experimentId)
                        && result.calculatedFromSource
                        && result.safetyPassed)
                      .reduce((count, result) => count + (result.runObservations?.filter(observation => observation.validForOptimization !== false).length || 0), 0);
                    if (controlledRunCount < 3) {
                      return <span className="text-xs text-amber-700">先完成至少 3 条受控在线运行并回收源数据结果</span>;
                    }
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
                render: value => <div className="max-w-72 text-xs leading-5">{(value || []).map((item, index) => <div key={`${item.field}:${index}`}>{item.field}：{item.sourceValue || "未声明"} → {item.targetValue || "未声明"}</div>)}</div>,
              },
              {
                key: "failures",
                label: "失败与边界",
                render: (value, row) => <div className="max-w-96 text-xs leading-5 text-slate-600">{(value || []).map((item, index) => <div key={`failure:${index}:${item}`}>失败：{item}</div>)}{(row.warnings || []).map((item, index) => <div key={`warning:${index}:${item}`}>提示：{item}</div>)}</div>,
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
