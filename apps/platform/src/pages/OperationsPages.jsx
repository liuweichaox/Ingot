
// 呈现生产运行、过程曲线和比较入口；页面只编排受权 API 数据，不在客户端推断工艺因果。
import { ArrowRightIcon, BeakerIcon, CircleStackIcon, ClipboardDocumentCheckIcon, MagnifyingGlassIcon, SignalIcon } from "@heroicons/react/24/outline";
import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { getJson } from "../api/http";
import { processCurveTraces } from "../charts/chartAdapters";
import { extractRows, useApi } from "../hooks/useApi";
import { useProcessCurves } from "../hooks/useProcessCurves";
import { Alert, Badge, Button, Card, DataTable, EmptyState, Field, Input, Metric, Pagination, Page, RequestError, Select, StatusBadge, cx } from "../ui/components";
import { formatTime, formatInteger, formatMeasurementValue, formatDuration, edgeStatus, eventTypeLabel, LoadingCard } from "./shared";
import PlotlyChart from "../components/PlotlyChart";

const runIssueSeverityRank = {
  info: 1,
  warning: 2,
  error: 3,
};

export function mergeRunIssues(dataIssues = [], processDataQualityIssues = []) {
  const issuesByMessage = new Map();
  for (const rawIssue of [...dataIssues, ...processDataQualityIssues]) {
    const issue = typeof rawIssue === "string"
      ? { code: rawIssue, message: rawIssue, severity: "warning" }
      : { ...rawIssue, message: rawIssue?.message || rawIssue?.code || "" };
    const messageKey = issue.message.trim();
    if (!messageKey) continue;

    const severity = String(issue.severity || "warning").toLowerCase();
    const candidate = { ...issue, severity };
    const current = issuesByMessage.get(messageKey);
    if (!current || (runIssueSeverityRank[severity] || 0) > (runIssueSeverityRank[current.severity] || 0)) {
      issuesByMessage.set(messageKey, candidate);
    }
  }
  return Array.from(issuesByMessage.values());
}

