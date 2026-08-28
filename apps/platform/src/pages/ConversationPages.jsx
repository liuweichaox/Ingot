// 管理 Chat 运行创建、历史、流式状态和受控删除交互。
import { ArrowLeftIcon, ArrowPathIcon, ChatBubbleLeftRightIcon, MagnifyingGlassIcon, PaperAirplaneIcon, TrashIcon } from "@heroicons/react/24/outline";
import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { deleteJson, getJson, postJson, streamSse } from "../api/http";
import { extractRows, useApi } from "../hooks/useApi";
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
  useEffect(() => {
    if (!live) return undefined;
    const newest = rows.reduce((maximum, item) => Math.max(maximum, Number(item.ingestId || 0)), 0);
    const streamParams = new URLSearchParams();
    Object.entries(appliedFilters).forEach(([key, value]) => value.trim() && streamParams.set(key, value.trim()));
    if (newest) streamParams.set("afterIngestId", String(newest));
    const cancellation = new AbortController();
    let cursor = newest;
    void (async () => {
      while (!cancellation.signal.aborted) {
        try {
          cursor = await streamSse(`/api/v1/events/stream?${streamParams}`, {
            signal: cancellation.signal,
            lastEventId: cursor,
            onEvent: async ({ data: item }) => {
              setStreamError("");
              setData(current => {
                const currentRows = extractRows(current);
                if (currentRows.some(value => value.ingestId === item.ingestId)) return current;
                return { ...(current || {}), data: [item, ...currentRows].slice(0, pageSize), total: Number(current?.total || currentRows.length) + 1 };
              });
            },
          });
        } catch (error) {
          if (cancellation.signal.aborted || error?.name === "AbortError") return;
          setStreamError("实时事件连接暂时中断，正在使用最近确认位置重连。");
        }
        await new Promise(resolve => window.setTimeout(resolve, 1000));
      }
    })();
    return () => cancellation.abort();
  }, [appliedFilters, live, pageSize, setData]);
  return (
    <Page
      title="运行事件"
      actions={<label className="flex items-center gap-2 text-sm text-slate-600"><input type="checkbox" checked={live} onChange={event => { setPage(1); setLive(event.target.checked); }} />实时追踪</label>}
    >
      <Card title="事件筛选">
        <form className="grid gap-3 md:grid-cols-2 xl:grid-cols-[1fr_1fr_1fr_1fr_auto]" onSubmit={event => { event.preventDefault(); setLive(false); setAppliedFilters(filters); setPage(1); setQuery(makeEventQuery(filters, 1, pageSize)); }}>
          <Field label="事件类型"><Input value={filters.type} onChange={event => setFilters({ ...filters, type: event.target.value })} placeholder="process.execution.started" /></Field>
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
  "run.started": "正在理解问题",
  "plan.created": "已确定查询范围",
  "iteration.started": "正在准备数据查询",
  "tool.started": "正在查询生产数据",
  "tool.completed": "数据查询完成",
  "relatedRecords.checked": "正在核对数据来源",
  "discussion.started": "正在从工艺、质量和复核视角交叉审查",
  "discussion.message": "正在汇总各视角判断",
  "discussion.completed": "多视角研判完成",
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

const researchStatusLabels = {
  draft: "草稿",
  active: "研发中",
  validating: "验证中",
  completed: "已完成",
  archived: "已归档",
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
      <div className="max-w-[90%] rounded-lg border border-slate-200 bg-white px-4 py-3 text-sm leading-7 text-slate-700">
        <p className="whitespace-pre-wrap">{answer}</p>
      </div>
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
      <div className="max-w-[90%] rounded-lg border border-slate-200 bg-white px-4 py-3 text-sm leading-7 text-slate-700">
        <p className="whitespace-pre-wrap">{answer.summary || "请继续说明你的问题。"}</p>
        {(answer.followUpQuestions || []).length > 0 && <div className="mt-3 flex flex-wrap gap-2">{answer.followUpQuestions.map(item => (
          <button key={item} type="button" className="rounded-lg border border-slate-200 px-3 py-1.5 text-left text-[13px] text-slate-700 hover:border-blue-300 hover:bg-blue-50" onClick={() => onFollowUp(item)}>{item}</button>
        ))}</div>}
      </div>
    );
  }
  return (
    <article className="overflow-hidden rounded-lg border border-slate-200 bg-white">
      <header className="flex items-center justify-between gap-3 border-b border-slate-100 bg-slate-50/80 px-4 py-3">
        <div>
          <p className="text-sm font-semibold text-slate-900">分析结论</p>
          <p className="mt-0.5 text-[13px] text-slate-500">基于当前可访问记录生成，结论仍需工程验证</p>
        </div>
        <Badge tone="blue">只读分析</Badge>
      </header>
      <div className="space-y-5 px-4 py-4 text-sm leading-7 text-slate-700 sm:px-5">
        <p className="whitespace-pre-wrap font-medium text-slate-900">{answer.summary || "未形成摘要。"}</p>
      {findings.length > 0 && (
        <div>
          <p className="text-[13px] font-semibold text-slate-500">关键发现</p>
          <ol className="mt-2 space-y-2">
            {findings.map((item, index) => (
              <li key={`${item.statement}-${index}`} className="flex gap-3 rounded-lg bg-slate-50 px-3 py-2.5">
                <span className="grid size-6 shrink-0 place-items-center rounded-full bg-blue-100 text-[13px] font-semibold text-blue-700">{index + 1}</span>
                <span className="min-w-0 flex-1">
                  <span className="block">{item.statement}</span>
                  <span className="mt-1 block text-[13px] text-slate-500">
                    {findingStrengthLabels[item.strength] || "证据陈述"} · {item.evidenceReferences?.length || 0} 条记录引用
                  </span>
                </span>
              </li>
            ))}
          </ol>
        </div>
      )}
      {(answer.limitations || []).length > 0 && (
        <div className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-amber-900">
          <p className="font-semibold">证据边界</p>
          <ul className="mt-1 list-disc space-y-1 pl-5">
            {answer.limitations.map(item => <li key={item}>{item}</li>)}
          </ul>
        </div>
      )}
      {combined && (
        <section className="rounded-lg border border-slate-200 bg-slate-50 p-4">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <p className="font-semibold text-slate-950">多视角研判</p>
              <p className="mt-1 text-[13px] text-slate-500">工艺、质量和复核视角基于同一批记录交叉审查</p>
            </div>
            <Badge tone={combined.status === "needs-review" ? "warning" : "neutral"}>{combined.status === "needs-review" ? "待工程复核" : "证据不足"}</Badge>
          </div>
          <p className="mt-3 text-sm leading-6 text-slate-700">{combined.summary}</p>
          {(combined.possibleCauses || []).length > 0 && (
            <div className="mt-4 grid gap-2">
              <p className="text-[13px] font-semibold text-slate-500">可能原因</p>
              {combined.possibleCauses.map(cause => {
                const reviews = (combined.reviews || []).filter(review => review.causeId === cause.causeId);
                const latestReviews = Object.values(reviews.reduce((latest, review) => ({ ...latest, [review.authorRole]: review }), {}));
                return (
                  <article key={cause.causeId} className="rounded-lg border border-slate-200 bg-white p-3">
                    <div className="flex flex-wrap items-center gap-2"><Badge tone="blue">{perspectiveLabels[cause.authorRole] || cause.authorRole}</Badge><strong className="text-sm text-slate-900">{cause.statement}</strong></div>
                    <p className="mt-1 text-sm leading-6 text-slate-600">{cause.reason}</p>
                    {latestReviews.length > 0 && <div className="mt-2 flex flex-wrap gap-2">{latestReviews.map(review => <span key={review.authorRole} className="rounded-md bg-slate-100 px-2 py-1 text-[13px] text-slate-600">{perspectiveLabels[review.authorRole] || review.authorRole} · {reviewPositionLabels[review.position] || review.position}</span>)}</div>}
                  </article>
                );
              })}
            </div>
          )}
          {(combined.reviewSteps || []).length > 0 && (
            <details className="mt-3">
              <summary className="cursor-pointer text-[13px] font-medium text-slate-700">查看 {combined.reviewSteps.length} 条交叉审查记录</summary>
              <ol className="mt-2 space-y-2">{combined.reviewSteps.map((step, index) => <li key={`${step.role}-${step.round}-${index}`} className="rounded-lg bg-white px-3 py-2 text-[13px] leading-5 text-slate-600"><strong className="text-slate-800">第 {step.round} 轮 · {perspectiveLabels[step.role] || step.role}</strong><p>{step.summary}</p></li>)}</ol>
            </details>
          )}
        </section>
      )}
      {(answer.relatedRecords || []).length > 0 && (
        <div>
          <p className="text-[13px] font-semibold text-slate-500">相关记录</p>
          <div className="mt-2 flex flex-wrap gap-2">
            {answer.relatedRecords.map(item => item.url ? (
              <Link key={`${item.kind}:${item.id}`} to={item.url} className="rounded-lg border border-blue-200 bg-blue-50 px-3 py-1.5 text-[13px] font-medium text-blue-700 hover:bg-blue-100">
                {item.label}
              </Link>
            ) : null)}
          </div>
        </div>
      )}
      {(answer.followUpQuestions || []).length > 0 && (
        <div>
          <p className="text-[13px] font-semibold text-slate-500">继续调查</p>
          <div className="mt-2 flex flex-wrap gap-2">
            {answer.followUpQuestions.map(item => (
              <button key={item} type="button" className="rounded-lg border border-slate-200 px-3 py-1.5 text-left text-[13px] text-slate-700 hover:border-blue-300 hover:bg-blue-50" onClick={() => onFollowUp(item)}>
                {item}
              </button>
            ))}
          </div>
        </div>
      )}
      </div>
    </article>
  );
}

