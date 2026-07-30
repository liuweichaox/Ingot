import { Tab, TabGroup, TabList, TabPanel, TabPanels } from "@headlessui/react";
import {
  ArrowPathIcon,
  MagnifyingGlassIcon,
  PaperAirplaneIcon,
} from "@heroicons/react/24/outline";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { deleteJson, getJson, postForm, postJson, putJson, streamSse } from "../api/http";
import {
  extractProcessSamples,
  processSignalTraces,
  qualityOutcomeTraces,
} from "../charts/chartAdapters";
import PlotlyChart from "../components/PlotlyChart";
import {
  createRegistryBusinessForm,
  RegistryBusinessEditor,
  registryBusinessPayload,
  registryBusinessValidation,
} from "../components/RegistryBusinessEditor";
import { extractRows, useApi } from "../hooks/useApi";
import {
  Alert,
  Badge,
  Button,
  Card,
  DataTable,
  Drawer,
  EmptyState,
  Field,
  Input,
  Metric,
  Pagination,
  Page,
  Select,
  StatusBadge,
  Textarea,
  WorkflowGuide,
  notify,
} from "../ui/components";

export { ResearchProjectsPage } from "./ResearchProjectsPage";
export { ResearchAssetsPage } from "./ResearchAssetsPage";

const formatTime = value => value ? new Date(value).toLocaleString("zh-CN") : "—";
const formatInteger = value => Number.isFinite(Number(value)) ? Number(value).toLocaleString("zh-CN") : "—";
const formatMeasurementValue = value => {
  if (value == null || value === "") return "—";
  const numeric = Number(value);
  return Number.isFinite(numeric)
    ? numeric.toLocaleString("zh-CN", { maximumFractionDigits: 6 })
    : String(value);
};
const formatBytes = value => {
  const bytes = Number(value);
  if (!Number.isFinite(bytes)) return "—";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 ** 2) return `${(bytes / 1024).toFixed(1)} KiB`;
  if (bytes < 1024 ** 3) return `${(bytes / 1024 ** 2).toFixed(1)} MiB`;
  return `${(bytes / 1024 ** 3).toFixed(1)} GiB`;
};
const metricSamples = (payload, name) => payload?.metrics?.[name]?.data || [];
const metricTotal = (payload, name) => metricSamples(payload, name).reduce((sum, sample) => sum + Number(sample.value || 0), 0);
const formatDuration = value => {
  const milliseconds = Number(value);
  if (!Number.isFinite(milliseconds)) return "—";
  if (milliseconds < 1000) return `${Math.round(milliseconds)} 毫秒`;
  const totalSeconds = Math.round(milliseconds / 1000);
  if (totalSeconds < 60) return `${totalSeconds} 秒`;
  const totalMinutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  if (totalMinutes < 60) return seconds ? `${totalMinutes} 分 ${seconds} 秒` : `${totalMinutes} 分钟`;
  const totalHours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  if (totalHours < 24) return minutes ? `${totalHours} 小时 ${minutes} 分` : `${totalHours} 小时`;
  const days = Math.floor(totalHours / 24);
  const hours = totalHours % 24;
  return hours ? `${days} 天 ${hours} 小时` : `${days} 天`;
};
const edgeStatus = edge => {
  if (!edge?.lastSeen) return "unknown";
  if (edge.lastError) return "degraded";
  return Date.now() - new Date(edge.lastSeen).getTime() <= 30000 ? "online" : "offline";
};

const inspectionInputTypeLabels = {
  numeric: "数值",
  text: "文本",
  select: "选项",
  boolean: "是/否",
};

const acquisitionProtocolLabels = {
  "http-polling": "HTTP 轮询",
  mqtt: "MQTT",
  "opc-ua": "OPC UA",
  "modbus-tcp": "Modbus TCP",
  "melsec-a1e": "三菱 MELSEC 1E",
};

const objectTypeLabels = {
  equipment: "生产设备",
  "optical-molding-machine": "光学模压设备",
  machine: "设备",
  workpiece: "工件",
};

const objectTypeLabel = value => objectTypeLabels[value] || value || "未分类";

const eventTypeLabels = {
  "process.started": "生产开始",
  "process.completed": "生产完成",
  "process.sample": "过程采样",
  "cycle.started": "周期开始",
  "cycle.completed": "周期完成",
  "recipe/step_changed": "工艺步骤切换",
  "quality.inspection.completed": "质检完成",
  "alarm.raised": "设备报警",
  "alarm.cleared": "报警解除",
};

const eventTypeLabel = value => eventTypeLabels[value] || value?.split(".").join(" / ") || "生产事件";

function emptyInspectionCharacteristic() {
  return {
    code: "",
    name: "",
    inputType: "numeric",
    unit: "",
    lowerLimit: "",
    upperLimit: "",
    allowedValuesText: "",
    required: true,
  };
}

function inspectionDefinitionForm(value = {}, version) {
  const characteristics = Array.isArray(value.characteristics) && value.characteristics.length > 0
    ? value.characteristics.map(characteristic => ({
      code: characteristic.code || "",
      name: characteristic.name || "",
      inputType: characteristic.inputType || "numeric",
      unit: characteristic.unit || "",
      lowerLimit: characteristic.lowerLimit ?? "",
      upperLimit: characteristic.upperLimit ?? "",
      allowedValuesText: (characteristic.allowedValues || []).join("\n"),
      required: characteristic.required !== false,
    }))
    : [emptyInspectionCharacteristic()];

  return {
    code: value.code || "",
    version: version ?? value.version ?? 1,
    name: value.name || "",
    description: value.description || "",
    characteristics,
  };
}

function inspectionDefinitionPayload(form) {
  return {
    code: form.code.trim(),
    version: Number(form.version),
    name: form.name.trim(),
    description: form.description.trim() || null,
    characteristics: form.characteristics.map(characteristic => ({
      code: characteristic.code.trim(),
      name: characteristic.name.trim(),
      inputType: characteristic.inputType,
      unit: characteristic.inputType === "numeric" ? characteristic.unit.trim() || null : null,
      lowerLimit: characteristic.inputType === "numeric" && characteristic.lowerLimit !== ""
        ? Number(characteristic.lowerLimit)
        : null,
      upperLimit: characteristic.inputType === "numeric" && characteristic.upperLimit !== ""
        ? Number(characteristic.upperLimit)
        : null,
      allowedValues: characteristic.inputType === "select"
        ? characteristic.allowedValuesText.split(/\r?\n|,/).map(value => value.trim()).filter(Boolean)
        : [],
      required: characteristic.required,
    })),
  };
}

function inspectionDefinitionValidation(form) {
  const codePattern = /^[a-z][a-z0-9_-]*(?:\.[a-z0-9][a-z0-9_-]*)*$/;
  if (!codePattern.test(form.code.trim())) return "定义代码需使用小写点分格式，例如 hardness.final。";
  if (!Number.isInteger(Number(form.version)) || Number(form.version) < 1) return "版本必须是大于 0 的整数。";
  if (!form.name.trim()) return "请填写定义名称。";
  if (form.characteristics.length === 0) return "请至少添加一个检测特性。";

  const codes = new Set();
  for (const [index, characteristic] of form.characteristics.entries()) {
    const position = `第 ${index + 1} 个检测特性`;
    const code = characteristic.code.trim();
    if (!codePattern.test(code)) return `${position}的代码需使用小写点分格式。`;
    if (codes.has(code)) return `检测特性代码“${code}”重复。`;
    codes.add(code);
    if (!characteristic.name.trim()) return `${position}缺少名称。`;
    if (characteristic.inputType === "select" &&
        !characteristic.allowedValuesText.split(/\r?\n|,/).some(value => value.trim())) {
      return `${position}是选项类型，请至少填写一个可选值。`;
    }
    if (characteristic.inputType === "numeric") {
      const lower = characteristic.lowerLimit === "" ? null : Number(characteristic.lowerLimit);
      const upper = characteristic.upperLimit === "" ? null : Number(characteristic.upperLimit);
      if ((lower !== null && !Number.isFinite(lower)) || (upper !== null && !Number.isFinite(upper))) {
        return `${position}的上下限必须是有效数字。`;
      }
      if (lower !== null && upper !== null && lower > upper) return `${position}的下限不能大于上限。`;
    }
  }
  return "";
}

function inspectionInputTypes(characteristics) {
  if (!Array.isArray(characteristics) || characteristics.length === 0) return "—";
  return [...new Set(characteristics.map(item => inspectionInputTypeLabels[item.inputType] || item.inputType))].join("、");
}

function LoadingCard() {
  return (
    <Card>
      <div className="grid min-h-44 place-items-center text-sm text-slate-500">
        <span className="inline-flex items-center gap-2"><ArrowPathIcon className="size-5 animate-spin" />正在读取数据</span>
      </div>
    </Card>
  );
}

