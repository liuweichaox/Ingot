// 已发布配方版本上的下一轮校正。采用不产生新版本；显著变更交给修订草稿。
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { Link } from "react-router";
import { getJson } from "../api/http";
import { extractRows, useApi } from "../hooks/useApi";
import {
  briefForRecipe,
  chooseCharacteristic,
  dominant,
  inferObjective,
  proposalStillOpen,
  revisionOverrides,
  runsAfterDecision,
  runsForRecipe,
} from "./proposalScope";
import {
  createSuggestion,
  freezeOutcome,
  linkRun,
  listSuggestionFlows,
  readReadiness,
  recordDecision,
} from "./suggestionApi";
import { Alert, Button, Field, Input, RequestError, Textarea, notify } from "../ui/components";
import { formatMeasurementValue, formatTime } from "../pages/shared";
import { OutcomeReceipt, RecommendationEvidence } from "./RecommendationEvidence";

const stateLabels = {
  "pending-decision": "待决定",
  rejected: "已拒绝",
  "pending-execution": "待关联实际运行",
  "pending-outcome": "待读取结果",
  "outcome-frozen": "结果已冻结",
  "outcome-excluded": "结果未纳入本轮验证",
  stale: "已过期，需重新生成",
};

const directionLabels = {
  minimize: "越小越好",
  maximize: "越大越好",
  target: "逼近目标值",
};

function matchesVersion(flow, specification) {
  return flow.recommendation?.processSpecificationId === specification.processSpecificationId &&
    String(flow.recommendation?.brief?.context?.process_specification_version || "") === String(specification.version);
}

// 仅在运行标识和检查时的站点范围都有效时生成详情深链。
function readinessExecutionHref(brief, executionKey) {
  const normalizedExecutionKey = typeof executionKey === "string" ? executionKey.trim() : "";
  // The brief field is named siteCode, but this UI fills it with the registered site ID expected by the run route.
  const siteId = typeof brief?.siteCode === "string" ? brief.siteCode.trim() : "";
  if (!normalizedExecutionKey || normalizedExecutionKey.length > 200 || !siteId) return "";
  return `/process-executions/${encodeURIComponent(normalizedExecutionKey)}?siteId=${encodeURIComponent(siteId)}`;
}

// 组件只展示本次准入范围、计数和运行级排除证据，不把准入结果解释为因果结论。
function RecipeReadinessPanel({ readiness }) {
  const result = readiness.result || {};
  const brief = readiness.brief || {};
  const candidateCount = Number(result.candidateRunCount) || 0;
  const validCount = Number(result.validObservationCount) || 0;
  const excludedCount = Number(result.excludedObservationCount) || 0;
  const excludedObservations = result.excludedObservations || [];
  const unobservedCount = Math.max(0, candidateCount - validCount - excludedCount);
  const siteId = typeof brief.siteCode === "string" ? brief.siteCode.trim() : "";
  const processVersion = brief.context?.process_specification_version || "未提供";
  const lookbackDays = brief.context?.lookback_days || "未提供";

  return (
    <section className="grid gap-2 text-sm leading-6 text-slate-600" aria-label="校正数据准入检查结果">
      <p>
        本次检查范围：工艺规范 {brief.processSpecificationId || "未提供"} / 第 {processVersion} 版 · 站点 {siteId || "未提供"} · 回看近 {lookbackDays} 天。
      </p>
      <p>
        候选 {candidateCount} 条；已组装并通过本次准入 {validCount} 条；已组装但未通过 {excludedCount} 条。
        {unobservedCount > 0 && `另有 ${unobservedCount} 条候选未形成可展示观察，未计为已核验的排除项。`}
      </p>
      <p>生成时会重新检查门槛：至少 3 条参数和质量结果完整的运行，且实际参数至少有 2 种组合。</p>
      {result.truncated && <p role="status">候选运行超过 2,000 条上限，本次检查结果范围不完整；请缩小范围后重新检查。</p>}
      {excludedObservations.length > 0 && (
        <ul className="grid gap-1" aria-label="本次准入排除的运行">
          {excludedObservations.map((item, index) => {
            const executionKey = typeof item.executionKey === "string" ? item.executionKey.trim() : "";
            const executionHref = readinessExecutionHref(brief, executionKey);
            return (
              <li key={`${executionKey || "excluded"}:${index}`}>
                {executionHref ? (
                  <Link className="font-medium text-trajectory-700 underline decoration-trajectory-300 underline-offset-2 hover:text-trajectory-900" to={executionHref}>
                    {executionKey}
                  </Link>
                ) : executionKey || "未知运行"}
                ：{item.reason}
              </li>
            );
          })}
        </ul>
      )}
      {excludedCount > excludedObservations.length && (
        <p>
          {excludedObservations.length > 0 ? `排除原因明细仅显示前 ${excludedObservations.length} 条` : "本次未返回排除原因明细"}，共 {excludedCount} 条。
        </p>
      )}
    </section>
  );
}

