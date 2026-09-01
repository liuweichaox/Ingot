// 展示真实生产证据和下一配方建议闭环；所有写操作由页面容器处理。
import {
  formatResearchNumber,
  shortResearchHash,
  statusLabels,
  usefulnessLabels,
} from "../researchProjectModel";
import {
  Alert,
  Button,
  Card,
  DataTable,
  EmptyState,
  Metric,
  StatusBadge,
} from "../../ui/components";

function formatResearchDate(value) {
  return value ? new Date(value).toLocaleString("zh-CN") : "时间未知";
}

function normalizeFlowAction(value) {
  const action = typeof value === "string" ? value : value?.action || value?.code || value?.name;
  return String(action || "").trim().toLowerCase();
}

function flowAllows(row, ...actions) {
  if (!row.hasExplicitAllowedActions) return true;
  const expected = new Set(actions.map(normalizeFlowAction));
  if (Array.isArray(row.allowedActions)) {
    return row.allowedActions.some(action => expected.has(normalizeFlowAction(action)));
  }
  return Object.entries(row.allowedActions || {}).some(([action, allowed]) =>
    allowed === true && expected.has(normalizeFlowAction(action)));
}

function recommendationStateLabel(row) {
  if (row.outcome) return "结果已冻结";
  if (row.decision?.decision === "rejected") return "已拒绝，流程结束";
  if (row.actualExecutionKey) return "等待结果冻结";
  if (row.decision) return "等待关联实际运行";
  return "等待工程师决定";
}

function parameterList(parameters, variableByCode) {
  if (!parameters?.length) return "—";
  return parameters.map(parameter => {
    const variable = variableByCode.get(parameter.variableCode);
    return `${variable?.name || parameter.variableCode} ${formatResearchNumber(parameter.value)} ${parameter.unit || variable?.unit || ""}`.trim();
  }).join("；");
}

function outcomeList(outcome, objectiveByCode) {
  const values = outcome?.outcomes || outcome?.objectiveValues || {};
  const entries = Array.isArray(values)
    ? values.map(value => [value.objectiveCode, value.value ?? value.observedValue])
    : Object.entries(values);
  if (!entries.length) return "等待质量结果";
  return entries.map(([code, value]) => {
    const objective = objectiveByCode.get(code);
    return `${objective?.name || code} ${formatResearchNumber(value)} ${objective?.unit || ""}`.trim();
  }).join("；");
}

const auditActionLabels = {
  created: "已创建",
  generated: "已生成建议",
  decided: "已登记决定",
  "execution-linked": "已关联运行",
  "outcome-materialized": "已冻结结果",
  "status-changed": "已变更状态",
};

const auditResourceLabels = {
  "research-project": "优化任务",
  "recipe-recommendation": "下一配方建议",
  "recipe-recommendation-decision": "工程师决定",
  "process-execution": "实际生产运行",
  "quality-result": "质量结果",
};

export function MemberManagementButton({ allowed, onClick }) {
  return allowed ? <Button onClick={onClick}>添加协作成员</Button> : null;
}