function ChatHistoryList({ items, loading, submitting, deletingConversationId, selectedConversationId, onOpen, onDelete }) {
  if (loading) {
    return <div className="inline-flex items-center gap-2 px-2 py-5 text-sm text-slate-500"><ArrowPathIcon className="size-4 animate-spin" />正在读取</div>;
  }
  if (!items.length) {
    return <p className="px-3 py-6 text-sm leading-6 text-slate-500">从一个生产、质量或工艺问题开始。</p>;
  }
  return items.map(item => (
    <div key={item.conversationId} className={`group mb-1 grid grid-cols-[minmax(0,1fr)_2rem] items-center rounded-md ${selectedConversationId === item.conversationId ? "bg-blue-50" : "hover:bg-white"}`}>
      <button type="button" className="min-w-0 px-3 py-2.5 text-left disabled:opacity-60" onClick={() => onOpen(item.conversationId)} disabled={submitting} aria-current={selectedConversationId === item.conversationId ? "page" : undefined}>
        <p className="truncate text-sm font-medium text-slate-800">{item.title}</p>
        <p className="mt-1 text-xs text-slate-400">{chatHistoryStatusLabels[item.lastMessageStatus] || item.lastMessageStatus || "对话"} · {formatTime(item.lastMessageAt)}</p>
      </button>
      <button type="button" className="grid size-8 place-items-center rounded-md text-slate-400 opacity-100 hover:bg-rose-50 hover:text-rose-700 focus:opacity-100 lg:opacity-0 lg:group-hover:opacity-100" aria-label={`删除对话：${item.title}`} onClick={() => onDelete(item)} disabled={Boolean(deletingConversationId) || submitting}>
        <TrashIcon className="size-4" />
      </button>
    </div>
  ));
}

