
import { MagnifyingGlassIcon } from "@heroicons/react/24/outline";
import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { getJson } from "../api/http";
import { processCurveTraces } from "../charts/chartAdapters";
import { extractRows, useApi } from "../hooks/useApi";
import { useProcessCurves } from "../hooks/useProcessCurves";
import { Alert, Badge, Button, Card, DataTable, EmptyState, Field, Input, Metric, Pagination, Page, Select, StatusBadge } from "../ui/components";
import { formatTime, formatInteger, formatMeasurementValue, formatDuration, edgeStatus, eventTypeLabel, LoadingCard } from "./shared";
import PlotlyChart from "../components/PlotlyChart";

export function WorkbenchPage({ identity }) {
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
  }, []);

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
      tone: pendingInspections ? "border-amber-200 bg-amber-50" : "border-emerald-200 bg-emerald-50",
      action: pendingInspections ? "去处理" : "查看记录",
    };
  const engineeringAction = {
      title: activeOptimizationProjects ? `${activeOptimizationProjects} 个研发项目正在推进` : "从一个真实问题开始研发",
      description: activeOptimizationProjects ? "查看证据缺口、待审核实验或需要独立验证的工艺窗口。" : "将质量偏差或运行异常转为可验证的研发项目。",
      to: "/research-projects",
      tone: activeOptimizationProjects ? "border-blue-200 bg-blue-50" : "border-amber-200 bg-amber-50",
      action: activeOptimizationProjects ? "进入研发" : "创建项目",
    };
  const platformAction = {
      title: `${onlineEdges}/${state.edges.length} 个现场节点在线`,
      description: onlineEdges === state.edges.length && state.edges.length ? "设备采集与数据上行正常。" : "检查离线节点或尚未接入的设备。",
      to: "/edges",
      tone: onlineEdges === state.edges.length && state.edges.length ? "border-emerald-200 bg-emerald-50" : "border-rose-200 bg-rose-50",
      action: "查看状态",
    };
  const analysisAction = {
    title: state.executionTotal > 1 ? "从生产运行开始工艺追因" : "积累可比较的生产运行",
    description: state.executionTotal > 1 ? "选择异常或偏离运行，系统自动核对同类条件。" : "至少需要两次运行，才能形成有意义的同类对比。",
    to: state.executionTotal > 1 ? "/analysis" : "/process-executions",
    tone: state.executionTotal > 1 ? "border-blue-200 bg-blue-50" : "border-amber-200 bg-amber-50",
    action: state.executionTotal > 1 ? "开始分析" : "查看运行",
  };
  const dailyActions = isQualityRole && !isEngineeringRole
    ? [qualityAction, analysisAction, platformAction]
    : isEngineeringRole
      ? [analysisAction, engineeringAction, isAdministrator ? platformAction : qualityAction]
      : [analysisAction, qualityAction, platformAction];
  return (
    <Page title="工作台" description="集中查看今天的待办、生产状态、质量风险与研发进展。">
      {state.error && <Alert tone="danger">{state.error}</Alert>}
      {state.loading ? <LoadingCard /> : (
        <div className="flex flex-col gap-5">
          <section className="order-1 grid gap-4 rounded-2xl border border-blue-100 bg-gradient-to-br from-blue-50 via-white to-white p-5 shadow-sm lg:grid-cols-[minmax(0,1fr)_20rem]">
            <div>
              <p className="text-sm font-semibold text-blue-700">看清这次运行，优化下一次运行。</p>
              <h2 className="mt-2 max-w-3xl text-xl font-semibold tracking-tight text-slate-950">把生产条件、过程轨迹和质量结果连成可追溯证据。</h2>
              <p className="mt-2 max-w-3xl text-sm leading-6 text-slate-600">从可信运行事实出发，比较差异、形成候选原因，并推进可验证实验。</p>
              <div className="mt-4 flex flex-wrap gap-2">
                <Link to="/process-executions" className="inline-flex min-h-9 items-center rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50">查看运行证据</Link>
                <Link to="/analysis" className="inline-flex min-h-9 items-center rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700">开始工艺追因</Link>
              </div>
            </div>
            <div className="grid content-start gap-3 sm:grid-cols-2 lg:grid-cols-1">
              <div className="rounded-xl border border-white bg-white/80 p-4"><p className="text-xs font-medium text-slate-500">现场数据贯通</p><p className="mt-1 text-lg font-semibold text-slate-950">{onlineEdges}/{state.edges.length} 个节点在线</p></div>
              <div className="rounded-xl border border-white bg-white/80 p-4"><p className="text-xs font-medium text-slate-500">研发闭环</p><p className="mt-1 text-lg font-semibold text-slate-950">{activeOptimizationProjects} 个项目推进中</p></div>
            </div>
          </section>
          {!hasProductionFoundation && (
            <Card className="order-2 border-amber-200 bg-amber-50/50" title="先完成首次接入" description="按依赖顺序建立一条可追溯的数据闭环，准备完成后工作台会切换为日常待办。">
              <div className="grid gap-3 sm:grid-cols-3">
                <Link to="/configuration" className="rounded-xl border border-amber-200 bg-white p-4 text-sm font-medium text-amber-900 hover:border-amber-300">1. 定义数据与判断规则</Link>
                <Link to="/edges" className="rounded-xl border border-amber-200 bg-white p-4 text-sm font-medium text-amber-900 hover:border-amber-300">2. 连接现场节点和设备</Link>
                <Link to="/production/changeover" className="rounded-xl border border-amber-200 bg-white p-4 text-sm font-medium text-amber-900 hover:border-amber-300">3. 建立当前生产上下文</Link>
              </div>
            </Card>
          )}
          <Card className="order-3" title={isQualityRole && !isEngineeringRole ? "我的质量工作" : "今天先做这些"} description="根据当前岗位优先展示需要处理的工作。">
            <div className="grid gap-3 lg:grid-cols-3">
              {dailyActions.map(action => (
                <Link key={action.to} to={action.to} className={`group rounded-xl border p-4 transition hover:-translate-y-0.5 hover:shadow-md ${action.tone}`}>
                  <p className="font-semibold text-slate-950">{action.title}</p>
                  <p className="mt-1 min-h-10 text-sm leading-5 text-slate-600">{action.description}</p>
                  <p className="mt-3 text-sm font-medium text-blue-700 group-hover:text-blue-800">{action.action} →</p>
                </Link>
              ))}
            </div>
          </Card>
          <div className="order-4 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <Metric label="生产运行" value={state.executionTotal} hint={`${activeProcessExecutions} 个正在进行`} />
            <Metric label="待处理质检" value={pendingInspections} hint="来自当前质量任务" />
            <Metric label="采集节点" value={`${onlineEdges}/${state.edges.length}`} hint="在线 / 全部" />
            <Metric label="研发项目" value={activeOptimizationProjects} hint={`${activeContexts} 个有效生产上下文`} />
          </div>
          <div className="order-5 grid gap-5 xl:grid-cols-[1.3fr_.7fr]">
            <Card title="最近生产运行" actions={<Link className="text-sm font-medium text-blue-600 hover:text-blue-700" to="/process-executions">查看全部</Link>}>
              <DataTable
                rows={state.executions}
                keyField="executionId"
                columns={[
                  { key: "executionId", label: "运行号" },
                  { key: "equipmentId", label: "设备" },
                  { key: "productCode", label: "产品" },
                  { key: "qualityStatus", label: "质量", render: value => <StatusBadge value={value} /> },
                  { key: "startedAt", label: "开始", render: formatTime },
                ]}
              />
            </Card>
            <Card title="最新事件">
              <div className="space-y-3">
                {state.events.slice(0, 7).map(item => (
                  <div key={item.ingestId} className="rounded-xl border border-slate-100 bg-slate-50 p-3">
                    <div className="flex items-center justify-between gap-3">
                      <Badge tone={item.event?.eventType?.startsWith("alarm.") ? "danger" : "info"}>{eventTypeLabel(item.event?.eventType)}</Badge>
                      <span className="text-xs text-slate-400">#{item.ingestId}</span>
                    </div>
                    <p className="mt-2 truncate text-sm text-slate-700">{item.event?.subject?.id || item.event?.executionId || "—"}</p>
                  </div>
                ))}
                {!state.events.length && <EmptyState />}
              </div>
            </Card>
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
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [query, setQuery] = useState(() => makeProcessExecutionQuery(filters, 1, 50));
  const { data, loading, error } = useApi(`/api/v1/process-executions?${query}`);
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
    <Page title="运行记录" description="按 Edge、设备、批次、工件和运行号追溯跨设备生产过程。">
      <Card title="筛选条件">
        <form className="grid gap-3 md:grid-cols-2 xl:grid-cols-[140px_repeat(5,minmax(0,1fr))_auto]" onSubmit={event => { event.preventDefault(); setAppliedFilters(filters); setPage(1); setQuery(makeProcessExecutionQuery(filters, 1, pageSize)); }}>
          <Field label="状态"><Select value={filters.status} onChange={event => setFilters({ ...filters, status: event.target.value })}><option value="all">全部</option><option value="active">进行中</option><option value="completed">已完成</option></Select></Field>
          <Field label="Edge"><Input value={filters.edgeId} onChange={event => setFilters({ ...filters, edgeId: event.target.value })} placeholder="现场节点编号" /></Field>
          <Field label="设备"><Input value={filters.equipmentId} onChange={event => setFilters({ ...filters, equipmentId: event.target.value })} placeholder="设备编号" /></Field>
          <Field label="生产批次"><Input value={filters.externalBatchRef} onChange={event => setFilters({ ...filters, externalBatchRef: event.target.value })} placeholder="跨设备批次编号" /></Field>
          <Field label="工件"><Input value={filters.outputItemId} onChange={event => setFilters({ ...filters, outputItemId: event.target.value })} placeholder="跨工序工件编号" /></Field>
          <Field label="运行号"><Input value={filters.executionId} onChange={event => setFilters({ ...filters, executionId: event.target.value })} placeholder="精确运行号" /></Field>
          <Button className="self-end" variant="primary" type="submit"><MagnifyingGlassIcon className="size-4" />查询</Button>
        </form>
      </Card>
      {error && <Alert tone="danger">{error}</Alert>}
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
  const runIssues = [...(execution?.dataIssues || []), ...(dataQuality?.issues || []).map(message => ({ code: message, message, severity: "warning" }))];
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
      : current.length < 6 ? [...current, code] : current);
  }

  return (
    <Page
      title={execution?.executionId || "生产运行详情"}
      description={execution
        ? [execution.equipmentId, execution.productCode, execution.externalBatchRef].filter(Boolean).join(" · ")
        : "判断运行结果、检查过程曲线并追溯质量证据。"}
      actions={(
        <>
          <Link className="inline-flex min-h-9 items-center rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50" to="/process-executions">返回运行记录</Link>
          <Link className="inline-flex min-h-9 items-center rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50" to={`/events?executionId=${encodedId}`}>查看全部事件</Link>
          <Link className="inline-flex min-h-9 items-center rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700" to={`/comparisons?executionId=${encodedId}`}>历史对比</Link>
        </>
      )}
    >
      {executionResponse.error && <Alert tone="danger" title="运行详情暂不可用">{executionResponse.error}</Alert>}
      {executionResponse.loading && !executionResponse.data ? <LoadingCard /> : !execution ? (
        <EmptyState title="未找到生产运行" description="该运行可能尚未同步，或运行号已经失效。" />
      ) : (
        <>
          <section className={`overflow-hidden rounded-2xl border shadow-sm ${needsAttention ? "border-amber-200 bg-amber-50/50" : "border-emerald-200 bg-white"}`}>
            <div className="flex flex-col gap-3 border-b border-slate-100 px-5 py-4 sm:flex-row sm:items-center sm:justify-between">
              <div>
                <p className={`text-sm font-semibold ${needsAttention ? "text-amber-800" : "text-emerald-800"}`}>
                  {needsAttention ? "本次运行需要关注" : "本次运行记录完整，未发现数据问题"}
                </p>
                <p className="mt-1 text-xs text-slate-500">{formatTime(execution.startedAt)} 至 {formatTime(execution.completedAt)}</p>
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
                <div key={label} className="min-h-24 px-4 py-4 sm:px-5">
                  <p className="text-xs font-medium text-slate-500">{label}</p>
                  <div className="mt-2 text-xl font-semibold text-slate-950">{value}</div>
                  <p className="mt-1 text-xs text-slate-500">{hint}</p>
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

          <div className="sticky top-16 z-10 -mx-1 overflow-x-auto bg-slate-50/95 px-1 py-2 backdrop-blur">
            <div className="flex min-w-max gap-1 rounded-xl border border-slate-200 bg-white p-1 shadow-sm" role="tablist" aria-label="运行详情分区">
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
                  className={`rounded-lg px-4 py-2 text-sm font-medium transition ${activeTab === key ? "bg-blue-600 text-white shadow-sm" : "text-slate-600 hover:bg-slate-100 hover:text-slate-950"}`}
                >
                  {label}
                </button>
              ))}
            </div>
          </div>

          {activeTab === "overview" && (
            <div className="space-y-5" role="tabpanel">
              <div className="grid gap-5 xl:grid-cols-2">
                <Card title="生产身份" description="运行开始时固化的生产上下文">
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
                        <dt className="text-xs font-medium text-slate-500">{label}</dt>
                        <dd className="mt-1 break-words text-sm font-medium text-slate-800">{value || "未记录"}</dd>
                      </div>
                    ))}
                  </dl>
                </Card>

                <Card title="过程数据健康" description="判断本次运行数据是否适合继续分析">
                  <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
                    <Metric label="健康状态" value={<StatusBadge value={dataQuality?.status || "unknown"} />} />
                    <Metric label="采样中位间隔" value={dataQuality?.medianIntervalMs == null ? "—" : formatDuration(dataQuality.medianIntervalMs)} />
                    <Metric label="最大断点" value={dataQuality?.maximumGapMs == null ? "—" : formatDuration(dataQuality.maximumGapMs)} />
                  </div>
                  {!dataQuality?.issues?.length && <p className="mt-4 rounded-xl bg-emerald-50 px-4 py-3 text-sm text-emerald-800">过程数据连续，未发现影响分析的问题。</p>}
                </Card>
              </div>

              <Card title="工艺阶段" description={`${execution.phaseCount ?? execution.phases?.length ?? 0} 个已识别阶段；用于曲线对齐和特征计算。`}>
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

              <Card title="实际执行工艺规范" description="本次运行实际使用的参数，是历史对比和优化分析的正式条件。">
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
              <Card title="过程曲线工作台" description="选择关键信号，在统一时间轴中查看阶段、尖峰和采样断点。">
                {analysisResponse.loading && !analysis ? <LoadingCard /> : analysisResponse.error ? <Alert tone="danger">{analysisResponse.error}</Alert> : !availableSignals.length ? (
                  <EmptyState title="尚无可用信号" description="发布运行分析方案并采集有效过程值后即可查看。" />
                ) : (
                  <div className="grid gap-5 xl:grid-cols-[16rem_minmax(0,1fr)]">
                    <aside className="rounded-xl border border-slate-200 bg-slate-50 p-3">
                      <div className="flex items-center justify-between gap-2">
                        <h3 className="text-sm font-semibold text-slate-900">选择信号</h3>
                        <span className="text-xs text-slate-500">{selectedSignalCodes.length}/6</span>
                      </div>
                      <Input className="mt-3" value={signalSearch} onChange={event => setSignalSearch(event.target.value)} placeholder="搜索名称、代码或单位" />
                      <div className="mt-3 flex gap-2">
                        <button type="button" className="text-xs font-medium text-blue-700" onClick={() => setSelectedSignalCodes(availableSignals.slice(0, 3).map(signal => signal.code))}>关键信号</button>
                        <button type="button" className="text-xs font-medium text-slate-500" onClick={() => setSelectedSignalCodes([])}>清空</button>
                      </div>
                      <div className="mt-3 max-h-80 space-y-1 overflow-y-auto">
                        {visibleSignals.map(signal => {
                          const selected = selectedSignalCodes.includes(signal.code);
                          return (
                            <label key={signal.code} className={`flex cursor-pointer items-start gap-2 rounded-lg px-2 py-2 ${selected ? "bg-blue-50" : "hover:bg-white"}`}>
                              <input type="checkbox" className="mt-0.5" checked={selected} disabled={!selected && selectedSignalCodes.length >= 6} onChange={() => toggleSignal(signal.code)} />
                              <span className="min-w-0">
                                <span className="block truncate text-sm font-medium text-slate-800">{signal.name || signal.code}</span>
                                <span className="block truncate text-xs text-slate-400">{signal.code}{signal.unit ? ` · ${signal.unit}` : ""}</span>
                              </span>
                            </label>
                          );
                        })}
                      </div>
                      {selectedSignalCodes.length >= 6 && <p className="mt-3 text-xs leading-5 text-amber-700">为保持曲线清晰，一次最多显示6个信号。</p>}
                    </aside>

                    <div className="min-w-0">
                      {!selectedSignalCodes.length ? <EmptyState title="请选择过程信号" description="建议先选择最关键的2到3个信号。" /> : curveResponse.loading ? (
                        <div className="grid min-h-96 place-items-center rounded-xl border border-dashed border-slate-200 bg-slate-50">
                          <div className="text-center"><p className="text-sm font-medium text-slate-700">正在准备过程曲线…</p><p className="mt-1 text-xs text-slate-500">正在读取所选信号并保留峰值与异常点</p></div>
                        </div>
                      ) : curveResponse.error ? <Alert tone="danger" title="曲线暂时无法加载">{curveResponse.error}</Alert> : curveTraces.length ? (
                        <>
                          <div className="mb-3 flex flex-wrap items-center justify-between gap-2 text-xs text-slate-500">
                            <span>原始采样 {formatInteger(curveResponse.data.totalFrameCount)} 帧 · 当前绘制 {formatInteger(curveResponse.data.returnedPointCount)} 点</span>
                            {curveResponse.data.downsampled && <Badge tone="info">已保形降采样</Badge>}
                          </div>
                          <PlotlyChart
                            traces={curveTraces}
                            layout={curveLayout}
                            height={Math.min(920, Math.max(420, selectedSignals.length * 190 + 110))}
                          />
                          {curveResponse.data.downsampled && <p className="mt-2 rounded-lg bg-blue-50 px-3 py-2 text-xs text-blue-700">当前概览保留每个时间区间的最小值和最大值，不会隐藏短时尖峰。</p>}
                        </>
                      ) : <EmptyState title="所选信号没有有效采样" description="可以更换信号，或检查数据模型与设备点位映射。" />}
                    </div>
                  </div>
                )}
              </Card>

              <Card title="阶段特征" description="由冻结曲线按工艺阶段计算，供运行对比、追因和优化使用。">
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
  const count = Math.max(1, signals.length);
  const gap = count > 1 ? 0.06 : 0;
  const domainHeight = (1 - gap * (count - 1)) / count;
  const layout = {
    hovermode: "x unified",
    margin: { l: 70, r: 28, t: 70, b: 58 },
    showlegend: true,
    legend: { orientation: "h", y: 1.16, x: 0 },
    xaxis: { title: { text: "运行相对时间（秒）" }, rangeslider: { visible: count <= 3, thickness: 0.06 } },
    shapes: phaseShapes(execution),
    annotations: phaseAnnotations(execution),
  };
  signals.forEach((signal, index) => {
    const top = 1 - index * (domainHeight + gap);
    const key = index ? `yaxis${index + 1}` : "yaxis";
    layout[key] = {
      domain: [Math.max(0, top - domainHeight), top],
      title: { text: signal.unit || signal.name || signal.code },
      zeroline: false,
      automargin: true,
    };
  });
  return layout;
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