function ResourcePage({ title, description, endpoint, columns, keyField, getRowKey, emptyDescription, interval = 0, actions }) {
  const { data, loading, error } = useApi(endpoint, { interval });
  const rows = extractRows(data);
  return (
    <Page title={title} description={description} actions={actions}>
      {error && <Alert tone="danger" title="数据暂不可用">{error}</Alert>}
      {loading && !data ? <LoadingCard /> : (
        <Card title={`${title}列表`} description={`共 ${data?.total ?? rows.length} 条记录`}>
          {rows.length
            ? <DataTable columns={columns} rows={rows} keyField={keyField} getRowKey={getRowKey} />
            : <EmptyState description={emptyDescription} />}
        </Card>
      )}
    </Page>
  );
}

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
    <Page title="工业决策工作台" description="在一个入口理解现场运行、质量结果、数据可信度，以及下一项最有价值的工艺行动。">
      {state.error && <Alert tone="danger">{state.error}</Alert>}
      {state.loading ? <LoadingCard /> : (
        <div className="flex flex-col gap-5">
          <section className="order-3 grid gap-4 rounded-2xl border border-blue-100 bg-gradient-to-br from-blue-50 via-white to-white p-5 shadow-sm lg:grid-cols-[minmax(0,1fr)_20rem]">
            <div>
              <p className="text-sm font-semibold text-blue-700">从现场问题进入可验证决策</p>
              <h2 className="mt-2 max-w-3xl text-xl font-semibold tracking-tight text-slate-950">运行、质量、数据可信度和优化行动使用同一业务上下文。</h2>
              <p className="mt-2 max-w-3xl text-sm leading-6 text-slate-600">异常先成为可解释的证据，再成为需要工程审核的实验与优化行动。</p>
              <div className="mt-4 flex flex-wrap gap-2">
                <Link to="/comparisons" className="inline-flex min-h-9 items-center rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50">开始周期对比</Link>
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
                  { key: "correlationId", label: "周期号" },
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
    correlationId: params.get("cycleId") || "",
  });
  const [appliedFilters, setAppliedFilters] = useState(filters);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [query, setQuery] = useState(() => makeCycleQuery(filters, 1, 50));
  const { data, loading, error } = useApi(`/api/v1/cycles?${query}`);
  const rows = extractRows(data);
  return (
    <Page title="运行记录" description="按设备、状态和周期号追溯完整生产运行。">
      <Card title="筛选条件">
        <form className="grid gap-3 md:grid-cols-[160px_1fr_1fr_auto]" onSubmit={event => { event.preventDefault(); setAppliedFilters(filters); setPage(1); setQuery(makeCycleQuery(filters, 1, pageSize)); }}>
          <Field label="状态"><Select value={filters.status} onChange={event => setFilters({ ...filters, status: event.target.value })}><option value="all">全部</option><option value="active">进行中</option><option value="completed">已完成</option></Select></Field>
          <Field label="设备"><Input value={filters.machineId} onChange={event => setFilters({ ...filters, machineId: event.target.value })} placeholder="设备编号" /></Field>
          <Field label="周期号"><Input value={filters.correlationId} onChange={event => setFilters({ ...filters, correlationId: event.target.value })} placeholder="精确周期号" /></Field>
          <Button className="self-end" variant="primary" type="submit"><MagnifyingGlassIcon className="size-4" />查询</Button>
        </form>
      </Card>
      {error && <Alert tone="danger">{error}</Alert>}
      {loading && !data ? <LoadingCard /> : (
        <Card title="生产周期" description={`共 ${data?.total ?? rows.length} 条`}>
          <DataTable
            rows={rows}
            keyField="correlationId"
            onRowClick={row => navigate(`/cycles/${encodeURIComponent(row.correlationId)}`)}
            columns={[
              { key: "correlationId", label: "周期号" },
              { key: "machineId", label: "设备" },
              { key: "productCode", label: "产品" },
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
      title={cycle?.correlationId || "生产周期详情"}
      description="在一个页面查看生产身份、过程完整性、质量结果和关键事件。"
      actions={(
        <>
          <Link className="inline-flex min-h-9 items-center rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50" to="/cycles">返回运行记录</Link>
          <Link className="inline-flex min-h-9 items-center rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50" to={`/events?cycleId=${encodedId}`}>查看全部事件</Link>
          <Link className="inline-flex min-h-9 items-center rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700" to={`/comparisons?cycleId=${encodedId}`}>历史对比</Link>
        </>
      )}
    >
      {error && <Alert tone="danger" title="周期详情暂不可用">{error}</Alert>}
      {loading && !cycleResponse.data ? <LoadingCard /> : !cycle ? (
        <EmptyState title="未找到生产周期" description="该周期可能尚未同步，或周期号已经失效。" />
      ) : (
        <>
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <Metric label="运行状态" value={<StatusBadge value={cycle.status} />} hint={`${formatTime(cycle.startedAt)} 开始`} />
            <Metric label="运行时长" value={formatDuration(cycle.durationMs)} hint={cycle.completedAt ? `结束于 ${formatTime(cycle.completedAt)}` : "仍在运行"} />
            <Metric label="过程数据" value={completion} hint={`${formatInteger(dataQuality?.sampleCount ?? cycle.sampleCount)} 个有效采样时刻`} />
            <Metric label="质量状态" value={<StatusBadge value={cycle.qualityStatus} />} hint={inspections.length ? `${inspections.length} 条检测记录` : "暂无检测记录"} />
          </div>

          {cycle.dataIssues?.length > 0 && (
            <Alert tone={cycle.dataIssues.some(issue => issue.severity === "error") ? "danger" : "warning"} title="本周期需要关注">
              <ul className="list-disc space-y-1 pl-5">
                {cycle.dataIssues.map(issue => <li key={`${issue.code}:${issue.message}`}>{issue.message}</li>)}
              </ul>
            </Alert>
          )}

          <div className="grid gap-5 xl:grid-cols-2">
            <Card title="生产身份" description="周期开始时固化的生产上下文">
              <dl className="grid gap-x-6 gap-y-4 sm:grid-cols-2">
                {[
                  ["设备", cycle.machineId],
                  ["产品系列", cycle.productSeries],
                  ["产品", cycle.productCode],
                  ["配方", cycle.recipeId && `${cycle.recipeId}${cycle.recipeVersion ? ` / v${cycle.recipeVersion}` : ""}`],
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

            <Card title="过程数据健康" description="判断本周期数据是否适合继续分析">
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

          <Card title="工艺阶段" description={`${cycle.phaseCount ?? cycle.phases?.length ?? 0} 个阶段，${cycle.phaseComplete ? "阶段记录完整" : "存在缺失阶段"}`}>
            <DataTable
              rows={cycle.phases || []}
              keyField="code"
              columns={[
                { key: "order", label: "顺序" },
                { key: "name", label: "阶段" },
                { key: "isComplete", label: "状态", render: value => <StatusBadge value={value ? "complete" : "pending"} /> },
                { key: "sampleCount", label: "有效采样", render: formatInteger },
                { key: "startedAt", label: "开始", render: formatTime },
                { key: "endedAt", label: "结束", render: formatTime },
              ]}
            />
          </Card>

          <Card
            title="实际执行配方"
            description="显示周期开始时从设备或控制系统回读的真实参数；优化建模使用这些值，不使用人工猜测值。"
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
            description="由冻结的过程曲线按工艺阶段计算，是周期对比、追因和优化器轨迹代理的正式输入。"
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
            ) : <EmptyState title="尚无阶段特征" description="阶段完整并完成分析物化后自动生成。" />}
          </Card>

          <div className="grid gap-5 xl:grid-cols-[1.1fr_.9fr]">
            <Card
              title="质量记录"
              description={inspections.length ? `已关联 ${inspections.length} 条检测记录` : "尚未产生与本周期关联的检测记录"}
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
              ) : <EmptyState title="暂无质量记录" description="完成质量任务后，检测结果会自动归集到本周期。" />}
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
                {!events.length && <EmptyState title="暂无事件" description="该周期尚未接收到生产事件。" />}
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
  if (filters.correlationId.trim()) query.set("correlationId", filters.correlationId.trim());
  return query.toString();
}

export function EventsPage() {
  const [urlParams] = useSearchParams();
  const [filters, setFilters] = useState({
    type: "",
    edgeId: "",
    subjectId: urlParams.get("subjectId") || "",
    correlationId: urlParams.get("cycleId") || "",
  });
  const [appliedFilters, setAppliedFilters] = useState(filters);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [live, setLive] = useState(false);
  const [streamError, setStreamError] = useState("");
  const [query, setQuery] = useState(() => makeEventQuery(filters, 1, 50));
  const { data, setData, loading, error } = useApi(`/api/v1/events?${query}`);
  const rows = extractRows(data);
  useEffect(() => {
    if (!live) return undefined;
    const newest = rows.reduce((maximum, item) => Math.max(maximum, Number(item.ingestId || 0)), 0);
    const streamParams = new URLSearchParams();
    Object.entries(appliedFilters).forEach(([key, value]) => value.trim() && streamParams.set(key, value.trim()));
    if (newest) streamParams.set("afterIngestId", String(newest));
    const source = new EventSource(`/api/v1/events/stream?${streamParams}`);
    source.onmessage = message => {
      const item = JSON.parse(message.data);
      setData(current => {
        const currentRows = extractRows(current);
        if (currentRows.some(value => value.ingestId === item.ingestId)) return current;
        return { ...(current || {}), data: [item, ...currentRows].slice(0, pageSize), total: Number(current?.total || currentRows.length) + 1 };
      });
    };
    source.onopen = () => setStreamError("");
    source.onerror = () => setStreamError("实时事件连接暂时中断，浏览器正在自动重连。");
    return () => source.close();
  }, [appliedFilters, live, pageSize, setData]);
  return (
    <Page
      title="生产事件"
      description="检索标准事件并回到所属周期。"
      actions={<label className="flex items-center gap-2 text-sm text-slate-600"><input type="checkbox" checked={live} onChange={event => { setPage(1); setLive(event.target.checked); }} />实时追踪</label>}
    >
      <Card title="事件筛选">
        <form className="grid gap-3 md:grid-cols-2 xl:grid-cols-[1fr_1fr_1fr_1fr_auto]" onSubmit={event => { event.preventDefault(); setLive(false); setAppliedFilters(filters); setPage(1); setQuery(makeEventQuery(filters, 1, pageSize)); }}>
          <Field label="事件类型"><Input value={filters.type} onChange={event => setFilters({ ...filters, type: event.target.value })} placeholder="process.sample" /></Field>
          <Field label="采集节点"><Input value={filters.edgeId} onChange={event => setFilters({ ...filters, edgeId: event.target.value })} /></Field>
          <Field label="工业对象"><Input value={filters.subjectId} onChange={event => setFilters({ ...filters, subjectId: event.target.value })} placeholder="设备或对象编号" /></Field>
          <Field label="周期号"><Input value={filters.correlationId} onChange={event => setFilters({ ...filters, correlationId: event.target.value })} /></Field>
          <Button variant="primary" type="submit" className="self-end"><MagnifyingGlassIcon className="size-4" />查询</Button>
        </form>
      </Card>
      {error && <Alert tone="danger">{error}</Alert>}
      {streamError && <Alert tone="warning">{streamError}</Alert>}
      {loading && !data ? <LoadingCard /> : (
        <Card title="事件历史" description={`共 ${data?.total ?? rows.length} 条`}>
          <DataTable
            rows={rows}
            keyField="ingestId"
            columns={[
              { key: "ingestId", label: "摄入序号" },
              { key: "event", label: "类型", render: value => <Badge tone="info">{value?.eventType || "—"}</Badge> },
              { key: "event", label: "对象", render: value => value?.subject?.id || "—" },
              { key: "event", label: "周期号", render: value => value?.correlationId || "—" },
              { key: "event", label: "发生时间", render: value => formatTime(value?.occurredAt) },
            ]}
          />
          <Pagination
            page={page}
            pageSize={pageSize}
            total={data?.total ?? rows.length}
            onPageChange={value => { setLive(false); setPage(value); setQuery(makeEventQuery(appliedFilters, value, pageSize)); }}
            onPageSizeChange={value => { setLive(false); setPageSize(value); setPage(1); setQuery(makeEventQuery(appliedFilters, 1, value)); }}
          />
        </Card>
      )}
    </Page>
  );
}

function makeEventQuery(filters, page, pageSize) {
  const query = new URLSearchParams({ limit: String(pageSize), offset: String((page - 1) * pageSize) });
  Object.entries(filters).forEach(([key, value]) => value.trim() && query.set(key, value.trim()));
  return query.toString();
}

const chatModeLabels = {
  quick: "快速分析",
  combined: "综合分析",
};

const chatProgressLabels = {
  "run.started": "正在理解问题",
  "plan.created": "已确定查询范围",
  "iteration.started": "正在准备数据查询",
  "tool.started": "正在查询生产数据",
  "tool.completed": "数据查询完成",
  "relatedRecords.checked": "正在核对数据来源",
  "answer.delta": "正在整理回答",
  "run.completed": "回答已生成",
  "run.failed": "分析失败",
  "run.cancelled": "分析已取消",
};

const chatHistoryStatusLabels = {
  queued: "等待分析",
  running: "正在分析",
  cancelling: "正在取消",
  failed: "分析失败",
  cancelled: "已取消",
  completed: "回答已完成",
};

function chatProgressText(item) {
  const payload = item?.data || {};
  if (item?.type === "tool.completed" && payload.summary) return payload.summary;
  if (item?.type === "answer.delta" && payload.text) return payload.text;
  if (item?.type === "run.failed" && payload.error) return payload.error;
  if (item?.type === "run.cancelled" && payload.reason) return payload.reason;
  return chatProgressLabels[item?.type] || "";
}

function ChatAnswer({ answer, onFollowUp }) {
  if (!answer) return null;
  if (typeof answer === "string") {
    return <div className="max-w-3xl rounded-2xl rounded-bl-md bg-slate-900 px-5 py-4 text-sm leading-6 text-white whitespace-pre-wrap">{answer}</div>;
  }

  const findings = (answer.findings || []).filter(item => item && item !== answer.summary);
  return (
    <div className="max-w-3xl space-y-4 rounded-2xl rounded-bl-md bg-slate-900 px-5 py-4 text-sm leading-6 text-white">
      <p className="whitespace-pre-wrap">{answer.summary}</p>
      {findings.length > 0 && (
        <div>
          <p className="font-semibold text-slate-200">分析结果</p>
          <ul className="mt-1 list-disc space-y-1 pl-5 text-slate-100">
            {findings.map(item => <li key={item}>{item}</li>)}
          </ul>
        </div>
      )}
      {(answer.limitations || []).length > 0 && (
        <div className="rounded-xl bg-white/10 px-4 py-3">
          <p className="font-semibold text-slate-200">数据说明</p>
          <ul className="mt-1 list-disc space-y-1 pl-5 text-slate-200">
            {answer.limitations.map(item => <li key={item}>{item}</li>)}
          </ul>
        </div>
      )}
      {(answer.relatedRecords || []).length > 0 && (
        <div className="flex flex-wrap gap-2">
          {answer.relatedRecords.map(item => item.url ? (
            <Link key={`${item.kind}:${item.id}`} to={item.url} className="rounded-lg bg-white/10 px-3 py-1.5 text-xs font-medium text-blue-100 hover:bg-white/20">
              {item.label}
            </Link>
          ) : null)}
        </div>
      )}
      {(answer.followUpQuestions || []).length > 0 && (
        <div>
          <p className="font-semibold text-slate-200">可以继续问</p>
          <div className="mt-2 flex flex-wrap gap-2">
            {answer.followUpQuestions.map(item => (
              <button key={item} type="button" className="rounded-lg border border-white/20 px-3 py-1.5 text-left text-xs text-slate-100 hover:bg-white/10" onClick={() => onFollowUp(item)}>
                {item}
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

export function ChatPage() {
  const [searchParams] = useSearchParams();
  const projectId = searchParams.get("projectId");
  const [capabilities, setCapabilities] = useState(null);
  const [capabilitiesLoading, setCapabilitiesLoading] = useState(true);
  const [question, setQuestion] = useState("");
  const [mode, setMode] = useState("quick");
  const [run, setRun] = useState(null);
  const [events, setEvents] = useState([]);
  const [history, setHistory] = useState([]);
  const [historyLoading, setHistoryLoading] = useState(true);
  const [error, setError] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const controller = useRef(null);

  const loadHistory = useCallback(async () => {
    try {
      const value = await getJson("/api/v1/chat/runs?limit=8");
      setHistory(value.items || []);
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setHistoryLoading(false);
    }
  }, []);

  useEffect(() => {
    getJson("/api/v1/chat/capabilities").then(value => {
      setCapabilities(value);
      setMode(value.modes?.[0] || "quick");
    }).catch(requestError => setError(requestError.message))
      .finally(() => setCapabilitiesLoading(false));
    void loadHistory();
    return () => controller.current?.abort();
  }, [loadHistory]);

  async function start(event) {
    event.preventDefault();
    if (!question.trim()) return;
    setSubmitting(true);
    setError("");
    setEvents([]);
    try {
      const created = await postJson("/api/v1/chat/runs", {
        question: question.trim(),
        pageContext: projectId ? { kind: "research-project", id: projectId } : null,
        mode,
      });
      setRun({ ...created, question });
      controller.current = new AbortController();
      await streamSse(created.streamUrl, {
        signal: controller.current.signal,
        onEvent: ({ data }) => setEvents(current => [...current, data]),
      });
      setRun(await getJson(`/api/v1/chat/runs/${created.runId}`));
      await loadHistory();
    } catch (requestError) {
      if (requestError.name !== "AbortError") setError(requestError.message);
    } finally {
      setSubmitting(false);
    }
  }

  async function cancel() {
    if (!run?.runId || cancelling) return;
    setCancelling(true);
    setError("");
    try {
      await postJson(`/api/v1/chat/runs/${run.runId}:cancel`, {});
      controller.current?.abort();
      setRun(await getJson(`/api/v1/chat/runs/${run.runId}`));
      await loadHistory();
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setSubmitting(false);
      setCancelling(false);
    }
  }

  async function openHistory(runId) {
    if (submitting) return;
    setError("");
    try {
      const value = await getJson(`/api/v1/chat/runs/${runId}`);
      setRun(value);
      setQuestion(value.question || "");
      setMode(value.mode || "quick");
      setEvents([]);
    } catch (requestError) {
      setError(requestError.message);
    }
  }

  const visibleProgress = events
    .map(item => ({ ...item, message: chatProgressText(item) }))
    .filter(item => item.message)
    .slice(-4);

  return (
    <Page title="AI 工艺研发助手" description="围绕当前研发项目查询证据、分析数据并说明结论边界。">
      {capabilitiesLoading && <Alert title="正在连接 AI 助手">正在读取可用的分析能力。</Alert>}
      {!capabilitiesLoading && capabilities && !capabilities.enabled && <Alert tone="warning" title="AI 助手当前未启用">请联系管理员启用分析服务。</Alert>}
      {projectId && <Alert title="已绑定研发项目">本次问答只使用该项目可访问的研发记录和知识来源。</Alert>}
      {error && <Alert tone="danger">{error}</Alert>}
      <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_360px]">
        <Card title="分析问答">
          <div className="min-h-[420px] space-y-4">
            {!run && <EmptyState title="从一个生产问题开始" description="例如：当前有哪些运行对象？最近哪些周期数据不完整？" />}
            {run && (
              <div className="space-y-4">
                <div className="ml-auto max-w-2xl rounded-2xl rounded-br-md bg-blue-600 px-4 py-3 text-sm text-white">{run.question || question}</div>
                {!run.answer && visibleProgress.map((item, index) => (
                  <div key={`${item.sequence || item.type || "event"}-${index}`} className="max-w-3xl rounded-2xl rounded-bl-md border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-700">
                    <p className="whitespace-pre-wrap">{item.message}</p>
                  </div>
                ))}
                {submitting && visibleProgress.length === 0 && (
                  <div className="inline-flex items-center gap-2 rounded-xl bg-slate-100 px-4 py-3 text-sm text-slate-600">
                    <ArrowPathIcon className="size-4 animate-spin" />正在理解问题
                  </div>
                )}
                <ChatAnswer answer={run.answer} onFollowUp={setQuestion} />
                {run.error && <Alert tone="danger" title="分析失败">{run.error}</Alert>}
                {run.cancellationReason && <Alert title="分析已取消">{run.cancellationReason}</Alert>}
              </div>
            )}
          </div>
          <form className="mt-5 flex flex-col gap-3 border-t border-slate-100 pt-4" onSubmit={start}>
            <Field label="调查问题">
              <Textarea required value={question} onChange={event => setQuestion(event.target.value)} placeholder="描述要调查的现象、批次或周期…" />
            </Field>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <Field label="分析模式">
                <Select className="w-auto min-w-36" value={mode} onChange={event => setMode(event.target.value)}>
                  {(capabilities?.modes || ["quick"]).map(item => <option key={item} value={item}>{chatModeLabels[item] ?? item}</option>)}
                </Select>
              </Field>
              <div className="flex gap-2">
                {submitting && <Button type="button" onClick={cancel} disabled={cancelling}>{cancelling ? "正在取消" : "取消分析"}</Button>}
                <Button variant="primary" type="submit" disabled={!capabilities?.enabled || !question.trim() || submitting}>
                  <PaperAirplaneIcon className="size-4" />{submitting ? "分析中" : "开始分析"}
                </Button>
              </div>
            </div>
          </form>
        </Card>
        <Card title="最近问答" description="选择一条记录查看完整回答">
          {historyLoading ? (
            <div className="inline-flex items-center gap-2 text-sm text-slate-500"><ArrowPathIcon className="size-4 animate-spin" />正在读取</div>
          ) : history.length > 0 ? (
            <div className="space-y-2">
              {history.map(item => (
                <button key={item.runId} type="button" className="w-full rounded-xl border border-slate-200 px-3 py-3 text-left hover:border-blue-300 hover:bg-blue-50/50" onClick={() => openHistory(item.runId)} disabled={submitting}>
                  <p className="line-clamp-2 text-sm font-medium text-slate-800">{item.question}</p>
                  <p className="mt-1 line-clamp-2 text-xs leading-5 text-slate-500">{item.summary || chatHistoryStatusLabels[item.status] || "暂无回答"}</p>
                  <p className="mt-1 text-xs text-slate-400">{formatTime(item.createdAt)}</p>
                </button>
              ))}
            </div>
          ) : (
            <EmptyState title="暂无问答记录" description="完成一次分析后会显示在这里。" />
          )}
        </Card>
      </div>
    </Page>
  );
}

export function ObjectExplorerPage() {
  const objects = useApi("/api/v1/data-objects?limit=500");
  const rows = extractRows(objects.data);
  const [query, setQuery] = useState("");
  const [selectedKey, setSelectedKey] = useState("");
  const filtered = useMemo(() => rows.filter(row => JSON.stringify(row).toLowerCase().includes(query.toLowerCase())), [query, rows]);
  const rowKey = row => `${row.subjectType}:${row.subjectId}`;
  const selected = filtered.find(row => rowKey(row) === selectedKey) || filtered[0] || null;
  const objectTypeCount = new Set(rows.map(row => row.subjectType).filter(Boolean)).size;
  const eventTotal = rows.reduce((total, row) => total + Number(row.eventCount || 0), 0);
  const sampleTotal = rows.reduce((total, row) => total + Number(row.sampleCount || 0), 0);

  useEffect(() => {
    if (!selectedKey && rows.length) setSelectedKey(rowKey(rows[0]));
  }, [rows, selectedKey]);

  return (
    <Page
      title="工业对象"
      description="从真实设备和生产对象出发，连续查看它的运行、事件、质量与数据健康。"
      actions={<Link className="inline-flex min-h-9 items-center rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700" to="/configuration/acquisition-profiles">接入设备</Link>}
    >
      {objects.error && <Alert tone="danger" title="工业对象暂不可用">{objects.error}</Alert>}
      {objects.loading && !objects.data ? <LoadingCard /> : (
        rows.length ? (
          <>
            <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
              <Metric label="工业对象" value={objects.data?.total ?? rows.length} hint="已从现场数据自动识别" />
              <Metric label="对象类型" value={objectTypeCount} hint="统一业务语义下的分类" />
              <Metric label="累计事件" value={formatInteger(eventTotal)} hint="可追溯的运行与状态变化" />
              <Metric label="累计样本" value={formatInteger(sampleTotal)} hint="已归属到对象的过程数据" />
            </div>
            <Card
              title="对象目录"
              description="选择一个对象，右侧会保留它的身份和业务入口。"
            >
              <div className="grid min-h-[520px] gap-5 xl:grid-cols-[minmax(280px,0.78fr)_minmax(0,1.5fr)]">
                <section className="min-w-0 rounded-xl border border-slate-200 bg-slate-50/70 p-3" aria-label="工业对象列表">
                  <Field label="搜索对象">
                    <Input value={query} onChange={event => setQuery(event.target.value)} placeholder="设备编号、对象类型或采集节点" />
                  </Field>
                  <p className="mt-3 px-1 text-xs text-slate-500">
                    {query.trim() ? `找到 ${filtered.length} 个对象` : `共 ${rows.length} 个对象`}
                  </p>
                  <div className="mt-2 grid max-h-[420px] gap-2 overflow-y-auto pr-1">
                    {filtered.map(row => {
                      const active = selected && rowKey(row) === rowKey(selected);
                      return (
                        <button
                          key={rowKey(row)}
                          type="button"
                          aria-pressed={active}
                          onClick={() => setSelectedKey(rowKey(row))}
                          className={`rounded-xl border px-3 py-3 text-left transition ${active
                            ? "border-blue-300 bg-white shadow-sm ring-2 ring-blue-100"
                            : "border-transparent bg-white/70 hover:border-slate-200 hover:bg-white"}`}
                        >
                          <div className="flex items-center justify-between gap-3">
                            <Badge tone={active ? "info" : "neutral"}>{objectTypeLabel(row.subjectType)}</Badge>
                            <span className="text-xs text-slate-400">{formatInteger(row.sampleCount)} 样本</span>
                          </div>
                          <p className="mt-2 truncate text-sm font-semibold text-slate-900">{row.subjectId}</p>
                          <p className="mt-1 truncate text-xs text-slate-500">{row.edgeId || "未关联采集节点"}</p>
                        </button>
                      );
                    })}
                    {!filtered.length && (
                      <EmptyState title="没有匹配的对象" description="请调整搜索条件后重试。" />
                    )}
                  </div>
                </section>

                {selected && (
                  <section className="min-w-0 rounded-xl border border-slate-200 bg-white p-5" aria-live="polite">
                    <div className="flex flex-col gap-3 border-b border-slate-100 pb-5 sm:flex-row sm:items-start sm:justify-between">
                      <div className="min-w-0">
                        <Badge tone="info">{objectTypeLabel(selected.subjectType)}</Badge>
                        <h2 className="mt-3 break-words text-xl font-semibold text-slate-950">{selected.subjectId}</h2>
                        <p className="mt-1 text-sm text-slate-500">对象详情与相关工作入口</p>
                      </div>
                      <span className="shrink-0 rounded-lg bg-emerald-50 px-3 py-2 text-xs font-medium text-emerald-700">
                        已接收现场数据
                      </span>
                    </div>

                    <div className="grid gap-3 py-5 sm:grid-cols-3">
                      {[
                        ["事件", formatInteger(selected.eventCount)],
                        ["过程样本", formatInteger(selected.sampleCount)],
                        ["最新活动", eventTypeLabel(selected.latestEventType)],
                      ].map(([label, value]) => (
                        <div key={label} className="rounded-xl bg-slate-50 p-4">
                          <p className="text-xs font-medium text-slate-500">{label}</p>
                          <p className="mt-2 break-words text-lg font-semibold text-slate-900">{value}</p>
                        </div>
                      ))}
                    </div>

                    <dl className="grid gap-x-6 gap-y-4 border-y border-slate-100 py-5 sm:grid-cols-2">
                      <div><dt className="text-xs font-medium text-slate-500">对象类型</dt><dd className="mt-1 text-sm font-medium text-slate-800">{objectTypeLabel(selected.subjectType)}</dd></div>
                      <div><dt className="text-xs font-medium text-slate-500">采集节点</dt><dd className="mt-1 break-words text-sm font-medium text-slate-800">{selected.edgeId || "未关联"}</dd></div>
                      <div><dt className="text-xs font-medium text-slate-500">最后活动</dt><dd className="mt-1 text-sm font-medium text-slate-800">{formatTime(selected.lastObservedAt)}</dd></div>
                      <div><dt className="text-xs font-medium text-slate-500">最后样本</dt><dd className="mt-1 text-sm font-medium text-slate-800">{formatTime(selected.lastSampleAt)}</dd></div>
                    </dl>

                    <div className="pt-5">
                      <h3 className="text-sm font-semibold text-slate-900">在这个对象中继续工作</h3>
                      <p className="mt-1 text-xs leading-5 text-slate-500">进入其他页面时保留当前对象，避免重新查找和填写编号。</p>
                      <div className="mt-3 grid gap-3 sm:grid-cols-2">
                        {[
                          [`/cycles?machineId=${encodeURIComponent(selected.subjectId)}`, "运行记录", "查看该对象的生产周期与上下文"],
                          [`/events?subjectId=${encodeURIComponent(selected.subjectId)}`, "事件时间线", "追溯该对象上报的事件与状态变化"],
                          [`/quality-analysis?subjectType=${encodeURIComponent(selected.subjectType)}&subjectId=${encodeURIComponent(selected.subjectId)}`, "质量分析", "查看与该对象关联的检测结果"],
                          [`/data-quality?subjectType=${encodeURIComponent(selected.subjectType)}&subjectId=${encodeURIComponent(selected.subjectId)}`, "数据健康", "确认样本范围、连续性和更新时间"],
                        ].map(([to, label, description]) => (
                          <Link key={label} to={to} className="rounded-xl border border-slate-200 p-4 transition hover:border-blue-300 hover:bg-blue-50/50">
                            <p className="text-sm font-semibold text-blue-700">{label} →</p>
                            <p className="mt-1 text-xs leading-5 text-slate-500">{description}</p>
                          </Link>
                        ))}
                      </div>
                    </div>
                  </section>
                )}
              </div>
            </Card>
          </>
        ) : (
          <>
            <WorkflowGuide
              title="建立第一个工业对象"
              description="这里不需要手工维护目录；设备开始上报数据后，平台会自动建立可追溯对象。"
              steps={[
                { title: "接入设备", description: "选择现场节点、通信方式和设备地址。", state: "current" },
                { title: "开始采集", description: "现场节点读取数据并持续上报。", state: "upcoming" },
                { title: "形成对象", description: "运行、事件和样本会归集到统一对象。", state: "upcoming" },
              ]}
            />
            <EmptyState
              title="尚未收到生产数据"
              description="完成设备接入并开始采集后，对象会自动显示在这里。"
            />
          </>
        )
      )}
    </Page>
  );
}

const productionResources = {
  context: {
    title: "生产切换", endpoint: "/api/v1/production-contexts", key: "contextId",
    description: "为设备选择接下来生产的产品、配方和已装工装，保存后对新周期生效。",
    drawerDescription: "按顺序确认设备、产品、配方和工装；保存后只影响新开始的生产周期。",
    columns: [["machineId", "设备"], ["productCode", "产品"], ["recipeId", "配方"], ["validFrom", "生效时间"], ["validTo", "结束时间"]],
    template: { machineId: "", productSeries: "", productCode: "", recipeId: "", recipeVersion: 1, toolingInstallationId: "", source: "manual", materialLotRef: "" },
    createLabel: "配置下一批生产",
    requiredFields: ["machineId", "productSeries", "productCode", "recipeId"],
    prepare: value => ({ ...value, validFrom: new Date().toISOString() }),
    lifecycle: { label: "结束", visible: value => !value.validTo, url: value => `/api/v1/production-contexts/${value.contextId}:close`, body: () => ({ at: new Date().toISOString() }) },
  },
  installation: {
    title: "工装装卸", endpoint: "/api/v1/tooling-installations", key: "installationId",
    description: "记录哪个工装组合版本在何时装入设备，供后续周期自动关联。",
    drawerDescription: "选择设备和已经建立的工装组合版本，装入后会进入该设备的有效工装记录。",
    columns: [["machineId", "设备"], ["moldId", "工装"], ["installedAt", "装入"], ["removedAt", "卸下"]],
    template: { machineId: "", assemblyRevisionId: "", source: "manual" },
    createLabel: "装入工装",
    requiredFields: ["machineId", "assemblyRevisionId"],
    prepare: value => ({ ...value, installedAt: new Date().toISOString(), commandId: crypto.randomUUID() }),
    lifecycle: { label: "卸下", visible: value => !value.removedAt, url: value => `/api/v1/tooling-installations/${value.installationId}:remove`, body: () => ({ at: new Date().toISOString() }) },
  },
  componentType: {
    title: "组件类型", endpoint: "/api/v1/tooling-component-types", key: "componentTypeCode",
    description: "建立组件分类，供组件台账和工装装配位置使用。",
    columns: [["componentTypeCode", "代码"], ["name", "名称"], ["status", "状态"], ["attributes", "属性"]],
    template: { componentTypeCode: "", name: "", status: "active", attributes: {} },
    createLabel: "新建组件类型",
    requiredFields: ["componentTypeCode", "name"],
    statusOptions: [["active", "启用"], ["inactive", "停用"]],
    deleteUrl: value => `/api/v1/tooling-component-types/${encodeURIComponent(value.componentTypeCode)}`,
  },
  component: {
    title: "组件台账", endpoint: "/api/v1/tooling-components", key: "componentId",
    description: "登记可更换、复用并需要追溯的物理组件。",
    columns: [["componentId", "组件"], ["componentTypeCode", "类型"], ["serialNo", "序列号"], ["name", "名称"], ["status", "状态"]],
    template: { componentId: "", componentTypeCode: "", serialNo: "", name: "", status: "available", attributes: {} },
    createLabel: "登记组件",
    requiredFields: ["componentId", "componentTypeCode", "serialNo", "name"],
    statusOptions: [["available", "可用"], ["maintenance", "维护中"], ["retired", "已退役"]],
    deleteUrl: value => `/api/v1/tooling-components/${encodeURIComponent(value.componentId)}`,
  },
  type: {
    title: "工装类型", endpoint: "/api/v1/tooling-types", key: "toolingTypeCode",
    description: "定义一类工装包含哪些装配位置，以及每个位置允许使用的组件。",
    columns: [["toolingTypeCode", "代码"], ["version", "版本"], ["name", "名称"], ["status", "状态"], ["roles", "装配位置"]],
    template: { toolingTypeCode: "", version: 1, name: "", status: "active", roles: [] },
    createLabel: "新建工装类型",
    requiredFields: ["toolingTypeCode", "name"],
    statusOptions: [["active", "启用"], ["inactive", "停用"]],
    deleteUrl: value => `/api/v1/tooling-types/${encodeURIComponent(value.toolingTypeCode)}/${value.version}`,
  },
  assembly: {
    title: "工装组合", endpoint: "/api/v1/tooling-assemblies", key: "moldId",
    description: "建立工装身份；具体装配内容通过不可变版本保留历史。",
    columns: [["moldId", "工装"], ["name", "名称"], ["toolingTypeCode", "类型"], ["status", "状态"]],
    template: { moldId: "", toolingTypeCode: "", name: "", status: "active" },
    createLabel: "新建工装",
    requiredFields: ["moldId", "toolingTypeCode", "name"],
    statusOptions: [["active", "启用"], ["inactive", "停用"]],
    deleteUrl: value => `/api/v1/tooling-assemblies/${encodeURIComponent(value.moldId)}`,
  },
};

const productionFieldLabels = {
  machineId: "设备编号",
  productSeries: "产品系列",
  productCode: "产品编号",
  recipeId: "配方编号",
  recipeVersion: "配方版本",
  toolingInstallationId: "工装装卸记录",
  source: "记录来源",
  materialLotRef: "物料批次",
  assemblyRevisionId: "工装组合版本",
  componentTypeCode: "组件类型代码",
  name: "名称",
  status: "状态",
  attributes: "扩展属性",
  componentId: "组件编号",
  serialNo: "序列号",
  toolingTypeCode: "工装类型代码",
  version: "版本",
  roles: "装配位置",
  moldId: "工装编号",
};

function createProductionEditor(resource, value) {
  return Object.fromEntries(Object.entries(resource.template).map(([key, initial]) => [
    key,
    key === "attributes"
      ? Object.entries(value[key] ?? initial).map(([attribute, attributeValue]) => ({ attribute, value: attributeValue }))
      : key === "roles"
        ? (value[key] ?? initial).map(role => ({ ...role, acceptedComponentTypeCodes: role.acceptedComponentTypeCodes || [] }))
      : value[key] ?? initial,
  ]));
}

function parseProductionEditor(resource, editor, base) {
  const value = { ...base };
  Object.entries(resource.template).forEach(([key, initial]) => {
    if (key === "attributes") {
      value[key] = Object.fromEntries((editor[key] || [])
        .filter(item => item.attribute.trim() && item.value.trim())
        .map(item => [item.attribute.trim(), item.value.trim()]));
    } else if (key === "roles") {
      value[key] = editor[key].map(role => ({
        ...role,
        code: role.code.trim(),
        name: role.name.trim(),
        maxCount: Number(role.maxCount),
        sortOrder: Number(role.sortOrder),
      }));
    } else if (typeof initial === "number") {
      value[key] = Number(editor[key]);
    } else {
      value[key] = editor[key];
    }
  });
  return value;
}

function isProductionEditorValid(resource, editor) {
  if (resource.requiredFields?.some(key => !String(editor[key] ?? "").trim())) return false;
  return Object.entries(resource.template).every(([key, initial]) => {
    if (typeof initial === "number") return Number(editor[key]) >= 1;
    if (key === "attributes") return (editor[key] || []).every(item =>
      (!item.attribute.trim() && !item.value.trim()) || (item.attribute.trim() && item.value.trim()));
    if (key === "roles") return editor[key].length > 0 && editor[key].every(role =>
      role.code.trim() && role.name.trim() && Number(role.maxCount) >= 1 && Number(role.sortOrder) >= 0 &&
      role.acceptedComponentTypeCodes.length > 0);
    return true;
  });
}

function ProductionReferenceField({ fieldKey, value, required, editor, onChange }) {
  const settings = {
    machineId: {
      endpoint: "/api/v1/data-objects?limit=500",
      label: "设备",
      filter: row => ["equipment", "machine", "optical-molding-machine"].includes(row.subjectType),
      optionValue: row => row.subjectId,
      optionLabel: row => `${row.subjectId}${row.edgeId ? ` · 由 ${row.edgeId} 采集` : ""}`,
    },
    toolingInstallationId: {
      endpoint: "/api/v1/tooling-installations?activeOnly=true",
      label: "当前已装工装",
      filter: row => !editor.machineId || row.machineId === editor.machineId,
      optionValue: row => row.installationId,
      optionLabel: row => `${row.machineId} 当前工装${row.installedAt ? ` · ${formatTime(row.installedAt)}装入` : ""}`,
    },
    assemblyRevisionId: {
      endpoint: "/api/v1/tooling-assemblies/revisions",
      label: "工装组合版本",
      optionValue: row => row.assemblyRevisionId,
      optionLabel: row => `${row.moldId} · 版本 ${row.revision}`,
    },
    componentTypeCode: {
      endpoint: "/api/v1/tooling-component-types",
      label: "组件类型",
      filter: row => row.status !== "inactive",
      optionValue: row => row.componentTypeCode,
      optionLabel: row => `${row.name} · ${row.componentTypeCode}`,
    },
    toolingTypeCode: {
      endpoint: "/api/v1/tooling-types",
      label: "工装类型",
      filter: row => row.status !== "inactive",
      optionValue: row => row.toolingTypeCode,
      optionLabel: row => `${row.name} · ${row.toolingTypeCode} v${row.version}`,
    },
  };
  const setting = settings[fieldKey];
  const { data, error } = useApi(setting.endpoint);
  const sourceRows = extractRows(data).filter(setting.filter || (() => true));
  const options = [...new Map(sourceRows.map(row => [setting.optionValue(row), row])).values()];
  const hasValue = options.some(row => setting.optionValue(row) === value);
  return (
    <Field label={setting.label} error={error || ""}>
      <Select required={required} value={value || ""} onChange={event => onChange(fieldKey, event.target.value)}>
        <option value="">{required ? "请选择" : "不关联"}</option>
        {value && !hasValue && <option value={value}>{value}（历史值）</option>}
        {options.map(row => <option key={setting.optionValue(row)} value={setting.optionValue(row)}>{setting.optionLabel(row)}</option>)}
      </Select>
    </Field>
  );
}

function RecipeReferenceField({ editor, onChange, required }) {
  const { data, error } = useApi("/api/v1/recipe-versions");
  const recipes = extractRows(data).filter(row =>
    row.status === "published" || (row.recipeId === editor.recipeId && Number(row.version) === Number(editor.recipeVersion)));
  const selected = editor.recipeId ? `${editor.recipeId}:${editor.recipeVersion}` : "";
  const hasSelected = recipes.some(row => `${row.recipeId}:${row.version}` === selected);
  return (
    <Field label="配方" error={error || ""}>
      <Select
        required={required}
        value={selected}
        onChange={event => {
          const row = recipes.find(item => `${item.recipeId}:${item.version}` === event.target.value);
          onChange("recipeId", row?.recipeId || "");
          onChange("recipeVersion", row?.version || 1);
        }}
      >
        <option value="">请选择已发布配方</option>
        {selected && !hasSelected && <option value={selected}>{editor.recipeId} · v{editor.recipeVersion}（历史值）</option>}
        {recipes.map(row => <option key={`${row.recipeId}:${row.version}`} value={`${row.recipeId}:${row.version}`}>{row.name} · {row.recipeId} v{row.version}</option>)}
      </Select>
    </Field>
  );
}

function ProductionRecordForm({ resource, editor, onChange }) {
  if (resource === productionResources.context) {
    const hasMachine = Boolean(editor.machineId);
    const hasProduct = Boolean(editor.productCode?.trim() && editor.productSeries?.trim());
    const hasRecipe = Boolean(editor.recipeId);
    return (
      <div className="grid gap-5">
        <WorkflowGuide
          title="完成这 3 步即可生效"
          description="必填内容完成后，底部按钮会自动变为可用。"
          steps={[
            { title: "选择生产设备", description: "确定接下来要切换的现场设备。", state: hasMachine ? "done" : "current" },
            { title: "确认产品与配方", description: "填写产品身份并选择已发布配方。", state: hasProduct && hasRecipe ? "done" : hasMachine ? "current" : "upcoming" },
            { title: "检查并生效", description: "核对工装和物料批次后保存。", state: hasMachine && hasProduct && hasRecipe ? "current" : "upcoming" },
          ]}
        />
        <Card title="1. 选择生产设备" description="只显示已经通过现场节点上报过数据的设备。">
          <ProductionReferenceField
            fieldKey="machineId"
            value={editor.machineId}
            editor={editor}
            required
            onChange={(key, value) => {
              onChange(key, value);
              onChange("toolingInstallationId", "");
            }}
          />
        </Card>
        <Card title="2. 确认产品与配方" description="产品编号用于追溯实物，产品系列用于同类分析。">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="产品系列" hint="例如 LENS-A、轴类零件">
              <Input required value={editor.productSeries || ""} onChange={event => onChange("productSeries", event.target.value)} />
            </Field>
            <Field label="产品编号" hint="填写现场使用的产品或物料编号">
              <Input required value={editor.productCode || ""} onChange={event => onChange("productCode", event.target.value)} />
            </Field>
            <div className="sm:col-span-2">
              <RecipeReferenceField editor={editor} onChange={onChange} required />
            </div>
          </div>
        </Card>
        <Card title="3. 补充现场信息" description="工装和物料批次可选；填写后会自动关联到后续生产周期。">
          <div className="grid gap-4 sm:grid-cols-2">
            <ProductionReferenceField fieldKey="toolingInstallationId" value={editor.toolingInstallationId} editor={editor} onChange={onChange} />
            <Field label="物料批次" hint="没有批次管理时可以留空">
              <Input value={editor.materialLotRef || ""} onChange={event => onChange("materialLotRef", event.target.value)} />
            </Field>
          </div>
        </Card>
        {hasMachine && hasProduct && hasRecipe && (
          <Alert tone="success" title="可以生效">
            保存后，设备 {editor.machineId} 新开始的周期将使用产品 {editor.productCode} 和配方 {editor.recipeId} v{editor.recipeVersion}。
          </Alert>
        )}
      </div>
    );
  }
  return (
    <div className="grid gap-4 sm:grid-cols-2">
      {Object.entries(resource.template).map(([key, initial]) => {
        const required = resource.requiredFields?.includes(key);
        const label = productionFieldLabels[key] ?? key;
        if (key === "recipeVersion") return null;
        if (key === "recipeId") return <RecipeReferenceField key={key} editor={editor} onChange={onChange} required={required} />;
        if (["machineId", "toolingInstallationId", "assemblyRevisionId", "componentTypeCode", "toolingTypeCode"].includes(key)) {
          return <ProductionReferenceField key={key} fieldKey={key} value={editor[key]} editor={editor} onChange={onChange} required={required} />;
        }
        if (key === "attributes") return <AttributeFields key={key} value={editor[key] || []} onChange={value => onChange(key, value)} />;
        if (key === "roles") return <ToolingRoleFields key={key} value={editor[key] || []} onChange={value => onChange(key, value)} />;
        if (key === "status" && resource.statusOptions) {
          return (
            <Field key={key} label={label}>
              <Select required={required} value={editor[key] ?? ""} onChange={event => onChange(key, event.target.value)}>
                {resource.statusOptions.map(([value, optionLabel]) => <option key={value} value={value}>{optionLabel}</option>)}
              </Select>
            </Field>
          );
        }
        if (key === "source") {
          return (
            <Field key={key} label={label}>
              <Select value={editor[key] ?? "manual"} onChange={event => onChange(key, event.target.value)}>
                <option value="manual">手动操作</option>
              </Select>
            </Field>
          );
        }
        return (
          <Field key={key} label={label}>
            <Input
              required={required}
              type={typeof initial === "number" ? "number" : "text"}
              min={typeof initial === "number" ? 1 : undefined}
              value={editor[key] ?? ""}
              onChange={event => onChange(key, event.target.value)}
            />
          </Field>
        );
      })}
    </div>
  );
}

function AttributeFields({ value, onChange }) {
  const rows = value.length ? value : [{ attribute: "", value: "" }];
  function update(index, field, nextValue) {
    const source = value.length ? value : [{ attribute: "", value: "" }];
    onChange(source.map((item, rowIndex) => rowIndex === index ? { ...item, [field]: nextValue } : item));
  }
  return (
    <Card
      className="sm:col-span-2"
      title="扩展属性"
      description="登记需要在台账中查询的业务属性。"
      actions={<Button onClick={() => onChange([...value, { attribute: "", value: "" }])}>添加属性</Button>}
    >
      <div className="grid gap-2">
        {rows.map((item, index) => (
          <div key={index} className="grid gap-2 sm:grid-cols-[1fr_1fr_auto]">
            <Input aria-label={`属性名称 ${index + 1}`} value={item.attribute} placeholder="属性名称" onChange={event => update(index, "attribute", event.target.value)} />
            <Input aria-label={`属性内容 ${index + 1}`} value={item.value} placeholder="属性内容" onChange={event => update(index, "value", event.target.value)} />
            {value.length > 0 && <Button variant="ghost" className="text-rose-700" onClick={() => onChange(value.filter((_item, rowIndex) => rowIndex !== index))}>移除</Button>}
          </div>
        ))}
      </div>
    </Card>
  );
}

function ToolingRoleFields({ value, onChange }) {
  const { data, error } = useApi("/api/v1/tooling-component-types");
  const componentTypes = useMemo(
    () => [...new Map(extractRows(data).map(item => [item.componentTypeCode, item])).values()],
    [data],
  );
  function update(index, patch) {
    onChange(value.map((role, rowIndex) => rowIndex === index ? { ...role, ...patch } : role));
  }
  function add() {
    onChange([...value, { code: "", name: "", required: true, maxCount: 1, sortOrder: value.length + 1, acceptedComponentTypeCodes: [] }]);
  }
  return (
    <Card className="sm:col-span-2" title="装配位置" description="定义工装由哪些组件位置组成。" actions={<Button onClick={add}>添加装配位置</Button>}>
      {error && <Alert tone="danger">{error}</Alert>}
      <div className="grid gap-4">
        {value.length === 0 && <p className="text-sm text-slate-500">请至少添加一个装配位置。</p>}
        {value.map((role, index) => (
          <div key={index} className="grid gap-3 rounded-xl border border-slate-200 p-4 sm:grid-cols-2">
            <Field label="位置代码"><Input value={role.code} onChange={event => update(index, { code: event.target.value })} /></Field>
            <Field label="位置名称"><Input value={role.name} onChange={event => update(index, { name: event.target.value })} /></Field>
            <Field label="最大组件数"><Input type="number" min="1" value={role.maxCount} onChange={event => update(index, { maxCount: event.target.value })} /></Field>
            <Field label="显示顺序"><Input type="number" min="0" value={role.sortOrder} onChange={event => update(index, { sortOrder: event.target.value })} /></Field>
            <div className="sm:col-span-2">
              <p className="mb-2 text-sm font-medium text-slate-700">允许的组件类型</p>
              <div className="flex flex-wrap gap-3">
                {componentTypes.map(type => (
                  <label key={type.componentTypeCode} className="flex items-center gap-1.5 text-sm">
                    <input
                      type="checkbox"
                      checked={role.acceptedComponentTypeCodes.includes(type.componentTypeCode)}
                      onChange={event => update(index, {
                        acceptedComponentTypeCodes: event.target.checked
                          ? [...role.acceptedComponentTypeCodes, type.componentTypeCode]
                          : role.acceptedComponentTypeCodes.filter(code => code !== type.componentTypeCode),
                      })}
                    />
                    {type.name}
                  </label>
                ))}
              </div>
            </div>
            <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={role.required} onChange={event => update(index, { required: event.target.checked })} />必须装配</label>
            <Button variant="ghost" className="justify-self-start text-rose-700" onClick={() => onChange(value.filter((_item, rowIndex) => rowIndex !== index))}>移除</Button>
          </div>
        ))}
      </div>
    </Card>
  );
}

export function ProductionSetupPage({ section }) {
  const resource = productionResources[section];
  const { data, loading, error, reload } = useApi(resource.endpoint);
  const rows = extractRows(data);
  const [open, setOpen] = useState(false);
  const [editor, setEditor] = useState({});
  const [editorBase, setEditorBase] = useState({});
  const [editorMode, setEditorMode] = useState("create");
  const [actionError, setActionError] = useState("");
  const [saving, setSaving] = useState(false);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const pagedRows = rows.slice((page - 1) * pageSize, page * pageSize);
  const editorValid = isProductionEditorValid(resource, editor);
  const activeRows = rows.filter(row => section === "context" ? !row.validTo : section === "installation" ? !row.removedAt : false);

  useEffect(() => {
    setPage(1);
  }, [section]);

  useEffect(() => {
    const pageCount = Math.max(1, Math.ceil(rows.length / pageSize));
    if (page > pageCount) setPage(pageCount);
  }, [page, pageSize, rows.length]);

  function openEditor(row = null) {
    const value = row ? structuredClone(row) : structuredClone(resource.template);
    if (row?.version && section === "type") {
      value.version = Number(row.version) + 1;
      value.status = "active";
    }
    setEditorMode(row ? (section === "type" ? "version" : "edit") : "create");
    setEditorBase(value);
    setEditor(createProductionEditor(resource, value));
    setActionError("");
    setOpen(true);
  }

  async function save() {
    setSaving(true);
    setActionError("");
    try {
      const value = parseProductionEditor(resource, editor, editorBase);
      await postJson(resource.endpoint, resource.prepare ? resource.prepare(value) : value);
      setOpen(false);
      await reload();
      notify(section === "context" ? "生产配置已生效，新开始的周期会自动关联。" : `${resource.title}已保存。`);
    } catch (requestError) {
      setActionError(requestError.message);
    } finally {
      setSaving(false);
    }
  }

  async function lifecycle(row) {
    if (!window.confirm(`确认${resource.lifecycle.label}这条${resource.title}记录？历史引用会继续保留。`)) return;
    try {
      await postJson(resource.lifecycle.url(row), resource.lifecycle.body(row));
      await reload();
      notify(`${resource.lifecycle.label}操作已完成。`);
    } catch (requestError) {
      setActionError(requestError.message);
    }
  }

  async function remove(row) {
    if (!window.confirm("只能删除尚未形成历史引用的数据，是否继续？")) return;
    try {
      await deleteJson(resource.deleteUrl(row));
      await reload();
    } catch (requestError) {
      setActionError(requestError.message);
    }
  }

  const columns = [
    ...resource.columns.map(([key, label]) => ({
      key,
      label,
      render: key.endsWith("At") || ["validFrom", "validTo"].includes(key)
        ? formatTime
        : key === "status" ? value => <StatusBadge value={value} />
          : key === "recipeId" ? (value, row) => `${value} v${row.recipeVersion}`
            : key === "productCode" ? (value, row) => <div><p className="font-medium text-slate-800">{value}</p>{row.productSeries && <p className="mt-0.5 text-xs text-slate-500">{row.productSeries}</p>}</div>
          : key === "roles" ? value => value?.length ? value.map(role => role.name).join("、") : "—"
            : key === "attributes" ? value => {
              const entries = Object.entries(value || {});
              return entries.length ? entries.map(([attribute, attributeValue]) => `${attribute}：${attributeValue}`).join("、") : "—";
            }
              : undefined,
    })),
    {
      key: "_actions",
      label: "操作",
      render: (_value, row) => (
        <div className="flex min-w-max gap-1">
          {!["context", "installation"].includes(section) && <Button variant="ghost" className="px-2" onClick={() => openEditor(row)}>{section === "type" ? "新版本维护" : "编辑"}</Button>}
          {resource.lifecycle?.visible(row) && <Button variant="ghost" className="px-2 text-amber-700" onClick={() => lifecycle(row)}>{resource.lifecycle.label}</Button>}
          {resource.deleteUrl && <Button variant="ghost" className="px-2 text-rose-700" onClick={() => remove(row)}>删除</Button>}
        </div>
      ),
    },
  ];

  return (
    <Page title={resource.title} description={resource.description} actions={section === "context" ? undefined : <Button variant="primary" onClick={() => openEditor()}>{resource.createLabel}</Button>}>
      {(error || (!open && actionError)) && <Alert tone="danger">{error || actionError}</Alert>}
      {loading && !data ? <LoadingCard /> : (
        <>
          {section === "context" && (
            <>
              <WorkflowGuide
                title="生产开始前"
                description="设备接入和配方发布通常只需配置一次；每次换产品或换配方时更新生产配置。"
                steps={[
                  { title: "设备已有数据", description: "在“设备采集”中完成设备接入。", state: rows.length ? "done" : "current" },
                  { title: "产品与配方就绪", description: "准备产品编号和已发布配方。", state: rows.some(row => row.recipeId) ? "done" : rows.length ? "current" : "upcoming" },
                  { title: "启用生产配置", description: "确认设备、产品、配方和当前工装。", state: activeRows.length ? "done" : "current" },
                ]}
              />
              <Card
                title="当前生效配置"
                description={activeRows.length ? `${activeRows.length} 台设备已准备好开始新周期` : "目前没有正在生效的生产配置"}
                actions={<Button variant="primary" onClick={() => openEditor()}>{activeRows.length ? "切换产品或配方" : "开始配置"}</Button>}
              >
                {activeRows.length ? (
                  <div className="grid gap-3 lg:grid-cols-2">
                    {activeRows.map(row => (
                      <article key={row.contextId} className="rounded-xl border border-slate-200 bg-slate-50 p-4">
                        <div className="flex items-start justify-between gap-3">
                          <div>
                            <p className="font-semibold text-slate-950">{row.machineId}</p>
                            <p className="mt-1 text-sm text-slate-600">{row.productCode} · {row.productSeries || "未填写系列"}</p>
                          </div>
                          <StatusBadge value="active" />
                        </div>
                        <p className="mt-3 text-sm text-slate-600">配方：{row.recipeId} v{row.recipeVersion}</p>
                        <p className="mt-1 text-xs text-slate-400">自 {formatTime(row.validFrom)} 生效</p>
                      </article>
                    ))}
                  </div>
                ) : <EmptyState title="还没有生效配置" description="点击“开始配置”，完成设备、产品和配方选择。" />}
              </Card>
            </>
          )}
          {section === "installation" && (
            <WorkflowGuide
              title="工装装卸怎么用"
              steps={[
                { title: "先建立工装组合", description: "在工装管理中确定工装及其组件版本。", state: rows.length ? "done" : "current" },
                { title: "选择设备并装入", description: "一台设备可保留当前有效工装记录。", state: activeRows.length ? "done" : "current" },
                { title: "换装时先卸下", description: "卸下后历史周期仍保留原工装关联。", state: activeRows.length ? "current" : "upcoming" },
              ]}
            />
          )}
          <Card title={["context", "installation"].includes(section) ? "历史记录" : `${resource.title}记录`} description={`共 ${rows.length} 条`}>
            <DataTable
              rows={pagedRows}
              keyField={resource.key}
              getRowKey={section === "type" ? row => `${row[resource.key]}:${row.version ?? 1}` : undefined}
              columns={columns}
            />
            <Pagination
              page={page}
              pageSize={pageSize}
              total={rows.length}
              onPageChange={setPage}
              onPageSizeChange={value => { setPageSize(value); setPage(1); }}
            />
          </Card>
        </>
      )}
      <Drawer
        open={open}
        onClose={() => setOpen(false)}
        closeOnBackdrop={false}
        title={editorMode === "create" ? resource.createLabel : editorMode === "version" ? "新版本维护" : `编辑${resource.title}`}
        description={resource.drawerDescription || "填写业务信息后保存，平台会校验引用并保留历史。"}
        footer={<><Button onClick={() => setOpen(false)}>取消</Button><Button variant="primary" onClick={save} disabled={saving || !editorValid}>{saving ? "保存中" : section === "context" ? "确认并生效" : "保存"}</Button></>}
      >
        {actionError && <Alert tone="danger">{actionError}</Alert>}
        <ProductionRecordForm
          resource={resource}
          editor={editor}
          onChange={(key, value) => setEditor(current => ({ ...current, [key]: value }))}
        />
      </Drawer>
    </Page>
  );
}

export function InspectionsPage() {
  const inspectionPageSize = 50;
  const [taskStatus, setTaskStatus] = useState("pending");
  const [taskPage, setTaskPage] = useState(1);
  const [recordPage, setRecordPage] = useState(1);
  const tasks = useApi(`/api/v1/inspection-tasks?status=${taskStatus}&limit=${inspectionPageSize}&offset=${(taskPage - 1) * inspectionPageSize}`);
  const taskSummary = useApi("/api/v1/inspection-tasks/summary");
  const records = useApi(`/api/v1/inspection-records?limit=${inspectionPageSize}&offset=${(recordPage - 1) * inspectionPageSize}`);
  const definitions = useApi("/api/v1/inspection-definitions");
  const [entryOpen, setEntryOpen] = useState(false);
  const [reviewOpen, setReviewOpen] = useState(false);
  const [taskTarget, setTaskTarget] = useState(null);
  const [reviewTarget, setReviewTarget] = useState(null);
  const [reviewHistory, setReviewHistory] = useState([]);
  const [reviewLoading, setReviewLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState("");
  const [form, setForm] = useState({ workpieceId: "", operationRunId: "", definitionKey: "", outcome: "PASS", notes: "", measurements: {}, file: null });
  const [review, setReview] = useState({ decision: "CONFIRMED", notes: "" });
  const definitionRows = extractRows(definitions.data);
  const selectedDefinition = definitionRows.find(item => `${item.code}:${item.version}` === form.definitionKey);
  const requiredCharacteristics = (selectedDefinition?.characteristics || []).filter(item => item.required);
  const measurementsComplete = requiredCharacteristics.every(item => {
    const value = form.measurements[item.code];
    return value !== undefined && value !== null && value !== "";
  });
  const requiresAttachment = Boolean(taskTarget?.requiredInspections?.find(
    item => `${item.definitionCode}:${item.definitionVersion}` === form.definitionKey,
  )?.requiresAttachment);
  const entryReady = Boolean(
    form.workpieceId.trim() && form.operationRunId.trim() && selectedDefinition &&
    measurementsComplete && (!requiresAttachment || form.file),
  );
  const availableDefinitions = taskTarget
    ? definitionRows.filter(item => taskTarget.missingDefinitionCodes?.includes(item.code))
    : definitionRows;

  function openTask(task = null) {
    const firstDefinition = definitionRows.find(item => item.code === task?.missingDefinitionCodes?.[0]) || definitionRows[0];
    setTaskTarget(task);
    setForm({
      workpieceId: task?.workpieceId || "",
      operationRunId: task?.operationRunId || "",
      definitionKey: firstDefinition ? `${firstDefinition.code}:${firstDefinition.version}` : "",
      outcome: "PASS",
      notes: "",
      measurements: {},
      file: null,
    });
    setActionError("");
    setEntryOpen(true);
  }

  async function openTaskAction(task) {
    if (task.status === "review_pending" && task.visualInspectionRecordId) {
      setBusy(true);
      setActionError("");
      try {
        const record = await getJson(`/api/v1/inspection-records/${encodeURIComponent(task.visualInspectionRecordId)}`);
        await openReview(record);
      } catch (requestError) {
        setActionError(requestError.message);
      } finally {
        setBusy(false);
      }
      return;
    }
    openTask(task);
  }

  function updateMeasurement(code, value) {
    setForm(current => ({ ...current, measurements: { ...current.measurements, [code]: value } }));
  }

  async function submitRecord(event) {
    event.preventDefault();
    if (!selectedDefinition) return;
    setBusy(true);
    setActionError("");
    try {
      const attachments = [];
      if (form.file) {
        const upload = new FormData();
        upload.append("file", form.file);
        attachments.push(await postForm("/api/v1/inspection-attachments", upload));
      }
      const measurements = (selectedDefinition.characteristics || []).map(characteristic => {
        const raw = form.measurements[characteristic.code];
        if (raw === undefined || raw === null || raw === "") return null;
        const numeric = ["numeric", "number"].includes(characteristic.inputType);
        const numericValue = numeric ? Number(raw) : null;
        const outcome = numeric && Number.isFinite(numericValue)
          ? evaluateMeasurement(numericValue, characteristic)
          : form.outcome;
        return {
          characteristicCode: characteristic.code,
          outcome,
          numericValue: numeric ? numericValue : null,
          textValue: numeric ? null : String(raw),
          unit: numeric ? (characteristic.unit || "1") : null,
          lowerLimit: characteristic.lowerLimit ?? characteristic.minimum ?? null,
          upperLimit: characteristic.upperLimit ?? characteristic.maximum ?? null,
        };
      }).filter(Boolean);
      const now = new Date().toISOString();
      await postJson("/api/v1/inspection-records", {
        recordId: uuidv7(),
        workpieceId: form.workpieceId.trim(),
        operationRunId: form.operationRunId.trim(),
        definitionCode: selectedDefinition.code,
        definitionVersion: selectedDefinition.version,
        measuredAt: now,
        recordedAt: now,
        outcome: form.outcome,
        measurements,
        attachments,
        notes: form.notes.trim() || null,
      });
      setEntryOpen(false);
      await Promise.all([records.reload(), tasks.reload(), taskSummary.reload()]);
      notify("检测记录已保存；需要复核时会自动进入待复核队列。");
    } catch (requestError) {
      setActionError(requestError.message);
    } finally {
      setBusy(false);
    }
  }

  async function openReview(row) {
    setReviewTarget(row);
    setReview({ decision: "CONFIRMED", notes: "" });
    setReviewHistory([]);
    setActionError("");
    setReviewOpen(true);
    setReviewLoading(true);
    try {
      const value = await getJson(`/api/v1/inspection-reviews?inspectionRecordId=${encodeURIComponent(row.recordId)}&limit=200`);
      setReviewHistory(extractRows(value));
    } catch (requestError) {
      setActionError(requestError.message);
    } finally {
      setReviewLoading(false);
    }
  }

  async function submitReview(event) {
    event.preventDefault();
    setBusy(true);
    setActionError("");
    try {
      await postJson("/api/v1/inspection-reviews", {
        reviewId: uuidv7(),
        inspectionRecordId: reviewTarget.recordId,
        decision: review.decision,
        notes: review.notes.trim() || null,
      });
      setReviewOpen(false);
      await Promise.all([tasks.reload(), taskSummary.reload()]);
      notify(review.decision === "CONFIRMED" ? "复核已确认，质量任务已更新。" : "复核意见已保存，任务会按决定继续处理。");
    } catch (requestError) {
      setActionError(requestError.message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Page title="质量任务" description="从待办开始完成检测录入、独立复核和原图追溯。" actions={<Button onClick={() => openTask()}>补录检测记录</Button>}>
      {(tasks.error || taskSummary.error || records.error || definitions.error || (!entryOpen && !reviewOpen && actionError)) && <Alert tone="danger">{tasks.error || taskSummary.error || records.error || definitions.error || actionError}</Alert>}
      <WorkflowGuide
        title="质量任务怎么处理"
        description="正常情况下直接点击任务队列中的操作按钮；只有补录历史结果时才使用右上角“补录检测记录”。"
        steps={[
          { title: "选择待办任务", description: "平台已按生产周期生成需要处理的检测项目。", state: Number(taskSummary.data?.pending || 0) > 0 ? "current" : "done" },
          { title: "录入结果与附件", description: "检测值会按定义自动判定，原图与记录一起保存。", state: Number(taskSummary.data?.pending || 0) > 0 ? "current" : "done" },
          { title: "由另一人复核", description: "待复核任务进入独立队列，确认或要求重检。", state: Number(taskSummary.data?.reviewPending || 0) > 0 ? "current" : "done" },
        ]}
      />
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Metric label="需要处理" value={taskSummary.data?.actionRequired ?? "—"} hint="录入与复核合计" />
        <Metric label="待录入" value={taskSummary.data?.pending ?? "—"} />
        <Metric label="待复核" value={taskSummary.data?.reviewPending ?? "—"} />
        <Metric label="已完成" value={taskSummary.data?.completed ?? "—"} />
      </div>
      <TabGroup>
        <TabList className="flex w-fit gap-1 rounded-xl bg-slate-200/70 p-1">
          <Tab className="rounded-lg px-4 py-2 text-sm font-medium text-slate-600 outline-none data-selected:bg-white data-selected:text-blue-700 data-selected:shadow-sm">任务队列</Tab>
          <Tab className="rounded-lg px-4 py-2 text-sm font-medium text-slate-600 outline-none data-selected:bg-white data-selected:text-blue-700 data-selected:shadow-sm">检测记录</Tab>
        </TabList>
        <TabPanels className="mt-4">
          <TabPanel>
            <Card
              title="检测任务"
              description={`共 ${tasks.data?.total ?? extractRows(tasks.data).length} 条`}
              actions={(
                <Select
                  className="min-w-32"
                  aria-label="任务状态"
                  value={taskStatus}
                  onChange={event => {
                    setTaskStatus(event.target.value);
                    setTaskPage(1);
                  }}
                >
                  <option value="pending">待录入</option>
                  <option value="review_pending">待复核</option>
                  <option value="completed">已完成</option>
                  <option value="all">全部任务</option>
                </Select>
              )}
            >
              <DataTable
                rows={extractRows(tasks.data)}
                getRowKey={row => `${row.operationRunId}:${row.inspectionPlanId}:${row.inspectionPlanVersion}`}
                columns={[
                  { key: "operationRunId", label: "运行" },
                  { key: "workpieceId", label: "工件" },
                  { key: "inspectionPlanName", label: "质量方案" },
                  { key: "status", label: "状态", render: value => <StatusBadge value={value} /> },
                  { key: "completedAt", label: "周期完成", render: formatTime },
                  {
                    key: "_actions",
                    label: "操作",
                    render: (_value, row) => row.status === "completed"
                      ? <span className="text-sm text-slate-400">已完成</span>
                      : <Button variant="primary" disabled={busy} onClick={() => openTaskAction(row)}>
                        {row.status === "review_pending" ? "开始复核" : "录入检测"}
                      </Button>,
                  },
                ]}
              />
              <Pagination
                page={taskPage}
                pageSize={inspectionPageSize}
                total={tasks.data?.total ?? extractRows(tasks.data).length}
                onPageChange={setTaskPage}
              />
            </Card>
          </TabPanel>
          <TabPanel>
            <Card title="检测记录" description={`共 ${records.data?.total ?? extractRows(records.data).length} 条`}>
              <DataTable
                rows={extractRows(records.data)}
                keyField="recordId"
                columns={[
                  { key: "workpieceId", label: "工件" },
                  { key: "definitionCode", label: "检测定义" },
                  { key: "outcome", label: "结果", render: value => <StatusBadge value={value} /> },
                  { key: "measuredAt", label: "检测时间", render: formatTime },
                  { key: "attachments", label: "附件", render: value => `${value?.length ?? 0} 个` },
                  {
                    key: "_actions",
                    label: "操作",
                    render: (_value, row) => <Button variant="ghost" onClick={() => openReview(row)}>
                      {row.attachments?.length ? "查看与复核" : "查看详情"}
                    </Button>,
                  },
                ]}
              />
              <Pagination
                page={recordPage}
                pageSize={inspectionPageSize}
                total={records.data?.total ?? extractRows(records.data).length}
                onPageChange={setRecordPage}
              />
            </Card>
          </TabPanel>
        </TabPanels>
      </TabGroup>
      <Drawer
        open={entryOpen}
        onClose={() => setEntryOpen(false)}
        closeOnBackdrop={false}
        title="录入检测结果"
        description="检测值、判定规则和原始附件会作为同一条固定质量记录保存。"
        size="lg"
        footer={<><Button onClick={() => setEntryOpen(false)}>取消</Button><Button variant="primary" type="submit" form="inspection-entry" disabled={busy || !entryReady}>{busy ? "提交中" : "提交检测记录"}</Button></>}
      >
        {actionError && <Alert tone="danger">{actionError}</Alert>}
        <WorkflowGuide
          title="录入检测结果"
          steps={[
            { title: "确认工件与运行", description: "从任务进入时已自动带入。", state: form.workpieceId && form.operationRunId ? "done" : "current" },
            { title: "选择检测项目", description: "检测定义决定要填写的字段和判定规则。", state: selectedDefinition ? "done" : form.workpieceId && form.operationRunId ? "current" : "upcoming" },
            { title: "填写结果并提交", description: "完成必填项，按需要上传原始附件。", state: entryReady ? "current" : "upcoming" },
          ]}
        />
        <form id="inspection-entry" className="grid gap-5" onSubmit={submitRecord}>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="工件编号"><Input required value={form.workpieceId} readOnly={Boolean(taskTarget)} onChange={event => setForm({ ...form, workpieceId: event.target.value })} /></Field>
            <Field label="运行编号"><Input required value={form.operationRunId} readOnly={Boolean(taskTarget)} onChange={event => setForm({ ...form, operationRunId: event.target.value })} /></Field>
          </div>
          <Field label="检测定义">
            <Select required value={form.definitionKey} onChange={event => setForm({ ...form, definitionKey: event.target.value, measurements: {} })}>
              <option value="">选择定义</option>
              {availableDefinitions.map(item => <option key={`${item.code}:${item.version}`} value={`${item.code}:${item.version}`}>{item.name || item.code} · v{item.version}</option>)}
            </Select>
          </Field>
          {(selectedDefinition?.characteristics || []).map(characteristic => (
            <Field key={characteristic.code} label={`${characteristic.name || characteristic.code}${characteristic.unit ? `（${characteristic.unit}）` : ""}`} hint={characteristic.required ? "必填" : "可选"}>
              {characteristic.inputType === "select" ? (
                <Select required={characteristic.required} value={form.measurements[characteristic.code] ?? ""} onChange={event => updateMeasurement(characteristic.code, event.target.value)}>
                  <option value="">请选择</option>
                  {(characteristic.allowedValues || []).map(value => <option key={value} value={value}>{value}</option>)}
                </Select>
              ) : characteristic.inputType === "boolean" ? (
                <Select required={characteristic.required} value={form.measurements[characteristic.code] ?? ""} onChange={event => updateMeasurement(characteristic.code, event.target.value)}>
                  <option value="">请选择</option><option value="true">是</option><option value="false">否</option>
                </Select>
              ) : (
                <Input required={characteristic.required} type={["numeric", "number"].includes(characteristic.inputType) ? "number" : "text"} step="any" value={form.measurements[characteristic.code] ?? ""} onChange={event => updateMeasurement(characteristic.code, event.target.value)} />
              )}
            </Field>
          ))}
          <Alert title="结果由检测值自动判定">平台会依据检测定义中的范围和规则计算总体结果。</Alert>
          <Field
            label="原始附件"
            hint={requiresAttachment
              ? "当前检测项目必须上传原始附件。"
              : "支持平台允许的图片或文件格式。"}
          >
            <Input
              type="file"
              required={requiresAttachment}
              onChange={event => setForm({ ...form, file: event.target.files?.[0] || null })}
            />
          </Field>
          <Field label="备注"><Textarea value={form.notes} onChange={event => setForm({ ...form, notes: event.target.value })} /></Field>
        </form>
      </Drawer>
      <Drawer
        open={reviewOpen}
        onClose={() => setReviewOpen(false)}
        title="检测详情与质量复核"
        description={reviewTarget ? `检测记录 ${reviewTarget.recordId}` : ""}
        size="xl"
        footer={(
          <>
            <Button onClick={() => setReviewOpen(false)}>关闭</Button>
            {reviewTarget?.attachments?.length > 0 && <Button variant="primary" type="submit" form="inspection-review" disabled={busy}>提交复核</Button>}
          </>
        )}
      >
        {actionError && <Alert tone="danger">{actionError}</Alert>}
        {reviewTarget && (
          <form id="inspection-review" className="grid gap-5" onSubmit={submitReview}>
            <div className="grid gap-3 sm:grid-cols-3">
              <Metric label="检测结果" value={<StatusBadge value={reviewTarget.outcome} />} />
              <Metric label="检测时间" value={formatTime(reviewTarget.measuredAt)} />
              <Metric label="检测人员" value={reviewTarget.submittedBy || "—"} />
            </div>
            <Card title="检测对象">
              <div className="grid gap-3 text-sm sm:grid-cols-2">
                <p><span className="text-slate-500">工件：</span>{reviewTarget.workpieceId}</p>
                <p><span className="text-slate-500">运行：</span>{reviewTarget.operationRunId}</p>
                <p><span className="text-slate-500">检测定义：</span>{reviewTarget.definitionCode} · v{reviewTarget.definitionVersion}</p>
                <p><span className="text-slate-500">记录时间：</span>{formatTime(reviewTarget.recordedAt)}</p>
              </div>
            </Card>
            <Card title="测量结果">
              <DataTable
                rows={reviewTarget.measurements || []}
                keyField="characteristicCode"
                columns={[
                  { key: "characteristicCode", label: "检测特性" },
                  { key: "numericValue", label: "测量值", render: (value, row) => value ?? row.textValue ?? "—" },
                  { key: "unit", label: "单位" },
                  { key: "lowerLimit", label: "下限" },
                  { key: "upperLimit", label: "上限" },
                  { key: "outcome", label: "判定", render: value => <StatusBadge value={value} /> },
                ]}
              />
            </Card>
            <Card title="原始附件" description={`${reviewTarget.attachments?.length ?? 0} 个附件`}>
              {reviewTarget.attachments?.length ? (
                <div className="grid gap-4 sm:grid-cols-2">
                  {reviewTarget.attachments.map(attachment => {
                    const contentUrl = `/api/v1/inspection-attachments/${encodeURIComponent(attachment.attachmentId)}/content`;
                    return (
                      <article key={attachment.attachmentId} className="overflow-hidden rounded-xl border border-slate-200">
                        {attachment.mediaType?.startsWith("image/") && (
                          <a href={contentUrl} target="_blank" rel="noreferrer">
                            <img src={contentUrl} alt={attachment.fileName} className="max-h-72 w-full bg-slate-50 object-contain" />
                          </a>
                        )}
                        <div className="p-3 text-sm">
                          <p className="font-medium text-slate-900">{attachment.fileName}</p>
                          <p className="mt-1 text-xs text-slate-500">{Math.ceil(attachment.sizeBytes / 1024)} KiB</p>
                          <a className="mt-2 inline-flex text-sm font-medium text-blue-600 hover:text-blue-700" href={contentUrl} target="_blank" rel="noreferrer">查看原始文件</a>
                        </div>
                      </article>
                    );
                  })}
                </div>
              ) : <EmptyState title="没有原始附件" description="该记录不能执行视觉复核，只能查看检测详情。" />}
            </Card>
            {(reviewTarget.instrument || reviewTarget.notes) && (
              <Card title="补充信息">
                {reviewTarget.instrument && (
                  <div className="grid gap-2 text-sm sm:grid-cols-2">
                    <p><span className="text-slate-500">检测仪器：</span>{reviewTarget.instrument.instrumentId}</p>
                    <p><span className="text-slate-500">型号：</span>{reviewTarget.instrument.model || "—"}</p>
                    <p><span className="text-slate-500">校准记录：</span>{reviewTarget.instrument.calibrationRef || "—"}</p>
                    <p><span className="text-slate-500">校准有效期：</span>{formatTime(reviewTarget.instrument.calibrationValidUntil)}</p>
                  </div>
                )}
                {reviewTarget.notes && <p className="mt-3 whitespace-pre-wrap text-sm text-slate-700">{reviewTarget.notes}</p>}
              </Card>
            )}
            <Card title="复核历史">
              {reviewLoading ? <p className="text-sm text-slate-500">正在读取复核历史…</p> : reviewHistory.length ? (
                <DataTable
                  rows={reviewHistory}
                  keyField="reviewId"
                  columns={[
                    { key: "reviewedAt", label: "时间", render: formatTime },
                    { key: "reviewedBy", label: "复核人" },
                    { key: "decision", label: "决定", render: value => <StatusBadge value={value} /> },
                    { key: "notes", label: "说明" },
                  ]}
                />
              ) : <EmptyState title="尚无复核记录" description="完成复核后会保留完整历史。" />}
            </Card>
            {reviewTarget.attachments?.length > 0 && (
              <Card title="提交复核">
                <div className="grid gap-4">
                  <Field label="复核决定"><Select value={review.decision} onChange={event => setReview({ ...review, decision: event.target.value })}><option value="CONFIRMED">确认</option><option value="REJECTED">驳回</option><option value="REINSPECTION_REQUIRED">要求重检</option></Select></Field>
                  <Field label="复核说明"><Textarea value={review.notes} onChange={event => setReview({ ...review, notes: event.target.value })} /></Field>
                </div>
              </Card>
            )}
          </form>
        )}
      </Drawer>
    </Page>
  );
}

function evaluateMeasurement(value, characteristic) {
  const minimum = characteristic.lowerLimit ?? characteristic.minimum;
  const maximum = characteristic.upperLimit ?? characteristic.maximum;
  if (minimum !== null && minimum !== undefined && value < Number(minimum)) return "FAIL";
  if (maximum !== null && maximum !== undefined && value > Number(maximum)) return "FAIL";
  return "PASS";
}

export function QualityAnalysisPage() {
  const [searchParams] = useSearchParams();
  const [filters, setFilters] = useState({
    productSeries: "",
    subjectType: searchParams.get("subjectType") || "",
    subjectId: searchParams.get("subjectId") || "",
  });
  const [query, setQuery] = useState(() => {
    const params = new URLSearchParams({ limit: "1000", offset: "0" });
    if (searchParams.get("subjectType")) params.set("subjectType", searchParams.get("subjectType"));
    if (searchParams.get("subjectId")) params.set("subjectId", searchParams.get("subjectId"));
    return params.toString();
  });
  const { data, loading, error } = useApi(`/api/v1/quality-analysis?${query}`);
  const records = extractRows(data);
  const summary = records.reduce((result, row) => {
    const outcome = String(row.outcome || "INCONCLUSIVE").toUpperCase();
    if (outcome === "PASS") result.pass += 1;
    else if (outcome === "FAIL") result.fail += 1;
    else result.inconclusive += 1;
    result.attachments += Number(row.attachmentCount || 0);
    return result;
  }, { pass: 0, fail: 0, inconclusive: 0, attachments: 0 });
  const productGroups = groupQuality(records, row => row.productSeries || "未关联产品系列");
  const recipeGroups = groupQuality(records, row => [row.recipeId, row.recipeVersion ? `v${row.recipeVersion}` : ""].filter(Boolean).join(" · ") || "未关联配方");
  const chartLayout = useMemo(() => ({
    barmode: "stack",
    hovermode: "x unified",
    xaxis: { type: "category", tickangle: -24 },
    yaxis: { title: { text: "检测记录数" }, rangemode: "tozero" },
  }), []);

  function search(event) {
    event.preventDefault();
    const params = new URLSearchParams({ limit: "1000", offset: "0" });
    Object.entries(filters).forEach(([key, value]) => value.trim() && params.set(key, value.trim()));
    setQuery(params.toString());
  }

  return (
    <Page title="质量分析" description="按产品和生产上下文查看质量结果、合格率与原始附件覆盖。">
      <Card title="分析范围">
        <form className="grid gap-3 md:grid-cols-[1fr_1fr_1fr_auto]" onSubmit={search}>
          <Field label="产品系列"><Input value={filters.productSeries} onChange={event => setFilters({ ...filters, productSeries: event.target.value })} /></Field>
          <Field label="对象类型"><Input value={filters.subjectType} onChange={event => setFilters({ ...filters, subjectType: event.target.value })} /></Field>
          <Field label="对象 ID"><Input value={filters.subjectId} onChange={event => setFilters({ ...filters, subjectId: event.target.value })} /></Field>
          <Button className="self-end" variant="primary" type="submit"><MagnifyingGlassIcon className="size-4" />分析</Button>
        </form>
      </Card>
      {error && <Alert tone="danger">{error}</Alert>}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5">
        <Metric label="有效检测记录" value={records.length} />
        <Metric label="合格" value={summary.pass} hint={ratio(summary.pass, records.length)} />
        <Metric label="不合格" value={summary.fail} hint={ratio(summary.fail, records.length)} />
        <Metric label="待确认" value={summary.inconclusive} hint={ratio(summary.inconclusive, records.length)} />
        <Metric label="原始附件" value={summary.attachments} />
      </div>
      {loading && !data ? <LoadingCard /> : (
        <>
          <div className="grid gap-5 xl:grid-cols-2">
            <Card title="按产品系列">
              <PlotlyChart traces={qualityOutcomeTraces(productGroups.slice(0, 12))} layout={chartLayout} height={300} />
              <DataTable rows={productGroups} keyField="name" columns={[
                { key: "name", label: "产品系列" }, { key: "total", label: "检测" },
                { key: "pass", label: "合格" }, { key: "fail", label: "不合格" },
              ]} />
            </Card>
            <Card title="按配方版本">
              <PlotlyChart traces={qualityOutcomeTraces(recipeGroups.slice(0, 12))} layout={chartLayout} height={300} />
              <DataTable rows={recipeGroups} keyField="name" columns={[
                { key: "name", label: "配方" }, { key: "total", label: "检测" },
                { key: "pass", label: "合格" }, { key: "fail", label: "不合格" },
              ]} />
            </Card>
          </div>
          <Card title="质量结果明细">
            <DataTable rows={records} keyField="recordId" columns={[
              { key: "measuredAt", label: "检测时间", render: formatTime },
              { key: "analysisScopeId", label: "分析范围" },
              { key: "subjectId", label: "运行对象" },
              { key: "productCode", label: "产品" },
              { key: "definitionCode", label: "检测定义" },
              { key: "outcome", label: "结果", render: value => <StatusBadge value={value} /> },
              { key: "attachmentCount", label: "附件" },
            ]} />
          </Card>
        </>
      )}
    </Page>
  );
}

function groupQuality(rows, keySelector) {
  const groups = new Map();
  for (const row of rows) {
    const name = keySelector(row);
    if (!groups.has(name)) groups.set(name, { name, total: 0, pass: 0, fail: 0, inconclusive: 0 });
    const group = groups.get(name);
    group.total += 1;
    const outcome = String(row.outcome || "INCONCLUSIVE").toLowerCase();
    if (outcome === "pass") group.pass += 1;
    else if (outcome === "fail") group.fail += 1;
    else group.inconclusive += 1;
  }
  return [...groups.values()].sort((left, right) => right.total - left.total || left.name.localeCompare(right.name));
}

function ratio(value, total) {
  return total ? `${Math.round(value / total * 100)}%` : "—";
}

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
      notify(`已将周期比较转为 ${created.length} 条候选假设；请补充验证标准后再让优化器设计实验。`);
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
      phaseLabel: signal.phaseName || "全周期",
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
  return (
    <Page title="周期对比与候选原因" description="从已完成的同类运行中选择基准和对比对象，按阶段对齐后形成待验证的原因假设。">
      {error && <Alert tone="danger">{error}</Alert>}
      <Card title="选择可比较的生产运行" description="先选择需要解释的异常运行；默认与同产品的完整样本组比较，避免从单个偶然样本得出结论。">
        <div className="mb-4 grid gap-3 md:grid-cols-[minmax(0,1fr)_auto] md:items-end">
          <Field label="筛选运行" hint="可按运行号、产品、设备或配方筛选；这是查找，不是录入周期编号。"><Input value={cycleFilter} onChange={event => setCycleFilter(event.target.value)} placeholder="例如：产品系列、设备编号或运行号" /></Field>
          <p className="pb-2 text-sm text-slate-500">显示 {visibleCycles.length} / {cycles.length} 条已完成运行</p>
        </div>
        <form className="grid gap-3 md:grid-cols-[1fr_1fr_1fr_auto]" onSubmit={compare}>
          <Field label="基准运行" hint="通常选择质量异常、规格偏离或需要解释的一次运行。"><Select value={baseline} onChange={event => setBaseline(event.target.value)} required disabled={catalogLoading || !cycles.length}><option value="">选择已完成运行</option>{baseline && !cycles.some(item => item.correlationId === baseline) && <option value={baseline}>{baseline}（来自当前页面链接）</option>}{visibleCycles.map(cycle => <option key={cycle.correlationId} value={cycle.correlationId}>{cycleLabel(cycle)}</option>)}</Select></Field>
          <Field label="对比范围" hint="历史样本组由服务端按产品、时间、质量和数据完整性筛选。"><Select value={comparisonScope} onChange={event => setComparisonScope(event.target.value)} disabled={!baseline}><option value="cohort">同产品历史样本组</option><option value="single">指定一个同类运行</option></Select></Field>
          {comparisonScope === "single" ? <Field label="对比运行" hint={baselineCycle?.productSeries ? `仅显示产品系列“${baselineCycle.productSeries}”的运行。` : baselineCycle ? `该运行未标注产品系列，暂按设备“${baselineCycle.machineId || "未标注"}”筛选。` : "正在读取基准运行。"}><Select value={candidate} onChange={event => setCandidate(event.target.value)} required disabled={!baselineCycle || catalogLoading}><option value="">选择同类运行</option>{comparableCycles.map(cycle => <option key={cycle.correlationId} value={cycle.correlationId}>{cycleLabel(cycle)}</option>)}</Select></Field> : <div className="self-end rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-sm text-slate-600">系统最多选择 24 个同产品历史运行，并保留质量覆盖和数据完整性证据。</div>}
          <Button variant="primary" type="submit" className="self-end" disabled={busy || !baseline || (comparisonScope === "single" && !candidate)}>{busy ? "正在对比…" : "开始周期对比"}</Button>
        </form>
        {catalogLoading && <p className="mt-3 text-sm text-slate-500">正在读取可比较的已完成运行…</p>}
        {!catalogLoading && cycles.length === 0 && <Alert tone="warning" title="暂无可选择的运行">需要至少两条已完成且上下文完整的生产运行，才能开始周期对比。</Alert>}
      </Card>
      {result ? (
        <>
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-5">
            <Metric label="产品系列" value={result.productSeries || "—"} />
            <Metric label="参与对比" value={result.acceptance?.cycleCount ?? comparedCycles.length} hint="个生产周期" />
            <Metric label="数据可用" value={result.acceptance?.availableCycleCount ?? 0} hint={`异常 ${result.acceptance?.degradedCycleCount ?? 0} 个`} />
            <Metric label="阶段完整" value={result.acceptance?.phaseCompleteCycleCount ?? 0} hint={`共 ${result.acceptance?.completeCycleCount ?? 0} 个完整周期`} />
            <Metric label="分析证据" value={evidenceLevelLabels[result.evidenceLevel] || result.evidenceLevel || "—"} />
          </div>
          <Card title="周期概况">
            <DataTable
              rows={comparedCycles}
              getRowKey={row => `${row.comparisonRole}-${row.correlationId}`}
              columns={[
                { key: "comparisonRole", label: "角色", render: value => <Badge tone={value === "基准" ? "info" : "neutral"}>{value}</Badge> },
                { key: "correlationId", label: "周期" },
                { key: "machineId", label: "设备" },
                { key: "completedAt", label: "结束时间", render: formatTime },
                { key: "durationMs", label: "时长（秒）", render: value => formatDecimal(Number(value) / 1000) },
                { key: "sampleCount", label: "样本数", render: formatInteger },
                { key: "phaseComplete", label: "阶段完整", render: value => value ? <Badge tone="success">完整</Badge> : <Badge tone="warning">不完整</Badge> },
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
            ) : <EmptyState title="尚无质量候选原因" description="至少需要合格与不合格周期，并且配方或过程特征具有可比较差异。" />}
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
            ) : <EmptyState title="暂无可比信号" description="所选周期还没有可用于阶段对比的信号特征。" />}
          </Card>
        </>
      ) : <EmptyState title="尚未执行周期对比" description="从下拉列表选择基准运行和同类对比运行后开始；系统会保留数据可用性和阶段完整性证据。" />}
    </Page>
  );
}

export function DataQualityPage() {
  const [params] = useSearchParams();
  const query = new URLSearchParams({ limit: "200" });
  if (params.get("subjectType")) query.set("subjectType", params.get("subjectType"));
  if (params.get("subjectId")) query.set("subjectId", params.get("subjectId"));
  return (
    <ResourcePage
      title="数据健康"
      description={params.get("subjectId")
        ? `检查对象 ${params.get("subjectId")} 的数据范围、采样连续性和周期完整性。`
        : "检查对象数据范围、采样连续性和周期完整性。"}
      endpoint={`/api/v1/data-objects?${query}`}
      getRowKey={row => `${row.subjectType}:${row.subjectId}`}
      columns={[
        { key: "subjectType", label: "对象类型", render: objectTypeLabel },
        { key: "subjectId", label: "对象" },
        { key: "sampleCount", label: "样本数" },
        { key: "maximumSampleGapSeconds", label: "最大采样间隔（秒）", render: value => value == null ? "—" : Number(value).toLocaleString("zh-CN") },
        { key: "lastSampleAt", label: "最后样本", render: formatTime },
      ]}
    />
  );
}

const registryPages = {
  processModels: {
    kind: "processModel",
    title: "工艺数据模型", description: "版本化管理数据项、参数和工艺阶段。", endpoint: "/api/v1/process-data-models", key: "modelId",
    columns: [["modelId", "模型"], ["version", "版本"], ["name", "名称"], ["status", "状态"], ["updatedAt", "更新时间"]],
    createLabel: "创建工艺数据模型",
    template: { modelId: "", version: 1, name: "", description: "", status: "draft", acquisition: { samplePeriodMs: 1000, stepSourceKey: null, dataItems: [] }, recipeParameters: [], stages: [], updatedAt: "" },
    deleteUrl: value => `/api/v1/process-data-models/${encodeURIComponent(value.modelId)}/${value.version}`,
  },
  recipes: {
    kind: "recipeVersion",
    title: "配方版本", description: "维护引用工艺数据模型的完整参数值。", endpoint: "/api/v1/recipe-versions", key: "recipeId",
    columns: [["recipeId", "配方"], ["version", "版本"], ["name", "名称"], ["status", "状态"], ["updatedAt", "更新时间"]],
    createLabel: "创建配方版本",
    template: { recipeId: "", version: 1, name: "", basedOnVersion: null, dataModelId: "", dataModelVersion: 1, status: "draft", contextSelector: {}, values: [], updatedAt: "" },
    deleteUrl: value => `/api/v1/recipe-versions/${encodeURIComponent(value.recipeId)}/${value.version}`,
  },
  plans: {
    kind: "analysisPlan",
    title: "分析方案", description: "配置分析范围、阶段对齐和质量分组。", endpoint: "/api/v1/process-analysis-plans", key: "planId",
    columns: [["planId", "方案"], ["version", "版本"], ["name", "名称"], ["status", "状态"], ["updatedAt", "更新时间"]],
    createLabel: "创建分析方案",
    template: { planId: "", version: 1, name: "", description: "", status: "draft", dataModelId: "", dataModelVersion: 1, analysisScope: "production-cycle", alignmentMode: "stage-relative", cohortDimension: "", comparisonKeys: ["product_series"], contextSelector: {}, signals: [], updatedAt: "" },
    deleteUrl: value => `/api/v1/process-analysis-plans/${encodeURIComponent(value.planId)}/${value.version}`,
  },
  definitions: {
    kind: "inspectionDefinition",
    title: "检测定义", description: "定义检测项目、录入方式、单位和判定范围。", endpoint: "/api/v1/inspection-definitions", key: "code",
    createLabel: "创建检测定义",
    columns: [["code", "代码"], ["version", "版本"], ["name", "名称"], ["characteristics", "录入类型"], ["updatedAt", "更新时间"]],
    render: { characteristics: inspectionInputTypes },
    template: { code: "", version: 1, name: "", description: "", characteristics: [] },
    deleteUrl: value => `/api/v1/inspection-definitions/${encodeURIComponent(value.code)}/${value.version}`,
  },
  plansQuality: {
    kind: "qualityPlan",
    title: "质量方案", description: "将检测定义组成适用于产品的版本化质量方案。", endpoint: "/api/v1/inspection-plans", key: "planId",
    columns: [["planId", "方案"], ["version", "版本"], ["name", "名称"], ["status", "状态"], ["updatedAt", "更新时间"]],
    createLabel: "创建质量方案",
    template: { planId: "", version: 1, name: "", description: "", status: "draft", priority: 0, effectiveFrom: null, effectiveTo: null, scope: {}, items: [], updatedAt: "" },
    deleteUrl: value => `/api/v1/inspection-plans/${encodeURIComponent(value.planId)}/${value.version}`,
  },
  acquisition: {
    kind: "acquisitionProfile",
    title: "数据源配置", description: "将现场设备或系统数据映射为带工艺语义的过程、配方和周期事件。", endpoint: "/api/v1/acquisition-profiles", key: "profileId",
    columns: [["subjectId", "数据源对象"], ["edgeId", "现场节点"], ["name", "数据源配置"], ["protocol", "接入协议"], ["status", "状态"]],
    render: { protocol: value => acquisitionProtocolLabels[value] || value },
    createLabel: "配置数据源",
    template: { profileId: "", version: 1, name: "", status: "draft", protocol: "http-polling", edgeId: "", dataModelId: "", dataModelVersion: 1, source: "", subjectType: "equipment", subjectId: "", valueMappings: [] },
    deleteUrl: value => `/api/v1/acquisition-profiles/${encodeURIComponent(value.profileId)}/${value.version}`,
  },
};

function RegistryPage({ definition }) {
  const { data, loading, error, reload } = useApi(definition.endpoint);
  const rows = extractRows(data);
  const [open, setOpen] = useState(false);
  const [mode, setMode] = useState("create");
  const [inspectionForm, setInspectionForm] = useState(() => inspectionDefinitionForm());
  const [businessForm, setBusinessForm] = useState(() => createRegistryBusinessForm(definition.kind));
  const [editorError, setEditorError] = useState("");
  const [saving, setSaving] = useState(false);
  const isInspectionDefinition = definition.kind === "inspectionDefinition";
  const hasBusinessEditor = Boolean(definition.kind) && !isInspectionDefinition;
  const inspectionValidation = isInspectionDefinition ? inspectionDefinitionValidation(inspectionForm) : "";
  const businessValidation = hasBusinessEditor ? registryBusinessValidation(definition.kind, businessForm) : "";
  const editorValidation = inspectionValidation || businessValidation;

  function openCreate() {
    setMode("create");
    if (isInspectionDefinition) {
      setInspectionForm(inspectionDefinitionForm());
    } else {
      setBusinessForm(createRegistryBusinessForm(definition.kind));
    }
    setEditorError("");
    setOpen(true);
  }
  function openMaintain(row) {
    setMode("maintain");
    if (isInspectionDefinition) {
      setInspectionForm(inspectionDefinitionForm(row));
    } else {
      setBusinessForm(createRegistryBusinessForm(definition.kind, row));
    }
    setEditorError("");
    setOpen(true);
  }

  function openNewVersion(row) {
    setMode("version");
    if (isInspectionDefinition) {
      setInspectionForm(inspectionDefinitionForm(row, Number(row.version || 0) + 1));
    } else {
      setBusinessForm(createRegistryBusinessForm(definition.kind, row, Number(row.version || 0) + 1));
    }
    setEditorError("");
    setOpen(true);
  }

  async function save() {
    setSaving(true);
    setEditorError("");
    try {
      const payload = isInspectionDefinition
        ? inspectionDefinitionPayload(inspectionForm)
        : registryBusinessPayload(definition.kind, businessForm);
      if (payload.updatedAt !== undefined) payload.updatedAt = new Date().toISOString();
      await postJson(definition.endpoint, payload);
      setOpen(false);
      await reload();
      notify(`${definition.title}已保存。`);
    } catch (saveError) {
      setEditorError(saveError.message);
    } finally {
      setSaving(false);
    }
  }

  async function retire(row) {
    try {
      await postJson(definition.endpoint, {
        ...row,
        status: "retired",
        effectiveTo: definition.kind === "qualityPlan" ? new Date().toISOString() : row.effectiveTo,
        updatedAt: row.updatedAt !== undefined ? new Date().toISOString() : undefined,
      });
      await reload();
      notify(`${definition.title}已停用，历史版本仍会保留。`);
    } catch (requestError) {
      setEditorError(requestError.message);
    }
  }

  async function remove(row) {
    if (!window.confirm(`确认删除草稿 ${row[definition.key]} v${row.version ?? 1}？此操作不可恢复。`)) return;
    try {
      await deleteJson(definition.deleteUrl(row));
      await reload();
    } catch (requestError) {
      setEditorError(requestError.message);
    }
  }

  const columns = [
    ...definition.columns.map(([key, label]) => ({
      key,
      label,
      render: definition.render?.[key] || (key === "status" ? value => <StatusBadge value={value} /> : key.endsWith("At") ? formatTime : undefined),
    })),
    {
      key: "_actions",
      label: "操作",
      render: (_value, row) => (
        <div className="flex min-w-max flex-wrap gap-1" onClick={event => event.stopPropagation()}>
          <Button variant="ghost" className="px-2" onClick={() => openMaintain(row)}>
            {isInspectionDefinition || (hasBusinessEditor && row.status !== "draft") ? "查看" : "维护"}
          </Button>
          <Button variant="ghost" className="px-2" onClick={() => openNewVersion(row)}>沿用为新版本</Button>
          {!isInspectionDefinition && row.status !== "retired" && <Button variant="ghost" className="px-2 text-amber-700" onClick={() => retire(row)}>停用</Button>}
          {!isInspectionDefinition && row.status === "draft" && <Button variant="ghost" className="px-2 text-rose-700" onClick={() => remove(row)}>删除草稿</Button>}
        </div>
      ),
    },
  ];
  const businessReadOnly = hasBusinessEditor && mode === "maintain" && businessForm.status !== "draft";
  const editorReadOnly = mode === "maintain" && (isInspectionDefinition || businessReadOnly);

  return (
    <Page
      title={definition.title}
      description={definition.description}
      actions={definition.kind === "acquisitionProfile"
        ? <><Link className="inline-flex min-h-9 items-center rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50" to="/edges">查看现场节点</Link><Button variant="primary" onClick={openCreate}>{definition.createLabel}</Button></>
        : <Button variant="primary" onClick={openCreate}>{definition.createLabel || "创建新版本"}</Button>}
    >
      {definition.kind === "acquisitionProfile" && (
        <Card title="设备接入进度" description="完成采集任务并发布后，设备会自动出现在“工业对象”和运行记录中。">
          <div className="grid gap-4 md:grid-cols-4">
            {[
              ["1", "现场节点在线", "确认设备所在节点能够正常上报心跳。", "/edges", "查看节点"],
              ["2", "选择数据模型", "数据模型决定要采集哪些工艺量。", "/configuration/process-data-models", "查看模型"],
              ["3", "配置并发布", "选择设备连接方式并映射实际数据项。", null, `${rows.filter(row => row.status === "published").length} 个已发布`],
              ["4", "确认数据到达", "在工业对象中确认设备、样本和最后活动时间。", "/explorer", "查看工业对象"],
            ].map(([number, title, text, path, action]) => (
              <div key={number} className="rounded-xl border border-slate-200 bg-slate-50 p-4">
                <div className="flex items-center gap-2">
                  <span className="grid size-7 place-items-center rounded-full bg-blue-600 text-xs font-semibold text-white">{number}</span>
                  <h3 className="font-semibold text-slate-800">{title}</h3>
                </div>
                <p className="mt-3 text-sm leading-6 text-slate-500">{text}</p>
                {path
                  ? <Link className="mt-3 inline-block text-sm font-medium text-blue-600 hover:text-blue-700" to={path}>{action}</Link>
                  : <p className="mt-3 text-sm font-medium text-slate-700">{action}</p>}
              </div>
            ))}
          </div>
        </Card>
      )}
      {definition.kind === "inspectionDefinition" && (
        <WorkflowGuide
          title="先定义检测内容，再组成质量方案"
          steps={[
            { title: "创建检测定义", description: "设置要填写的检测项、单位、上下限或选项。", state: rows.length ? "done" : "current" },
            { title: "加入质量方案", description: "决定哪些产品需要使用这些检测项目。", state: rows.length ? "current" : "upcoming" },
            { title: "按任务录入", description: "生产周期完成后，平台自动生成质量待办。", state: "upcoming" },
          ]}
        />
      )}
      {definition.kind === "qualityPlan" && (
        <WorkflowGuide
          title="质量方案决定什么时候检测什么"
          steps={[
            { title: "准备检测定义", description: "先确认需要的检测项目已经建立。", state: rows.length ? "done" : "current" },
            { title: "配置产品适用范围", description: "选择检测定义并设置原图、复核等要求。", state: rows.length ? "done" : "current" },
            { title: "发布后自动生成任务", description: "新生产周期会按适用范围进入质量队列。", state: rows.some(row => row.status === "published") ? "done" : "upcoming" },
          ]}
        />
      )}
      {(error || (!open && editorError)) && <Alert tone="danger">{error || editorError}</Alert>}
      {loading && !data ? <LoadingCard /> : (
        <Card title={`${definition.title}列表`} description={`共 ${data?.total ?? rows.length} 条记录`}>
          <DataTable
            rows={rows}
            keyField={definition.key}
            getRowKey={row => `${row[definition.key]}:${row.version ?? 1}`}
            columns={columns}
          />
        </Card>
      )}
      <Drawer
        open={open}
        onClose={() => setOpen(false)}
        closeOnBackdrop={false}
        title={mode === "create"
          ? `创建${definition.title}`
          : mode === "version" ? "沿用为新版本"
            : editorReadOnly ? `查看${definition.title}` : `维护${definition.title}`}
        description={isInspectionDefinition
          ? mode === "maintain" ? "查看该版本的基本信息和检测特性。" : "填写基本信息并配置一个或多个检测特性。"
          : hasBusinessEditor
            ? editorReadOnly ? "查看该版本的业务配置。" : "按业务字段完成配置，保存前会检查必填项和引用。"
          : "编辑完整版本内容。保存前会由平台执行结构、引用与状态校验。"}
        footer={editorReadOnly
          ? <Button onClick={() => setOpen(false)}>关闭</Button>
          : <><Button onClick={() => setOpen(false)}>取消</Button><Button variant="primary" onClick={save} disabled={saving || Boolean(editorValidation)}>{saving ? "保存中" : "保存"}</Button></>}
        size="xl"
      >
        {editorError && <Alert tone="danger">{editorError}</Alert>}
        {isInspectionDefinition ? (
          <InspectionDefinitionEditor
            form={inspectionForm}
            onChange={setInspectionForm}
            readOnly={mode === "maintain"}
            validation={inspectionValidation}
            lockIdentity={mode !== "create"}
          />
        ) : (
          <RegistryBusinessEditor
            kind={definition.kind}
            form={businessForm}
            onChange={setBusinessForm}
            readOnly={editorReadOnly}
            validation={businessValidation}
            lockIdentity={mode !== "create"}
          />
        )}
      </Drawer>
    </Page>
  );
}

function InspectionDefinitionEditor({ form, onChange, readOnly, validation, lockIdentity }) {
  function update(field, value) {
    onChange({ ...form, [field]: value });
  }

  function updateCharacteristic(index, field, value) {
    onChange({
      ...form,
      characteristics: form.characteristics.map((characteristic, characteristicIndex) =>
        characteristicIndex === index ? { ...characteristic, [field]: value } : characteristic),
    });
  }

  function addCharacteristic() {
    onChange({ ...form, characteristics: [...form.characteristics, emptyInspectionCharacteristic()] });
  }

  function removeCharacteristic(index) {
    onChange({ ...form, characteristics: form.characteristics.filter((_item, characteristicIndex) => characteristicIndex !== index) });
  }

  return (
    <div className="grid gap-5">
      {!readOnly && validation && <Alert tone="warning">{validation}</Alert>}
      <div className="grid gap-4 md:grid-cols-2">
        <Field label="定义代码" hint="使用小写字母开头的点分格式，例如 hardness.final。">
          <Input required value={form.code} disabled={readOnly || lockIdentity} onChange={event => update("code", event.target.value)} placeholder="hardness.final" />
        </Field>
        <Field label="版本">
          <Input required type="number" min="1" step="1" value={form.version} disabled={readOnly || lockIdentity} onChange={event => update("version", event.target.value)} />
        </Field>
        <Field label="定义名称">
          <Input required value={form.name} disabled={readOnly} onChange={event => update("name", event.target.value)} placeholder="成品硬度检测" />
        </Field>
        <Field label="说明" className="md:col-span-2">
          <Textarea className="min-h-20" value={form.description} disabled={readOnly} onChange={event => update("description", event.target.value)} placeholder="说明检测场景和目的" />
        </Field>
      </div>

      <div className="flex items-center justify-between gap-3">
        <div>
          <h3 className="font-semibold text-slate-900">检测特性</h3>
          <p className="mt-1 text-sm text-slate-500">每个特性对应一次具体录入，例如硬度、外观结论或是否合格。</p>
        </div>
        {!readOnly && <Button onClick={addCharacteristic}>添加检测特性</Button>}
      </div>

      <div className="grid gap-4">
        {form.characteristics.map((characteristic, index) => (
          <Card
            key={index}
            title={`检测特性 ${index + 1}`}
            actions={!readOnly && form.characteristics.length > 1
              ? <Button variant="ghost" className="text-rose-700" onClick={() => removeCharacteristic(index)}>移除</Button>
              : undefined}
          >
            <div className="grid gap-4 md:grid-cols-2">
              <Field label="特性代码" hint="同一定义内不可重复。">
                <Input required value={characteristic.code} disabled={readOnly} onChange={event => updateCharacteristic(index, "code", event.target.value)} placeholder="hardness.hrc" />
              </Field>
              <Field label="特性名称">
                <Input required value={characteristic.name} disabled={readOnly} onChange={event => updateCharacteristic(index, "name", event.target.value)} placeholder="洛氏硬度" />
              </Field>
              <Field label="录入类型">
                <Select value={characteristic.inputType} disabled={readOnly} onChange={event => updateCharacteristic(index, "inputType", event.target.value)}>
                  <option value="numeric">数值</option>
                  <option value="text">文本</option>
                  <option value="select">选项</option>
                  <option value="boolean">是/否</option>
                </Select>
              </Field>
              {characteristic.inputType === "numeric" && (
                <Field label="单位">
                  <Input value={characteristic.unit} disabled={readOnly} onChange={event => updateCharacteristic(index, "unit", event.target.value)} placeholder="例如 HRC、mm、℃" />
                </Field>
              )}
              {characteristic.inputType === "numeric" && (
                <>
                  <Field label="下限" hint="不限制可留空。">
                    <Input type="number" step="any" value={characteristic.lowerLimit} disabled={readOnly} onChange={event => updateCharacteristic(index, "lowerLimit", event.target.value)} />
                  </Field>
                  <Field label="上限" hint="不限制可留空。">
                    <Input type="number" step="any" value={characteristic.upperLimit} disabled={readOnly} onChange={event => updateCharacteristic(index, "upperLimit", event.target.value)} />
                  </Field>
                </>
              )}
              {characteristic.inputType === "select" && (
                <Field label="可选值" hint="每行填写一个选项。" className="md:col-span-2">
                  <Textarea value={characteristic.allowedValuesText} disabled={readOnly} onChange={event => updateCharacteristic(index, "allowedValuesText", event.target.value)} placeholder={"合格\n不合格"} />
                </Field>
              )}
              <label className="flex items-center gap-2 text-sm font-medium text-slate-700 md:col-span-2">
                <input type="checkbox" checked={characteristic.required} disabled={readOnly} onChange={event => updateCharacteristic(index, "required", event.target.checked)} />
                必须录入
              </label>
            </div>
          </Card>
        ))}
      </div>
    </div>
  );
}

export const ProcessDataModelsPage = () => <RegistryPage definition={registryPages.processModels} />;
export const RecipeVersionsPage = () => <RegistryPage definition={registryPages.recipes} />;
export const ProcessAnalysisPlansPage = () => <RegistryPage definition={registryPages.plans} />;
export const InspectionDefinitionsPage = () => <RegistryPage definition={registryPages.definitions} />;
export const QualityPlansPage = () => <RegistryPage definition={registryPages.plansQuality} />;
export const AcquisitionProfilesPage = () => <RegistryPage definition={registryPages.acquisition} />;

export function EdgesPage() {
  const { data, loading, error } = useApi("/api/edges", { interval: 10000 });
  const rows = extractRows(data);
  const online = rows.filter(row => edgeStatus(row) === "online").length;
  return (
    <Page
      title="现场节点"
      description="查看部署在现场、负责连接设备并上报数据的节点。"
      actions={<Link className="inline-flex min-h-9 items-center rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700" to="/configuration/acquisition-profiles">配置数据源</Link>}
    >
      {error && <Alert tone="danger" title="现场节点暂不可用">{error}</Alert>}
      <div className="grid gap-4 sm:grid-cols-3">
        <Metric label="现场节点" value={rows.length} hint="已登记" />
        <Metric label="当前在线" value={online} hint="30 秒内有心跳" />
        <Metric label="需要处理" value={rows.filter(row => edgeStatus(row) !== "online").length} hint="离线或运行异常" />
      </div>
      {loading && !data ? <LoadingCard /> : (
        <Card title="节点状态" description="节点在线后即可承载一个或多个数据源采集任务。">
          <DataTable
            rows={rows}
            keyField="edgeId"
            columns={[
              { key: "edgeId", label: "节点" },
              { key: "hostname", label: "名称" },
              { key: "_status", label: "状态", render: (_value, row) => <StatusBadge value={edgeStatus(row)} /> },
              { key: "lastSeen", label: "最后心跳", render: formatTime },
              { key: "lastError", label: "最近问题", render: value => value || "无" },
              { key: "version", label: "版本" },
              { key: "_action", label: "操作", render: (_value, row) => <Link className="font-medium text-blue-600 hover:text-blue-700" to={`/edges/${encodeURIComponent(row.edgeId)}`}>查看诊断</Link> },
            ]}
          />
        </Card>
      )}
    </Page>
  );
}

export function EdgeDetailPage() {
  const { edgeId = "" } = useParams();
  const encodedId = encodeURIComponent(edgeId);
  const edges = useApi("/api/edges", { interval: 10000 });
  const acquisition = useApi(`/api/edges/${encodedId}/acquisition/status`, { interval: 5000 });
  const metrics = useApi(`/api/edges/${encodedId}/metrics/json`, { interval: 10000 });
  const logs = useApi(`/api/edges/${encodedId}/logs?page=1&pageSize=50`, { interval: 10000 });
  const profiles = useApi("/api/v1/acquisition-profiles", { interval: 10000 });
  const edge = extractRows(edges.data).find(row => row.edgeId === edgeId);
  const tasks = acquisition.data?.tasks || [];
  const edgeProfiles = extractRows(profiles.data).filter(profile => profile.edgeId === edgeId);
  const profilesByTaskKey = new Map(edgeProfiles.map(profile => [`${profile.profileId}@${profile.version}`, profile]));
  const taskRows = tasks.map(task => ({ ...task, profile: profilesByTaskKey.get(task.configurationKey) }));
  const runningTasks = tasks.filter(task => task.state === "running").length;
  const publishedProfiles = edgeProfiles.filter(profile => profile.status === "published");
  const processSignalCount = publishedProfiles.reduce((total, profile) => total + (profile.valueMappings?.length || 0), 0);
  const recipeMappingCount = publishedProfiles.reduce((total, profile) => total + (profile.recipe?.parameterMappings?.length || 0), 0);
  const lifecycleProfileCount = publishedProfiles.filter(profile => profile.lifecycle).length;
  const allTaskProfilesResolved = tasks.length > 0 && taskRows.every(task => task.profile);
  const error = edges.error || acquisition.error || metrics.error || logs.error || profiles.error;
  const outboxBacklog = metricTotal(metrics.data, "event_outbox_backlog");
  const shipped = metricTotal(metrics.data, "event_shipped_total");
  const emitted = metricTotal(metrics.data, "event_emitted_total");
  const recentLogs = extractRows(logs.data);
  const deliveryReady = runningTasks > 0 && processSignalCount > 0 && recipeMappingCount > 0 && lifecycleProfileCount > 0 && outboxBacklog === 0;

  return (
    <Page
      title={edge?.hostname || edgeId || "数据源节点"}
      description="确认现场数据是否已从设备连接、采集和上行，交付为可用于工艺追因与优化的过程证据。"
      actions={(
        <>
          <Link className="inline-flex min-h-9 items-center rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50" to="/edges">返回现场节点</Link>
          <Link className="inline-flex min-h-9 items-center rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700" to="/configuration/acquisition-profiles">配置数据源</Link>
        </>
      )}
    >
      {error && <Alert tone="danger" title="部分诊断信息暂不可用">{error}</Alert>}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Metric label="设备连接" value={<StatusBadge value={edgeStatus(edge)} />} hint={edge?.lastSeen ? `最后心跳 ${formatTime(edge.lastSeen)}` : "尚未收到心跳"} />
        <Metric label="采集任务" value={<StatusBadge value={acquisition.data?.state || "unknown"} />} hint={`${runningTasks} 个运行中 / ${tasks.length} 个已加载`} />
        <Metric label="数据上行" value={outboxBacklog > 0 ? `${formatInteger(outboxBacklog)} 待处理` : "已同步"} hint={`已确认 ${formatInteger(shipped)} / 已产生 ${formatInteger(emitted)}`} />
        <Metric label="工艺建模" value={recipeMappingCount > 0 ? "配方已映射" : "待映射"} hint={`${processSignalCount} 条过程信号 · ${recipeMappingCount} 个配方参数`} />
      </div>
      {(edge?.lastError || acquisition.data?.lastError || outboxBacklog > 0) ? (
        <Alert tone="warning" title="节点需要关注">
          <ul className="list-disc space-y-1 pl-5">
            {edge?.lastError && <li>{edge.lastError}</li>}
            {acquisition.data?.lastError && <li>{acquisition.data.lastError}</li>}
            {outboxBacklog > 0 && <li>仍有 {formatInteger(outboxBacklog)} 条事件等待上行。</li>}
          </ul>
        </Alert>
      ) : !deliveryReady ? (
        <Alert tone="warning" title="数据源尚未具备工艺闭环条件">
          <ul className="list-disc space-y-1 pl-5">
            {runningTasks === 0 && <li>尚无运行中的采集任务，请先发布并下发数据源配置。</li>}
            {processSignalCount === 0 && <li>尚未映射过程信号，无法形成可分析的过程曲线。</li>}
            {recipeMappingCount === 0 && <li>尚未回读实际配方参数，无法区分真实执行条件。</li>}
            {lifecycleProfileCount === 0 && <li>尚未映射周期边界，连续数据无法自动归属到一次运行。</li>}
          </ul>
        </Alert>
      ) : <Alert tone="success" title="采集端已具备交付条件">过程信号、实际配方、周期边界与数据上行均已就绪；请继续确认质检结果已关联到相同运行。</Alert>}
      <WorkflowGuide
        title="从设备数据到工艺证据"
        description="节点只负责可靠交付数据；完整闭环还必须在平台把周期、实际配方、过程曲线与质量结果关联起来。"
        compact
        steps={[
          { title: "连接数据源", description: edgeStatus(edge) === "online" ? "现场节点持续在线。" : "等待节点恢复心跳。", state: edgeStatus(edge) === "online" ? "done" : "current" },
          { title: "采集并上行", description: runningTasks > 0 ? `${runningTasks} 个任务正在采集，${outboxBacklog > 0 ? `${formatInteger(outboxBacklog)} 条事件等待上行。` : "当前没有积压事件。"}` : "尚无运行中的采集任务。", state: runningTasks > 0 && outboxBacklog === 0 ? "done" : "current" },
          { title: "映射工艺语义", description: `${processSignalCount} 条过程信号、${recipeMappingCount} 个配方参数${lifecycleProfileCount > 0 ? "，已配置周期边界。" : "；尚未配置周期边界。"}`, state: processSignalCount > 0 && recipeMappingCount > 0 && lifecycleProfileCount > 0 ? "done" : "current" },
          { title: "验证闭环证据", description: deliveryReady ? "采集端条件已具备；请在周期与质检页面确认实际关联，再进入追因和实验。" : "补齐当前步骤后，再用周期与质检数据验证证据是否完整。", state: deliveryReady ? "current" : "upcoming" },
        ]}
      />
      <Card
        title="数据源交付情况"
        description="这里显示已发布配置所承诺的工艺语义，不把节点运行指标误当成已经完成的质量或追因结论。"
        actions={<Link className="text-sm font-medium text-blue-600 hover:text-blue-700" to="/configuration/acquisition-profiles">查看数据源配置</Link>}
      >
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <Metric label="已发布数据源" value={publishedProfiles.length} hint={allTaskProfilesResolved ? "运行任务已关联配置版本" : tasks.length ? "有运行任务尚未匹配配置版本" : "尚未加载运行任务"} />
          <Metric label="过程信号映射" value={processSignalCount} hint="用于形成过程曲线和特征" />
          <Metric label="配方参数回读" value={recipeMappingCount} hint={recipeMappingCount ? "用于区分实际执行条件" : "追因与优化需要实际配方回读"} />
          <Metric label="周期边界映射" value={lifecycleProfileCount} hint={lifecycleProfileCount ? "可生成离散运行周期" : "连续数据尚不能自动形成周期"} />
        </div>
        <p className="mt-5 rounded-xl bg-slate-50 px-4 py-3 text-sm leading-6 text-slate-600">
          {deliveryReady
            ? "采集端已满足过程信号、实际配方与周期边界的交付条件。下一步在“周期”和“质检”中确认同一运行的曲线与结果已关联，随后再发起追因或优化实验。"
            : "这不是追因结论。请先补齐运行任务、过程信号、实际配方回读和周期边界；质量结果由质检流程关联后，才形成可用于追因和优化的完整证据。"}
        </p>
      </Card>
      <Card title="运行中的数据源" description="每行对应一份已下发到节点的不可变数据源配置版本。">
        <DataTable
          rows={taskRows}
          keyField="configurationKey"
          columns={[
            { key: "profile", label: "数据源", render: (_value, row) => row.profile ? <div><p className="font-medium text-slate-900">{row.profile.name}</p><p className="text-xs text-slate-500">{objectTypeLabel(row.profile.subjectType)} · {row.profile.subjectId}</p></div> : <span className="text-slate-500">{row.configurationKey}</span> },
            { key: "_protocol", label: "接入协议", render: (_value, row) => row.profile ? acquisitionProtocolLabels[row.profile.protocol] || row.profile.protocol : "配置未匹配" },
            { key: "_coverage", label: "采集内容", render: (_value, row) => row.profile ? `${row.profile.valueMappings?.length || 0} 信号 · ${row.profile.recipe?.parameterMappings?.length || 0} 配方参数${row.profile.lifecycle ? " · 周期" : ""}` : "—" },
            { key: "state", label: "状态", render: value => <StatusBadge value={value} /> },
            { key: "samplesCollected", label: "已采样", render: formatInteger },
            { key: "observedIntervalMs", label: "实际间隔", render: value => formatDuration(value) },
            { key: "lastReadDurationMs", label: "最近读取耗时", render: value => formatDuration(value) },
            { key: "lastSuccessAt", label: "最近成功", render: formatTime },
            { key: "lastError", label: "最近问题", render: value => value || "无" },
          ]}
        />
      </Card>
      <Card title="节点诊断日志" description={`仅在排查连接、协议或上行问题时使用 · 最近 ${recentLogs.length} 条 / 共 ${logs.data?.total ?? recentLogs.length} 条`}>
        <DataTable
          rows={recentLogs}
          getRowKey={(row, index) => `${row.timestamp}:${index}`}
          columns={[
            { key: "timestamp", label: "时间", render: formatTime },
            { key: "level", label: "级别", render: value => <Badge tone={["error", "fatal"].includes(String(value).toLowerCase()) ? "danger" : String(value).toLowerCase() === "warning" ? "warning" : "neutral"}>{value}</Badge> },
            { key: "message", label: "内容" },
            { key: "source", label: "来源", render: value => String(value || "—").replaceAll("\"", "") },
          ]}
        />
      </Card>
    </Page>
  );
}

const platformRoleOptions = [
  ["platform.admin", "平台管理员", "用户、权限和系统配置"],
  ["quality.inspector", "质量检验员", "质量结果录入"],
  ["quality.reviewer", "质量复核员", "质量结果复核"],
  ["process.engineer", "工艺工程师", "分析、调查、模型和改进建议"],
];

export function UsersPage() {
  const { data, loading, error, reload } = useApi("/api/v1/users");
  const [createOpen, setCreateOpen] = useState(false);
  const [manageOpen, setManageOpen] = useState(false);
  const [selected, setSelected] = useState(null);
  const [createForm, setCreateForm] = useState({ username: "", displayName: "", password: "", roles: ["quality.inspector"] });
  const [roles, setRoles] = useState([]);
  const [password, setPassword] = useState("");
  const [actionError, setActionError] = useState("");
  const [busy, setBusy] = useState(false);

  function startCreate() {
    setCreateForm({ username: "", displayName: "", password: "", roles: ["quality.inspector"] });
    setActionError("");
    setCreateOpen(true);
  }

  function startManage(user) {
    setSelected(user);
    setRoles(user.roles || []);
    setPassword("");
    setActionError("");
    setManageOpen(true);
  }

  function toggleRole(role, enabled, target = "manage") {
    if (target === "create") {
      setCreateForm(current => ({
        ...current,
        roles: enabled ? [...current.roles, role] : current.roles.filter(value => value !== role),
      }));
      return;
    }
    setRoles(current => enabled ? [...current, role] : current.filter(value => value !== role));
  }

  async function runAction(action) {
    setBusy(true);
    setActionError("");
    try {
      await action();
      await reload();
      return true;
    } catch (requestError) {
      setActionError(requestError.message);
      return false;
    } finally {
      setBusy(false);
    }
  }

  async function createUser() {
    const saved = await runAction(() => postJson("/api/v1/users", createForm));
    if (saved) {
      setCreateOpen(false);
      notify(`用户 ${createForm.displayName || createForm.username} 已创建。`);
    }
  }

  async function saveRoles() {
    const saved = await runAction(() => postJson(`/api/v1/users/${encodeURIComponent(selected.userId)}:set-roles`, { roles }));
    if (saved) notify("岗位权限已更新。");
  }

  async function savePassword() {
    const saved = await runAction(() => postJson(`/api/v1/users/${encodeURIComponent(selected.userId)}:set-password`, { password }));
    if (saved) {
      setPassword("");
      notify("密码已更新，该用户的其他会话已退出。");
    }
  }

  async function changeDisabled() {
    const saved = await runAction(() => postJson(
      `/api/v1/users/${encodeURIComponent(selected.userId)}:set-disabled`,
      { disabled: !selected.disabled },
    ));
    if (saved) {
      setSelected(current => ({ ...current, disabled: !current.disabled }));
      notify(selected.disabled ? "账户已恢复。" : "账户已停用。");
    }
  }

  const users = extractRows(data);
  return (
    <Page
      title="用户与权限"
      description="管理员创建本地账户，并按岗位分配最小必要权限。"
      actions={<Button variant="primary" onClick={startCreate}>创建用户</Button>}
    >
      <Alert tone="info" title="岗位分权">
        质量录入与复核应由不同人员承担；配置、生产操作和工艺改进也建议使用独立账户。
      </Alert>
      {error && <Alert tone="danger" title="用户列表不可用">{error}</Alert>}
      {loading && !data ? <LoadingCard /> : (
        <Card title="平台用户" description={`共 ${users.length} 个账户`}>
          {users.length ? (
            <DataTable
              rows={users}
              keyField="userId"
              onRowClick={startManage}
              columns={[
                { key: "username", label: "用户名" },
                { key: "displayName", label: "姓名", render: value => value || "—" },
                { key: "roles", label: "岗位权限", render: value => (value || []).map(role => platformRoleOptions.find(option => option[0] === role)?.[1] || role).join("、") || "未分配" },
                { key: "disabled", label: "状态", render: value => <StatusBadge value={value ? "disabled" : "active"} /> },
                { key: "createdAt", label: "创建时间", render: formatTime },
                { key: "_action", label: "操作", render: (_value, row) => <Button variant="ghost" onClick={event => { event.stopPropagation(); startManage(row); }}>管理</Button> },
              ]}
            />
          ) : <EmptyState title="还没有本地账户" description="创建首个岗位账户后，可用于生产环境登录。" />}
        </Card>
      )}

      <Drawer
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        closeOnBackdrop={false}
        title="创建用户"
        description="用户名创建后不可修改；密码至少 8 位。"
        footer={<><Button onClick={() => setCreateOpen(false)}>取消</Button><Button variant="primary" disabled={busy || !createForm.username.trim() || createForm.password.length < 8 || createForm.roles.length === 0} onClick={createUser}>{busy ? "创建中" : "创建"}</Button></>}
      >
        {actionError && <Alert tone="danger">{actionError}</Alert>}
        <div className="grid gap-4">
          <Field label="用户名"><Input required autoComplete="off" value={createForm.username} onChange={event => setCreateForm({ ...createForm, username: event.target.value })} /></Field>
          <Field label="姓名"><Input value={createForm.displayName} onChange={event => setCreateForm({ ...createForm, displayName: event.target.value })} /></Field>
          <Field label="初始密码" hint="至少 8 位"><Input required type="password" autoComplete="new-password" value={createForm.password} onChange={event => setCreateForm({ ...createForm, password: event.target.value })} /></Field>
          <RoleSelector value={createForm.roles} onChange={(role, enabled) => toggleRole(role, enabled, "create")} />
        </div>
      </Drawer>

      <Drawer
        open={manageOpen}
        onClose={() => setManageOpen(false)}
        closeOnBackdrop={false}
        title={selected ? `管理用户 · ${selected.displayName || selected.username}` : "管理用户"}
        description="角色和密码变更立即生效；修改密码会注销该用户的其他会话。"
        footer={<Button onClick={() => setManageOpen(false)}>关闭</Button>}
      >
        {actionError && <Alert tone="danger">{actionError}</Alert>}
        {selected && (
          <div className="grid gap-5">
            <Card title="账户状态">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div><p className="font-medium">{selected.username}</p><p className="mt-1 text-sm text-slate-500">{selected.disabled ? "该账户目前不能登录。" : "该账户可以正常登录。"}</p></div>
                <Button variant={selected.disabled ? "primary" : "danger"} disabled={busy} onClick={changeDisabled}>{selected.disabled ? "恢复账户" : "停用账户"}</Button>
              </div>
            </Card>
            <Card title="岗位权限">
              <div className="grid gap-4">
                <RoleSelector value={roles} onChange={(role, enabled) => toggleRole(role, enabled)} />
                <div><Button variant="primary" disabled={busy || roles.length === 0} onClick={saveRoles}>保存权限</Button></div>
              </div>
            </Card>
            <Card title="重置密码" description="新密码至少 8 位。">
              <div className="grid gap-3 sm:grid-cols-[1fr_auto] sm:items-end">
                <Field label="新密码"><Input type="password" autoComplete="new-password" value={password} onChange={event => setPassword(event.target.value)} /></Field>
                <Button variant="primary" disabled={busy || password.length < 8} onClick={savePassword}>更新密码</Button>
              </div>
            </Card>
          </div>
        )}
      </Drawer>
    </Page>
  );
}

function RoleSelector({ value, onChange }) {
  return (
    <fieldset>
      <legend className="text-sm font-medium text-slate-700">岗位权限</legend>
      <div className="mt-2 grid gap-2">
        {platformRoleOptions.map(([role, label, description]) => (
          <label key={role} className="flex cursor-pointer gap-3 rounded-xl border border-slate-200 p-3 hover:bg-slate-50">
            <input type="checkbox" className="mt-1" checked={value.includes(role)} onChange={event => onChange(role, event.target.checked)} />
            <span><span className="block text-sm font-medium text-slate-800">{label}</span><span className="mt-0.5 block text-xs text-slate-500">{description}</span></span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}

export function MetricsPage() {
  const edgeResponse = useApi("/api/edges", { interval: 10000 });
  const metricResponse = useApi("/api/metrics-data?names=event_ingest_total,process_start_time_seconds,process_working_set_bytes,system_runtime_dotnet_thread_pool_queue_length", { interval: 30000 });
  const cycleResponse = useApi("/api/v1/cycles?limit=1", { interval: 10000 });
  const qualityResponse = useApi("/api/v1/inspection-tasks/summary", { interval: 10000 });
  const profileResponse = useApi("/api/v1/acquisition-profiles", { interval: 10000 });
  const rows = extractRows(edgeResponse.data);
  const online = rows.filter(row => edgeStatus(row) === "online").length;
  const offline = rows.filter(row => edgeStatus(row) === "offline").length;
  const unknown = Math.max(0, rows.length - online - offline);
  const metrics = metricResponse.data;
  const ingested = metricTotal(metrics, "event_ingest_total");
  const startedAtSeconds = metricTotal(metrics, "process_start_time_seconds");
  const uptime = startedAtSeconds ? Date.now() - startedAtSeconds * 1000 : null;
  const memory = metricTotal(metrics, "process_working_set_bytes");
  const threadQueue = metricTotal(metrics, "system_runtime_dotnet_thread_pool_queue_length");
  const publishedProfiles = extractRows(profileResponse.data).filter(row => row.status === "published").length;
  const actionRequired = qualityResponse.data?.actionRequired ?? 0;
  const error = edgeResponse.error || metricResponse.error || cycleResponse.error || qualityResponse.error || profileResponse.error;
  const healthy = offline === 0 && unknown === 0 && threadQueue === 0;
  return (
    <Page title="平台运行状态" description="从业务处理、设备采集和平台资源三个层面确认系统是否正常。">
      {error && <Alert tone="danger">{error}</Alert>}
      <Alert tone={healthy ? "success" : "warning"} title={healthy ? "平台运行正常" : "平台存在需要关注的项目"}>
        {healthy ? "中心服务和现场节点均在正常工作。" : `离线节点 ${offline} 个，待确认节点 ${unknown} 个，后台排队 ${formatInteger(threadQueue)} 项。`}
      </Alert>
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Metric label="已保存周期" value={formatInteger(cycleResponse.data?.total)} hint="可追溯生产运行" />
        <Metric label="待处理质量任务" value={formatInteger(actionRequired)} hint="录入与复核合计" />
        <Metric label="已发布采集任务" value={formatInteger(publishedProfiles)} hint="正在向现场下发" />
        <Metric label="已摄入事件" value={formatInteger(ingested)} hint="本次平台运行累计" />
      </div>
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Metric label="平台运行时间" value={uptime == null ? "—" : formatDuration(uptime)} />
        <Metric label="当前内存" value={formatBytes(memory)} />
        <Metric label="后台排队" value={formatInteger(threadQueue)} />
        <Metric label="现场节点在线" value={`${online}/${rows.length}`} hint={`${offline} 个离线`} />
      </div>
      <Card title="现场节点" description="点击诊断可查看采集任务、上行积压和最近日志。">
        <DataTable rows={rows} keyField="edgeId" columns={[
          { key: "edgeId", label: "节点" },
          { key: "_status", label: "状态", render: (_value, row) => <StatusBadge value={edgeStatus(row)} /> },
          { key: "lastSeen", label: "最后心跳", render: formatTime },
          { key: "lastError", label: "最近问题", render: value => value || "无" },
          { key: "_action", label: "操作", render: (_value, row) => <Link className="font-medium text-blue-600 hover:text-blue-700" to={`/edges/${encodeURIComponent(row.edgeId)}`}>查看诊断</Link> },
        ]} />
      </Card>
    </Page>
  );
}

export function SubscriptionsPage() {
  const { data, loading, error, reload } = useApi("/api/v1/subscriptions", { interval: 10000 });
  const rows = extractRows(data);
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState(null);
  const [saving, setSaving] = useState(false);
  const [actionError, setActionError] = useState("");
  const [form, setForm] = useState(emptySubscription());

  function startCreate() {
    setEditing(null);
    setForm(emptySubscription());
    setActionError("");
    setOpen(true);
  }

  function startEdit(row) {
    setEditing(row);
    setForm({
      name: row.name || "",
      endpoint: row.endpoint || "",
      eventTypes: (row.eventTypes || []).join(", "),
      subjectType: row.subjectType || "",
      subjectId: row.subjectId || "",
      contextPairs: Object.entries(row.context || {}).map(([key, value]) => ({ key, value })),
      secret: "",
      clearSecret: false,
      startMode: "new",
    });
    setActionError("");
    setOpen(true);
  }

  function update(key, value) {
    setForm(current => ({ ...current, [key]: value }));
  }

  async function save(event) {
    event.preventDefault();
    setSaving(true);
    setActionError("");
    try {
      const payload = {
        name: form.name.trim(),
        endpoint: form.endpoint.trim(),
        eventTypes: form.eventTypes.split(",").map(value => value.trim()).filter(Boolean),
        subjectType: form.subjectType.trim() || null,
        subjectId: form.subjectId.trim() || null,
        context: Object.fromEntries(form.contextPairs
          .filter(item => item.key.trim() && item.value.trim())
          .map(item => [item.key.trim(), item.value.trim()])),
        secret: form.secret || null,
        clearSecret: form.secret ? false : form.clearSecret,
        startAfterIngestId: form.startMode === "history" ? 0 : null,
      };
      if (editing) await putJson(`/api/v1/subscriptions/${editing.subscriptionId}`, payload);
      else await postJson("/api/v1/subscriptions", payload);
      setOpen(false);
      await reload();
    } catch (requestError) {
      setActionError(requestError.message);
    } finally {
      setSaving(false);
    }
  }

  async function toggle(row) {
    try {
      await putJson(`/api/v1/subscriptions/${row.subscriptionId}/enabled`, { enabled: !row.enabled });
      await reload();
    } catch (requestError) {
      setActionError(requestError.message);
    }
  }

  async function remove(row) {
    if (!window.confirm(`删除订阅“${row.name}”后将停止投递，是否继续？`)) return;
    try {
      await deleteJson(`/api/v1/subscriptions/${row.subscriptionId}`);
      await reload();
    } catch (requestError) {
      setActionError(requestError.message);
    }
  }

  const summary = {
    enabled: rows.filter(row => row.enabled).length,
    failed: rows.filter(row => row.enabled && row.lastError).length,
    signed: rows.filter(row => row.hasSecret).length,
  };

  return (
    <Page title="事件订阅" description="维护向 MES、QMS 或其他外部系统投递的 CloudEvents。" actions={<Button variant="primary" onClick={startCreate}>新建订阅</Button>}>
      {(error || actionError) && <Alert tone="danger">{error || actionError}</Alert>}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Metric label="订阅总数" value={rows.length} />
        <Metric label="已启用" value={summary.enabled} />
        <Metric label="投递异常" value={summary.failed} />
        <Metric label="签名保护" value={summary.signed} />
      </div>
      {loading && !data ? <LoadingCard /> : (
        <Card title="订阅与投递">
          <DataTable
            rows={rows}
            keyField="subscriptionId"
            columns={[
              { key: "name", label: "名称" },
              { key: "endpoint", label: "接收地址" },
              { key: "eventTypes", label: "事件范围", render: value => value?.length ? value.join("、") : "全部事件" },
              { key: "enabled", label: "状态", render: (value, row) => <StatusBadge value={!value ? "disabled" : row.lastError ? "failed" : row.lastSuccessAt ? "active" : "pending"} /> },
              {
                key: "_actions",
                label: "操作",
                render: (_value, row) => (
                  <div className="flex min-w-max gap-1">
                    <Button variant="ghost" className="px-2" onClick={() => startEdit(row)}>编辑</Button>
                    <Button variant="ghost" className="px-2" onClick={() => toggle(row)}>{row.enabled ? "停用" : "启用"}</Button>
                    <Button variant="ghost" className="px-2 text-rose-700" onClick={() => remove(row)}>删除</Button>
                  </div>
                ),
              },
            ]}
          />
        </Card>
      )}
      <Drawer
        open={open}
        onClose={() => setOpen(false)}
        closeOnBackdrop={false}
        title={editing ? "编辑事件订阅" : "新建事件订阅"}
        description="事件类型、对象和上下文条件之间为同时满足关系。"
        footer={<><Button onClick={() => setOpen(false)}>取消</Button><Button variant="primary" type="submit" form="subscription-form" disabled={saving}>{saving ? "保存中" : editing ? "保存修改" : "创建订阅"}</Button></>}
      >
        {actionError && <Alert tone="danger">{actionError}</Alert>}
        <form id="subscription-form" className="grid gap-5" onSubmit={save}>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="名称"><Input value={form.name} onChange={event => update("name", event.target.value)} required placeholder="质量系统事件接收" /></Field>
            <Field label="接收地址"><Input value={form.endpoint} onChange={event => update("endpoint", event.target.value)} required type="url" placeholder="https://example.com/ingot/events" /></Field>
          </div>
          <Field label="签名密钥" hint={editing?.hasSecret ? "留空保留现有密钥。" : "可选，用于 HMAC-SHA256 验签。"}><Input value={form.secret} onChange={event => update("secret", event.target.value)} type="password" autoComplete="new-password" /></Field>
          {editing?.hasSecret && (
            <label className="flex items-center gap-2 text-sm text-slate-700"><input type="checkbox" checked={form.clearSecret} disabled={Boolean(form.secret)} onChange={event => update("clearSecret", event.target.checked)} />清除现有签名密钥</label>
          )}
          <Field label="事件类型" hint="多个类型用英文逗号分隔；留空表示全部事件。"><Input value={form.eventTypes} onChange={event => update("eventTypes", event.target.value)} placeholder="quality.inspection.completed, alarm.raised" /></Field>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="对象类型"><Input value={form.subjectType} onChange={event => update("subjectType", event.target.value)} placeholder="留空表示全部" /></Field>
            <Field label="对象 ID"><Input value={form.subjectId} onChange={event => update("subjectId", event.target.value)} placeholder="留空表示全部" /></Field>
          </div>
          <SubscriptionContextFields value={form.contextPairs} onChange={value => update("contextPairs", value)} />
          {!editing && <Field label="首次启用"><Select value={form.startMode} onChange={event => update("startMode", event.target.value)}><option value="new">仅投递创建后的新事件</option><option value="history">从最早记录开始回放</option></Select></Field>}
        </form>
      </Drawer>
    </Page>
  );
}

function SubscriptionContextFields({ value, onChange }) {
  const rows = value.length ? value : [{ key: "", value: "" }];
  function update(index, field, nextValue) {
    const source = value.length ? value : [{ key: "", value: "" }];
    onChange(source.map((item, rowIndex) => rowIndex === index ? { ...item, [field]: nextValue } : item));
  }
  return (
    <Card
      title="上下文过滤"
      description="例如只投递指定设备或产品系列的事件。"
      actions={<Button onClick={() => onChange([...value, { key: "", value: "" }])}>添加条件</Button>}
    >
      <div className="grid gap-2">
        {rows.map((item, index) => (
          <div key={index} className="grid gap-2 sm:grid-cols-[1fr_1fr_auto]">
            <Input aria-label={`过滤字段 ${index + 1}`} value={item.key} placeholder="例如 machine_id" onChange={event => update(index, "key", event.target.value)} />
            <Input aria-label={`过滤内容 ${index + 1}`} value={item.value} placeholder="例如 PRESS-01" onChange={event => update(index, "value", event.target.value)} />
            {value.length > 0 && <Button variant="ghost" className="text-rose-700" onClick={() => onChange(value.filter((_item, rowIndex) => rowIndex !== index))}>移除</Button>}
          </div>
        ))}
      </div>
    </Card>
  );
}

function emptySubscription() {
  return {
    name: "",
    endpoint: "",
    eventTypes: "",
    subjectType: "",
    subjectId: "",
    contextPairs: [],
    secret: "",
    clearSecret: false,
    startMode: "new",
  };
}

export function LogsPage() {
  const { data: edges } = useApi("/api/edges");
  const edgeRows = extractRows(edges);
  const [edgeId, setEdgeId] = useState("");
  const [level, setLevel] = useState("");
  const endpoint = edgeId ? `/api/edges/${encodeURIComponent(edgeId)}/logs?pageSize=200${level ? `&level=${level}` : ""}` : null;
  const logs = useApi(endpoint, { enabled: Boolean(edgeId), interval: 5000 });
  return (
    <Page title="运行日志" description="按边缘节点和级别查询结构化运行记录。">
      <Card title="查询条件">
        <div className="grid gap-3 md:grid-cols-2">
          <Field label="边缘节点"><Select value={edgeId} onChange={event => setEdgeId(event.target.value)}><option value="">选择节点</option>{edgeRows.map(row => <option key={row.edgeId} value={row.edgeId}>{row.edgeId}</option>)}</Select></Field>
          <Field label="级别"><Select value={level} onChange={event => setLevel(event.target.value)}><option value="">全部</option><option value="Information">信息</option><option value="Warning">警告</option><option value="Error">错误</option></Select></Field>
        </div>
      </Card>
      {logs.error && <Alert tone="danger">{logs.error}</Alert>}
      <Card title="日志记录">
        {edgeId
          ? logs.loading && !logs.data
            ? <div className="inline-flex items-center gap-2 text-sm text-slate-500"><ArrowPathIcon className="size-4 animate-spin" />正在读取日志</div>
            : <DataTable
              rows={extractRows(logs.data)}
              getRowKey={(row, index) => `${row.timestamp}:${row.source}:${index}`}
              columns={[
                { key: "timestamp", label: "时间", render: formatTime },
                { key: "level", label: "级别", render: value => <StatusBadge value={value} /> },
                { key: "source", label: "来源", render: value => String(value || "—").replace(/^"|"$/g, "") },
                { key: "message", label: "消息" },
              ]}
            />
          : <EmptyState title="请选择边缘节点" description="选择后日志会自动加载并持续更新。" />}
      </Card>
    </Page>
  );
}

export function NotFoundPage() {
  return (
    <div className="grid min-h-[60vh] place-items-center text-center">
      <div>
        <p className="text-sm font-semibold text-blue-600">404</p>
        <h1 className="mt-2 text-3xl font-semibold text-slate-950">页面不存在</h1>
        <p className="mt-2 text-slate-500">地址可能已经变更，回到工作台继续。</p>
        <Link to="/workbench" className="mt-6 inline-flex rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white">返回工作台</Link>
      </div>
    </div>
  );
}

function uuidv7() {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  const timestamp = BigInt(Date.now());
  bytes[0] = Number((timestamp >> 40n) & 0xffn);
  bytes[1] = Number((timestamp >> 32n) & 0xffn);
  bytes[2] = Number((timestamp >> 24n) & 0xffn);
  bytes[3] = Number((timestamp >> 16n) & 0xffn);
  bytes[4] = Number((timestamp >> 8n) & 0xffn);
  bytes[5] = Number(timestamp & 0xffn);
  bytes[6] = (bytes[6] & 0x0f) | 0x70;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = [...bytes].map(value => value.toString(16).padStart(2, "0")).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
