// 管理 Ingot Chat 的运行创建、历史、流式状态和受控删除交互。
import { ArrowPathIcon, MagnifyingGlassIcon, TrashIcon } from "@heroicons/react/24/outline";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { deleteJson, getJson, postJson, streamSse } from "../api/http";
import { extractRows, useApi } from "../hooks/useApi";
import { useSseReconnect } from "../hooks/useSseStream";
import { Alert, Badge, Button, Card, DataTable, Drawer, Field, Input, Pagination, Page, Select, notify, useConfirmDialog } from "../ui/components";
import { eventTypeLabel, formatTime, LoadingCard } from "./shared";

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
  const newest = rows.reduce((maximum, item) => Math.max(maximum, Number(item.ingestId || 0)), 0);
  const streamUrl = useMemo(() => {
    const streamParams = new URLSearchParams();
    Object.entries(appliedFilters).forEach(([key, value]) => value.trim() && streamParams.set(key, value.trim()));
    return `/api/v1/events/stream?${streamParams}`;
  }, [appliedFilters]);

  useSseReconnect(streamUrl, {
    enabled: live,
    initialLastEventId: newest,
    onEvent: ({ data: item }) => {
      setStreamError("");
      setData(current => {
        const currentRows = extractRows(current);
        if (currentRows.some(value => value.ingestId === item.ingestId)) return current;
        return { ...(current || {}), data: [item, ...currentRows].slice(0, pageSize), total: Number(current?.total || currentRows.length) + 1 };
      });
    },
    onError: () => {
      setStreamError("实时事件连接暂时中断，正在使用最近确认位置重连。");
    },
  });
  return (
    <Page
      title="原始事件"
      actions={<label className="flex items-center gap-2 text-sm text-slate-600"><input type="checkbox" checked={live} onChange={event => { setPage(1); setLive(event.target.checked); }} />实时追踪</label>}
    >
      <Card title="事件筛选">
        <form className="grid gap-3 md:grid-cols-2 xl:grid-cols-[1fr_1fr_1fr_1fr_auto]" onSubmit={event => { event.preventDefault(); setLive(false); setAppliedFilters(filters); setPage(1); setQuery(makeEventQuery(filters, 1, pageSize)); }}>
          <Field label="事件类型"><Input value={filters.type} onChange={event => setFilters({ ...filters, type: event.target.value })} placeholder="process.execution.started" /></Field>
          <Field label="现场节点"><Input value={filters.edgeId} onChange={event => setFilters({ ...filters, edgeId: event.target.value })} /></Field>
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
              { key: "event", label: "类型", render: value => <Badge tone="info">{eventTypeLabel(value?.eventType)}</Badge> },
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
  quick: "证据核对",
  combined: "多视角研判",
};

const chatModeDescriptions = {
  quick: "查询平台记录并明确证据范围，不把观察性差异表述为根因。",
  combined: "由工艺、质量与复核视角交叉审查后形成结论。",
};

const perspectiveLabels = {
  process: "工艺视角",
  quality: "质量视角",
  review: "复核视角",
};

const reviewPositionLabels = {
  support: "支持",
  oppose: "反对",
  uncertain: "待确认",
};

const chatProgressLabels = {
  "run.started": "已接收调查事项",
  "plan.created": "已确定查询范围",
  "iteration.started": "正在准备相关记录",
  "tool.started": "正在读取生产数据",
  "tool.completed": "已读取记录",
  "relatedRecords.checked": "正在核对数据来源",
  "discussion.started": "正在执行工艺、质量与复核交叉审查",
  "discussion.message": "正在汇总交叉审查结果",
  "discussion.completed": "多视角研判完成",
  "answer.delta": "正在整理调查结论",
  "run.completed": "调查已完成",
  "run.failed": "调查失败",
  "run.cancelled": "调查已取消",
};

