// 管理系统内 AI 助手的会话、历史、流式状态和受控删除交互。
import { ArrowPathIcon, ChatBubbleLeftRightIcon, CheckCircleIcon, ChevronDownIcon, ClockIcon, InformationCircleIcon, LinkIcon, MagnifyingGlassIcon, PaperAirplaneIcon, PlusIcon, SparklesIcon, StopIcon, TrashIcon } from "@heroicons/react/24/outline";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { deleteJson, getJson, postJson, streamSse } from "../api/http";
import { extractRows, useApi } from "../hooks/useApi";
import { useSseReconnect } from "../hooks/useSseStream";
import { Alert, Badge, Button, Card, DataTable, Drawer, Field, Input, Pagination, Page, notify, useConfirmDialog } from "../ui/components";
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
  "run.started": "正在处理",
  "plan.created": "已确定处理方式",
  "iteration.started": "正在准备回答",
  "tool.started": "正在读取相关信息",
  "tool.completed": "已读取相关信息",
  "tool.failed": "读取相关信息失败",
  "plan.rejected": "查询方式未通过校验",
  "relatedRecords.checked": "正在核对相关内容",
  "discussion.started": "正在进行多视角分析",
  "discussion.message": "正在整理分析结果",
  "discussion.completed": "多视角分析完成",
  "answer.delta": "正在组织回答",
  "run.retrying": "模型服务暂时不可用，稍后重试",
  "run.completed": "回答已完成",
  "run.failed": "对话处理失败",
  "run.cancelled": "对话已取消",
};

const chatHistoryStatusLabels = {
  queued: "等待处理",
  running: "处理中",
  cancelling: "正在取消",
  failed: "处理失败",
  cancelled: "已取消",
  completed: "已完成",
};

function chatProgressText(item) {
  const payload = item?.data || {};
  if (item?.type === "tool.completed" && payload.summary) return payload.summary;
  if (item?.type === "tool.failed" && payload.error) return payload.error;
  if (item?.type === "plan.rejected" && payload.error) return payload.error;
  if (item?.type === "answer.delta" && payload.text) return payload.text;
  if (item?.type === "run.failed" && payload.error) return payload.error;
  if (item?.type === "run.cancelled" && payload.reason) return payload.reason;
  return chatProgressLabels[item?.type] || "";
}

async function streamChatRun(streamUrl, { signal, onEvent, maxReconnects = 5 }) {
  let lastEventId = 0;
  let reconnects = 0;
  let terminalReceived = false;
  while (!signal.aborted && !terminalReceived) {
    try {
      lastEventId = await streamSse(streamUrl, {
        signal,
        lastEventId,
        onEvent: event => {
          if (Number.isFinite(event?.id)) lastEventId = Math.max(lastEventId, event.id);
          onEvent(event);
          terminalReceived = ["run.completed", "run.failed", "run.cancelled"].includes(event?.data?.type);
        },
      });
      if (terminalReceived) return;
      reconnects += 1;
    } catch (streamError) {
      if (streamError.name === "AbortError" || signal.aborted) return;
      reconnects += 1;
      if (reconnects > maxReconnects) throw streamError;
    }
    if (reconnects > maxReconnects) throw new Error("对话连接中断，已达到自动重连次数上限。");
    await new Promise((resolve, reject) => {
      const timer = window.setTimeout(resolve, Math.min(1000 * reconnects, 5000));
      signal.addEventListener("abort", () => {
        window.clearTimeout(timer);
        reject(Object.assign(new Error("The operation was aborted."), { name: "AbortError" }));
      }, { once: true });
    });
  }
}

function normalizeAssistantContent(content) {
  return content
    .replace(/\b\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})\b/g, value => formatTime(value))
    .replace(/\btruncated\s*=\s*false\b/gi, "结果未截断")
    .replace(/\btruncated\s*=\s*true\b/gi, "结果已截断")
    .replace(/\breturnedCount\s*=\s*/gi, "已返回 ")
    .replace(/\btotalCount\s*=\s*/gi, "总数 ");
}

function AssistantAvatar({ small = false }) {
  return (
    <span className={`grid shrink-0 place-items-center rounded-[0.85rem] bg-coal-950 text-evidence-400 shadow-[0_3px_10px_rgba(18,25,23,0.12)] ${small ? "size-7" : "size-9"}`} aria-hidden="true">
      <SparklesIcon className={small ? "size-3.5" : "size-4.5"} />
    </span>
  );
}

