import { MagnifyingGlassIcon } from "@heroicons/react/24/outline";
import { useEffect, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { getJson } from "../api/http";
import { extractProcessSamples, processSignalTraces } from "../charts/chartAdapters";
import { extractRows, useApi } from "../hooks/useApi";
import { Alert, Badge, Button, Card, DataTable, EmptyState, Field, Input, Metric, Pagination, Page, Select, StatusBadge } from "../ui/components";
import { formatTime, formatInteger, formatMeasurementValue, formatDuration, edgeStatus, eventTypeLabel, LoadingCard } from "./shared";
import PlotlyChart from "../components/PlotlyChart";

export function WorkbenchPage() {
  const [state, setState] = useState({
    loading: true,
    error: "",
    cycles: [],
    cycleTotal: 0,
    cycleOverview: {},
    summary: {},
    events: [],
    edges: [],
    contexts: [],
    researchProjects: [],
  });
  useEffect(() => {
    let alive = true;
    Promise.all([
      getJson("/api/v1/cycles?limit=8"),
      getJson("/api/v1/inspection-tasks/summary"),
      getJson("/api/v1/events?limit=20"),
      getJson("/api/edges"),
      getJson("/api/v1/production-contexts"),
      getJson("/api/v1/research-projects?limit=100"),
    ]).then(([cycles, summary, events, edges, contexts, researchProjects]) => {
      if (alive) setState({
        loading: false,
        error: "",
        cycles: extractRows(cycles),
        cycleTotal: cycles.total ?? extractRows(cycles).length,
        cycleOverview: cycles.overview || {},
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

  const activeCycles = state.cycleOverview.activeCount
    ?? state.cycles.filter(item => item.status === "active" || !item.completedAt).length;
  const onlineEdges = state.edges.filter(item => edgeStatus(item) === "online").length;
  const pendingInspections = state.summary.pending ?? state.summary.pendingCount ?? 0;
  const activeContexts = state.contexts.filter(item => !item.validTo).length;
  const activeOptimizationProjects = state.researchProjects.filter(item =>
    item.status === "active" || item.status === "validating").length;
  const dailyActions = [
    {
      title: pendingInspections ? `处理 ${pendingInspections} 个质量待办` : "质量任务已处理",
      description: pendingInspections ? "优先完成检测录入和复核。" : "当前没有待录入或待复核任务。",
      to: "/inspections",
      tone: pendingInspections ? "border-amber-200 bg-amber-50" : "border-emerald-200 bg-emerald-50",
      action: pendingInspections ? "去处理" : "查看记录",
    },
    {
      title: activeOptimizationProjects ? `${activeOptimizationProjects} 个优化项目正在推进` : "从一个真实问题开始优化",
      description: activeOptimizationProjects ? "查看证据缺口、待审核实验或需要独立验证的工艺窗口。" : "将质量偏差或运行异常转为可验证的优化项目。",
      to: "/research-projects",
      tone: activeOptimizationProjects ? "border-blue-200 bg-blue-50" : "border-amber-200 bg-amber-50",
      action: activeOptimizationProjects ? "进入优化" : "创建项目",
    },
    {
      title: `${onlineEdges}/${state.edges.length} 个现场节点在线`,
      description: onlineEdges === state.edges.length && state.edges.length ? "设备采集与数据上行正常。" : "检查离线节点或尚未接入的设备。",
      to: "/edges",
      tone: onlineEdges === state.edges.length && state.edges.length ? "border-emerald-200 bg-emerald-50" : "border-rose-200 bg-rose-50",
      action: "查看状态",
    },
  ];
  return (
    <Page title="决策总览" description="在一个入口理解现场运行、质量结果、数据可信度，以及下一项最有价值的工艺行动。">
      {state.error && <Alert tone="danger">{state.error}</Alert>}
      {state.loading ? <LoadingCard /> : (
        <div className="flex flex-col gap-5">
          <section className="order-3 grid gap-4 rounded-2xl border border-blue-100 bg-gradient-to-br from-blue-50 via-white to-white p-5 shadow-sm lg:grid-cols-[minmax(0,1fr)_20rem]">
            <div>
              <p className="text-sm font-semibold text-blue-700">从现场问题进入可验证决策</p>
              <h2 className="mt-2 max-w-3xl text-xl font-semibold tracking-tight text-slate-950">运行、质量、数据可信度和优化行动使用同一业务上下文。</h2>
              <p className="mt-2 max-w-3xl text-sm leading-6 text-slate-600">异常先成为可解释的证据，再成为需要工程审核的实验与优化行动。</p>
              <div className="mt-4 flex flex-wrap gap-2">
                <Link to="/comparisons" className="inline-flex min-h-9 items-center rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50">开始运行对比</Link>
                <Link to="/research-projects" className="inline-flex min-h-9 items-center rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700">进入优化工作台</Link>
              </div>
            </div>
            <div className="grid content-start gap-3 sm:grid-cols-2 lg:grid-cols-1">
              <div className="rounded-xl border border-white bg-white/80 p-4"><p className="text-xs font-medium text-slate-500">现场数据贯通</p><p className="mt-1 text-lg font-semibold text-slate-950">{onlineEdges}/{state.edges.length} 个节点在线</p></div>
              <div className="rounded-xl border border-white bg-white/80 p-4"><p className="text-xs font-medium text-slate-500">优化闭环</p><p className="mt-1 text-lg font-semibold text-slate-950">{activeOptimizationProjects} 个项目推进中</p></div>
            </div>
          </section>
          <Card className="order-1" title="今天先做这些" description="系统按决策优先级聚合待办，不需要逐个后台模块查找。">
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
          <div className="order-2 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <Metric label="生产运行" value={state.cycleTotal} hint={`${activeCycles} 个正在进行`} />
            <Metric label="待处理质检" value={pendingInspections} hint="来自当前质量任务" />
            <Metric label="采集节点" value={`${onlineEdges}/${state.edges.length}`} hint="在线 / 全部" />
            <Metric label="优化项目" value={activeOptimizationProjects} hint={`${activeContexts} 个有效生产上下文`} />
          </div>
          <div className="order-4 grid gap-5 xl:grid-cols-[1.3fr_.7fr]">
            <Card title="最近生产运行" actions={<Link className="text-sm font-medium text-blue-600 hover:text-blue-700" to="/cycles">查看全部</Link>}>
              <DataTable
                rows={state.cycles}
                keyField="correlationId"
                columns={[
                  { key: "correlationId", label: "运行号" },
                  { key: "machineId", label: "设备" },
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
                    <p className="mt-2 truncate text-sm text-slate-700">{item.event?.subject?.id || item.event?.correlationId || "—"}</p>
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

export function CyclesPage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [filters, setFilters] = useState({
    status: "all",
    machineId: params.get("machineId") || "",
    edgeId: params.get("edgeId") || "",
    externalBatchRef: params.get("externalBatchRef") || "",
    workpieceId: params.get("workpieceId") || "",
    correlationId: params.get("cycleId") || "",
  });
  const [appliedFilters, setAppliedFilters] = useState(filters);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [query, setQuery] = useState(() => makeCycleQuery(filters, 1, 50));
  const { data, loading, error } = useApi(`/api/v1/cycles?${query}`);
  const rows = extractRows(data);
  return (
    <Page title="运行记录" description="按 Edge、设备、批次、工件和运行号追溯跨设备生产过程。">
      <Card title="筛选条件">
        <form className="grid gap-3 md:grid-cols-2 xl:grid-cols-[140px_repeat(5,minmax(0,1fr))_auto]" onSubmit={event => { event.preventDefault(); setAppliedFilters(filters); setPage(1); setQuery(makeCycleQuery(filters, 1, pageSize)); }}>
          <Field label="状态"><Select value={filters.status} onChange={event => setFilters({ ...filters, status: event.target.value })}><option value="all">全部</option><option value="active">进行中</option><option value="completed">已完成</option></Select></Field>
          <Field label="Edge"><Input value={filters.edgeId} onChange={event => setFilters({ ...filters, edgeId: event.target.value })} placeholder="现场节点编号" /></Field>
          <Field label="设备"><Input value={filters.machineId} onChange={event => setFilters({ ...filters, machineId: event.target.value })} placeholder="设备编号" /></Field>
          <Field label="生产批次"><Input value={filters.externalBatchRef} onChange={event => setFilters({ ...filters, externalBatchRef: event.target.value })} placeholder="跨设备批次编号" /></Field>
          <Field label="工件"><Input value={filters.workpieceId} onChange={event => setFilters({ ...filters, workpieceId: event.target.value })} placeholder="跨工序工件编号" /></Field>
          <Field label="运行号"><Input value={filters.correlationId} onChange={event => setFilters({ ...filters, correlationId: event.target.value })} placeholder="精确运行号" /></Field>
          <Button className="self-end" variant="primary" type="submit"><MagnifyingGlassIcon className="size-4" />查询</Button>
        </form>
      </Card>
      {error && <Alert tone="danger">{error}</Alert>}
      {loading && !data ? <LoadingCard /> : (
        <Card title="生产运行" description={`共 ${data?.total ?? rows.length} 条`}>
          <DataTable
            rows={rows}
            keyField="correlationId"
            onRowClick={row => navigate(`/cycles/${encodeURIComponent(row.correlationId)}`)}
            columns={[
              { key: "correlationId", label: "运行号" },
              { key: "machineId", label: "来源", render: (value, row) => <div><p className="font-medium text-slate-800">{value}</p><p className="text-xs text-slate-500">{row.edgeIds?.join("、") || "Edge 未记录"}</p></div> },
              { key: "productCode", label: "产品" },
              { key: "externalBatchRef", label: "批次 / 工件", render: (value, row) => <div><p>{value || "批次未记录"}</p><p className="text-xs text-slate-500">{row.workpieceId || "工件未记录"}</p></div> },
              { key: "recipeId", label: "配方" },
              { key: "qualityStatus", label: "质量", render: value => <StatusBadge value={value} /> },
              { key: "startedAt", label: "开始", render: formatTime },
              { key: "completedAt", label: "结束", render: formatTime },
              {
                key: "correlationId",
                label: "操作",
                render: value => <Link className="font-medium text-blue-600 hover:text-blue-700" to={`/cycles/${encodeURIComponent(value)}`} onClick={event => event.stopPropagation()}>查看详情</Link>,
              },
            ]}
          />
          <Pagination
            page={page}
            pageSize={pageSize}
            total={data?.total ?? rows.length}
            onPageChange={value => { setPage(value); setQuery(makeCycleQuery(appliedFilters, value, pageSize)); }}
            onPageSizeChange={value => { setPageSize(value); setPage(1); setQuery(makeCycleQuery(appliedFilters, 1, value)); }}
          />
        </Card>
      )}
    </Page>
  );
}

export function CycleDetailPage() {
  const { correlationId = "" } = useParams();
  const encodedId = encodeURIComponent(correlationId);
  const cycleResponse = useApi(`/api/v1/cycles?correlationId=${encodedId}&limit=1`);
  const analysisResponse = useApi(`/api/v1/cycles/${encodedId}/analysis`);
  const evidenceResponse = useApi(`/api/v1/cycles/${encodedId}`);
  const eventResponse = useApi(`/api/v1/events?correlationId=${encodedId}&limit=30`);
  const inspectionResponse = useApi(`/api/v1/inspection-records?operationRunId=${encodedId}&limit=50`);
  const cycle = extractRows(cycleResponse.data)[0];
  const analysis = analysisResponse.data;
  const events = extractRows(eventResponse.data);
  const inspections = extractRows(inspectionResponse.data);
  const processSamples = extractProcessSamples(evidenceResponse.data?.events || []);
  const samplesByRun = { [correlationId]: processSamples };
  const chartRun = cycle ? [{
    correlationId,
    machineId: cycle.machineId,
    startedAt: cycle.startedAt,
    isBaseline: true,
  }] : [];
  const stageFeatureRows = (analysis?.signals || []).flatMap(signal =>
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
  const loading = cycleResponse.loading || analysisResponse.loading ||
    evidenceResponse.loading || eventResponse.loading || inspectionResponse.loading;
  const error = cycleResponse.error || analysisResponse.error ||
    evidenceResponse.error || eventResponse.error || inspectionResponse.error;
  const dataQuality = cycle?.processDataQuality;
  const completion = cycle?.expectedSampleCount
    ? `${Math.round(Number(cycle.sampleCompleteness || 0) * 100)}%`
    : `${formatInteger(cycle?.sampleCount)} 条`;

  return (
    <Page
      title={cycle?.correlationId || "生产运行详情"}
      description="在一个页面查看生产身份、过程完整性、质量结果和关键事件。"
      actions={(
        <>
          <Link className="inline-flex min-h-9 items-center rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50" to="/cycles">返回运行记录</Link>
          <Link className="inline-flex min-h-9 items-center rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50" to={`/events?cycleId=${encodedId}`}>查看全部事件</Link>
          <Link className="inline-flex min-h-9 items-center rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700" to={`/comparisons?cycleId=${encodedId}`}>历史对比</Link>
        </>
      )}
    >
      {error && <Alert tone="danger" title="运行详情暂不可用">{error}</Alert>}
      {loading && !cycleResponse.data ? <LoadingCard /> : !cycle ? (
        <EmptyState title="未找到生产运行" description="该运行可能尚未同步，或运行号已经失效。" />
      ) : (
        <>
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <Metric
              label="运行状态"
              value={<StatusBadge value={cycle.status} />}
              hint={cycle.hasStarted ? `${formatTime(cycle.startedAt)} 开始` : "缺少生产开始事件"}
            />
            <Metric
              label="周期边界"
              value={<StatusBadge value={cycle.lifecycleComplete ? "complete" : "incomplete"} />}
              hint={cycle.lifecycleComplete
                ? `结束于 ${formatTime(cycle.completedAt)}`
                : cycle.hasStarted
                  ? "尚未收到生产结束事件"
                  : cycle.hasCompleted
                    ? "已收到结束事件，但缺少开始事件"
                    : "未收到生产开始和结束事件"}
            />
            <Metric label="过程数据" value={completion} hint={`${formatInteger(dataQuality?.sampleCount ?? cycle.sampleCount)} 个有效采样时刻`} />
            <Metric label="质量状态" value={<StatusBadge value={cycle.qualityStatus} />} hint={inspections.length ? `${inspections.length} 条检测记录` : "暂无检测记录"} />
          </div>

          {cycle.dataIssues?.length > 0 && (
            <Alert tone={cycle.dataIssues.some(issue => issue.severity === "error") ? "danger" : "warning"} title="本次运行需要关注">
              <ul className="list-disc space-y-1 pl-5">
                {cycle.dataIssues.map(issue => <li key={`${issue.code}:${issue.message}`}>{issue.message}</li>)}
              </ul>
            </Alert>
          )}

          <div className="grid gap-5 xl:grid-cols-2">
            <Card title="生产身份" description="运行开始时固化的生产上下文">
              <dl className="grid gap-x-6 gap-y-4 sm:grid-cols-2">
                {[
                  ["设备", cycle.machineId],
                  ["Edge", cycle.edgeIds?.join("、")],
                  ["产品系列", cycle.productSeries],
                  ["产品", cycle.productCode],
                  ["配方", cycle.recipeId && `${cycle.recipeId}${cycle.recipeVersion ? ` / v${cycle.recipeVersion}` : ""}`],
                  ["生产批次", cycle.externalBatchRef],
                  ["工件", cycle.workpieceId],
                  ["材料批次", cycle.materialLotRef],
                  ["工装", cycle.toolingId],
                  ["模具", cycle.moldId],
                ].map(([label, value]) => (
                  <div key={label}>
                    <dt className="text-xs font-medium text-slate-500">{label}</dt>
                    <dd className="mt-1 break-words text-sm font-medium text-slate-800">{value || "未记录"}</dd>
                  </div>
                ))}
              </dl>
            </Card>

            <Card title="过程数据健康" description="判断本次运行数据是否适合继续分析">
              <div className="grid gap-4 sm:grid-cols-3">
                <Metric label="健康状态" value={<StatusBadge value={dataQuality?.status || "unknown"} />} />
                <Metric label="采样中位间隔" value={dataQuality?.medianIntervalMs == null ? "—" : formatDuration(dataQuality.medianIntervalMs)} />
                <Metric label="最大断点" value={dataQuality?.maximumGapMs == null ? "—" : formatDuration(dataQuality.maximumGapMs)} />
              </div>
              {dataQuality?.issues?.length > 0 ? (
                <ul className="mt-4 list-disc space-y-1 rounded-xl bg-amber-50 px-5 py-3 text-sm text-amber-800">
                  {dataQuality.issues.map(issue => <li key={issue}>{issue}</li>)}
                </ul>
              ) : (
                <p className="mt-4 rounded-xl bg-emerald-50 px-4 py-3 text-sm text-emerald-800">过程数据连续，未发现影响分析的问题。</p>
              )}
            </Card>
          </div>

          <Card
            title="工艺阶段"
            description={`${cycle.phaseCount ?? cycle.phases?.length ?? 0} 个已识别阶段；阶段号用于过程对齐，不参与运行完整性判定。`}
          >
            <DataTable
              rows={cycle.phases || []}
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

          <Card
            title="实际执行配方"
            description="显示运行开始时从设备或控制系统回读的真实参数；优化建模使用这些值，不使用人工猜测值。"
          >
            {(analysis?.recipeParameters || []).length ? (
              <DataTable
                rows={analysis.recipeParameters}
                keyField="code"
                columns={[
                  { key: "name", label: "参数", render: (value, row) => value || row.code },
                  { key: "code", label: "稳定代码" },
                  { key: "value", label: "实际值", render: formatMeasurementValue },
                  { key: "unit", label: "单位" },
                ]}
              />
            ) : <EmptyState title="尚无实际配方回读" description="没有实际参数的运行不能进入优化模型。" />}
          </Card>

          <Card
            title="全过程曲线"
            description={`${processSamples.length} 个采样时刻；横轴为本次运行开始后的相对时间，阶段来自设备工艺步序。`}
          >
            {(analysis?.signals || []).length && processSamples.length ? (
              <div className="grid gap-4 xl:grid-cols-2">
                {analysis.signals.map(signal => (
                  <section key={signal.code} className="rounded-xl border border-slate-200 bg-white p-3">
                    <div className="mb-2 flex items-baseline justify-between gap-3">
                      <h3 className="text-sm font-semibold text-slate-900">{signal.name || signal.code}</h3>
                      <span className="text-xs text-slate-500">{signal.unit || "无单位"}</span>
                    </div>
                    <PlotlyChart
                      traces={processSignalTraces(chartRun, samplesByRun, signal.code)}
                      layout={{
                        hovermode: "x unified",
                        margin: { l: 55, r: 20, t: 10, b: 45 },
                        xaxis: { title: { text: "运行相对时间（秒）" } },
                        yaxis: { title: { text: signal.unit || "" } },
                        showlegend: false,
                      }}
                      height={260}
                    />
                  </section>
                ))}
              </div>
            ) : <EmptyState title="尚无可绘制曲线" description="需要至少一个有效过程采样和信号定义。" />}
          </Card>

          <Card
            title="阶段特征"
            description="由冻结的过程曲线按工艺阶段计算，是运行对比、追因和优化器轨迹代理的正式输入。"
          >
            {stageFeatureRows.length ? (
              <DataTable
                rows={stageFeatureRows}
                keyField="id"
                columns={[
                  { key: "signalName", label: "信号" },
                  { key: "phaseName", label: "阶段" },
                  { key: "featureCode", label: "特征" },
                  { key: "value", label: "数值", render: formatMeasurementValue },
                  { key: "unit", label: "单位" },
                ]}
              />
            ) : <EmptyState title="尚无阶段特征" description="采集到阶段号并完成分析物化后自动生成。" />}
          </Card>

          <div className="grid gap-5 xl:grid-cols-[1.1fr_.9fr]">
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
                      { key: "characteristicCode", label: "质量特性" },
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

            <Card
              title="最近事件"
              description={`显示最近 ${events.length} 条，完整历史共 ${eventResponse.data?.total ?? events.length} 条`}
              actions={<Link className="text-sm font-medium text-blue-600 hover:text-blue-700" to={`/events?cycleId=${encodedId}`}>查看完整事件</Link>}
            >
              <div className="space-y-3">
                {events.slice(0, 10).map(item => (
                  <div key={item.ingestId} className="flex items-start justify-between gap-4 rounded-xl border border-slate-100 bg-slate-50 p-3">
                    <div className="min-w-0">
                      <Badge tone={item.event?.eventType?.startsWith("alarm.") ? "danger" : item.event?.eventType === "process.sample" ? "neutral" : "info"}>
                        {item.event?.eventType || "event"}
                      </Badge>
                      <p className="mt-2 truncate text-sm text-slate-700">{item.event?.subject?.id || cycle.machineId || "—"}</p>
                    </div>
                    <time className="shrink-0 text-xs text-slate-500">{formatTime(item.event?.occurredAt)}</time>
                  </div>
                ))}
                {!events.length && <EmptyState title="暂无事件" description="该运行尚未接收到生产事件。" />}
              </div>
            </Card>
          </div>
        </>
      )}
    </Page>
  );
}

function makeCycleQuery(filters, page, pageSize) {
  const query = new URLSearchParams({ limit: String(pageSize), offset: String((page - 1) * pageSize), status: filters.status });
  if (filters.machineId.trim()) query.set("machineId", filters.machineId.trim());
  if (filters.edgeId.trim()) query.set("edgeId", filters.edgeId.trim());
  if (filters.externalBatchRef.trim()) query.set("externalBatchRef", filters.externalBatchRef.trim());
  if (filters.workpieceId.trim()) query.set("workpieceId", filters.workpieceId.trim());
  if (filters.correlationId.trim()) query.set("correlationId", filters.correlationId.trim());
  return query.toString();
}