export function WorkspaceContent({
  workspace,
  loading,
  historyLoading,
  onLoadOlderHistory,
  onGenerateRecipeRecommendation,
  onRecipeRecommendationDecision,
  onLinkRecipeRecommendationExecution,
  onMaterializeRecipeRecommendationOutcome,
  onAskAi,
}) {
  if (!workspace) return null;

  const { project, recipeRecommendationFlows = [], audit = [] } = workspace;
  const observationSummary = workspace.optimizationObservationSummary;
  const canEdit = !["completed", "archived"].includes(project.status);
  const variableByCode = new Map((project.variables || []).map(item => [item.code, item]));
  const objectiveByCode = new Map((project.objectives || []).map(item => [item.code, item]));
  const dailyRecipeItems = recipeRecommendationFlows.map(flow => {
    const recommendation = flow.recommendation || {
      recommendationId: flow.recommendationId,
      generatedAt: flow.generatedAt,
      projectSnapshotHash: flow.projectSnapshotHash,
    };
    const item = flow.item || flow.recommendationItem || {
      recommendationKey: flow.recommendationKey,
      parameters: flow.suggestedParameters || flow.parameters || [],
      prediction: flow.prediction,
    };
    const decision = flow.decision || flow.engineerDecision || null;
    return {
      ...flow,
      recordId: flow.flowId || `${recommendation.recommendationId}:${item.recommendationKey}`,
      recommendation,
      item,
      decision,
      outcome: flow.outcome || decision?.outcome || null,
      actualExecutionKey: flow.actualExecutionKey || decision?.actualExecutionKey || null,
      hasExplicitAllowedActions: Object.prototype.hasOwnProperty.call(flow, "allowedActions"),
      allowedActions: flow.allowedActions,
    };
  });
  const validRunCount = Number(observationSummary?.validObservationCount || 0);
  const pendingDecisionCount = dailyRecipeItems.filter(row => !row.decision).length;
  const pendingRunCount = dailyRecipeItems.filter(row => row.decision?.decision !== "rejected" && row.decision && !row.actualExecutionKey).length;
  const pendingOutcomeCount = dailyRecipeItems.filter(row => row.decision?.decision !== "rejected" && row.actualExecutionKey && !row.outcome).length;
  const currentStep = project.status === "draft"
    ? ["定义优化范围", "确认质量目标、可调配方参数与安全边界。"]
    : validRunCount === 0
      ? ["等待真实运行证据", "系统会自动关联已完成生产运行、实际参数回读和质量结果。"]
      : pendingDecisionCount > 0
        ? ["登记工程师决定", "为每条建议冻结采用、修改或拒绝以及原因。"]
        : pendingRunCount > 0
          ? ["关联实际运行", "决定冻结后，由工程师关联后续启动的真实生产运行。"]
          : pendingOutcomeCount > 0
            ? ["冻结实际结果", "运行与质量结果齐备后，系统把源证据冻结为闭环结果。"]
            : ["持续优化", "后续真实生产运行会持续丰富证据，并可生成下一配方建议。"];
  const workflowSteps = [
    { title: "定义", description: "目标与边界", state: project.status === "draft" ? "current" : "done" },
    { title: "证据", description: "真实生产运行", state: validRunCount > 0 ? "done" : project.status === "draft" ? "upcoming" : "current" },
    { title: "建议", description: "下一配方", state: dailyRecipeItems.length ? "done" : validRunCount > 0 ? "current" : "upcoming" },
    { title: "决定", description: "工程师确认", state: pendingDecisionCount > 0 ? "current" : dailyRecipeItems.some(row => row.decision) ? "done" : "upcoming" },
    { title: "闭环", description: "运行与质量结果", state: pendingRunCount || pendingOutcomeCount ? "current" : dailyRecipeItems.some(row => row.outcome) ? "done" : "upcoming" },
  ];
  const visibleAudit = audit.filter(entry => Object.hasOwn(auditResourceLabels, entry.resourceType));

  return (
    <div className="space-y-5">
      {loading && <Alert tone="info">正在更新真实运行证据与建议闭环…</Alert>}
      {Object.values(workspace.nextCursors || {}).some(Boolean) && (
        <Alert tone="info" title="当前先显示最近 100 条记录">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <span>下一配方建议和审计记录按游标分页，可继续读取更早历史。</span>
            <Button disabled={historyLoading} onClick={onLoadOlderHistory}>{historyLoading ? "正在加载…" : "加载更早记录"}</Button>
          </div>
        </Alert>
      )}

      <section className="border border-slate-200 bg-white p-5">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <p className="text-[13px] font-medium text-slate-500">当前决策</p>
            <h3 className="mt-1 text-lg font-semibold text-slate-950">{currentStep[0]}</h3>
            <p className="mt-1 max-w-2xl text-sm leading-6 text-slate-600">{currentStep[1]}</p>
          </div>
          <StatusBadge value={project.status} label={statusLabels[project.status] || project.status} />
        </div>
        <div className="mt-5 flex flex-wrap gap-2">
          <Button onClick={() => onAskAi(project.projectId)}>让 AI 协助分析</Button>
          {project.status !== "draft" && canEdit && (
            <Button
              variant="primary"
              disabled={validRunCount < 3}
              title={validRunCount < 3 ? "至少需要 3 条有效真实生产运行" : "建议不会自动下发，必须由工程师确认"}
              onClick={onGenerateRecipeRecommendation}
            >
              生成下一配方建议
            </Button>
          )}
        </div>
      </section>

      <Card title="真实运行证据" description="系统只使用可关联实际参数、过程轨迹和质量结果的生产运行作为建议依据。">
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <Metric label="有效运行" value={validRunCount} hint="已通过关联与完整性校验" />
          <Metric label="候选运行" value={Number(observationSummary?.candidateRunCount || 0)} hint="等待完整过程或质量数据" />
          <Metric label="已关联运行" value={Number(observationSummary?.linkedExecutionCount || observationSummary?.observedExecutionKeys?.length || 0)} hint="可追溯到实际生产身份" />
          <Metric label="质量目标" value={(project.objectives || []).length} hint="项目定义的质量结果" />
        </div>
        {validRunCount === 0 && <EmptyState title="尚无可用真实运行证据" description="完成生产运行并回收质量结果后，系统会自动装配可用证据。" />}
      </Card>

      <section className="space-y-5">
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
          {workflowSteps.map(step => <div key={step.title} className="border border-slate-200 bg-white px-3 py-3">
            <div className="flex items-center justify-between gap-2"><strong className="text-sm text-slate-900">{step.title}</strong><StatusBadge value={step.state === "done" ? "已完成" : step.state === "current" ? "进行中" : "待开始"} /></div>
            <div className="mt-1 text-[13px] text-slate-500">{step.description}</div>
          </div>)}
        </div>
        <Card title="日常下一配方" description="每条建议都要记录工程师最终决定、实际生产运行和源数据结果，形成不可覆盖的闭环证据。">
          {dailyRecipeItems.length === 0 ? (
            <EmptyState title="尚无下一配方建议" description={validRunCount >= 3 ? "可基于现有真实运行生成下一配方建议。" : "积累至少 3 条有效真实生产运行后即可生成建议。"} />
          ) : (
            <DataTable rows={dailyRecipeItems} getRowKey={row => row.recordId} columns={[
              {
                key: "recommendation",
                label: "建议配方",
                render: (_, row) => <div className="min-w-56 space-y-1 text-[13px]">
                  <div>{parameterList(row.item.parameters, variableByCode)}</div>
                  <div className="text-slate-500">生成于 {formatResearchDate(row.recommendation.generatedAt)}</div>
                  {row.recommendation.projectSnapshotHash && <code title={row.recommendation.projectSnapshotHash}>{shortResearchHash(row.recommendation.projectSnapshotHash)}</code>}
                </div>,
              },
              {
                key: "prediction",
                label: "预测与依据",
                render: (_, row) => <div className="max-w-80 text-[13px] leading-5">
                  <div>{outcomeList({ outcomes: row.item.prediction?.objectives }, objectiveByCode)}</div>
                  {row.item.prediction?.feasibilityProbability != null && <div>可行性 {Math.round(Number(row.item.prediction.feasibilityProbability) * 100)}%</div>}
                  {row.item.prediction?.rationale && <div className="mt-1 text-slate-500">依据：{row.item.prediction.rationale}</div>}
                </div>,
              },
              {
                key: "decision",
                label: "工程师决定",
                render: (_, row) => row.decision ? <div className="max-w-72 space-y-1 text-[13px]">
                  <StatusBadge value={row.decision.decision === "accepted" ? "采用" : row.decision.decision === "modified" ? "修改" : "拒绝"} />
                  {row.decision.engineerSelectedParameters?.length > 0 && <div>工程选择 {parameterList(row.decision.engineerSelectedParameters, variableByCode)}</div>}
                  {row.decision.reason && <div>原因：{row.decision.reason}</div>}
                  {row.decision.usefulnessRating && <div>有用性：{usefulnessLabels[row.decision.usefulnessRating] || row.decision.usefulnessRating}</div>}
                  <div className="text-slate-500">决定人：{row.decision.decidedBy || "—"}</div>
                </div> : <span className="text-[13px] text-slate-500">待工程师确认</span>,
              },
              {
                key: "execution",
                label: "实际运行与质量结果",
                render: (_, row) => <div className="max-w-72 space-y-1 text-[13px]">
                  <div>实际运行：<code>{row.actualExecutionKey || "—"}</code></div>
                  <div>{row.outcome ? outcomeList(row.outcome, objectiveByCode) : row.decision?.decision === "rejected" ? "终态：无需关联运行或回收结果" : "等待实际运行与质量结果"}</div>
                  {row.outcome?.sourceContentHash && <code title={row.outcome.sourceContentHash}>{shortResearchHash(row.outcome.sourceContentHash)}</code>}
                </div>,
              },
              {
                key: "action",
                label: "下一步",
                render: (_, row) => {
                  if (!canEdit) return <span className="text-[13px] text-slate-500">项目只读</span>;
                  if (!row.decision && flowAllows(row, "decide", "decision")) return <Button onClick={event => { event.stopPropagation(); onRecipeRecommendationDecision(row.recommendation, row.item); }}>接受 / 修改 / 拒绝</Button>;
                  if (row.decision?.decision === "rejected") return <span className="text-[13px] text-slate-500">已拒绝，流程结束</span>;
                  if (!row.actualExecutionKey && flowAllows(row, "link-execution", "execution-link")) return <Button onClick={event => { event.stopPropagation(); onLinkRecipeRecommendationExecution(row.decision); }}>关联实际运行</Button>;
                  if (!row.outcome && flowAllows(row, "materialize-outcome", "outcome")) return <Button onClick={event => { event.stopPropagation(); onMaterializeRecipeRecommendationOutcome(row.decision); }}>冻结实际结果</Button>;
                  return <span className="text-[13px] text-slate-500">{recommendationStateLabel(row)}</span>;
                },
              },
            ]} />
          )}
        </Card>
      </section>

      <Card title="审计记录" description="项目状态、建议决定、运行关联和结果冻结均保留操作者和时间。">
        {visibleAudit.length === 0 ? <EmptyState title="尚无审计记录" description="项目发生可审计操作后会在这里显示。" /> : (
          <DataTable rows={visibleAudit} keyField="entryId" columns={[
            { key: "action", label: "操作", render: value => <StatusBadge value={auditActionLabels[value] || value} /> },
            { key: "resourceType", label: "对象", render: (value, row) => <div className="space-y-1 text-[13px]"><strong className="text-slate-900">{auditResourceLabels[value] || "项目记录"}</strong><div><code>{row.resourceId}</code></div></div> },
            { key: "userId", label: "操作者", render: value => value || "未知" },
            { key: "createdAt", label: "时间", render: value => formatResearchDate(value) },
          ]} />
        )}
      </Card>
    </div>
  );
}
