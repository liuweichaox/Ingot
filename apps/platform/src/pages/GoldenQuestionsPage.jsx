import { useCallback, useEffect, useState } from "react";
import { getJson, postJson } from "../api/http";
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
  Page,
  Select,
  Textarea,
  notify,
} from "../ui/components";

const emptyForm = () => ({
  caseId: "",
  version: 1,
  name: "",
  question: "",
  mode: "quick",
  pageKind: "",
  pageId: "",
  expectRefusal: false,
  expectedFacts: [],
  expectedRecordReferences: [],
});

const parseExpectedValue = value => {
  const trimmed = value.trim();
  if (!trimmed) return "";
  try { return JSON.parse(trimmed); } catch { return trimmed; }
};

export function GoldenQuestionsPage() {
  const [cases, setCases] = useState([]);
  const [evaluations, setEvaluations] = useState([]);
  const [summary, setSummary] = useState({});
  const [form, setForm] = useState(emptyForm);
  const [fact, setFact] = useState({ factId: "", tool: "", jsonPointer: "", expectedValue: "", answerMustContain: "" });
  const [reference, setReference] = useState({ kind: "event-query", id: "", label: "" });
  const [runIds, setRunIds] = useState({});
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    try {
      const [casePayload, evaluationPayload] = await Promise.all([
        getJson("/api/v1/golden-questions"),
        getJson("/api/v1/golden-questions/evaluations?limit=500"),
      ]);
      setCases(casePayload.data || []);
      setEvaluations(evaluationPayload.data || []);
      setSummary(evaluationPayload.summary || {});
      setError("");
    } catch (requestError) {
      setError(requestError.message);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  function beginCreate() {
    setForm(emptyForm());
    setFact({ factId: "", tool: "", jsonPointer: "", expectedValue: "", answerMustContain: "" });
    setReference({ kind: "event-query", id: "", label: "" });
    setOpen(true);
  }

  function beginVersion(source) {
    setForm({
      ...source,
      version: Number(source.version) + 1,
      pageKind: source.pageContext?.kind || "",
      pageId: source.pageContext?.id || "",
    });
    setOpen(true);
  }

  function addFact() {
    if (!fact.factId.trim() || !fact.tool.trim() || (!fact.jsonPointer.startsWith("/") && fact.jsonPointer !== "")) {
      setError("事实需要标识、工具以及以 / 开头的 JSON Pointer。");
      return;
    }
    setForm(current => ({
      ...current,
      expectedFacts: [...current.expectedFacts, {
        ...fact,
        expectedValue: parseExpectedValue(fact.expectedValue),
        answerMustContain: fact.answerMustContain.trim() || null,
      }],
    }));
    setFact({ factId: "", tool: "", jsonPointer: "", expectedValue: "", answerMustContain: "" });
  }

  function addReference() {
    if (!reference.kind.trim() || !reference.id.trim() || !reference.label.trim()) {
      setError("记录引用需要类型、记录标识和显示名称。");
      return;
    }
    setForm(current => ({
      ...current,
      expectedRecordReferences: [...current.expectedRecordReferences, reference],
    }));
    setReference({ kind: "event-query", id: "", label: "" });
  }

  async function saveDraft(event) {
    event.preventDefault();
    setBusy(true);
    try {
      await postJson("/api/v1/golden-questions", {
        ...form,
        caseId: form.caseId || "00000000-0000-0000-0000-000000000000",
        version: Number(form.version),
        entryPoint: "chat",
        pageContext: form.pageKind && form.pageId ? { kind: form.pageKind, id: form.pageId } : null,
        status: "draft",
      });
      setOpen(false);
      await load();
      notify("黄金问题草稿已保存；审核前仍可补充事实和记录引用。");
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setBusy(false);
    }
  }

  async function review(item) {
    setBusy(true);
    try {
      await postJson(`/api/v1/golden-questions/${item.caseId}/${item.version}:review`, {});
      await load();
      notify("该黄金问题版本已审核冻结，后续修改请创建新版本。");
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setBusy(false);
    }
  }

  async function evaluate(item) {
    const agentRunId = runIds[`${item.caseId}@${item.version}`]?.trim();
    if (!agentRunId) {
      setError("请填写使用同一问题完成的 Agent 运行 ID。");
      return;
    }
    setBusy(true);
    try {
      await postJson(`/api/v1/golden-questions/${item.caseId}/${item.version}:evaluate`, { agentRunId });
      await load();
      notify("黄金问题评测完成，事实、引用、拒绝和因果边界已自动核对。");
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setBusy(false);
    }
  }

  const latestEvaluations = new Map(evaluations.map(item => [`${item.caseId}@${item.caseVersion}`, item]));
  const rows = cases.map(item => ({
    ...item,
    key: `${item.caseId}@${item.version}`,
    factCount: item.expectedFacts?.length || 0,
    referenceCount: item.expectedRecordReferences?.length || 0,
    latestEvaluation: latestEvaluations.get(`${item.caseId}@${item.version}`),
  }));

  return (
    <Page title="评测问题集" description="用真实现场问题冻结可核对事实与记录引用，持续评测分析结果是否诚实、有据并正确拒绝。" actions={<Button variant="primary" onClick={beginCreate}>录入真实问题</Button>}>
      {error && <Alert tone="danger">{error}</Alert>}
      <div className="grid gap-4 md:grid-cols-5">
        <Metric label="评测次数" value={summary.evaluationCount || 0} />
        <Metric label="总体通过率" value={`${Math.round((summary.passRate || 0) * 100)}%`} />
        <Metric label="事实门通过率" value={`${Math.round((summary.factGatePassRate || 0) * 100)}%`} />
        <Metric label="引用门通过率" value={`${Math.round((summary.referenceGatePassRate || 0) * 100)}%`} />
        <Metric label="因果守卫通过率" value={`${Math.round((summary.causalGuardGatePassRate || 0) * 100)}%`} />
      </div>
      <Alert tone="info" title="审核规则">草稿可以先记录问题；审核前必须补充工具事实或正确拒绝条件，并至少指定一条原始生产记录。审核版本不可修改。</Alert>
      <Card title="问题与评测">
        {rows.length ? <DataTable rows={rows} keyField="key" columns={[
          { key: "name", label: "问题名称" },
          { key: "version", label: "版本" },
          { key: "question", label: "工程师原问" },
          { key: "status", label: "状态", render: value => <Badge tone={value === "reviewed" ? "success" : "warning"}>{value === "reviewed" ? "已审核" : "草稿"}</Badge> },
          { key: "factCount", label: "事实" },
          { key: "referenceCount", label: "记录引用" },
          { key: "latestEvaluation", label: "最近结果", render: value => value ? <Badge tone={value.passed ? "success" : "danger"}>{value.passed ? "通过" : "失败"}</Badge> : "未评测" },
          { key: "actions", label: "操作", render: (_, item) => <div className="flex min-w-64 flex-wrap gap-2">
            {item.status === "draft" ? <Button size="sm" disabled={busy} onClick={() => review(item)}>审核冻结</Button> : <>
              <Input aria-label={`${item.name} Agent 运行 ID`} className="w-40" placeholder="Agent 运行 ID" value={runIds[item.key] || ""} onChange={event => setRunIds(current => ({ ...current, [item.key]: event.target.value }))} />
              <Button size="sm" disabled={busy} onClick={() => evaluate(item)}>执行评测</Button>
              <Button size="sm" onClick={() => beginVersion(item)}>创建新版本</Button>
            </>}
          </div> },
        ]} /> : <EmptyState title="尚无真实黄金问题" description="从工程师实际调查中录入问题，不要用模型自动生成的问题代替现场审核。" />}
      </Card>
      <Drawer open={open} onClose={() => setOpen(false)} title="黄金问题草稿">
        <form className="space-y-4" onSubmit={saveDraft}>
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="问题名称"><Input required value={form.name} onChange={event => setForm(current => ({ ...current, name: event.target.value }))} /></Field>
            <Field label="版本"><Input type="number" min="1" required value={form.version} onChange={event => setForm(current => ({ ...current, version: event.target.value }))} /></Field>
          </div>
          <Field label="工程师原始问题" hint="保持现场真实措辞；评测运行必须使用完全相同的问题。"><Textarea required value={form.question} onChange={event => setForm(current => ({ ...current, question: event.target.value }))} /></Field>
          <div className="grid gap-3 sm:grid-cols-3">
            <Field label="分析模式"><Select value={form.mode} onChange={event => setForm(current => ({ ...current, mode: event.target.value }))}><option value="quick">快速</option><option value="combined">推理</option></Select></Field>
            <Field label="页面对象类型"><Input value={form.pageKind || ""} onChange={event => setForm(current => ({ ...current, pageKind: event.target.value }))} placeholder="cycle" /></Field>
            <Field label="页面对象 ID"><Input value={form.pageId || ""} onChange={event => setForm(current => ({ ...current, pageId: event.target.value }))} /></Field>
          </div>
          <label className="flex items-center gap-2 text-sm text-slate-700"><input type="checkbox" checked={form.expectRefusal} onChange={event => setForm(current => ({ ...current, expectRefusal: event.target.checked }))} />该问题在现有数据下应明确拒绝确定判断</label>
          <Card title="可自动核对的工具事实" description="JSON Pointer 指向只读工具结果中的字段；审核值支持数字、布尔、null 或文本。">
            <div className="grid gap-2 sm:grid-cols-2">
              <Input aria-label="事实标识" placeholder="事实标识" value={fact.factId} onChange={event => setFact(current => ({ ...current, factId: event.target.value }))} />
              <Input aria-label="工具名" placeholder="工具名，例如 compare_cycles" value={fact.tool} onChange={event => setFact(current => ({ ...current, tool: event.target.value }))} />
              <Input aria-label="JSON Pointer" placeholder="/process/diagnosis/evidenceLevel" value={fact.jsonPointer} onChange={event => setFact(current => ({ ...current, jsonPointer: event.target.value }))} />
              <Input aria-label="审核值" placeholder="审核值" value={fact.expectedValue} onChange={event => setFact(current => ({ ...current, expectedValue: event.target.value }))} />
              <Input aria-label="回答必须包含" placeholder="回答必须包含的审核文本（可选）" value={fact.answerMustContain} onChange={event => setFact(current => ({ ...current, answerMustContain: event.target.value }))} />
              <Button type="button" onClick={addFact}>添加事实</Button>
            </div>
            <ul className="mt-3 list-disc pl-5 text-sm text-slate-600">{form.expectedFacts.map(item => <li key={item.factId}>{item.factId}：{item.tool}{item.jsonPointer}</li>)}</ul>
          </Card>
          <Card title="预期原始记录引用">
            <div className="grid gap-2 sm:grid-cols-2">
              <Input aria-label="引用类型" placeholder="引用类型" value={reference.kind} onChange={event => setReference(current => ({ ...current, kind: event.target.value }))} />
              <Input aria-label="记录标识" placeholder="记录标识" value={reference.id} onChange={event => setReference(current => ({ ...current, id: event.target.value }))} />
              <Input aria-label="显示名称" placeholder="显示名称" value={reference.label} onChange={event => setReference(current => ({ ...current, label: event.target.value }))} />
              <Button type="button" onClick={addReference}>添加记录引用</Button>
            </div>
            <ul className="mt-3 list-disc pl-5 text-sm text-slate-600">{form.expectedRecordReferences.map(item => <li key={`${item.kind}-${item.id}`}>{item.kind}：{item.id}</li>)}</ul>
          </Card>
          <Button variant="primary" type="submit" disabled={busy}>{busy ? "保存中…" : "保存草稿"}</Button>
        </form>
      </Drawer>
    </Page>
  );
}
