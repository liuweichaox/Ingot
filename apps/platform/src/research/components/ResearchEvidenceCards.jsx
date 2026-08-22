// 展示研发项目中的历史回放、准入、回退、在线与影子证据，不发起状态变更。
import { formatResearchNumber, shadowDecisionLabels } from "../researchProjectModel";
import { Alert, Button, Card, DataTable, EmptyState, Metric, StatusBadge } from "../../ui/components";

export function HistoricalReplayCard({ reports, currentUserId, onReview }) {
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
            render: (value, row) => <div className="max-w-96 text-xs leading-5 text-slate-600">{(value || []).map((item, index) => <div key={`failure:${index}:${item}`}>失败：{item}</div>)}<div>限制：{row.limitations}</div></div>,
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

export function OnlineAdmissionCard({ evidence }) {
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
            {(evidence.failures || []).map((item, index) => <div key={`failure:${index}:${item}`}>{item}</div>)}
          </Alert>
        )}
        {(evidence.warnings || []).length > 0 && (
          <Alert tone="warning" title="运行前必须确认">
            {(evidence.warnings || []).map((item, index) => <div key={`warning:${index}:${item}`}>{item}</div>)}
          </Alert>
        )}
      </div>
    </Card>
  );
}

export function RollbackDrillCard({ drills, currentUserId, onReview }) {
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
          { key: "evidence", label: "实际证据", render: (_, row) => <div className="max-w-72 text-xs leading-5">{(row.observedActions || []).map((value, index) => <div key={`action:${index}:${value}`}>· {value}</div>)}<div>{row.evidenceReference}</div></div> },
          { key: "status", label: "结论", render: (value, row) => <div className="space-y-1 text-xs"><StatusBadge value={row.passed ? "演练通过" : "演练失败"} /><StatusBadge value={value === "reviewed" ? "已独立复核" : "待独立复核"} /></div> },
          { key: "actions", label: "操作", render: (_, row) => row.status === "recorded" && row.conductedBy !== currentUserId ? <Button onClick={event => { event.stopPropagation(); onReview(row); }}>复核演练证据</Button> : row.status === "recorded" ? <span className="text-xs text-slate-500">等待其他工程师复核</span> : "已冻结" },
        ]} />
      )}
    </Card>
  );
}

export function OnlineCampaignCard({ report, objectiveByCode }) {
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

export function ShadowEvidenceCard({ recommendations, report, variableByCode, objectiveByCode, onMaterialize }) {
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
            render: (value, row) => <div className="space-y-2 text-xs"><StatusBadge value={shadowDecisionLabels[value] || value} /><div>有用性：{{ useful: "有用", "partly-useful": "部分有用", "not-useful": "无用" }[row.usefulnessRating] || "未评分"}</div>{(row.engineerSelectedFactors || []).map((factor, index) => <div key={`${factor.variableCode}:${index}`}>{variableByCode.get(factor.variableCode)?.name || factor.variableCode}：<strong>{formatResearchNumber(factor.value)} {factor.unit}</strong></div>)}</div>,
          },
          {
            key: "reason",
            label: "拒绝原因 / 现场限制",
            render: (_, row) => <div className="max-w-72 text-xs leading-5 text-slate-600"><div>{row.rejectionReason || "采用建议，无拒绝原因"}</div>{(row.siteLimitations || []).map((value, index) => <div key={`limitation:${index}:${value}`}>限制：{value}</div>)}</div>,
          },
          {
            key: "outcome",
            label: "源数据结果",
            render: value => value ? <div className="space-y-1 text-xs"><StatusBadge value={value.validForOptimization ? "数据完整" : "数据不足"} />{Object.entries(value.outcomes || {}).map(([code, number]) => <div key={`outcome:${code}`}>{objectiveByCode.get(code)?.name || code}：<strong>{formatResearchNumber(number)}</strong></div>)}{Object.entries(value.settingDeviationFromEngineerSelection || {}).map(([code, number]) => <div key={`deviation:${code}`}>实际设置偏差 {variableByCode.get(code)?.name || code}：<strong>{formatResearchNumber(number)}</strong></div>)}<code title={value.sourceContentHash}>{String(value.sourceContentHash).slice(0, 12)}…</code></div> : <span className="text-xs text-slate-500">等待实际运行与检验</span>,
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
