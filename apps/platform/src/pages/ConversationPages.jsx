import { ArrowLeftIcon, ArrowPathIcon, BeakerIcon, ChatBubbleLeftRightIcon, MagnifyingGlassIcon, PaperAirplaneIcon } from "@heroicons/react/24/outline";
import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { getJson, postJson, streamSse } from "../api/http";
import { extractRows, useApi } from "../hooks/useApi";
import { Alert, Badge, Button, Card, DataTable, Field, Input, Pagination, Page, Select, Textarea } from "../ui/components";
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
  quick: "快速分析",
  combined: "综合分析",
};

const chatModeDescriptions = {
  quick: "优先查询关键记录，适合事实核对和单一问题。",
  combined: "分轮核对多类证据，适合复杂追因；耗时更长。",
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

const researchStatusLabels = {
  draft: "草稿",
  active: "研发中",
  validating: "验证中",
  completed: "已完成",
  archived: "已归档",
};

const suggestedQuestions = [
  "概括当前项目已有证据、关键缺口和下一步建议。",
  "哪些生产运行满足正式分析准入条件？",
  "列出当前最值得优先验证的假设及其依据。",
  "当前结论有哪些数据限制或因果边界？",
];

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
      <article className="rounded-xl border border-slate-200 bg-white shadow-sm">
        <header className="border-b border-slate-100 bg-slate-50/80 px-4 py-3">
          <p className="text-sm font-semibold text-slate-900">分析结论</p>
        </header>
        <p className="whitespace-pre-wrap px-4 py-4 text-sm leading-7 text-slate-700">{answer}</p>
      </article>
    );
  }

  const findings = (answer.findings || []).filter(item => item && item !== answer.summary);
  return (
    <article className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
      <header className="flex items-center justify-between gap-3 border-b border-slate-100 bg-slate-50/80 px-4 py-3">
        <div>
          <p className="text-sm font-semibold text-slate-900">分析结论</p>
          <p className="mt-0.5 text-xs text-slate-500">基于当前可访问记录生成，结论仍需工程验证</p>
        </div>
        <Badge tone="blue">只读分析</Badge>
      </header>
      <div className="space-y-5 px-4 py-4 text-sm leading-7 text-slate-700 sm:px-5">
        <p className="whitespace-pre-wrap font-medium text-slate-900">{answer.summary || "未形成摘要。"}</p>
      {findings.length > 0 && (
        <div>
          <p className="text-xs font-semibold tracking-wide text-slate-500">关键发现</p>
          <ol className="mt-2 space-y-2">
            {findings.map((item, index) => (
              <li key={item} className="flex gap-3 rounded-lg bg-slate-50 px-3 py-2.5">
                <span className="grid size-6 shrink-0 place-items-center rounded-full bg-blue-100 text-xs font-semibold text-blue-700">{index + 1}</span>
                <span>{item}</span>
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
      {(answer.relatedRecords || []).length > 0 && (
        <div>
          <p className="text-xs font-semibold tracking-wide text-slate-500">相关记录</p>
          <div className="mt-2 flex flex-wrap gap-2">
            {answer.relatedRecords.map(item => item.url ? (
              <Link key={`${item.kind}:${item.id}`} to={item.url} className="rounded-lg border border-blue-200 bg-blue-50 px-3 py-1.5 text-xs font-medium text-blue-700 hover:bg-blue-100">
                {item.label}
              </Link>
            ) : null)}
          </div>
        </div>
      )}
      {(answer.followUpQuestions || []).length > 0 && (
        <div>
          <p className="text-xs font-semibold tracking-wide text-slate-500">继续调查</p>
          <div className="mt-2 flex flex-wrap gap-2">
            {answer.followUpQuestions.map(item => (
              <button key={item} type="button" className="rounded-lg border border-slate-200 px-3 py-1.5 text-left text-xs text-slate-700 hover:border-blue-300 hover:bg-blue-50" onClick={() => onFollowUp(item)}>
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
  const [project, setProject] = useState(null);
  const [projectLoading, setProjectLoading] = useState(Boolean(projectId));
  const [projectError, setProjectError] = useState("");
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

  const scopedHistory = projectId
    ? history.filter(item => item.pageContext?.kind === "research-project" && item.pageContext?.id === projectId)
    : history;
  const serviceEnabled = Boolean(capabilities?.enabled);
  const analysisBlocked = capabilitiesLoading || !serviceEnabled || submitting;

  return (
    <Page
      title="项目分析"
      description="从当前优化项目的生产运行、质量、实验和知识记录中核对事实，形成可验证的调查结论。"
      actions={projectId && <Link to={`/research-projects/${encodeURIComponent(projectId)}`} className="inline-flex min-h-9 items-center gap-2 rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"><ArrowLeftIcon className="size-4" />返回优化项目</Link>}
    >
      <section className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
        <div className="grid gap-4 p-4 sm:p-5 lg:grid-cols-[minmax(0,1fr)_auto] lg:items-center">
          <div className="flex min-w-0 items-start gap-3">
            <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-blue-50 text-blue-700"><BeakerIcon className="size-5" /></span>
            <div className="min-w-0">
              <p className="text-xs font-semibold tracking-wide text-blue-700">当前分析范围</p>
              {projectLoading ? <p className="mt-1 text-sm text-slate-500">正在读取优化项目…</p> : project ? (
                <>
                  <h2 className="mt-1 break-words text-lg font-semibold text-slate-950">{project.name}</h2>
                  <p className="mt-1 line-clamp-1 text-sm leading-6 text-slate-500 sm:line-clamp-2">{project.description || "该项目尚未填写问题说明。"}</p>
                  <p className="mt-2 text-xs font-medium text-slate-600 sm:hidden">{researchStatusLabels[project.status] || project.status} · {project.productName || "未限定产品"} · {project.objectives?.map(item => item.name).filter(Boolean).join("、") || "未设定目标"}</p>
                </>
              ) : (
                <h2 className="mt-1 text-base font-semibold text-slate-900">未绑定优化项目</h2>
              )}
            </div>
          </div>
          {project && (
            <dl className="hidden grid-cols-2 gap-x-5 gap-y-2 text-sm sm:grid sm:grid-cols-4 lg:grid-cols-2 xl:grid-cols-4">
              <div><dt className="text-xs text-slate-500">阶段</dt><dd className="mt-1 font-medium text-slate-800">{researchStatusLabels[project.status] || project.status}</dd></div>
              <div><dt className="text-xs text-slate-500">工艺</dt><dd className="mt-1 max-w-40 truncate font-medium text-slate-800" title={project.processName}>{project.processName || "未记录"}</dd></div>
              <div><dt className="text-xs text-slate-500">产品</dt><dd className="mt-1 max-w-40 truncate font-medium text-slate-800" title={project.productName}>{project.productName || "未限定"}</dd></div>
              <div><dt className="text-xs text-slate-500">研发目标</dt><dd className="mt-1 max-w-40 truncate font-medium text-slate-800" title={project.objectives?.map(item => item.name).join("、")}>{project.objectives?.map(item => item.name).filter(Boolean).join("、") || "未设定"}</dd></div>
            </dl>
          )}
        </div>
        <div className="border-t border-slate-100 bg-slate-50/70 px-4 py-3 text-xs text-slate-600 sm:px-5">
          <p className="sm:hidden">只读分析 · 附证据边界 · 不自动修改工艺</p>
          <div className="hidden flex-wrap items-center gap-x-5 gap-y-2 sm:flex">
            <span>✓ 只读取当前账户有权访问的记录</span>
            <span>✓ 回答附带数据限制与相关记录</span>
            <span>✓ 不执行设备写入或自动工艺变更</span>
          </div>
        </div>
      </section>

      {projectError && <Alert tone="danger" title="无法读取优化项目">{projectError}</Alert>}
      {!projectId && <Alert tone="warning" title="建议从优化项目进入">未绑定项目时无法限定研发记录和知识范围，请从具体优化项目打开分析助手。</Alert>}
      {!capabilitiesLoading && capabilities && !serviceEnabled && (
        <Alert tone="warning" title="分析服务未启用">当前部署未配置分析模型服务，启用后即可提交问题。</Alert>
      )}
      {error && <Alert tone="danger">{error}</Alert>}

      <div className="grid items-start gap-5 xl:grid-cols-[minmax(0,1fr)_20rem]">
        <div className="space-y-5">
          <Card title="提出调查问题" description="一次只问一个可核对的问题；明确现象、范围和希望得到的判断。">
            <form className="space-y-4" onSubmit={start}>
              <Field label="调查问题" hint="系统会保留问题、查询过程、回答和引用记录，便于后续复核。">
                <Textarea className="min-h-24" required value={question} onChange={event => setQuestion(event.target.value)} disabled={!serviceEnabled || submitting} placeholder="例如：为什么这批运行的中心厚度偏差增大？请比较阶段信号并列出证据缺口。" />
              </Field>
              <div>
                <p className="text-xs font-medium text-slate-500">常用问题</p>
                <div className="mt-2 flex flex-wrap gap-2">
                  {suggestedQuestions.map(item => <button key={item} type="button" className="rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-left text-xs leading-5 text-slate-700 hover:border-blue-300 hover:bg-blue-50 disabled:cursor-not-allowed disabled:opacity-50" onClick={() => setQuestion(item)} disabled={!serviceEnabled || submitting}>{item}</button>)}
                </div>
              </div>
              <div className="flex flex-col gap-3 border-t border-slate-100 pt-4 sm:flex-row sm:items-end sm:justify-between">
                <Field label="分析方式" hint={chatModeDescriptions[mode] || "按当前服务能力分析。"}>
                  <Select className="sm:w-44" value={mode} onChange={event => setMode(event.target.value)} disabled={!serviceEnabled || submitting}>
                    {(capabilities?.modes || ["quick"]).map(item => <option key={item} value={item}>{chatModeLabels[item] ?? item}</option>)}
                  </Select>
                </Field>
                <div className="flex gap-2 sm:pb-0.5">
                  {submitting && <Button type="button" onClick={cancel} disabled={cancelling}>{cancelling ? "正在取消" : "取消分析"}</Button>}
                  <Button className="min-h-10 justify-center sm:min-w-32" variant="primary" type="submit" disabled={analysisBlocked || !question.trim()}>
                    <PaperAirplaneIcon className="size-4" />{submitting ? "分析中" : capabilitiesLoading ? "连接中" : "开始分析"}
                  </Button>
                </div>
              </div>
            </form>
          </Card>

          <Card title={run ? "调查结果" : "结果与证据"} description={run ? "问题、执行进度、结论边界和相关记录保存在同一调查记录中。" : "提交问题后，这里会按结论、关键发现、证据边界和相关记录组织结果。"}>
            {!run ? (
              <div className="grid gap-3 sm:grid-cols-3">
                {[['1', '查询事实', '读取项目范围内的运行、质量、实验和知识记录。'], ['2', '核对证据', '区分已观测事实、推断和缺失数据。'], ['3', '形成结论', '给出边界、相关记录和可继续验证的问题。']].map(([step, title, description]) => (
                  <div key={step} className="rounded-xl border border-slate-200 bg-slate-50/70 p-4">
                    <span className="grid size-7 place-items-center rounded-full bg-blue-100 text-xs font-semibold text-blue-700">{step}</span>
                    <p className="mt-3 text-sm font-semibold text-slate-900">{title}</p>
                    <p className="mt-1 text-xs leading-5 text-slate-500">{description}</p>
                  </div>
                ))}
              </div>
            ) : (
              <div className="space-y-4">
                <section className="rounded-xl border border-blue-100 bg-blue-50 px-4 py-3">
                  <p className="text-xs font-semibold text-blue-700">调查问题</p>
                  <p className="mt-1 whitespace-pre-wrap text-sm leading-6 text-slate-800">{run.question || question}</p>
                </section>
                {!run.answer && visibleProgress.length > 0 && (
                  <ol className="space-y-2" aria-label="分析进度">
                    {visibleProgress.map((item, index) => (
                      <li key={`${item.sequence || item.type || "event"}-${index}`} className="flex items-start gap-3 rounded-lg bg-slate-50 px-3 py-2.5 text-sm text-slate-700">
                        <ArrowPathIcon className="mt-0.5 size-4 shrink-0 animate-spin text-blue-600" /><p className="whitespace-pre-wrap">{item.message}</p>
                      </li>
                    ))}
                  </ol>
                )}
                {submitting && visibleProgress.length === 0 && <div className="inline-flex items-center gap-2 rounded-lg bg-slate-100 px-3 py-2.5 text-sm text-slate-600"><ArrowPathIcon className="size-4 animate-spin" />正在理解问题</div>}
                <ChatAnswer answer={run.answer} onFollowUp={setQuestion} />
                {run.error && <Alert tone="danger" title="分析失败">{run.error}</Alert>}
                {run.cancellationReason && <Alert title="分析已取消">{run.cancellationReason}</Alert>}
              </div>
            )}
          </Card>
        </div>

        <Card className="xl:sticky xl:top-36" title={projectId ? "本项目调查记录" : "最近调查记录"} description="选择记录查看完整问题、结论和证据边界。">
          {historyLoading ? (
            <div className="inline-flex items-center gap-2 py-6 text-sm text-slate-500"><ArrowPathIcon className="size-4 animate-spin" />正在读取</div>
          ) : scopedHistory.length > 0 ? (
            <div className="space-y-2">
              {scopedHistory.map(item => (
                <button key={item.runId} type="button" className="w-full rounded-xl border border-slate-200 px-3 py-3 text-left hover:border-blue-300 hover:bg-blue-50/50 disabled:opacity-60" onClick={() => openHistory(item.runId)} disabled={submitting}>
                  <div className="flex items-start gap-2">
                    <ChatBubbleLeftRightIcon className="mt-0.5 size-4 shrink-0 text-blue-600" />
                    <p className="line-clamp-2 text-sm font-medium leading-5 text-slate-800">{item.question}</p>
                  </div>
                  <div className="mt-2 flex items-center justify-between gap-2 text-xs text-slate-400">
                    <span>{chatHistoryStatusLabels[item.status] || item.status}</span><time>{formatTime(item.createdAt)}</time>
                  </div>
                </button>
              ))}
            </div>
          ) : (
            <div className="py-8 text-center">
              <ChatBubbleLeftRightIcon className="mx-auto size-8 text-slate-300" />
              <p className="mt-3 text-sm font-medium text-slate-700">暂无本项目调查记录</p>
              <p className="mt-1 text-xs leading-5 text-slate-500">提交第一个问题后，记录会保存在这里。</p>
            </div>
          )}
        </Card>
      </div>
    </Page>
  );
}
