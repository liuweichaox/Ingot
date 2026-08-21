
import { useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router";
import { getJson, postJson } from "../api/http";
import { extractRows, useApi } from "../hooks/useApi";
import { Alert, Badge, Button, Card, ConclusionBoundary, DataTable, EmptyState, EvidenceLevel, Field, Input, Metric, Page, Select, StatusBadge, Textarea, notify } from "../ui/components";
import { contextFieldLabel, formatTime, formatInteger, formatDuration, objectTypeLabel, LoadingCard } from "./shared";

const comparisonFeatureLabels = {
  min: "最小值",
  max: "最大值",
  mean: "平均值",
  stddev: "波动",
};

const comparisonContextLabels = {
  product_family_code: "产品系列",
  product_code: "产品代码",
  equipment_id: "生产设备",
  tooling_assembly_id: "工装总成",
  process_specification_id: "工艺规范",
  material_lot_ref: "材料批次",
  recipe_id: "配方",
};

const readinessBlockingReasonLabels = {
  "quality-outcomes-missing": "质量结果尚未与运行关联",
  "outcome-class-missing": "合格或不合格类别样本不足",
  "effective-weight-insufficient": "有效证据权重不足",
  "process-data-unavailable": "部分运行的过程数据不可用",
};

function formatDecimal(value) {
  if (!Number.isFinite(Number(value))) return "—";
  return Number(value).toLocaleString("zh-CN", { maximumFractionDigits: 3 });
}

function MatchingContext({ value }) {
  const entries = Object.entries(value || {});
  return (
    <section className="mb-4 min-w-0 rounded-xl border border-slate-200 bg-slate-50/70 p-4" aria-labelledby="matching-context-title">
      <div className="flex flex-col gap-1 sm:flex-row sm:items-baseline sm:justify-between">
        <h4 id="matching-context-title" className="text-sm font-semibold text-slate-900">同类运行匹配条件</h4>
        <p className="text-xs text-slate-500">对比运行必须具有相同的上下文值</p>
      </div>
      {entries.length ? (
        <dl className="mt-3 grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
          {entries.map(([key, contextValue]) => (
            <div key={key} className="min-w-0 rounded-lg border border-slate-200 bg-white px-3 py-2.5">
              <dt className="text-xs text-slate-500">{comparisonContextLabels[key] || key}</dt>
              <dd className="mt-1 min-w-0 break-words text-sm font-semibold leading-5 text-slate-900">{String(contextValue)}</dd>
            </div>
          ))}
        </dl>
      ) : (
        <p className="mt-3 text-sm text-slate-500">未记录匹配条件。</p>
      )}
    </section>
  );
}

export function AnalysisReadinessCard({ diagnosis = {} }) {
  const readiness = diagnosis.readiness || { mode: "descriptive-only", blockingReasons: [] };
  return (
    <Card title="分析就绪度" description="数据不足时只展示描述性事实，不生成候选原因。">
      <div className="grid gap-3 md:grid-cols-3">
        <Metric label="当前模式" value={{
          "descriptive-only": "仅描述性统计",
          exploratory: "探索性候选",
          "candidate-ranking": "候选排序",
        }[readiness.mode] || "仅描述性统计"} />
        <Metric label="已调整上下文" value={(diagnosis.adjustedContextVariables || []).length} hint={(diagnosis.adjustedContextVariables || []).map(contextFieldLabel).join("、") || "未启用"} />
        <Metric label="混杂敏感性" value="暂不可估" hint={diagnosis.sensitivityAssessment?.reason || "缺少可解释的风险比和置信区间"} />
      </div>
      {(readiness.blockingReasons || []).length > 0 && (
        <ul className="mt-3 list-disc space-y-1 pl-5 text-sm text-amber-800">
          {readiness.blockingReasons.map(reason => <li key={reason}>{readinessBlockingReasonLabels[reason] || reason}</li>)}
        </ul>
      )}
      {(diagnosis.observedPossibleConfounders || []).length > 0 && (
        <p className="mt-3 text-sm text-slate-700">已观测到的组间不平衡：{diagnosis.observedPossibleConfounders.map(contextFieldLabel).join("、")}</p>
      )}
      {(diagnosis.knownUnmeasuredConfounders || []).length > 0 && (
        <p className="mt-3 text-sm text-slate-700">已知但未记录：{diagnosis.knownUnmeasuredConfounders.map(item => item.name).join("、")}</p>
      )}
    </Card>
  );
}

export function ExecutionComparisonPage() {
  const [params] = useSearchParams();
  const requestedSiteId = params.get("siteId") || "";
  const [baseline, setBaseline] = useState(params.get("executionId") || "");
  const [candidate, setCandidate] = useState("");
  const [comparisonScope, setComparisonScope] = useState("cohort");
  const [result, setResult] = useState(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [executions, setProcessExecutions] = useState([]);
  const [linkedBaseline, setLinkedBaseline] = useState(null);
  const [catalogLoading, setCatalogLoading] = useState(true);
  const [executionFilter, setProcessExecutionFilter] = useState("");
  const [researchProjects, setResearchProjects] = useState([]);
  const [researchProjectId, setResearchProjectId] = useState("");
  const [additionalConfounders, setAdditionalConfounders] = useState("");
  useEffect(() => {
    let mounted = true;
    getJson("/api/v1/research-projects?limit=100").then(projectPayload => {
      if (mounted) setResearchProjects(projectPayload?.data || []);
    }).catch(() => {
      if (mounted) setResearchProjects([]);
    });
    return () => { mounted = false; };
  }, []);
  useEffect(() => {
    let mounted = true;
    const search = executionFilter.trim();
    const query = new URLSearchParams({ status: "completed", limit: "200" });
    if (requestedSiteId) query.set("siteId", requestedSiteId);
    if (search) query.set("search", search);
    setCatalogLoading(true);
    getJson(`/api/v1/process-executions?${query}`).then(executionPayload => {
      if (!mounted) return;
      const loadedExecutions = extractRows(executionPayload);
      setProcessExecutions(loadedExecutions);
      if (!baseline && !search && loadedExecutions.length > 0) setBaseline(loadedExecutions[0].executionId);
    }).catch(requestError => {
      if (!mounted) return;
      setProcessExecutions([]);
      setError(requestError.message || "无法读取可比较的生产运行。");
    }).finally(() => {
      if (mounted) setCatalogLoading(false);
    });
    return () => { mounted = false; };
  }, [executionFilter, requestedSiteId]);

  useEffect(() => {
    let mounted = true;
    if (!baseline || executions.some(item => item.executionId === baseline)) {
      setLinkedBaseline(null);
      return () => { mounted = false; };
    }
    getJson(`/api/v1/process-executions?executionId=${encodeURIComponent(baseline)}&siteId=${encodeURIComponent(requestedSiteId)}&limit=1`)
      .then(payload => {
        if (mounted) setLinkedBaseline(extractRows(payload)[0] || null);
      })
      .catch(() => {
        if (mounted) setLinkedBaseline(null);
      });
    return () => { mounted = false; };
  }, [baseline, executions, requestedSiteId]);

  const baselineProcessExecution = executions.find(item => item.executionId === baseline) || linkedBaseline;
  const normalizedProcessExecutionFilter = executionFilter.trim().toLowerCase();
  const visibleProcessExecutions = executions.filter(item => !normalizedProcessExecutionFilter || [
    item.executionId,
    item.productFamilyCode,
    item.productCode,
    item.equipmentId,
    item.processSpecificationId,
  ].some(value => String(value || "").toLowerCase().includes(normalizedProcessExecutionFilter)));
  const comparableProcessExecutions = executions.filter(item =>
    item.executionId !== baseline &&
    item.siteId === baselineProcessExecution?.siteId &&
    (!baselineProcessExecution?.productFamilyCode
      ? item.equipmentId === baselineProcessExecution?.equipmentId
      : item.productFamilyCode === baselineProcessExecution.productFamilyCode) &&
    (!normalizedProcessExecutionFilter || [
      item.executionId,
      item.productFamilyCode,
      item.productCode,
      item.equipmentId,
      item.processSpecificationId,
    ].some(value => String(value || "").toLowerCase().includes(normalizedProcessExecutionFilter))),
  );
  const comparisonReady = Boolean(baselineProcessExecution) && comparableProcessExecutions.length > 0;

  useEffect(() => {
    if (comparisonScope === "single" && candidate && !comparableProcessExecutions.some(item => item.executionId === candidate)) {
      setCandidate("");
    }
  }, [candidate, comparableProcessExecutions, comparisonScope]);

  const executionLabel = execution => [
    execution.qualityStatus && execution.qualityStatus !== "not_applicable" ? `质量：${execution.qualityStatus}` : null,
    execution.dataQualityStatus ? `数据：${execution.dataQualityStatus}` : null,
    execution.completedAt ? new Date(execution.completedAt).toLocaleString("zh-CN") : "时间未知",
    execution.productFamilyCode || execution.productCode || "未标注产品",
    execution.equipmentId || "未标注设备",
    `…${String(execution.executionId || "").slice(-8)}`,
  ].filter(Boolean).join(" · ");

  async function compare(event) {
    event.preventDefault();
    setBusy(true);
    setError("");
    try {
      const baselineProcessExecutionId = baseline.trim();
      const siteId = baselineProcessExecution?.siteId || requestedSiteId;
      const knownUnmeasuredConfounders = additionalConfounders
        .split(/[\n,，]+/)
        .map(value => value.trim())
        .filter(Boolean);
      if (comparisonScope === "cohort") {
        const query = new URLSearchParams({ limit: "24", siteId });
        knownUnmeasuredConfounders.forEach(value => query.append("knownUnmeasuredConfounder", value));
        setResult(await getJson(`/api/v1/execution-comparisons/${encodeURIComponent(baselineProcessExecutionId)}?${query}`));
      } else {
        setResult(await postJson(`/api/v1/execution-comparisons?siteId=${encodeURIComponent(siteId)}`, {
          baselineProcessExecutionId,
          processExecutionIds: [baselineProcessExecutionId, candidate],
          additionalKnownUnmeasuredConfounders: knownUnmeasuredConfounders,
        }));
      }
    } catch (requestError) {
      setResult(null);
      setError(requestError.message);
    } finally {
      setBusy(false);
    }
  }
  async function createHypotheses() {
    if (!researchProjectId || !result) return;
    setBusy(true);
    try {
      const created = await postJson(
        `/api/v1/research-projects/${researchProjectId}/hypotheses/from-execution-comparison`,
        {
          baselineProcessExecutionId: result.baselineProcessExecutionId,
          executionIds: comparedProcessExecutions.map(item => item.executionId),
          maximumHypotheses: 3,
        },
      );
      notify(`已将运行对比转为 ${created.length} 条候选假设；请补充验证标准后再让优化器设计实验。`);
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setBusy(false);
    }
  }
  const comparedProcessExecutions = result ? [
    { ...result.baseline, comparisonRole: "基准" },
    ...(result.historicalProcessExecutions || []).map(execution => ({ ...execution, comparisonRole: "对比" })),
  ] : [];
  const signalRows = useMemo(() => (result?.signalComparisons || [])
    .map(signal => ({
      ...signal,
      phaseLabel: signal.phaseName || "全运行",
      featureLabel: comparisonFeatureLabels[signal.featureCode] || signal.featureCode,
      difference: Number.isFinite(Number(signal.baselineValue)) && Number.isFinite(Number(signal.historicalMedian))
        ? Number(signal.baselineValue) - Number(signal.historicalMedian)
        : null,
    }))
    .sort((left, right) => {
      const leftScore = Math.abs(Number(left.robustDeviation ?? left.difference ?? 0));
      const rightScore = Math.abs(Number(right.robustDeviation ?? right.difference ?? 0));
      return rightScore - leftScore;
    })
    .slice(0, 30), [result]);
  const causeRows = useMemo(() => (result?.diagnosis?.readiness?.mode === "descriptive-only"
    ? []
    : result?.diagnosis?.candidates || [])
    .map(candidate => ({
      ...candidate,
      sourceLabel: candidate.sourceKind === "control-parameter" ? "实际工艺规范" : "过程轨迹",
      actionabilityLabel: candidate.actionability === "controllable" ? "可直接实验" : "需映射控制量",
      stabilityLabel: Number.isFinite(Number(candidate.stabilitySelectionRate))
        ? `${Math.round(Number(candidate.stabilitySelectionRate) * 100)}%`
        : "样本不足",
      confoundersLabel: (candidate.possibleConfounders || []).map(contextFieldLabel).join("、") || "未识别明确混杂",
    }))
    .slice(0, 30), [result]);
  const investigation = result?.investigation;
  const firstDeviationRows = (investigation?.firstDeviations || []).map(item => ({
    ...item,
    phaseLabel: item.phaseName || item.phaseCode || "全运行",
  }));
  const experimentRows = (investigation?.nextExperiments || []).map(item => ({
    ...item,
    blockingLabel: (item.blockingFactors || []).join("、") || "无已识别区组因素",
    designLabel: `${item.minimumLevels} 水平 × ${item.minimumBlocks} 区组 × 每条件 ${item.repeatsPerCondition} 次`,
  }));
  return (
    <Page title="运行对比" description="选择一条需要解释的运行，系统自动寻找生产条件一致的历史运行，并把差异整理成可验证结论。">
      {error && <Alert tone="danger">{error}</Alert>}
      <Card title="选择目标运行并开始对比" description="默认选择最近完成的运行；如果存在质量异常或参数偏离，请优先选择对应运行。">
        <form className="grid items-start gap-3 md:grid-cols-2 xl:grid-cols-[minmax(15rem,.8fr)_minmax(0,1.4fr)_minmax(12rem,.7fr)_minmax(15rem,.8fr)_auto]" onSubmit={compare}>
          <Field label="筛选运行" hint={`显示 ${visibleProcessExecutions.length} / ${executions.length} 条已完成运行`}><Input value={executionFilter} onChange={event => setProcessExecutionFilter(event.target.value)} placeholder="产品、设备、规范或运行号" /></Field>
          <Field label="目标运行" hint="通常选择质量异常、参数偏离或刚刚完成的一次运行。"><Select value={baseline} onChange={event => { setBaseline(event.target.value); setResult(null); }} required disabled={catalogLoading || !executions.length}><option value="">选择已完成运行</option>{baseline && !executions.some(item => item.executionId === baseline) && <option value={baseline}>{baseline}（来自当前页面链接）</option>}{visibleProcessExecutions.map(execution => <option key={execution.executionId} value={execution.executionId}>{executionLabel(execution)}</option>)}</Select></Field>
          <Field label="对比范围" hint="历史样本组由服务端按产品、时间、质量和数据完整性筛选。"><Select value={comparisonScope} onChange={event => setComparisonScope(event.target.value)} disabled={!baseline}><option value="cohort">同产品历史样本组</option><option value="single">指定一个同类运行</option></Select></Field>
          {comparisonScope === "single" ? <Field label="对比运行" hint={baselineProcessExecution?.productFamilyCode ? `仅显示产品系列“${baselineProcessExecution.productFamilyCode}”的运行。` : baselineProcessExecution ? `该运行未标注产品系列，暂按设备“${baselineProcessExecution.equipmentId || "未标注"}”筛选。` : "正在读取基准运行。"}><Select value={candidate} onChange={event => setCandidate(event.target.value)} required disabled={!baselineProcessExecution || catalogLoading}><option value="">选择同类运行</option>{comparableProcessExecutions.map(execution => <option key={execution.executionId} value={execution.executionId}>{executionLabel(execution)}</option>)}</Select></Field> : <div className="self-end rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-sm leading-5 text-slate-600">系统最多选择 24 个同产品历史运行，并保留质量覆盖和数据完整性证据。</div>}
          <Button variant="primary" type="submit" className="min-h-10 self-end" disabled={busy || !comparisonReady || (comparisonScope === "single" && !candidate)}>{busy ? "正在对比…" : "生成对比结论"}</Button>
        </form>
        <div className="mt-3">
          <Field label="本次补充的潜在未测量混杂因素" hint="例如操作员、环境波动；每行一项。系统会与分析方案默认清单合并并写入本次结果快照。">
            <Textarea rows={2} value={additionalConfounders} onChange={event => setAdditionalConfounders(event.target.value)} placeholder={"操作员经验\n环境温湿度波动"} />
          </Field>
        </div>
        {catalogLoading && <p className="mt-3 text-sm text-slate-500">正在读取可比较的已完成运行…</p>}
        {!catalogLoading && executions.length === 0 && <Alert tone="warning" title="还没有已完成运行">完成生产准备并积累至少两次运行后，即可开始对比。</Alert>}
        {!catalogLoading && baselineProcessExecution && (
          <div className="mt-4 rounded-xl border border-slate-200 bg-slate-50 p-4">
            <div className="flex flex-wrap items-start justify-between gap-3"><div><p className="text-sm font-semibold text-slate-900">系统已核对同类条件</p><p className="mt-1 text-xs text-slate-500">匹配条件来自当前运行分析方案。</p></div><Badge tone={comparisonReady ? "success" : "warning"}>{comparisonReady ? `找到 ${comparableProcessExecutions.length} 条同类运行` : "没有同类运行"}</Badge></div>
            <dl className="mt-3 grid gap-2 sm:grid-cols-3">
              <div><dt className="text-xs text-slate-500">产品系列</dt><dd className="mt-1 text-sm font-medium">{baselineProcessExecution.productFamilyCode || "未记录"}</dd></div>
              <div><dt className="text-xs text-slate-500">设备</dt><dd className="mt-1 text-sm font-medium">{baselineProcessExecution.equipmentId || "未记录"}</dd></div>
              <div><dt className="text-xs text-slate-500">工艺规范</dt><dd className="mt-1 text-sm font-medium">{baselineProcessExecution.processSpecificationId || "未记录"}</dd></div>
            </dl>
            {!comparisonReady && <p className="mt-3 text-sm text-amber-800">需要另一条具有相同产品系列（未记录产品系列时使用相同设备）的已完成运行。</p>}
          </div>
        )}
      </Card>
      {result ? (
        <>
          <Alert tone={investigation?.status === "ready" ? "success" : "warning"} title="3. 对比结论">
            {investigation?.status === "ready"
              ? `已找到 ${firstDeviationRows.length} 个优先偏离和 ${causeRows.length} 个候选原因，可以进入受控验证。`
              : `对比计算已成功，但当前只能作为探索性证据。${(investigation?.missingData || []).length ? ` 还缺少：${investigation.missingData.join("；")}` : " 需要更多质量结果和重复运行。"}`}
          </Alert>
          <AnalysisReadinessCard diagnosis={result.diagnosis} />
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-5">
            <Metric label="产品系列" value={result.productFamilyCode || "—"} valueClassName="text-2xl" />
            <Metric label="参与对比" value={result.acceptance?.executionCount ?? comparedProcessExecutions.length} hint="条生产运行" />
            <Metric label="数据可用" value={result.acceptance?.availableProcessExecutionCount ?? 0} hint={`异常 ${result.acceptance?.degradedProcessExecutionCount ?? 0} 个`} />
            <Metric label="运行完整" value={result.acceptance?.completeProcessExecutionCount ?? 0} hint="同时具有生产开始与结束事件" />
            <Metric label="分析证据" value={<EvidenceLevel value={result.evidenceLevel} />} />
          </div>
          <Card title="调查报告" description="汇总运行匹配、数据质量、首次偏离和后续验证建议。">
            <div className="mb-3 grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
              <Metric label="调查状态" value={investigation?.status === "ready" ? "可进入验证" : investigation?.status === "exploratory" ? "探索性" : "数据不足"} />
              <Metric label="目标数据" value={<StatusBadge value={investigation?.dataQuality?.targetStatus || "unknown"} />} hint={`证据权重 ${formatDecimal(investigation?.dataQuality?.targetEvidenceWeight)}`} />
              <Metric label="基线有效权重" value={formatDecimal(investigation?.comparisonBaseline?.effectiveProcessExecutionWeight)} hint={`${investigation?.comparisonBaseline?.comparisonProcessExecutionIds?.length || 0} 条对比运行`} />
            </div>
            <MatchingContext value={investigation?.comparisonBaseline?.matchingContext} />
            {firstDeviationRows.length ? (
              <div className="mb-4">
                <h4 className="mb-2 text-sm font-semibold text-slate-900">首次阶段偏离</h4>
                <DataTable
                  rows={firstDeviationRows}
                  getRowKey={(row, index) => `${row.signalCode}-${row.phaseCode || "execution"}-${row.featureCode}-${index}`}
                  columns={[
                    { key: "phaseLabel", label: "阶段" },
                    { key: "signalCode", label: "信号" },
                    { key: "featureCode", label: "特征" },
                    { key: "startedAt", label: "首次时间", render: formatTime },
                    { key: "targetValue", label: "目标运行值", render: formatDecimal },
                    { key: "historicalMedian", label: "历史中位数", render: formatDecimal },
                    { key: "robustDeviation", label: "稳健偏离", render: formatDecimal },
                  ]}
                />
              </div>
            ) : <Alert tone="warning" title="尚不能定位首次偏离">需要具有阶段时间和足够历史分布的过程特征。</Alert>}
            <div className="grid gap-4 xl:grid-cols-2">
              <div>
                <h4 className="mb-2 text-sm font-semibold text-slate-900">反证与边界</h4>
                {(investigation?.counterEvidence || []).length ? <ul className="list-disc space-y-1 pl-5 text-sm text-slate-700">{investigation.counterEvidence.map((item, index) => <li key={`${item.candidateId}-${item.kind}-${index}`}>{item.statement}</li>)}</ul> : <p className="text-sm text-slate-500">尚无候选原因可进行反证检查。</p>}
              </div>
              <div>
                <h4 className="mb-2 text-sm font-semibold text-slate-900">缺失数据</h4>
                {(investigation?.missingData || []).length ? <ul className="list-disc space-y-1 pl-5 text-sm text-slate-700">{investigation.missingData.map(item => <li key={item}>{item}</li>)}</ul> : <p className="text-sm text-emerald-700">当前调查所需的关键数据项已覆盖。</p>}
              </div>
            </div>
            {experimentRows.length > 0 && (
              <div className="mt-4">
                <h4 className="mb-2 text-sm font-semibold text-slate-900">下一步验证实验</h4>
                <DataTable rows={experimentRows} keyField="candidateId" columns={[
                  { key: "variableCode", label: "可控变量" },
                  { key: "designLabel", label: "最低设计" },
                  { key: "blockingLabel", label: "区组因素" },
                  { key: "rationale", label: "验证方法" },
                ]} />
              </div>
            )}
            <ConclusionBoundary>{investigation?.conclusionGuardrail || "当前结果只能作为待验证假设。"}</ConclusionBoundary>
          </Card>
          <details className="rounded-2xl border border-slate-200 bg-white shadow-sm"><summary className="cursor-pointer px-5 py-4 text-sm font-semibold text-slate-900">查看参与对比的 {comparedProcessExecutions.length} 条运行</summary><div className="border-t border-slate-100 p-5"><Card title="运行概况">
            <DataTable
              rows={comparedProcessExecutions}
              getRowKey={row => `${row.comparisonRole}-${row.executionId}`}
              columns={[
                { key: "comparisonRole", label: "角色", render: value => <Badge tone={value === "基准" ? "info" : "neutral"}>{value}</Badge> },
                { key: "executionId", label: "运行" },
                { key: "equipmentId", label: "设备" },
                { key: "completedAt", label: "结束时间", render: formatTime },
                { key: "durationMs", label: "时长（秒）", render: value => formatDecimal(Number(value) / 1000) },
                { key: "sampleCount", label: "样本数", render: formatInteger },
                { key: "lifecycleComplete", label: "过程执行边界", render: value => value ? <Badge tone="success">完整</Badge> : <Badge tone="warning">缺少开始或结束</Badge> },
                { key: "processDataQuality", label: "数据状态", render: value => <StatusBadge value={value?.status} /> },
              ]}
            />
          </Card></div></details>
          <Card title="将追因结果带入研发" description="系统只把有证据的关联转为候选假设；因果关系仍需后续受控实验验证。">
            <div className="grid gap-3 md:grid-cols-[1fr_auto]">
              <Field label="研发项目"><Select value={researchProjectId} onChange={event => setResearchProjectId(event.target.value)}><option value="">选择研发项目</option>{researchProjects.filter(item => !["completed", "archived"].includes(item.status)).map(item => <option key={item.projectId} value={item.projectId}>{item.name}</option>)}</Select></Field>
              <Button className="self-end" disabled={!researchProjectId || busy || result.diagnosis?.readiness?.mode === "descriptive-only"} onClick={createHypotheses}>生成候选假设</Button>
            </div>
          </Card>
          <Card title="质量候选原因" description="同时比较实际控制参数与过程轨迹特征；优先选择能直接映射到可控变量的候选原因。">
            {causeRows.length ? (
              <>
                <div className="mb-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                  <Metric label="诊断模型" value={result.diagnosis?.modelFamily || "稳健筛选"} hint="按样本规模自动选择" />
                  <Metric label="上下文校正" value={result.diagnosis?.adjustmentMethod || "未启用"} hint={`${result.diagnosis?.contextVariables?.length || 0} 个校正项`} />
                  <Metric label="交叉验证" value={Number.isFinite(Number(result.diagnosis?.crossValidationScore)) ? formatDecimal(result.diagnosis.crossValidationScore) : "样本不足"} hint={`${result.diagnosis?.foldCount || 0} 折样本外验证`} />
                  <Metric label="稳定性重采样" value={result.diagnosis?.stabilityRuns || 0} hint="次 bootstrap 选择" />
                </div>
                <DataTable
                  rows={causeRows}
                  keyField="candidateId"
                  columns={[
                    { key: "displayName", label: "候选原因" },
                    { key: "sourceLabel", label: "来源" },
                    { key: "actionabilityLabel", label: "可操作性", render: (value, row) => <Badge tone={row.actionability === "controllable" ? "success" : "warning"}>{value}</Badge> },
                    { key: "passMedian", label: "合格组中位数", render: formatDecimal },
                    { key: "failMedian", label: "不合格组中位数", render: formatDecimal },
                    { key: "robustEffect", label: "稳健效应", render: formatDecimal },
                    { key: "adjustedEffect", label: "校正后效应", render: formatDecimal },
                    { key: "stabilityLabel", label: "稳定入选率" },
                    { key: "candidateScore", label: "候选分数", render: formatDecimal },
                    { key: "evidenceLevel", label: "证据", render: value => <EvidenceLevel value={value} /> },
                    { key: "confoundersLabel", label: "可能混杂" },
                  ]}
                />
                <div className="mt-4 grid gap-3 xl:grid-cols-2">
                  {causeRows.map(candidate => (
                    <ConclusionBoundary key={candidate.candidateId} title={candidate.displayName}>
                      这是观察性候选，不是已验证原因。
                      {(candidate.possibleConfounders || []).length
                        ? ` 当前可能受${candidate.confoundersLabel}混杂，必须通过受控实验拆解。`
                        : " 当前未识别出明确混杂因素，仍需经过受控重复实验验证。"}
                      {(result.diagnosis?.knownUnmeasuredConfounders || []).length
                        ? ` 已知但本次未记录的因素包括：${result.diagnosis.knownUnmeasuredConfounders.map(item => item.name).join("、")}。`
                        : ""}
                    </ConclusionBoundary>
                  ))}
                </div>
                {(result.diagnosis?.interactions || []).length > 0 && (
                  <div className="mt-4">
                    <h4 className="mb-2 text-sm font-semibold text-slate-900">变量交互候选</h4>
                    <DataTable
                      rows={result.diagnosis.interactions}
                      getRowKey={(row, index) => `${row.leftDataSource}-${row.rightDataSource}-${index}`}
                      columns={[
                        { key: "leftDataSource", label: "变量 A" },
                        { key: "rightDataSource", label: "变量 B" },
                        { key: "adjustedEffect", label: "交互效应", render: formatDecimal },
                        { key: "stabilitySelectionRate", label: "稳定入选率", render: value => `${Math.round(Number(value) * 100)}%` },
                      ]}
                    />
                  </div>
                )}
                {(result.diagnosis?.limitations || []).length > 0 && (
                  <Alert tone="warning" title="诊断边界">
                    <ul className="list-disc space-y-1 pl-5">
                      {result.diagnosis.limitations.map(item => <li key={item}>{item}</li>)}
                    </ul>
                  </Alert>
                )}
              </>
            ) : <EmptyState title="尚无质量候选原因" description="至少需要合格与不合格运行，并且工艺规范或过程特征具有可比较差异。" />}
          </Card>
          <details className="rounded-2xl border border-slate-200 bg-white shadow-sm"><summary className="cursor-pointer px-5 py-4 text-sm font-semibold text-slate-900">查看全部信号差异（{signalRows.length} 项）</summary><div className="border-t border-slate-100 p-5"><Card title="信号差异" description="按变化幅度列出前 30 项，便于工程师核对阶段和参数差异。">
            {signalRows.length ? (
              <DataTable
                rows={signalRows}
                getRowKey={(row, index) => `${row.signalCode}-${row.phaseCode || "execution"}-${row.featureCode}-${index}`}
                columns={[
                  { key: "signalCode", label: "信号" },
                  { key: "phaseLabel", label: "阶段" },
                  { key: "featureLabel", label: "指标" },
                  { key: "baselineValue", label: "基准值", render: formatDecimal },
                  { key: "historicalMedian", label: "对比值", render: formatDecimal },
                  { key: "difference", label: "差值", render: formatDecimal },
                  { key: "baselinePercentile", label: "所处分位", render: value => Number.isFinite(Number(value)) ? `${Math.round(Number(value) * 100)}%` : "—" },
                ]}
              />
            ) : <EmptyState title="暂无可比信号" description="所选运行还没有可用于阶段对比的信号特征。" />}
          </Card></div></details>
        </>
      ) : <EmptyState title="选择目标运行开始对比" description="系统将自动匹配同类运行并汇总主要差异。" />}
    </Page>
  );
}
export function DataQualityPage() {
  const [params] = useSearchParams();
  const objectQuery = new URLSearchParams({ limit: "200" });
  if (params.get("subjectType")) objectQuery.set("subjectType", params.get("subjectType"));
  if (params.get("subjectId")) objectQuery.set("subjectId", params.get("subjectId"));
  const baselineQuery = new URLSearchParams({ maximumRuns: "2000" });
  if ((!params.get("subjectType") || params.get("subjectType") === "equipment") && params.get("subjectId")) {
    baselineQuery.set("equipmentId", params.get("subjectId"));
  }
  const baseline = useApi(`/api/v1/data-reliability/baseline?${baselineQuery}`);
  const objects = useApi(`/api/v1/data-objects?${objectQuery}`);
  const rates = baseline.data?.rates || [];
  const contexts = Array.from(new Map(
    (baseline.data?.contextFields || []).map(item => [item.field, item]),
  ).values());
  const contextFactors = baseline.data?.contextFactors || [];
  const factorOverlaps = baseline.data?.contextFactorOverlaps || [];
  const exclusions = baseline.data?.exclusions || [];
  const objectRows = extractRows(objects.data);
  const rate = code => rates.find(item => item.code === code);
  const rateValue = code => {
    const value = rate(code)?.rate;
    return value == null ? "—" : `${Math.round(Number(value) * 100)}%`;
  };
  const factorNames = Object.fromEntries(contextFactors.map(item => [item.field, item.name]));
  const factorRows = contextFactors.flatMap(factor => (factor.levels || []).map(level => ({
    ...level,
    field: factor.field,
    factorName: factor.name,
    distinctLevelCount: factor.distinctLevelCount,
  })));
  const overlapLabel = value => ({
    overlapping: "可比较",
    limited: "有限重叠",
    confounded: "完全混杂",
    insufficient_levels: "水平不足",
  }[value] || value || "未知");
  const loading = baseline.loading || objects.loading;
  const error = baseline.error || objects.error;
  return (
    <Page
      title="数据健康"
      description={params.get("subjectId")
        ? `检查对象 ${params.get("subjectId")} 的证据完整性、实际参数、上下文和质量关联。`
        : "用明确分子、分母和排除原因建立可重复的数据可靠性基线。"}
    >
      {error && <Alert tone="danger">{error}</Alert>}
      {loading ? <LoadingCard /> : (
        <div className="space-y-5">
          {baseline.data?.truncated && (
            <Alert tone="warning">匹配运行超过本次上限；当前基线只分析最近 {formatInteger(baseline.data.analyzedRunCount)} 次运行。</Alert>
          )}
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <Metric label="过程数据完整率" value={rateValue("process_data_completeness")} hint={`${rate("process_data_completeness")?.numerator ?? 0} / ${rate("process_data_completeness")?.denominator ?? 0} 次运行`} />
            <Metric label="实际参数覆盖率" value={rateValue("actual_parameter_coverage")} hint="只认设备回读，不使用计划值" />
            <Metric label="最小上下文覆盖率" value={rateValue("minimal_context_coverage")} hint="设备身份与运行身份同时存在" />
            <Metric label="运行—质量关联率" value={rateValue("run_quality_association")} hint="至少关联一条有效检验结果" />
          </div>
          <div className="grid gap-5 xl:grid-cols-[1.4fr_.6fr]">
            <Card title="正式分析准入" description="只有全部准入条件通过的运行才进入追因、实验分析和工艺研发。">
              <div className="mb-4 grid gap-4 sm:grid-cols-3">
                <Metric label="准入率" value={rateValue("analysis_admission")} hint={`${rate("analysis_admission")?.numerator ?? 0} / ${rate("analysis_admission")?.denominator ?? 0} 次运行`} />
                <Metric label="序列缺口" value={formatInteger(baseline.data?.sequenceGapCount)} hint="已分析运行累计" />
                <Metric label="最大采样空窗" value={baseline.data?.maximumSampleGapMs == null ? "—" : formatDuration(baseline.data.maximumSampleGapMs)} />
              </div>
              <DataTable
                rows={rates}
                keyField="code"
                columns={[
                  { key: "name", label: "指标" },
                  { key: "rate", label: "结果", render: (value, row) => value == null ? "—" : `${Math.round(Number(value) * 100)}%（${row.numerator}/${row.denominator}）` },
                  { key: "definition", label: "计算定义" },
                ]}
              />
            </Card>
            <Card title="排除原因" description="同一次运行可能同时命中多个原因。">
              {exclusions.length ? (
                <DataTable
                  rows={exclusions}
                  keyField="code"
                  columns={[
                    { key: "name", label: "原因" },
                    { key: "runCount", label: "运行数", render: formatInteger },
                  ]}
                />
              ) : <EmptyState title="没有准入缺口" description="当前分析范围内的运行全部满足正式准入规则。" />}
            </Card>
          </div>
          <Card title="时间、顺序与上送质量" description="时钟偏差按设备源时间与 Edge 记录时间计算；上送延迟按 Edge 记录到 Platform 摄入计算，因此会真实包含断网积压。">
            <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
              <Metric label="重复时间戳" value={formatInteger(baseline.data?.duplicateTimestampCount)} hint="累计被去重的采样" />
              <Metric label="晚到或乱序" value={formatInteger(baseline.data?.outOfOrderCount)} hint="按摄入顺序检测" />
              <Metric label="源序列缺口" value={formatInteger(baseline.data?.sequenceGapCount)} hint="设备源序号不连续" />
              <Metric label="最大采样空窗" value={baseline.data?.maximumSampleGapMs == null ? "—" : formatDuration(baseline.data.maximumSampleGapMs)} />
              <Metric label="最大设备时钟偏差" value={baseline.data?.maximumAbsoluteSourceClockOffsetMs == null ? "—" : formatDuration(baseline.data.maximumAbsoluteSourceClockOffsetMs)} hint="源时间与 Edge 记录时间的绝对差" />
              <Metric label="最差运行 P95 上送延迟" value={baseline.data?.worstRunP95PlatformIngestLatencyMs == null ? "—" : formatDuration(baseline.data.worstRunP95PlatformIngestLatencyMs)} hint="包含离线缓存后的补传" />
              <Metric label="最大上送延迟" value={baseline.data?.maximumPlatformIngestLatencyMs == null ? "—" : formatDuration(baseline.data.maximumPlatformIngestLatencyMs)} />
              <Metric label="负上送延迟异常" value={formatInteger(baseline.data?.negativePlatformIngestLatencyCount)} hint="Platform 时间早于 Edge 超过 1 秒" />
            </div>
          </Card>
          <Card title="上下文字段覆盖" description="设备与运行身份是准入必需字段；材料、工装和维护校准字段先用于追溯与分层。">
            <DataTable
              rows={contexts}
              keyField="field"
              columns={[
                { key: "field", label: "字段", render: contextFieldLabel },
                { key: "requiredForAdmission", label: "准入要求", render: value => value ? <Badge tone="blue">必需</Badge> : <Badge>可选追溯</Badge> },
                { key: "coverage", label: "覆盖率", render: (value, row) => value == null ? "—" : `${Math.round(Number(value) * 100)}%（${row.presentRunCount}/${row.runCount}）` },
              ]}
            />
          </Card>
          <div className="grid gap-5 xl:grid-cols-2">
            <Card title="上下文分层统计" description="按设备、工装和材料批次展示运行、过程完整性和质量结果；这里只描述观察事实，不直接宣称因果。">
              {factorRows.length ? (
                <DataTable
                  rows={factorRows}
                  getRowKey={row => `${row.field}:${row.value}`}
                  columns={[
                    { key: "factorName", label: "因素" },
                    { key: "value", label: "水平" },
                    { key: "runCount", label: "运行", render: formatInteger },
                    { key: "processCompleteRunCount", label: "过程完整", render: formatInteger },
                    { key: "qualityLinkedRunCount", label: "已质检", render: formatInteger },
                    { key: "quality", label: "质量结果", render: (_, row) => `合格 ${row.passRunCount} · 不合格 ${row.failRunCount} · 不确定 ${row.inconclusiveRunCount}` },
                    { key: "meanDurationMs", label: "平均运行时长", render: value => value == null ? "—" : formatDuration(value) },
                  ]}
                />
              ) : <EmptyState title="暂无可分层上下文" description="采集到设备、工装或材料批次后，这里会按实际水平汇总。" />}
            </Card>
            <Card title="因素重叠与混杂" description="重叠度表示实际出现的因素组合占理论组合的比例；完全绑定的因素无法仅凭观察数据拆分影响。">
              <div className="mb-4">
                <Metric label="不可辨识混杂" value={formatInteger(baseline.data?.unidentifiableConfoundingCount)} hint="标记为完全混杂的因素对" />
              </div>
              <DataTable
                rows={factorOverlaps}
                getRowKey={row => `${row.leftField}:${row.rightField}`}
                columns={[
                  { key: "leftField", label: "因素 A", render: value => factorNames[value] || value },
                  { key: "rightField", label: "因素 B", render: value => factorNames[value] || value },
                  { key: "levels", label: "水平数", render: (_, row) => `${row.leftLevelCount} × ${row.rightLevelCount}` },
                  { key: "combinations", label: "组合覆盖", render: (_, row) => `${row.observedCombinationCount}/${row.possibleCombinationCount}` },
                  { key: "overlapRate", label: "重叠度", render: value => value == null ? "—" : `${Math.round(Number(value) * 100)}%` },
                  { key: "identifiability", label: "可辨识性", render: value => <Badge tone={value === "overlapping" ? "green" : value === "confounded" ? "red" : "yellow"}>{overlapLabel(value)}</Badge> },
                ]}
              />
            </Card>
          </div>
          <Card title="工业对象采样范围" description="用于定位具体设备的数据量、最近采样和最大间隔。">
            <DataTable
              rows={objectRows}
              getRowKey={row => `${row.subjectType}:${row.subjectId}`}
              columns={[
                { key: "subjectType", label: "对象类型", render: objectTypeLabel },
                { key: "subjectId", label: "对象" },
                { key: "sampleCount", label: "样本数", render: formatInteger },
                { key: "maximumSampleGapSeconds", label: "最大采样间隔（秒）", render: value => value == null ? "—" : Number(value).toLocaleString("zh-CN") },
                { key: "lastSampleAt", label: "最后样本", render: formatTime },
              ]}
            />
          </Card>
        </div>
      )}
    </Page>
  );
}