export function WorkbenchPage({ identity }) {
  const [retryKey, setRetryKey] = useState(0);
  const [state, setState] = useState({
    loading: true,
    error: "",
    executions: [],
    executionTotal: 0,
    executionOverview: {},
    summary: {},
    events: [],
    edges: [],
    contexts: [],
    researchProjects: [],
  });
  useEffect(() => {
    let alive = true;
    Promise.all([
      getJson("/api/v1/process-executions?limit=8"),
      getJson("/api/v1/inspection-tasks/summary"),
      getJson("/api/v1/events?limit=20"),
      getJson("/api/edges"),
      getJson("/api/v1/production-contexts"),
      getJson("/api/v1/research-projects?limit=100"),
    ]).then(([executions, summary, events, edges, contexts, researchProjects]) => {
      if (alive) setState({
        loading: false,
        error: "",
        executions: extractRows(executions),
        executionTotal: executions.total ?? extractRows(executions).length,
        executionOverview: executions.overview || {},
        summary,
        events: extractRows(events),
        edges: extractRows(edges),
        contexts: extractRows(contexts),
        researchProjects: extractRows(researchProjects),
      });
    }).catch(error => {
      if (alive) setState(current => ({ ...current, loading: false, error: error.message }));
    });
    return () => { alive = false; };
  }, [retryKey]);

  const activeProcessExecutions = state.executionOverview.activeCount
    ?? state.executions.filter(item => item.status === "active" || !item.completedAt).length;
  const onlineEdges = state.edges.filter(item => edgeStatus(item) === "online").length;
  const pendingInspections = state.summary.pending ?? state.summary.pendingCount ?? 0;
  const activeContexts = state.contexts.filter(item => !item.validTo).length;
  const activeOptimizationProjects = state.researchProjects.filter(item =>
    item.status === "active" || item.status === "validating").length;
  const roles = identity?.roles || [];
  const isQualityRole = roles.some(role => role === "quality.inspector" || role === "quality.reviewer");
  const isEngineeringRole = roles.some(role => role === "process.engineer" || role === "platform.admin");
  const isAdministrator = roles.includes("platform.admin");
  const hasProductionFoundation = state.edges.length > 0 && state.contexts.length > 0 && state.executionTotal > 0;
  const qualityAction = {
      title: pendingInspections ? `处理 ${pendingInspections} 个质量待办` : "质量任务已处理",
      description: pendingInspections ? "优先完成检测录入和复核。" : "当前没有待录入或待复核任务。",
      to: "/inspections",
      tone: pendingInspections ? "border-l-amber-500" : "border-l-emerald-500",
      action: pendingInspections ? "去处理" : "查看记录",
    };
  const engineeringAction = {
      title: activeOptimizationProjects ? `${activeOptimizationProjects} 个配方优化任务正在推进` : "从真实配方运行开始优化",
      description: activeOptimizationProjects ? "查看新增运行、下一配方建议或可选受控验证。" : "确定目标和安全边界，后续运行会自动进入优化证据。",
      to: "/research-projects",
      tone: activeOptimizationProjects ? "border-l-blue-500" : "border-l-amber-500",
      action: activeOptimizationProjects ? "进入优化" : "创建任务",
    };
  const platformAction = {
      title: `${onlineEdges}/${state.edges.length} 个现场节点在线`,
      description: onlineEdges === state.edges.length && state.edges.length ? "设备采集与数据上行正常。" : "检查离线节点或尚未接入的设备。",
      to: "/edges",
      tone: onlineEdges === state.edges.length && state.edges.length ? "border-l-emerald-500" : "border-l-rose-500",
      action: "查看状态",
    };
  const analysisAction = {
    title: state.executionTotal > 1 ? "从生产运行开始工艺追因" : "积累可比较的生产运行",
    description: state.executionTotal > 1 ? "选择异常或偏离运行，系统自动核对同类条件。" : "至少需要两次运行，才能形成有意义的同类对比。",
    to: state.executionTotal > 1 ? "/analysis" : "/process-executions",
    tone: state.executionTotal > 1 ? "border-l-blue-500" : "border-l-amber-500",
    action: state.executionTotal > 1 ? "开始分析" : "查看运行",
  };
  const dailyActions = isQualityRole && !isEngineeringRole
    ? [qualityAction, analysisAction, platformAction]
    : isEngineeringRole
      ? [analysisAction, engineeringAction, isAdministrator ? platformAction : qualityAction]
      : [analysisAction, qualityAction, platformAction];
  const overviewItems = [
    { label: "生产运行", value: state.executionTotal, hint: `${activeProcessExecutions} 个进行中`, icon: CircleStackIcon, tone: "text-trajectory-100 bg-trajectory-500/12 ring-trajectory-500/20" },
    { label: "待处理质检", value: pendingInspections, hint: "录入与复核", icon: ClipboardDocumentCheckIcon, tone: pendingInspections ? "text-amber-200 bg-amber-500/12 ring-amber-500/20" : "text-emerald-200 bg-emerald-500/12 ring-emerald-500/20" },
    { label: "现场节点", value: `${onlineEdges}/${state.edges.length}`, hint: "在线 / 全部", icon: SignalIcon, tone: onlineEdges === state.edges.length && state.edges.length ? "text-emerald-200 bg-emerald-500/12 ring-emerald-500/20" : "text-rose-200 bg-rose-500/12 ring-rose-500/20" },
    { label: "配方优化", value: activeOptimizationProjects, hint: `${activeContexts} 个有效上下文`, icon: BeakerIcon, tone: "text-evidence-400 bg-evidence-500/12 ring-evidence-500/20" },
  ];
  return (
    <Page
      title="工作台"
      description="把运行、质量、现场状态和配方优化进展汇总为今天需要处理的工程任务。"
      actions={<Link to="/analysis" className="inline-flex min-h-10 items-center gap-2 rounded-lg border border-evidence-500 bg-evidence-500 px-4 py-2 text-sm font-semibold text-coal-950 shadow-sm transition hover:border-evidence-400 hover:bg-evidence-400">开始工艺追因<ArrowRightIcon className="size-4" /></Link>}
    >
      <RequestError error={state.error} onRetry={() => setRetryKey(value => value + 1)} />
      {state.loading ? <LoadingCard /> : (
        <div className="flex flex-col gap-6">
          <section className="product-panel-dark overflow-hidden rounded-2xl" aria-label="运行概览">
            <div className="flex flex-col gap-3 border-b border-white/8 px-5 py-5 sm:flex-row sm:items-center sm:justify-between sm:px-6">
              <div><p className="data-label text-evidence-400">Operational evidence</p><h2 className="mt-1 text-lg font-semibold tracking-[-0.02em] text-white">当前运行与证据状态</h2></div>
              <p className="max-w-lg text-xs leading-5 text-slate-400">先处理影响分析准入的质量与数据问题，再把可比较运行推进到验证。</p>
            </div>
            <div className="grid grid-cols-2 divide-x divide-y divide-white/8 sm:grid-cols-4 sm:divide-y-0">
              {overviewItems.map(({ label, value, hint, icon: Icon, tone }) => (
                <div key={label} className="min-w-0 px-5 py-5 sm:px-6">
                  <div className="flex items-center justify-between gap-3">
                    <p className="data-label text-slate-400">{label}</p>
                    <span className={`grid size-8 place-items-center rounded-lg ring-1 ring-inset ${tone}`}><Icon className="size-4" /></span>
                  </div>
                  <strong className="data-value mt-4 block text-3xl font-semibold text-white">{value}</strong>
                  <span className="mt-1 block truncate text-xs text-slate-400">{hint}</span>
                </div>
              ))}
            </div>
          </section>
          {!hasProductionFoundation && (
            <Card className="border-evidence-400 border-l-4" title="先完成首次接入" description="按依赖顺序建立一条可追溯的数据闭环。">
              <div className="grid divide-y divide-slate-200 sm:grid-cols-3 sm:divide-x sm:divide-y-0">
                <Link to="/configuration" className="px-3 py-2.5 text-sm font-medium text-slate-800 hover:bg-slate-50">1. 定义数据与判断规则</Link>
                <Link to="/edges" className="px-3 py-2.5 text-sm font-medium text-slate-800 hover:bg-slate-50">2. 连接现场节点和设备</Link>
                <Link to="/production/changeover" className="px-3 py-2.5 text-sm font-medium text-slate-800 hover:bg-slate-50">3. 建立当前生产上下文</Link>
              </div>
            </Card>
          )}
          <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_20rem]">
            <Card title="最近生产运行" description="优先查看质量异常、数据不可用和仍在进行的运行。" actions={<Link className="text-sm font-semibold text-trajectory-700 hover:text-trajectory-600" to="/process-executions">查看全部</Link>}>
              <DataTable
                rows={state.executions}
                keyField="executionId"
                columns={[
                  { key: "executionId", label: "运行号" },
                  { key: "equipmentId", label: "设备" },
                  { key: "qualityStatus", label: "质量", render: value => <StatusBadge value={value} /> },
                  { key: "startedAt", label: "开始", render: formatTime },
                ]}
              />
            </Card>
            <div className="grid content-start gap-6">
              <Card title={isQualityRole && !isEngineeringRole ? "质量待办" : "下一步"} description="按当前角色和证据状态排序。">
                <div className="grid gap-2">
                  {dailyActions.map(action => (
                    <Link key={action.to} to={action.to} className={`group block rounded-lg border border-slate-200 border-l-2 bg-slate-50/60 px-3.5 py-3.5 transition hover:border-slate-300 hover:bg-white ${action.tone}`}>
                      <div className="flex items-start justify-between gap-3">
                        <p className="text-sm font-semibold text-slate-900">{action.title}</p>
                        <ArrowRightIcon className="mt-0.5 size-4 shrink-0 text-slate-400 transition group-hover:translate-x-0.5 group-hover:text-trajectory-700" />
                      </div>
                      <p className="mt-1 text-[13px] leading-5 text-slate-500">{action.description}</p>
                      <span className="mt-2 block text-xs font-semibold text-trajectory-700">{action.action}</span>
                    </Link>
                  ))}
                </div>
              </Card>
              <Card title="最新事件" description="来自现场和质量流程的最近记录。">
                <div className="relative divide-y divide-slate-200 before:absolute before:bottom-3 before:left-[5px] before:top-3 before:w-px before:bg-slate-200">
                  {state.events.slice(0, 5).map(item => (
                    <div key={item.ingestId} className="relative flex items-center gap-3 py-3 pl-5 first:pt-0 last:pb-0">
                      <span className="absolute left-0 size-2.5 rounded-full border-2 border-white bg-trajectory-500 ring-1 ring-trajectory-500/25" />
                      <div className="min-w-0 flex-1"><Badge tone={item.event?.eventType?.startsWith("alarm.") ? "danger" : "neutral"}>{eventTypeLabel(item.event?.eventType)}</Badge><p className="mt-1 truncate text-xs text-slate-500">{item.event?.subject?.id || item.event?.executionId || "—"}</p></div>
                      <span className="text-xs text-slate-400 tabular-nums">#{item.ingestId}</span>
                    </div>
                  ))}
                  {!state.events.length && <EmptyState />}
                </div>
              </Card>
            </div>
          </div>
        </div>
      )}
    </Page>
  );
}

