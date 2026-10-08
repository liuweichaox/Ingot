// 把已发布配方、真实运行和质量结果接成下一配方建议、工程师决定和结果冻结。
import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router";
import { extractRows, useApi } from "../hooks/useApi";
import {
  activateScope,
  createScope,
  createSuggestion,
  freezeOutcome,
  linkRun,
  listScopes,
  listSuggestionFlows,
  readReadiness,
  recordDecision,
} from "../recipe/suggestionApi";
import { Alert, Button, Card, Field, Input, Page, RequestError, Select, Textarea, notify } from "../ui/components";
import { formatMeasurementValue } from "./shared";

const stateLabels = {
  "pending-decision": "待决定",
  rejected: "已拒绝",
  "pending-execution": "待关联运行",
  "pending-outcome": "待冻结结果",
  "outcome-frozen": "结果已冻结",
  "outcome-excluded": "结果未纳入后续建议",
  stale: "定义已变，需重新生成",
};

const statusLabels = {
  draft: "草稿",
  active: "使用中",
  completed: "已完成",
  archived: "已归档",
};

function numericParameters(model) {
  return (model?.controlParameters || []).filter(item => ["double", "integer"].includes(item.dataType || "double"));
}

function emptyDraft() {
  return {
    code: "",
    name: "",
    processName: "",
    siteCode: "",
    productFamilyCode: "",
    productCode: "",
    equipmentId: "",
    specificationKey: "",
    objectiveCode: "",
    objectiveTarget: "",
    objectiveDirection: "minimize",
    lookbackDays: "365",
    selectedCodes: [],
    bounds: {},
  };
}

