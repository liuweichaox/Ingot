// 提供 MechanismKnowledgeWorkbench 的可复用界面与交互边界。

import { useCallback, useEffect, useMemo, useState } from "react";
import { getJson, postForm, postJson } from "../api/http";
import { Alert, Button, Card, EmptyState, Field, Input, Select, StatusBadge, Textarea } from "../ui/components";

const emptyVariable = () => ({ variableCode: "", variableRole: "cause", direction: "", delayMilliseconds: "", unit: "1" });
const emptyScope = () => ({ dimensionCode: "product", dimensionValue: "" });
const emptyConstraint = () => ({ variableCode: "", constraintKind: "range", minimum: "", maximum: "", unit: "1", severity: "hard" });
const emptyForbiddenFactor = () => ({ variableCode: "", minimum: "", maximum: "", unit: "1" });
const emptyForbiddenCombination = () => ({ name: "", factors: [emptyForbiddenFactor(), emptyForbiddenFactor()] });
const initialForm = () => ({
  name: "", mechanismType: "qualitative", statement: "", expectedSignature: "",
  falsificationCondition: "", evidenceLevel: "engineering-observation",
  variables: [emptyVariable()], applicability: [emptyScope()], constraints: [], forbiddenCombinations: [],
  evidence: [], sourceId: "", polarity: "supporting",
});