export function ProcessExecutionsPage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [filters, setFilters] = useState({
    status: "all",
    equipmentId: params.get("equipmentId") || "",
    edgeId: params.get("edgeId") || "",
    externalBatchRef: params.get("externalBatchRef") || "",
    outputItemId: params.get("outputItemId") || "",
    executionId: params.get("executionId") || "",
  });
  const [appliedFilters, setAppliedFilters] = useState(filters);
  const [advancedFiltersOpen, setAdvancedFiltersOpen] = useState(() => Boolean(params.get("equipmentId") || params.get("edgeId") || params.get("externalBatchRef") || params.get("outputItemId")));
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [query, setQuery] = useState(() => makeProcessExecutionQuery(filters, 1, 50));
  const { data, loading, error, reload } = useApi(`/api/v1/process-executions?${query}`);
  const rows = extractRows(data);
  const showingEmptyState = Boolean(data) && rows.length === 0;
  const edgeResponse = useApi("/api/edges", { enabled: showingEmptyState });
  const ingestionResponse = useApi("/api/v1/ingestion-tasks", { enabled: showingEmptyState });
  const hasAppliedFilters = appliedFilters.status !== "all" || Object.entries(appliedFilters).some(([key, value]) => key !== "status" && value.trim());
  const edgeRows = extractRows(edgeResponse.data);
  const onlineEdges = edgeRows.filter(item => edgeStatus(item) === "online").length;
  const publishedIngestionTasks = extractRows(ingestionResponse.data).filter(item => item.status === "published").length;
  const edgeSummary = edgeResponse.error ? "检查失败" : edgeResponse.loading ? "检查中" : `${onlineEdges}/${edgeRows.length} 在线`;
  const ingestionSummary = ingestionResponse.error ? "检查失败" : ingestionResponse.loading ? "检查中" : publishedIngestionTasks;
  function resetFilters() {
    const cleared = { status: "all", equipmentId: "", edgeId: "", externalBatchRef: "", outputItemId: "", executionId: "" };
    setFilters(cleared);
    setAppliedFilters(cleared);
    setPage(1);
    setQuery(makeProcessExecutionQuery(cleared, 1, pageSize));
  }
  return (
    <Page title="运行记录">
      <Card title="筛选条件">
        <form className="grid gap-3 md:grid-cols-2 xl:grid-cols-[140px_repeat(5,minmax(0,1fr))_auto]" onSubmit={event => { event.preventDefault(); setAppliedFilters(filters); setPage(1); setQuery(makeProcessExecutionQuery(filters, 1, pageSize)); }}>
          <Field label="状态"><Select value={filters.status} onChange={event => setFilters({ ...filters, status: event.target.value })}><option value="all">全部</option><option value="active">进行中</option><option value="completed">已完成</option></Select></Field>
          <div className={cx("gap-3 md:col-span-2 md:grid-cols-2 xl:contents", advancedFiltersOpen ? "grid" : "hidden xl:contents")}>
            <Field label="Edge"><Input value={filters.edgeId} onChange={event => setFilters({ ...filters, edgeId: event.target.value })} placeholder="现场节点编号" /></Field>
            <Field label="设备"><Input value={filters.equipmentId} onChange={event => setFilters({ ...filters, equipmentId: event.target.value })} placeholder="设备编号" /></Field>
            <Field label="生产批次"><Input value={filters.externalBatchRef} onChange={event => setFilters({ ...filters, externalBatchRef: event.target.value })} placeholder="跨设备批次编号" /></Field>
            <Field label="工件"><Input value={filters.outputItemId} onChange={event => setFilters({ ...filters, outputItemId: event.target.value })} placeholder="跨工序工件编号" /></Field>
          </div>
          <Field label="运行号"><Input value={filters.executionId} onChange={event => setFilters({ ...filters, executionId: event.target.value })} placeholder="精确运行号" /></Field>
          <Button className="justify-center xl:hidden" type="button" onClick={() => setAdvancedFiltersOpen(current => !current)}>{advancedFiltersOpen ? "收起筛选" : "更多筛选"}</Button>
          <Button className="self-end" variant="primary" type="submit"><MagnifyingGlassIcon className="size-4" />查询</Button>
        </form>
      </Card>
      <RequestError error={error} onRetry={reload} />
      {loading && !data ? <LoadingCard /> : (
        <Card title="生产运行" description={`共 ${data?.total ?? rows.length} 条`}>
          {rows.length ? <DataTable
            rows={rows}
            keyField="executionId"
            onRowClick={row => navigate(`/process-executions/${encodeURIComponent(row.executionId)}?siteId=${encodeURIComponent(row.siteId)}`)}
            columns={[
              { key: "executionId", label: "运行号" },
              { key: "equipmentId", label: "来源", render: (value, row) => <div><p className="font-medium text-slate-800">{value}</p><p className="text-xs text-slate-500">{row.edgeIds?.join("、") || "Edge 未记录"}</p></div> },
              { key: "productCode", label: "产品" },
              { key: "externalBatchRef", label: "批次 / 工件", render: (value, row) => <div><p>{value || "批次未记录"}</p><p className="text-xs text-slate-500">{row.outputItemId || "工件未记录"}</p></div> },
              { key: "processSpecificationId", label: "工艺规范" },
              { key: "qualityStatus", label: "质量", render: value => <StatusBadge value={value} /> },
              { key: "startedAt", label: "开始", render: formatTime },
              { key: "completedAt", label: "结束", render: formatTime },
              {
                key: "executionId",
                label: "操作",
                render: (value, row) => <Link className="font-medium text-blue-600 hover:text-blue-700" to={`/process-executions/${encodeURIComponent(value)}?siteId=${encodeURIComponent(row.siteId)}`} onClick={event => event.stopPropagation()}>查看详情</Link>,
              },
            ]}
          /> : hasAppliedFilters ? (
            <EmptyState
              title="当前筛选条件下没有运行记录"
              description="数据可能尚未到达，也可能被设备、批次、工件或运行号筛选掉。"
              actions={<Button type="button" onClick={resetFilters}>清除筛选条件</Button>}
            />
          ) : (
            <EmptyState
              title="还没有形成生产运行"
              description="先确认现场节点在线、采集任务已发布，并且设备数据包含明确的运行开始与结束边界。"
              details={(
                <dl className="grid gap-2 rounded-xl border border-slate-200 bg-white p-3 sm:grid-cols-2">
                  <div className="flex items-center justify-between gap-4"><dt>现场节点</dt><dd className={`font-semibold ${edgeResponse.error ? "text-rose-700" : "text-slate-800"}`}>{edgeSummary}</dd></div>
                  <div className="flex items-center justify-between gap-4"><dt>已发布采集任务</dt><dd className={`font-semibold ${ingestionResponse.error ? "text-rose-700" : "text-slate-800"}`}>{ingestionSummary}</dd></div>
                </dl>
              )}
              actions={(
                <>
                  <Link className="inline-flex min-h-10 items-center rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-semibold text-slate-700 hover:bg-slate-50" to="/edges">查看现场节点</Link>
                  <Link className="inline-flex min-h-10 items-center rounded-lg bg-blue-600 px-3 py-2 text-sm font-semibold text-white hover:bg-blue-700" to="/configuration/ingestion-tasks">配置数据源</Link>
                </>
              )}
            />
          )}
          {rows.length > 0 && <Pagination
            page={page}
            pageSize={pageSize}
            total={data?.total ?? rows.length}
            onPageChange={value => { setPage(value); setQuery(makeProcessExecutionQuery(appliedFilters, value, pageSize)); }}
            onPageSizeChange={value => { setPageSize(value); setPage(1); setQuery(makeProcessExecutionQuery(appliedFilters, 1, value)); }}
          />}
        </Card>
      )}
    </Page>
  );
}

