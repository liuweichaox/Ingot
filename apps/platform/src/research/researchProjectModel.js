// 定义配方优化任务表单默认值、状态文案和建议闭环辅助函数。
export const projectFormInitial = {
  name: "",
  referenceProcessExecutionId: "",
  referenceContext: {},
  scenarioPackageKey: "",
  dataModelKey: "",
  processName: "",
  productName: "",
  materialName: "",
  description: "",
  objectiveKey: "",
  objectiveCode: "",
  objectiveName: "",
  objectiveUnit: "",
  objectiveDirection: "minimize",
  objectiveTarget: "",
  objectiveWeight: "1",
  objectiveDataSource: "",
  variableCode: "",
  variableName: "",
  variableUnit: "",
  variableLower: "",
  variableUpper: "",
  variableDataSource: "",
  outcomeConstraintKey: "",
  outcomeConstraintCode: "",
  outcomeConstraintName: "",
  outcomeConstraintMetric: "",
  outcomeConstraintOperator: "<=",
  outcomeConstraintLimit: "",
  outcomeConstraintUnit: "",
  outcomeConstraintProbability: "0.95",
};

export const statusLabels = {
  draft: "草稿",
  active: "研发中",
  validating: "复核中",
  completed: "已完成",
  archived: "已归档",
  proposed: "待选择",
  selected: "已选择",
  supported: "已支持",
  rejected: "本轮未支持",
  inconclusive: "无定论",
  candidate: "候选",
  validated: "已验证",
  evidence: "证据已关联",
  replay: "回放已复核",
  production: "生产运行",
  ready: "待执行",
  accepted: "采用建议",
  modified: "修改建议",
  recorded: "待复核",
};

export const usefulnessLabels = {
  useful: "有用",
  "partly-useful": "部分有用",
  "not-useful": "无用",
};

export function shortResearchHash(value) {
  const normalized = String(value || "").trim();
  return normalized ? `${normalized.slice(0, 12)}…` : "—";
}

export function buildRecipeRecommendationDecisionPayload(item, form) {
  const rejected = form.decision === "rejected";
  return {
    decision: form.decision,
    engineerSelectedParameters: rejected ? [] : (item.parameters || []).map(parameter => ({
      ...parameter,
      value: Number(form.factors?.[parameter.variableCode]),
    })),
    reason: form.reason || null,
    usefulnessRating: form.usefulnessRating || null,
  };
}

export function formatResearchNumber(value) {
  if (!Number.isFinite(Number(value))) return "—";
  return Number(value).toLocaleString("zh-CN", { maximumFractionDigits: 4 });
}

export function nextProjectAction(status) {
  if (status === "draft") return ["开始研发", "active"];
  if (status === "active") return ["进入复核", "validating"];
  if (status === "validating") return ["完成项目", "completed"];
  return null;
}

export function canArchiveProject(status) {
  return status === "draft" || status === "completed";
}