const chatHistoryStatusLabels = {
  queued: "等待核对",
  running: "正在核对",
  cancelling: "正在取消",
  failed: "调查失败",
  cancelled: "已取消",
  completed: "调查已完成",
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
    return (
      <article className="border border-slate-300 border-l-2 border-l-trajectory-600 bg-white px-4 py-4 text-sm leading-7 text-slate-700">
        <p className="data-label mb-2">调查说明</p>
        <p className="whitespace-pre-wrap">{answer}</p>
      </article>
    );
  }

  const findings = (answer.findings || [])
    .map(item => typeof item === "string"
      ? { statement: item, strength: "observation", evidenceReferences: [] }
      : item)
    .filter(item => item?.statement && item.statement !== answer.summary);
  const findingStrengthLabels = {
    observation: "记录事实",
    association: "统计关联",
    hypothesis: "待验证假设",
  };
  const combined = answer.combinedAnalysis;
  const isConversationReply = findings.length === 0 &&
    (answer.relatedRecords || []).length === 0 &&
    (answer.charts || []).length === 0 &&
    (answer.proposals || []).length === 0 &&
    (answer.limitations || []).length === 0 &&
    !combined;
  if (isConversationReply) {
    return (
      <article className="border border-slate-300 border-l-2 border-l-trajectory-600 bg-white px-4 py-4 text-sm leading-7 text-slate-700">
        <p className="data-label mb-2">调查说明</p>
        <p className="whitespace-pre-wrap">{answer.summary || "请继续说明你的问题。"}</p>
        {(answer.followUpQuestions || []).length > 0 && <div className="mt-4 flex flex-wrap gap-2 border-t border-slate-100 pt-3">{answer.followUpQuestions.map(item => (
          <button key={item} type="button" className="border border-slate-300 bg-white px-3 py-1.5 text-left text-[13px] font-medium text-slate-700 transition hover:border-trajectory-600 hover:bg-trajectory-50" onClick={() => onFollowUp(item)}>{item}</button>
        ))}</div>}
      </article>
    );
  }
  return (
    <article className="overflow-hidden border border-slate-300 bg-white">
      <header className="flex items-center justify-between gap-3 border-b border-slate-200 bg-slate-50 px-4 py-3 sm:px-5">
        <div>
          <p className="data-label">调查结论</p>
          <p className="mt-1 text-sm font-semibold text-slate-900">基于当前授权记录的核对结果</p>
        </div>
        <Badge tone="info">只读核对</Badge>
      </header>
      <div className="space-y-6 px-4 py-5 text-sm leading-7 text-slate-700 sm:px-5">
        <section className="border-l-2 border-trajectory-600 pl-4">
          <p className="data-label mb-2">摘要</p>
          <p className="whitespace-pre-wrap font-medium text-slate-950">{answer.summary || "未形成摘要。"}</p>
        </section>
      {findings.length > 0 && (
        <section>
          <p className="data-label">关键发现</p>
          <ol className="mt-3 divide-y divide-slate-200 border-y border-slate-200">
            {findings.map((item, index) => (
              <li key={`${item.statement}-${index}`} className="flex gap-3 px-0 py-3">
                <span className="data-value grid size-7 shrink-0 place-items-center border border-slate-300 bg-slate-50 text-[13px] font-semibold text-slate-700">{String(index + 1).padStart(2, "0")}</span>
                <span className="min-w-0 flex-1">
                  <span className="block text-slate-900">{item.statement}</span>
                  <span className="mt-1 block text-[13px] text-slate-500">
                    {findingStrengthLabels[item.strength] || "证据陈述"} · {item.evidenceReferences?.length || 0} 条记录引用
                  </span>
                </span>
              </li>
            ))}
          </ol>
        </section>
      )}
      {(answer.limitations || []).length > 0 && (
        <section className="border-l-2 border-l-amber-500 bg-amber-50/70 px-4 py-3 text-amber-950">
          <p className="data-label !text-amber-700">证据边界</p>
          <ul className="mt-1 list-disc space-y-1 pl-5">
            {answer.limitations.map(item => <li key={item}>{item}</li>)}
          </ul>
        </section>
      )}
      {combined && (
        <section className="border-y border-slate-200 bg-slate-50 px-4 py-4">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <p className="data-label">多视角研判</p>
              <p className="mt-1 font-semibold text-slate-950">工艺、质量与复核基于同一批记录交叉审查</p>
            </div>
            <Badge tone={combined.status === "needs-review" ? "warning" : "neutral"}>{combined.status === "needs-review" ? "待工程复核" : "证据不足"}</Badge>
          </div>
          <p className="mt-3 text-sm leading-6 text-slate-700">{combined.summary}</p>
          {(combined.possibleCauses || []).length > 0 && (
            <div className="mt-4 grid gap-2">
              <p className="data-label">可能原因</p>
              {combined.possibleCauses.map(cause => {
                const reviews = (combined.reviews || []).filter(review => review.causeId === cause.causeId);
                const latestReviews = Object.values(reviews.reduce((latest, review) => ({ ...latest, [review.authorRole]: review }), {}));
                return (
                  <article key={cause.causeId} className="border border-slate-200 bg-white p-3">
                    <div className="flex flex-wrap items-center gap-2"><Badge tone="info">{perspectiveLabels[cause.authorRole] || cause.authorRole}</Badge><strong className="text-sm text-slate-900">{cause.statement}</strong></div>
                    <p className="mt-1 text-sm leading-6 text-slate-600">{cause.reason}</p>
                    {latestReviews.length > 0 && <div className="mt-2 flex flex-wrap gap-2">{latestReviews.map(review => <span key={review.authorRole} className="border border-slate-200 bg-slate-50 px-2 py-1 text-[13px] text-slate-600">{perspectiveLabels[review.authorRole] || review.authorRole} · {reviewPositionLabels[review.position] || review.position}</span>)}</div>}
                  </article>
                );
              })}
            </div>
          )}
          {(combined.reviewSteps || []).length > 0 && (
            <details className="mt-3">
              <summary className="cursor-pointer text-[13px] font-medium text-slate-700">查看 {combined.reviewSteps.length} 条交叉审查记录</summary>
              <ol className="mt-2 space-y-2">{combined.reviewSteps.map((step, index) => <li key={`${step.role}-${step.round}-${index}`} className="border-l-2 border-slate-300 bg-white px-3 py-2 text-[13px] leading-5 text-slate-600"><strong className="text-slate-800">第 {step.round} 轮 · {perspectiveLabels[step.role] || step.role}</strong><p>{step.summary}</p></li>)}</ol>
            </details>
          )}
        </section>
      )}
      {(answer.relatedRecords || []).length > 0 && (
        <section>
          <p className="data-label">相关记录</p>
          <div className="mt-3 flex flex-wrap gap-2">
            {answer.relatedRecords.map(item => item.url ? (
              <Link key={`${item.kind}:${item.id}`} to={item.url} className="border border-slate-300 bg-white px-3 py-1.5 text-[13px] font-semibold text-trajectory-700 transition hover:border-trajectory-600 hover:bg-trajectory-50">
                {item.label}
              </Link>
            ) : (
              <span key={`${item.kind}:${item.id}`} className="border border-slate-200 bg-slate-50 px-3 py-1.5 text-[13px] font-medium text-slate-700">
                {item.label}
              </span>
            ))}
          </div>
        </section>
      )}
      {(answer.followUpQuestions || []).length > 0 && (
        <section>
          <p className="data-label">继续调查</p>
          <div className="mt-3 flex flex-wrap gap-2">
            {answer.followUpQuestions.map(item => (
              <button key={item} type="button" className="border border-slate-300 bg-white px-3 py-1.5 text-left text-[13px] font-medium text-slate-700 transition hover:border-trajectory-600 hover:bg-trajectory-50" onClick={() => onFollowUp(item)}>
                {item}
              </button>
            ))}
          </div>
        </section>
      )}
      </div>
    </article>
  );
}