export function ProcessExecutionDetailPage() {
  const { executionId = "" } = useParams();
  const [searchParams, setSearchParams] = useSearchParams();
  const [selectedSignalCodes, setSelectedSignalCodes] = useState([]);
  const [signalSearch, setSignalSearch] = useState("");
  const encodedId = encodeURIComponent(executionId);
  const siteId = searchParams.get("siteId") || "";
  const encodedSiteId = encodeURIComponent(siteId);
  const executionResponse = useApi(`/api/v1/process-executions?executionId=${encodedId}&siteId=${encodedSiteId}&limit=1`);
  const analysisResponse = useApi(`/api/v1/process-executions/${encodedId}/analysis?siteId=${encodedSiteId}`);
  const eventResponse = useApi(`/api/v1/events?executionId=${encodedId}&siteId=${encodedSiteId}&limit=30`);
  const inspectionResponse = useApi(`/api/v1/inspection-records?executionId=${encodedId}&limit=50`);
  const execution = extractRows(executionResponse.data)[0];
  const analysis = analysisResponse.data;
  const events = extractRows(eventResponse.data);
  const inspections = extractRows(inspectionResponse.data);
  const tabs = ["overview", "curves", "quality", "events"];
  const requestedTab = searchParams.get("tab") || "overview";
  const activeTab = tabs.includes(requestedTab) ? requestedTab : "overview";
  const availableSignals = useMemo(() => analysis?.signals || [], [analysis?.signals]);
  useEffect(() => {
    if (!availableSignals.length) return;
    setSelectedSignalCodes(current => {
      const valid = current.filter(code => availableSignals.some(signal => signal.code === code));
      return valid.length ? valid : availableSignals.slice(0, 3).map(signal => signal.code);
    });
  }, [availableSignals]);
  const curveResponse = useProcessCurves(executionId, selectedSignalCodes, {
    enabled: activeTab === "curves" && selectedSignalCodes.length > 0,
    maxPoints: 2000,
    siteId,
  });
  const selectedSignals = useMemo(
    () => availableSignals.filter(signal => selectedSignalCodes.includes(signal.code)),
    [availableSignals, selectedSignalCodes],
  );
  const visibleSignals = availableSignals.filter(signal => {
    const needle = signalSearch.trim().toLowerCase();
    return !needle || `${signal.name || ""} ${signal.code} ${signal.unit || ""}`.toLowerCase().includes(needle);
  });
  const curveTraces = useMemo(
    () => processCurveTraces(curveResponse.data?.series, selectedSignals, execution?.startedAt),
    [curveResponse.data?.series, execution?.startedAt, selectedSignals],
  );
  const curveLayout = useMemo(
    () => buildProcessCurveLayout(selectedSignals, execution),
    [execution, selectedSignals],
  );
  const stageFeatureRows = selectedSignals.flatMap(signal =>
    (signal.features || [])
      .filter(feature => feature.phaseCode && ["mean", "max", "slope", "integral"].includes(feature.code))
      .map(feature => ({
        id: `${signal.code}:${feature.phaseCode}:${feature.phaseOrder}:${feature.code}`,
        signalName: signal.name || signal.code,
        phaseName: feature.phaseName || feature.phaseCode,
        featureCode: feature.code,
        value: feature.value,
        unit: signal.unit,
      })));
  const measurementRows = inspections.flatMap(inspection =>
    (inspection.measurements || []).map(measurement => ({
      id: `${inspection.recordId}:${measurement.characteristicCode}`,
      ...measurement,
    })));
  const dataQuality = execution?.processDataQuality;
  const completion = execution?.expectedSampleCount
    ? `${Math.round((Number(execution.sampleCount || 0) / Number(execution.expectedSampleCount)) * 100)}%`
    : `${formatInteger(execution?.sampleCount)} 条`;
  const runIssues = mergeRunIssues(execution?.dataIssues, dataQuality?.issues);
  const needsAttention = runIssues.length > 0 || ["FAIL", "FAILED"].includes(String(execution?.qualityStatus || "").toUpperCase());

  function selectTab(tab) {
    const next = new URLSearchParams(searchParams);
    if (tab === "overview") next.delete("tab");
    else next.set("tab", tab);
    setSearchParams(next, { replace: true });
  }

  function toggleSignal(code) {
    setSelectedSignalCodes(current => current.includes(code)
      ? current.filter(item => item !== code)
      : [...current, code]);
  }

  return (
    <Page
      title={execution?.executionId || "生产运行详情"}
      description={execution
        ? [execution.equipmentId, execution.productCode, execution.externalBatchRef].filter(Boolean).join(" · ")
        : undefined}
      actions={(
        <>
          <Link className="inline-flex min-h-9 items-center rounded-md border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50" to="/process-executions">返回运行记录</Link>
          <Link className="inline-flex min-h-9 items-center rounded-md border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50" to={`/events?executionId=${encodedId}`}>查看全部事件</Link>
          <Link className="inline-flex min-h-9 items-center rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700" to={`/comparisons?executionId=${encodedId}`}>历史对比</Link>
        </>
      )}
    >
      <RequestError
        error={executionResponse.error || analysisResponse.error || eventResponse.error || inspectionResponse.error}
        title="运行详情暂不可用"
        onRetry={() => Promise.all([executionResponse.reload(), analysisResponse.reload(), eventResponse.reload(), inspectionResponse.reload()])}
      />
      {executionResponse.loading && !executionResponse.data ? <LoadingCard /> : !execution ? (
        <EmptyState title="未找到生产运行" description="该运行可能尚未同步，或运行号已经失效。" />
      ) : (
        <>
          <section className={`overflow-hidden rounded-lg border ${needsAttention ? "border-amber-200 bg-amber-50/50" : "border-emerald-200 bg-white"}`}>
            <div className="flex flex-col gap-3 border-b border-slate-100 px-5 py-4 sm:flex-row sm:items-center sm:justify-between">
              <div>
                <p className={`text-sm font-semibold ${needsAttention ? "text-amber-800" : "text-emerald-800"}`}>
                  {needsAttention ? "本次运行需要关注" : "本次运行记录完整，未发现数据问题"}
                </p>
                <p className="mt-1 text-[13px] text-slate-500">{formatTime(execution.startedAt)} 至 {formatTime(execution.completedAt)}</p>
              </div>
              {needsAttention && <Button type="button" variant="secondary" onClick={() => selectTab("curves")}>查看过程曲线</Button>}
            </div>
            <div className="grid grid-cols-2 divide-x divide-y divide-slate-100 sm:grid-cols-4 sm:divide-y-0">
              {[
                ["运行", <StatusBadge value={execution.status} />, execution.lifecycleComplete ? "边界完整" : "边界不完整"],
                ["采样", completion, `${formatInteger(dataQuality?.sampleCount ?? execution.sampleCount)} 个时刻`],
                ["数据", <StatusBadge value={dataQuality?.status || "unknown"} />, dataQuality?.maximumGapMs == null ? "断点未知" : `最大断点 ${formatDuration(dataQuality.maximumGapMs)}`],
                ["质量", <StatusBadge value={execution.qualityStatus} />, inspections.length ? `${inspections.length} 条检测记录` : "暂无检测记录"],
              ].map(([label, value, hint]) => (
                <div key={label} className="min-h-20 px-4 py-3 sm:px-5">
                  <p className="text-[13px] font-medium text-slate-500">{label}</p>
                  <div className="mt-2 text-xl font-semibold text-slate-950">{value}</div>
                  <p className="mt-1 text-[13px] text-slate-500">{hint}</p>
                </div>
              ))}
            </div>
          </section>

          {runIssues.length > 0 && (
            <Alert tone={runIssues.some(issue => issue.severity === "error") ? "danger" : "warning"} title="需要处理的数据问题">
              <ul className="list-disc space-y-1 pl-5">
                {runIssues.map(issue => <li key={`${issue.code}:${issue.message}`}>{issue.message}</li>)}
              </ul>
            </Alert>
          )}

          <div className="sticky top-14 z-10 -mx-1 overflow-x-auto bg-slate-50 px-1 py-2">
            <div className="flex min-w-max gap-1 rounded-lg border border-slate-200 bg-white p-1" role="tablist" aria-label="运行详情分区">
              {[
                ["overview", "运行概览"],
                ["curves", "过程曲线"],
                ["quality", `质量结果${inspections.length ? ` · ${inspections.length}` : ""}`],
                ["events", `事件证据${events.length ? ` · ${events.length}` : ""}`],
              ].map(([key, label]) => (
                <button
                  key={key}
                  type="button"
                  role="tab"
                  aria-selected={activeTab === key}
                  onClick={() => selectTab(key)}
                  className={`rounded-md px-4 py-2 text-sm font-medium transition ${activeTab === key ? "bg-blue-600 text-white" : "text-slate-600 hover:bg-slate-100 hover:text-slate-950"}`}
                >
                  {label}
                </button>
              ))}
            </div>
          </div>

          {activeTab === "overview" && (
            <div className="space-y-5" role="tabpanel">
              <div className="grid gap-5 xl:grid-cols-2">
                <Card title="生产身份">
                  <dl className="grid gap-x-6 gap-y-4 sm:grid-cols-2">
                    {[
                      ["设备", execution.equipmentId],
                      ["Edge", execution.edgeIds?.join("、")],
                      ["产品系列", execution.productFamilyCode],
                      ["产品", execution.productCode],
                      ["工艺规范", execution.processSpecificationId && `${execution.processSpecificationId}${execution.processSpecificationVersion ? ` / v${execution.processSpecificationVersion}` : ""}`],
                      ["生产批次", execution.externalBatchRef],
                      ["工件", execution.outputItemId],
                      ["材料批次", execution.materialLotRef],
                      ["工装总成", execution.toolingAssemblyId],
                    ].map(([label, value]) => (
                      <div key={label}>
                        <dt className="text-[13px] font-medium text-slate-500">{label}</dt>
                        <dd className="mt-1 break-words text-sm font-medium text-slate-800">{value || "未记录"}</dd>
                      </div>
                    ))}
                  </dl>
                </Card>

                <Card title="过程数据健康">
                  <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
                    <Metric label="健康状态" value={<StatusBadge value={dataQuality?.status || "unknown"} />} />
                    <Metric label="采样中位间隔" value={dataQuality?.medianIntervalMs == null ? "—" : formatDuration(dataQuality.medianIntervalMs)} />
                    <Metric label="最大断点" value={dataQuality?.maximumGapMs == null ? "—" : formatDuration(dataQuality.maximumGapMs)} />
                  </div>
                  {!dataQuality?.issues?.length && <p className="mt-4 rounded-md bg-emerald-50 px-4 py-3 text-sm text-emerald-800">过程数据连续，未发现影响分析的问题。</p>}
                </Card>
              </div>

              <Card title={`工艺阶段（${execution.phaseCount ?? execution.phases?.length ?? 0}）`}>
                <DataTable
                  rows={execution.phases || []}
                  keyField="code"
                  columns={[
                    { key: "order", label: "顺序" },
                    { key: "name", label: "阶段" },
                    { key: "sampleCount", label: "有效采样", render: formatInteger },
                    { key: "startedAt", label: "开始", render: formatTime },
                    { key: "endedAt", label: "结束", render: formatTime },
                  ]}
                />
              </Card>

              <Card title="实际执行工艺规范">
                {analysisResponse.loading && !analysis ? <LoadingCard /> : analysisResponse.error ? <Alert tone="danger">{analysisResponse.error}</Alert> : (analysis?.controlParameters || []).length ? (
                  <DataTable
                    rows={analysis.controlParameters}
                    keyField="code"
                    columns={[
                      { key: "name", label: "参数", render: (value, row) => value || row.code },
                      { key: "value", label: "实际值", render: formatMeasurementValue },
                      { key: "unit", label: "单位" },
                      { key: "code", label: "稳定代码", render: value => <span className="text-xs text-slate-400">{value}</span> },
                    ]}
                  />
                ) : <EmptyState title="尚无实际控制参数回读" description="没有实际参数的运行不能进入优化模型。" />}
              </Card>
            </div>
          )}

          {activeTab === "curves" && (
            <div className="space-y-5" role="tabpanel">
              <Card title="过程曲线">
                {analysisResponse.loading && !analysis ? <LoadingCard /> : analysisResponse.error ? <Alert tone="danger">{analysisResponse.error}</Alert> : !availableSignals.length ? (
                  <EmptyState title="尚无可用信号" description="发布运行分析规则并采集有效过程值后即可查看。" />
                ) : (
                  <div className="grid gap-5 xl:grid-cols-[16rem_minmax(0,1fr)]">
                    <aside className="rounded-lg border border-slate-200 bg-slate-50 p-3">
                      <div className="flex items-center justify-between gap-2">
                        <h3 className="text-sm font-semibold text-slate-900">选择信号</h3>
                        <span className="text-xs text-slate-500">已选 {selectedSignalCodes.length}</span>
                      </div>
                      <Input className="mt-3" value={signalSearch} onChange={event => setSignalSearch(event.target.value)} placeholder="搜索名称、代码或单位" />
                      <div className="mt-3 flex gap-2">
                        <button type="button" className="text-sm font-medium text-blue-700" onClick={() => setSelectedSignalCodes(availableSignals.slice(0, 3).map(signal => signal.code))}>关键信号</button>
                        <button type="button" className="text-sm font-medium text-blue-700" onClick={() => setSelectedSignalCodes(availableSignals.map(signal => signal.code))}>全部</button>
                        <button type="button" className="text-sm font-medium text-slate-500" onClick={() => setSelectedSignalCodes([])}>清空</button>
                      </div>
                      <div className="mt-3 max-h-80 space-y-1 overflow-y-auto">
                        {visibleSignals.map(signal => {
                          const selected = selectedSignalCodes.includes(signal.code);
                          return (
                            <label key={signal.code} className={`flex cursor-pointer items-start gap-2 rounded-lg px-2 py-2 ${selected ? "bg-blue-50" : "hover:bg-white"}`}>
                              <input type="checkbox" className="mt-0.5" checked={selected} onChange={() => toggleSignal(signal.code)} />
                              <span className="min-w-0">
                                <span className="block truncate text-sm font-medium text-slate-800">{signal.name || signal.code}</span>
                                <span className="block truncate text-xs text-slate-400">{signal.code}{signal.unit ? ` · ${signal.unit}` : ""}</span>
                              </span>
                            </label>
                          );
                        })}
                      </div>
                    </aside>

                    <div className="min-w-0">
                      {!selectedSignalCodes.length ? <EmptyState title="请选择过程信号" description="所有选中信号会叠加在同一个坐标图中。" /> : curveResponse.loading ? (
                        <div className="grid min-h-96 place-items-center rounded-lg border border-dashed border-slate-200 bg-slate-50">
                          <div className="text-center"><p className="text-sm font-medium text-slate-700">正在准备过程曲线…</p><p className="mt-1 text-[13px] text-slate-500">正在读取所选信号并保留峰值与异常点</p></div>
                        </div>
                      ) : curveResponse.error ? <Alert tone="danger" title="曲线暂时无法加载">{curveResponse.error}</Alert> : curveTraces.length ? (
                        <>
                          <div className="mb-3 flex flex-wrap items-center justify-between gap-2 text-[13px] text-slate-500">
                            <span>原始采样 {formatInteger(curveResponse.data.totalFrameCount)} 帧 · 当前绘制 {formatInteger(curveResponse.data.returnedPointCount)} 点</span>
                            {curveResponse.data.downsampled && <Badge tone="info">已保形降采样</Badge>}
                          </div>
                          <PlotlyChart
                            traces={curveTraces}
                            layout={curveLayout}
                            height={520}
                          />
                          {curveResponse.data.downsampled && <p className="mt-2 rounded-md bg-blue-50 px-3 py-2 text-[13px] text-blue-700">当前概览保留每个时间区间的最小值和最大值，不会隐藏短时尖峰。</p>}
                        </>
                      ) : <EmptyState title="所选信号没有有效采样" description="可以更换信号，或检查数据模型与设备点位映射。" />}
                    </div>
                  </div>
                )}
              </Card>

              <Card title="阶段特征">
                {stageFeatureRows.length ? (
                  <DataTable
                    rows={stageFeatureRows}
                    keyField="id"
                    columns={[
                      { key: "signalName", label: "信号" },
                      { key: "phaseName", label: "阶段" },
                      { key: "featureCode", label: "特征", render: value => featureLabel(value) },
                      { key: "value", label: "数值", render: formatMeasurementValue },
                      { key: "unit", label: "单位" },
                    ]}
                  />
                ) : <EmptyState title="尚无阶段特征" description="采集到阶段号并完成分析物化后自动生成。" />}
              </Card>
            </div>
          )}

          {activeTab === "quality" && (
            <Card
              title="质量记录"
              description={inspections.length ? `已关联 ${inspections.length} 条检测记录` : "尚未产生与本次运行关联的检测记录"}
              actions={<Link className="text-sm font-medium text-blue-600 hover:text-blue-700" to="/inspections">进入质量任务</Link>}
            >
              {inspectionResponse.loading && !inspectionResponse.data ? <LoadingCard /> : inspections.length ? (
                <DataTable
                  rows={inspections}
                  keyField="recordId"
                  columns={[
                    { key: "definitionCode", label: "检测项目" },
                    { key: "outcome", label: "判定", render: value => <StatusBadge value={value} /> },
                    { key: "measuredAt", label: "检测时间", render: formatTime },
                    { key: "attachments", label: "附件", render: value => `${value?.length || 0} 个` },
                  ]}
                />
              ) : <EmptyState title="暂无质量记录" description="完成质量任务后，检测结果会自动归集到本次运行。" />}
              {measurementRows.length > 0 && (
                <div className="mt-4 border-t border-slate-100 pt-4">
                  <h3 className="mb-3 text-sm font-semibold text-slate-900">测量值与规格</h3>
                  <DataTable
                    rows={measurementRows}
                    keyField="id"
                    columns={[
                      { key: "characteristicName", label: "质量特性", render: (value, row) => value || row.characteristicCode },
                      {
                        key: "numericValue",
                        label: "实测值",
                        render: (value, row) => `${formatMeasurementValue(value ?? row.textValue)}${row.unit ? ` ${row.unit}` : ""}`,
                      },
                      {
                        key: "lowerLimit",
                        label: "规格下限",
                        render: (value, row) => value == null ? "—" : `${formatMeasurementValue(value)}${row.unit ? ` ${row.unit}` : ""}`,
                      },
                      {
                        key: "upperLimit",
                        label: "规格上限",
                        render: (value, row) => value == null ? "—" : `${formatMeasurementValue(value)}${row.unit ? ` ${row.unit}` : ""}`,
                      },
                      { key: "outcome", label: "判定", render: value => <StatusBadge value={value} /> },
                    ]}
                  />
                </div>
              )}
            </Card>
          )}

          {activeTab === "events" && (
            <Card
              title="最近事件"
              description={`显示最近 ${events.length} 条，完整历史共 ${eventResponse.data?.total ?? events.length} 条`}
              actions={<Link className="text-sm font-medium text-blue-600 hover:text-blue-700" to={`/events?executionId=${encodedId}`}>查看完整事件</Link>}
            >
              <div className="space-y-3">
                {events.slice(0, 10).map(item => (
                  <div key={item.ingestId} className="flex items-start justify-between gap-4 rounded-xl border border-slate-100 bg-slate-50 p-3">
                    <div className="min-w-0">
                      <Badge tone={item.event?.eventType?.startsWith("alarm.") ? "danger" : "info"}>
                        {eventTypeLabel(item.event?.eventType)}
                      </Badge>
                      <p className="mt-2 truncate text-sm text-slate-700">{item.event?.subject?.id || execution.equipmentId || "—"}</p>
                    </div>
                    <time className="shrink-0 text-xs text-slate-500">{formatTime(item.event?.occurredAt)}</time>
                  </div>
                ))}
                {!events.length && <EmptyState title="暂无事件" description="该运行尚未接收到生产事件。" />}
              </div>
            </Card>
          )}
        </>
      )}
    </Page>
  );
}

