
// 呈现运行对比、候选原因与数据可信度，并在证据不足时阻止结论升级。
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { getJson, postJson } from "../api/http";
import { extractRows, useApi } from "../hooks/useApi";
import { useSiteScope } from "../hooks/useSiteScope";
import { useIsMounted } from "../hooks/useIsMounted";
import { formatLocalDateTime, localDateTimeToIso } from "../ui/dateTime";
import { Alert, Badge, Button, Card, ConclusionBoundary, DataTable, DateTimeField, EmptyState, EvidenceLevel, Field, Input, Metric, Page, Pagination, RequestError, Select, StatusBadge, Textarea } from "../ui/components";
import { contextFieldLabel, formatTime, formatInteger, formatDuration, objectTypeLabel, toFiniteNumber, LoadingCard, SiteScopeField } from "./shared";

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
  process_specification_id: "配方版本",
  material_lot_ref: "材料批次",
  recipe_id: "配方",
};

const readinessBlockingReasonLabels = {
  "quality-outcomes-missing": "质量结果尚未与运行关联",
  "outcome-class-missing": "合格或不合格类别样本不足",
  "effective-weight-insufficient": "有效证据权重不足",
  "process-data-unavailable": "部分运行的过程数据不可用",
  "process-data-degraded": "质量降级的运行已从候选原因计算中排除",
};

const reliabilityRunReasonDetails = {
  lifecycle_incomplete: ["运行边界不完整", "核对开始与结束事件是否都已上报；缺少结束事件也可能表示运行仍在进行。"],
  process_data_incomplete: ["过程数据未达完整标准", "查看运行详情中的过程数据状态与质量提示。"],
  actual_parameters_missing: ["缺少实际参数回读", "检查配方应用后的设备实际参数是否关联到运行。"],
  parameter_unit_missing: ["实际参数或单位不完整", "确认实际参数回读存在，且每项都包含单位。"],
  context_capture_invalid: ["生产上下文捕获无效", "核对上下文捕获状态与现场节点配置。"],
  minimal_context_missing: ["最小上下文不完整", "确认捕获状态有效，并包含设备编号与运行编号。"],
  quality_unlinked: ["未关联有效质量结果", "核对检验结果是否关联到该运行。"],
};

const sampleSourceVerificationDetails = {
  matched: {
    label: "平台任务版本与 Edge 一致",
    tone: "success",
    detail: "该任务版本存在于当前站点的平台记录中，且记录归属 Edge 与帧报告 Edge 一致。",
  },
  "version-not-found": {
    label: "站点内未找到该任务版本",
    tone: "warning",
    detail: "当前站点的平台任务记录中没有这组任务编号和版本。",
  },
  "edge-mismatch": {
    label: "平台记录与帧来源 Edge 不一致",
    tone: "warning",
    detail: "当前站点存在该任务版本，但记录归属 Edge 与帧报告 Edge 不同。",
  },
  unverifiable: {
    label: "无法逐版本核对",
    tone: "neutral",
    detail: "来源已汇总、帧来源信息不完整，或配置引用不是采集任务版本。",
  },
};

const ingestionTaskStatusLabels = {
  draft: "草稿",
  published: "已发布",
  retired: "已停用",
};

function reliabilityRunActions(reasonCode, run, siteId) {
  const detailQuery = new URLSearchParams();
  if (siteId) detailQuery.set("siteId", siteId);
  const detailHref = tab => {
    const query = new URLSearchParams(detailQuery);
    if (tab) query.set("tab", tab);
    const search = query.toString();
    return `/process-executions/${encodeURIComponent(run.executionId)}${search ? `?${search}` : ""}`;
  };
  const edgeIds = [...new Set((run.edgeIds || []).filter(Boolean))];
  const actions = [];

  if (reasonCode === "lifecycle_incomplete")
    actions.push({ href: detailHref("events"), label: "核对本次运行事件" });
  if (reasonCode === "process_data_incomplete")
    actions.push({ href: detailHref("curves"), label: "查看本次运行曲线" });
  if (reasonCode === "quality_unlinked") {
    const query = new URLSearchParams({ executionId: run.executionId });
    if (siteId) query.set("siteId", siteId);
    actions.push({ href: `/inspections?${query}`, label: "打开本次运行检验任务" });
  }

  if ([
    "process_data_incomplete",
    "lifecycle_incomplete",
    "actual_parameters_missing",
    "parameter_unit_missing",
    "context_capture_invalid",
    "minimal_context_missing",
  ].includes(reasonCode)) {
    for (const edgeId of edgeIds) {
      const query = new URLSearchParams({ edgeId });
      if (siteId) query.set("siteId", siteId);
      actions.push({
        href: `/configuration/ingestion-tasks?${query}#task-configurations`,
        label: `检查 ${edgeId} 的采集配置`,
      });
    }
  }

  if (!actions.length)
    actions.push({ href: detailHref(null), label: "查看运行详情" });
  return actions;
}