function ChatHistoryList({ items, loading, submitting, deletingConversationId, selectedConversationId, onOpen, onDelete }) {
  if (loading) {
    return <div className="inline-flex items-center gap-2 px-2 py-5 text-sm text-slate-500"><ArrowPathIcon className="size-4 animate-spin" />正在读取调查记录</div>;
  }
  if (!items.length) {
    return <p className="px-3 py-6 text-sm leading-6 text-slate-500">从一项生产、质量或工艺核对开始。</p>;
  }
  return items.map(item => (
    <div key={item.conversationId} className={`group mb-1 grid grid-cols-[minmax(0,1fr)_2rem] items-center border-l-2 ${selectedConversationId === item.conversationId ? "border-trajectory-600 bg-white" : "border-transparent hover:border-slate-300 hover:bg-white/80"}`}>
      <button type="button" className="min-w-0 px-3 py-2.5 text-left disabled:opacity-60" onClick={() => onOpen(item.conversationId)} disabled={submitting} aria-current={selectedConversationId === item.conversationId ? "page" : undefined}>
        <p className="truncate text-sm font-medium text-slate-800">{item.title}</p>
        <p className="mt-1 text-xs text-slate-400">{chatHistoryStatusLabels[item.lastMessageStatus] || item.lastMessageStatus || "调查"} · {formatTime(item.lastMessageAt)}</p>
      </button>
      <button type="button" className="grid size-8 place-items-center text-slate-400 opacity-100 hover:bg-rose-50 hover:text-rose-700 focus:opacity-100 lg:opacity-0 lg:group-hover:opacity-100" aria-label={`删除调查：${item.title}`} onClick={() => onDelete(item)} disabled={Boolean(deletingConversationId) || submitting}>
        <TrashIcon className="size-4" />
      </button>
    </div>
  ));
}

