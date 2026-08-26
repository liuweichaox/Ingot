
// 提供待检任务录入、附件预览、复核和历史记录页面。
import { Tab, TabGroup, TabList, TabPanel, TabPanels } from "@headlessui/react";
import { MagnifyingGlassIcon } from "@heroicons/react/24/outline";
import { useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router";
import { getBlob, getJson, postForm, postJson } from "../api/http";
import { qualityOutcomeTraces } from "../charts/chartAdapters";
import { extractRows, useApi } from "../hooks/useApi";
import { Alert, Button, Card, DataTable, Drawer, EmptyState, Field, Input, Metric, Pagination, Page, RequestError, Select, StatusBadge, Textarea, WorkflowGuide, notify } from "../ui/components";
import { formatTime, LoadingCard, uuidv7 } from "./shared";
import PlotlyChart from "../components/PlotlyChart";

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

function InspectionAttachmentPreview({ attachment }) {
  const [objectUrl, setObjectUrl] = useState("");
  const [error, setError] = useState("");
  const contentUrl = `/api/v1/inspection-attachments/${encodeURIComponent(attachment.attachmentId)}/content`;

  useEffect(() => {
    let active = true;
    let url = "";
    getBlob(contentUrl).then(blob => {
      if (!active) return;
      url = URL.createObjectURL(blob);
      setObjectUrl(url);
    }).catch(requestError => {
      if (active) setError(requestError.message);
    });
    return () => {
      active = false;
      if (url) URL.revokeObjectURL(url);
    };
  }, [contentUrl]);

  function download() {
    if (!objectUrl) return;
    const anchor = document.createElement("a");
    anchor.href = objectUrl;
    anchor.download = attachment.fileName;
    document.body.append(anchor);
    anchor.click();
    anchor.remove();
  }

  return (
    <article className="overflow-hidden rounded-xl border border-slate-200">
      {attachment.mediaType?.startsWith("image/") && objectUrl && (
        <img src={objectUrl} alt={attachment.fileName} className="max-h-72 w-full bg-slate-50 object-contain" />
      )}
      <div className="p-3 text-sm">
        <p className="font-medium text-slate-900">{attachment.fileName}</p>
        <p className="mt-1 text-xs text-slate-500">{Math.ceil(attachment.sizeBytes / 1024)} KiB</p>
        {error ? <p className="mt-2 text-xs text-red-600">{error}</p> : (
          <button className="mt-2 inline-flex text-sm font-medium text-blue-600 hover:text-blue-700 disabled:text-slate-400" type="button" disabled={!objectUrl} onClick={download}>
            {objectUrl ? "下载原始文件" : "正在读取附件…"}
          </button>
        )}
      </div>
    </article>
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
  const [form, setForm] = useState({ outputItemId: "", executionId: "", definitionKey: "", outcome: "PASS", notes: "", measurements: {}, file: null });
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
    form.executionId.trim() && selectedDefinition &&
    measurementsComplete && (!requiresAttachment || form.file),
  );
  const availableDefinitions = taskTarget
    ? definitionRows.filter(item => taskTarget.missingDefinitionCodes?.includes(item.code))
    : definitionRows;

  function openTask(task = null) {
    const firstDefinition = definitionRows.find(item => item.code === task?.missingDefinitionCodes?.[0]) || definitionRows[0];
    setTaskTarget(task);
    setForm({
      outputItemId: task?.outputItemId || "",
      executionId: task?.executionId || "",
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
        if (taskTarget?.siteId) upload.append("siteId", taskTarget.siteId);
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
        siteId: taskTarget?.siteId || "",
        recordId: uuidv7(),
        outputItemId: form.outputItemId.trim() || null,
        executionId: form.executionId.trim(),
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
    <Page title="检验任务" actions={<Button onClick={() => openTask()}>补录检测记录</Button>}>
      <RequestError
        error={tasks.error || taskSummary.error || records.error || definitions.error || (!entryOpen && !reviewOpen && actionError)}
        onRetry={() => Promise.all([tasks.reload(), taskSummary.reload(), records.reload(), definitions.reload()])}
      />
      <section className="grid grid-cols-2 divide-x divide-y divide-slate-200 rounded-lg border border-slate-200 bg-white sm:grid-cols-4 sm:divide-y-0" aria-label="检验任务摘要">
        {[
          ["需要处理", taskSummary.data?.actionRequired ?? "—", "录入与复核"],
          ["待录入", taskSummary.data?.pending ?? "—", "等待检测结果"],
          ["待复核", taskSummary.data?.reviewPending ?? "—", "需要独立确认"],
          ["已完成", taskSummary.data?.completed ?? "—", "记录已归档"],
        ].map(([label, value, hint]) => (
          <div key={label} className="px-4 py-3">
            <p className="text-[13px] font-medium text-slate-500">{label}</p>
            <div className="mt-1 flex items-baseline gap-2">
              <strong className="text-xl font-semibold text-slate-950 tabular-nums">{value}</strong>
              <span className="text-[13px] text-slate-500">{hint}</span>
            </div>
          </div>
        ))}
      </section>
      <TabGroup>
        <TabList className="flex w-fit gap-1 rounded-lg border border-slate-200 bg-white p-1">
          <Tab className="rounded-md px-4 py-2 text-sm font-medium text-slate-600 outline-none data-selected:bg-white data-selected:text-blue-700">任务队列</Tab>
          <Tab className="rounded-md px-4 py-2 text-sm font-medium text-slate-600 outline-none data-selected:bg-white data-selected:text-blue-700">检测记录</Tab>
        </TabList>
        <TabPanels className="mt-4">
          <TabPanel>
            <Card
              title={`检测任务（${tasks.data?.total ?? extractRows(tasks.data).length}）`}
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
                getRowKey={row => `${row.executionId}:${row.inspectionPlanId}:${row.inspectionPlanVersion}`}
                columns={[
                  { key: "executionId", label: "运行" },
                  { key: "outputItemId", label: "工件" },
                  { key: "inspectionPlanName", label: "质量方案" },
                  { key: "status", label: "状态", render: value => <StatusBadge value={value} /> },
                  { key: "completedAt", label: "运行完成", render: formatTime },
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
            <Card title={`检测记录（${records.data?.total ?? extractRows(records.data).length}）`}>
              <DataTable
                rows={extractRows(records.data)}
                keyField="recordId"
                columns={[
                  { key: "outputItemId", label: "工件" },
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
            { title: "确认工件与运行", description: "从任务进入时已自动带入。", state: form.outputItemId && form.executionId ? "done" : "current" },
            { title: "选择检测项目", description: "检测定义决定要填写的字段和判定规则。", state: selectedDefinition ? "done" : form.outputItemId && form.executionId ? "current" : "upcoming" },
            { title: "填写结果并提交", description: "完成必填项，按需要上传原始附件。", state: entryReady ? "current" : "upcoming" },
          ]}
        />
        <form id="inspection-entry" className="grid gap-5" onSubmit={submitRecord}>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="工件编号"><Input required value={form.outputItemId} readOnly={Boolean(taskTarget)} onChange={event => setForm({ ...form, outputItemId: event.target.value })} /></Field>
            <Field label="运行编号"><Input required value={form.executionId} readOnly={Boolean(taskTarget)} onChange={event => setForm({ ...form, executionId: event.target.value })} /></Field>
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
                <p><span className="text-slate-500">工件：</span>{reviewTarget.outputItemId}</p>
                <p><span className="text-slate-500">运行：</span>{reviewTarget.executionId}</p>
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
                  {reviewTarget.attachments.map(attachment => (
                    <InspectionAttachmentPreview key={attachment.attachmentId} attachment={attachment} />
                  ))}
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
    productFamilyCode: "",
    subjectType: searchParams.get("subjectType") || "",
    subjectId: searchParams.get("subjectId") || "",
  });
  const [query, setQuery] = useState(() => {
    const params = new URLSearchParams({ limit: "1000", offset: "0" });
    if (searchParams.get("subjectType")) params.set("subjectType", searchParams.get("subjectType"));
    if (searchParams.get("subjectId")) params.set("subjectId", searchParams.get("subjectId"));
    return params.toString();
  });
  const { data, loading, error, reload } = useApi(`/api/v1/quality-analysis?${query}`);
  const records = extractRows(data);
  const summary = records.reduce((result, row) => {
    const outcome = String(row.outcome || "INCONCLUSIVE").toUpperCase();
    if (outcome === "PASS") result.pass += 1;
    else if (outcome === "FAIL") result.fail += 1;
    else result.inconclusive += 1;
    result.attachments += Number(row.attachmentCount || 0);
    return result;
  }, { pass: 0, fail: 0, inconclusive: 0, attachments: 0 });
  const productGroups = groupQuality(records, row => row.productFamilyCode || "未关联产品系列");
  const processSpecificationGroups = groupQuality(records, row => [row.processSpecificationId, row.processSpecificationVersion ? `v${row.processSpecificationVersion}` : ""].filter(Boolean).join(" · ") || "未关联工艺规范");
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
    <Page title="偏差分析">
      <Card title="分析范围">
        <form className="grid gap-3 md:grid-cols-[1fr_1fr_1fr_auto]" onSubmit={search}>
          <Field label="产品系列"><Input value={filters.productFamilyCode} onChange={event => setFilters({ ...filters, productFamilyCode: event.target.value })} /></Field>
          <Field label="对象类型"><Input value={filters.subjectType} onChange={event => setFilters({ ...filters, subjectType: event.target.value })} /></Field>
          <Field label="对象 ID"><Input value={filters.subjectId} onChange={event => setFilters({ ...filters, subjectId: event.target.value })} /></Field>
          <Button className="self-end" variant="primary" type="submit"><MagnifyingGlassIcon className="size-4" />分析</Button>
        </form>
      </Card>
      <RequestError error={error} onRetry={reload} />
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
            <Card title="按工艺规范版本">
              <PlotlyChart traces={qualityOutcomeTraces(processSpecificationGroups.slice(0, 12))} layout={chartLayout} height={300} />
              <DataTable rows={processSpecificationGroups} keyField="name" columns={[
                { key: "name", label: "工艺规范" }, { key: "total", label: "检测" },
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