export function MarkdownContent({ content, className = "" }) {
  if (typeof content !== "string" || !content.trim()) return null;
  return (
    <div className={`chat-markdown min-w-0 space-y-3 break-words ${className}`}>
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        components={{
          h1: ({ children }) => <h1 className="mt-5 text-xl font-semibold tracking-tight text-slate-950 first:mt-0">{children}</h1>,
          h2: ({ children }) => <h2 className="mt-5 text-lg font-semibold text-slate-950 first:mt-0">{children}</h2>,
          h3: ({ children }) => <h3 className="mt-4 text-base font-semibold text-slate-950 first:mt-0">{children}</h3>,
          p: ({ children }) => <p className="leading-7">{children}</p>,
          ul: ({ children }) => <ul className="list-disc space-y-1 pl-5">{children}</ul>,
          ol: ({ children }) => <ol className="list-decimal space-y-1 pl-5">{children}</ol>,
          li: ({ children }) => <li className="pl-1 leading-7">{children}</li>,
          strong: ({ children }) => <strong className="font-semibold text-slate-950">{children}</strong>,
          em: ({ children }) => <em className="italic">{children}</em>,
          blockquote: ({ children }) => <blockquote className="border-l-2 border-slate-300 pl-4 text-slate-600">{children}</blockquote>,
          hr: () => <hr className="border-slate-200" />,
          a: ({ href, children }) => <a className="font-medium text-trajectory-700 underline decoration-trajectory-300 underline-offset-2 hover:text-trajectory-900" href={href} target="_blank" rel="noreferrer">{children}</a>,
          pre: ({ children }) => <pre className="overflow-x-auto rounded-lg bg-coal-950 px-4 py-3 text-xs leading-6 text-slate-100">{children}</pre>,
          code: ({ className: codeClassName, children }) => <code className={`font-mono text-[0.9em] ${codeClassName || ""}`}>{children}</code>,
          table: ({ children }) => <div className="overflow-x-auto"><table className="min-w-full border-collapse text-left text-[13px]">{children}</table></div>,
          thead: ({ children }) => <thead className="border-b border-slate-300 bg-slate-50">{children}</thead>,
          th: ({ children }) => <th className="px-3 py-2 font-semibold text-slate-800">{children}</th>,
          td: ({ children }) => <td className="border-b border-slate-200 px-3 py-2 align-top">{children}</td>,
        }}
      >
        {normalizeAssistantContent(content)}
      </ReactMarkdown>
    </div>
  );
}

function referenceKey(reference) {
  return `${reference?.kind || ""}:${reference?.id || ""}`;
}

function uniqueReferences(references) {
  const seen = new Set();
  return references.filter(reference => {
    if (!reference?.kind || !reference?.id || seen.has(referenceKey(reference))) return false;
    seen.add(referenceKey(reference));
    return true;
  });
}

function ReferenceTarget({ reference, children, className = "" }) {
  if (!reference?.url) return <span className={className}>{children}</span>;
  if (/^https?:\/\//i.test(reference.url)) {
    return <a className={className} href={reference.url} target="_blank" rel="noreferrer">{children}</a>;
  }
  return <Link className={className} to={reference.url}>{children}</Link>;
}

function CitationMarkers({ references, sourceNumbers }) {
  const markers = uniqueReferences(references || [])
    .map(reference => ({ reference, number: sourceNumbers.get(referenceKey(reference)) }))
    .filter(item => item.number);
  if (!markers.length) return null;
  return (
    <span className="ml-1 inline-flex items-baseline gap-0.5 align-super text-[11px] leading-none">
      {markers.map(({ reference, number }) => (
        <sup key={number}>
          <ReferenceTarget
            reference={reference}
            className="rounded bg-slate-100 px-1 py-0.5 font-medium text-slate-500 no-underline hover:bg-trajectory-50 hover:text-trajectory-700"
          >[{number}]</ReferenceTarget>
        </sup>
      ))}
    </span>
  );
}