function InvestigationStarter({ onSelect }) {
  const starters = [
    { id: "run", title: "核对一次生产运行", detail: "根据运行号、设备或产品核对实际设定、过程轨迹和质量结果。", prompt: "核对一次生产运行：" },
    { id: "quality", title: "排查质量偏差", detail: "查看质量结果、可比运行和当前证据边界。", prompt: "核对质量偏差：" },
    { id: "comparison", title: "比较同类运行", detail: "比较指定运行与可比基线之间的过程和结果差异。", prompt: "比较同类运行：" },
  ];
  return (
    <section className="overflow-hidden border border-slate-300 bg-white">
      <header className="flex flex-col gap-3 border-b border-slate-200 bg-slate-50 px-5 py-5 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <p className="data-label">Ingot Chat</p>
          <h2 className="mt-2 text-xl font-semibold tracking-[-0.025em] text-slate-950">从一项可回查的工程问题开始</h2>
          <p className="mt-2 max-w-2xl text-sm leading-6 text-slate-600">系统只读取当前授权范围内的生产、质量和工艺记录；每项结论都应回到来源记录和证据边界。</p>
        </div>
        <Badge tone="info">只读范围</Badge>
      </header>
      <div className="grid divide-y divide-slate-200 md:grid-cols-3 md:divide-x md:divide-y-0">
        {starters.map((starter, index) => (
          <button key={starter.id} type="button" className="group min-h-40 px-5 py-5 text-left transition hover:bg-trajectory-50/70 focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-trajectory-600" onClick={() => onSelect(starter.prompt)}>
            <span className="data-value text-sm text-trajectory-700">0{index + 1}</span>
            <p className="mt-4 text-sm font-semibold text-slate-900">{starter.title}</p>
            <p className="mt-2 text-[13px] leading-5 text-slate-500">{starter.detail}</p>
            <span className="mt-4 block text-[13px] font-semibold text-trajectory-700">填写调查事项 →</span>
          </button>
        ))}
      </div>
      <dl className="grid divide-y divide-slate-200 border-t border-slate-200 text-sm sm:grid-cols-2 sm:divide-x sm:divide-y-0">
        <div className="grid grid-cols-[6rem_minmax(0,1fr)] px-5 py-3"><dt className="font-medium text-slate-500">调查范围</dt><dd className="text-slate-700">运行、质量结果、设备状态和工艺证据</dd></div>
        <div className="grid grid-cols-[6rem_minmax(0,1fr)] px-5 py-3"><dt className="font-medium text-slate-500">输出形式</dt><dd className="text-slate-700">结论、记录引用、限制和下一步</dd></div>
      </dl>
    </section>
  );
}

