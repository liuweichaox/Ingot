// 解析和格式化研发机理、因果链与工作流字段，供页面和表单共享。
import { formatResearchNumber } from "./researchProjectModel";

export function groupMechanismUsages(usages) {
  const grouped = new Map();
  for (const usage of usages) {
    const key = `${usage.claimId}:${usage.claimVersion}`;
    const current = grouped.get(key) || { ...usage, usageTypes: [] };
    if (!current.usageTypes.includes(usage.usageType)) current.usageTypes.push(usage.usageType);
    if (!current.appliedClaim && usage.appliedClaim) current.appliedClaim = usage.appliedClaim;
    grouped.set(key, current);
  }
  return [...grouped.values()];
}

export function mechanismUsageLabel(value) {
  return ({
    "hard-constraint": "缩窄硬边界",
    "candidate-ranking": "候选偏好排序",
    "knowledge-context": "上下文与解释",
  })[value] || value;
}

export function mechanismEvidenceLabel(value) {
  return ({
    "knowledge-source": "原始知识来源",
    "knowledge-fragment": "可定位知识片段",
    "experiment-result": "正式实验结果",
  })[value] || value;
}

export function formatMechanismConstraint(constraint) {
  const lower = constraint.minimum == null ? "−∞" : formatResearchNumber(constraint.minimum);
  const upper = constraint.maximum == null ? "+∞" : formatResearchNumber(constraint.maximum);
  return `${lower} ～ ${upper} ${constraint.unit || ""}`.trim();
}

export function lines(value) {
  return String(value || "").split("\n").map(item => item.trim()).filter(Boolean);
}

export function parseWorkflowSteps(value) {
  return lines(value).map((item, index) => {
    const separator = item.lastIndexOf("|");
    const name = separator < 0 ? "" : item.slice(0, separator).trim();
    const minutes = separator < 0 ? Number.NaN : Number(item.slice(separator + 1).trim());
    if (!name || !Number.isFinite(minutes) || minutes < 0) throw new Error("流程步骤必须逐行填写为：步骤名称|分钟。");
    return { sequence: index + 1, name, minutes };
  });
}
export function parseCausalChain(value) {
  return lines(value).map(item => {
    const [edge, mechanism, direction = "unknown"] = item.split("|").map(part => part.trim());
    const [fromVariableCode, toVariableCode] = edge.split("->").map(part => part.trim());
    if (!fromVariableCode || !toVariableCode || !mechanism) throw new Error("作用链格式不完整。");
    return { fromVariableCode, toVariableCode, mechanism, direction };
  });
}

export function parseTemporalFeatures(value) {
  return lines(value).map(item => {
    const [variableCode, featureCode, phaseCode, delay, window] = item.split("|").map(part => part.trim());
    if (!variableCode || !featureCode) throw new Error("时间特征格式不完整。");
    return { variableCode, featureCode, phaseCode: phaseCode || null, delayMilliseconds: delay ? Number(delay) : null, windowMilliseconds: window ? Number(window) : null };
  });
}

export function parseInteractions(value) {
  return lines(value).map(item => {
    const [codes, description] = item.split("|").map(part => part.trim());
    if (!description) throw new Error("交互作用格式不完整。");
    return { variableCodes: codes.split(",").map(code => code.trim()).filter(Boolean), description };
  });
}

export function parseFailureConditions(value) {
  return lines(value).map(item => {
    const [condition, observableSignal, requiredResponse] = item.split("|").map(part => part.trim());
    if (!condition || !observableSignal || !requiredResponse) throw new Error("失效条件格式不完整。");
    return { condition, observableSignal, requiredResponse };
  });
}
