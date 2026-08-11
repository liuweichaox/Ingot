import { ArrowPathIcon, MagnifyingGlassIcon, PaperAirplaneIcon } from "@heroicons/react/24/outline";
import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { getJson, postJson, streamSse } from "../api/http";
import { extractRows, useApi } from "../hooks/useApi";
import { Alert, Badge, Button, Card, DataTable, EmptyState, Field, Input, Pagination, Page, Select, Textarea } from "../ui/components";
import { formatTime, LoadingCard } from "./shared";

export function EventsPage() {
  const [urlParams] = useSearchParams();
  const [filters, setFilters] = useState({
    type: "",
    edgeId: "",
    subjectId: urlParams.get("subjectId") || "",
    executionId: urlParams.get("executionId") || "",
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
      title="运行事件"
      description="检索标准事件并回到所属生产运行。"
      actions={<label className="flex items-center gap-2 text-sm text-slate-600"><input type="checkbox" checked={live} onChange={event => { setPage(1); setLive(event.target.checked); }} />实时追踪</label>}
    >
      <Card title="事件筛选">
        <form className="grid gap-3 md:grid-cols-2 xl:grid-cols-[1fr_1fr_1fr_1fr_auto]" onSubmit={event => { event.preventDefault(); setLive(false); setAppliedFilters(filters); setPage(1); setQuery(makeEventQuery(filters, 1, pageSize)); }}>
          <Field label="事件类型"><Input value={filters.type} onChange={event => setFilters({ ...filters, type: event.target.value })} placeholder="process.sample" /></Field>
          <Field label="采集节点"><Input value={filters.edgeId} onChange={event => setFilters({ ...filters, edgeId: event.target.value })} /></Field>
          <Field label="工业对象"><Input value={filters.subjectId} onChange={event => setFilters({ ...filters, subjectId: event.target.value })} placeholder="设备或对象编号" /></Field>
          <Field label="运行号"><Input value={filters.executionId} onChange={event => setFilters({ ...filters, executionId: event.target.value })} /></Field>
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
              { key: "event", label: "运行号", render: value => value?.executionId || "—" },
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
    <Page title="分析助手" description="围绕当前优化项目查询证据、分析数据并说明结论边界。">
      {capabilitiesLoading && <Alert title="正在连接 AI 助手">正在读取可用的分析能力。</Alert>}
      {!capabilitiesLoading && capabilities && !capabilities.enabled && <Alert tone="warning" title="AI 助手当前未启用">请联系管理员启用分析服务。</Alert>}
      {projectId && <Alert title="已绑定研发项目">本次问答只使用该项目可访问的研发记录和知识来源。</Alert>}
      {error && <Alert tone="danger">{error}</Alert>}
      <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_360px]">
        <Card title="分析问答">
          <div className="min-h-[420px] space-y-4">
            {!run && <EmptyState title="从一个生产问题开始" description="例如：当前有哪些运行对象？最近哪些运行数据不完整？" />}
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
              <Textarea required value={question} onChange={event => setQuestion(event.target.value)} placeholder="描述要调查的现象、批次或运行…" />
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
