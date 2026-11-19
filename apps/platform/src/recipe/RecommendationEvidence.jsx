// 展示下一配方建议的预测证据和冻结结果回执；只读，不改变任何业务状态。
import { Link } from "react-router";
import { Alert, Badge } from "../ui/components";
import { formatMeasurementValue, formatTime } from "../pages/shared";

const adherenceLabels = {
  "within-tolerance": { label: "按决定执行", tone: "success" },
  "outside-tolerance": { label: "未按决定执行", tone: "danger" },
  unverifiable: { label: "无法核对执行参数", tone: "warning" },
};

function intervalText(metric) {
  const lower = Number(metric?.lower95);
  const upper = Number(metric?.upper95);
  if (!Number.isFinite(lower) || !Number.isFinite(upper)) return "—";
  return `[${formatMeasurementValue(lower)}, ${formatMeasurementValue(upper)}]`;
}

function PredictionRow({ code, metric }) {
  return (
    <div className="flex flex-wrap items-baseline justify-between gap-2 border-t border-slate-200 py-1.5 first:border-t-0">
      <span className="text-[13px] font-medium text-slate-700">{code}</span>
      <span className="text-[13px] text-slate-600">
        预测 {formatMeasurementValue(metric?.mean)} {metric?.unit || ""}
        <span className="ml-2 text-slate-500">95% 区间 {intervalText(metric)}</span>
      </span>
    </div>
  );
}

export function RecommendationEvidence({ recommendation, prediction }) {
  if (!prediction) return null;
  const objectives = Object.entries(prediction.objectives || {});
  const constraints = Object.entries(prediction.constraints || {});
  const feasibility = prediction.feasibilityProbability == null ? NaN : Number(prediction.feasibilityProbability);
  const observationCount = Number(recommendation?.observationCount);

  return (
    <section className="grid gap-2 rounded-lg border border-slate-200 bg-slate-50/70 p-3" aria-label="建议预测证据">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-[13px] text-slate-600">
        <span>模型 {recommendation?.modelVersion || "未记录"}</span>
        <span>观察 {Number.isFinite(observationCount) ? observationCount : "—"} 条</span>
        {prediction.coldStart && <Badge tone="warning">冷启动</Badge>}
        {Number.isFinite(feasibility) && <span>可行概率 {Math.round(feasibility * 100)}%</span>}
      </div>
      {objectives.length > 0 && (
        <div aria-label="目标预测">
          {objectives.map(([code, metric]) => <PredictionRow key={code} code={code} metric={metric} />)}
        </div>
      )}
      {constraints.length > 0 && (
        <div aria-label="结果约束预测">
          {constraints.map(([code, metric]) => <PredictionRow key={code} code={code} metric={metric} />)}
        </div>
      )}
      <p className="text-xs leading-5 text-slate-500">
        区间是模型预测，不是质量保证；未估计测量误差时按固定噪声下限计算。
      </p>
    </section>
  );
}