function buildProcessCurveLayout(signals, execution) {
  const units = [...new Set(signals.map(signal => signal.unit).filter(Boolean))];
  return {
    hovermode: "x unified",
    margin: { l: 70, r: 28, t: 70, b: 58 },
    showlegend: true,
    legend: { orientation: "h", y: 1.16, x: 0 },
    xaxis: { title: { text: "运行相对时间（秒）" }, rangeslider: { visible: true, thickness: 0.06 } },
    yaxis: {
      title: { text: units.length === 1 ? units[0] : "信号值（单位见图例）" },
      zeroline: false,
      automargin: true,
    },
    shapes: phaseShapes(execution),
    annotations: phaseAnnotations(execution),
  };
}

function phaseShapes(execution) {
  const origin = new Date(execution?.startedAt || 0).getTime();
  const colors = ["rgba(59,130,246,.055)", "rgba(16,185,129,.055)", "rgba(245,158,11,.06)", "rgba(139,92,246,.05)"];
  if (!Number.isFinite(origin)) return [];
  return (execution?.phases || []).flatMap((phase, index) => {
    const start = new Date(phase.startedAt).getTime();
    const end = new Date(phase.endedAt).getTime();
    if (!Number.isFinite(start) || !Number.isFinite(end)) return [];
    return [{
      type: "rect",
      xref: "x",
      yref: "paper",
      x0: (start - origin) / 1000,
      x1: (end - origin) / 1000,
      y0: 0,
      y1: 1,
      fillcolor: colors[index % colors.length],
      line: { width: 0 },
      layer: "below",
    }];
  });
}

