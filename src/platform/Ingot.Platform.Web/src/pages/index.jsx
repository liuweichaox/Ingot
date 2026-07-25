import { Tab, TabGroup, TabList, TabPanel, TabPanels } from "@headlessui/react";
import {
  ArrowPathIcon,
  BoltIcon,
  CheckCircleIcon,
  ExclamationTriangleIcon,
  MagnifyingGlassIcon,
  PaperAirplaneIcon,
} from "@heroicons/react/24/outline";
import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useLocation, useNavigate, useSearchParams } from "react-router-dom";
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
} from "../ui/components";

const formatTime = value => value ? new Date(value).toLocaleString("zh-CN") : "—";

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

function ResourcePage({ title, description, endpoint, columns, keyField, emptyDescription, interval = 0, actions }) {
  const { data, loading, error } = useApi(endpoint, { interval });
  const rows = extractRows(data);
  return (
    <Page title={title} description={description} actions={actions}>
      {error && <Alert tone="danger" title="数据暂不可用">{error}</Alert>}
      {loading && !data ? <LoadingCard /> : (
        <Card title={`${title}列表`} description={`共 ${data?.total ?? rows.length} 条记录`}>
          {rows.length
            ? <DataTable columns={columns} rows={rows} keyField={keyField} />
            : <EmptyState description={emptyDescription} />}
        </Card>
      )}
    </Page>
  );
}