export function ChatPage() {
  const { conversationId: routeConversationId = "" } = useParams();
  const navigate = useNavigate();
  const [capabilities, setCapabilities] = useState(null);
  const [capabilitiesLoading, setCapabilitiesLoading] = useState(true);
  const [question, setQuestion] = useState("");
  const [mode, setMode] = useState("quick");
  const [run, setRun] = useState(null);
  const [conversation, setConversation] = useState(null);
  const [messages, setMessages] = useState([]);
  const [events, setEvents] = useState([]);
  const [history, setHistory] = useState([]);
  const [historyLoading, setHistoryLoading] = useState(true);
  const [historyOpen, setHistoryOpen] = useState(false);
  const [error, setError] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const [deletingConversationId, setDeletingConversationId] = useState("");
  const [queueDelayRunId, setQueueDelayRunId] = useState("");
  const { confirm, confirmationDialog } = useConfirmDialog();
  const controller = useRef(null);
  const queueDelayTimer = useRef(null);
  const queueDelayVersion = useRef(0);

  const clearQueueDelayWarning = useCallback(() => {
    queueDelayVersion.current += 1;
    if (queueDelayTimer.current) {
      window.clearTimeout(queueDelayTimer.current);
      queueDelayTimer.current = null;
    }
    setQueueDelayRunId("");
  }, []);

  const warnWhenStillQueued = useCallback(runId => {
    const version = ++queueDelayVersion.current;
    queueDelayTimer.current = window.setTimeout(() => {
      void getJson(`/api/v1/chat/runs/${encodeURIComponent(runId)}`)
        .then(snapshot => {
          if (queueDelayVersion.current === version && snapshot.status === "queued") setQueueDelayRunId(runId);
        })
        .catch(() => {});
    }, 15000);
  }, []);

  const loadHistory = useCallback(async () => {
    try {
      const value = await getJson("/api/v1/chat/conversations?limit=50");
      setHistory(value.items || []);
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setHistoryLoading(false);
    }
  }, []);

  const routeFor = useCallback(value => value ? `/chat/${encodeURIComponent(value)}` : "/chat", []);

  const loadConversation = useCallback(async value => {
    const detail = await getJson(`/api/v1/chat/conversations/${encodeURIComponent(value)}`);
    setConversation(detail.conversation);
    setMessages(detail.messages || []);
    const latestAssistant = [...(detail.messages || [])].reverse().find(item => item.role === "assistant");
    setRun(latestAssistant?.runId ? { runId: latestAssistant.runId, status: latestAssistant.status } : null);
    setEvents([]);
    return detail;
  }, []);

  useEffect(() => {
    getJson("/api/v1/chat/capabilities")
      .then(value => {
        setCapabilities(value);
        setMode(value.modes?.[0] || "quick");
      })
      .catch(requestError => setError(requestError.message))
      .finally(() => setCapabilitiesLoading(false));
    void loadHistory();
    return () => {
      controller.current?.abort();
      clearQueueDelayWarning();
    };
  }, [clearQueueDelayWarning, loadHistory]);

  useEffect(() => {
    if (!routeConversationId) {
      setConversation(null);
      setMessages([]);
      setRun(null);
      setEvents([]);
      clearQueueDelayWarning();
      return;
    }
    setError("");
    loadConversation(routeConversationId).catch(requestError => {
      setError(requestError.message);
      if (requestError.status === 404) navigate(routeFor(""), { replace: true });
    });
  }, [loadConversation, navigate, routeConversationId, routeFor]);

  function handleQuestionKeyDown(event) {
    if (event.key !== "Enter" || event.shiftKey || event.nativeEvent.isComposing || event.nativeEvent.keyCode === 229) return;
    event.preventDefault();
    event.currentTarget.form?.requestSubmit();
  }

  async function start(event) {
    event.preventDefault();
    if (!question.trim()) return;
    const submittedQuestion = question.trim();
    const availableModes = capabilities?.modes || [];
    const submittedMode = availableModes.includes(mode) ? mode : availableModes[0] || "quick";
    setSubmitting(true);
    setError("");
    setEvents([]);
    clearQueueDelayWarning();
    try {
      const payload = {
        text: submittedQuestion,
        clientMessageId: crypto.randomUUID(),
        mode: submittedMode,
      };
      const created = routeConversationId
        ? await postJson(`/api/v1/chat/conversations/${encodeURIComponent(routeConversationId)}/messages`, payload)
        : await postJson("/api/v1/chat/conversations", payload);
      setRun(created);
      if (!routeConversationId) navigate(routeFor(created.conversationId), { replace: true });
      setMessages(current => [
        ...current,
        { messageId: created.userMessageId, role: "user", status: "completed", text: submittedQuestion },
        { messageId: created.assistantMessageId, role: "assistant", status: "generating", runId: created.runId },
      ]);
      setQuestion("");
      warnWhenStillQueued(created.runId);
      controller.current = new AbortController();
      let terminalReceived = false;
      try {
        await streamSse(created.streamUrl, {
          signal: controller.current.signal,
          onEvent: ({ data }) => {
            setEvents(current => [...current, data]);
            clearQueueDelayWarning();
            if (["run.completed", "run.failed", "run.cancelled"].includes(data?.type)) {
              terminalReceived = true;
              controller.current?.abort();
            }
          },
        });
      } catch (streamError) {
        if (streamError.name !== "AbortError" || !terminalReceived) throw streamError;
      }
      await loadConversation(created.conversationId);
      await loadHistory();
    } catch (requestError) {
      if (requestError.name !== "AbortError") setError(requestError.message);
    } finally {
      clearQueueDelayWarning();
      setSubmitting(false);
    }
  }

  async function cancel() {
    if (!run?.runId || cancelling) return;
    setCancelling(true);
    setError("");
    clearQueueDelayWarning();
    try {
      await postJson(`/api/v1/chat/runs/${run.runId}:cancel`, {});
      controller.current?.abort();
      if (routeConversationId) await loadConversation(routeConversationId);
      await loadHistory();
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setSubmitting(false);
      setCancelling(false);
    }
  }

  async function openHistory(selectedConversationId) {
    if (submitting) return;
    setHistoryOpen(false);
    navigate(routeFor(selectedConversationId));
  }

  function newConversation() {
    if (submitting) return;
    setRun(null);
    setConversation(null);
    setMessages([]);
    setQuestion("");
    setEvents([]);
    clearQueueDelayWarning();
    setError("");
    setHistoryOpen(false);
    setMode(current => capabilities?.modes?.includes(current) ? current : capabilities?.modes?.[0] || "quick");
    navigate(routeFor(""));
  }

  async function deleteHistory(item) {
    if (!await confirm({
      title: "删除调查",
      description: `“${item.title}”的全部调查说明及分析过程将被永久删除。业务运行、质检和工艺配置记录不会受影响。`,
      confirmLabel: "确认删除",
      tone: "danger",
    })) return;
    setDeletingConversationId(item.conversationId);
    setError("");
    try {
      await deleteJson(`/api/v1/chat/conversations/${encodeURIComponent(item.conversationId)}`);
      if (routeConversationId === item.conversationId) newConversation();
      await loadHistory();
      notify("调查已删除。", "success");
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setDeletingConversationId("");
    }
  }

  const visibleProgress = events
    .map(item => ({ ...item, message: chatProgressText(item) }))
    .filter(item => item.message)
    .slice(-4);
  const serviceEnabled = Boolean(capabilities?.enabled);
  const deterministicModel = Boolean(capabilities?.isDeterministic);
  const analysisBlocked = capabilitiesLoading || !serviceEnabled || submitting;

  return (
    <div className="grid h-full min-h-0 bg-slate-100/60 lg:grid-cols-[16rem_minmax(0,1fr)]">
      <aside className="hidden min-h-0 border-r border-slate-300 bg-slate-50 lg:flex lg:flex-col">
        <div className="border-b border-slate-300 p-3">
          <Button className="w-full justify-center" variant="primary" onClick={newConversation} disabled={submitting}>
            <MagnifyingGlassIcon className="size-4" />新建调查
          </Button>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto p-2">
          <p className="data-label px-2 py-3">调查记录</p>
          <ChatHistoryList items={history} loading={historyLoading} submitting={submitting} deletingConversationId={deletingConversationId} selectedConversationId={routeConversationId} onOpen={openHistory} onDelete={deleteHistory} />
        </div>
      </aside>

      <section className="flex min-h-0 min-w-0 flex-col bg-white">
        <header className="flex flex-wrap items-center justify-between gap-4 border-b border-slate-300 bg-white px-4 py-4 sm:px-6">
          <div className="min-w-0">
            <p className="data-label">Ingot Chat</p>
            <h1 className="mt-1 truncate text-xl font-semibold tracking-[-0.025em] text-slate-950">{conversation?.title || "新建调查"}</h1>
            <p className="mt-1 truncate text-[13px] text-slate-500">授权范围内的运行、检验和工艺记录</p>
          </div>
          <div className="flex min-w-0 flex-1 items-center justify-end gap-2 sm:flex-none">
            {(capabilities?.modes || []).length > 1 && <Select aria-label="分析方法" className="w-36" value={mode} onChange={event => setMode(event.target.value)} disabled={!serviceEnabled || submitting} title={chatModeDescriptions[mode]}>
              {capabilities.modes.map(item => <option key={item} value={item}>{chatModeLabels[item] ?? item}</option>)}
            </Select>}
            <Button className="lg:hidden" onClick={() => setHistoryOpen(true)}><MagnifyingGlassIcon className="size-4" />调查记录</Button>
            <Button className="lg:hidden" onClick={newConversation} disabled={submitting}>新建调查</Button>
          </div>
        </header>

        <div className="min-h-0 flex-1 overflow-y-auto bg-slate-100/45">
          <div className="mx-auto flex min-h-full w-full max-w-6xl flex-col gap-5 px-4 py-5 sm:px-6">
            <section className="grid overflow-hidden border border-slate-300 bg-white text-[13px] sm:grid-cols-3" aria-label="调查边界">
              <div className="border-b border-slate-200 px-4 py-3 sm:border-b-0 sm:border-r"><p className="data-label">记录范围</p><p className="mt-1 font-medium text-slate-700">已授权的生产与质量记录</p></div>
              <div className="border-b border-slate-200 px-4 py-3 sm:border-b-0 sm:border-r"><p className="data-label">核对方式</p><p className="mt-1 font-medium text-slate-700">{chatModeLabels[mode] || "证据核对"}</p></div>
              <div className="px-4 py-3"><p className="data-label">输出边界</p><p className="mt-1 font-medium text-slate-700">结论需工程验证</p></div>
            </section>
            <div className="space-y-3">
              {!capabilitiesLoading && capabilities && !serviceEnabled && <Alert tone="warning" title="Ingot Chat 未配置模型服务">当前 Ingot Chat 尚未配置可用的模型服务，请由平台管理员在“系统管理 → 模型服务”中配置。此状态不影响现场采集、运行记录、质量检验或数值优化服务。</Alert>}
              {!capabilitiesLoading && serviceEnabled && deterministicModel && <Alert tone="info" title="当前核对范围">当前仅核对平台记录和证据边界，不提供多视角研判。</Alert>}
              {error && <Alert tone="danger">{error}</Alert>}
            </div>
            {!messages.length ? (
              <InvestigationStarter onSelect={setQuestion} />
            ) : (
              <div className="w-full space-y-5 self-start">
                {messages.map(message => message.role === "user" ? (
                  <article key={message.messageId} className="border border-slate-300 border-l-2 border-l-slate-600 bg-white px-4 py-4">
                    <p className="data-label mb-2">调查事项</p>
                    <p className="whitespace-pre-wrap text-sm leading-6 text-slate-900">{message.text}</p>
                  </article>
                ) : (
                  <section key={message.messageId} className="space-y-4">
                    {message.runId === run?.runId && !message.answer && visibleProgress.length > 0 && <section className="border border-slate-300 bg-white px-4 py-4" aria-label="调查进度">
                      <div className="flex items-center justify-between gap-3 border-b border-slate-200 pb-3"><div><p className="data-label">核对进度</p><p className="mt-1 text-sm font-semibold text-slate-900">正在建立可回查的调查记录</p></div><Badge tone="info">进行中</Badge></div>
                      <ol className="mt-3 divide-y divide-slate-100">{visibleProgress.map((item, index) => <li key={`${item.sequence || item.type || "event"}-${index}`} className="flex items-start gap-3 py-2.5 text-sm text-slate-600"><span className="data-value grid size-6 shrink-0 place-items-center border border-trajectory-500 text-xs font-semibold text-trajectory-700">{String(index + 1).padStart(2, "0")}</span><p className="whitespace-pre-wrap leading-6">{item.message}</p></li>)}</ol>
                    </section>}
                    {message.runId === run?.runId && submitting && !message.answer && visibleProgress.length === 0 && (queueDelayRunId === message.runId
                      ? <Alert tone="warning" title="调查任务仍在排队">后台 Worker 尚未领取此任务。请检查 Platform Worker 是否运行；任务会在 Worker 恢复后继续处理。</Alert>
                      : <section className="border-l-2 border-l-trajectory-600 bg-white px-4 py-3 text-sm text-slate-600"><p className="data-label mb-1">核对进度</p>已接收调查事项，正在建立查询范围并核对记录。</section>)}
                    <ChatAnswer answer={message.answer || message.text} onFollowUp={setQuestion} />
                    {message.status === "failed" && <Alert tone="danger" title="调查失败">{message.error || "未能形成调查结论，请重试。"}</Alert>}
                    {message.status === "cancelled" && <Alert title="调查已取消">{message.error || "本次调查已取消。"}</Alert>}
                    {message.runId && <p className="border-t border-slate-200 pt-2 text-right text-xs text-slate-400" title={message.runId}>调查编号 {message.runId}</p>}
                  </section>
                ))}
              </div>
            )}
          </div>
        </div>

        <footer className="border-t border-slate-300 bg-white px-3 py-3 sm:px-6">
          <form className="mx-auto flex max-w-6xl items-end gap-3 border border-slate-300 bg-white p-2 transition focus-within:border-trajectory-600 focus-within:ring-2 focus-within:ring-trajectory-500/15" onSubmit={start}>
            <div className="min-w-0 flex-1 px-2">
              <label className="data-label" htmlFor="investigation-question">调查事项</label>
              <textarea id="investigation-question" aria-label="提交调查说明" className="mt-1 max-h-40 min-h-10 min-w-0 w-full resize-none border-0 bg-transparent py-1 text-sm leading-6 text-slate-900 outline-none placeholder:text-slate-400" rows="1" required value={question} onChange={event => setQuestion(event.target.value)} disabled={!serviceEnabled || submitting} placeholder="描述需要核对的事实、运行号、设备或质量偏差…" onKeyDown={handleQuestionKeyDown} />
            </div>
            {submitting ? <Button type="button" onClick={cancel} disabled={cancelling}>{cancelling ? "取消中" : "停止调查"}</Button> : <Button className="size-10 rounded-md px-0" variant="primary" type="submit" aria-label="开始调查" disabled={analysisBlocked || !question.trim()}><MagnifyingGlassIcon className="size-5" /></Button>}
          </form>
          <p className="mx-auto mt-2 max-w-6xl text-[13px] text-slate-500">调查只基于平台记录，并标注范围、来源和证据边界；不会自动修改配方或控制设备。</p>
        </footer>
      </section>
      <Drawer
        open={historyOpen}
        onClose={() => setHistoryOpen(false)}
        title="调查记录"
        description={`当前账号共 ${history.length} 条`}
        size="md"
      >
        <ChatHistoryList items={history} loading={historyLoading} submitting={submitting} deletingConversationId={deletingConversationId} selectedConversationId={routeConversationId} onOpen={openHistory} onDelete={deleteHistory} />
      </Drawer>
      {confirmationDialog}
    </div>
  );
}