function SourceReferences({ references }) {
  if (!references.length) return null;
  return (
    <details className="group border-t border-slate-200/80 pt-3" open={references.length <= 3}>
      <summary className="flex cursor-pointer list-none items-center justify-between gap-3 text-sm font-semibold text-slate-700 marker:content-none">
        <span className="inline-flex items-center gap-2"><LinkIcon className="size-4 text-trajectory-600" />来源 <span className="text-xs font-normal text-slate-400">{references.length} 条记录</span></span>
        <ChevronDownIcon className="size-4 text-slate-400 transition group-open:rotate-180" />
      </summary>
      <ol className="mt-3 grid gap-2 sm:grid-cols-2">
        {references.map((reference, index) => (
          <li key={referenceKey(reference)} className="flex min-w-0 items-start gap-2 rounded-lg bg-slate-50 px-3 py-2 text-[13px] leading-5 text-slate-600 ring-1 ring-slate-200/70">
            <span className="mt-0.5 shrink-0 font-mono text-xs font-semibold text-slate-400">[{index + 1}]</span>
            <ReferenceTarget
              reference={reference}
              className="min-w-0 truncate text-trajectory-700 underline decoration-trajectory-200 underline-offset-2 hover:text-trajectory-900"
            >
              {reference.label}
            </ReferenceTarget>
          </li>
        ))}
      </ol>
    </details>
  );
}

function AnswerProvenance({ provenance }) {
  if (!provenance) return null;
  const siteIds = Array.isArray(provenance.siteIds) ? provenance.siteIds.filter(Boolean) : [];
  const hasCount = provenance.returnedCount != null || provenance.totalCount != null;
  const hasAuditData = siteIds.length > 0 || hasCount || provenance.truncated || provenance.sourceAsOf;
  if (!hasAuditData) return null;
  const scope = siteIds.length > 0 ? siteIds.join("、") : "当前授权范围";
  const count = hasCount
    ? ` · 返回 ${provenance.returnedCount ?? 0}${provenance.totalCount != null ? ` / ${provenance.totalCount}` : ""}`
    : "";
  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs leading-5 text-slate-400">
      <span>数据范围：{scope}{count}</span>
      {provenance.truncated && <span className="text-amber-700">结果已截断</span>}
      {provenance.readAt && <span>读取于 {formatTime(provenance.readAt)}</span>}
    </div>
  );
}

function AnswerProcessingTime({ milliseconds }) {
  const value = Number(milliseconds);
  if (!Number.isFinite(value) || value < 0) return null;
  const label = value >= 1000
    ? `${(value / 1000).toFixed(1)} 秒`
    : `${Math.round(value)} 毫秒`;
  return <span className="inline-flex items-center gap-1 text-xs text-slate-400"><ClockIcon className="size-3.5" />处理耗时 {label}</span>;
}

function AnswerMeta({ answer, references }) {
  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-2 border-t border-slate-200/80 pt-4">
      <AnswerProcessingTime milliseconds={answer.durationMilliseconds} />
      {references.length > 0 && <span className="inline-flex items-center gap-1 text-xs text-slate-400"><LinkIcon className="size-3.5" />已关联 {references.length} 条记录</span>}
      <AnswerProvenance provenance={answer.provenance} />
    </div>
  );
}