export function ChatPage() {
  const { conversationId: routeConversationId = "" } = useParams();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const projectId = searchParams.get("projectId");
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
  const [project, setProject] = useState(null);
  const [projectLoading, setProjectLoading] = useState(Boolean(projectId));
  const [projectError, setProjectError] = useState("");
  const [error, setError] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const [deletingConversationId, setDeletingConversationId] = useState("");
  const { confirm, confirmationDialog } = useConfirmDialog();
  const controller = useRef(null);

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

  const routeFor = useCallback(value => {
    const suffix = projectId ? `?projectId=${encodeURIComponent(projectId)}` : "";
    return value ? `/chat/${encodeURIComponent(value)}${suffix}` : `/chat${suffix}`;
  }, [projectId]);

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
    return () => controller.current?.abort();
  }, [loadHistory]);

  useEffect(() => {
    if (!routeConversationId) {
      setConversation(null);
      setMessages([]);
      setRun(null);
      setEvents([]);
      return;
    }
    setError("");
    loadConversation(routeConversationId).catch(requestError => {
      setError(requestError.message);
      if (requestError.status === 404) navigate(routeFor(""), { replace: true });
    });
  }, [loadConversation, navigate, routeConversationId, routeFor]);

  useEffect(() => {
    if (!projectId) {
      setProject(null);
      setProjectLoading(false);
      setProjectError("");
      return;
    }
    setProjectLoading(true);
    setProjectError("");
    getJson(`/api/v1/research-projects/${encodeURIComponent(projectId)}`)
      .then(value => setProject(value?.project || null))
      .catch(requestError => setProjectError(requestError.message))
      .finally(() => setProjectLoading(false));
  }, [projectId]);

  async function start(event) {
    event.preventDefault();
    if (!question.trim()) return;
    const submittedQuestion = question.trim();
    const availableModes = capabilities?.modes || [];
    const submittedMode = availableModes.includes(mode) ? mode : availableModes[0] || "quick";
    setSubmitting(true);
    setError("");
    setEvents([]);
    try {
      const payload = {
        text: submittedQuestion,
        clientMessageId: crypto.randomUUID(),
        mode: submittedMode,
      };
      const created = routeConversationId
        ? await postJson(`/api/v1/chat/conversations/${encodeURIComponent(routeConversationId)}/messages`, payload)
        : await postJson("/api/v1/chat/conversations", {
            ...payload,
            pageContext: projectId ? { kind: "research-project", id: projectId } : null,
          });
      setRun(created);
      if (!routeConversationId) navigate(routeFor(created.conversationId), { replace: true });
      setMessages(current => [
        ...current,
        { messageId: created.userMessageId, role: "user", status: "completed", text: submittedQuestion },
        { messageId: created.assistantMessageId, role: "assistant", status: "generating", runId: created.runId },
      ]);
      setQuestion("");
      controller.current = new AbortController();
      let terminalReceived = false;
      try {
        await streamSse(created.streamUrl, {
          signal: controller.current.signal,
          onEvent: ({ data }) => {
            setEvents(current => [...current, data]);
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
    setError("");
    setHistoryOpen(false);
    setMode(current => capabilities?.modes?.includes(current) ? current : capabilities?.modes?.[0] || "quick");
    navigate(routeFor(""));
  }

  async function deleteHistory(item) {
    if (!await confirm({
      title: "删除对话",
      description: `“${item.title}”的全部消息及分析过程将被永久删除。业务运行、质检和项目记录不会受影响。`,
      confirmLabel: "确认删除",
      tone: "danger",
    })) return;
    setDeletingConversationId(item.conversationId);
    setError("");
    try {
      await deleteJson(`/api/v1/chat/conversations/${encodeURIComponent(item.conversationId)}`);
      if (routeConversationId === item.conversationId) newConversation();
      await loadHistory();
      notify("对话已删除。", "success");
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
  const scopedHistory = projectId
    ? history.filter(item => item.pageContext?.kind === "research-project" && item.pageContext?.id === projectId)
    : history;
  const serviceEnabled = Boolean(capabilities?.enabled);
  const deterministicDemo = Boolean(capabilities?.isDeterministic);
  const analysisBlocked = capabilitiesLoading || !serviceEnabled || submitting;

  return (
    <div className="grid h-full min-h-0 bg-white lg:grid-cols-[18rem_minmax(0,1fr)]">
      <aside className="hidden min-h-0 border-r border-slate-200 bg-slate-50/80 lg:flex lg:flex-col">
        <div className="border-b border-slate-200 p-3">
          <Button className="w-full justify-center" variant="primary" onClick={newConversation} disabled={submitting}>
            <ChatBubbleLeftRightIcon className="size-4" />新对话
          </Button>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto p-2">
          <p className="px-2 py-2 text-[13px] font-semibold text-slate-500">{projectId ? "当前项目的对话" : "最近对话"}</p>
          <ChatHistoryList items={scopedHistory} loading={historyLoading} submitting={submitting} deletingConversationId={deletingConversationId} selectedConversationId={routeConversationId} onOpen={openHistory} onDelete={deleteHistory} />
        </div>
      </aside>

      <section className="flex min-h-0 min-w-0 flex-col bg-white">
        <header className="flex min-h-16 flex-wrap items-center justify-between gap-3 border-b border-slate-200 px-4 py-3 sm:px-6">
          <div className="min-w-0">
            <div className="flex items-center gap-2"><ChatBubbleLeftRightIcon className="size-5 text-blue-600" /><h1 className="truncate font-semibold text-slate-950">{conversation?.title || "工艺分析助手"}</h1></div>
            <p className="mt-0.5 truncate text-[13px] text-slate-500">{projectLoading ? "正在读取上下文…" : project ? `${project.name} · ${researchStatusLabels[project.status] || project.status}` : "生产与工艺数据"}</p>
          </div>
          <div className="flex min-w-0 flex-1 items-center justify-end gap-2 sm:flex-none">
            {(capabilities?.modes || []).length > 1 && <Select aria-label="分析方法" className="w-36" value={mode} onChange={event => setMode(event.target.value)} disabled={!serviceEnabled || submitting} title={chatModeDescriptions[mode]}>
              {capabilities.modes.map(item => <option key={item} value={item}>{chatModeLabels[item] ?? item}</option>)}
            </Select>}
            <Button className="lg:hidden" onClick={() => setHistoryOpen(true)}><ChatBubbleLeftRightIcon className="size-4" />对话记录</Button>
            <Button className="lg:hidden" onClick={newConversation} disabled={submitting}>新对话</Button>
            {projectId && <Link to={`/research-projects/${encodeURIComponent(projectId)}`} className="hidden min-h-9 items-center gap-2 rounded-lg px-3 py-2 text-sm font-medium text-slate-600 hover:bg-slate-100 sm:inline-flex"><ArrowLeftIcon className="size-4" />项目</Link>}
          </div>
        </header>

        <div className="min-h-0 flex-1 overflow-y-auto bg-slate-50/50">
          <div className="mx-auto flex min-h-full w-full max-w-4xl flex-col px-4 py-6 sm:px-6">
            <div className="space-y-3">
              {projectError && <Alert tone="danger" title="无法读取项目上下文">{projectError}</Alert>}
              {!capabilitiesLoading && capabilities && !serviceEnabled && <Alert tone="warning" title="分析服务未启用">当前部署未启用分析服务，请联系平台管理员检查模型服务配置。</Alert>}
              {!capabilitiesLoading && serviceEnabled && deterministicDemo && <Alert tone="info" title="当前分析范围">当前仅核对平台记录和证据边界，不提供多视角研判。</Alert>}
              {error && <Alert tone="danger">{error}</Alert>}
            </div>
            {!messages.length ? (
              <div className="w-full max-w-3xl self-start rounded-lg border border-slate-200 bg-white">
                <div className="border-b border-slate-200 px-4 py-3">
                  <h2 className="text-sm font-semibold text-slate-950">新建分析</h2>
                </div>
                <dl className="grid grid-cols-[6rem_minmax(0,1fr)] text-sm sm:grid-cols-[8rem_minmax(0,1fr)]">
                  <dt className="border-b border-r border-slate-100 px-4 py-3 font-medium text-slate-500">分析范围</dt>
                  <dd className="border-b border-slate-100 px-4 py-3 text-slate-700">生产运行、质量结果、设备状态和工艺证据</dd>
                  <dt className="border-r border-slate-100 px-4 py-3 font-medium text-slate-500">输入方式</dt>
                  <dd className="px-4 py-3 text-slate-700">在下方输入运行号、设备编号或需要核对的问题</dd>
                </dl>
              </div>
            ) : (
              <div className="w-full space-y-7 self-start">
                {messages.map(message => message.role === "user" ? (
                  <div key={message.messageId} className="flex justify-end"><div className="max-w-[85%] rounded-lg rounded-br-md bg-blue-600 px-4 py-3 text-sm leading-6 text-white"><p className="whitespace-pre-wrap">{message.text}</p></div></div>
                ) : (
                  <section key={message.messageId} className="space-y-4">
                    {message.runId === run?.runId && !message.answer && visibleProgress.length > 0 && <ol className="space-y-2" aria-label="分析进度">{visibleProgress.map((item, index) => <li key={`${item.sequence || item.type || "event"}-${index}`} className="flex items-start gap-3 text-sm text-slate-600"><ArrowPathIcon className="mt-0.5 size-4 shrink-0 animate-spin text-blue-600" /><p className="whitespace-pre-wrap">{item.message}</p></li>)}</ol>}
                    {message.runId === run?.runId && submitting && !message.answer && visibleProgress.length === 0 && <div className="inline-flex items-center gap-2 text-sm text-slate-500"><ArrowPathIcon className="size-4 animate-spin" />正在理解问题并核对记录</div>}
                    <ChatAnswer answer={message.answer || message.text} onFollowUp={setQuestion} />
                    {message.status === "failed" && <Alert tone="danger" title="分析失败">{message.error || "回答生成失败，请重试。"}</Alert>}
                    {message.status === "cancelled" && <Alert title="分析已取消">{message.error || "本次回答已取消。"}</Alert>}
                    {message.runId && <p className="text-center text-xs text-slate-400" title={message.runId}>调查记录 {message.runId}</p>}
                  </section>
                ))}
              </div>
            )}
          </div>
        </div>

        <footer className="border-t border-slate-200 bg-white px-3 py-3 sm:px-6">
          <form className="mx-auto flex max-w-4xl items-end gap-2 rounded-lg border border-slate-300 bg-white p-2 focus-within:border-blue-500 focus-within:ring-3 focus-within:ring-blue-500/15" onSubmit={start}>
            <textarea aria-label="给工艺分析助手发送消息" className="max-h-40 min-h-10 min-w-0 flex-1 resize-none border-0 bg-transparent px-2 py-2 text-sm leading-6 text-slate-900 outline-none placeholder:text-slate-400" rows="1" required value={question} onChange={event => setQuestion(event.target.value)} disabled={!serviceEnabled || submitting} placeholder="询问生产、质量或工艺问题…" onKeyDown={event => { if (event.key === "Enter" && !event.shiftKey) { event.preventDefault(); event.currentTarget.form?.requestSubmit(); } }} />
            {submitting ? <Button type="button" onClick={cancel} disabled={cancelling}>{cancelling ? "取消中" : "停止"}</Button> : <Button className="size-10 rounded-xl px-0" variant="primary" type="submit" aria-label="发送消息" disabled={analysisBlocked || !question.trim()}><PaperAirplaneIcon className="size-5" /></Button>}
          </form>
          <p className="mx-auto mt-2 max-w-4xl text-center text-[13px] text-slate-500">回答基于平台记录，并标注证据范围。</p>
        </footer>
      </section>
      <Drawer
        open={historyOpen}
        onClose={() => setHistoryOpen(false)}
        title="对话记录"
        description={projectId ? `当前项目共 ${scopedHistory.length} 条` : `当前账号共 ${scopedHistory.length} 条`}
        size="md"
      >
        <ChatHistoryList items={scopedHistory} loading={historyLoading} submitting={submitting} deletingConversationId={deletingConversationId} selectedConversationId={routeConversationId} onOpen={openHistory} onDelete={deleteHistory} />
      </Drawer>
      {confirmationDialog}
    </div>
  );
}
