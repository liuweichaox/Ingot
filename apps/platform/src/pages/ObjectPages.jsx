import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router";
import { extractRows, useApi } from "../hooks/useApi";
import { Alert, Badge, Card, EmptyState, Field, Input, Metric, Page, WorkflowGuide } from "../ui/components";
import { formatTime, formatInteger, objectTypeLabel, eventTypeLabel, LoadingCard } from "./shared";

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
      title="对象目录"
      description="从真实设备和生产对象出发，连续查看它的运行、事件、质量与数据健康。"
      actions={<Link className="inline-flex min-h-9 items-center rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700" to="/configuration/ingestion-tasks">接入设备</Link>}
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
                      <p className="mt-1 text-xs leading-5 text-slate-500">围绕当前对象查看运行、事件、质量和数据健康。</p>
                      <div className="mt-3 grid gap-3 sm:grid-cols-2">
                        {[
                          [`/process-executions?equipmentId=${encodeURIComponent(selected.subjectId)}`, "运行记录", "查看该对象的生产运行与上下文"],
                          [`/events?subjectId=${encodeURIComponent(selected.subjectId)}`, "事件时间线", "追溯该对象上报的事件与状态变化"],
                          [`/quality-analysis?subjectType=${encodeURIComponent(selected.subjectType)}&subjectId=${encodeURIComponent(selected.subjectId)}`, "质量偏差分析", "查看与该对象关联的检测结果并追溯运行证据"],
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
              description="设备上报数据后，平台自动建立可追溯对象。"
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