export function MechanismKnowledgeWorkbench({ projectId, sources = [], reloadAssets }) {
  const [claims, setClaims] = useState([]);
  const [conflicts, setConflicts] = useState([]);
  const [form, setForm] = useState(initialForm);
  const [conflict, setConflict] = useState({ leftClaimId: "", rightClaimId: "", conflictKind: "contradiction", rationale: "" });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [upload, setUpload] = useState({ title: "", sourceKind: "document", file: null });
  const [workspace, setWorkspace] = useState({ hypotheses: [], experimentResults: [] });
  const [reviewAction, setReviewAction] = useState(null);
  const [lifecycleAction, setLifecycleAction] = useState(null);
  const [resolutionAction, setResolutionAction] = useState(null);
  const base = `/api/v1/research-projects/${encodeURIComponent(projectId)}/mechanism-claims`;

  const load = useCallback(async () => {
    if (!projectId) return;
    try {
      const [claimPayload, conflictPayload, projectWorkspace] = await Promise.all([
        getJson(base), getJson(`${base}/conflicts`), getJson(`/api/v1/research-projects/${encodeURIComponent(projectId)}`),
      ]);
      setClaims(claimPayload?.data || []);
      setConflicts(conflictPayload?.data || []);
      setWorkspace(projectWorkspace || { hypotheses: [], experimentResults: [] });
    } catch (requestError) {
      setError(requestError.message);
    }
  }, [base, projectId]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => {
    setForm(current => ({ ...current, sourceId: sources.some(source => source.sourceId === current.sourceId) ? current.sourceId : sources[0]?.sourceId || "" }));
  }, [sources]);
  useEffect(() => {
    if (!reloadAssets || !sources.some(source => source.extractionStatus === "pending" || source.extractionStatus === "running")) return undefined;
    const timer = window.setInterval(() => reloadAssets(), 3000);
    return () => window.clearInterval(timer);
  }, [reloadAssets, sources]);

  const sourceById = useMemo(() => Object.fromEntries(sources.map(source => [source.sourceId, source])), [sources]);
  const projectVariables = workspace.project?.variables || [];
  const eligibleResults = useMemo(() => {
    if (!lifecycleAction?.validationHypothesisId) return [];
    const experimentIds = new Set((workspace.experiments || [])
      .filter(item => item.hypothesisId === lifecycleAction.validationHypothesisId && item.status === "completed")
      .map(item => item.experimentId));
    return (workspace.experimentResults || []).filter(item => experimentIds.has(item.experimentId) && item.safetyPassed && item.calculatedFromSource);
  }, [lifecycleAction?.validationHypothesisId, workspace.experimentResults, workspace.experiments]);

  async function save(event) {
    event.preventDefault();
    const source = sourceById[form.sourceId];
    setBusy(true); setError("");
    try {
      await postJson(base, {
        ...form,
        variables: form.variables.map(item => ({ ...item, delayMilliseconds: numberOrNull(item.delayMilliseconds) })),
        applicability: form.applicability,
        constraints: form.constraints.map(item => ({ ...item, minimum: numberOrNull(item.minimum), maximum: numberOrNull(item.maximum) })),
        forbiddenCombinations: form.forbiddenCombinations.map(item => ({ ...item, factors: item.factors.map(factor => ({ ...factor, minimum: numberOrNull(factor.minimum), maximum: numberOrNull(factor.maximum) })) })),
        evidence: form.evidence.length > 0 ? form.evidence : source ? [{ evidenceKind: "knowledge-source", referenceId: source.sourceId, polarity: form.polarity, contentHash: source.sha256 }] : [],
      });
      setForm({ ...initialForm(), sourceId: sources[0]?.sourceId || "" });
      await load();
    } catch (requestError) { setError(requestError.message); }
    finally { setBusy(false); }
  }

  async function generateDraft(sourceId) {
    setBusy(true); setError("");
    try {
      const draft = await postJson(`${base}/draft-from-source`, { sourceId });
      setForm(current => ({
        ...current,
        ...draft,
        sourceId,
        variables: draft.variables?.length ? draft.variables.map(item => ({ ...item, delayMilliseconds: item.delayMilliseconds ?? "" })) : [emptyVariable()],
        applicability: draft.applicability?.length ? draft.applicability : [emptyScope()],
        constraints: (draft.constraints || []).map(item => ({ ...item, minimum: item.minimum ?? "", maximum: item.maximum ?? "" })),
        forbiddenCombinations: (draft.forbiddenCombinations || []).map(item => ({ ...item, factors: item.factors.map(factor => ({ ...factor, minimum: factor.minimum ?? "", maximum: factor.maximum ?? "" })) })),
        evidence: draft.evidence || [],
      }));
    } catch (requestError) { setError(requestError.message); }
    finally { setBusy(false); }
  }

  async function uploadSource(event) {
    event.preventDefault(); setBusy(true); setError("");
    try {
      const data = new FormData();
      data.set("projectId", projectId); data.set("title", upload.title);
      data.set("sourceKind", upload.sourceKind); data.set("file", upload.file);
      await postForm("/api/v1/process-knowledge", data);
      setUpload({ title: "", sourceKind: "document", file: null });
      await reloadAssets?.();
    } catch (requestError) { setError(requestError.message); }
    finally { setBusy(false); }
  }

  async function retryExtraction(sourceId) {
    setBusy(true); setError("");
    try {
      await postJson(`/api/v1/process-knowledge/${encodeURIComponent(sourceId)}/extract`, {});
      await reloadAssets?.();
    } catch (requestError) { setError(requestError.message); }
    finally { setBusy(false); }
  }

  async function submitReview(event) {
    event.preventDefault();
    const { claimId, decision, comment } = reviewAction;
    if (decision === "reject" && !comment.trim()) return;
    setBusy(true); setError("");
    try {
      await postJson(`${base}/${encodeURIComponent(claimId)}/review`, { decision, comment });
      setReviewAction(null);
      await load();
    } catch (requestError) { setError(requestError.message); }
    finally { setBusy(false); }
  }

  function beginTransition(claim) {
    const targetStatus = ({ reviewed: "supported", supported: "validated", validated: "active", active: "retired" })[claim.status];
    if (!targetStatus) return;
    setLifecycleAction({ claimId: claim.claimId, targetStatus, validationHypothesisId: "", resultId: "", evaluationSummary: "", comment: "" });
  }

  async function submitTransition(event) {
    event.preventDefault();
    const result = workspace.experimentResults?.find(item => item.resultId === lifecycleAction.resultId);
    setBusy(true); setError("");
    try {
      await postJson(`${base}/${encodeURIComponent(lifecycleAction.claimId)}/lifecycle`, {
        targetStatus: lifecycleAction.targetStatus,
        evidenceKind: result ? "experiment-result" : null,
        referenceId: result?.resultId || null,
        contentHash: result?.analysisHash || null,
        validationHypothesisId: lifecycleAction.validationHypothesisId || null,
        evaluationOutcome: lifecycleAction.targetStatus === "falsified" ? "falsifies" : "supports",
        evaluationSummary: lifecycleAction.evaluationSummary || null,
        comment: lifecycleAction.comment,
      });
      setLifecycleAction(null);
      await load();
    } catch (requestError) { setError(requestError.message); }
    finally { setBusy(false); }
  }

  async function resolveConflict(event) {
    event.preventDefault(); setBusy(true); setError("");
    try {
      await postJson(`${base}/conflicts/${encodeURIComponent(resolutionAction.conflictId)}/resolve`, { resolution: resolutionAction.resolution });
      setResolutionAction(null); await load();
    } catch (requestError) { setError(requestError.message); }
    finally { setBusy(false); }
  }

  async function addConflict(event) {
    event.preventDefault(); setBusy(true); setError("");
    try {
      const left = claims.find(item => item.claimId === conflict.leftClaimId);
      const right = claims.find(item => item.claimId === conflict.rightClaimId);
      await postJson(`${base}/conflicts`, {
        ...conflict, leftClaimVersion: left?.version || 1, rightClaimVersion: right?.version || 1,
      });
      setConflict({ leftClaimId: "", rightClaimId: "", conflictKind: "contradiction", rationale: "" });
      await load();
    } catch (requestError) { setError(requestError.message); }
    finally { setBusy(false); }
  }

  return (
    <div className="space-y-5">
      {error && <Alert tone="danger">{error}</Alert>}
      <Card title="知识来源" description="上传原始文件后立即计算哈希并执行确定性提取；原文件保持不可变。">
        <form className="grid gap-4 md:grid-cols-[1fr_12rem_1fr_auto] md:items-end" onSubmit={uploadSource}>
          <Field label="来源标题"><Input required value={upload.title} onChange={field(setUpload, "title")} /></Field>
          <Field label="来源类型"><Select value={upload.sourceKind} onChange={field(setUpload, "sourceKind")}><option value="document">文档</option><option value="spreadsheet">表格</option><option value="image">现场图片</option><option value="field-note">现场记录</option></Select></Field>
          <Field label="文件"><Input required type="file" accept=".pdf,.xlsx,.xlsm,.csv,.txt,.md,.png,.jpg,.jpeg,.webp,.tif,.tiff" onChange={event => setUpload(current => ({ ...current, file: event.target.files?.[0] || null }))} /></Field>
          <Button type="submit" variant="primary" disabled={busy || !upload.file}>上传并提取</Button>
        </form>
        {sources.length > 0 && <div className="mt-4 grid gap-2 md:grid-cols-2">{sources.map(source => <div className="flex items-center justify-between gap-3 rounded-lg bg-slate-50 px-3 py-2 text-sm" key={source.sourceId}><span><span className="font-medium text-slate-800">{source.title}</span><span className="ml-2 text-xs text-slate-500">{source.sha256.slice(0, 12)} · {extractionStatusLabel(source.extractionStatus)}</span></span><span className="flex gap-2">{source.extractionStatus === "completed" && <Button disabled={busy} onClick={() => generateDraft(source.sourceId)}>生成语义草稿</Button>}{source.extractionStatus === "failed" && <Button disabled={busy} onClick={() => retryExtraction(source.sourceId)}>重新提取</Button>}</span></div>)}</div>}
        <p className="mt-3 text-xs text-slate-500">语义生成只回填可编辑草稿，不会自动保存、审核或激活；模型输出必须保留原始片段引用。</p>
      </Card>
      <Card title="机理知识工作台" description="把工程经验转成带变量、范围、反证条件和原始引用的声明草稿；创建人与审核人必须分离。">
        <form className="space-y-5" onSubmit={save}>
          <div className="grid gap-4 md:grid-cols-3">
            <Field label="声明名称"><Input required value={form.name} onChange={field(setForm, "name")} /></Field>
            <Field label="机理类型"><Select value={form.mechanismType} onChange={field(setForm, "mechanismType")}>{mechanismTypes.map(([value, label]) => <option key={value} value={value}>{label}</option>)}</Select></Field>
            <Field label="证据等级"><Select value={form.evidenceLevel} onChange={field(setForm, "evidenceLevel")}><option value="engineering-observation">工程观察</option><option value="documented-rule">受控文件</option><option value="experimental">实验结果</option><option value="model-assisted-draft">模型辅助草稿</option></Select></Field>
          </div>
          <div className="grid gap-4 lg:grid-cols-2">
            <Field label="机理陈述"><Textarea required rows={4} value={form.statement} onChange={field(setForm, "statement")} placeholder="什么变量通过什么作用影响什么结果。" /></Field>
            <Field label="反证条件"><Textarea required rows={4} value={form.falsificationCondition} onChange={field(setForm, "falsificationCondition")} placeholder="观察到什么现象时，应认为该声明不成立。" /></Field>
          </div>
          <Field label="预期可观测特征（可选）"><Textarea className="min-h-20" value={form.expectedSignature} onChange={field(setForm, "expectedSignature")} /></Field>

          <EditorRows title="作用变量" add={() => setForm(current => ({ ...current, variables: [...current.variables, emptyVariable()] }))}>
            {form.variables.map((item, index) => <div className="grid gap-3 rounded-xl bg-slate-50 p-3 md:grid-cols-6" key={index}>
              <Field label="变量代码"><Select required value={item.variableCode} onChange={rowField(setForm, "variables", index, "variableCode")}><option value="">请选择项目变量</option>{projectVariables.map(variable => <option key={variable.code} value={variable.code}>{variable.name || variable.code}</option>)}</Select></Field>
              <Field label="作用"><Select value={item.variableRole} onChange={rowField(setForm, "variables", index, "variableRole")}><option value="cause">原因</option><option value="mediator">中介</option><option value="outcome">结果</option><option value="moderator">交互变量</option></Select></Field>
              <Field label="方向"><Select value={item.direction} onChange={rowField(setForm, "variables", index, "direction")}><option value="">未指定</option><option value="increase">增加</option><option value="decrease">降低</option><option value="nonlinear">非线性</option></Select></Field>
              <Field label="时滞（毫秒）"><Input type="number" min="0" value={item.delayMilliseconds} onChange={rowField(setForm, "variables", index, "delayMilliseconds")} /></Field>
              <Field label="单位"><Input required value={item.unit} onChange={rowField(setForm, "variables", index, "unit")} /></Field>
              <Button type="button" variant="danger" disabled={form.variables.length === 1} onClick={() => removeRow(setForm, "variables", index)}>删除</Button>
            </div>)}
          </EditorRows>

          <EditorRows title="禁止参数组合（可选）" add={() => setForm(current => ({ ...current, forbiddenCombinations: [...current.forbiddenCombinations, emptyForbiddenCombination()] }))}>
            {form.forbiddenCombinations.length === 0 && <p className="text-sm text-slate-500">未声明多变量联合禁区。</p>}
            {form.forbiddenCombinations.map((combination, combinationIndex) => <div className="space-y-3 rounded-xl border border-rose-200 bg-rose-50 p-3" key={combinationIndex}>
              <div className="flex items-end gap-3"><Field className="flex-1" label="组合名称"><Input required value={combination.name} onChange={rowField(setForm, "forbiddenCombinations", combinationIndex, "name")} /></Field><Button type="button" variant="danger" onClick={() => removeRow(setForm, "forbiddenCombinations", combinationIndex)}>删除组合</Button></div>
              {combination.factors.map((factor, factorIndex) => <div className="grid gap-3 md:grid-cols-5" key={factorIndex}>
                <Field label="变量"><Select required value={factor.variableCode} onChange={nestedRowField(setForm, combinationIndex, factorIndex, "variableCode")}><option value="">请选择可控变量</option>{projectVariables.filter(variable => variable.role === "control").map(variable => <option key={variable.code} value={variable.code}>{variable.name || variable.code}</option>)}</Select></Field>
                <Field label="最小值"><Input type="number" value={factor.minimum} onChange={nestedRowField(setForm, combinationIndex, factorIndex, "minimum")} /></Field>
                <Field label="最大值"><Input type="number" value={factor.maximum} onChange={nestedRowField(setForm, combinationIndex, factorIndex, "maximum")} /></Field>
                <Field label="单位"><Input required value={factor.unit} onChange={nestedRowField(setForm, combinationIndex, factorIndex, "unit")} /></Field>
                <Button type="button" variant="danger" disabled={combination.factors.length <= 2} onClick={() => removeNestedRow(setForm, combinationIndex, factorIndex)}>删除条件</Button>
              </div>)}
              <Button type="button" onClick={() => addForbiddenFactor(setForm, combinationIndex)}>添加联合条件</Button>
            </div>)}
          </EditorRows>

          <EditorRows title="适用范围" add={() => setForm(current => ({ ...current, applicability: [...current.applicability, emptyScope()] }))}>
            {form.applicability.map((item, index) => <div className="grid gap-3 rounded-xl bg-slate-50 p-3 md:grid-cols-3" key={index}>
              <Field label="维度"><Select value={item.dimensionCode} onChange={rowField(setForm, "applicability", index, "dimensionCode")}><option value="product">产品</option><option value="material">材料</option><option value="equipment">设备</option><option value="tooling">工装</option><option value="process-specification">工艺规范</option><option value="phase">阶段</option></Select></Field>
              <Field label="适用值"><Input required value={item.dimensionValue} onChange={rowField(setForm, "applicability", index, "dimensionValue")} /></Field>
              <Button type="button" variant="danger" disabled={form.applicability.length === 1} onClick={() => removeRow(setForm, "applicability", index)}>删除</Button>
            </div>)}
          </EditorRows>

          <EditorRows title="工程约束（可选）" add={() => setForm(current => ({ ...current, constraints: [...current.constraints, emptyConstraint()] }))}>
            {form.constraints.length === 0 && <p className="text-sm text-slate-500">当前声明不产生约束。</p>}
            {form.constraints.map((item, index) => <div className="grid gap-3 rounded-xl bg-slate-50 p-3 md:grid-cols-7" key={index}>
              <Field label="变量"><Select required value={item.variableCode} onChange={rowField(setForm, "constraints", index, "variableCode")}><option value="">请选择可控变量</option>{projectVariables.filter(variable => variable.role === "control").map(variable => <option key={variable.code} value={variable.code}>{variable.name || variable.code}</option>)}</Select></Field>
              <Field label="约束类型"><Select required value={item.constraintKind} onChange={rowField(setForm, "constraints", index, "constraintKind")}><option value="range">范围</option><option value="safe-range">安全范围</option><option value="preferred-range">优选范围</option></Select></Field>
              <Field label="最小值"><Input type="number" value={item.minimum} onChange={rowField(setForm, "constraints", index, "minimum")} /></Field>
              <Field label="最大值"><Input type="number" value={item.maximum} onChange={rowField(setForm, "constraints", index, "maximum")} /></Field>
              <Field label="单位"><Input required value={item.unit} onChange={rowField(setForm, "constraints", index, "unit")} /></Field>
              <Field label="级别"><Select value={item.severity} onChange={rowField(setForm, "constraints", index, "severity")}><option value="hard">硬约束</option><option value="soft">软约束</option></Select></Field>
              <Button type="button" variant="danger" onClick={() => removeRow(setForm, "constraints", index)}>删除</Button>
            </div>)}
          </EditorRows>

          <div className="grid gap-4 md:grid-cols-2">
            <Field label="原始知识引用" hint="引用冻结的来源标识和 SHA-256；来源不是结论。"><Select required value={form.sourceId} onChange={event => setForm(current => ({ ...current, sourceId: event.target.value, evidence: [] }))}><option value="">请选择已上传来源</option>{sources.map(source => <option key={source.sourceId} value={source.sourceId}>{source.title}</option>)}</Select></Field>
            <Field label="证据方向"><Select value={form.polarity} onChange={field(setForm, "polarity")}><option value="supporting">支持</option><option value="opposing">反对</option></Select></Field>
          </div>
          <Button type="submit" variant="primary" disabled={busy || !form.sourceId}>保存声明草稿</Button>
        </form>
      </Card>

      <Card title="声明审核与生效" description="草稿不能影响实验建议；声明必须依次经过独立审核、两份不同正式实验结果支持与验证，最后才能激活。">
        {claims.length === 0 ? <EmptyState title="暂无机理声明" description="先从上方创建一条带引用和反证条件的声明。" /> : <div className="space-y-3">{claims.map(claim => <article className="rounded-xl border border-slate-200 p-4" key={claim.claimId}>
          <div className="flex flex-wrap items-start justify-between gap-3"><div><h3 className="font-medium text-slate-900">{claim.name}</h3><p className="mt-1 text-sm leading-6 text-slate-600">{claim.statement}</p></div><StatusBadge value={claim.status} /></div>
          <p className="mt-2 text-xs text-slate-500">版本 {claim.version} · {labelForType(claim.mechanismType)} · {claim.variables.length} 个变量 · {claim.evidence.length} 条证据 · 哈希 {claim.contentHash.slice(0, 12)}</p>
          {claim.status === "draft" && <div className="mt-3 flex gap-2"><Button variant="primary" disabled={busy} onClick={() => setReviewAction({ claimId: claim.claimId, decision: "approve", comment: "" })}>通过审核</Button><Button variant="danger" disabled={busy} onClick={() => setReviewAction({ claimId: claim.claimId, decision: "reject", comment: "" })}>驳回</Button></div>}
          {claim.status === "reviewed" && <div className="mt-3"><Button variant="primary" disabled={busy} onClick={() => beginTransition(claim)}>登记支持实验</Button></div>}
          {claim.status === "supported" && <div className="mt-3"><Button variant="primary" disabled={busy} onClick={() => beginTransition(claim)}>登记独立验证实验</Button></div>}
          {claim.status === "validated" && <div className="mt-3"><Button variant="primary" disabled={busy} onClick={() => beginTransition(claim)}>激活用于实验设计</Button></div>}
          {claim.status === "active" && <div className="mt-3"><Button variant="danger" disabled={busy} onClick={() => beginTransition(claim)}>退休该声明</Button></div>}
          {["reviewed", "supported", "validated", "active"].includes(claim.status) && <div className="mt-2"><Button variant="danger" disabled={busy} onClick={() => setLifecycleAction({ claimId: claim.claimId, targetStatus: "falsified", validationHypothesisId: "", resultId: "", evaluationSummary: "", comment: "" })}>登记反证实验</Button></div>}
        </article>)}</div>}
        {reviewAction && <form className="mt-4 space-y-3 rounded-xl border border-blue-200 bg-blue-50 p-4" onSubmit={submitReview}><Field label={reviewAction.decision === "approve" ? "审核意见" : "驳回原因"}><Textarea required={reviewAction.decision === "reject"} value={reviewAction.comment} onChange={event => setReviewAction(current => ({ ...current, comment: event.target.value }))} /></Field><div className="flex gap-2"><Button type="submit" variant="primary" disabled={busy}>确认提交</Button><Button type="button" onClick={() => setReviewAction(null)}>取消</Button></div></form>}
        {lifecycleAction && <form className="mt-4 space-y-3 rounded-xl border border-blue-200 bg-blue-50 p-4" onSubmit={submitTransition}>
          {(lifecycleAction.targetStatus === "supported" || lifecycleAction.targetStatus === "validated" || lifecycleAction.targetStatus === "falsified") && <div className="grid gap-3 md:grid-cols-2"><Field label="验证假设"><Select required value={lifecycleAction.validationHypothesisId} onChange={event => setLifecycleAction(current => ({ ...current, validationHypothesisId: event.target.value, resultId: "" }))}><option value="">请选择已预注册效应的正式假设</option>{(workspace.hypotheses || []).filter(item => item.validationOutcomeCode && item.expectedEffectDirection && Number(item.minimumEffect) > 0).map(item => <option key={item.hypothesisId} value={item.hypothesisId}>{item.statement}</option>)}</Select></Field><Field label="正式实验结果"><Select required value={lifecycleAction.resultId} onChange={event => setLifecycleAction(current => ({ ...current, resultId: event.target.value }))}><option value="">请选择该假设的安全结果</option>{eligibleResults.map(item => <option key={item.resultId} value={item.resultId}>{item.resultId} · {String(item.analysisHash || "").slice(0, 12)}</option>)}</Select></Field><Field label={lifecycleAction.targetStatus === "falsified" ? "结果如何反证该声明" : "结果如何支持该声明"}><Textarea required value={lifecycleAction.evaluationSummary} onChange={event => setLifecycleAction(current => ({ ...current, evaluationSummary: event.target.value }))} /></Field></div>}
          <Field label="决定说明"><Textarea required value={lifecycleAction.comment} onChange={event => setLifecycleAction(current => ({ ...current, comment: event.target.value }))} /></Field><div className="flex gap-2"><Button type="submit" variant="primary" disabled={busy}>确认提交</Button><Button type="button" onClick={() => setLifecycleAction(null)}>取消</Button></div>
        </form>}
      </Card>

      <Card title="知识冲突" description="相互矛盾的声明并存并显式登记，不使用最后写入覆盖。">
        <form className="grid gap-3 md:grid-cols-4" onSubmit={addConflict}>
          <Field label="声明 A"><Select required value={conflict.leftClaimId} onChange={field(setConflict, "leftClaimId")}><option value="">请选择</option>{claims.map(item => <option key={item.claimId} value={item.claimId}>{item.name} v{item.version}</option>)}</Select></Field>
          <Field label="声明 B"><Select required value={conflict.rightClaimId} onChange={field(setConflict, "rightClaimId")}><option value="">请选择</option>{claims.map(item => <option key={item.claimId} value={item.claimId}>{item.name} v{item.version}</option>)}</Select></Field>
          <Field label="冲突类型"><Select value={conflict.conflictKind} onChange={field(setConflict, "conflictKind")}><option value="contradiction">结论冲突</option><option value="scope-overlap">范围重叠</option><option value="unit-mismatch">单位冲突</option><option value="evidence-disagreement">证据分歧</option></Select></Field>
          <Field label="冲突说明"><Input required value={conflict.rationale} onChange={field(setConflict, "rationale")} /></Field>
          <Button type="submit" disabled={busy || claims.length < 2 || conflict.leftClaimId === conflict.rightClaimId}>登记冲突</Button>
        </form>
        {conflicts.length > 0 && <div className="mt-4 space-y-2">{conflicts.map(item => <div className="flex items-center justify-between gap-3 rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-900" key={item.conflictId}><span>{item.rationale} <span className="text-xs">（{item.status === "open" ? "待解决" : `已解决：${item.resolution || "已记录结论"}`}）</span></span>{item.status === "open" && <Button onClick={() => setResolutionAction({ conflictId: item.conflictId, resolution: "" })}>解决冲突</Button>}</div>)}</div>}
        {resolutionAction && <form className="mt-4 space-y-3" onSubmit={resolveConflict}><Field label="冲突解决结论"><Textarea required value={resolutionAction.resolution} onChange={event => setResolutionAction(current => ({ ...current, resolution: event.target.value }))} /></Field><div className="flex gap-2"><Button type="submit" variant="primary" disabled={busy}>确认解决</Button><Button type="button" onClick={() => setResolutionAction(null)}>取消</Button></div></form>}
      </Card>
    </div>
  );
}