export function OutcomeReceipt({ decision, siteId }) {
  const outcome = decision?.outcome;
  const prediction = decision?.prediction;
  if (!outcome) return null;

  const adherence = outcome.executionAdherence;
  const adherenceLabel = adherenceLabels[adherence?.status] || null;
  const runHref = outcome.actualExecutionKey && siteId
    ? `/process-executions/${encodeURIComponent(outcome.actualExecutionKey)}?siteId=${encodeURIComponent(siteId)}`
    : "";
  const objectives = Object.entries(prediction?.objectives || {});
  const actuals = outcome.outcomes || {};
  const comparable = decision?.decision === "accepted" && adherence?.status === "within-tolerance";

  return (
    <section className="grid gap-3 rounded-lg border border-slate-200 p-3" aria-label="冻结结果回执">
      <div className="flex flex-wrap items-center gap-2">
        {adherenceLabel
          ? <Badge tone={adherenceLabel.tone}>{adherenceLabel.label}</Badge>
          : <Badge tone="neutral">未记录执行核对</Badge>}
        {runHref && (
          <Link
            className="text-sm font-medium text-trajectory-700 underline decoration-trajectory-300 underline-offset-2 hover:text-trajectory-900"
            to={runHref}
          >
            查看运行 {outcome.actualExecutionKey}
          </Link>
        )}
        <span className="text-[13px] text-slate-500">冻结于 {formatTime(outcome.capturedAt)}</span>
      </div>

      {objectives.length > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-[13px]">
            <thead className="text-xs text-slate-500">
              <tr>
                <th className="py-1 pr-3 font-medium">目标</th>
                <th className="py-1 pr-3 font-medium">预测区间</th>
                <th className="py-1 pr-3 font-medium">实际值</th>
                <th className="py-1 font-medium">区间命中</th>
              </tr>
            </thead>
            <tbody>
              {objectives.map(([code, metric]) => {
                const actual = Number(actuals[code]);
                const lower = Number(metric?.lower95);
                const upper = Number(metric?.upper95);
                const hit = comparable && actuals[code] != null && metric?.lower95 != null && metric?.upper95 != null && Number.isFinite(actual) && Number.isFinite(lower) && Number.isFinite(upper)
                  ? actual >= lower && actual <= upper
                  : null;
                return (
                  <tr key={code} className="border-t border-slate-100">
                    <td className="py-1 pr-3 font-medium text-slate-700">{code}</td>
                    <td className="py-1 pr-3 text-slate-600">{intervalText(metric)} {metric?.unit || ""}</td>
                    <td className="py-1 pr-3 text-slate-700">{formatMeasurementValue(actuals[code])} {metric?.unit || ""}</td>
                    <td className="py-1 text-slate-600">{hit == null ? "不适用" : hit ? "是" : "否"}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {adherence?.variables?.length > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-[13px]">
            <thead className="text-xs text-slate-500">
              <tr>
                <th className="py-1 pr-3 font-medium">参数</th>
                <th className="py-1 pr-3 font-medium">决定值</th>
                <th className="py-1 pr-3 font-medium">实际值</th>
                <th className="py-1 pr-3 font-medium">偏差</th>
                <th className="py-1 font-medium">核对</th>
              </tr>
            </thead>
            <tbody>
              {adherence.variables.map(variable => (
                <tr key={variable.variableCode} className="border-t border-slate-100">
                  <td className="py-1 pr-3 font-medium text-slate-700">{variable.variableCode}</td>
                  <td className="py-1 pr-3 text-slate-600">{formatMeasurementValue(variable.expectedValue)} {variable.unit || ""}</td>
                  <td className="py-1 pr-3 text-slate-600">{formatMeasurementValue(variable.actualValue)} {variable.unit || ""}</td>
                  <td className="py-1 pr-3 text-slate-600">{formatMeasurementValue(variable.deviation)}（容差 {formatMeasurementValue(variable.tolerance)}）</td>
                  <td className="py-1">
                    <Badge tone={variable.withinTolerance ? "success" : "danger"}>
                      {variable.withinTolerance ? "在容差内" : "超出容差"}
                    </Badge>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {!comparable && <p className="text-[13px] text-slate-500">区间命中不适用：仅采用原建议且已核对按决定执行的结果可比较。</p>}
      {decision?.decision === "modified" && (
        <p className="text-[13px] text-amber-700">
          本轮为修改后执行；预测对应原建议参数，实际值与原预测的差异包含参数修改的影响。
        </p>
      )}

      {adherence?.status === "outside-tolerance" && (
        <p className="text-[13px] text-slate-500">
          该运行可能按实际参数进入后续模型观察，但不会作为这轮建议的验证结果。
        </p>
      )}

      {!adherence && (
        <p className="text-[13px] text-slate-500">
          这条结果生成于执行核对上线前，没有记录实际参数与决定参数的一致性。
        </p>
      )}

      {outcome.exclusionReason && (
        <Alert tone="warning" title="本轮结果未纳入验证">{outcome.exclusionReason}</Alert>
      )}
    </section>
  );
}
