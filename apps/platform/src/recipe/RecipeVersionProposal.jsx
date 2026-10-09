// 已发布配方版本上的下一轮校正。采用不产生新版本；显著变更交给修订草稿。
import { useEffect, useRef, useState } from "react";
import { getJson } from "../api/http";
import { extractRows, registeredSiteIds, useApi } from "../hooks/useApi";
import {
  briefForRecipe,
  chooseCharacteristic,
  dominant,
  inferObjective,
  nextRunAfterDecision,
  proposalStillOpen,
  revisionOverrides,
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
import { formatMeasurementValue } from "../pages/shared";

const stateLabels = {
  "pending-decision": "待决定",
  rejected: "已拒绝",
  "pending-execution": "等待下一轮运行",
  "pending-outcome": "等待质量结果",
  "outcome-frozen": "结果已冻结",
  "outcome-excluded": "结果未纳入后续建议",
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

export function RecipeVersionProposal({ specification, canWrite = true, onPromote }) {
  const models = useApi("/api/v1/process-data-models");
  const definitions = useApi("/api/v1/inspection-definitions");
  const qualityPlans = useApi("/api/v1/inspection-plans");
  const analysisPlans = useApi("/api/v1/process-analysis-plans");
  const edges = useApi("/api/edges");
  const [flows, setFlows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [readiness, setReadiness] = useState(null);
  const [busy, setBusy] = useState(false);
  const [runsBySite, setRunsBySite] = useState({});
  const [runsReady, setRunsReady] = useState(false);
  const [decisionDrafts, setDecisionDrafts] = useState({});
  const attached = useRef(new Set());

  const siteOptions = [...new Set([
    ...registeredSiteIds(edges.data),
    ...flows.map(flow => flow.recommendation?.siteCode).filter(Boolean),
  ])];
  const siteKey = siteOptions.join("|");

  async function reloadFlows() {
    setLoading(true);
    try {
      const page = await listSuggestionFlows({
        processSpecificationId: specification.processSpecificationId,
      });
      setFlows((page.items || []).filter(flow => matchesVersion(flow, specification)));
      setError("");
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void reloadFlows();
  }, [specification.processSpecificationId, specification.version]);

  useEffect(() => {
    if (!siteKey) {
      setRunsReady(true);
      return undefined;
    }
    let cancelled = false;
    async function load() {
      await Promise.all(siteKey.split("|").map(async site => {
        try {
          const page = await getJson(`/api/v1/process-executions?status=completed&siteId=${encodeURIComponent(site)}&limit=100`);
          if (!cancelled) setRunsBySite(current => ({ ...current, [site]: extractRows(page) }));
        } catch (requestError) {
          if (!cancelled) setError(requestError.message);
        }
      }));
      if (!cancelled) setRunsReady(true);
    }
    void load();
    const timer = setInterval(() => { void load(); }, 15000);
    return () => {
      cancelled = true;
      clearInterval(timer);
    };
  }, [siteKey]);

  const completedRuns = Object.values(runsBySite).flat();
  const recipeRuns = runsForRecipe(completedRuns, specification);
  const siteCode = dominant(recipeRuns, "siteId") || siteOptions[0] || "";
  const model = extractRows(models.data).find(item =>
    item.modelId === specification.dataModelId && Number(item.version) === Number(specification.dataModelVersion));
  const characteristic = chooseCharacteristic(extractRows(qualityPlans.data), extractRows(definitions.data), specification);
  const objective = characteristic ? inferObjective(characteristic) : null;
  const openProposal = flows.find(proposalStillOpen) || null;
  const hasAnalysisPlan = extractRows(analysisPlans.data).some(plan =>
    plan.status === "published" &&
    plan.dataModelId === specification.dataModelId &&
    (plan.signals || []).length > 0);

  useEffect(() => {
    if (!runsReady) return undefined;
    const waiting = flows.filter(flow => flow.state === "pending-execution" || flow.state === "pending-outcome");
    if (!waiting.length) return undefined;
    let cancelled = false;
    async function settle() {
      for (const flow of waiting) {
        if (cancelled) return;
        const recommendation = flow.recommendation;
        const version = recommendation?.brief?.context?.process_specification_version;
        const runs = runsBySite[recommendation?.siteCode] || [];
        try {
          if (flow.state === "pending-execution") {
            const run = nextRunAfterDecision(runs, recommendation.processSpecificationId, version, flow.decision?.decidedAt);
            if (!run) continue;
            const key = `link:${flow.decision.decisionId}:${run.executionId}`;
            if (attached.current.has(key)) continue;
            attached.current.add(key);
            await linkRun(flow.decision.decisionId, run.executionId);
            if (!cancelled) notify(`已把 ${run.executionId} 接到这一版的校正上。`);
          } else {
            const key = `freeze:${flow.decision.decisionId}`;
            if (attached.current.has(key)) continue;
            attached.current.add(key);
            await freezeOutcome(flow.decision.decisionId);
            if (!cancelled) notify("这轮质量结果已冻结。");
          }
          if (!cancelled) await reloadFlows();
        } catch (requestError) {
          if (!cancelled) setError(requestError.message);
        }
      }
    }
    void settle();
    return () => {
      cancelled = true;
    };
  }, [flows, runsBySite, runsReady]);

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
    setBusy(true);
    setError("");
    try {
      await action();
      if (success) notify(success);
      await reloadFlows();
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setBusy(false);
    }
  }

  async function checkReadiness() {
    setBusy(true);
    setError("");
    try {
      setReadiness(await readReadiness(currentBrief()));
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setBusy(false);
    }
  }

  function submitSuggestion(event) {
    event.preventDefault();
    let brief;
    try {
      brief = currentBrief();
    } catch (validationError) {
      setError(validationError.message);
      return;
    }
    void runAction(() => createSuggestion(brief), "已生成这一版的下一轮校正。");
  }

  function decisionState(flow) {
    return decisionDrafts[flow.item.recommendationKey] || {
      reason: "",
      values: Object.fromEntries((flow.item.parameters || []).map(item => [item.variableCode, String(item.value)])),
    };
  }

  function updateDecision(flow, patch) {
    const key = flow.item.recommendationKey;
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
      {!hasAnalysisPlan && <Alert tone="warning" title="还没有覆盖这个配方的过程分析">没有已发布且包含过程曲线的过程分析时，运行进不了校正。</Alert>}
      {!characteristic && <Alert tone="warning" title="还没有覆盖这个配方的质量方案">已发布质量方案里需要有带判定范围的数值指标。</Alert>}
      <p className="text-sm leading-6 text-slate-600">{scopeText}</p>
      <p className="text-sm leading-6 text-slate-600">采用为下一轮校正不会发布新版本，下一轮仍使用这一版配方。要改配方结构或质量目标时，用「作为显著变更」打开修订草稿。</p>
      {openProposal && <Alert tone="info" title="这一版已有未完成的校正">先完成决定，或等下一轮运行把结果冻结回来，再生成新的校正。</Alert>}
      {readiness && (
        <div className="text-sm leading-6 text-slate-600">
          <p>
            条件内 {readiness.candidateRunCount ?? 0} 条运行，其中 {readiness.validObservationCount ?? 0} 条可以用于校正
            {readiness.excludedObservationCount ? `，${readiness.excludedObservationCount} 条因数据不完整被排除` : ""}。
            至少需要 3 条完整运行，并且实际配方不少于两种。
          </p>
          {(readiness.excludedObservations || []).map(item => (
            <p key={item.executionKey}>{item.executionKey}：{item.reason}</p>
          ))}
        </div>
      )}
      {canWrite && (
        <form className="flex flex-wrap gap-2" onSubmit={submitSuggestion}>
          <Button type="button" disabled={busy || Boolean(openProposal) || !runsReady} onClick={checkReadiness}>检查数据</Button>
          <Button variant="primary" type="submit" disabled={busy || Boolean(openProposal) || !runsReady}>{busy ? "正在处理…" : "生成下一轮校正"}</Button>
        </form>
      )}
      <RequestError error={models.error || definitions.error || qualityPlans.error || edges.error} />
      {loading && !flows.length && <p className="text-sm text-slate-500">正在读取这一版的校正…</p>}
      {flows.map(flow => {
        const editor = decisionState(flow);
        const parameters = flow.item.parameters || [];
        const objectives = Object.entries(flow.item.prediction?.objectives || {});
        const recommendation = flow.recommendation;
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
            {objectives.length > 0 && (
              <p className="text-sm text-slate-600">
                预测：{objectives.map(([code, metric]) => `${code} ${formatMeasurementValue(metric.mean)} ${metric.unit || ""}`).join("；")}
              </p>
            )}
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
                    usefulnessRating: "useful",
                  }), "已采用为下一轮校正。已发布版本不变。")}>采用为下一轮校正</Button>
                  <Button disabled={busy} onClick={() => runAction(() => recordDecision(recommendation.recommendationId, flow.item.recommendationKey, {
                    decision: "modified",
                    reason: editor.reason,
                    engineerSelectedParameters: parameters.map(parameter => ({ ...parameter, value: Number(editor.values[parameter.variableCode]) })),
                    usefulnessRating: "partly-useful",
                  }), "已登记修改后的下一轮校正。已发布版本不变。")}>修改后作为下一轮校正</Button>
                  <Button disabled={busy} onClick={() => promote(flow)}>作为显著变更</Button>
                  <Button disabled={busy} onClick={() => runAction(() => recordDecision(recommendation.recommendationId, flow.item.recommendationKey, {
                    decision: "rejected",
                    reason: editor.reason,
                    engineerSelectedParameters: [],
                    usefulnessRating: "not-useful",
                  }), "已拒绝这轮校正。")}>拒绝</Button>
                </div>
              </div>
            )}
            {flow.state === "pending-execution" && (
              <p className="text-sm text-slate-600">已发布版本不变。决定之后，下一轮使用这一版配方并完成的运行会自动接上。</p>
            )}
            {canWrite && flow.state === "pending-outcome" && (
              <div className="flex flex-wrap items-center gap-2">
                <p className="text-sm text-slate-600">正在读取这轮运行的质量结果。</p>
                <Button variant="primary" disabled={busy} onClick={() => {
                  attached.current.delete(`freeze:${flow.decision.decisionId}`);
                  void runAction(() => freezeOutcome(flow.decision.decisionId), "已冻结这轮结果。");
                }}>再次读取结果</Button>
              </div>
            )}
            {flow.decision?.outcome && (
              <Alert tone={flow.decision.outcome.validForOptimization ? "success" : "warning"} title="结果已从真实运行冻结">
                {flow.decision.outcome.exclusionReason || "这轮质量结果可以进入下一轮校正。"}
              </Alert>
            )}
          </section>
        );
      })}
    </div>
  );
}