// 组件仅展示当前配方的建议流程，并将就绪检查结果绑定到检查时冻结的简报范围。
export function RecipeVersionProposal({ specification, siteId = "", canWrite = true, onPromote }) {
  const scopeKey = JSON.stringify([siteId, specification.processSpecificationId, String(specification.version)]);
  const currentScope = useRef(scopeKey);
  useLayoutEffect(() => { currentScope.current = scopeKey; }, [scopeKey]);
  const models = useApi("/api/v1/process-data-models");
  const definitions = useApi("/api/v1/inspection-definitions");
  const qualityPlans = useApi("/api/v1/inspection-plans");
  const analysisPlans = useApi("/api/v1/process-analysis-plans");
  const [flows, setFlows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [errorState, setErrorState] = useState({ scopeKey: "", message: "" });
  const [readiness, setReadiness] = useState(null);
  const [busyScope, setBusyScope] = useState("");
  const [runsBySite, setRunsBySite] = useState({});
  const [runsReadySite, setRunsReadySite] = useState("");
  const [decisionDrafts, setDecisionDrafts] = useState({});
  const [runIds, setRunIds] = useState({});
  const [runConfirmation, setRunConfirmation] = useState(null);
  const flowsRequestId = useRef(0);
  const error = errorState.scopeKey === scopeKey ? errorState.message : "";
  const busy = busyScope === scopeKey;
  const visibleReadiness = readiness?.scopeKey === scopeKey ? readiness : null;
  function setScopedError(targetScope, message) {
    if (currentScope.current === targetScope) setErrorState({ scopeKey: targetScope, message });
  }
  function setScopedBusy(targetScope, isBusy) {
    setBusyScope(current => isBusy ? targetScope : current === targetScope ? "" : current);
  }
  const scopedFlows = useMemo(
    () => siteId ? flows.filter(flow =>
      flow.recommendation?.siteCode === siteId && matchesVersion(flow, specification)) : [],
    [flows, siteId, specification.processSpecificationId, specification.version],
  );
  const completedRuns = siteId ? runsBySite[siteId] || [] : [];
  const recipeRuns = runsForRecipe(completedRuns, specification);
  const siteCode = siteId;
  const runsReady = Boolean(siteId && runsReadySite === siteId);

  async function reloadFlows(targetScope = scopeKey) {
    if (currentScope.current !== targetScope) return;
    const requestId = ++flowsRequestId.current;
    if (!siteId) {
      setFlows([]);
      setLoading(false);
      return;
    }
    setLoading(true);
    setFlows([]);
    setScopedError(targetScope, "");
    try {
      const page = await listSuggestionFlows({
        siteId,
        processSpecificationId: specification.processSpecificationId,
      });
      if (currentScope.current === targetScope && requestId === flowsRequestId.current) {
        setFlows((page.items || []).filter(flow =>
          matchesVersion(flow, specification) && flow.recommendation?.siteCode === siteId));
        setScopedError(targetScope, "");
      }
    } catch (requestError) {
      if (currentScope.current === targetScope && requestId === flowsRequestId.current) setScopedError(targetScope, requestError.message);
    } finally {
      if (currentScope.current === targetScope && requestId === flowsRequestId.current) setLoading(false);
    }
  }

  useEffect(() => {
    setReadiness(current => current?.scopeKey === scopeKey ? current : null);
    void reloadFlows();
    return () => { flowsRequestId.current += 1; };
  }, [scopeKey]);

  useEffect(() => {
    if (!siteId) {
      setRunsReadySite("");
      setRunsBySite({});
      return undefined;
    }
    let cancelled = false;
    setRunsReadySite("");
    setScopedError(scopeKey, "");
    async function load() {
      try {
        const page = await getJson(`/api/v1/process-executions?status=completed&siteId=${encodeURIComponent(siteId)}&limit=100`);
        if (!cancelled) {
          setRunsBySite(current => ({ ...current, [siteId]: extractRows(page) }));
          setRunsReadySite(siteId);
        }
      } catch (requestError) {
        if (!cancelled) {
          setScopedError(scopeKey, requestError.message);
          setRunsReadySite("");
        }
      }
    }
    void load();
    const timer = setInterval(() => { void load(); }, 15000);
    return () => {
      cancelled = true;
      clearInterval(timer);
    };
  }, [siteId, scopeKey]);

  const model = extractRows(models.data).find(item =>
    item.modelId === specification.dataModelId && Number(item.version) === Number(specification.dataModelVersion));
  const characteristic = chooseCharacteristic(extractRows(qualityPlans.data), extractRows(definitions.data), specification);
  const objective = characteristic ? inferObjective(characteristic) : null;
  const openProposal = scopedFlows.find(proposalStillOpen) || null;
  const hasAnalysisPlan = extractRows(analysisPlans.data).some(plan =>
    plan.status === "published" &&
    plan.dataModelId === specification.dataModelId &&
    (plan.signals || []).length > 0);

  function currentBrief() {
    return briefForRecipe({
      specification,
      model,
      runs: completedRuns,
      characteristic,
      siteCode,
    });
  }

  async function runAction(action, success) {
    const actionScope = scopeKey;
    setScopedBusy(actionScope, true);
    setScopedError(actionScope, "");
    try {
      await action();
      if (currentScope.current !== actionScope) return;
      if (success) notify(success);
      await reloadFlows(actionScope);
    } catch (requestError) {
      setScopedError(actionScope, requestError.message);
    } finally {
      setScopedBusy(actionScope, false);
    }
  }

  function attachRun(flow, run) {
    return runAction(
      () => linkRun(flow.decision.decisionId, run.executionId),
      `已关联 ${run.executionId}。系统将在结果冻结时核对实际参数。`,
    );
  }

  // 关联不可覆盖，先冻结用户选择的运行号和已知信息供确认。
  function reviewRun(flow, run) {
    setRunConfirmation({ scopeKey, decisionId: flow.decision.decisionId, run });
  }

  function readOutcome(flow) {
    return runAction(
      () => freezeOutcome(flow.decision.decisionId),
      "已读取并冻结这轮结果。",
    );
  }

  async function checkReadiness() {
    const checkScope = scopeKey;
    setScopedBusy(checkScope, true);
    setScopedError(checkScope, "");
    setReadiness(null);
    try {
      const brief = currentBrief();
      const result = await readReadiness(brief);
      if (currentScope.current === checkScope) setReadiness({ scopeKey: checkScope, brief, result });
    } catch (requestError) {
      setScopedError(checkScope, requestError.message);
    } finally {
      setScopedBusy(checkScope, false);
    }
  }

  function submitSuggestion(event) {
    event.preventDefault();
    let brief;
    try {
      brief = currentBrief();
    } catch (validationError) {
      setScopedError(scopeKey, validationError.message);
      return;
    }
    void runAction(() => createSuggestion(brief), "已生成这一版的下一轮校正。");
  }

  function decisionState(flow) {
    const draftKey = JSON.stringify([scopeKey, flow.item.recommendationKey]);
    return decisionDrafts[draftKey] || {
      reason: "",
      values: Object.fromEntries((flow.item.parameters || []).map(item => [item.variableCode, String(item.value)])),
    };
  }

  function updateDecision(flow, patch) {
    const key = JSON.stringify([scopeKey, flow.item.recommendationKey]);
    setDecisionDrafts(current => ({ ...current, [key]: { ...decisionState(flow), ...patch } }));
  }

  function promote(flow) {
    const editor = decisionState(flow);
    const parameters = (flow.item.parameters || []).map(parameter => ({
      variableCode: parameter.variableCode,
      value: editor.values[parameter.variableCode] ?? parameter.value,
      dataType: "double",
    }));
    onPromote?.({
      changeReason: editor.reason.trim() || flow.item.prediction?.rationale || "把这一轮校正转为新的配方版本。",
      parameterOverrides: revisionOverrides(specification, parameters),
      evidenceReferences: recipeRuns
        .filter(run => ["COMPLETE", "FAILED"].includes(String(run.qualityStatus || "").toUpperCase()))
        .map(run => ({ kind: "process-execution", referenceId: run.executionId })),
    });
  }

  const scopeText = [
    siteCode && `站点 ${siteCode}`,
    recipeRuns.length ? `${recipeRuns.length} 条已完成运行` : "还没有这一版的已完成运行",
    dominant(recipeRuns, "productFamilyCode") && `产品 ${[dominant(recipeRuns, "productFamilyCode"), dominant(recipeRuns, "productCode")].filter(Boolean).join(" / ")}`,
    dominant(recipeRuns, "equipmentId") && `设备 ${dominant(recipeRuns, "equipmentId")}`,
    objective && `质量目标 ${characteristic.name || characteristic.code}，${directionLabels[objective.direction] || objective.direction}，目标 ${objective.target}`,
  ].filter(Boolean).join(" · ");

  return (
    <div className="grid gap-4">
      {error && <Alert tone="danger">{error}</Alert>}
      {!siteId && <Alert tone="warning" title="先选择一个现场">校正检查按单个现场读取已完成运行和质量证据。请关闭此面板并在配方页选择现场。</Alert>}
      {!hasAnalysisPlan && <Alert tone="warning" title="还没有覆盖这个配方的分析规则">没有已发布且包含过程曲线的分析规则时，运行进不了校正。</Alert>}
      {!characteristic && <Alert tone="warning" title="还没有覆盖这个配方的质量方案">已发布质量方案里需要有带判定范围的数值指标。</Alert>}
      <p className="text-sm leading-6 text-slate-600">{scopeText}</p>
      <p className="text-sm leading-6 text-slate-600">采用为下一轮校正不会发布新版本，下一轮仍使用这一版配方。要改配方结构或质量目标时，用「作为显著变更」打开修订草稿。</p>
      {openProposal && <Alert tone="info" title="这一版已有未完成的校正">先完成决定，或等下一轮运行把结果冻结回来，再生成新的校正。</Alert>}
      {visibleReadiness && <RecipeReadinessPanel readiness={visibleReadiness} />}
      {canWrite && siteId && (
        <form className="flex flex-wrap gap-2" onSubmit={submitSuggestion}>
          <Button type="button" disabled={busy || Boolean(openProposal) || !runsReady} onClick={checkReadiness}>检查数据</Button>
          <Button variant="primary" type="submit" disabled={busy || Boolean(openProposal) || !runsReady}>{busy ? "正在处理…" : "生成下一轮校正"}</Button>
        </form>
      )}
      <RequestError error={models.error || definitions.error || qualityPlans.error || analysisPlans.error} />
      {loading && !scopedFlows.length && siteId && <p className="text-sm text-slate-500">正在读取这一现场的校正…</p>}
      {scopedFlows.map(flow => {
        const editor = decisionState(flow);
        const parameters = flow.item.parameters || [];
        const recommendation = flow.recommendation;
        const decisionAt = flow.decision?.decidedAt;
        const version = recommendation?.brief?.context?.process_specification_version;
        const candidateRuns = flow.state === "pending-execution"
          ? runsAfterDecision(
            recipeRuns,
            recommendation.processSpecificationId,
            version,
            decisionAt,
          )
          : [];
        const confirmation = runConfirmation?.scopeKey === scopeKey &&
          runConfirmation.decisionId === flow.decision?.decisionId ? runConfirmation : null;
        return (
          <section key={`${recommendation.recommendationId}:${flow.item.recommendationKey}`} className="grid gap-3 border-t border-slate-200 pt-4" aria-label={stateLabels[flow.state] || flow.state}>
            <div>
              <h3 className="text-sm font-semibold text-slate-950">{stateLabels[flow.state] || flow.state}</h3>
              {flow.item.prediction?.rationale && <p className="mt-1 text-sm text-slate-600">{flow.item.prediction.rationale}</p>}
            </div>
            <dl className="grid gap-3 sm:grid-cols-2">
              {parameters.map(parameter => (
                <div key={parameter.variableCode}>
                  <dt className="text-[13px] text-slate-500">{parameter.variableCode}</dt>
                  <dd className="mt-1 text-sm font-medium text-slate-900">{formatMeasurementValue(parameter.value)} {parameter.unit}</dd>
                </div>
              ))}
            </dl>
            <RecommendationEvidence recommendation={recommendation} prediction={flow.item.prediction} />
            {canWrite && flow.state === "pending-decision" && (
              <div className="grid gap-3">
                <Field label="修改后的参数。作为下一轮校正采用时保持建议值。">
                  <div className="grid gap-2 md:grid-cols-2">
                    {parameters.map(parameter => (
                      <Input key={parameter.variableCode} aria-label={parameter.variableCode} type="number" step="any" value={editor.values[parameter.variableCode] ?? ""} onChange={event => updateDecision(flow, { values: { ...editor.values, [parameter.variableCode]: event.target.value } })} />
                    ))}
                  </div>
                </Field>
                <Field label="原因"><Textarea rows={2} value={editor.reason} onChange={event => updateDecision(flow, { reason: event.target.value })} placeholder="修改、拒绝或转为显著变更时填写" /></Field>
                <div className="flex flex-wrap gap-2">
                  <Button variant="primary" disabled={busy} onClick={() => runAction(() => recordDecision(recommendation.recommendationId, flow.item.recommendationKey, {
                    decision: "accepted",
                    engineerSelectedParameters: parameters,
                  }), "已采用为下一轮校正。已发布版本不变。")}>采用为下一轮校正</Button>
                  <Button disabled={busy} onClick={() => runAction(() => recordDecision(recommendation.recommendationId, flow.item.recommendationKey, {
                    decision: "modified",
                    reason: editor.reason,
                    engineerSelectedParameters: parameters.map(parameter => ({ ...parameter, value: Number(editor.values[parameter.variableCode]) })),
                  }), "已登记修改后的下一轮校正。已发布版本不变。")}>修改后作为下一轮校正</Button>
                  <Button disabled={busy} onClick={() => promote(flow)}>作为显著变更</Button>
                  <Button disabled={busy} onClick={() => runAction(() => recordDecision(recommendation.recommendationId, flow.item.recommendationKey, {
                    decision: "rejected",
                    reason: editor.reason,
                    engineerSelectedParameters: [],
                  }), "已拒绝这轮校正。")}>拒绝</Button>
                </div>
              </div>
            )}
            {flow.state === "pending-execution" && (
              <div className="grid gap-2 rounded-lg border border-slate-200 p-3">
                <p className="text-sm leading-6 text-slate-600">
                  选择实际执行了这轮参数的运行。系统会在结果冻结时核对实际参数；未按决定执行的结果会保留，但不作为本轮验证；运行可能按实际参数进入后续观察。
                </p>
                {!canWrite && <p className="text-[13px] text-slate-500">等待有权限的工程师关联实际运行。</p>}
                {canWrite && candidateRuns.length === 0 && (
                  <p className="text-[13px] text-slate-500">最近读取的运行中没有符合条件的候选；也可以输入运行号关联。</p>
                )}
                {canWrite && (
                  <div className="flex flex-wrap items-end gap-2">
                    <Field label="输入运行号">
                      <Input aria-label={`输入运行号 ${flow.item.recommendationKey}`} value={runIds[flow.decision.decisionId] || ""} onChange={event => setRunIds(current => ({ ...current, [flow.decision.decisionId]: event.target.value }))} />
                    </Field>
                    <Button disabled={busy || !runIds[flow.decision.decisionId]?.trim()} onClick={() => {
                      const executionId = runIds[flow.decision.decisionId].trim();
                      reviewRun(flow, candidateRuns.find(run => run.executionId === executionId) || { executionId });
                    }}>核对输入的运行</Button>
                  </div>
                )}
                {canWrite && candidateRuns.length > 0 && (
                  <ul className="grid gap-2">
                    {candidateRuns.map(run => (
                      <li key={run.executionId} className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-slate-100 px-3 py-2">
                        <div>
                          <p className="text-sm font-medium text-slate-900">{run.executionId}</p>
                          <p className="text-[13px] text-slate-500">{formatTime(run.startedAt)} · {run.qualityStatus || "质量状态未知"} · 设备 {run.equipmentId || "未知"} · 产品 {run.productCode || "未知"}</p>
                        </div>
                        <Button variant="primary" disabled={busy} onClick={() => reviewRun(flow, run)}>核对此运行</Button>
                      </li>
                    ))}
                  </ul>
                )}
                {confirmation && (
                  <div className="grid gap-2 rounded-md border border-amber-300 bg-amber-50 p-3 text-sm" aria-label="确认关联运行">
                    <p className="font-medium">确认运行 {confirmation.run.executionId}</p>
                    <p>决定范围：站点 {siteId} · 配方 {recommendation.processSpecificationId} / 第 {version || "未知"} 版</p>
                    {confirmation.run.startedAt ? (
                      <p>开始 {formatTime(confirmation.run.startedAt)} · 设备 {confirmation.run.equipmentId || "未知"} · 产品 {confirmation.run.productCode || "未知"} · 质量 {confirmation.run.qualityStatus || "未知"}</p>
                    ) : <p>最近运行列表没有该运行的详细信息，请先核实运行号和执行范围。</p>}
                    {(flow.decision.engineerSelectedParameters || []).length > 0 && (
                      <p>工程师决定：{flow.decision.engineerSelectedParameters.map(item => `${item.variableCode} ${formatMeasurementValue(item.value)} ${item.unit || ""}`).join("；")}</p>
                    )}
                    <Link className="text-trajectory-700 underline" to={`/process-executions/${encodeURIComponent(confirmation.run.executionId)}?siteId=${encodeURIComponent(siteId)}`}>查看运行详情</Link>
                    <p>关联后不能更换运行；实际参数将在冻结结果时核对。请确认这是执行该决定的运行。</p>
                    <div className="flex flex-wrap gap-2">
                      <Button variant="primary" disabled={busy} onClick={() => { void attachRun(flow, confirmation.run); }}>确认关联</Button>
                      <Button disabled={busy} onClick={() => setRunConfirmation(null)}>取消</Button>
                    </div>
                  </div>
                )}
              </div>
            )}
            {flow.state === "pending-outcome" && (
              <div className="flex flex-wrap items-center gap-2">
                <p className="text-sm leading-6 text-slate-600">
                  运行已关联。读取质量结果后冻结；若实际参数与决定不一致，结果不作为本轮验证；运行可能按实际参数进入后续观察。
                </p>
                {canWrite && <Button variant="primary" disabled={busy} onClick={() => readOutcome(flow)}>读取并冻结结果</Button>}
              </div>
            )}
            {flow.decision?.outcome && <OutcomeReceipt decision={flow.decision} siteId={siteId} />}
          </section>
        );
      })}
    </div>
  );
}
