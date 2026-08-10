import { useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router";
import { getJson, postJson } from "../api/http";
import { extractRows, useApi } from "../hooks/useApi";
import { Alert, Badge, Button, Card, DataTable, EmptyState, Field, Input, Metric, Page, Select, StatusBadge, notify } from "../ui/components";
import { formatTime, formatInteger, formatDuration, objectTypeLabel, LoadingCard } from "./shared";

const comparisonFeatureLabels = {
  min: "最小值",
  max: "最大值",
  mean: "平均值",
  stddev: "波动",
};

const evidenceLevelLabels = {
  stable: "证据稳定",
  exploratory: "探索性证据",
  screening: "仅稳健筛选",
  sufficient: "证据充分",
  limited: "证据有限",
  insufficient: "证据不足",
};

function formatDecimal(value) {
  if (!Number.isFinite(Number(value))) return "—";
  return Number(value).toLocaleString("zh-CN", { maximumFractionDigits: 3 });
}

export function CycleComparisonPage() {
  const [params] = useSearchParams();
  const [baseline, setBaseline] = useState(params.get("cycleId") || "");
  const [candidate, setCandidate] = useState("");
  const [comparisonScope, setComparisonScope] = useState("cohort");
  const [result, setResult] = useState(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [cycles, setCycles] = useState([]);
  const [linkedBaseline, setLinkedBaseline] = useState(null);
  const [catalogLoading, setCatalogLoading] = useState(true);
  const [cycleFilter, setCycleFilter] = useState("");
  const [researchProjects, setResearchProjects] = useState([]);
  const [researchProjectId, setResearchProjectId] = useState("");
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
    const search = cycleFilter.trim();
    const query = new URLSearchParams({ status: "completed", limit: "200" });
    if (search) query.set("search", search);
    setCatalogLoading(true);
    getJson(`/api/v1/cycles?${query}`).then(cyclePayload => {
      if (!mounted) return;
      setCycles(extractRows(cyclePayload));
    }).catch(requestError => {
      if (!mounted) return;
      setCycles([]);
      setError(requestError.message || "无法读取可比较的生产运行。");
    }).finally(() => {
      if (mounted) setCatalogLoading(false);
    });
    return () => { mounted = false; };
  }, [cycleFilter]);

  useEffect(() => {
    let mounted = true;
    if (!baseline || cycles.some(item => item.correlationId === baseline)) {
      setLinkedBaseline(null);
      return () => { mounted = false; };
    }
    getJson(`/api/v1/cycles?correlationId=${encodeURIComponent(baseline)}&limit=1`)
      .then(payload => {
        if (mounted) setLinkedBaseline(extractRows(payload)[0] || null);
      })
      .catch(() => {
        if (mounted) setLinkedBaseline(null);
      });
    return () => { mounted = false; };
  }, [baseline, cycles]);

  const baselineCycle = cycles.find(item => item.correlationId === baseline) || linkedBaseline;
  const normalizedCycleFilter = cycleFilter.trim().toLowerCase();
  const visibleCycles = cycles.filter(item => !normalizedCycleFilter || [
    item.correlationId,
    item.productSeries,
    item.productCode,
    item.machineId,
    item.recipeId,
  ].some(value => String(value || "").toLowerCase().includes(normalizedCycleFilter)));
  const comparableCycles = cycles.filter(item =>
    item.correlationId !== baseline &&
    (!baselineCycle?.productSeries
      ? item.machineId === baselineCycle?.machineId
      : item.productSeries === baselineCycle.productSeries) &&
    (!normalizedCycleFilter || [
      item.correlationId,
      item.productSeries,
      item.productCode,
      item.machineId,
      item.recipeId,
    ].some(value => String(value || "").toLowerCase().includes(normalizedCycleFilter))),
  );

  useEffect(() => {
    if (comparisonScope === "single" && candidate && !comparableCycles.some(item => item.correlationId === candidate)) {
      setCandidate("");
    }
  }, [candidate, comparableCycles, comparisonScope]);

  const cycleLabel = cycle => [
    cycle.correlationId,
    cycle.productSeries || cycle.productCode || "未标注产品",
    cycle.machineId || "未标注设备",
    cycle.completedAt ? new Date(cycle.completedAt).toLocaleString("zh-CN") : "",
  ].filter(Boolean).join(" · ");

  async function compare(event) {
    event.preventDefault();
    setBusy(true);
    setError("");
    try {
      const baselineCycleId = baseline.trim();
      if (comparisonScope === "cohort") {
        setResult(await getJson(`/api/v1/cycle-comparisons/${encodeURIComponent(baselineCycleId)}?limit=24`));
      } else {
        setResult(await postJson("/api/v1/cycle-comparisons", {
          baselineCycleId,
          cycleIds: [baselineCycleId, candidate],
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
        `/api/v1/research-projects/${researchProjectId}/hypotheses/from-cycle-comparison`,
        {
          baselineCycleId: result.baselineCycleId,
          cycleIds: comparedCycles.map(item => item.correlationId),
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
  const comparedCycles = result ? [
    { ...result.baseline, comparisonRole: "基准" },
    ...(result.historicalCycles || []).map(cycle => ({ ...cycle, comparisonRole: "对比" })),
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
  const causeRows = useMemo(() => (result?.diagnosis?.candidates || [])
    .map(candidate => ({
      ...candidate,
      sourceLabel: candidate.sourceKind === "recipe-parameter" ? "实际配方" : "过程轨迹",
      actionabilityLabel: candidate.actionability === "controllable" ? "可直接实验" : "需映射控制量",
      stabilityLabel: Number.isFinite(Number(candidate.stabilitySelectionRate))
        ? `${Math.round(Number(candidate.stabilitySelectionRate) * 100)}%`
        : "样本不足",
      confoundersLabel: (candidate.possibleConfounders || []).join("、") || "未发现明显差异",
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
    <Page title="运行对比" description="从已完成的同类运行中选择基准和对比对象，按阶段对齐后形成待验证的原因假设。">
      {error && <Alert tone="danger">{error}</Alert>}
      <Card title="选择可比较的生产运行" description="先选择需要解释的异常运行；默认与同产品的完整样本组比较，避免从单个偶然样本得出结论。">
        <div className="mb-4 grid gap-3 md:grid-cols-[minmax(0,1fr)_auto] md:items-end">
          <Field label="筛选运行" hint="可按运行号、产品、设备或配方筛选；这是查找，不是录入运行编号。"><Input value={cycleFilter} onChange={event => setCycleFilter(event.target.value)} placeholder="例如：产品系列、设备编号或运行号" /></Field>
          <p className="pb-2 text-sm text-slate-500">显示 {visibleCycles.length} / {cycles.length} 条已完成运行</p>
        </div>
        <form className="grid gap-3 md:grid-cols-[1fr_1fr_1fr_auto]" onSubmit={compare}>
          <Field label="基准运行" hint="通常选择质量异常、规格偏离或需要解释的一次运行。"><Select value={baseline} onChange={event => setBaseline(event.target.value)} required disabled={catalogLoading || !cycles.length}><option value="">选择已完成运行</option>{baseline && !cycles.some(item => item.correlationId === baseline) && <option value={baseline}>{baseline}（来自当前页面链接）</option>}{visibleCycles.map(cycle => <option key={cycle.correlationId} value={cycle.correlationId}>{cycleLabel(cycle)}</option>)}</Select></Field>
          <Field label="对比范围" hint="历史样本组由服务端按产品、时间、质量和数据完整性筛选。"><Select value={comparisonScope} onChange={event => setComparisonScope(event.target.value)} disabled={!baseline}><option value="cohort">同产品历史样本组</option><option value="single">指定一个同类运行</option></Select></Field>
          {comparisonScope === "single" ? <Field label="对比运行" hint={baselineCycle?.productSeries ? `仅显示产品系列“${baselineCycle.productSeries}”的运行。` : baselineCycle ? `该运行未标注产品系列，暂按设备“${baselineCycle.machineId || "未标注"}”筛选。` : "正在读取基准运行。"}><Select value={candidate} onChange={event => setCandidate(event.target.value)} required disabled={!baselineCycle || catalogLoading}><option value="">选择同类运行</option>{comparableCycles.map(cycle => <option key={cycle.correlationId} value={cycle.correlationId}>{cycleLabel(cycle)}</option>)}</Select></Field> : <div className="self-end rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-sm text-slate-600">系统最多选择 24 个同产品历史运行，并保留质量覆盖和数据完整性证据。</div>}
          <Button variant="primary" type="submit" className="self-end" disabled={busy || !baseline || (comparisonScope === "single" && !candidate)}>{busy ? "正在对比…" : "开始运行对比"}</Button>
        </form>
        {catalogLoading && <p className="mt-3 text-sm text-slate-500">正在读取可比较的已完成运行…</p>}
        {!catalogLoading && cycles.length === 0 && <Alert tone="warning" title="暂无可选择的运行">需要至少两条已完成且上下文完整的生产运行，才能开始运行对比。</Alert>}
      </Card>
      {result ? (
        <>
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-5">
            <Metric label="产品系列" value={result.productSeries || "—"} />
            <Metric label="参与对比" value={result.acceptance?.cycleCount ?? comparedCycles.length} hint="条生产运行" />
            <Metric label="数据可用" value={result.acceptance?.availableCycleCount ?? 0} hint={`异常 ${result.acceptance?.degradedCycleCount ?? 0} 个`} />
            <Metric label="运行完整" value={result.acceptance?.completeCycleCount ?? 0} hint="同时具有生产开始与结束事件" />
            <Metric label="分析证据" value={evidenceLevelLabels[result.evidenceLevel] || result.evidenceLevel || "—"} />
          </div>
          <Card title="确定性调查报告" description="以下事实由系统查询和计算生成；本地模型只能组织解释，不能补写数字或把观察性候选说成根因。">
            <div className="mb-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
              <Metric label="调查状态" value={investigation?.status === "ready" ? "可进入验证" : investigation?.status === "exploratory" ? "探索性" : "数据不足"} />
              <Metric label="目标数据" value={investigation?.dataQuality?.targetStatus || "—"} hint={`证据权重 ${formatDecimal(investigation?.dataQuality?.targetEvidenceWeight)}`} />
              <Metric label="基线有效权重" value={formatDecimal(investigation?.comparisonBaseline?.effectiveCycleWeight)} hint={`${investigation?.comparisonBaseline?.comparisonCycleIds?.length || 0} 条对比运行`} />
              <Metric label="匹配条件" value={Object.entries(investigation?.comparisonBaseline?.matchingContext || {}).map(([key, value]) => `${key}=${value}`).join("；") || "未记录"} />
            </div>
            {firstDeviationRows.length ? (
              <div className="mb-4">
                <h4 className="mb-2 text-sm font-semibold text-slate-900">首次阶段偏离</h4>
                <DataTable
                  rows={firstDeviationRows}
                  getRowKey={(row, index) => `${row.signalCode}-${row.phaseCode || "cycle"}-${row.featureCode}-${index}`}
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
            <Alert tone="warning" title="结论边界">{investigation?.conclusionGuardrail || "当前结果只能作为待验证假设。"}</Alert>
          </Card>
          <Card title="运行概况">
            <DataTable
              rows={comparedCycles}
              getRowKey={row => `${row.comparisonRole}-${row.correlationId}`}
              columns={[
                { key: "comparisonRole", label: "角色", render: value => <Badge tone={value === "基准" ? "info" : "neutral"}>{value}</Badge> },
                { key: "correlationId", label: "运行" },
                { key: "machineId", label: "设备" },
                { key: "completedAt", label: "结束时间", render: formatTime },
                { key: "durationMs", label: "时长（秒）", render: value => formatDecimal(Number(value) / 1000) },
                { key: "sampleCount", label: "样本数", render: formatInteger },
                { key: "lifecycleComplete", label: "周期边界", render: value => value ? <Badge tone="success">完整</Badge> : <Badge tone="warning">缺少开始或结束</Badge> },
                { key: "processDataQuality", label: "数据状态", render: value => <StatusBadge value={value?.status} /> },
              ]}
            />
          </Card>
          <Card title="将追因结果带入研发" description="系统只把有证据的关联转为候选假设；因果关系仍需后续受控实验验证。">
            <div className="grid gap-3 md:grid-cols-[1fr_auto]">
              <Field label="研发项目"><Select value={researchProjectId} onChange={event => setResearchProjectId(event.target.value)}><option value="">选择研发项目</option>{researchProjects.filter(item => !["completed", "archived"].includes(item.status)).map(item => <option key={item.projectId} value={item.projectId}>{item.name}</option>)}</Select></Field>
              <Button className="self-end" disabled={!researchProjectId || busy} onClick={createHypotheses}>生成候选假设</Button>
            </div>
          </Card>
          <Card title="质量候选原因" description="同时比较实际配方参数与过程轨迹特征；优先选择能直接映射到可控变量的候选原因。">
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
                    { key: "evidenceLevel", label: "证据", render: value => <Badge tone={value === "stable" ? "success" : value === "exploratory" ? "warning" : "neutral"}>{evidenceLevelLabels[value] || value}</Badge> },
                    { key: "confoundersLabel", label: "可能混杂" },
                  ]}
                />
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
            ) : <EmptyState title="尚无质量候选原因" description="至少需要合格与不合格运行，并且配方或过程特征具有可比较差异。" />}
          </Card>
          <Card title="信号差异" description="按变化幅度列出前 30 项，便于快速定位阶段和参数差异。">
            {signalRows.length ? (
              <DataTable
                rows={signalRows}
                getRowKey={(row, index) => `${row.signalCode}-${row.phaseCode || "cycle"}-${row.featureCode}-${index}`}
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
          </Card>
        </>
      ) : <EmptyState title="尚未执行运行对比" description="从下拉列表选择基准运行和同类对比运行后开始；系统会保留数据可用性和生产开始/结束边界证据。" />}
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
  const contexts = baseline.data?.contextFields || [];
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
            <Card title="正式分析准入" description="只有全部准入条件通过的运行才进入追因、实验分析和优化。">
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
                { key: "field", label: "字段" },
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