function EditorRows({ title, add, children }) { return <section className="space-y-3"><div className="flex items-center justify-between"><h3 className="text-sm font-semibold text-slate-900">{title}</h3><Button type="button" onClick={add}>添加一项</Button></div>{children}</section>; }
function field(setter, key) { return event => setter(current => ({ ...current, [key]: event.target.value })); }
function rowField(setter, collection, index, key) { return event => setter(current => ({ ...current, [collection]: current[collection].map((item, itemIndex) => itemIndex === index ? { ...item, [key]: event.target.value } : item) })); }
function nestedRowField(setter, combinationIndex, factorIndex, key) { return event => setter(current => ({ ...current, forbiddenCombinations: current.forbiddenCombinations.map((combination, index) => index === combinationIndex ? { ...combination, factors: combination.factors.map((factor, nestedIndex) => nestedIndex === factorIndex ? { ...factor, [key]: event.target.value } : factor) } : combination) })); }
function addForbiddenFactor(setter, combinationIndex) { setter(current => ({ ...current, forbiddenCombinations: current.forbiddenCombinations.map((combination, index) => index === combinationIndex ? { ...combination, factors: [...combination.factors, emptyForbiddenFactor()] } : combination) })); }
function removeNestedRow(setter, combinationIndex, factorIndex) { setter(current => ({ ...current, forbiddenCombinations: current.forbiddenCombinations.map((combination, index) => index === combinationIndex ? { ...combination, factors: combination.factors.filter((_, nestedIndex) => nestedIndex !== factorIndex) } : combination) })); }
function removeRow(setter, collection, index) { setter(current => ({ ...current, [collection]: current[collection].filter((_, itemIndex) => itemIndex !== index) })); }
function numberOrNull(value) { return value === "" || value === null || value === undefined ? null : Number(value); }
function labelForType(value) { return mechanismTypes.find(item => item[0] === value)?.[1] || value; }
function extractionStatusLabel(value) { return ({ pending: "待提取", running: "提取中", completed: "已提取", failed: "提取失败" })[value] || "状态未知"; }
const mechanismTypes = [["qualitative", "定性作用链"], ["monotonic", "单调关系"], ["threshold", "阈值"], ["interaction", "交互作用"], ["temporal", "时间响应"], ["constraint", "工程约束"], ["failure-mode", "失效模式"], ["executable-model", "可执行机理模型"]];