export function ChatAnswer({ answer, onFollowUp }) {
  if (!answer) return null;
  if (typeof answer === "string") {
    return (
      <div className="flex items-start gap-3">
        <AssistantAvatar />
        <article className="max-w-3xl rounded-[1.25rem] rounded-tl-sm bg-white px-5 py-4 text-sm leading-7 text-slate-700 shadow-[0_4px_18px_rgba(18,25,23,0.05)] ring-1 ring-slate-200/90">
          <MarkdownContent content={answer} />
        </article>
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
  const limitations = (answer.limitations || []).filter(item => !isGenericChatLimitation(item));
  const inferredAnswerKind = combined
    ? "analysis"
    : findings.length > 0 || (answer.relatedRecords || []).length > 0 || (answer.charts || []).length > 0 || (answer.proposals || []).length > 0 || limitations.length > 0
      ? "data_lookup"
      : "conversation";
  const answerKind = answer.answerKind || inferredAnswerKind;
  const isConversationReply = answerKind === "conversation";
  const sourceRecords = uniqueReferences([
    ...(answer.relatedRecords || []),
    ...findings.flatMap(item => item.evidenceReferences || []),
    ...(combined?.relatedRecords || []),
    ...(combined?.possibleCauses || []).flatMap(item => item.relatedRecords || []),
    ...(combined?.reviews || []).flatMap(item => item.relatedRecords || []),
  ]);
  const sourceNumbers = new Map(sourceRecords.map((reference, index) => [referenceKey(reference), index + 1]));
  if (isConversationReply) {
    return (
      <div className="flex items-start gap-3">
        <AssistantAvatar />
        <article className="max-w-3xl rounded-[1.25rem] rounded-tl-sm bg-white px-5 py-4 text-sm leading-7 text-slate-700 shadow-[0_4px_18px_rgba(18,25,23,0.05)] ring-1 ring-slate-200/90">
          <MarkdownContent content={answer.summary || "请继续说明你的问题。"} />
          {answer.durationMilliseconds != null && <div className="mt-4 border-t border-slate-100 pt-3"><AnswerProcessingTime milliseconds={answer.durationMilliseconds} /></div>}
          {(answer.followUpQuestions || []).length > 0 && <div className="mt-4 flex flex-wrap gap-2 border-t border-slate-100 pt-3">{answer.followUpQuestions.map(item => (
            <button key={item} type="button" className="rounded-full border border-slate-300 bg-white px-3 py-1.5 text-left text-[13px] font-medium text-slate-700 transition hover:border-trajectory-600 hover:bg-trajectory-50" onClick={() => onFollowUp(item)}>{item}</button>
          ))}</div>}
        </article>
      </div>
    );
  }
  return (
    <div className="flex items-start gap-3">
      <AssistantAvatar />
      <article className="max-w-4xl flex-1 overflow-hidden rounded-[1.35rem] rounded-tl-sm bg-white px-5 py-5 text-sm leading-7 text-slate-700 shadow-[0_5px_24px_rgba(18,25,23,0.06)] ring-1 ring-slate-200/90 sm:px-6 sm:py-6">
        <div className="space-y-6">
          <div className="border-b border-slate-200/80 pb-5">
            {sourceRecords.length > 0 && <div className="mb-3 inline-flex items-center gap-1.5 rounded-full bg-trajectory-50 px-2.5 py-1 text-xs font-medium text-trajectory-700"><span className="size-1.5 rounded-full bg-trajectory-500" />基于系统记录</div>}
            <MarkdownContent content={answer.summary || "未形成摘要。"} className="text-[15px] text-slate-800" />
          </div>
      {findings.length > 0 && (
        <details className="group rounded-xl border border-slate-200/80">
          <summary className="flex cursor-pointer list-none items-center justify-between gap-3 px-4 py-3 text-sm font-semibold text-slate-800 marker:content-none">
            <span className="inline-flex items-center gap-2"><CheckCircleIcon className="size-4 text-trajectory-600" />主要依据 <span className="text-xs font-normal text-slate-400">{findings.length} 项</span></span>
            <ChevronDownIcon className="size-4 text-slate-400 transition group-open:rotate-180" />
          </summary>
          <ol className="grid gap-2 border-t border-slate-200/80 bg-slate-50/50 p-3 sm:grid-cols-2">
            {findings.map((item, index) => (
              <li key={`${item.statement}-${index}`} className="flex gap-3 rounded-xl bg-slate-50/80 px-3.5 py-3 ring-1 ring-slate-200/70">
                <CheckCircleIcon className="mt-0.5 size-5 shrink-0 text-trajectory-600" />
                <div className="min-w-0 flex-1">
                  <div className="text-slate-900"><MarkdownContent content={item.statement} className="space-y-0" /><CitationMarkers references={item.evidenceReferences} sourceNumbers={sourceNumbers} /></div>
                  {item.strength && item.strength !== "observation" && <span className="mt-1 block text-xs text-slate-500">{findingStrengthLabels[item.strength] || item.strength}</span>}
                </div>
              </li>
            ))}
          </ol>
        </details>
      )}
      {limitations.length > 0 && (
        <details className="group rounded-xl bg-amber-50/65 ring-1 ring-amber-200/80">
          <summary className="flex cursor-pointer list-none items-center justify-between gap-3 px-4 py-3 text-sm font-semibold text-amber-900 marker:content-none">
            <span className="inline-flex items-center gap-2"><InformationCircleIcon className="size-4" />数据范围与限制</span>
            <ChevronDownIcon className="size-4 transition group-open:rotate-180" />
          </summary>
          <ul className="border-t border-amber-200/70 px-5 py-3 pl-9 text-[13px] leading-6 text-amber-950">
            {limitations.map(item => <li key={item} className="list-disc">{item}</li>)}
          </ul>
        </details>
      )}
      {combined && (
        <section className="rounded-xl bg-slate-50 px-4 py-4 ring-1 ring-slate-200/80">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <p className="text-sm font-semibold text-slate-950">深入分析</p>
              <p className="mt-1 text-xs text-slate-500">基于相关记录整理</p>
            </div>
            <Badge tone={combined.status === "needs-review" ? "warning" : "neutral"}>{combined.status === "needs-review" ? "待工程复核" : "证据不足"}</Badge>
          </div>
          <MarkdownContent content={combined.summary} className="mt-3 text-sm leading-6 text-slate-700" />
          {(combined.possibleCauses || []).length > 0 && (
            <div className="mt-4 grid gap-2">
              <p className="data-label">可能原因</p>
              {combined.possibleCauses.map(cause => {
                const reviews = (combined.reviews || []).filter(review => review.causeId === cause.causeId);
                const latestReviews = Object.values(reviews.reduce((latest, review) => ({ ...latest, [review.authorRole]: review }), {}));
                return (
                  <article key={cause.causeId} className="border border-slate-200 bg-white p-3">
                    <div className="flex flex-wrap items-center gap-2"><Badge tone="info">{perspectiveLabels[cause.authorRole] || cause.authorRole}</Badge><MarkdownContent content={cause.statement} className="space-y-0 text-sm text-slate-900" /><CitationMarkers references={cause.relatedRecords} sourceNumbers={sourceNumbers} /></div>
                    <div className="mt-1 text-sm leading-6 text-slate-600"><MarkdownContent content={cause.reason} className="space-y-0" /><CitationMarkers references={cause.relatedRecords} sourceNumbers={sourceNumbers} /></div>
                    {latestReviews.length > 0 && <div className="mt-2 flex flex-wrap gap-2">{latestReviews.map(review => <span key={review.authorRole} className="border border-slate-200 bg-slate-50 px-2 py-1 text-[13px] text-slate-600">{perspectiveLabels[review.authorRole] || review.authorRole} · {reviewPositionLabels[review.position] || review.position}</span>)}</div>}
                  </article>
                );
              })}
            </div>
          )}
          {(combined.reviewSteps || []).length > 0 && (
            <details className="mt-3">
              <summary className="cursor-pointer text-[13px] font-medium text-slate-700">查看 {combined.reviewSteps.length} 条交叉审查记录</summary>
              <ol className="mt-2 space-y-2">{combined.reviewSteps.map((step, index) => <li key={`${step.role}-${step.round}-${index}`} className="border-l-2 border-slate-300 bg-white px-3 py-2 text-[13px] leading-5 text-slate-600"><strong className="text-slate-800">第 {step.round} 轮 · {perspectiveLabels[step.role] || step.role}</strong><MarkdownContent content={step.summary} className="space-y-0" /></li>)}</ol>
            </details>
          )}
        </section>
      )}
      <AnswerMeta answer={answer} references={sourceRecords} />
      <SourceReferences references={sourceRecords} />
      {(answer.followUpQuestions || []).length > 0 && (
        <section className="border-t border-slate-200/80 pt-4">
          <span className="sr-only">继续提问</span>
          <div className="flex flex-wrap gap-2">
            {answer.followUpQuestions.map(item => (
              <button key={item} type="button" className="rounded-full border border-slate-300 bg-white px-3 py-1.5 text-left text-[13px] font-medium text-slate-700 transition hover:border-trajectory-600 hover:bg-trajectory-50" onClick={() => onFollowUp(item)}>
                {item}
              </button>
            ))}
          </div>
        </section>
      )}
        </div>
      </article>
    </div>
  );
}

function isGenericChatLimitation(value) {
  return typeof value === "string" && (
    value.includes("只能查已经上报的记录") ||
    value.includes("回答只基于平台记录")
  );
}

function ChatHistoryList({ items, loading, submitting, deletingConversationId, selectedConversationId, onOpen, onDelete }) {
  if (loading) {
    return <div className="inline-flex items-center gap-2 px-2 py-5 text-sm text-slate-500"><ArrowPathIcon className="size-4 animate-spin" />正在读取对话记录</div>;
  }
  if (!items.length) {
    return <p className="px-3 py-6 text-sm leading-6 text-slate-500">从一项生产、质量或工艺核对开始。</p>;
  }
  return items.map(item => (
    <div key={item.conversationId} className={`group mb-1 grid grid-cols-[minmax(0,1fr)_2rem] items-center border-l-2 ${selectedConversationId === item.conversationId ? "border-trajectory-600 bg-white" : "border-transparent hover:border-slate-300 hover:bg-white/80"}`}>
      <button type="button" className="min-w-0 px-3 py-2.5 text-left disabled:opacity-60" onClick={() => onOpen(item.conversationId)} disabled={submitting} aria-current={selectedConversationId === item.conversationId ? "page" : undefined}>
        <p className="truncate text-sm font-medium text-slate-800">{item.title}</p>
        <p className="mt-1 text-xs text-slate-400">{chatHistoryStatusLabels[item.lastMessageStatus] || item.lastMessageStatus || "对话"} · {formatTime(item.lastMessageAt)}</p>
      </button>
      <button type="button" className="grid size-8 place-items-center text-slate-400 opacity-100 hover:bg-rose-50 hover:text-rose-700 focus:opacity-100 lg:opacity-0 lg:group-hover:opacity-100" aria-label={`删除对话：${item.title}`} onClick={() => onDelete(item)} disabled={Boolean(deletingConversationId) || submitting}>
        <TrashIcon className="size-4" />
      </button>
    </div>
  ));
}

function ChatWelcome({ onSelect }) {
  const starters = [
    { id: "capability", title: "了解 AI 助手", detail: "看看这个系统能帮你做什么。", prompt: "这个系统是做什么的？" },
    { id: "run", title: "查看一次生产运行", detail: "了解某次运行的过程和结果。", prompt: "帮我查看一次生产运行：" },
    { id: "comparison", title: "比较两次运行", detail: "找出两个运行之间的主要差异。", prompt: "帮我比较两次生产运行：" },
  ];
  return (
    <section className="mx-auto flex w-full max-w-4xl flex-col items-center px-2 py-10 text-center sm:py-16">
      <span className="grid size-14 place-items-center rounded-2xl bg-coal-950 text-evidence-400 shadow-lg shadow-coal-950/10"><SparklesIcon className="size-7" /></span>
      <h2 className="mt-5 text-2xl font-semibold tracking-[-0.035em] text-slate-950 sm:text-3xl">你好，我是 AI 助手</h2>
      <p className="mt-3 max-w-xl text-sm leading-6 text-slate-500">我可以帮你理解生产、质量和工艺记录。你可以直接提问，也可以从下面的示例开始。</p>
      <div className="mt-8 grid w-full gap-3 text-left sm:grid-cols-3">
        {starters.map((starter, index) => (
          <button key={starter.id} type="button" className="group rounded-2xl border border-slate-200 bg-white p-4 text-left shadow-sm transition hover:-translate-y-0.5 hover:border-trajectory-400 hover:shadow-md focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-trajectory-600" onClick={() => onSelect(starter.prompt)}>
            <span className="text-xs font-semibold text-slate-400">0{index + 1}</span>
            <p className="mt-3 text-sm font-semibold text-slate-900">{starter.title}</p>
            <p className="mt-1.5 text-[13px] leading-5 text-slate-500">{starter.detail}</p>
            <span className="mt-3 block text-xs font-semibold text-trajectory-700">开始提问 <span aria-hidden="true">→</span></span>
          </button>
        ))}
      </div>
    </section>
  );
}

export function ChatPage() {
  const { conversationId: routeConversationId = "" } = useParams();
  const navigate = useNavigate();
  const [urlParams] = useSearchParams();
  const [capabilities, setCapabilities] = useState(null);
  const [capabilitiesLoading, setCapabilitiesLoading] = useState(true);
  const [question, setQuestion] = useState("");
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
  const urlPageContext = useMemo(() => {
    const kind = urlParams.get("contextKind")?.trim().toLowerCase();
    const id = urlParams.get("contextId")?.trim();
    const siteId = urlParams.get("contextSiteId")?.trim();
    return kind && id ? { kind, id, ...(siteId ? { siteId } : {}) } : null;
  }, [urlParams]);
  const activePageContext = urlPageContext || conversation?.pageContext;

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

  const routeFor = useCallback(value => {
    if (value) return `/chat/${encodeURIComponent(value)}`;
    if (!activePageContext) return "/chat";
    const params = new URLSearchParams({
      contextKind: activePageContext.kind,
      contextId: activePageContext.id,
    });
    if (activePageContext.siteId) params.set("contextSiteId", activePageContext.siteId);
    return `/chat?${params}`;
  }, [activePageContext]);

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
    setSubmitting(true);
    setError("");
    setEvents([]);
    clearQueueDelayWarning();
    try {
      const payload = {
        text: submittedQuestion,
        clientMessageId: crypto.randomUUID(),
        mode: "auto",
        ...(activePageContext ? { pageContext: activePageContext } : {}),
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
      try {
        await streamChatRun(created.streamUrl, {
          signal: controller.current.signal,
          onEvent: ({ data }) => {
            setEvents(current => [...current, data]);
            clearQueueDelayWarning();
          },
        });
      } catch (streamError) {
        if (streamError.name !== "AbortError") throw streamError;
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
    navigate(routeFor(""));
  }

  async function deleteHistory(item) {
    if (!await confirm({
      title: "删除对话",
      description: `“${item.title}”的全部消息及分析过程将被永久删除。业务运行、质检和工艺配置记录不会受影响。`,
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
  const serviceEnabled = Boolean(capabilities?.enabled);
  const analysisBlocked = capabilitiesLoading || !serviceEnabled || submitting;

  return (
    <div className="grid h-full min-h-0 bg-slate-50 lg:grid-cols-[17rem_minmax(0,1fr)]">
      <aside className="hidden min-h-0 border-r border-slate-200 bg-slate-50 lg:flex lg:flex-col">
        <div className="border-b border-slate-200 p-4">
          <Button className="w-full justify-center rounded-xl" variant="primary" onClick={newConversation} disabled={submitting}>
            <PlusIcon className="size-4" />新建对话
          </Button>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto px-2 py-3">
          <div className="flex items-center gap-2 px-3 py-2 text-xs font-semibold text-slate-500"><ChatBubbleLeftRightIcon className="size-4" />最近对话</div>
          <ChatHistoryList items={history} loading={historyLoading} submitting={submitting} deletingConversationId={deletingConversationId} selectedConversationId={routeConversationId} onOpen={openHistory} onDelete={deleteHistory} />
        </div>
        <div className="border-t border-slate-200 px-4 py-3 text-xs leading-5 text-slate-400">仅使用当前账号有权访问的记录</div>
      </aside>

      <section className="flex min-h-0 min-w-0 flex-col bg-white">
        <header className="flex min-h-16 flex-wrap items-center justify-between gap-3 border-b border-slate-200 bg-white px-4 py-3 sm:px-6">
          <div className="min-w-0">
            <h1 className="truncate text-base font-semibold text-slate-950">{conversation?.title || "新对话"}</h1>
          </div>
          <div className="flex min-w-0 items-center justify-end gap-2 sm:flex-none">
            <Button className="rounded-xl lg:hidden" onClick={() => setHistoryOpen(true)}><ChatBubbleLeftRightIcon className="size-4" />对话记录</Button>
            <Button className="rounded-xl lg:hidden" onClick={newConversation} disabled={submitting}><PlusIcon className="size-4" />新建</Button>
          </div>
        </header>

        <div className="min-h-0 flex-1 overflow-y-auto bg-[#f8f9fa]">
          <div className="mx-auto flex min-h-full w-full max-w-4xl flex-col px-4 sm:px-6">
            <div className="space-y-3 pt-4">
              {!capabilitiesLoading && capabilities && !serviceEnabled && <Alert tone="warning" title="AI 助手尚未配置">当前 AI 助手尚未配置可用的模型服务，请由平台管理员在“系统管理 → 模型服务”中完成配置。配置完成后即可开始对话。</Alert>}
              {error && <Alert tone="danger">{error}</Alert>}
            </div>
            {!messages.length ? (
              <ChatWelcome onSelect={setQuestion} />
            ) : (
              <div className="w-full space-y-8 self-start py-8">
                {messages.map(message => message.role === "user" ? (
                  <div key={message.messageId} className="flex justify-end">
                    <article className="max-w-2xl rounded-2xl rounded-br-sm bg-coal-950 px-4 py-3 text-sm leading-7 text-white shadow-sm">
                      <p className="whitespace-pre-wrap">{message.text}</p>
                    </article>
                  </div>
                ) : (
                  <section key={message.messageId} className="space-y-4">
                    {message.runId === run?.runId && !message.answer && visibleProgress.length > 0 && <div className="flex items-start gap-3" aria-label="对话进度">
                      <AssistantAvatar />
                      <div className="max-w-3xl rounded-2xl rounded-tl-sm bg-white px-4 py-3 shadow-sm ring-1 ring-slate-200">
                        <div className="flex items-center gap-2 text-sm font-medium text-slate-800"><span className="size-2 animate-pulse rounded-full bg-trajectory-600" />正在分析你的问题</div>
                        <ol className="mt-2 space-y-1 text-xs leading-5 text-slate-500">{visibleProgress.map((item, index) => <li key={`${item.sequence || item.type || "event"}-${index}`} className="whitespace-pre-wrap">{item.message}</li>)}</ol>
                      </div>
                    </div>}
                    {message.runId === run?.runId && submitting && !message.answer && visibleProgress.length === 0 && (queueDelayRunId === message.runId
                      ? <Alert tone="warning" title="对话任务仍在排队">后台 Worker 尚未领取此任务。请检查 Platform Worker 是否运行；任务会在 Worker 恢复后继续处理。</Alert>
                      : <div className="flex items-center gap-3 text-sm text-slate-500"><AssistantAvatar /><div className="rounded-2xl rounded-tl-sm bg-white px-4 py-3 shadow-sm ring-1 ring-slate-200"><span className="inline-flex items-center gap-1.5"><span className="size-1.5 animate-bounce rounded-full bg-slate-400 [animation-delay:-.2s]" /><span className="size-1.5 animate-bounce rounded-full bg-slate-400 [animation-delay:-.1s]" /><span className="size-1.5 animate-bounce rounded-full bg-slate-400" /></span></div></div>)}
                    <ChatAnswer answer={message.answer || message.text} onFollowUp={setQuestion} />
                    {message.status === "failed" && <Alert tone="danger" title="对话处理失败">{message.error || "未能形成回答，请重试。"}</Alert>}
                    {message.status === "cancelled" && <Alert title="对话已取消">{message.error || "本次对话已取消。"}</Alert>}
                  </section>
                ))}
              </div>
            )}
          </div>
        </div>

        <footer className="border-t border-slate-200 bg-white px-3 pb-3 pt-3 sm:px-6">
          <form className="mx-auto max-w-4xl rounded-2xl border border-slate-300 bg-white p-3 shadow-sm transition focus-within:border-trajectory-600 focus-within:ring-4 focus-within:ring-trajectory-500/10" onSubmit={start}>
            <label className="sr-only" htmlFor="investigation-question">提交问题</label>
            <div className="flex items-end gap-3">
              <textarea id="investigation-question" aria-label="提交问题" className="max-h-40 min-h-14 flex-1 resize-none border-0 bg-transparent px-2 py-2 text-sm leading-6 text-slate-900 outline-none placeholder:text-slate-400" rows="1" required value={question} onChange={event => setQuestion(event.target.value)} disabled={!serviceEnabled || submitting} placeholder="输入你的问题…" onKeyDown={handleQuestionKeyDown} />
              {submitting ? <Button className="size-10 shrink-0 rounded-xl px-0" type="button" onClick={cancel} disabled={cancelling} aria-label="停止处理"><StopIcon className="size-4" /></Button> : <Button className="size-10 shrink-0 rounded-xl px-0" variant="primary" type="submit" aria-label="发送消息" disabled={analysisBlocked || !question.trim()}><PaperAirplaneIcon className="size-4" /></Button>}
            </div>
          </form>
        </footer>
      </section>
      <Drawer
        open={historyOpen}
        onClose={() => setHistoryOpen(false)}
        title="对话记录"
        description={`当前账号共 ${history.length} 条`}
        size="md"
      >
        <ChatHistoryList items={history} loading={historyLoading} submitting={submitting} deletingConversationId={deletingConversationId} selectedConversationId={routeConversationId} onOpen={openHistory} onDelete={deleteHistory} />
      </Drawer>
      {confirmationDialog}
    </div>
  );
}
