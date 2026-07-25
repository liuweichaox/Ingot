import { Tab, TabGroup, TabList, TabPanel, TabPanels } from "@headlessui/react";
import {
  ArrowPathIcon,
  MagnifyingGlassIcon,
  PaperAirplaneIcon,
} from "@heroicons/react/24/outline";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useLocation, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { deleteJson, getJson, postForm, postJson, putJson, streamSse } from "../api/http";
import { qualityOutcomeTraces } from "../charts/chartAdapters";
import { BusinessObjectEditor } from "../components/BusinessObjectEditor";
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

const formatTime = value => value ? new Date(value).toLocaleString("zh-CN") : "—";
const formatInteger = value => Number.isFinite(Number(value)) ? Number(value).toLocaleString("zh-CN") : "—";
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
  });
  useEffect(() => {
    let alive = true;
    Promise.all([
      getJson("/api/v1/cycles?limit=8"),
      getJson("/api/v1/inspection-tasks/summary"),
      getJson("/api/v1/events?limit=20"),
      getJson("/api/edges"),
      getJson("/api/v1/production-contexts"),
    ]).then(([cycles, summary, events, edges, contexts]) => {
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
  const dailyActions = [
    {
      title: pendingInspections ? `处理 ${pendingInspections} 个质量待办` : "质量任务已处理",
      description: pendingInspections ? "优先完成检测录入和复核。" : "当前没有待录入或待复核任务。",
      to: "/inspections",
      tone: pendingInspections ? "border-amber-200 bg-amber-50" : "border-emerald-200 bg-emerald-50",
      action: pendingInspections ? "去处理" : "查看记录",
    },
    {
      title: activeContexts ? `${activeContexts} 台设备已配置生产` : "配置接下来的生产",
      description: activeContexts ? "确认设备、产品、配方和工装是否正确。" : "生产开始前先启用设备生产配置。",
      to: "/production/changeover",
      tone: activeContexts ? "border-blue-200 bg-blue-50" : "border-amber-200 bg-amber-50",
      action: activeContexts ? "检查配置" : "开始配置",
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
    <Page title="工作台" description="从生产运行、质量待办和采集状态开始今天的工作。">
      {state.error && <Alert tone="danger">{state.error}</Alert>}
      {state.loading ? <LoadingCard /> : (
        <>
          <Card title="今天先做这些" description="从需要处理的事项开始，不必逐个模块查找。">
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
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <Metric label="生产运行" value={state.cycleTotal} hint={`${activeCycles} 个正在进行`} />
            <Metric label="待处理质检" value={pendingInspections} hint="来自当前质量任务" />
            <Metric label="采集节点" value={`${onlineEdges}/${state.edges.length}`} hint="在线 / 全部" />
            <Metric label="有效生产配置" value={activeContexts} hint="当前设备上下文" />
          </div>
          <div className="grid gap-5 xl:grid-cols-[1.3fr_.7fr]">
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
        </>
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
  const eventResponse = useApi(`/api/v1/events?correlationId=${encodedId}&limit=30`);
  const inspectionResponse = useApi(`/api/v1/inspection-records?operationRunId=${encodedId}&limit=50`);
  const cycle = extractRows(cycleResponse.data)[0];
  const events = extractRows(eventResponse.data);
  const inspections = extractRows(inspectionResponse.data);
  const loading = cycleResponse.loading || eventResponse.loading || inspectionResponse.loading;
  const error = cycleResponse.error || eventResponse.error || inspectionResponse.error;
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

          <div className="grid gap-5 xl:grid-cols-[1.1fr_.9fr]">
            <Card
              title="质量记录"
              description={inspections.length ? `已关联 ${inspections.length} 条检测记录` : "尚未产生与本周期关联的检测记录"}
              actions={<Link className="text-sm font-medium text-blue-600 hover:text-blue-700" to="/inspections">进入质量任务</Link>}
            >
              {inspectionResponse.loading && !inspectionResponse.data ? <LoadingCard /> : inspections.length ? (
                <DataTable
                  rows={inspections}
                  keyField="id"
                  columns={[
                    { key: "inspectionDefinitionName", label: "检测项目" },
                    { key: "overallOutcome", label: "判定", render: value => <StatusBadge value={value} /> },
                    { key: "inspectedAt", label: "检测时间", render: formatTime },
                    { key: "attachments", label: "附件", render: value => `${value?.length || 0} 个` },
                  ]}
                />
              ) : <EmptyState title="暂无质量记录" description="完成质量任务后，检测结果会自动归集到本周期。" />}
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
  const [filters, setFilters] = useState({ type: "", edgeId: "", correlationId: urlParams.get("cycleId") || "" });
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
        <form className="grid gap-3 md:grid-cols-[1fr_1fr_1fr_auto]" onSubmit={event => { event.preventDefault(); setLive(false); setAppliedFilters(filters); setPage(1); setQuery(makeEventQuery(filters, 1, pageSize)); }}>
          <Field label="事件类型"><Input value={filters.type} onChange={event => setFilters({ ...filters, type: event.target.value })} placeholder="process.sample" /></Field>
          <Field label="采集节点"><Input value={filters.edgeId} onChange={event => setFilters({ ...filters, edgeId: event.target.value })} /></Field>
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
      const created = await postJson("/api/v1/chat/runs", { question: question.trim(), pageContext: null, mode });
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
    <Page title="AI 助手" description="用自然语言查询和分析已经保存的生产数据。">
      {capabilitiesLoading && <Alert title="正在连接 AI 助手">正在读取可用的分析能力。</Alert>}
      {!capabilitiesLoading && capabilities && !capabilities.enabled && <Alert tone="warning" title="AI 助手当前未启用">请联系管理员启用分析服务。</Alert>}
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
  const location = useLocation();
  const objects = useApi("/api/v1/data-objects?limit=500");
  const rows = extractRows(objects.data);
  const [query, setQuery] = useState("");
  const searchInput = useRef(null);
  const filtered = useMemo(() => rows.filter(row => JSON.stringify(row).toLowerCase().includes(query.toLowerCase())), [query, rows]);
  useEffect(() => {
    if (location.state?.focusSearch) searchInput.current?.focus();
  }, [location.state]);
  return (
    <Page
      title="设备与对象"
      description="查找已经接入平台、并持续上报生产数据的设备和业务对象。"
      actions={<Link className="inline-flex min-h-9 items-center rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700" to="/configuration/acquisition-profiles">接入设备</Link>}
    >
      {objects.error && <Alert tone="danger" title="设备与对象暂不可用">{objects.error}</Alert>}
      <WorkflowGuide
        title="设备为什么会出现在这里"
        description="这里不需要手工创建目录；设备开始上报数据后，平台会自动建立可追溯对象。"
        steps={[
          { title: "接入设备", description: "选择现场节点、通信方式和设备地址。", state: rows.length ? "done" : "current" },
          { title: "开始采集", description: "现场节点读取数据并持续上报。", state: rows.some(row => Number(row.sampleCount) > 0) ? "done" : rows.length ? "current" : "upcoming" },
          { title: "自动形成对象", description: "可在这里搜索设备、周期和其他业务对象。", state: rows.length ? "done" : "upcoming" },
        ]}
      />
      <Card>
        <Field label="搜索设备与对象"><Input ref={searchInput} value={query} onChange={event => setQuery(event.target.value)} placeholder="输入设备编号、对象编号或现场节点" /></Field>
      </Card>
      {objects.loading && !objects.data ? <LoadingCard /> : (
        <Card
          title="已发现的设备与对象"
          description={query.trim()
            ? `找到 ${filtered.length} 个对象 · 共 ${objects.data?.total ?? rows.length} 个`
            : `共 ${objects.data?.total ?? rows.length} 个对象`}
        >
          {filtered.length ? (
            <DataTable
              rows={filtered}
              getRowKey={row => `${row.subjectType}:${row.subjectId}`}
              columns={[
                { key: "subjectType", label: "对象类型", render: value => <Badge tone="info">{objectTypeLabel(value)}</Badge> },
                { key: "subjectId", label: "对象编号" },
                { key: "edgeId", label: "采集节点" },
                { key: "eventCount", label: "事件数", render: formatInteger },
                { key: "sampleCount", label: "样本数", render: formatInteger },
                { key: "lastObservedAt", label: "最后活动", render: formatTime },
                { key: "latestEventType", label: "最新活动", render: eventTypeLabel },
              ]}
            />
          ) : (
            <EmptyState
              title={query ? "没有匹配的设备或对象" : "尚未收到生产数据"}
              description={query ? "请调整搜索条件后重试。" : "采集节点上报生产事件后，设备与对象会自动显示在这里。"}
            />
          )}
        </Card>
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
  const [filters, setFilters] = useState({ productSeries: "", subjectType: "", subjectId: "" });
  const [query, setQuery] = useState("limit=1000&offset=0");
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
  const [result, setResult] = useState(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  async function compare(event) {
    event.preventDefault();
    setBusy(true);
    setError("");
    try {
      const baselineCycleId = baseline.trim();
      setResult(await postJson("/api/v1/cycle-comparisons", {
        baselineCycleId,
        cycleIds: [baselineCycleId, candidate.trim()],
      }));
    } catch (requestError) {
      setResult(null);
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
  return (
    <Page title="历史对比" description="把同类运行按阶段对齐，检查参数与结果差异。">
      {error && <Alert tone="danger">{error}</Alert>}
      <Card title="选择周期">
        <form className="grid gap-3 md:grid-cols-[1fr_1fr_auto]" onSubmit={compare}>
          <Field label="基准周期"><Input value={baseline} onChange={event => setBaseline(event.target.value)} required /></Field>
          <Field label="对比周期"><Input value={candidate} onChange={event => setCandidate(event.target.value)} required /></Field>
          <Button variant="primary" type="submit" className="self-end" disabled={busy}>{busy ? "正在对比…" : "开始对比"}</Button>
        </form>
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
      ) : <EmptyState title="尚未执行对比" description="填写基准周期和对比周期后开始。" />}
    </Page>
  );
}

export function DataQualityPage() {
  return (
    <ResourcePage
      title="数据健康"
      description="检查对象数据范围、采样连续性和周期完整性。"
      endpoint="/api/v1/data-objects?limit=200"
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

const improvementTabs = [
  {
    label: "调查", endpoint: "/api/v1/process-investigations", key: "investigationId",
    columns: [["title", "问题"], ["status", "状态"], ["createdAt", "创建时间"]],
    template: { title: "", problemCode: "", description: "", contextSelector: {}, cycleIds: [] },
  },
  {
    label: "数据模型", endpoint: "/api/v1/process-models", key: "modelId",
    columns: [["modelId", "模型"], ["version", "版本"], ["status", "状态"], ["outputCode", "输出"]],
    template: { modelId: "", version: 1, name: "", modelKind: "quality-risk", problemCode: "", status: "draft", algorithm: "", datasetId: "", datasetVersion: 1, artifactRef: "", artifactSha256: "", contextSelector: {}, inputFeatureCodes: [], outputCode: "", uncertaintyMethod: "none", changeNote: "" },
  },
  {
    label: "机理模型", endpoint: "/api/v1/mechanism-models", key: "modelId",
    columns: [["modelId", "模型"], ["version", "版本"], ["name", "名称"], ["status", "状态"], ["output", "输出变量"]],
    template: { modelId: "", version: 1, name: "", status: "draft", equationKind: "affine", inputs: [], output: { code: "", unit: "1", validMinimum: null, validMaximum: null }, intercept: 0, coefficients: {}, applicabilityContext: {}, scientificBasis: "", sourceReference: "" },
  },
  {
    label: "融合执行", endpoint: "/api/v1/mechanism-fusions", key: "fusionId",
    columns: [["fusionId", "融合"], ["version", "版本"], ["mode", "方式"], ["status", "状态"], ["outputCode", "输出"]],
    template: { fusionId: "", version: 1, name: "", status: "draft", mode: "calibration", mechanismModelId: "", mechanismModelVersion: 1, dataModelId: "", dataModelVersion: 1, calibrationScale: 1, calibrationOffset: 0, postProcessingGain: 1, mechanismReference: 0, mechanismWeight: 0.5, mechanismFeatureCode: "mechanism.output", outputCode: "", applicabilityContext: {} },
  },
  {
    label: "训练数据", endpoint: "/api/v1/training-datasets", key: "datasetId",
    columns: [["datasetId", "数据集"], ["version", "版本"], ["rowCount", "行数"], ["createdAt", "创建时间"]],
    template: { datasetId: "", version: 1, name: "", analysisPlanId: "", analysisPlanVersion: 1, dataModelId: "", dataModelVersion: 1, contextSelector: {}, cycleIds: [], featureCodes: [], targetCode: "", windowStart: "", windowEnd: "", rowCount: 0, contentHash: "" },
  },
  {
    label: "知识", endpoint: "/api/v1/process-knowledge", key: "sourceId",
    columns: [["title", "来源"], ["sourceKind", "类型"], ["status", "状态"], ["uploadedAt", "上传时间"]],
    upload: "knowledge",
  },
  {
    label: "参数建议", endpoint: "/api/v1/parameter-recommendations", key: "recommendationId",
    columns: [["title", "建议"], ["status", "状态"], ["expectedAnnualValue", "预期年价值"]],
    template: { investigationId: "", conclusionId: "", modelId: "", modelVersion: 1, title: "", applicableContext: {}, parameterSettings: [], constraints: [], expectedOutcomes: [], valueEstimate: { currency: "CNY", expectedAnnualValue: 0, trialCost: 0, implementationCost: 0, downsideAtRisk: 0 }, riskSummary: "", stopRule: "", rollbackPlan: "" },
  },
  {
    label: "科研验证", endpoint: "/api/v1/scientific-validation", key: "reportId",
    columns: [["datasetId", "数据集"], ["industry", "行业"], ["process", "工艺"], ["status", "状态"], ["rowCount", "数据行"]],
    upload: "validation",
    template: { datasetId: "", version: 1, industry: "", process: "", dataKind: "measured-experiment", isMeasuredData: true, sourceUri: "https://", retrievalUri: "https://", archiveMemberPath: "", license: "", citation: "", doi: "", expectedSha256: "", sheetName: "", headerRowCount: 1, matVariableName: "", cycleColumn: "", timestampColumn: "", phaseColumn: "", signalColumns: [], outcomeColumns: [], minimumSignalNumericCoverage: 0.8, minimumOutcomeNumericCoverage: 0.3, units: {}, validSignalRanges: {} },
  },
  {
    label: "历史回填", endpoint: "/api/v1/cycle-analysis-backfills", key: "jobId",
    columns: [["jobId", "任务"], ["status", "状态"], ["processedCycles", "已处理"], ["failedCycles", "失败"]],
    template: { from: null, to: null, correlationIds: [] },
  },
];

export function ProcessImprovementPage() {
  const [selectedTab, setSelectedTab] = useState(0);
  return (
    <Page title="工艺改进" description="从调查、模型、知识到受控参数建议的闭环。">
      <WorkflowGuide
        title="从问题到可验证改进"
        description="第一次使用建议从“调查”开始；模型、知识和参数建议是调查过程中逐步沉淀的成果。"
        steps={[
          { title: "先建立调查", description: "明确问题、关联周期并记录可能原因。", state: selectedTab === 0 ? "current" : "done" },
          { title: "用试验或模型验证", description: "通过受控试验、数据模型或机理模型验证判断。", state: [1, 2, 3].includes(selectedTab) ? "current" : selectedTab > 3 ? "done" : "upcoming" },
          { title: "批准并跟踪效果", description: "形成参数建议，经过审批、执行和实际效果确认。", state: selectedTab === 6 ? "current" : "upcoming" },
        ]}
      />
      <TabGroup selectedIndex={selectedTab} onChange={setSelectedTab}>
        <TabList className="flex gap-1 overflow-x-auto rounded-xl bg-slate-200/70 p-1">
          {improvementTabs.map(item => (
            <Tab key={item.label} className="shrink-0 rounded-lg px-4 py-2 text-sm font-medium text-slate-600 outline-none data-selected:bg-white data-selected:text-blue-700 data-selected:shadow-sm">{item.label}</Tab>
          ))}
        </TabList>
        <TabPanels className="mt-4">
          {improvementTabs.map((tab, index) => (
            <TabPanel key={tab.label}>
              {index === selectedTab && <ImprovementPanel definition={tab} />}
            </TabPanel>
          ))}
        </TabPanels>
      </TabGroup>
    </Page>
  );
}

function ImprovementPanel({ definition }) {
  const { data, loading, error, reload } = useApi(definition.endpoint);
  const [open, setOpen] = useState(false);
  const [detailOpen, setDetailOpen] = useState(false);
  const [selected, setSelected] = useState(null);
  const [detail, setDetail] = useState(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState("");
  const [detailBusy, setDetailBusy] = useState(false);
  const [executionEditor, setExecutionEditor] = useState({});
  const [executionResult, setExecutionResult] = useState(null);
  const [editor, setEditor] = useState(() => structuredClone(definition.template || {}));
  const [file, setFile] = useState(null);
  const [title, setTitle] = useState("");
  const [sourceKind, setSourceKind] = useState("document");
  const [actionError, setActionError] = useState("");
  const [saving, setSaving] = useState(false);
  const supportsDetail = ["调查", "数据模型", "机理模型", "融合执行", "知识", "参数建议"].includes(definition.label);

  function detailUrl(row) {
    if (definition.label === "调查") {
      return `/api/v1/process-investigations/${encodeURIComponent(row.investigationId)}`;
    }
    if (definition.label === "数据模型") {
      return `/api/v1/process-models/${encodeURIComponent(row.modelId)}/${row.version}`;
    }
    if (definition.label === "机理模型") {
      return `/api/v1/mechanism-models/${encodeURIComponent(row.modelId)}/${row.version}`;
    }
    if (definition.label === "融合执行") {
      return `/api/v1/mechanism-fusions/${encodeURIComponent(row.fusionId)}/${row.version}`;
    }
    if (definition.label === "知识") {
      return `/api/v1/process-knowledge/${encodeURIComponent(row.sourceId)}`;
    }
    if (definition.label === "参数建议") {
      return `/api/v1/parameter-recommendations/${encodeURIComponent(row.recommendationId)}`;
    }
    return null;
  }

  async function loadDetail(row, { show = true } = {}) {
    const url = detailUrl(row);
    if (!url) return;
    if (show) {
      setSelected(row);
      setDetailOpen(true);
      setExecutionResult(null);
      if (definition.label === "融合执行") {
        setExecutionEditor({
          fusionId: row.fusionId,
          fusionVersion: row.version,
          mechanismInputs: {},
          dataPrediction: "",
          operatingContext: {},
        });
      } else if (definition.label === "参数建议") {
        const now = new Date();
        const windowEnd = now.toISOString().slice(0, 16);
        const windowStart = new Date(now.getTime() - 30 * 86400000).toISOString().slice(0, 16);
        setExecutionEditor({
          executionReference: "",
          notes: "",
          outcomes: (row.expectedOutcomes || []).map(outcome => ({
            metricCode: outcome.metricCode,
            baselineValue: outcome.baselineValue,
            actualValue: outcome.expectedValue,
            unit: outcome.unit,
            baselineSampleCount: 1,
            actualSampleCount: 1,
            safetyPassed: true,
          })),
          realizedValue: {
            currency: row.valueEstimate?.currency || "CNY",
            windowStart,
            windowEnd,
            grossValue: row.valueEstimate?.expectedAnnualValue || 0,
            implementationCost: row.valueEstimate?.implementationCost || 0,
            calculationNote: "",
          },
        });
      }
    }
    setDetailLoading(true);
    setDetailError("");
    try {
      setDetail(await getJson(url));
    } catch (requestError) {
      setDetailError(requestError.message);
    } finally {
      setDetailLoading(false);
    }
  }

  async function runDetailAction(action) {
    setDetailBusy(true);
    setDetailError("");
    try {
      await action();
      await Promise.all([loadDetail(selected, { show: false }), reload()]);
      notify(`${definition.label}已更新。`);
      return true;
    } catch (requestError) {
      setDetailError(requestError.message);
      return false;
    } finally {
      setDetailBusy(false);
    }
  }

  function changeDetailStatus(targetStatus) {
    let url;
    let body = { targetStatus };
    if (definition.label === "数据模型") {
      url = `/api/v1/process-models/${encodeURIComponent(selected.modelId)}/${selected.version}/status`;
    } else if (definition.label === "机理模型") {
      url = `/api/v1/mechanism-models/${encodeURIComponent(selected.modelId)}/${selected.version}/status`;
    } else if (definition.label === "融合执行") {
      url = `/api/v1/mechanism-fusions/${encodeURIComponent(selected.fusionId)}/${selected.version}/status`;
    } else if (definition.label === "知识") {
      url = `/api/v1/process-knowledge/${encodeURIComponent(selected.sourceId)}/status`;
    } else if (definition.label === "参数建议") {
      url = `/api/v1/parameter-recommendations/${encodeURIComponent(selected.recommendationId)}/status`;
      const verification = targetStatus === "verified" ? {
        outcomes: executionEditor.outcomes.map(outcome => ({
          ...outcome,
          baselineValue: Number(outcome.baselineValue),
          actualValue: Number(outcome.actualValue),
          effectValue: Number(outcome.actualValue) - Number(outcome.baselineValue),
          baselineSampleCount: Number(outcome.baselineSampleCount),
          actualSampleCount: Number(outcome.actualSampleCount),
        })),
        realizedValue: {
          ...executionEditor.realizedValue,
          windowStart: new Date(executionEditor.realizedValue.windowStart).toISOString(),
          windowEnd: new Date(executionEditor.realizedValue.windowEnd).toISOString(),
          grossValue: Number(executionEditor.realizedValue.grossValue),
          implementationCost: Number(executionEditor.realizedValue.implementationCost),
          netValue: Number(executionEditor.realizedValue.grossValue) - Number(executionEditor.realizedValue.implementationCost),
        },
        objectivesMet: false,
        safetyPassed: false,
        notes: executionEditor.notes || null,
      } : null;
      body = {
        targetStatus,
        executionReference: executionEditor.executionReference.trim() || null,
        verification,
      };
    }
    return runDetailAction(() => postJson(url, body));
  }

  function changeTrialStatus(trialId, targetStatus) {
    return runDetailAction(() => postJson(
      `/api/v1/process-investigations/trials/${encodeURIComponent(trialId)}/status`,
      { targetStatus },
    ));
  }

  function calculateTrialResult(trialId) {
    return runDetailAction(() => postJson(
      `/api/v1/process-investigations/trials/${encodeURIComponent(trialId)}/results/calculate`,
      {},
    ));
  }

  function addInvestigationCause(payload) {
    return runDetailAction(() => postJson(
      `/api/v1/process-investigations/${encodeURIComponent(selected.investigationId)}/causes`,
      payload,
    ));
  }

  function createInvestigationTrial(payload) {
    return runDetailAction(() => postJson(
      `/api/v1/process-investigations/${encodeURIComponent(selected.investigationId)}/trials`,
      payload,
    ));
  }

  function addTrialResult(trialId, payload) {
    return runDetailAction(() => postJson(
      `/api/v1/process-investigations/trials/${encodeURIComponent(trialId)}/results`,
      payload,
    ));
  }

  function addInvestigationConclusion(payload) {
    return runDetailAction(() => postJson(
      `/api/v1/process-investigations/${encodeURIComponent(selected.investigationId)}/conclusions`,
      payload,
    ));
  }

  function addModelEvaluation(payload) {
    return runDetailAction(() => postJson(
      `/api/v1/process-models/${encodeURIComponent(selected.modelId)}/${selected.version}/evaluations`,
      payload,
    ));
  }

  function addModelDrift(payload) {
    return runDetailAction(() => postJson(
      `/api/v1/process-models/${encodeURIComponent(selected.modelId)}/${selected.version}/drift`,
      payload,
    ));
  }

  function reviewKnowledgeRecord(record) {
    return runDetailAction(() => postJson(
      `/api/v1/process-knowledge/${encodeURIComponent(selected.sourceId)}/records`,
      { ...record, humanReviewed: true },
    ));
  }

  async function executeFusion() {
    setDetailBusy(true);
    setDetailError("");
    setExecutionResult(null);
    try {
      const result = await postJson("/api/v1/mechanism-fusions/execute", {
        ...executionEditor,
        dataPrediction: executionEditor.dataPrediction === "" ? null : Number(executionEditor.dataPrediction),
      });
      setExecutionResult(result);
    } catch (requestError) {
      setDetailError(requestError.message);
    } finally {
      setDetailBusy(false);
    }
  }

  function start() {
    setEditor(structuredClone(definition.template || {}));
    setFile(null);
    setTitle("");
    setSourceKind("document");
    setActionError("");
    setOpen(true);
  }

  async function save() {
    setSaving(true);
    setActionError("");
    try {
      if (definition.upload === "knowledge") {
        if (!file || !title.trim()) throw new Error("请选择文件并填写来源标题。");
        const form = new FormData();
        form.append("file", file);
        form.append("title", title.trim());
        form.append("sourceKind", sourceKind);
        form.append("contextSelectorJson", "{}");
        await postForm(definition.endpoint, form);
      } else if (definition.upload === "validation") {
        if (!file) throw new Error("请选择科研原始数据文件。");
        const form = new FormData();
        form.append("file", file);
        form.append("manifestJson", JSON.stringify(editor));
        await postForm(definition.endpoint, form);
      } else {
        await postJson(definition.endpoint, editor);
      }
      setOpen(false);
      await reload();
      notify(`${definition.label}已提交。`);
    } catch (requestError) {
      setActionError(requestError.message);
    } finally {
      setSaving(false);
    }
  }

  if (error) return <Alert tone="danger">{error}</Alert>;
  if (loading && !data) return <LoadingCard />;
  return (
    <>
      {actionError && !open && <Alert tone="danger">{actionError}</Alert>}
      <Card
        title={definition.label}
        description={`共 ${extractRows(data).length} 条${supportsDetail ? " · 可进入详情完成验证与复核" : ""}`}
        actions={<Button variant="primary" onClick={start}>{definition.upload ? `上传${definition.label}` : `新建${definition.label}`}</Button>}
      >
        <DataTable
          rows={extractRows(data)}
          keyField={definition.key}
          getRowKey={definition.columns.some(([key]) => key === "version")
            ? row => `${row[definition.key]}:${row.version ?? 1}`
            : undefined}
          onRowClick={supportsDetail ? row => loadDetail(row) : undefined}
          columns={[
            ...definition.columns.map(([key, columnLabel]) => ({
              key,
              label: columnLabel,
              render: key === "status" ? value => <StatusBadge value={value} /> : key.endsWith("At") ? formatTime : undefined,
            })),
            ...(supportsDetail ? [{
              key: "__actions",
              label: "操作",
              render: (_value, row) => (
                <Button
                  variant="ghost"
                  onClick={event => {
                    event.stopPropagation();
                    loadDetail(row);
                  }}
                >
                  查看与处理
                </Button>
              ),
            }] : []),
          ]}
        />
      </Card>
      <ImprovementDetailDrawer
        definition={definition}
        open={detailOpen}
        onClose={() => setDetailOpen(false)}
        detail={detail}
        loading={detailLoading}
        error={detailError}
        busy={detailBusy}
        executionEditor={executionEditor}
        setExecutionEditor={setExecutionEditor}
        executionResult={executionResult}
        onStatus={changeDetailStatus}
        onReviewRecord={reviewKnowledgeRecord}
        onExtract={() => runDetailAction(() => postJson(`/api/v1/process-knowledge/${encodeURIComponent(selected.sourceId)}/extract`, {}))}
        onExecute={executeFusion}
        onTrialStatus={changeTrialStatus}
        onCalculateTrialResult={calculateTrialResult}
        onAddCause={addInvestigationCause}
        onCreateTrial={createInvestigationTrial}
        onAddTrialResult={addTrialResult}
        onAddConclusion={addInvestigationConclusion}
        onAddModelEvaluation={addModelEvaluation}
        onAddModelDrift={addModelDrift}
      />
      <Drawer
        open={open}
        onClose={() => setOpen(false)}
        closeOnBackdrop={false}
        title={definition.upload ? `上传${definition.label}` : `新建${definition.label}`}
        description={definition.upload === "knowledge" ? "上传后自动解析并进入人工复核队列。" : definition.upload === "validation" ? "上传后会校验来源、许可、文件完整性、字段覆盖与流批一致性。" : "保存前会校验必填项和引用关系。"}
        size={definition.upload === "validation" ? "xl" : "lg"}
        footer={<><Button onClick={() => setOpen(false)}>取消</Button><Button variant="primary" onClick={save} disabled={saving || (definition.upload === "knowledge" && (!file || !title.trim())) || (definition.upload === "validation" && !file)}>{saving ? "处理中" : "提交"}</Button></>}
      >
        {actionError && <Alert tone="danger">{actionError}</Alert>}
        <div className="grid gap-5">
          {definition.label === "融合执行" && (
            <Alert tone="info">
              选择机理模型与数据模型的组合方式，并固定所引用的模型版本。
            </Alert>
          )}
          {definition.upload === "validation" && (
            <Alert tone="info">
              数据清单可登记经文献或设备说明确认的有效范围；超界样本数量、判定规则和流批一致性会写入科研验证报告。
            </Alert>
          )}
          {definition.upload === "knowledge" ? (
            <>
              <Field label="来源标题"><Input required value={title} onChange={event => setTitle(event.target.value)} /></Field>
              <Field label="来源类型"><Select value={sourceKind} onChange={event => setSourceKind(event.target.value)}><option value="document">文档</option><option value="spreadsheet">表格</option><option value="image">图片</option><option value="field-note">现场记录</option></Select></Field>
              <Field label="原始文件"><Input required type="file" accept=".pdf,.xlsx,.xlsm,.csv,.txt,.md,.png,.jpg,.jpeg,.webp,.tif,.tiff" onChange={event => setFile(event.target.files?.[0] || null)} /></Field>
            </>
          ) : (
            <>
              {definition.upload === "validation" && <Field label="原始科研数据"><Input required type="file" accept=".csv,.xlsx,.xlsm,.mat" onChange={event => setFile(event.target.files?.[0] || null)} /></Field>}
              <BusinessObjectEditor value={editor} onChange={setEditor} />
            </>
          )}
        </div>
      </Drawer>
    </>
  );
}

function localDateTimeValue(date = new Date()) {
  const offset = date.getTimezoneOffset() * 60000;
  return new Date(date.getTime() - offset).toISOString().slice(0, 16);
}

function emptyCauseEditor() {
  return {
    title: "",
    referenceKind: "parameter",
    referenceCode: "",
    phaseCode: "",
    direction: "unknown",
    reasoning: "",
    relatedCycleIds: "",
  };
}

function emptyTrialEditor(causeId = "") {
  return {
    causeId,
    name: "",
    parameterCode: "",
    phaseCode: "",
    baselineValue: "",
    trialValue: "",
    unit: "",
    allowedMinimum: "",
    allowedMaximum: "",
    constraintCode: "",
    constraintDescription: "",
    constraintOperator: "<=",
    constraintLimit: "",
    constraintUnit: "",
    controlCycleIds: "",
    trialCycleIds: "",
    stopRule: "",
    rollbackPlan: "",
  };
}

function emptyResultEditor(trialId = "") {
  return {
    trialId,
    metricCode: "",
    baselineValue: "",
    trialValue: "",
    unit: "",
    baselineSampleCount: 1,
    trialSampleCount: 1,
    safetyPassed: true,
  };
}

function emptyConclusionEditor(trialId = "") {
  return { trialId, decision: "confirmed", summary: "" };
}

function emptyEvaluationEditor() {
  return {
    split: "holdout",
    sampleCount: "",
    metricCode: "",
    value: "",
    unit: "",
    requiredMinimum: "",
    requiredMaximum: "",
    notes: "",
  };
}

function emptyDriftEditor() {
  const windowEnd = new Date();
  const windowStart = new Date(windowEnd.getTime() - 86400000);
  return {
    metricCode: "",
    value: "",
    warningThreshold: "",
    stopThreshold: "",
    sampleCount: "",
    windowStart: localDateTimeValue(windowStart),
    windowEnd: localDateTimeValue(windowEnd),
  };
}

function ImprovementDetailDrawer({
  definition,
  open,
  onClose,
  detail,
  loading,
  error,
  busy,
  executionEditor,
  setExecutionEditor,
  executionResult,
  onStatus,
  onReviewRecord,
  onExtract,
  onExecute,
  onTrialStatus,
  onCalculateTrialResult,
  onAddCause,
  onCreateTrial,
  onAddTrialResult,
  onAddConclusion,
  onAddModelEvaluation,
  onAddModelDrift,
}) {
  const resource = detail?.investigation || detail?.recommendation || detail?.model || detail?.fusion || detail?.source;
  const [workflowMode, setWorkflowMode] = useState("");
  const [causeEditor, setCauseEditor] = useState(emptyCauseEditor);
  const [trialEditor, setTrialEditor] = useState(emptyTrialEditor);
  const [resultEditor, setResultEditor] = useState(emptyResultEditor);
  const [conclusionEditor, setConclusionEditor] = useState(emptyConclusionEditor);
  const [evaluationEditor, setEvaluationEditor] = useState(emptyEvaluationEditor);
  const [driftEditor, setDriftEditor] = useState(emptyDriftEditor);
  const records = detail?.records || [];
  const status = resource?.status;
  const allReviewed = records.length > 0 && records.every(record => record.humanReviewed);
  const availableCauses = detail?.causes || [];
  const runningExploratoryTrials = (detail?.trials || []).filter(trial => trial.status === "running" && trial.rigorLevel !== "confirmatory");
  const completedTrials = (detail?.trials || []).filter(trial => trial.status === "completed" && detail?.results?.[trial.trialId]?.length);

  useEffect(() => {
    setWorkflowMode("");
    setCauseEditor(emptyCauseEditor());
    setTrialEditor(emptyTrialEditor());
    setResultEditor(emptyResultEditor());
    setConclusionEditor(emptyConclusionEditor());
    setEvaluationEditor(emptyEvaluationEditor());
    setDriftEditor(emptyDriftEditor());
  }, [resource?.investigationId, resource?.modelId, resource?.version]);

  function openWorkflow(mode) {
    setWorkflowMode(mode);
    if (mode === "trial") setTrialEditor(emptyTrialEditor(availableCauses[0]?.causeId || ""));
    if (mode === "result") setResultEditor(emptyResultEditor(runningExploratoryTrials[0]?.trialId || ""));
    if (mode === "conclusion") setConclusionEditor(emptyConclusionEditor(completedTrials[0]?.trialId || ""));
  }

  async function submitCause() {
    const saved = await onAddCause({
      title: causeEditor.title.trim(),
      parameterCode: causeEditor.referenceKind === "parameter" ? causeEditor.referenceCode.trim() : null,
      signalCode: causeEditor.referenceKind === "signal" ? causeEditor.referenceCode.trim() : null,
      phaseCode: causeEditor.phaseCode.trim() || null,
      direction: causeEditor.direction,
      reasoning: causeEditor.reasoning.trim(),
      relatedCycleIds: causeEditor.relatedCycleIds.split(/[\s,，]+/).map(value => value.trim()).filter(Boolean),
    });
    if (saved) {
      setWorkflowMode("");
      setCauseEditor(emptyCauseEditor());
    }
  }

  async function submitTrial() {
    const saved = await onCreateTrial({
      causeId: trialEditor.causeId,
      name: trialEditor.name.trim(),
      trialKind: "controlled-field-trial",
      rigorLevel: "exploratory",
      parameterChanges: [{
        parameterCode: trialEditor.parameterCode.trim(),
        phaseCode: trialEditor.phaseCode.trim() || null,
        baselineValue: Number(trialEditor.baselineValue),
        trialValue: Number(trialEditor.trialValue),
        unit: trialEditor.unit.trim(),
        allowedMinimum: Number(trialEditor.allowedMinimum),
        allowedMaximum: Number(trialEditor.allowedMaximum),
      }],
      safetyConstraints: [{
        code: trialEditor.constraintCode.trim(),
        description: trialEditor.constraintDescription.trim(),
        operator: trialEditor.constraintOperator,
        limit: Number(trialEditor.constraintLimit),
        unit: trialEditor.constraintUnit.trim(),
      }],
      controlCycleIds: trialEditor.controlCycleIds.split(/[\s,，]+/).map(value => value.trim()).filter(Boolean),
      trialCycleIds: trialEditor.trialCycleIds.split(/[\s,，]+/).map(value => value.trim()).filter(Boolean),
      stopRule: trialEditor.stopRule.trim(),
      rollbackPlan: trialEditor.rollbackPlan.trim(),
    });
    if (saved) setWorkflowMode("");
  }

  async function submitResult() {
    const saved = await onAddTrialResult(resultEditor.trialId, {
      metricCode: resultEditor.metricCode.trim(),
      baselineValue: Number(resultEditor.baselineValue),
      trialValue: Number(resultEditor.trialValue),
      effectValue: Number(resultEditor.trialValue) - Number(resultEditor.baselineValue),
      unit: resultEditor.unit.trim(),
      baselineSampleCount: Number(resultEditor.baselineSampleCount),
      trialSampleCount: Number(resultEditor.trialSampleCount),
      safetyPassed: resultEditor.safetyPassed,
      calculatedFromSource: false,
      computationMethod: "manual",
    });
    if (saved) setWorkflowMode("");
  }

  async function submitConclusion() {
    const trial = completedTrials.find(item => item.trialId === conclusionEditor.trialId);
    const saved = await onAddConclusion({
      causeId: trial.causeId,
      trialId: trial.trialId,
      decision: conclusionEditor.decision,
      summary: conclusionEditor.summary.trim(),
      applicableContext: resource.contextSelector || {},
      resultIds: (detail.results?.[trial.trialId] || []).map(result => result.resultId),
    });
    if (saved) setWorkflowMode("");
  }

  async function submitEvaluation() {
    const minimum = evaluationEditor.requiredMinimum === "" ? null : Number(evaluationEditor.requiredMinimum);
    const maximum = evaluationEditor.requiredMaximum === "" ? null : Number(evaluationEditor.requiredMaximum);
    const value = Number(evaluationEditor.value);
    const saved = await onAddModelEvaluation({
      split: evaluationEditor.split,
      sampleCount: Number(evaluationEditor.sampleCount),
      metrics: [{
        code: evaluationEditor.metricCode.trim(),
        value,
        unit: evaluationEditor.unit.trim() || null,
        requiredMinimum: minimum,
        requiredMaximum: maximum,
      }],
      passed: (minimum === null || value >= minimum) && (maximum === null || value <= maximum),
      notes: evaluationEditor.notes.trim() || null,
    });
    if (saved) setWorkflowMode("");
  }

  async function submitDrift() {
    const saved = await onAddModelDrift({
      metricCode: driftEditor.metricCode.trim(),
      value: Number(driftEditor.value),
      warningThreshold: Number(driftEditor.warningThreshold),
      stopThreshold: Number(driftEditor.stopThreshold),
      sampleCount: Number(driftEditor.sampleCount),
      windowStart: new Date(driftEditor.windowStart).toISOString(),
      windowEnd: new Date(driftEditor.windowEnd).toISOString(),
    });
    if (saved) setWorkflowMode("");
  }

  const statusButtons = [];
  if (["数据模型", "机理模型", "融合执行"].includes(definition.label)) {
    if (status === "draft") statusButtons.push(["提交验证", "validated"]);
    if (status === "validated") {
      statusButtons.push(["启用", "active"]);
      statusButtons.push(["停用", "retired"]);
    }
    if (status === "active") {
      if (definition.label === "数据模型") statusButtons.push(["暂停运行", "suspended"]);
      statusButtons.push(["停用", "retired"]);
    }
    if (status === "suspended" && definition.label === "数据模型") statusButtons.push(["重新验证", "validated"]);
  }
  if (definition.label === "知识") {
    if (status === "indexed" && allReviewed) statusButtons.push(["确认复核完成", "reviewed"]);
    if (["uploaded", "indexed", "reviewed"].includes(status)) statusButtons.push(["停用来源", "retired"]);
  }
  if (definition.label === "参数建议") {
    if (status === "draft") {
      statusButtons.push(["完成复核", "reviewed"]);
      statusButtons.push(["撤回", "withdrawn"]);
    }
    if (status === "reviewed") {
      statusButtons.push(["批准", "approved"]);
      statusButtons.push(["驳回", "rejected"]);
    }
    if (status === "approved") {
      statusButtons.push(["记录执行", "executed"]);
      statusButtons.push(["撤回", "withdrawn"]);
    }
    if (status === "executed") statusButtons.push(["确认实际效果", "verified"]);
    if (status === "rollback-required") statusButtons.push(["记录回退", "rolled-back"]);
  }
  const statusActionDisabled = targetStatus => {
    if (busy) return true;
    if (["executed", "rolled-back"].includes(targetStatus)) return !executionEditor.executionReference?.trim();
    if (targetStatus !== "verified") return false;
    return !(executionEditor.outcomes?.length > 0 &&
      executionEditor.realizedValue?.windowStart &&
      executionEditor.realizedValue?.windowEnd &&
      executionEditor.realizedValue?.calculationNote?.trim());
  };

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title={`${definition.label}详情`}
      description="所有验证、复核和状态变更都会留下审计记录。"
      size="xl"
      footer={(
        <>
          {statusButtons.map(([label, targetStatus]) => (
            <Button
              key={targetStatus}
              variant={targetStatus === "retired" ? "danger" : "primary"}
              disabled={statusActionDisabled(targetStatus)}
              onClick={() => onStatus(targetStatus)}
            >
              {label}
            </Button>
          ))}
          <Button onClick={onClose}>关闭</Button>
        </>
      )}
    >
      {error && <Alert tone="danger" title="操作未完成">{error}</Alert>}
      {loading && !detail ? <LoadingCard /> : resource ? (
        <div className="grid gap-5">
          <div className="grid gap-3 sm:grid-cols-3">
            <Metric label="当前状态" value={<StatusBadge value={status} />} />
            <Metric label="版本" value={resource.version ?? "—"} />
            <Metric
              label={definition.label === "知识" ? "已复核记录" : "内容指纹"}
              value={definition.label === "知识"
                ? `${records.filter(record => record.humanReviewed).length}/${records.length}`
                : (resource.contentHash?.slice(0, 12) || "—")}
            />
          </div>

          {definition.label === "调查" && (
            <>
              {!["concluded", "closed"].includes(status) && (
                <Card title="调查下一步" description="先记录可能原因，再创建受控试验；运行中的探索性试验可以人工登记结果。">
                  <div className="flex flex-wrap gap-2">
                    <Button variant="primary" onClick={() => openWorkflow("cause")}>添加可能原因</Button>
                    <Button disabled={!availableCauses.length} onClick={() => openWorkflow("trial")}>创建调整试验</Button>
                    <Button disabled={!runningExploratoryTrials.length} onClick={() => openWorkflow("result")}>登记试验结果</Button>
                    <Button disabled={!completedTrials.length} onClick={() => openWorkflow("conclusion")}>形成调查结论</Button>
                  </div>
                  {!availableCauses.length && <p className="mt-3 text-sm text-slate-500">添加至少一个可能原因后，才能创建调整试验。</p>}
                </Card>
              )}
              {workflowMode === "cause" && (
                <Card title="添加可能原因" description="原因必须关联一个工艺参数或采集信号，并说明判断依据。">
                  <div className="grid gap-4 sm:grid-cols-2">
                    <Field label="原因标题"><Input required value={causeEditor.title} onChange={event => setCauseEditor({ ...causeEditor, title: event.target.value })} /></Field>
                    <Field label="关联类型">
                      <Select value={causeEditor.referenceKind} onChange={event => setCauseEditor({ ...causeEditor, referenceKind: event.target.value })}>
                        <option value="parameter">工艺参数</option>
                        <option value="signal">采集信号</option>
                      </Select>
                    </Field>
                    <Field label={causeEditor.referenceKind === "parameter" ? "参数代码" : "信号代码"}><Input required placeholder="例如 pressure.setpoint" value={causeEditor.referenceCode} onChange={event => setCauseEditor({ ...causeEditor, referenceCode: event.target.value })} /></Field>
                    <Field label="工艺阶段"><Input placeholder="可选" value={causeEditor.phaseCode} onChange={event => setCauseEditor({ ...causeEditor, phaseCode: event.target.value })} /></Field>
                    <Field label="影响方向">
                      <Select value={causeEditor.direction} onChange={event => setCauseEditor({ ...causeEditor, direction: event.target.value })}>
                        <option value="unknown">待确认</option>
                        <option value="positive">正向</option>
                        <option value="negative">负向</option>
                        <option value="nonlinear">非线性</option>
                      </Select>
                    </Field>
                    <Field label="关联周期"><Input placeholder="多个周期用逗号分隔" value={causeEditor.relatedCycleIds} onChange={event => setCauseEditor({ ...causeEditor, relatedCycleIds: event.target.value })} /></Field>
                    <Field label="判断依据" className="sm:col-span-2"><Textarea required value={causeEditor.reasoning} onChange={event => setCauseEditor({ ...causeEditor, reasoning: event.target.value })} /></Field>
                    <div className="flex gap-2 sm:col-span-2">
                      <Button variant="primary" disabled={busy || !causeEditor.title.trim() || !causeEditor.referenceCode.trim() || !causeEditor.reasoning.trim()} onClick={submitCause}>保存原因</Button>
                      <Button disabled={busy} onClick={() => setWorkflowMode("")}>取消</Button>
                    </div>
                  </div>
                </Card>
              )}
              {workflowMode === "trial" && (
                <Card title="创建探索性调整试验" description="明确改动范围、安全门槛、停止规则和回退方案后，交由另一位人员批准。">
                  <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
                    <Field label="可能原因">
                      <Select required value={trialEditor.causeId} onChange={event => setTrialEditor({ ...trialEditor, causeId: event.target.value })}>
                        {availableCauses.map(cause => <option key={cause.causeId} value={cause.causeId}>{cause.title}</option>)}
                      </Select>
                    </Field>
                    <Field label="试验名称" className="lg:col-span-2"><Input required value={trialEditor.name} onChange={event => setTrialEditor({ ...trialEditor, name: event.target.value })} /></Field>
                    <Field label="调整参数"><Input required placeholder="例如 pressure.setpoint" value={trialEditor.parameterCode} onChange={event => setTrialEditor({ ...trialEditor, parameterCode: event.target.value })} /></Field>
                    <Field label="工艺阶段"><Input placeholder="可选" value={trialEditor.phaseCode} onChange={event => setTrialEditor({ ...trialEditor, phaseCode: event.target.value })} /></Field>
                    <Field label="单位"><Input required value={trialEditor.unit} onChange={event => setTrialEditor({ ...trialEditor, unit: event.target.value })} /></Field>
                    <Field label="当前值"><Input required type="number" step="any" value={trialEditor.baselineValue} onChange={event => setTrialEditor({ ...trialEditor, baselineValue: event.target.value })} /></Field>
                    <Field label="试验值"><Input required type="number" step="any" value={trialEditor.trialValue} onChange={event => setTrialEditor({ ...trialEditor, trialValue: event.target.value })} /></Field>
                    <Field label="允许下限"><Input required type="number" step="any" value={trialEditor.allowedMinimum} onChange={event => setTrialEditor({ ...trialEditor, allowedMinimum: event.target.value })} /></Field>
                    <Field label="允许上限"><Input required type="number" step="any" value={trialEditor.allowedMaximum} onChange={event => setTrialEditor({ ...trialEditor, allowedMaximum: event.target.value })} /></Field>
                    <Field label="安全约束代码"><Input required placeholder="例如 temperature.max" value={trialEditor.constraintCode} onChange={event => setTrialEditor({ ...trialEditor, constraintCode: event.target.value })} /></Field>
                    <Field label="安全约束说明"><Input required value={trialEditor.constraintDescription} onChange={event => setTrialEditor({ ...trialEditor, constraintDescription: event.target.value })} /></Field>
                    <Field label="约束关系">
                      <Select value={trialEditor.constraintOperator} onChange={event => setTrialEditor({ ...trialEditor, constraintOperator: event.target.value })}>
                        <option value="<=">不高于</option>
                        <option value=">=">不低于</option>
                      </Select>
                    </Field>
                    <Field label="安全限值"><Input required type="number" step="any" value={trialEditor.constraintLimit} onChange={event => setTrialEditor({ ...trialEditor, constraintLimit: event.target.value })} /></Field>
                    <Field label="安全限值单位"><Input required value={trialEditor.constraintUnit} onChange={event => setTrialEditor({ ...trialEditor, constraintUnit: event.target.value })} /></Field>
                    <Field label="基准周期"><Input placeholder="多个周期用逗号分隔" value={trialEditor.controlCycleIds} onChange={event => setTrialEditor({ ...trialEditor, controlCycleIds: event.target.value })} /></Field>
                    <Field label="试验周期"><Input placeholder="多个周期用逗号分隔" value={trialEditor.trialCycleIds} onChange={event => setTrialEditor({ ...trialEditor, trialCycleIds: event.target.value })} /></Field>
                    <Field label="停止规则" className="lg:col-span-3"><Textarea required value={trialEditor.stopRule} onChange={event => setTrialEditor({ ...trialEditor, stopRule: event.target.value })} /></Field>
                    <Field label="回退方案" className="lg:col-span-3"><Textarea required value={trialEditor.rollbackPlan} onChange={event => setTrialEditor({ ...trialEditor, rollbackPlan: event.target.value })} /></Field>
                    <div className="flex gap-2 lg:col-span-3">
                      <Button
                        variant="primary"
                        disabled={busy || !trialEditor.causeId || !trialEditor.name.trim() || !trialEditor.parameterCode.trim() || !trialEditor.unit.trim() ||
                          [trialEditor.baselineValue, trialEditor.trialValue, trialEditor.allowedMinimum, trialEditor.allowedMaximum, trialEditor.constraintLimit].some(value => value === "") ||
                          Number(trialEditor.allowedMinimum) > Number(trialEditor.allowedMaximum) ||
                          Number(trialEditor.trialValue) < Number(trialEditor.allowedMinimum) ||
                          Number(trialEditor.trialValue) > Number(trialEditor.allowedMaximum) ||
                          !trialEditor.constraintCode.trim() || !trialEditor.constraintDescription.trim() || !trialEditor.constraintUnit.trim() ||
                          !trialEditor.stopRule.trim() || !trialEditor.rollbackPlan.trim()}
                        onClick={submitTrial}
                      >
                        保存试验方案
                      </Button>
                      <Button disabled={busy} onClick={() => setWorkflowMode("")}>取消</Button>
                    </div>
                  </div>
                </Card>
              )}
              {workflowMode === "result" && (
                <Card title="登记探索性试验结果" description="登记的是现场已经测得的结果；系统自动计算变化量。">
                  <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
                    <Field label="运行中试验">
                      <Select required value={resultEditor.trialId} onChange={event => setResultEditor({ ...resultEditor, trialId: event.target.value })}>
                        {runningExploratoryTrials.map(trial => <option key={trial.trialId} value={trial.trialId}>{trial.name}</option>)}
                      </Select>
                    </Field>
                    <Field label="结果指标"><Input required placeholder="例如 defect.rate" value={resultEditor.metricCode} onChange={event => setResultEditor({ ...resultEditor, metricCode: event.target.value })} /></Field>
                    <Field label="单位"><Input required value={resultEditor.unit} onChange={event => setResultEditor({ ...resultEditor, unit: event.target.value })} /></Field>
                    <Field label="基准值"><Input required type="number" step="any" value={resultEditor.baselineValue} onChange={event => setResultEditor({ ...resultEditor, baselineValue: event.target.value })} /></Field>
                    <Field label="试验值"><Input required type="number" step="any" value={resultEditor.trialValue} onChange={event => setResultEditor({ ...resultEditor, trialValue: event.target.value })} /></Field>
                    <Metric label="变化量" value={resultEditor.baselineValue !== "" && resultEditor.trialValue !== "" ? formatDecimal(Number(resultEditor.trialValue) - Number(resultEditor.baselineValue)) : "—"} />
                    <Field label="基准样本数"><Input required type="number" min="1" value={resultEditor.baselineSampleCount} onChange={event => setResultEditor({ ...resultEditor, baselineSampleCount: event.target.value })} /></Field>
                    <Field label="试验样本数"><Input required type="number" min="1" value={resultEditor.trialSampleCount} onChange={event => setResultEditor({ ...resultEditor, trialSampleCount: event.target.value })} /></Field>
                    <label className="flex items-center gap-2 self-end pb-2 text-sm"><input type="checkbox" checked={resultEditor.safetyPassed} onChange={event => setResultEditor({ ...resultEditor, safetyPassed: event.target.checked })} />安全检查通过</label>
                    <div className="flex gap-2 lg:col-span-3">
                      <Button variant="primary" disabled={busy || !resultEditor.trialId || !resultEditor.metricCode.trim() || !resultEditor.unit.trim() || resultEditor.baselineValue === "" || resultEditor.trialValue === "" || Number(resultEditor.baselineSampleCount) < 1 || Number(resultEditor.trialSampleCount) < 1} onClick={submitResult}>保存结果</Button>
                      <Button disabled={busy} onClick={() => setWorkflowMode("")}>取消</Button>
                    </div>
                  </div>
                </Card>
              )}
              {workflowMode === "conclusion" && (
                <Card title="形成调查结论" description="结论会引用该试验的全部结果，并关闭本次调查。">
                  <div className="grid gap-4 sm:grid-cols-2">
                    <Field label="已完成试验">
                      <Select required value={conclusionEditor.trialId} onChange={event => setConclusionEditor({ ...conclusionEditor, trialId: event.target.value })}>
                        {completedTrials.map(trial => <option key={trial.trialId} value={trial.trialId}>{trial.name}</option>)}
                      </Select>
                    </Field>
                    <Field label="结论">
                      <Select value={conclusionEditor.decision} onChange={event => setConclusionEditor({ ...conclusionEditor, decision: event.target.value })}>
                        <option value="confirmed">原因确认</option>
                        <option value="rejected">原因排除</option>
                        <option value="inconclusive">暂不确定</option>
                      </Select>
                    </Field>
                    <Field label="结论说明" className="sm:col-span-2"><Textarea required value={conclusionEditor.summary} onChange={event => setConclusionEditor({ ...conclusionEditor, summary: event.target.value })} /></Field>
                    <div className="flex gap-2 sm:col-span-2">
                      <Button variant="primary" disabled={busy || !conclusionEditor.trialId || !conclusionEditor.summary.trim()} onClick={submitConclusion}>确认结论</Button>
                      <Button disabled={busy} onClick={() => setWorkflowMode("")}>取消</Button>
                    </div>
                  </div>
                </Card>
              )}
              <Card title="问题范围">
                <div className="grid gap-4 text-sm sm:grid-cols-2">
                  <p><span className="text-slate-500">问题代码：</span>{resource.problemCode}</p>
                  <p><span className="text-slate-500">负责人：</span>{resource.ownerUserId || "—"}</p>
                  <p className="whitespace-pre-wrap leading-6 sm:col-span-2">{resource.description || "暂无问题说明"}</p>
                  <div className="sm:col-span-2">
                    <p className="mb-2 text-slate-500">关联周期</p>
                    <div className="flex flex-wrap gap-2">
                      {(resource.cycleIds || []).map(cycleId => (
                        <Link key={cycleId} className="text-sm font-medium text-blue-600 hover:text-blue-700" to={`/cycles/${encodeURIComponent(cycleId)}`}>{cycleId}</Link>
                      ))}
                      {!resource.cycleIds?.length && <span className="text-slate-500">未指定周期</span>}
                    </div>
                  </div>
                </div>
              </Card>
              <Card title="可能原因" description="原因会随试验和结论逐步确认或排除。">
                <DataTable
                  rows={detail.causes || []}
                  keyField="causeId"
                  columns={[
                    { key: "title", label: "原因" },
                    { key: "parameterCode", label: "参数", render: value => value || "—" },
                    { key: "signalCode", label: "信号", render: value => value || "—" },
                    { key: "direction", label: "影响方向" },
                    { key: "status", label: "状态", render: value => <StatusBadge value={value} /> },
                    { key: "reasoning", label: "依据" },
                  ]}
                />
              </Card>
              <Card title="调整试验" description="试验需要经过批准、执行、结果计算和完成四个阶段。">
                <DataTable
                  rows={detail.trials || []}
                  keyField="trialId"
                  columns={[
                    { key: "name", label: "试验" },
                    { key: "rigorLevel", label: "严谨度", render: value => value === "confirmatory" ? "验证性试验" : "探索性试验" },
                    { key: "status", label: "状态", render: value => <StatusBadge value={value} /> },
                    { key: "approvedBy", label: "批准人", render: value => value || "—" },
                    {
                      key: "_action",
                      label: "下一步",
                      render: (_value, trial) => (
                        <div className="flex min-w-max flex-wrap gap-1">
                          {trial.status === "planned" && <Button variant="primary" disabled={busy} onClick={() => onTrialStatus(trial.trialId, "approved")}>批准试验</Button>}
                          {trial.status === "approved" && <Button variant="primary" disabled={busy} onClick={() => onTrialStatus(trial.trialId, "running")}>开始试验</Button>}
                          {trial.status === "running" && trial.rigorLevel === "confirmatory" && <Button disabled={busy} onClick={() => onCalculateTrialResult(trial.trialId)}>计算结果</Button>}
                          {trial.status === "running" && <Button variant="primary" disabled={busy || !(detail.results?.[trial.trialId]?.length)} onClick={() => onTrialStatus(trial.trialId, "completed")}>完成试验</Button>}
                          {["planned", "approved", "running"].includes(trial.status) && <Button variant="ghost" className="text-rose-700" disabled={busy} onClick={() => onTrialStatus(trial.trialId, "cancelled")}>取消</Button>}
                          {!["planned", "approved", "running"].includes(trial.status) && <span className="text-slate-500">流程已结束</span>}
                        </div>
                      ),
                    },
                  ]}
                />
              </Card>
              {(detail.trials || []).some(trial => detail.results?.[trial.trialId]?.length) && (
                <Card title="试验结果">
                  <DataTable
                    rows={(detail.trials || []).flatMap(trial => (detail.results?.[trial.trialId] || []).map(result => ({ ...result, trialName: trial.name })))}
                    keyField="resultId"
                    columns={[
                      { key: "trialName", label: "试验" },
                      { key: "metricCode", label: "指标" },
                      { key: "baselineValue", label: "基准", render: formatDecimal },
                      { key: "trialValue", label: "试验值", render: formatDecimal },
                      { key: "effectValue", label: "变化", render: formatDecimal },
                      { key: "safetyPassed", label: "安全检查", render: value => <StatusBadge value={value ? "passed" : "failed"} /> },
                    ]}
                  />
                </Card>
              )}
              <Card title="调查结论">
                <DataTable
                  rows={detail.conclusions || []}
                  keyField="conclusionId"
                  columns={[
                    { key: "decision", label: "结论", render: value => <StatusBadge value={value} /> },
                    { key: "summary", label: "说明" },
                    { key: "reviewedBy", label: "复核人" },
                    { key: "reviewedAt", label: "复核时间", render: formatTime },
                  ]}
                />
              </Card>
            </>
          )}

          {definition.label === "数据模型" && (
            <>
              <Card title="模型验证与监测" description="草稿或暂停状态可登记离线评估；运行中或暂停状态可登记漂移观测。">
                <div className="flex flex-wrap gap-2">
                  <Button variant="primary" disabled={!["draft", "validated", "suspended"].includes(status)} onClick={() => openWorkflow("evaluation")}>登记模型评估</Button>
                  <Button disabled={!["active", "suspended"].includes(status)} onClick={() => openWorkflow("drift")}>登记漂移观测</Button>
                </div>
              </Card>
              {workflowMode === "evaluation" && (
                <Card title="登记模型评估" description="评估是否通过由指标值和门槛自动判断。">
                  <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
                    <Field label="数据分组">
                      <Select value={evaluationEditor.split} onChange={event => setEvaluationEditor({ ...evaluationEditor, split: event.target.value })}>
                        <option value="holdout">留出集</option>
                        <option value="validation">验证集</option>
                        <option value="cross-validation">交叉验证</option>
                      </Select>
                    </Field>
                    <Field label="样本数"><Input required type="number" min="1" value={evaluationEditor.sampleCount} onChange={event => setEvaluationEditor({ ...evaluationEditor, sampleCount: event.target.value })} /></Field>
                    <Field label="评估指标"><Input required placeholder="例如 mae" value={evaluationEditor.metricCode} onChange={event => setEvaluationEditor({ ...evaluationEditor, metricCode: event.target.value })} /></Field>
                    <Field label="指标值"><Input required type="number" step="any" value={evaluationEditor.value} onChange={event => setEvaluationEditor({ ...evaluationEditor, value: event.target.value })} /></Field>
                    <Field label="单位"><Input placeholder="可选" value={evaluationEditor.unit} onChange={event => setEvaluationEditor({ ...evaluationEditor, unit: event.target.value })} /></Field>
                    <Field label="最低门槛"><Input type="number" step="any" placeholder="可选" value={evaluationEditor.requiredMinimum} onChange={event => setEvaluationEditor({ ...evaluationEditor, requiredMinimum: event.target.value })} /></Field>
                    <Field label="最高门槛"><Input type="number" step="any" placeholder="可选" value={evaluationEditor.requiredMaximum} onChange={event => setEvaluationEditor({ ...evaluationEditor, requiredMaximum: event.target.value })} /></Field>
                    <Field label="评估说明" className="sm:col-span-2 lg:col-span-3"><Textarea value={evaluationEditor.notes} onChange={event => setEvaluationEditor({ ...evaluationEditor, notes: event.target.value })} /></Field>
                    <div className="flex gap-2 sm:col-span-2 lg:col-span-3">
                      <Button
                        variant="primary"
                        disabled={busy || Number(evaluationEditor.sampleCount) < 1 || !evaluationEditor.metricCode.trim() || evaluationEditor.value === "" ||
                          (evaluationEditor.requiredMinimum !== "" && evaluationEditor.requiredMaximum !== "" && Number(evaluationEditor.requiredMinimum) > Number(evaluationEditor.requiredMaximum))}
                        onClick={submitEvaluation}
                      >
                        保存评估
                      </Button>
                      <Button disabled={busy} onClick={() => setWorkflowMode("")}>取消</Button>
                    </div>
                  </div>
                </Card>
              )}
              {workflowMode === "drift" && (
                <Card title="登记漂移观测" description="达到停用线时，运行中的模型会自动暂停。">
                  <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
                    <Field label="漂移指标"><Input required placeholder="例如 psi" value={driftEditor.metricCode} onChange={event => setDriftEditor({ ...driftEditor, metricCode: event.target.value })} /></Field>
                    <Field label="当前值"><Input required type="number" min="0" step="any" value={driftEditor.value} onChange={event => setDriftEditor({ ...driftEditor, value: event.target.value })} /></Field>
                    <Field label="样本数"><Input required type="number" min="1" value={driftEditor.sampleCount} onChange={event => setDriftEditor({ ...driftEditor, sampleCount: event.target.value })} /></Field>
                    <Field label="预警线"><Input required type="number" min="0" step="any" value={driftEditor.warningThreshold} onChange={event => setDriftEditor({ ...driftEditor, warningThreshold: event.target.value })} /></Field>
                    <Field label="停用线"><Input required type="number" min="0" step="any" value={driftEditor.stopThreshold} onChange={event => setDriftEditor({ ...driftEditor, stopThreshold: event.target.value })} /></Field>
                    <Field label="观察开始"><Input required type="datetime-local" value={driftEditor.windowStart} onChange={event => setDriftEditor({ ...driftEditor, windowStart: event.target.value })} /></Field>
                    <Field label="观察结束"><Input required type="datetime-local" value={driftEditor.windowEnd} onChange={event => setDriftEditor({ ...driftEditor, windowEnd: event.target.value })} /></Field>
                    <div className="flex gap-2 sm:col-span-2 lg:col-span-3">
                      <Button
                        variant="primary"
                        disabled={busy || !driftEditor.metricCode.trim() || driftEditor.value === "" || Number(driftEditor.warningThreshold) < 0 ||
                          Number(driftEditor.stopThreshold) <= Number(driftEditor.warningThreshold) || Number(driftEditor.sampleCount) < 1 ||
                          !driftEditor.windowStart || !driftEditor.windowEnd || new Date(driftEditor.windowEnd) <= new Date(driftEditor.windowStart)}
                        onClick={submitDrift}
                      >
                        保存漂移观测
                      </Button>
                      <Button disabled={busy} onClick={() => setWorkflowMode("")}>取消</Button>
                    </div>
                  </div>
                </Card>
              )}
              <Card title="模型版本">
                <dl className="grid gap-4 text-sm sm:grid-cols-2 lg:grid-cols-3">
                  {[
                    ["模型", `${resource.name} · ${resource.modelId} v${resource.version}`],
                    ["用途", resource.modelKind],
                    ["算法", resource.algorithm],
                    ["训练数据", `${resource.datasetId} v${resource.datasetVersion}`],
                    ["输出", resource.outputCode],
                    ["不确定性方法", resource.uncertaintyMethod],
                  ].map(([label, value]) => <div key={label}><dt className="text-slate-500">{label}</dt><dd className="mt-1 font-medium text-slate-800">{value || "—"}</dd></div>)}
                </dl>
              </Card>
              <Card title="评估记录" description="模型至少需要一条通过门槛的评估，才能进入验证或运行状态。">
                <DataTable
                  rows={detail.evaluations || []}
                  keyField="evaluationId"
                  columns={[
                    { key: "split", label: "数据分组" },
                    { key: "sampleCount", label: "样本数", render: formatInteger },
                    { key: "passed", label: "结论", render: value => <StatusBadge value={value ? "passed" : "failed"} /> },
                    { key: "evaluatedBy", label: "评估人" },
                    { key: "evaluatedAt", label: "评估时间", render: formatTime },
                  ]}
                />
              </Card>
              <Card title="漂移监测">
                <DataTable
                  rows={detail.driftReadings || []}
                  keyField="readingId"
                  columns={[
                    { key: "metricCode", label: "指标" },
                    { key: "value", label: "当前值", render: formatDecimal },
                    { key: "warningThreshold", label: "预警线", render: formatDecimal },
                    { key: "stopThreshold", label: "停用线", render: formatDecimal },
                    { key: "sampleCount", label: "样本数", render: formatInteger },
                    { key: "recordedAt", label: "记录时间", render: formatTime },
                  ]}
                />
              </Card>
            </>
          )}

          {definition.label === "知识" && (
            <>
              <Card
                title="原始来源"
                description={`${resource.fileName} · ${Math.ceil((resource.sizeBytes || 0) / 1024)} KiB`}
                actions={(
                  <div className="flex flex-wrap gap-2">
                    <a
                      className="inline-flex min-h-9 items-center rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
                      href={`/api/v1/process-knowledge/${encodeURIComponent(resource.sourceId)}/content`}
                      target="_blank"
                      rel="noreferrer"
                    >
                      查看原文件
                    </a>
                    <Button disabled={busy} onClick={onExtract}>重新解析</Button>
                  </div>
                )}
              >
                <div className="grid gap-3 text-sm sm:grid-cols-2">
                  <p><span className="text-slate-500">解析状态：</span>{resource.extractionStatus || "—"}</p>
                  <p><span className="text-slate-500">解析器：</span>{resource.extractorVersion || "—"}</p>
                  <p className="break-all sm:col-span-2"><span className="text-slate-500">SHA-256：</span>{resource.sha256}</p>
                  {resource.extractionError && <Alert tone="danger">{resource.extractionError}</Alert>}
                </div>
              </Card>
              <Card
                title="引用定位与人工复核"
                description="逐条核对提取内容与页码、工作表/单元格或图片区域；全部完成后才能确认来源。"
              >
                {records.length ? (
                  <div className="grid gap-3">
                    {records.map(record => (
                      <article key={record.recordId} className="rounded-xl border border-slate-200 p-4">
                        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                          <div className="min-w-0">
                            <div className="flex flex-wrap items-center gap-2">
                              <Badge tone={record.humanReviewed ? "success" : "warning"}>
                                {record.humanReviewed ? "已人工复核" : "待人工复核"}
                              </Badge>
                              <span className="text-xs text-slate-500">{knowledgeLocation(record)}</span>
                              {record.extractionConfidence != null && (
                                <span className="text-xs text-slate-500">置信度 {Math.round(record.extractionConfidence * 100)}%</span>
                              )}
                            </div>
                            <p className="mt-3 whitespace-pre-wrap text-sm leading-6 text-slate-700">{record.content}</p>
                            {record.citation?.contentHash && (
                              <p className="mt-2 break-all text-xs text-slate-400">内容指纹：{record.citation.contentHash}</p>
                            )}
                          </div>
                          {!record.humanReviewed && (
                            <Button variant="primary" disabled={busy} onClick={() => onReviewRecord(record)}>确认与原文一致</Button>
                          )}
                        </div>
                      </article>
                    ))}
                  </div>
                ) : <EmptyState title="尚无可复核内容" description="重新解析文件，或确认该文件格式是否受支持。" />}
              </Card>
              {status === "indexed" && !allReviewed && (
                <Alert tone="warning" title="尚不能确认来源">
                  必须至少有一条知识记录，且所有记录均经人工复核。
                </Alert>
              )}
            </>
          )}

          {definition.label === "融合执行" && (
            <Card title="受控执行" description="使用已启用定义执行一次可重放计算，不会向现场设备写入参数。">
              <div className="grid gap-4">
                <BusinessObjectEditor value={executionEditor} onChange={setExecutionEditor} />
                <div><Button variant="primary" disabled={busy || status !== "active"} onClick={onExecute}>执行融合计算</Button></div>
                {status !== "active" && <Alert tone="warning">融合定义和引用的机理模型都启用后才能执行。</Alert>}
                {executionResult && (
                  <div className="grid gap-3">
                    <div className="grid gap-3 sm:grid-cols-3">
                      <Metric label="机理预测" value={executionResult.mechanismPrediction} />
                      <Metric label="数据预测" value={executionResult.dataPrediction} />
                      <Metric label="融合输出" value={executionResult.fusedPrediction ?? "特征已生成"} />
                    </div>
                    <Alert tone="success" title="执行完成">
                      输出 {executionResult.outputCode}（{executionResult.outputUnit}），执行指纹 {executionResult.executionHash}
                    </Alert>
                  </div>
                )}
              </div>
            </Card>
          )}

          {definition.label === "机理模型" && (
            <div className="grid gap-4">
              <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                <Metric label="模型" value={resource.modelId} />
                <Metric label="版本" value={resource.version} />
                <Metric label="关系形式" value={resource.equationKind === "affine" ? "线性关系" : resource.equationKind} />
                <Metric label="常数项" value={formatDecimal(resource.intercept)} />
              </div>
              <Card title="输入与系数" description="列出计算使用的变量、适用范围和影响系数。">
                <DataTable
                  rows={(resource.inputs || []).map(input => ({
                    ...input,
                    coefficient: resource.coefficients?.[input.code],
                    validRange: `${input.validMinimum ?? "不限"} ～ ${input.validMaximum ?? "不限"}`,
                  }))}
                  keyField="code"
                  columns={[
                    { key: "code", label: "输入变量" },
                    { key: "unit", label: "单位" },
                    { key: "validRange", label: "有效范围" },
                    { key: "coefficient", label: "影响系数", render: formatDecimal },
                  ]}
                />
              </Card>
              <Card title="输出与依据">
                <div className="grid gap-5 text-sm text-slate-700 md:grid-cols-2">
                  <div>
                    <p className="font-medium text-slate-950">输出变量</p>
                    <p className="mt-2">{resource.output?.code || "—"} · {resource.output?.unit || "无单位"}</p>
                    <p className="mt-1 text-slate-500">有效范围：{resource.output?.validMinimum ?? "不限"} ～ {resource.output?.validMaximum ?? "不限"}</p>
                  </div>
                  <div>
                    <p className="font-medium text-slate-950">科学依据</p>
                    <p className="mt-2 whitespace-pre-wrap leading-6">{resource.scientificBasis || "尚未填写"}</p>
                    {resource.sourceReference && <p className="mt-1 text-slate-500">来源：{resource.sourceReference}</p>}
                  </div>
                </div>
                {Object.keys(resource.applicabilityContext || {}).length > 0 && (
                  <div className="mt-5 border-t border-slate-100 pt-4">
                    <p className="text-sm font-medium text-slate-950">适用条件</p>
                    <div className="mt-2 flex flex-wrap gap-2">
                      {Object.entries(resource.applicabilityContext).map(([key, value]) => (
                        <Badge key={key} tone="neutral">{key}：{value}</Badge>
                      ))}
                    </div>
                  </div>
                )}
              </Card>
            </div>
          )}

          {definition.label === "参数建议" && (
            <>
              <Card title="建议内容" description={resource.title}>
                <DataTable
                  rows={resource.parameterSettings || []}
                  getRowKey={(row, index) => `${row.parameterCode}:${row.phaseCode || "all"}:${index}`}
                  columns={[
                    { key: "parameterCode", label: "参数" },
                    { key: "phaseCode", label: "阶段", render: value => value || "全周期" },
                    { key: "currentValue", label: "当前值", render: formatDecimal },
                    { key: "recommendedValue", label: "建议值", render: formatDecimal },
                    { key: "unit", label: "单位" },
                    { key: "allowedMinimum", label: "允许下限", render: formatDecimal },
                    { key: "allowedMaximum", label: "允许上限", render: formatDecimal },
                  ]}
                />
              </Card>
              <div className="grid gap-5 xl:grid-cols-2">
                <Card title="风险控制">
                  <div className="space-y-4 text-sm leading-6 text-slate-700">
                    <div><p className="font-medium text-slate-950">风险说明</p><p>{resource.riskSummary}</p></div>
                    <div><p className="font-medium text-slate-950">停止规则</p><p>{resource.stopRule}</p></div>
                    <div><p className="font-medium text-slate-950">回退方案</p><p>{resource.rollbackPlan}</p></div>
                  </div>
                </Card>
                <Card title="预期价值">
                  <div className="grid gap-3 sm:grid-cols-2">
                    <Metric label="预期年价值" value={`${formatDecimal(resource.valueEstimate?.expectedAnnualValue)} ${resource.valueEstimate?.currency || ""}`} />
                    <Metric label="实施成本" value={`${formatDecimal(resource.valueEstimate?.implementationCost)} ${resource.valueEstimate?.currency || ""}`} />
                    <Metric label="试验成本" value={`${formatDecimal(resource.valueEstimate?.trialCost)} ${resource.valueEstimate?.currency || ""}`} />
                    <Metric label="潜在损失" value={`${formatDecimal(resource.valueEstimate?.downsideAtRisk)} ${resource.valueEstimate?.currency || ""}`} />
                  </div>
                </Card>
              </div>
              {["approved", "rollback-required"].includes(status) && (
                <Card title={status === "approved" ? "执行登记" : "回退登记"} description="这里只登记现场已经完成的受控操作编号。">
                  <Field label={status === "approved" ? "执行编号" : "回退执行编号"}>
                    <Input value={executionEditor.executionReference || ""} onChange={event => setExecutionEditor({ ...executionEditor, executionReference: event.target.value })} />
                  </Field>
                </Card>
              )}
              {status === "executed" && (
                <Card title="实际效果确认" description="填写每项实际结果和价值测量，平台会自动判断是否达到目标及是否需要回退。">
                  <div className="grid gap-4">
                    {(executionEditor.outcomes || []).map((outcome, index) => (
                      <div key={outcome.metricCode} className="grid gap-3 rounded-xl border border-slate-200 p-4 sm:grid-cols-3">
                        <Field label="指标"><Input value={outcome.metricCode} disabled /></Field>
                        <Field label="基准值"><Input type="number" step="any" value={outcome.baselineValue} onChange={event => setExecutionEditor({ ...executionEditor, outcomes: executionEditor.outcomes.map((item, rowIndex) => rowIndex === index ? { ...item, baselineValue: event.target.value } : item) })} /></Field>
                        <Field label="实际值"><Input type="number" step="any" value={outcome.actualValue} onChange={event => setExecutionEditor({ ...executionEditor, outcomes: executionEditor.outcomes.map((item, rowIndex) => rowIndex === index ? { ...item, actualValue: event.target.value } : item) })} /></Field>
                        <Field label="基准样本数"><Input type="number" min="1" value={outcome.baselineSampleCount} onChange={event => setExecutionEditor({ ...executionEditor, outcomes: executionEditor.outcomes.map((item, rowIndex) => rowIndex === index ? { ...item, baselineSampleCount: event.target.value } : item) })} /></Field>
                        <Field label="实际样本数"><Input type="number" min="1" value={outcome.actualSampleCount} onChange={event => setExecutionEditor({ ...executionEditor, outcomes: executionEditor.outcomes.map((item, rowIndex) => rowIndex === index ? { ...item, actualSampleCount: event.target.value } : item) })} /></Field>
                        <label className="flex items-center gap-2 self-end pb-2 text-sm"><input type="checkbox" checked={outcome.safetyPassed} onChange={event => setExecutionEditor({ ...executionEditor, outcomes: executionEditor.outcomes.map((item, rowIndex) => rowIndex === index ? { ...item, safetyPassed: event.target.checked } : item) })} />安全检查通过</label>
                      </div>
                    ))}
                    <div className="grid gap-3 sm:grid-cols-2">
                      <Field label="价值测量开始"><Input type="datetime-local" value={executionEditor.realizedValue?.windowStart || ""} onChange={event => setExecutionEditor({ ...executionEditor, realizedValue: { ...executionEditor.realizedValue, windowStart: event.target.value } })} /></Field>
                      <Field label="价值测量结束"><Input type="datetime-local" value={executionEditor.realizedValue?.windowEnd || ""} onChange={event => setExecutionEditor({ ...executionEditor, realizedValue: { ...executionEditor.realizedValue, windowEnd: event.target.value } })} /></Field>
                      <Field label="产生价值"><Input type="number" step="any" value={executionEditor.realizedValue?.grossValue ?? ""} onChange={event => setExecutionEditor({ ...executionEditor, realizedValue: { ...executionEditor.realizedValue, grossValue: event.target.value } })} /></Field>
                      <Field label="实际实施成本"><Input type="number" min="0" step="any" value={executionEditor.realizedValue?.implementationCost ?? ""} onChange={event => setExecutionEditor({ ...executionEditor, realizedValue: { ...executionEditor.realizedValue, implementationCost: event.target.value } })} /></Field>
                      <Field label="价值计算说明" className="sm:col-span-2"><Textarea value={executionEditor.realizedValue?.calculationNote || ""} onChange={event => setExecutionEditor({ ...executionEditor, realizedValue: { ...executionEditor.realizedValue, calculationNote: event.target.value } })} /></Field>
                      <Field label="效果说明" className="sm:col-span-2"><Textarea value={executionEditor.notes || ""} onChange={event => setExecutionEditor({ ...executionEditor, notes: event.target.value })} /></Field>
                    </div>
                  </div>
                </Card>
              )}
            </>
          )}

          {detail?.audit?.length > 0 && (
            <Card title="审计记录">
              <DataTable
                rows={detail.audit}
                keyField="entryId"
                columns={[
                  { key: "createdAt", label: "时间", render: formatTime },
                  { key: "action", label: "操作" },
                  { key: "userId", label: "人员" },
                  { key: "toStatus", label: "结果", render: value => <StatusBadge value={value} /> },
                ]}
              />
            </Card>
          )}
        </div>
      ) : !loading && <EmptyState title="详情不可用" description="请选择记录后重试。" />}
    </Drawer>
  );
}

function knowledgeLocation(record) {
  const citation = record.citation || {};
  if (citation.locationKind === "pdf-page" || citation.pageNumber) return `PDF 第 ${citation.pageNumber || record.pageOrSheet} 页`;
  if (citation.sheetName || citation.cellRange) return `${citation.sheetName || record.pageOrSheet || "工作表"} ${citation.cellRange || ""}`.trim();
  if (citation.region || record.region) return `图片区域 ${citation.region || record.region}`;
  return record.pageOrSheet || "来源位置未标注";
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
    title: "设备采集", description: "选择现场节点、设备和采集方式，让设备数据持续进入平台。", endpoint: "/api/v1/acquisition-profiles", key: "profileId",
    columns: [["subjectId", "设备"], ["edgeId", "现场节点"], ["name", "采集任务"], ["protocol", "采集方式"], ["status", "状态"]],
    render: { protocol: value => acquisitionProtocolLabels[value] || value },
    createLabel: "接入设备",
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
        <Card title="设备接入进度" description="完成采集任务并发布后，设备会自动出现在“设备与对象”和周期记录中。">
          <div className="grid gap-4 md:grid-cols-4">
            {[
              ["1", "现场节点在线", "确认设备所在节点能够正常上报心跳。", "/edges", "查看节点"],
              ["2", "选择数据模型", "数据模型决定要采集哪些工艺量。", "/configuration/process-data-models", "查看模型"],
              ["3", "配置并发布", "选择设备连接方式并映射实际数据项。", null, `${rows.filter(row => row.status === "published").length} 个已发布`],
              ["4", "确认数据到达", "在设备与对象中确认设备、样本和最后活动时间。", "/explorer", "查看设备与对象"],
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
      actions={<Link className="inline-flex min-h-9 items-center rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700" to="/configuration/acquisition-profiles">接入设备</Link>}
    >
      {error && <Alert tone="danger" title="现场节点暂不可用">{error}</Alert>}
      <div className="grid gap-4 sm:grid-cols-3">
        <Metric label="现场节点" value={rows.length} hint="已登记" />
        <Metric label="当前在线" value={online} hint="30 秒内有心跳" />
        <Metric label="需要处理" value={rows.filter(row => edgeStatus(row) !== "online").length} hint="离线或运行异常" />
      </div>
      {loading && !data ? <LoadingCard /> : (
        <Card title="节点状态" description="节点在线后即可承载设备采集任务。">
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
  const edge = extractRows(edges.data).find(row => row.edgeId === edgeId);
  const tasks = acquisition.data?.tasks || [];
  const error = edges.error || acquisition.error || metrics.error || logs.error;
  const outboxBacklog = metricTotal(metrics.data, "event_outbox_backlog");
  const shipped = metricTotal(metrics.data, "event_shipped_total");
  const emitted = metricTotal(metrics.data, "event_emitted_total");
  const recentLogs = extractRows(logs.data);

  return (
    <Page
      title={edgeId || "节点诊断"}
      description="从连接、采集、上行和最近日志判断现场节点是否正常。"
      actions={(
        <>
          <Link className="inline-flex min-h-9 items-center rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50" to="/edges">返回现场节点</Link>
          <Link className="inline-flex min-h-9 items-center rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700" to="/configuration/acquisition-profiles">配置设备</Link>
        </>
      )}
    >
      {error && <Alert tone="danger" title="部分诊断信息暂不可用">{error}</Alert>}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Metric label="节点连接" value={<StatusBadge value={edgeStatus(edge)} />} hint={edge?.lastSeen ? `最后心跳 ${formatTime(edge.lastSeen)}` : "尚未收到心跳"} />
        <Metric label="采集运行" value={<StatusBadge value={acquisition.data?.state || "unknown"} />} hint={`${tasks.length} 个任务`} />
        <Metric label="已采集样本" value={formatInteger(acquisition.data?.samplesCollected)} hint={acquisition.data?.lastSuccessAt ? `最近成功 ${formatTime(acquisition.data.lastSuccessAt)}` : "尚无成功记录"} />
        <Metric label="待上行事件" value={formatInteger(outboxBacklog)} hint={`已确认 ${formatInteger(shipped)} / 已产生 ${formatInteger(emitted)}`} />
      </div>
      {(edge?.lastError || acquisition.data?.lastError || outboxBacklog > 0) ? (
        <Alert tone="warning" title="节点需要关注">
          <ul className="list-disc space-y-1 pl-5">
            {edge?.lastError && <li>{edge.lastError}</li>}
            {acquisition.data?.lastError && <li>{acquisition.data.lastError}</li>}
            {outboxBacklog > 0 && <li>仍有 {formatInteger(outboxBacklog)} 条事件等待上行。</li>}
          </ul>
        </Alert>
      ) : <Alert tone="success" title="节点运行正常">心跳、设备采集和事件上行均未发现待处理问题。</Alert>}
      <Card title="采集任务" description="每个任务对应一台设备或一套设备数据来源。">
        <DataTable
          rows={tasks}
          keyField="configurationKey"
          columns={[
            { key: "configurationKey", label: "任务版本" },
            { key: "state", label: "状态", render: value => <StatusBadge value={value} /> },
            { key: "samplesCollected", label: "已采样", render: formatInteger },
            { key: "observedIntervalMs", label: "实际间隔", render: value => formatDuration(value) },
            { key: "lastReadDurationMs", label: "最近读取耗时", render: value => formatDuration(value) },
            { key: "lastSuccessAt", label: "最近成功", render: formatTime },
            { key: "lastError", label: "最近问题", render: value => value || "无" },
          ]}
        />
      </Card>
      <Card title="最近日志" description={`最近 ${recentLogs.length} 条 · 共 ${logs.data?.total ?? recentLogs.length} 条`}>
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