function phaseAnnotations(execution) {
  const origin = new Date(execution?.startedAt || 0).getTime();
  if (!Number.isFinite(origin)) return [];
  return (execution?.phases || []).flatMap(phase => {
    const startedAt = new Date(phase.startedAt).getTime();
    const endedAt = new Date(phase.endedAt).getTime();
    if (!Number.isFinite(startedAt) || !Number.isFinite(endedAt)) return [];
    const start = (startedAt - origin) / 1000;
    const end = (endedAt - origin) / 1000;
    return [{ x: (start + end) / 2, y: 1.03, xref: "x", yref: "paper", text: phase.name || phase.code, showarrow: false, font: { size: 11, color: "#64748b" } }];
  });
}

function featureLabel(value) {
  return ({ mean: "平均值", max: "最大值", slope: "变化速率", integral: "积分量" })[value] || value;
}

function makeProcessExecutionQuery(filters, page, pageSize) {
  const query = new URLSearchParams({ limit: String(pageSize), offset: String((page - 1) * pageSize), status: filters.status });
  if (filters.equipmentId.trim()) query.set("equipmentId", filters.equipmentId.trim());
  if (filters.edgeId.trim()) query.set("edgeId", filters.edgeId.trim());
  if (filters.externalBatchRef.trim()) query.set("externalBatchRef", filters.externalBatchRef.trim());
  if (filters.outputItemId.trim()) query.set("outputItemId", filters.outputItemId.trim());
  if (filters.executionId.trim()) query.set("executionId", filters.executionId.trim());
  return query.toString();
}