function formatDecimal(value) {
  const numeric = toFiniteNumber(value);
  if (numeric == null) return "—";
  return numeric.toLocaleString("zh-CN", { maximumFractionDigits: 3 });
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
    <Card title="分析就绪度" description="候选原因只使用过程数据质量完整的运行；数据不足时只展示描述性事实。">
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
  const siteScope = useSiteScope();
  const { siteId, siteChoices, selectSite } = siteScope;
  const isMounted = useIsMounted();
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
  const [additionalConfounders, setAdditionalConfounders] = useState("");
  const [catalogRetryKey, setCatalogRetryKey] = useState(0);
  const priorSiteId = useRef(params.get("siteId") || "");
  const siteScopeResolved = useRef(false);
  const catalogRequestId = useRef(0);
  const linkedBaselineRequestId = useRef(0);
  const comparisonRequestId = useRef(0);
  const comparisonContext = JSON.stringify([siteId, baseline, candidate, comparisonScope, additionalConfounders]);
  const comparisonContextRef = useRef(comparisonContext);
  useEffect(() => {
    comparisonContextRef.current = comparisonContext;
  }, [comparisonContext]);
  useEffect(() => {
    if (siteScope.loading || siteScope.error || siteScope.invalidRequestedSite) return;
    if (!siteScopeResolved.current) {
      if (!siteId) return;
      siteScopeResolved.current = true;
      priorSiteId.current = siteId;
      return;
    }
    if (priorSiteId.current === siteId) return;
    priorSiteId.current = siteId;
    catalogRequestId.current += 1;
    linkedBaselineRequestId.current += 1;
    comparisonRequestId.current += 1;
    setBaseline("");
    setCandidate("");
    setResult(null);
    setLinkedBaseline(null);
    setProcessExecutions([]);
    setBusy(false);
  }, [siteId, siteScope.error, siteScope.invalidRequestedSite, siteScope.loading]);
  useEffect(() => {
    comparisonRequestId.current += 1;
    setResult(null);
    setError("");
    setBusy(false);
  }, [comparisonContext]);
  useEffect(() => {
    const search = executionFilter.trim();
    const query = new URLSearchParams({ status: "completed", limit: "200" });
    const requestId = ++catalogRequestId.current;
    if (!siteId) {
      if (!siteScope.loading) {
        setCatalogLoading(false);
        setError(siteScope.error || (siteScope.invalidRequestedSite ? "当前账号无权访问 URL 中指定的站点。" : ""));
      }
      return () => {
        if (catalogRequestId.current === requestId) catalogRequestId.current += 1;
      };
    }
    const controller = new AbortController();
    query.set("siteId", siteId);
    if (search) query.set("search", search);
    setError("");
    setCatalogLoading(true);
    getJson(`/api/v1/process-executions?${query}`, { signal: controller.signal }).then(executionPayload => {
      if (!isMounted() || catalogRequestId.current !== requestId) return;
      const loadedExecutions = extractRows(executionPayload);
      setProcessExecutions(loadedExecutions);
      if (!baseline && !search && loadedExecutions.length > 0) setBaseline(loadedExecutions[0].executionId);
    }).catch(requestError => {
      if (!isMounted() || requestError?.name === "AbortError" || catalogRequestId.current !== requestId) return;
      setProcessExecutions([]);
      setError(requestError.message || "无法读取可比较的生产运行。");
    }).finally(() => {
      if (isMounted() && catalogRequestId.current === requestId) setCatalogLoading(false);
    });
    return () => {
      controller.abort();
      if (catalogRequestId.current === requestId) catalogRequestId.current += 1;
    };
  }, [catalogRetryKey, executionFilter, siteId, siteScope.error, siteScope.invalidRequestedSite, siteScope.loading]);

  useEffect(() => {
    const requestId = ++linkedBaselineRequestId.current;
    if (!baseline || !siteId || executions.some(item => item.executionId === baseline)) {
      setLinkedBaseline(null);
      return () => {
        if (linkedBaselineRequestId.current === requestId) linkedBaselineRequestId.current += 1;
      };
    }
    const controller = new AbortController();
    getJson(`/api/v1/process-executions?executionId=${encodeURIComponent(baseline)}&siteId=${encodeURIComponent(siteId)}&limit=1`, { signal: controller.signal })
      .then(payload => {
        if (isMounted() && linkedBaselineRequestId.current === requestId) {
          const linked = extractRows(payload)[0] || null;
          setLinkedBaseline(linked);
          if (!linked && baseline === params.get("executionId")) setBaseline("");
        }
      })
      .catch(() => {
        if (isMounted() && linkedBaselineRequestId.current === requestId) {
          setLinkedBaseline(null);
          if (baseline === params.get("executionId")) setBaseline("");
        }
      });
    return () => {
      controller.abort();
      if (linkedBaselineRequestId.current === requestId) linkedBaselineRequestId.current += 1;
    };
  }, [baseline, executions, params, siteId]);

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
    execution.subjectId && execution.subjectId !== execution.equipmentId ? `逻辑对象 ${execution.subjectId}` : null,
    `…${String(execution.executionId || "").slice(-8)}`,
  ].filter(Boolean).join(" · ");

  async function compare(event) {
    event.preventDefault();
    setBusy(true);
    setError("");
    try {
      const baselineProcessExecutionId = baseline.trim();
      const comparisonSiteId = baselineProcessExecution?.siteId || siteId;
      const knownUnmeasuredConfounders = additionalConfounders
        .split(/[\n,，]+/)
        .map(value => value.trim())
        .filter(Boolean);
      if (comparisonScope === "cohort") {
        const query = new URLSearchParams({ limit: "24", siteId: comparisonSiteId });
        knownUnmeasuredConfounders.forEach(value => query.append("knownUnmeasuredConfounder", value));
        setResult(await getJson(`/api/v1/execution-comparisons/${encodeURIComponent(baselineProcessExecutionId)}?${query}`));
      } else {
        setResult(await postJson(`/api/v1/execution-comparisons?siteId=${encodeURIComponent(comparisonSiteId)}`, {
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
      sourceLabel: candidate.sourceKind === "control-parameter" ? "实际配方版本" : "过程轨迹",
      actionabilityLabel: candidate.actionability === "controllable" ? "可形成下一配方建议" : "需映射控制量",
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
  return (
    <Page title="工艺分析" description="比较同类运行的设定和过程曲线，结合数据质量检查实际差异。">
      <Card title="分析站点" description="运行比较只使用所选站点的数据；多站点账号需要明确选择，避免默认落到其他现场。">
        <div className="max-w-md"><SiteScopeField siteId={siteId} siteChoices={siteChoices} onChange={selectSite} loading={siteScope.loading} /></div>
      </Card>
      {!siteId && !siteScope.loading && <Alert tone="warning">{siteScope.invalidRequestedSite ? "当前账号无权访问 URL 中指定的站点。" : siteChoices.length ? "请选择站点后读取可比较运行。" : "当前账号没有可用站点。"}</Alert>}
      <RequestError error={siteScope.error || error} onRetry={() => { setError(""); if (siteScope.error) void siteScope.reload(); setCatalogRetryKey(value => value + 1); }} />
      <Card title="对比条件">
        <form className="grid items-start gap-3 md:grid-cols-2 xl:grid-cols-[minmax(15rem,.8fr)_minmax(0,1.4fr)_minmax(12rem,.7fr)_minmax(15rem,.8fr)_auto]" onSubmit={compare}>
          <Field label="筛选运行"><Input value={executionFilter} onChange={event => setProcessExecutionFilter(event.target.value)} placeholder={`产品、设备、规范或运行号（${visibleProcessExecutions.length}/${executions.length}）`} /></Field>
          <Field label="目标运行"><Select value={baseline} onChange={event => { setBaseline(event.target.value); setResult(null); }} required disabled={catalogLoading || !executions.length}><option value="">选择已完成运行</option>{baseline && !executions.some(item => item.executionId === baseline) && <option value={baseline}>{baseline}（来自当前页面链接）</option>}{visibleProcessExecutions.map(execution => <option key={execution.executionId} value={execution.executionId}>{executionLabel(execution)}</option>)}</Select></Field>
          <Field label="对比范围"><Select value={comparisonScope} onChange={event => setComparisonScope(event.target.value)} disabled={!baseline}><option value="cohort">同产品历史样本组</option><option value="single">指定一个同类运行</option></Select></Field>
          {comparisonScope === "single" ? <Field label="对比运行" hint={baselineProcessExecution?.productFamilyCode ? `仅显示产品系列“${baselineProcessExecution.productFamilyCode}”的运行。` : baselineProcessExecution ? `该运行未标注产品系列，暂按设备“${baselineProcessExecution.equipmentId || "未标注"}”筛选。` : "正在读取基准运行。"}><Select value={candidate} onChange={event => setCandidate(event.target.value)} required disabled={!baselineProcessExecution || catalogLoading}><option value="">选择同类运行</option>{comparableProcessExecutions.map(execution => <option key={execution.executionId} value={execution.executionId}>{executionLabel(execution)}</option>)}</Select></Field> : <div className="self-end px-1 py-2 text-[13px] leading-5 text-slate-500">最多使用 24 条同产品历史运行</div>}
          <Button variant="primary" type="submit" className="min-h-10 self-end" disabled={busy || !comparisonReady || (comparisonScope === "single" && !candidate)}>{busy ? "正在对比…" : "生成对比结论"}</Button>
        </form>
        <div className="mt-3">
          <Field label="补充混杂因素">
            <Textarea rows={2} value={additionalConfounders} onChange={event => setAdditionalConfounders(event.target.value)} placeholder={"操作员经验\n环境温湿度波动"} />
          </Field>
        </div>
        {catalogLoading && <p className="mt-3 text-sm text-slate-500">正在读取可比较的已完成运行…</p>}
        {!catalogLoading && executions.length === 0 && <Alert tone="warning" title="还没有已完成运行">完成生产准备并积累至少两次运行后，即可开始对比。</Alert>}
        {!catalogLoading && baselineProcessExecution && (
          <div className="mt-4 border-t border-slate-200 pt-4">
            <div className="flex flex-wrap items-start justify-between gap-3"><p className="text-sm font-semibold text-slate-900">匹配条件</p><Badge tone={comparisonReady ? "success" : "warning"}>{comparisonReady ? `找到 ${comparableProcessExecutions.length} 条同类运行` : "没有同类运行"}</Badge></div>
            <dl className="mt-3 grid gap-2 sm:grid-cols-3">
              <div><dt className="text-[13px] text-slate-500">产品系列</dt><dd className="mt-1 text-sm font-medium">{baselineProcessExecution.productFamilyCode || "未记录"}</dd></div>
              <div><dt className="text-[13px] text-slate-500">设备</dt><dd className="mt-1 text-sm font-medium">{baselineProcessExecution.equipmentId || "未记录"}</dd></div>
              <div><dt className="text-[13px] text-slate-500">配方版本</dt><dd className="mt-1 text-sm font-medium">{baselineProcessExecution.processSpecificationId || "未记录"}</dd></div>
            </dl>
            {!comparisonReady && <p className="mt-3 text-sm text-amber-800">需要另一条具有相同产品系列（未记录产品系列时使用相同设备）的已完成运行。</p>}
          </div>
        )}
      </Card>
      {result ? (
        <>
          <Alert tone={investigation?.status === "ready" ? "success" : "warning"} title="对比结论">
            {investigation?.status === "ready"
              ? `已找到 ${firstDeviationRows.length} 个优先偏离和 ${causeRows.length} 个候选原因，可继续用真实生产证据复核。`
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
          <Card title="差异说明" description="汇总同类运行是否匹配、数据是否可用，以及第一次出现的偏离。">
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
                {(investigation?.counterEvidence || []).length ? <ul className="list-disc space-y-1 pl-5 text-sm text-slate-700">{investigation.counterEvidence.map((item, index) => <li key={`${item.candidateId}-${item.kind}-${index}`}>{item.statement}</li>)}</ul> : <p className="text-sm text-slate-500">还没有可核对的差异。</p>}
              </div>
              <div>
                <h4 className="mb-2 text-sm font-semibold text-slate-900">缺失数据</h4>
                {(investigation?.missingData || []).length ? <ul className="list-disc space-y-1 pl-5 text-sm text-slate-700">{investigation.missingData.map(item => <li key={item}>{item}</li>)}</ul> : <p className="text-sm text-emerald-700">对比所需的数据已经齐。</p>}
              </div>
            </div>
            <ConclusionBoundary>{investigation?.conclusionGuardrail || "当前结果只能作为待验证假设。"}</ConclusionBoundary>
          </Card>
          <details className="rounded-lg border border-slate-200 bg-white"><summary className="cursor-pointer px-5 py-4 text-sm font-semibold text-slate-900">查看参与对比的 {comparedProcessExecutions.length} 条运行</summary><div className="border-t border-slate-100 p-5"><Card title="运行概况">
            <DataTable
              rows={comparedProcessExecutions}
              getRowKey={row => `${row.comparisonRole}-${row.executionId}`}
              columns={[
                { key: "comparisonRole", label: "角色", render: value => <Badge tone={value === "基准" ? "info" : "neutral"}>{value}</Badge> },
                { key: "executionId", label: "运行" },
                { key: "equipmentId", label: "设备单元", render: (value, row) => <div><p>{value || "未记录"}</p>{row.subjectId && row.subjectId !== value && <p className="text-xs text-slate-500">逻辑对象 {row.subjectId}</p>}</div> },
                { key: "completedAt", label: "结束时间", render: formatTime },
                { key: "durationMs", label: "时长（秒）", render: value => formatDecimal(Number(value) / 1000) },
                { key: "sampleCount", label: "样本数", render: formatInteger },
                { key: "lifecycleComplete", label: "过程执行边界", render: value => value ? <Badge tone="success">完整</Badge> : <Badge tone="warning">缺少开始或结束</Badge> },
                { key: "processDataQuality", label: "数据状态", render: value => <StatusBadge value={value?.status} /> },
              ]}
            />
          </Card></div></details>
          <Card title="可能相关的控制参数" description="列出与质量差异同时出现、并且能对应到控制参数的差异。过程曲线只用来解释这些参数。" actions={<Link className="text-sm font-semibold text-trajectory-700" to="/configuration/process-specifications">去配方版本</Link>}>
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
                    { key: "displayName", label: "控制参数" },
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
                        ? ` 当前可能受${candidate.confoundersLabel}混杂，需要在后续真实运行中持续分层核对。`
                        : " 当前未识别出明确混杂因素，仍需通过后续真实运行结果复核。"}
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
            ) : <EmptyState title="还没有能对应到控制参数的差异" description="至少需要合格与不合格运行，并且控制参数或过程曲线具有可比较差异。" />}
          </Card>
          <details className="rounded-lg border border-slate-200 bg-white"><summary className="cursor-pointer px-5 py-4 text-sm font-semibold text-slate-900">查看全部信号差异（{signalRows.length} 项）</summary><div className="border-t border-slate-100 p-5"><Card title="信号差异" description="按变化幅度列出前 30 项，便于工程师核对阶段和参数差异。">
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
  const [params, setParams] = useSearchParams();
  const [runImpactPage, setRunImpactPage] = useState(1);
  const [runImpactPageSize, setRunImpactPageSize] = useState(50);
  const [selectedImpactReason, setSelectedImpactReason] = useState("");
  const [objectPage, setObjectPage] = useState(1);
  const [objectPageSize, setObjectPageSize] = useState(50);
  const [fromDraft, setFromDraft] = useState(() => formatLocalDateTime(params.get("from")));
  const [toDraft, setToDraft] = useState(() => formatLocalDateTime(params.get("to")));
  const siteScope = useSiteScope();
  const { siteId, siteChoices, selectSite } = siteScope;
  const fromParam = params.get("from") || "";
  const toParam = params.get("to") || "";
  const edgeId = params.get("edgeId") || "";
  const subjectType = params.get("subjectType")?.trim().toLowerCase() || "";
  const subjectId = params.get("subjectId")?.trim() || "";
  const parsedFrom = fromParam ? new Date(fromParam).getTime() : null;
  const parsedTo = toParam ? new Date(toParam).getTime() : null;
  const invalidAppliedRange = (fromParam && !Number.isFinite(parsedFrom)) ||
    (toParam && !Number.isFinite(parsedTo)) ||
    (parsedFrom != null && parsedTo != null && parsedFrom > parsedTo);
  const edgeRows = useMemo(
    () => extractRows(siteScope.edgeResponse.data).filter(edge => !siteId || edge.siteId === siteId),
    [siteId, siteScope.edgeResponse.data],
  );
  const objectQuery = new URLSearchParams({
    limit: String(objectPageSize),
    offset: String((objectPage - 1) * objectPageSize),
  });
  if (siteId) objectQuery.set("siteId", siteId);
  if (edgeId) objectQuery.set("edgeId", edgeId);
  if (fromParam) objectQuery.set("from", fromParam);
  if (toParam) objectQuery.set("to", toParam);
  if (subjectType) objectQuery.set("subjectType", subjectType);
  if (subjectId) objectQuery.set("subjectId", subjectId);
  const baselineQuery = new URLSearchParams({ maximumRuns: "2000" });
  if (edgeId) baselineQuery.set("edgeId", edgeId);
  if (fromParam) baselineQuery.set("from", fromParam);
  if (toParam) baselineQuery.set("to", toParam);
  if (subjectType === "equipment" && subjectId) {
    baselineQuery.set("equipmentId", subjectId);
  } else if (subjectType && subjectId) {
    baselineQuery.set("subjectType", subjectType);
    baselineQuery.set("subjectId", subjectId);
  }
  if (siteId) baselineQuery.set("siteId", siteId);
  const baseline = useApi(siteId && !invalidAppliedRange ? `/api/v1/data-reliability/baseline?${baselineQuery}` : "", { enabled: Boolean(siteId && !invalidAppliedRange) });
  const objectScopeKey = objectQuery.toString();
  const tagObjectScope = useCallback(payload => Array.isArray(payload)
    ? { data: payload, total: payload.length, requestScopeKey: objectScopeKey }
    : { ...(payload || {}), requestScopeKey: objectScopeKey }, [objectScopeKey]);
  const objects = useApi(siteId && !invalidAppliedRange ? `/api/v1/data-objects?${objectQuery}` : "", {
    enabled: Boolean(siteId && !invalidAppliedRange),
    transform: tagObjectScope,
  });
  const objectsMatchCurrentScope = objects.data?.requestScopeKey === objectScopeKey;
  const objectRows = objectsMatchCurrentScope ? extractRows(objects.data) : [];
  const objectTotal = objectsMatchCurrentScope ? Number(objects.data?.total ?? objectRows.length) : 0;
  const rates = baseline.data?.rates || [];
  const contexts = Array.from(new Map(
    (baseline.data?.contextFields || []).map(item => [item.field, item]),
  ).values());
  const contextFactors = baseline.data?.contextFactors || [];
  const factorOverlaps = baseline.data?.contextFactorOverlaps || [];
  const exclusions = baseline.data?.exclusions || [];
  const runImpacts = baseline.data?.runImpacts || [];
  const filteredRunImpacts = selectedImpactReason
    ? runImpacts.filter(item => item.reasonCodes?.includes(selectedImpactReason))
    : runImpacts;
  const impactFilterKey = [siteId, edgeId, fromParam, toParam, subjectType, subjectId].join("|");
  const rangeDraftDirty = fromDraft !== formatLocalDateTime(fromParam) || toDraft !== formatLocalDateTime(toParam);
  const draftFromTime = fromDraft ? new Date(fromDraft).getTime() : null;
  const draftToTime = toDraft ? new Date(toDraft).getTime() : null;
  const invalidRange = (fromDraft && !Number.isFinite(draftFromTime)) ||
    (toDraft && !Number.isFinite(draftToTime)) ||
    (draftFromTime != null && draftToTime != null && draftFromTime > draftToTime);
  const visibleRunImpacts = filteredRunImpacts.slice(
    (runImpactPage - 1) * runImpactPageSize,
    runImpactPage * runImpactPageSize,
  );
  const rate = code => rates.find(item => item.code === code);
  const responseFromMs = baseline.data?.from ? new Date(baseline.data.from).getTime() : null;
  const responseToMs = baseline.data?.to ? new Date(baseline.data.to).getTime() : null;
  const expectedEquipmentId = subjectType === "equipment" && subjectId ? subjectId : "";
  const expectedSubjectType = subjectType && subjectType !== "equipment" && subjectId ? subjectType : "";
  const expectedSubjectId = expectedSubjectType ? subjectId : "";
  const baselineScopeMatches = Boolean(baseline.data) && baseline.data.siteId === siteId &&
    (baseline.data.edgeId || "") === edgeId &&
    (baseline.data.equipmentId || "") === expectedEquipmentId &&
    (baseline.data.subjectType || "") === expectedSubjectType &&
    (baseline.data.subjectId || "") === expectedSubjectId &&
    responseFromMs === parsedFrom && responseToMs === parsedTo;
  const hasBaselineData = Boolean(baseline.data) && baselineScopeMatches && !invalidAppliedRange;
  const hasAnalyzedRuns = hasBaselineData && Number(baseline.data?.analyzedRunCount || 0) > 0;
  const baselineCount = value => hasAnalyzedRuns && value != null ? formatInteger(value) : "—";
  const rateCountHint = code => hasAnalyzedRuns
    ? `${formatInteger(rate(code)?.numerator ?? 0)} / ${formatInteger(rate(code)?.denominator ?? 0)} 次运行`
    : "暂无可评估运行";
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
  const loading = siteScope.loading || baseline.loading || objects.loading;
  const error = siteScope.error || baseline.error || objects.error;
  useEffect(() => {
    if (window.location.hash !== "#run-impacts") return undefined;
    const frame = window.requestAnimationFrame(() => {
      document.getElementById("run-impacts")?.scrollIntoView({ block: "start" });
    });
    return () => window.cancelAnimationFrame(frame);
  }, [baseline.data?.generatedAt, params]);
  useEffect(() => {
    setRunImpactPage(1);
    setSelectedImpactReason("");
    setObjectPage(1);
  }, [impactFilterKey, baseline.data?.generatedAt]);
  useEffect(() => {
    setFromDraft(formatLocalDateTime(fromParam));
    setToDraft(formatLocalDateTime(toParam));
  }, [fromParam, toParam]);
  useEffect(() => {
    if (!siteId || !edgeId || siteScope.edgeResponse.loading || siteScope.edgeResponse.error) return;
    if (edgeRows.some(edge => edge.edgeId === edgeId)) return;
    setParams(current => {
      if (current.get("edgeId") !== edgeId) return current;
      const next = new URLSearchParams(current);
      next.delete("edgeId");
      return next;
    }, { replace: true });
  }, [edgeId, edgeRows, setParams, siteId, siteScope.edgeResponse.error, siteScope.edgeResponse.loading]);
  const applyDateRange = (fromValue, toValue) => {
    setObjectPage(1);
    const next = new URLSearchParams(params);
    const preserveFrom = fromValue === formatLocalDateTime(fromParam) && Number.isFinite(parsedFrom);
    const preserveTo = toValue === formatLocalDateTime(toParam) && Number.isFinite(parsedTo);
    const fromIso = preserveFrom ? fromParam : fromValue ? localDateTimeToIso(fromValue) : null;
    const toIso = preserveTo ? toParam : toValue ? localDateTimeToIso(toValue) : null;
    if (fromIso) next.set("from", fromIso);
    else next.delete("from");
    if (toIso) next.set("to", toIso);
    else next.delete("to");
    setParams(next);
  };
  const applyQuickRange = hours => {
    const to = new Date();
    const from = new Date(to.getTime() - hours * 60 * 60 * 1000);
    const fromIso = from.toISOString();
    const toIso = to.toISOString();
    setFromDraft(formatLocalDateTime(fromIso));
    setToDraft(formatLocalDateTime(toIso));
    setObjectPage(1);
    const next = new URLSearchParams(params);
    next.set("from", fromIso);
    next.set("to", toIso);
    setParams(next);
  };
  const clearDateRange = () => {
    setFromDraft("");
    setToDraft("");
    applyDateRange("", "");
  };
  const updateSearchParam = (key, value) => {
    setObjectPage(1);
    setParams(current => {
      const next = new URLSearchParams(current);
      if (value) next.set(key, value);
      else next.delete(key);
      return next;
    });
  };
  return (
    <Page
      title="数据质量"
      description={subjectId
        ? subjectType === "equipment"
          ? `检查设备 ${subjectId} 的采样与运行数据是否满足通用基线条件；具体分析和建议仍需单独核验。`
          : subjectType
            ? `检查${objectTypeLabel(subjectType)} ${subjectId} 的采样和关联运行；通用基线只统计该对象命中的运行，具体分析和建议仍需单独核验。`
            : `数据对象列表已筛选到 ${subjectId}；运行可靠性基线按当前站点、节点与事件时间范围汇总。`
        : "先看采样是否连续、运行是否完整，以及运行是否满足通用数据可靠性基线条件。"}
    >
      <RequestError error={error} onRetry={() => siteScope.error ? siteScope.reload() : Promise.all([baseline.reload(), objects.reload()])} />
      <div className="mb-5 max-w-md"><SiteScopeField siteId={siteId} siteChoices={siteChoices} onChange={selectSite} loading={siteScope.loading} /></div>
      {siteId && (
        <Card title="查询范围" description="对象采样与运行基线共享站点、现场节点和事件时间范围；提供完整对象类型与编号时，运行基线也会限定到对应逻辑对象。时间按浏览器本地时区选择，提交时转换为带时区时间；起止边界均包含。">
          <div className="grid gap-3 lg:grid-cols-3">
            <Field label="现场节点">
              <Select value={edgeId} onChange={event => updateSearchParam("edgeId", event.target.value)}>
                <option value="">当前站点全部现场节点</option>
                {edgeRows.map(edge => (
                  <option key={edge.edgeId} value={edge.edgeId}>
                    {edge.edgeId}{edge.hostname ? ` · ${edge.hostname}` : ""}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="开始事件时间">
              <DateTimeField value={fromDraft} onChange={setFromDraft} aria-label="开始事件时间" />
            </Field>
            <Field label="结束事件时间">
              <DateTimeField value={toDraft} onChange={setToDraft} aria-label="结束事件时间" />
            </Field>
          </div>
          <div className="mt-4 flex flex-wrap items-center gap-2">
            <span className="mr-1 text-xs text-slate-500">快速范围</span>
            <Button onClick={() => applyQuickRange(24)}>近 24 小时</Button>
            <Button onClick={() => applyQuickRange(24 * 7)}>近 7 天</Button>
            <Button onClick={() => applyQuickRange(24 * 30)}>近 30 天</Button>
            <Button onClick={clearDateRange}>不限时间</Button>
            <Button variant="primary" disabled={!rangeDraftDirty || invalidRange} onClick={() => applyDateRange(fromDraft, toDraft)}>
              应用时间范围
            </Button>
          </div>
          {invalidRange && !invalidAppliedRange && <Alert tone="warning">开始时间不能晚于结束时间；当前查询结果仍对应上一次已应用的范围。</Alert>}
          {invalidAppliedRange && <Alert tone="danger">URL 中的事件时间范围无效，当前未发送数据查询。请修改时间并应用，或选择“不限时间”。</Alert>}
          {rangeDraftDirty && !invalidRange && <p className="mt-3 text-xs text-slate-500">时间范围尚未应用；表格仍显示上一次已应用范围的数据。</p>}
          <p className="mt-3 text-xs text-slate-500">
            当前已应用：{fromParam ? formatTime(fromParam) : "不限开始时间"} 至 {toParam ? formatTime(toParam) : "不限结束时间"}。按事件发生时间筛选；基线按范围内命中的开始或完成事件选择运行。
          </p>
          {subjectType && subjectId && <p className="mt-1 text-xs text-slate-500">对象范围：{objectTypeLabel(subjectType)} · {subjectId}{subjectType === "equipment" ? "（按物理设备编号筛选运行）" : "（按逻辑对象筛选运行）"}</p>}
        </Card>
      )}
      {!siteId && !siteScope.loading ? <EmptyState title={siteChoices.length ? "请选择分析站点" : "当前没有可访问站点"} description={siteScope.invalidRequestedSite ? "URL 中的站点不在当前账号可访问范围内。请选择一个有权访问的站点。" : "数据可靠性基线按单个站点计算。多站点账号需要先选择一个站点。"} /> : loading ? <LoadingCard /> : (
        <div className="space-y-5">
          {!hasBaselineData ? (
            <Card title="运行基线暂不可判断">
              <EmptyState
                title={invalidAppliedRange ? "时间范围无效" : baseline.error ? "运行基线请求失败" : "运行基线尚不可用"}
                description={invalidAppliedRange
                  ? "当前 URL 中的时间范围没有提交到服务端，因此不能判断运行是否满足基线条件。请修正范围并重新应用。"
                  : baseline.error
                    ? "没有可用的成功响应，不能把空结果解释为没有基线缺口。请重试基线查询。"
                    : "尚未收到运行基线结果；当前不能判断是否存在数据质量问题。"}
                actions={baseline.error ? <Button onClick={() => baseline.reload()}>重试基线查询</Button> : undefined}
              />
            </Card>
          ) : (
            <>
          {baseline.error && <Alert tone="warning">本次基线刷新失败；以下内容来自上一次成功查询，不代表本次刷新已完成。</Alert>}
          {baseline.data?.truncated && (
            <Alert tone="warning">
              匹配运行超过本次上限；按最近事件选取了 {formatInteger((baseline.data.analyzedRunCount ?? 0) + (baseline.data.missingExecutionRowCount ?? 0))} 个运行，其中 {formatInteger(baseline.data.analyzedRunCount)} 个有可用分析记录并计入指标和逐运行清单。
            </Alert>
          )}
          {baseline.data && <p className="text-sm leading-6 text-slate-600">
            本次观察到 {formatInteger(baseline.data.matchingObservedRunCount ?? baseline.data.matchingCompletedRunCount)} 个在查询时间范围内有开始或完成事件的运行，分析 {formatInteger(baseline.data.analyzedRunCount)} 个；其中 {formatInteger(baseline.data.matchingCompletedRunCount)} 个在范围内包含完成事件。缺少开始或完成边界的运行也计入生命周期完整率。
          </p>}
          <dl className="grid grid-cols-2 divide-x divide-y divide-slate-200 rounded-lg border border-slate-200 bg-white xl:grid-cols-4 xl:divide-y-0">
            {[
              ["过程数据完整率", rateValue("process_data_completeness"), rateCountHint("process_data_completeness")],
              ["实际参数覆盖率", rateValue("actual_parameter_coverage"), hasAnalyzedRuns ? "设备实际回读" : "暂无可评估运行"],
              ["最小上下文覆盖率", rateValue("minimal_context_coverage"), hasAnalyzedRuns ? "设备与运行身份" : "暂无可评估运行"],
              ["运行—质量关联率", rateValue("run_quality_association"), hasAnalyzedRuns ? "有效检验结果" : "暂无可评估运行"],
            ].map(([label, value, hint]) => (
              <div key={label} className="px-4 py-3">
                <dt className="text-[13px] font-medium text-slate-500">{label}</dt>
                <dd className="mt-1 flex items-baseline gap-2">
                  <strong className="text-xl font-semibold text-slate-950 tabular-nums">{value}</strong>
                  <span className="text-[13px] text-slate-500">{hint}</span>
                </dd>
              </div>
            ))}
          </dl>
          <div className="grid gap-5 xl:grid-cols-[1.4fr_.6fr]">
            <Card title="通用基线条件">
              <dl className="mb-4 grid divide-y divide-slate-200 rounded-md border border-slate-200 sm:grid-cols-3 sm:divide-x sm:divide-y-0">
                {[
                  ["条件通过率", rateValue("analysis_admission"), rateCountHint("analysis_admission")],
                  ["序列缺口", baselineCount(baseline.data?.sequenceGapCount), hasAnalyzedRuns ? "已分析运行累计" : "暂无可评估运行"],
                  ["最大采样空窗", baseline.data?.maximumSampleGapMs == null ? "—" : formatDuration(baseline.data.maximumSampleGapMs), ""],
                ].map(([label, value, hint]) => (
                  <div key={label} className="px-4 py-3">
                    <dt className="text-[13px] font-medium text-slate-500">{label}</dt>
                    <dd className="mt-1 text-xl font-semibold text-slate-950 tabular-nums">{value}</dd>
                    {hint && <dd className="mt-0.5 text-[13px] text-slate-500">{hint}</dd>}
                  </div>
                ))}
              </dl>
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
            <Card title="基线条件缺口">
              {exclusions.length ? (
                <DataTable
                  rows={exclusions}
                  keyField="code"
                  columns={[
                    {
                      key: "name",
                      label: "原因",
                      render: (value, row) => (
                        <button
                          type="button"
                          className="rounded text-left font-medium text-trajectory-700 underline decoration-trajectory-300 underline-offset-2 hover:text-trajectory-900 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-trajectory-600"
                          aria-pressed={selectedImpactReason === row.code}
                          aria-controls="run-impacts"
                          onClick={() => {
                            setSelectedImpactReason(current => current === row.code ? "" : row.code);
                            setRunImpactPage(1);
                            window.requestAnimationFrame(() => {
                              document.getElementById("run-impacts")?.scrollIntoView({ behavior: "smooth", block: "start" });
                            });
                          }}
                        >
                          {value}<span className="sr-only">：筛选对应运行</span>
                        </button>
                      ),
                    },
                    { key: "runCount", label: "运行数", render: formatInteger },
                  ]}
                />
              ) : <EmptyState title={hasAnalyzedRuns ? "未发现基线条件缺口" : "暂无可评估运行"} description={hasAnalyzedRuns ? "本次成功加载并分析的运行均满足通用数据可靠性基线条件；未加载的匹配运行不在此结论内。" : "当前范围没有成功加载的运行样本，因此不能计算基线缺口。"} />}
              {exclusions.length > 0 && <p className="mt-2 text-xs text-slate-500">选择一项原因可定位到对应运行。</p>}
            </Card>
          </div>
          <div id="run-impacts" className="scroll-mt-5">
            <Card
              title="未满足基线条件的运行"
              description="列表只显示未满足通用数据可靠性基线条件的已分析运行。命中原因本身不证明某项具体分析或配方建议已受影响；请在对应页面复核其准入结果。样本来源与质量汇总该运行本次返回的完整帧集；起止时间只用于筛选运行，因此帧的首末观测时间可能超出所选窗口。应用观察只表示 Edge 心跳报告过某任务版本已应用，区间端点是 Platform 接收时刻，不是精确生效边界；运行时间重叠仅是相关证据，不证明该版本造成缺口。没有应用观察或匹配帧时，状态保持未知；首末采样时间不代表连续覆盖。多项原因可以同时命中。"
            >
            {baseline.data?.missingExecutionRowCount > 0 && (
              <div className="mb-4">
                <Alert tone="warning">
                  本次纳入范围的匹配运行中，有 {formatInteger(baseline.data.missingExecutionRowCount)} 个没有可用的运行分析记录，未计入基线或本清单。
                </Alert>
              </div>
            )}
            {baseline.data?.taskDeploymentObservationHistoryTruncated && (
              <div className="mb-4">
                <Alert tone="warning">
                  任务版本应用观察超过本次查询上限。未列出的运行保持未知，不能据此认为该时段没有应用观察。
                </Alert>
              </div>
            )}
            {baseline.data?.expectedConfigurationCoverageTruncated && (
              <div className="mb-4">
                <Alert tone="warning">
                  应用观察覆盖详情超过本次响应上限。未显示的映射代码和覆盖结果保持未知。
                </Alert>
              </div>
            )}
            {selectedImpactReason && (
              <div className="mb-3 flex flex-wrap items-center justify-between gap-2 text-sm text-slate-600">
                <span role="status" aria-live="polite">按“{reliabilityRunReasonDetails[selectedImpactReason]?.[0] || selectedImpactReason}”筛选：{formatInteger(filteredRunImpacts.length)} 个已分析运行</span>
                <Button onClick={() => { setSelectedImpactReason(""); setRunImpactPage(1); }}>显示全部运行</Button>
              </div>
            )}
            {!selectedImpactReason && <span className="sr-only" role="status" aria-live="polite">显示全部已分析运行。</span>}
            {filteredRunImpacts.length ? (
              <>
                <DataTable
                  rows={visibleRunImpacts}
                  keyField="executionId"
                  columns={[
                    {
                      key: "executionId",
                      label: "运行",
                      render: value => (
                        <Link
                          className="font-medium text-trajectory-700 underline decoration-trajectory-300 underline-offset-2 hover:text-trajectory-900"
                          to={`/process-executions/${encodeURIComponent(value)}?siteId=${encodeURIComponent(siteId)}`}
                        >
                          {value}
                        </Link>
                      ),
                    },
                    { key: "equipmentId", label: "设备" },
                    {
                      key: "startedAt",
                      label: "开始 / 完成",
                      sortable: false,
                      render: (_, row) => (
                        <div className="space-y-1 text-xs text-slate-600">
                          <div>{row.hasStarted ? `开始 ${formatTime(row.startedAt)}` : "未记录开始事件"}</div>
                          <div>{row.hasCompleted ? `完成 ${formatTime(row.completedAt)}` : "未记录完成事件"}</div>
                        </div>
                      ),
                    },
                    {
                      key: "processDataStatus",
                      label: "过程数据",
                      render: value => <StatusBadge value={value} />,
                    },
                    {
                      key: "sampleConfigurationEvidence",
                      label: "样本来源与应用观察",
                      sortable: false,
                      render: (evidence, run) => {
                        const groups = evidence || [];
                        const expectedCoverage = run.expectedConfigurationCoverage || [];
                        const hasEvidence = groups.length > 0 || expectedCoverage.length > 0;
                        if (!hasEvidence) return (
                          <p className="min-w-64 whitespace-normal text-xs leading-5 text-slate-600">
                            {baseline.data?.taskDeploymentObservationHistoryTruncated
                              ? "应用观察历史已截断；此运行的来源状态未知。"
                              : baseline.data?.expectedConfigurationCoverageTruncated
                                ? "应用观察覆盖详情已截断；此运行的版本与映射来源未知。"
                                : "没有可核验的样本来源或任务版本应用观察；不能据此判断采集任务是否健康。"}
                          </p>
                        );
                        return (
                          <div className="min-w-80 space-y-3">
                            {groups.length === 0 && <p className="text-xs leading-5 text-slate-600">
                              未返回匹配的已保存采样帧；仅凭这一点无法判断采集任务是否健康。
                            </p>}
                            {groups.map((item, index) => {
                              const summarizedGroupCount = item.summarizedConfigurationGroupCount ?? 0;
                              const hasConfiguration = item.appliedConfigurationKind &&
                                item.appliedConfigurationId &&
                                item.appliedConfigurationVersion != null;
                              const source = summarizedGroupCount > 0
                                ? `其余 ${formatInteger(summarizedGroupCount)} 组配置来源（已汇总）`
                                : hasConfiguration
                                  ? `${item.appliedConfigurationKind} / ${item.appliedConfigurationId}@v${item.appliedConfigurationVersion}`
                                  : "来源未知 / 帧未报告配置引用";
                              const referenceIsVerifiable = summarizedGroupCount === 0 &&
                                item.appliedConfigurationKind === "ingestion-task" &&
                                Boolean(item.appliedConfigurationId) &&
                                Number.isInteger(Number(item.appliedConfigurationVersion)) &&
                                Number(item.appliedConfigurationVersion) > 0 &&
                                Boolean(item.sourceEdgeId);
                              const verificationStatus = referenceIsVerifiable &&
                                Object.hasOwn(sampleSourceVerificationDetails, item.referenceVerificationStatus)
                                ? item.referenceVerificationStatus
                                : "unverifiable";
                              const verification = sampleSourceVerificationDetails[verificationStatus];
                              const configurationStatusLabel = ingestionTaskStatusLabels[item.configurationStatus];
                              return (
                                <div key={`${source}:${index}`} className="rounded-md border border-slate-200 bg-slate-50 px-2.5 py-2">
                                  <div className="font-medium text-slate-800">{source}</div>
                                  <div className="mt-2 flex flex-wrap items-center gap-2">
                                    <span className="text-xs font-medium text-slate-600">帧报告来源/版本核对</span>
                                    <Badge tone={verification.tone}>{verification.label}</Badge>
                                  </div>
                                  <p className="mt-1 text-xs leading-5 text-slate-600">{verification.detail}</p>
                                  {item.sourceEdgeId && <div className="text-xs leading-5 text-slate-600">帧报告 Edge：{item.sourceEdgeId}</div>}
                                  {verificationStatus === "matched" && configurationStatusLabel && (
                                    <div className="text-xs leading-5 text-slate-600">当前平台记录状态：{configurationStatusLabel}</div>
                                  )}
                                  <div className="mt-1 text-xs leading-5 text-slate-600">
                                    {formatInteger(item.sampleFrameCount ?? 0)} 帧 · {formatInteger(item.observedValueCount ?? 0)} 个已保存值
                                  </div>
                                  <div className="text-xs leading-5 text-slate-600">
                                    首末观测：{item.firstOccurredAt ? formatTime(item.firstOccurredAt) : "—"} 至 {item.lastOccurredAt ? formatTime(item.lastOccurredAt) : "—"}
                                  </div>
                                  <div className="text-xs leading-5 text-slate-600">
                                    值质量：好 {formatInteger(item.goodValueCount ?? 0)} · 不确定 {formatInteger(item.uncertainValueCount ?? 0)} · 坏 {formatInteger(item.badValueCount ?? 0)} · 未分类 {formatInteger(item.unclassifiedQualityValueCount ?? 0)}
                                  </div>
                                </div>
                              );
                            })}
                            <section className="border-t border-slate-200 pt-2" aria-label={`${run.executionId} 的任务版本应用观察`}>
                              <h4 className="text-xs font-semibold text-slate-700">运行时段的任务版本应用观察</h4>
                              {expectedCoverage.length === 0 ? (
                                <p className="mt-1 text-xs leading-5 text-slate-600">
                                  {baseline.data?.taskDeploymentObservationHistoryTruncated
                                    ? "本次应用观察历史已截断；此运行是否有重叠观察仍未知。"
                                    : baseline.data?.expectedConfigurationCoverageTruncated
                                      ? "应用观察覆盖详情已截断；此运行的版本与映射来源保持未知。"
                                      : "没有可核验的 Applied 版本观察；这不代表该运行时段未部署任务或没有采样。"}
                                </p>
                              ) : (
                                <div className="mt-2 space-y-2">
                                  {expectedCoverage.map((item, index) => {
                                    const evidenceStatus = item.sampleEvidenceStatus;
                                    const sampleEvidenceStatus = {
                                      matched: { label: "找到该版本样本", tone: "success" },
                                      "no-matching-frames": { label: "未找到该版本样本", tone: "warning" },
                                      "summarized-unverifiable": { label: "来源已汇总，无法逐版本核验", tone: "neutral" },
                                      "unverifiable-task-version": { label: "任务版本归属无法核验", tone: "neutral" },
                                      "observation-history-truncated": { label: "应用观察历史截断，无法判断", tone: "neutral" },
                                      "coverage-result-truncated": { label: "映射覆盖详情截断，无法判断", tone: "neutral" },
                                    }[evidenceStatus] || { label: "样本来源未知", tone: "neutral" };
                                    const expectedCodes = item.requiredDataItemCodes || [];
                                    const observedCodes = item.observedDataItemCodes || [];
                                    const notObservedCodes = item.notObservedRequiredDataItemCodes || [];
                                    const sourceCodesAreVerifiable = ["matched", "no-matching-frames"].includes(evidenceStatus);
                                    return (
                                      <div key={`${item.edgeId}:${item.taskId}:${item.taskVersion}:${index}`} className="rounded-md border border-slate-200 bg-slate-50 px-2.5 py-2">
                                        <div className="font-medium text-slate-800">{item.taskId}@v{item.taskVersion} · {item.edgeId}</div>
                                        <div className="mt-1 text-xs leading-5 text-slate-600">
                                          Platform 观察：{item.firstObservedAt ? formatTime(item.firstObservedAt) : "—"} 至 {item.lastObservedAt ? formatTime(item.lastObservedAt) : "—"}
                                        </div>
                                        <div className="text-xs leading-5 text-slate-600">
                                          与运行重叠：{item.runOverlapStart ? formatTime(item.runOverlapStart) : "—"} 至 {item.runOverlapEnd ? formatTime(item.runOverlapEnd) : "—"}
                                        </div>
                                        <div className="mt-1 flex flex-wrap items-center gap-2">
                                          <Badge tone="info">Edge 报告已应用版本</Badge>
                                          <Badge tone={sampleEvidenceStatus.tone}>{sampleEvidenceStatus.label}</Badge>
                                          {evidenceStatus === "matched" && <span className="text-xs text-slate-600">{formatInteger(item.sampleFrameCount ?? 0)} 帧</span>}
                                        </div>
                                        {sourceCodesAreVerifiable && expectedCodes.length > 0 && (
                                          <div className="mt-2 text-xs leading-5 text-slate-600">
                                            <p>必需映射 {expectedCodes.length} 项；该版本样本报告 {observedCodes.length} 项。</p>
                                            {notObservedCodes.length > 0 && <details className="mt-1">
                                              <summary className="cursor-pointer font-medium text-amber-800">有 {notObservedCodes.length} 项必需映射未见于该版本返回的样本值</summary>
                                              <p className="mt-1 break-words">{notObservedCodes.join("、")}</p>
                                            </details>}
                                            {evidenceStatus === "matched" && notObservedCodes.length === 0 && <p>该版本返回的样本值包含全部必需映射代码。</p>}
                                          </div>
                                        )}
                                        <p className="mt-2 text-xs leading-5 text-slate-600">
                                          {evidenceStatus === "no-matching-frames"
                                            ? "该运行未找到此版本的已保存样本帧；请核对节点采样和运行边界，不能单凭此项归因缺口。"
                                            : evidenceStatus === "summarized-unverifiable"
                                              ? "样本来源分组超过逐项明细范围；未观察到的映射代码不能解释为缺失。"
                                              : evidenceStatus === "unverifiable-task-version"
                                                ? "平台无法核验此任务版本与运行对象的对应关系；不会列出缺失映射代码。"
                                                : evidenceStatus === "observation-history-truncated"
                                                  ? "应用观察超出本次查询结果上限；该版本的样本覆盖状态保持未知。"
                                                  : evidenceStatus === "coverage-result-truncated"
                                                    ? "覆盖详情超出本次响应上限；未返回的映射代码和覆盖结果保持未知。"
                                                : "应用观察与运行时间重叠只提供相关线索；不证明精确生效边界或因果关系。"}
                                        </p>
                                      </div>
                                    );
                                  })}
                                </div>
                              )}
                            </section>
                          </div>
                        );
                      },
                    },
                    {
                      key: "reasonCodes",
                      label: "基线缺口与建议处理",
                      sortable: false,
                      render: (codes, run) => {
                        const seenFollowUps = new Set();
                        return (
                          <div className="min-w-64 space-y-2">
                            {(codes || []).map(code => {
                              const [label, action] = reliabilityRunReasonDetails[code] || [code, "查看运行详情确认具体情况。"];
                              const followUps = reliabilityRunActions(code, run, siteId).filter(followUp => {
                                if (seenFollowUps.has(followUp.href)) return false;
                                seenFollowUps.add(followUp.href);
                                return true;
                              });
                              return (
                                <div key={code}>
                                  <Badge tone="warning">{label}</Badge>
                                  <p className="mt-1 whitespace-normal text-xs leading-5 text-slate-600">{action}</p>
                                  <div className="mt-1 flex flex-wrap gap-x-3 gap-y-1">
                                    {followUps.map(followUp => (
                                      <Link key={`${code}:${followUp.href}`} className="text-xs font-medium text-trajectory-700 underline decoration-trajectory-300 underline-offset-2 hover:text-trajectory-900" to={followUp.href}>
                                        {followUp.label}
                                      </Link>
                                    ))}
                                  </div>
                                </div>
                              );
                            })}
                          </div>
                        );
                      },
                    },
                  ]}
                />
                <Pagination
                  page={runImpactPage}
                  pageSize={runImpactPageSize}
                  total={filteredRunImpacts.length}
                  onPageChange={setRunImpactPage}
                  onPageSizeChange={value => { setRunImpactPageSize(value); setRunImpactPage(1); }}
                />
              </>
            ) : (
              <EmptyState
                title={selectedImpactReason ? "所选原因没有匹配的已分析运行" : hasAnalyzedRuns ? "未发现基线条件缺口" : "当前范围没有可分析的运行"}
                description={selectedImpactReason
                  ? "当前已分析的运行中没有命中此原因；清除原因筛选可查看其他基线缺口。"
                  : hasAnalyzedRuns
                    ? "本次成功加载并分析的运行均满足通用数据可靠性基线条件。"
                    : "当前范围没有成功加载的运行样本；扩大时间或现场节点范围后再查看。"}
                actions={selectedImpactReason ? <Button onClick={() => { setSelectedImpactReason(""); setRunImpactPage(1); }}>清除原因筛选</Button> : undefined}
              />
            )}
            </Card>
          </div>
          <Card title="时间、顺序与上送质量">
            <DataTable
              rows={[
                { id: "duplicate", name: "重复时间戳", value: baselineCount(baseline.data?.duplicateTimestampCount), definition: "累计被去重的采样" },
                { id: "order", name: "晚到或乱序", value: baselineCount(baseline.data?.outOfOrderCount), definition: "按摄入顺序检测" },
                { id: "sequence", name: "源序列缺口", value: baselineCount(baseline.data?.sequenceGapCount), definition: "设备源序号不连续" },
                { id: "sample-gap", name: "最大采样空窗", value: baseline.data?.maximumSampleGapMs == null ? "—" : formatDuration(baseline.data.maximumSampleGapMs), definition: "相邻有效采样的最大间隔" },
                { id: "clock", name: "最大设备时钟偏差", value: baseline.data?.maximumAbsoluteSourceClockOffsetMs == null ? "—" : formatDuration(baseline.data.maximumAbsoluteSourceClockOffsetMs), definition: "设备源时间与 Edge 记录时间的绝对差" },
                { id: "p95", name: "最差运行 P95 上送延迟", value: baseline.data?.worstRunP95PlatformIngestLatencyMs == null ? "—" : formatDuration(baseline.data.worstRunP95PlatformIngestLatencyMs), definition: "包含离线缓存后的补传" },
                { id: "latency", name: "最大上送延迟", value: baseline.data?.maximumPlatformIngestLatencyMs == null ? "—" : formatDuration(baseline.data.maximumPlatformIngestLatencyMs), definition: "Edge 记录到 Platform 摄入" },
                { id: "negative", name: "负上送延迟异常", value: baselineCount(baseline.data?.negativePlatformIngestLatencyCount), definition: "Platform 时间早于 Edge 超过 1 秒" },
              ]}
              columns={[
                { key: "name", label: "指标" },
                { key: "value", label: "结果" },
                { key: "definition", label: "计算定义" },
              ]}
            />
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
          <details className="rounded-lg border border-slate-200 bg-white">
            <summary className="cursor-pointer px-5 py-4 text-sm font-semibold text-slate-900">查看分层与因素重叠</summary>
            <div className="grid gap-5 border-t border-slate-100 p-5 xl:grid-cols-2">
            <Card title="上下文分层统计" description="按设备、工装和材料批次展示运行、过程完整性和质量结果。">
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
            <Card title="因素重叠与混杂" description="重叠度表示实际出现的因素组合占理论组合的比例；完全绑定的因素无法仅凭观察数据拆分影响。" actions={<span className="text-[13px] font-medium text-amber-700">不可辨识混杂 {baselineCount(baseline.data?.unidentifiableConfoundingCount)} 项</span>}>
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
          </details>
            </>
          )}
          <Card
            title={`工业对象采样范围${objects.loading ? "（读取中）" : `（${formatInteger(objectTotal)}）`}`}
            description="统计结果与上方站点、现场节点和事件时间范围一致；对象筛选作用于此列表，设备编号筛选也会限定运行基线。"
          >
            {invalidAppliedRange ? (
              <EmptyState title="尚未查询对象采样" description="当前时间范围无效，修正范围并应用后才能读取对象数据。" />
            ) : objects.loading ? (
              <p className="py-8 text-center text-sm text-slate-500" role="status">正在读取对象采样数据…</p>
            ) : objects.error ? (
              <EmptyState
                title="采样对象暂时无法读取"
                description="请检查上方的错误信息后重试当前查询。"
                actions={<Button onClick={() => objects.reload()}>重试对象查询</Button>}
              />
            ) : objectRows.length ? (
              <>
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
                <Pagination
                  page={objectPage}
                  pageSize={objectPageSize}
                  total={objectTotal}
                  onPageChange={setObjectPage}
                  onPageSizeChange={value => { setObjectPageSize(value); setObjectPage(1); }}
                />
              </>
            ) : (
              <EmptyState
                title="当前范围没有数据对象"
                description="调整事件时间、现场节点或对象筛选后重试。"
              />
            )}
          </Card>
        </div>
      )}
    </Page>
  );
}