export function RecipeSuggestionsPage() {
  const specifications = useApi("/api/v1/process-specifications");
  const models = useApi("/api/v1/process-data-models");
  const definitions = useApi("/api/v1/inspection-definitions");
  const executions = useApi("/api/v1/process-executions?status=completed&limit=100");
  const [scopes, setScopes] = useState([]);
  const [selectedId, setSelectedId] = useState("");
  const [readiness, setReadiness] = useState(null);
  const [flows, setFlows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [creating, setCreating] = useState(false);
  const [draft, setDraft] = useState(emptyDraft);
  const [decisionDrafts, setDecisionDrafts] = useState({});

  const publishedSpecs = extractRows(specifications.data).filter(item => item.status === "published");
  const completedRuns = extractRows(executions.data);
  const characteristics = extractRows(definitions.data).flatMap(definition =>
    (definition.characteristics || []).filter(item => (item.inputType || "numeric") === "numeric").map(item => ({
      ...item,
      definitionName: definition.name || definition.code,
    })));

  async function reload(preferredId) {
    setLoading(true);
    try {
      const listed = extractRows(await listScopes());
      setScopes(listed);
      const nextId = preferredId || selectedId || listed.find(item => item.status === "active")?.projectId || listed[0]?.projectId || "";
      setSelectedId(nextId);
      if (nextId) {
        const [ready, flowPage] = await Promise.all([readReadiness(nextId), listSuggestionFlows(nextId)]);
        setReadiness(ready);
        setFlows(flowPage.items || []);
      } else {
        setReadiness(null);
        setFlows([]);
      }
      setError("");
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void reload(); }, []);

  const selected = scopes.find(item => item.projectId === selectedId) || null;
  const selectedSpec = publishedSpecs.find(item => `${item.processSpecificationId}:${item.version}` === draft.specificationKey);
  const selectedModel = extractRows(models.data).find(item =>
    item.modelId === selectedSpec?.dataModelId && Number(item.version) === Number(selectedSpec?.dataModelVersion));
  const parameterChoices = numericParameters(selectedModel);

  function chooseSpecification(specificationKey) {
    const spec = publishedSpecs.find(item => `${item.processSpecificationId}:${item.version}` === specificationKey);
    const model = extractRows(models.data).find(item =>
      item.modelId === spec?.dataModelId && Number(item.version) === Number(spec?.dataModelVersion));
    const selectedCodes = numericParameters(model).filter(item => item.changeAllowed !== false).map(item => item.code);
    setDraft(current => ({ ...current, specificationKey, selectedCodes }));
  }

  const runOptions = useMemo(() => completedRuns.filter(run =>
    !selected?.siteCode || !run.siteId || run.siteId === selected.siteCode), [completedRuns, selected]);

  function updateDraft(field, value) {
    setDraft(current => ({ ...current, [field]: value }));
  }

  async function submitScope(event) {
    event.preventDefault();
    const objective = characteristics.find(item => item.code === draft.objectiveCode);
    const variables = parameterChoices.filter(item => draft.selectedCodes.includes(item.code)).map(item => {
      const bound = draft.bounds[item.code] || {};
      return {
        code: item.code,
        name: item.displayName || item.code,
        role: "control",
        unit: item.unit || bound.unit || "",
        lowerLimit: Number(bound.lower ?? item.minimum),
        upperLimit: Number(bound.upper ?? item.maximum),
      };
    });
    if (!objective) {
      setError("请选择一个数值质量指标作为优化目标。");
      return;
    }
    if (!variables.length || variables.some(item => !item.unit || !Number.isFinite(item.lowerLimit) || !Number.isFinite(item.upperLimit) || item.lowerLimit >= item.upperLimit)) {
      setError("每个可调参数都需要单位，以及下界小于上界的范围。");
      return;
    }
    setBusy(true);
    setError("");
    try {
      const created = await createScope({
        code: draft.code.trim().toLowerCase(),
        name: draft.name.trim(),
        processName: draft.processName.trim(),
        siteCode: draft.siteCode.trim(),
        productName: draft.productCode.trim() || null,
        status: "draft",
        objectives: [{
          code: objective.code,
          name: objective.name || objective.code,
          unit: objective.unit,
          direction: draft.objectiveDirection,
          target: Number(draft.objectiveTarget),
          dataSource: `inspection:${objective.code}`,
        }],
        variables,
        context: {
          product_family_code: draft.productFamilyCode.trim(),
          product_code: draft.productCode.trim(),
          equipment_id: draft.equipmentId.trim(),
          process_specification_id: selectedSpec.processSpecificationId,
          process_specification_version: String(selectedSpec.version),
          lookback_days: draft.lookbackDays.trim(),
        },
      });
      const active = await activateScope(created.projectId, created.revision);
      notify("建议范围已启用。");
      setCreating(false);
      setDraft(emptyDraft());
      await reload(active.projectId);
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setBusy(false);
    }
  }

  async function runAction(action, success) {
    setBusy(true);
    setError("");
    try {
      await action();
      if (success) notify(success);
      await reload(selectedId);
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setBusy(false);
    }
  }

  function decisionState(flow) {
    return decisionDrafts[flow.item.recommendationKey] || {
      reason: "",
      values: Object.fromEntries((flow.item.parameters || []).map(item => [item.variableCode, String(item.value)])),
      executionKey: "",
    };
  }

  function updateDecision(flow, patch) {
    const key = flow.item.recommendationKey;
    setDecisionDrafts(current => ({ ...current, [key]: { ...decisionState(flow), ...patch } }));
  }

  return (
    <Page
      title="配方建议"
      description="用已发布配方版本和真实运行的质量结果生成下一版配方。工程师决定后，用下一次真实运行冻结结果。"
      loading={loading && !scopes.length && !error}
      error={error}
      onRetry={() => reload(selectedId)}
    >
      <div className="flex flex-wrap items-center gap-2">
        {scopes.map(scope => (
          <Button key={scope.projectId} variant={scope.projectId === selectedId ? "primary" : "secondary"} onClick={() => { setSelectedId(scope.projectId); void reload(scope.projectId); }}>
            {scope.name}
          </Button>
        ))}
        <Button variant="ghost" onClick={() => setCreating(current => !current)}>{creating ? "收起" : "新建范围"}</Button>
        <Link className="text-sm font-medium text-trajectory-700" to="/comparisons">返回运行对比</Link>
      </div>

      {(creating || !scopes.length) && (
        <Card title="划定建议范围" description="参数和边界来自已发布配方引用的数据字典，质量目标来自检测定义。">
          {!publishedSpecs.length && <Alert tone="warning" title="还没有已发布配方">先在工艺配置里发布数据字典和配方版本，再回到这里。</Alert>}
          <form className="grid gap-3 md:grid-cols-2" onSubmit={submitScope}>
            <Field label="范围代码"><Input required value={draft.code} onChange={event => updateDraft("code", event.target.value)} placeholder="lens-recipe" /></Field>
            <Field label="名称"><Input required value={draft.name} onChange={event => updateDraft("name", event.target.value)} /></Field>
            <Field label="工艺名称"><Input required value={draft.processName} onChange={event => updateDraft("processName", event.target.value)} /></Field>
            <Field label="站点"><Input required value={draft.siteCode} onChange={event => updateDraft("siteCode", event.target.value)} placeholder={completedRuns.find(item => item.siteId)?.siteId || "SITE-001"} /></Field>
            <Field label="产品系列"><Input value={draft.productFamilyCode} onChange={event => updateDraft("productFamilyCode", event.target.value)} /></Field>
            <Field label="产品"><Input value={draft.productCode} onChange={event => updateDraft("productCode", event.target.value)} /></Field>
            <Field label="设备"><Input value={draft.equipmentId} onChange={event => updateDraft("equipmentId", event.target.value)} /></Field>
            <Field label="已发布配方">
              <Select required value={draft.specificationKey} onChange={event => chooseSpecification(event.target.value)}>
                <option value="">选择配方版本</option>
                {publishedSpecs.map(item => <option key={`${item.processSpecificationId}:${item.version}`} value={`${item.processSpecificationId}:${item.version}`}>{item.processSpecificationId} · 第 {item.version} 版</option>)}
              </Select>
            </Field>
            <Field label="质量目标">
              <Select required value={draft.objectiveCode} onChange={event => updateDraft("objectiveCode", event.target.value)}>
                <option value="">选择检测项目</option>
                {characteristics.map(item => <option key={`${item.definitionName}:${item.code}`} value={item.code}>{item.name || item.code}{item.unit ? `（${item.unit}）` : ""}</option>)}
              </Select>
            </Field>
            <Field label="目标方向">
              <Select value={draft.objectiveDirection} onChange={event => updateDraft("objectiveDirection", event.target.value)}>
                <option value="minimize">越小越好</option>
                <option value="maximize">越大越好</option>
                <option value="target">逼近目标值</option>
              </Select>
            </Field>
            <Field label="目标值"><Input required type="number" step="any" value={draft.objectiveTarget} onChange={event => updateDraft("objectiveTarget", event.target.value)} /></Field>
            <Field label="向前查看天数"><Input required type="number" min="1" max="3650" value={draft.lookbackDays} onChange={event => updateDraft("lookbackDays", event.target.value)} /></Field>
            {parameterChoices.length > 0 && (
              <div className="md:col-span-2 grid gap-3">
                <p className="text-sm font-semibold text-slate-900">允许调整的参数</p>
                {parameterChoices.map(parameter => {
                  const checked = draft.selectedCodes.includes(parameter.code);
                  const bound = draft.bounds[parameter.code] || {};
                  return (
                    <label key={parameter.code} className="grid gap-2 rounded-lg border border-slate-200 p-3 md:grid-cols-[auto_1fr_8rem_8rem]">
                      <input type="checkbox" checked={checked} onChange={event => updateDraft("selectedCodes", event.target.checked ? [...draft.selectedCodes, parameter.code] : draft.selectedCodes.filter(code => code !== parameter.code))} />
                      <span className="text-sm text-slate-800">{parameter.displayName || parameter.code}<span className="ml-2 text-slate-500">{parameter.unit || "需要填写单位"}</span></span>
                      <Input aria-label={`${parameter.displayName || parameter.code}下界`} type="number" step="any" placeholder={parameter.minimum ?? "下界"} value={bound.lower ?? ""} onChange={event => updateDraft("bounds", { ...draft.bounds, [parameter.code]: { ...bound, lower: event.target.value, unit: parameter.unit || bound.unit } })} />
                      <Input aria-label={`${parameter.displayName || parameter.code}上界`} type="number" step="any" placeholder={parameter.maximum ?? "上界"} value={bound.upper ?? ""} onChange={event => updateDraft("bounds", { ...draft.bounds, [parameter.code]: { ...bound, upper: event.target.value, unit: parameter.unit || bound.unit } })} />
                    </label>
                  );
                })}
              </div>
            )}
            <div className="md:col-span-2">
              <Button variant="primary" type="submit" disabled={busy || !publishedSpecs.length}>{busy ? "正在保存…" : "启用这个范围"}</Button>
            </div>
          </form>
        </Card>
      )}

      {selected && (
        <Card title={selected.name} description={`${statusLabels[selected.status] || selected.status} · ${selected.processName}`} actions={selected.status === "active" && <Button variant="primary" disabled={busy} onClick={() => runAction(() => createSuggestion(selected.projectId), "已生成下一版配方建议。")}>{busy ? "正在生成…" : "生成建议"}</Button>}>
          {selected.status === "draft" && <Button variant="primary" disabled={busy} onClick={() => runAction(() => activateScope(selected.projectId, selected.revision), "建议范围已启用。")}>开始使用</Button>}
          {readiness && (
            <p className="text-sm leading-6 text-slate-600">
              范围内 {readiness.candidateRunCount ?? 0} 条运行，其中 {readiness.validObservationCount ?? 0} 条可以用于建议
              {readiness.excludedObservationCount ? `，${readiness.excludedObservationCount} 条因数据不完整被排除` : ""}。
              至少需要 3 条完整运行，并且实际配方不少于两种。
            </p>
          )}
          <RequestError error={specifications.error || models.error || definitions.error || executions.error} />
        </Card>
      )}

      {flows.map(flow => {
        const editor = decisionState(flow);
        const parameters = flow.item.parameters || [];
        const objectives = Object.entries(flow.item.prediction?.objectives || {});
        return (
          <Card key={flow.item.recommendationKey} title={stateLabels[flow.state] || flow.state} description={flow.item.prediction?.rationale || "等待工程师决定。"}>
            <dl className="grid gap-3 sm:grid-cols-2">
              {parameters.map(parameter => (
                <div key={parameter.variableCode}>
                  <dt className="text-[13px] text-slate-500">{parameter.variableCode}</dt>
                  <dd className="mt-1 text-sm font-medium text-slate-900">{formatMeasurementValue(parameter.value)} {parameter.unit}</dd>
                </div>
              ))}
            </dl>
            {objectives.length > 0 && (
              <p className="mt-3 text-sm text-slate-600">
                预测：{objectives.map(([code, metric]) => `${code} ${formatMeasurementValue(metric.mean)} ${metric.unit || ""}`).join("；")}
              </p>
            )}
            {flow.state === "pending-decision" && (
              <div className="mt-4 grid gap-3">
                <Field label="修改后的参数。采用时保持建议值。">
                  <div className="grid gap-2 md:grid-cols-2">
                    {parameters.map(parameter => (
                      <Input key={parameter.variableCode} aria-label={parameter.variableCode} type="number" step="any" value={editor.values[parameter.variableCode] ?? ""} onChange={event => updateDecision(flow, { values: { ...editor.values, [parameter.variableCode]: event.target.value } })} />
                    ))}
                  </div>
                </Field>
                <Field label="原因"><Textarea rows={2} value={editor.reason} onChange={event => updateDecision(flow, { reason: event.target.value })} placeholder="修改或拒绝时必填" /></Field>
                <div className="flex flex-wrap gap-2">
                  <Button variant="primary" disabled={busy} onClick={() => runAction(() => recordDecision(flow.recommendation.recommendationId, flow.item.recommendationKey, {
                    decision: "accepted",
                    engineerSelectedParameters: parameters,
                    usefulnessRating: "useful",
                  }), "已采用这版配方。")}>采用</Button>
                  <Button disabled={busy} onClick={() => runAction(() => recordDecision(flow.recommendation.recommendationId, flow.item.recommendationKey, {
                    decision: "modified",
                    reason: editor.reason,
                    engineerSelectedParameters: parameters.map(parameter => ({ ...parameter, value: Number(editor.values[parameter.variableCode]) })),
                    usefulnessRating: "partly-useful",
                  }), "已登记修改后的配方。")}>修改后采用</Button>
                  <Button disabled={busy} onClick={() => runAction(() => recordDecision(flow.recommendation.recommendationId, flow.item.recommendationKey, {
                    decision: "rejected",
                    reason: editor.reason,
                    engineerSelectedParameters: [],
                    usefulnessRating: "not-useful",
                  }), "已拒绝这版配方。")}>拒绝</Button>
                </div>
              </div>
            )}
            {flow.state === "pending-execution" && (
              <form className="mt-4 flex flex-wrap items-end gap-2" onSubmit={event => { event.preventDefault(); void runAction(() => linkRun(flow.decision.decisionId, editor.executionKey), "已关联后续运行。"); }}>
                <Field label="决定之后的真实运行">
                  <Select required value={editor.executionKey} onChange={event => updateDecision(flow, { executionKey: event.target.value })}>
                    <option value="">选择已完成运行</option>
                    {runOptions.map(run => <option key={run.executionId} value={run.executionId}>{run.executionId}</option>)}
                  </Select>
                </Field>
                <Button variant="primary" type="submit" disabled={busy}>关联运行</Button>
              </form>
            )}
            {flow.state === "pending-outcome" && (
              <Button className="mt-4" variant="primary" disabled={busy} onClick={() => runAction(() => freezeOutcome(flow.decision.decisionId), "已冻结这轮结果。")}>冻结结果</Button>
            )}
            {flow.decision?.outcome && (
              <Alert tone={flow.decision.outcome.validForOptimization ? "success" : "warning"} title="结果已从真实运行冻结">
                {flow.decision.outcome.exclusionReason || "这轮质量结果可以进入下一轮建议。"}
              </Alert>
            )}
          </Card>
        );
      })}
      {selected && !flows.length && !loading && <Alert tone="info" title="还没有建议">范围启用后，用已完成运行生成第一版配方。</Alert>}
    </Page>
  );
}
