export const projectFormInitial = {
  name: "",
  referenceProcessExecutionId: "",
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
  active: "研发中",
  validating: "验证中",
  completed: "已完成",
  archived: "已归档",
  proposed: "待选择",
  selected: "已选择",
  supported: "已支持",
  rejected: "本轮未支持",
  inconclusive: "无定论",
  planned: "待批准",
  approved: "已批准",
  running: "执行中",
  cancelled: "已取消",
  candidate: "候选",
  validated: "已验证",
  evidence: "候选证据",
  replay: "回放已复核",
  laboratory: "实验室重复验证",
  production: "已发布生产",
  "awaiting-approval": "等待批准",
  ready: "待执行",
  dispatched: "已下发",
  draft: "草稿",
  reviewed: "已复核",
  accepted: "采用建议",
  modified: "修改建议",
  beneficial: "迁移有收益",
  neutral: "迁移无实质收益",
  "negative-transfer": "检测到负迁移",
  "insufficient-evidence": "迁移证据不足",
  recorded: "待复核",
};

export const taskTitles = {
  member: "添加项目成员",
  hypothesis: "提出研发假设",
  experiment: "设计验证实验",
  history: "导入历史运行",
  claim: "沉淀工艺知识",
  "rollback-drill": "记录停止与回退演练",
  transfer: "评估工艺知识迁移",
};

export const shadowDecisionLabels = {
  accepted: "采用建议",
  modified: "修改建议",
  rejected: "不采用建议",
};

export function formatResearchNumber(value) {
  if (!Number.isFinite(Number(value))) return "—";
  return Number(value).toLocaleString("zh-CN", { maximumFractionDigits: 4 });
}

export function experimentScale(experiment) {
  const runs = experiment.runPlan || [];
  const signatures = new Set(runs.map(run =>
    (run.factors || [])
      .map(factor => `${factor.variableCode}:${Number(factor.value)}`)
      .sort()
      .join("|")));
  const distinctConditions = experiment.optimization?.distinctConditionCount ||
    Math.max(1, signatures.size);
  return {
    distinctConditions,
    replicates: experiment.optimization?.replicatesPerCondition ||
      Math.max(1, Math.floor(runs.length / distinctConditions)),
  };
}

export function nextProjectAction(status) {
  if (status === "draft") return ["开始研发", "active"];
  if (status === "active") return ["进入验证", "validating"];
  if (status === "validating") return ["完成项目", "completed"];
  return null;
}

export function createTaskForm(task, workspace) {
  const variable = workspace?.project?.variables?.find(item => item.role === "control");
  return {
    member: "",
    statement: "",
    rationale: "",
    variableCode: variable?.code || "",
    validationOutcomeCode: "",
    expectedEffectDirection: "",
    minimumEffect: "",
    causalChain: "",
    temporalFeatures: "",
    interactions: "",
    failureConditions: "",
    falsificationConditions: "",
    hypothesisId: workspace?.hypotheses?.[0]?.hypothesisId || "",
    executionIds: [],
    baselineExecutionKeys: [],
    name: "",
    low: variable?.lowerLimit ?? "",
    high: variable?.upperLimit ?? "",
    designMethod: "full-factorial",
    designVariableCodes: variable?.code ? [variable.code] : [],
    designLevels: 2,
    designReplicates: 1,
    designBlocks: 1,
    designSampleCount: 8,
    responseSurfaceFamily: "central-composite",
    randomizationSeed: 0,
    generatedRunPlan: [],
    stopRule: "触发安全约束或设备异常时立即停止。",
    rollbackPlan: "恢复项目基线工艺规范并保留本次运行数据。",
    applicability: "",
    operatingRegionId: workspace?.operatingRegions?.find(item =>
      item.status === "validated" &&
      ["laboratory", "production"].includes(item.validationLevel))?.operatingRegionId || "",
    knowledgeSourceType: "window",
    transferAssessmentId: workspace?.transferAssessments?.find(item =>
      item.status === "reviewed" && item.outcome === "beneficial")?.assessmentId || "",
    drillName: "受控在线停止与回退演练",
    drillScenario: "模拟安全约束触发或优化器不可用",
    drillStopTrigger: "安全约束触发、数据失效或优化器不可用时停止下一条建议",
    drillRollbackTarget: "恢复上一组经现场确认的安全参数",
    drillExpectedActions: "停止下一条建议\n恢复安全参数\n保留运行与操作日志",
    drillObservedActions: "",
    drillPassed: "false",
    drillEvidenceReference: "",
    drillEvidenceContentHash: "",
    sourceOperatingRegionId: workspace?.transferSources?.[0]?.operatingRegionId || "",
    transferResultId: workspace?.experimentResults?.[0]?.resultId || "",
    coldStartResultId: workspace?.experimentResults?.[1]?.resultId || "",
    transferNotes: "",
  };
}