export function WorkbenchPage() {
  const [state, setState] = useState({ loading: true, error: "", cycles: [], summary: {}, events: [], edges: [], contexts: [] });
  useEffect(() => {
    let alive = true;
    Promise.all([
      getJson("/api/v1/cycles?limit=50"),
      getJson("/api/v1/inspection-tasks/summary"),
      getJson("/api/v1/events?limit=20"),
      getJson("/api/edges"),
      getJson("/api/v1/production-contexts"),
    ]).then(([cycles, summary, events, edges, contexts]) => {
      if (alive) setState({ loading: false, error: "", cycles: cycles.data || [], summary, events: events.data || [], edges: extractRows(edges), contexts: extractRows(contexts) });
    }).catch(error => {
      if (alive) setState(current => ({ ...current, loading: false, error: error.message }));
    });
    return () => { alive = false; };
  }, []);

  const activeCycles = state.cycles.filter(item => !item.endedAt).length;
  const onlineEdges = state.edges.filter(item => ["online", "healthy", "active"].includes(String(item.status).toLowerCase())).length;
  return (
    <Page title="工作台" description="从生产运行、质量待办和采集状态开始今天的工作。">
      {state.error && <Alert tone="danger">{state.error}</Alert>}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Metric label="最近运行" value={state.cycles.length} hint={`${activeCycles} 个正在进行`} />
        <Metric label="待处理质检" value={state.summary.pending ?? state.summary.pendingCount ?? 0} hint="来自当前质量任务" />
        <Metric label="采集节点" value={`${onlineEdges}/${state.edges.length}`} hint="在线 / 全部" />
        <Metric label="有效生产配置" value={state.contexts.filter(item => !item.validTo).length} hint="当前设备上下文" />
      </div>
      <div className="grid gap-5 xl:grid-cols-[1.3fr_.7fr]">
        <Card title="最近生产运行" actions={<Link className="text-sm font-medium text-blue-600 hover:text-blue-700" to="/cycles">查看全部</Link>}>
          <DataTable
            rows={state.cycles.slice(0, 8)}
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
                  <Badge tone={item.event?.eventType?.startsWith("alarm.") ? "danger" : "info"}>{item.event?.eventType || "event"}</Badge>
                  <span className="text-xs text-slate-400">#{item.ingestId}</span>
                </div>
                <p className="mt-2 truncate text-sm text-slate-700">{item.event?.subject?.id || item.event?.correlationId || "—"}</p>
              </div>
            ))}
            {!state.events.length && !state.loading && <EmptyState />}
          </div>
        </Card>
      </div>
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
            onRowClick={row => navigate(`/events?cycleId=${encodeURIComponent(row.correlationId)}`)}
            columns={[
              { key: "correlationId", label: "周期号" },
              { key: "machineId", label: "设备" },
              { key: "productCode", label: "产品" },
              { key: "recipeId", label: "配方" },
              { key: "qualityStatus", label: "质量", render: value => <StatusBadge value={value} /> },
              { key: "startedAt", label: "开始", render: formatTime },
              { key: "endedAt", label: "结束", render: formatTime },
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

export function ChatPage() {
  const [capabilities, setCapabilities] = useState(null);
  const [question, setQuestion] = useState("");
  const [mode, setMode] = useState("quick");
  const [run, setRun] = useState(null);
  const [events, setEvents] = useState([]);
  const [error, setError] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const controller = useRef(null);

  useEffect(() => {
    getJson("/api/v1/chat/capabilities").then(value => {
      setCapabilities(value);
      setMode(value.modes?.[0] || "quick");
    }).catch(requestError => setError(requestError.message));
    return () => controller.current?.abort();
  }, []);

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
    } catch (requestError) {
      if (requestError.name !== "AbortError") setError(requestError.message);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Page title="Ingot Chat" description="模型负责理解问题，确定性工具负责查询和计算。">
      {!capabilities?.enabled && <Alert tone="warning" title="AI 助手当前未启用">启用后才会向已登记模型发送问题；生产数据工具始终只读。</Alert>}
      {error && <Alert tone="danger">{error}</Alert>}
      <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_340px]">
        <Card title="调查对话">
          <div className="min-h-[420px] space-y-4">
            {!run && <EmptyState title="从一个工艺问题开始" description="例如：LOT-0716 一次通过率下降发生在哪个阶段？" />}
            {run && (
              <div className="space-y-4">
                <div className="ml-auto max-w-2xl rounded-2xl rounded-br-md bg-blue-600 px-4 py-3 text-sm text-white">{run.question || question}</div>
                {events.map((item, index) => (
                  <div key={`${item.type || "event"}-${index}`} className="max-w-3xl rounded-2xl rounded-bl-md border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-700">
                    <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">{item.type || "analysis"}</p>
                    <p className="mt-1 whitespace-pre-wrap">{item.message || item.text || item.answer || JSON.stringify(item)}</p>
                  </div>
                ))}
                {run.answer && <div className="max-w-3xl rounded-2xl rounded-bl-md bg-slate-900 px-5 py-4 text-sm leading-6 text-white whitespace-pre-wrap">{run.answer}</div>}
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
              <Button variant="primary" type="submit" disabled={!capabilities?.enabled || !question.trim() || submitting}>
                <PaperAirplaneIcon className="size-4" />{submitting ? "分析中" : "开始分析"}
              </Button>
            </div>
          </form>
        </Card>
        <Card title="回答边界">
          <ul className="space-y-4 text-sm leading-6 text-slate-600">
            <li className="flex gap-3"><CheckCircleIcon className="mt-0.5 size-5 shrink-0 text-emerald-600" />数字必须来自实际查询结果。</li>
            <li className="flex gap-3"><CheckCircleIcon className="mt-0.5 size-5 shrink-0 text-emerald-600" />工艺知识只有人工复核后才能用于回答。</li>
            <li className="flex gap-3"><ExclamationTriangleIcon className="mt-0.5 size-5 shrink-0 text-amber-600" />数据不足时明确写入限制，不推断缺失值。</li>
            <li className="flex gap-3"><BoltIcon className="mt-0.5 size-5 shrink-0 text-blue-600" />不写 PLC、CNC 或机器人。</li>
          </ul>
        </Card>
      </div>
    </Page>
  );
}

export function ObjectExplorerPage() {
  const location = useLocation();
  const contexts = useApi("/api/v1/production-contexts");
  const objects = useApi("/api/v1/data-objects?limit=500");
  const rows = extractRows(objects.data);
  const [query, setQuery] = useState("");
  const searchInput = useRef(null);
  const filtered = useMemo(() => rows.filter(row => JSON.stringify(row).toLowerCase().includes(query.toLowerCase())), [query, rows]);
  useEffect(() => {
    if (location.state?.focusSearch) searchInput.current?.focus();
  }, [location.state]);
  return (
    <Page title="对象目录" description="搜索设备、产品、工装、配方与生产对象。">
      {(objects.error || contexts.error) && <Alert tone="danger">{objects.error || contexts.error}</Alert>}
      <Card>
        <Field label="搜索对象"><Input ref={searchInput} value={query} onChange={event => setQuery(event.target.value)} placeholder="输入名称、编号或类型" /></Field>
      </Card>
      <Card title="数据对象" description={`显示 ${filtered.length} 个对象`}>
        <DataTable
          rows={filtered}
          keyField="objectId"
          columns={[
            { key: "objectType", label: "类型", render: value => <Badge tone="info">{value}</Badge> },
            { key: "objectId", label: "编号" },
            { key: "displayName", label: "名称" },
            { key: "status", label: "状态", render: value => <StatusBadge value={value} /> },
          ]}
        />
      </Card>
    </Page>
  );
}

const productionResources = {
  context: {
    title: "生产切换", endpoint: "/api/v1/production-contexts", key: "contextId",
    columns: [["contextId", "上下文"], ["machineId", "设备"], ["productCode", "产品"], ["recipeId", "配方"], ["validFrom", "生效时间"], ["validTo", "结束时间"]],
    template: { machineId: "", productSeries: "", productCode: "", recipeId: "", recipeVersion: 1, toolingInstallationId: "", source: "manual", materialLotRef: "" },
    createLabel: "启用生产配置",
    requiredFields: ["machineId", "productCode", "recipeId"],
    prepare: value => ({ ...value, validFrom: new Date().toISOString() }),
    lifecycle: { label: "结束", visible: value => !value.validTo, url: value => `/api/v1/production-contexts/${value.contextId}:close`, body: () => ({ at: new Date().toISOString() }) },
  },
  installation: {
    title: "工装装卸", endpoint: "/api/v1/tooling-installations", key: "installationId",
    columns: [["installationId", "记录"], ["machineId", "设备"], ["moldId", "工装"], ["installedAt", "装入"], ["removedAt", "卸下"]],
    template: { machineId: "", assemblyRevisionId: "", source: "manual" },
    createLabel: "装入工装",
    requiredFields: ["machineId", "assemblyRevisionId"],
    prepare: value => ({ ...value, installedAt: new Date().toISOString(), commandId: crypto.randomUUID() }),
    lifecycle: { label: "卸下", visible: value => !value.removedAt, url: value => `/api/v1/tooling-installations/${value.installationId}:remove`, body: () => ({ at: new Date().toISOString() }) },
  },
  componentType: {
    title: "组件类型", endpoint: "/api/v1/tooling-component-types", key: "componentTypeCode",
    columns: [["componentTypeCode", "代码"], ["name", "名称"], ["status", "状态"], ["attributes", "属性"]],
    template: { componentTypeCode: "", name: "", status: "active", attributes: {} },
    createLabel: "新建组件类型",
    requiredFields: ["componentTypeCode", "name"],
    statusOptions: [["active", "启用"], ["inactive", "停用"]],
    deleteUrl: value => `/api/v1/tooling-component-types/${encodeURIComponent(value.componentTypeCode)}`,
  },
  component: {
    title: "组件台账", endpoint: "/api/v1/tooling-components", key: "componentId",
    columns: [["componentId", "组件"], ["componentTypeCode", "类型"], ["serialNo", "序列号"], ["name", "名称"], ["status", "状态"]],
    template: { componentId: "", componentTypeCode: "", serialNo: "", name: "", status: "available", attributes: {} },
    createLabel: "登记组件",
    requiredFields: ["componentId", "componentTypeCode", "serialNo", "name"],
    statusOptions: [["available", "可用"], ["maintenance", "维护中"], ["retired", "已退役"]],
    deleteUrl: value => `/api/v1/tooling-components/${encodeURIComponent(value.componentId)}`,
  },
  type: {
    title: "工装类型", endpoint: "/api/v1/tooling-types", key: "toolingTypeCode",
    columns: [["toolingTypeCode", "代码"], ["version", "版本"], ["name", "名称"], ["status", "状态"], ["roles", "装配位置"]],
    template: { toolingTypeCode: "", version: 1, name: "", status: "active", roles: [] },
    createLabel: "新建工装类型",
    requiredFields: ["toolingTypeCode", "name"],
    statusOptions: [["active", "启用"], ["inactive", "停用"]],
    deleteUrl: value => `/api/v1/tooling-types/${encodeURIComponent(value.toolingTypeCode)}/${value.version}`,
  },
  assembly: {
    title: "工装组合", endpoint: "/api/v1/tooling-assemblies", key: "moldId",
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

function ProductionRecordForm({ resource, editor, onChange }) {
  return (
    <div className="grid gap-4 sm:grid-cols-2">
      {Object.entries(resource.template).map(([key, initial]) => {
        const required = resource.requiredFields?.includes(key);
        const label = productionFieldLabels[key] ?? key;
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
  const componentTypes = extractRows(data);
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
    <Page title={resource.title} description="维护物理工装、不可变组合版本及其在设备上的有效区间。" actions={<Button variant="primary" onClick={() => openEditor()}>{resource.createLabel}</Button>}>
      {(error || (!open && actionError)) && <Alert tone="danger">{error || actionError}</Alert>}
      {loading && !data ? <LoadingCard /> : (
        <Card title={`${resource.title}记录`} description={`共 ${rows.length} 条`}>
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
      )}
      <Drawer
        open={open}
        onClose={() => setOpen(false)}
        title={editorMode === "create" ? resource.createLabel : editorMode === "version" ? "新版本维护" : `编辑${resource.title}`}
        description="平台会校验设备、配方、组件、组合版本及其历史引用。"
        footer={<><Button onClick={() => setOpen(false)}>取消</Button><Button variant="primary" onClick={save} disabled={saving || !editorValid}>{saving ? "保存中" : "保存"}</Button></>}
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
  const [taskPage, setTaskPage] = useState(1);
  const [recordPage, setRecordPage] = useState(1);
  const tasks = useApi(`/api/v1/inspection-tasks?status=all&limit=${inspectionPageSize}&offset=${(taskPage - 1) * inspectionPageSize}`);
  const records = useApi(`/api/v1/inspection-records?limit=${inspectionPageSize}&offset=${(recordPage - 1) * inspectionPageSize}`);
  const definitions = useApi("/api/v1/inspection-definitions");
  const [entryOpen, setEntryOpen] = useState(false);
  const [reviewOpen, setReviewOpen] = useState(false);
  const [reviewTarget, setReviewTarget] = useState(null);
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState("");
  const [form, setForm] = useState({ workpieceId: "", operationRunId: "", definitionKey: "", outcome: "PASS", notes: "", measurements: {}, file: null });
  const [review, setReview] = useState({ decision: "CONFIRMED", notes: "" });
  const definitionRows = extractRows(definitions.data);
  const selectedDefinition = definitionRows.find(item => `${item.code}:${item.version}` === form.definitionKey);

  function openTask(task = null) {
    const firstDefinition = definitionRows.find(item => item.code === task?.missingDefinitionCodes?.[0]) || definitionRows[0];
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
      await Promise.all([records.reload(), tasks.reload()]);
    } catch (requestError) {
      setActionError(requestError.message);
    } finally {
      setBusy(false);
    }
  }

  function openReview(row) {
    setReviewTarget(row);
    setReview({ decision: "CONFIRMED", notes: "" });
    setActionError("");
    setReviewOpen(true);
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
      await tasks.reload();
    } catch (requestError) {
      setActionError(requestError.message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Page title="质量任务" description="在统一任务队列中完成检测、复核和原图追溯。" actions={<Button variant="primary" onClick={() => openTask()}>新建检测记录</Button>}>
      {(tasks.error || records.error || definitions.error || (!entryOpen && !reviewOpen && actionError)) && <Alert tone="danger">{tasks.error || records.error || definitions.error || actionError}</Alert>}
      <TabGroup>
        <TabList className="flex w-fit gap-1 rounded-xl bg-slate-200/70 p-1">
          <Tab className="rounded-lg px-4 py-2 text-sm font-medium text-slate-600 outline-none data-selected:bg-white data-selected:text-blue-700 data-selected:shadow-sm">待办任务</Tab>
          <Tab className="rounded-lg px-4 py-2 text-sm font-medium text-slate-600 outline-none data-selected:bg-white data-selected:text-blue-700 data-selected:shadow-sm">检测记录</Tab>
        </TabList>
        <TabPanels className="mt-4">
          <TabPanel>
            <Card title="检测任务" description={`共 ${tasks.data?.total ?? extractRows(tasks.data).length} 条`}>
              <DataTable
                rows={extractRows(tasks.data)}
                keyField="operationRunId"
                columns={[
                  { key: "operationRunId", label: "运行" },
                  { key: "workpieceId", label: "工件" },
                  { key: "planName", label: "质量方案" },
                  { key: "status", label: "状态", render: value => <StatusBadge value={value} /> },
                  { key: "updatedAt", label: "更新时间", render: formatTime },
                  { key: "_actions", label: "操作", render: (_value, row) => <Button variant="ghost" onClick={() => openTask(row)}>录入检测</Button> },
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
                  { key: "recordId", label: "记录" },
                  { key: "workpieceId", label: "工件" },
                  { key: "definitionCode", label: "检测定义" },
                  { key: "outcome", label: "结果", render: value => <StatusBadge value={value} /> },
                  { key: "measuredAt", label: "检测时间", render: formatTime },
                  { key: "attachmentCount", label: "附件" },
                  { key: "_actions", label: "操作", render: (_value, row) => <Button variant="ghost" onClick={() => openReview(row)}>质量复核</Button> },
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
        title="录入检测结果"
        description="检测值、判定规则和原始附件会作为同一条固定质量记录保存。"
        size="lg"
        footer={<><Button onClick={() => setEntryOpen(false)}>取消</Button><Button variant="primary" type="submit" form="inspection-entry" disabled={busy}>{busy ? "提交中" : "提交检测记录"}</Button></>}
      >
        {actionError && <Alert tone="danger">{actionError}</Alert>}
        <form id="inspection-entry" className="grid gap-5" onSubmit={submitRecord}>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="工件 ID"><Input required value={form.workpieceId} onChange={event => setForm({ ...form, workpieceId: event.target.value })} /></Field>
            <Field label="运行 ID"><Input required value={form.operationRunId} onChange={event => setForm({ ...form, operationRunId: event.target.value })} /></Field>
          </div>
          <Field label="检测定义">
            <Select required value={form.definitionKey} onChange={event => setForm({ ...form, definitionKey: event.target.value, measurements: {} })}>
              <option value="">选择定义</option>
              {definitionRows.map(item => <option key={`${item.code}:${item.version}`} value={`${item.code}:${item.version}`}>{item.name || item.code} · v{item.version}</option>)}
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
          <Field label="总体结果"><Select value={form.outcome} onChange={event => setForm({ ...form, outcome: event.target.value })}><option value="PASS">合格</option><option value="FAIL">不合格</option><option value="INCONCLUSIVE">待确认</option></Select></Field>
          <Field label="原始附件" hint="支持平台允许的图片或文件格式。"><Input type="file" onChange={event => setForm({ ...form, file: event.target.files?.[0] || null })} /></Field>
          <Field label="备注"><Textarea value={form.notes} onChange={event => setForm({ ...form, notes: event.target.value })} /></Field>
        </form>
      </Drawer>
      <Drawer
        open={reviewOpen}
        onClose={() => setReviewOpen(false)}
        title="质量复核"
        description={reviewTarget ? `检测记录 ${reviewTarget.recordId}` : ""}
        footer={<><Button onClick={() => setReviewOpen(false)}>取消</Button><Button variant="primary" type="submit" form="inspection-review" disabled={busy}>提交复核</Button></>}
      >
        {actionError && <Alert tone="danger">{actionError}</Alert>}
        <form id="inspection-review" className="grid gap-5" onSubmit={submitReview}>
          <Field label="复核决定"><Select value={review.decision} onChange={event => setReview({ ...review, decision: event.target.value })}><option value="CONFIRMED">确认</option><option value="REJECTED">驳回</option><option value="REINSPECTION_REQUIRED">要求重检</option></Select></Field>
          <Field label="复核说明"><Textarea value={review.notes} onChange={event => setReview({ ...review, notes: event.target.value })} /></Field>
        </form>
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
      setResult(await postJson("/api/v1/cycle-comparisons", { baselineCycleId: baseline.trim(), candidateCycleIds: [candidate.trim()] }));
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <Page title="历史对比" description="把同类运行按阶段对齐，检查参数与结果差异。">
      {error && <Alert tone="danger">{error}</Alert>}
      <Card title="选择周期">
        <form className="grid gap-3 md:grid-cols-[1fr_1fr_auto]" onSubmit={compare}>
          <Field label="基准周期"><Input value={baseline} onChange={event => setBaseline(event.target.value)} required /></Field>
          <Field label="对比周期"><Input value={candidate} onChange={event => setCandidate(event.target.value)} required /></Field>
          <Button variant="primary" type="submit" className="self-end" disabled={busy}>开始对比</Button>
        </form>
      </Card>
      {result ? (
        <Card title="对比结果">
          <pre className="max-h-[620px] overflow-auto rounded-xl bg-slate-950 p-4 text-xs leading-6 text-slate-100">{JSON.stringify(result, null, 2)}</pre>
        </Card>
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
      keyField="objectId"
      columns={[
        { key: "objectType", label: "对象类型" },
        { key: "objectId", label: "对象" },
        { key: "sampleCount", label: "样本数" },
        { key: "qualityStatus", label: "数据状态", render: value => <StatusBadge value={value} /> },
        { key: "lastSeenAt", label: "最后数据", render: formatTime },
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
      <Alert tone="info" title="受控改进流程">平台集中记录改进建议、审批结论、试验过程和实际效果。</Alert>
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
  const supportsDetail = ["机理模型", "融合执行", "知识"].includes(definition.label);

  function detailUrl(row) {
    if (definition.label === "机理模型") {
      return `/api/v1/mechanism-models/${encodeURIComponent(row.modelId)}/${row.version}`;
    }
    if (definition.label === "融合执行") {
      return `/api/v1/mechanism-fusions/${encodeURIComponent(row.fusionId)}/${row.version}`;
    }
    if (definition.label === "知识") {
      return `/api/v1/process-knowledge/${encodeURIComponent(row.sourceId)}`;
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
    } catch (requestError) {
      setDetailError(requestError.message);
    } finally {
      setDetailBusy(false);
    }
  }

  function changeDetailStatus(targetStatus) {
    let url;
    if (definition.label === "机理模型") {
      url = `/api/v1/mechanism-models/${encodeURIComponent(selected.modelId)}/${selected.version}/status`;
    } else if (definition.label === "融合执行") {
      url = `/api/v1/mechanism-fusions/${encodeURIComponent(selected.fusionId)}/${selected.version}/status`;
    } else {
      url = `/api/v1/process-knowledge/${encodeURIComponent(selected.sourceId)}/status`;
    }
    return runDetailAction(() => postJson(url, { targetStatus }));
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
        actions={<Button variant="primary" onClick={start}>{definition.upload ? "上传并验证" : "新建"}</Button>}
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
      />
      <Drawer
        open={open}
        onClose={() => setOpen(false)}
        title={definition.upload ? `上传${definition.label}` : `新建${definition.label}`}
        description={definition.upload === "knowledge" ? "上传后自动解析并进入人工复核队列。" : definition.upload === "validation" ? "来源、许可、哈希、字段覆盖和流批一致性会作为硬门禁。" : "保存后由平台执行业务规则和引用校验。"}
        size={definition.upload === "validation" ? "xl" : "lg"}
        footer={<><Button onClick={() => setOpen(false)}>取消</Button><Button variant="primary" onClick={save} disabled={saving || (definition.upload === "knowledge" && (!file || !title.trim())) || (definition.upload === "validation" && !file)}>{saving ? "处理中" : "提交"}</Button></>}
      >
        {actionError && <Alert tone="danger">{actionError}</Alert>}
        <div className="grid gap-5">
          {definition.label === "融合执行" && (
            <Alert tone="info">
              mode 可选：calibration、post-processing、mechanism-as-feature、ensemble。定义会固定机理模型与数据模型版本。
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
}) {
  const resource = detail?.model || detail?.fusion || detail?.source;
  const records = detail?.records || [];
  const status = resource?.status;
  const allReviewed = records.length > 0 && records.every(record => record.humanReviewed);
  const statusButtons = [];
  if (["机理模型", "融合执行"].includes(definition.label)) {
    if (status === "draft") statusButtons.push(["提交验证", "validated"]);
    if (status === "validated") {
      statusButtons.push(["启用", "active"]);
      statusButtons.push(["停用", "retired"]);
    }
    if (status === "active") statusButtons.push(["停用", "retired"]);
  }
  if (definition.label === "知识") {
    if (status === "indexed" && allReviewed) statusButtons.push(["确认复核完成", "reviewed"]);
    if (["uploaded", "indexed", "reviewed"].includes(status)) statusButtons.push(["停用来源", "retired"]);
  }

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
              disabled={busy}
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
            <Metric label="当前状态" value={status} />
            <Metric label="版本" value={resource.version ?? "—"} />
            <Metric
              label={definition.label === "知识" ? "已复核记录" : "内容指纹"}
              value={definition.label === "知识"
                ? `${records.filter(record => record.humanReviewed).length}/${records.length}`
                : (resource.contentHash?.slice(0, 12) || "—")}
            />
          </div>

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
            <Card title="机理定义" description="仅读取可审计仿射方程，不执行任意上传代码。">
              <pre className="overflow-x-auto rounded-xl bg-slate-950 p-4 text-xs leading-6 text-slate-100">{JSON.stringify(resource, null, 2)}</pre>
            </Card>
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
    title: "采集任务", description: "管理 HTTP、MQTT、OPC UA 与 Modbus TCP 采集版本。", endpoint: "/api/v1/acquisition-profiles", key: "profileId",
    columns: [["profileId", "任务"], ["version", "版本"], ["name", "名称"], ["protocol", "协议"], ["status", "状态"]],
    render: { protocol: value => acquisitionProtocolLabels[value] || value },
    createLabel: "创建采集任务",
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
    <Page title={definition.title} description={definition.description} actions={<Button variant="primary" onClick={openCreate}>{definition.createLabel || "创建新版本"}</Button>}>
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
  return (
    <ResourcePage
      title="采集节点"
      description="查看边缘节点、最后心跳与采集运行状态。"
      endpoint="/api/edges"
      keyField="edgeId"
      interval={10000}
      columns={[
        { key: "edgeId", label: "节点" },
        { key: "displayName", label: "名称" },
        { key: "status", label: "状态", render: value => <StatusBadge value={value} /> },
        { key: "lastSeenAt", label: "最后心跳", render: formatTime },
        { key: "version", label: "版本" },
      ]}
    />
  );
}

export function MetricsPage() {
  const { data: edges, error } = useApi("/api/edges", { interval: 10000 });
  const rows = extractRows(edges);
  const online = rows.filter(row => String(row.status).toLowerCase() === "online").length;
  const offline = rows.filter(row => String(row.status).toLowerCase() === "offline").length;
  const unknown = Math.max(0, rows.length - online - offline);
  return (
    <Page title="平台指标" description="查看平台和现场节点的运行状态。">
      {error && <Alert tone="danger">{error}</Alert>}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Metric label="边缘节点" value={rows.length} />
        <Metric label="在线节点" value={online} />
        <Metric label="离线节点" value={offline} />
        <Metric label="待上报状态" value={unknown} />
      </div>
      <Card title="节点状态">
        <DataTable rows={rows} keyField="edgeId" columns={[
          { key: "edgeId", label: "节点" },
          { key: "status", label: "状态", render: value => <StatusBadge value={value} /> },
          { key: "lastSeenAt", label: "最后心跳", render: formatTime },
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
  const endpoint = edgeId ? `/api/edges/${encodeURIComponent(edgeId)}/logs?limit=200${level ? `&level=${level}` : ""}` : null;
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
        {edgeId ? <DataTable rows={extractRows(logs.data)} keyField="id" columns={[
          { key: "timestamp", label: "时间", render: formatTime },
          { key: "level", label: "级别", render: value => <StatusBadge value={value} /> },
          { key: "category", label: "类别" },
          { key: "message", label: "消息" },
        ]} /> : <EmptyState title="请选择边缘节点" description="选择后日志会自动加载并持续更新。" />}
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
