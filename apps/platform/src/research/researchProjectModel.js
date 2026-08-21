
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
  preregistration: "冻结阶段 0 预注册",
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
  const now = new Date();
  const prior = new Date(now.getTime() - 30 * 24 * 60 * 60 * 1000);
  const workflowEnd = new Date(now.getTime() - 24 * 60 * 60 * 1000);
  const workflowStart = new Date(workflowEnd.getTime() - 60 * 60 * 1000);
  const localDateTime = value => new Date(value.getTime() - value.getTimezoneOffset() * 60000)
    .toISOString().slice(0, 16);
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
    preregDataScope: "同产品、同设备范围内已完成且可唯一关联实际参数、过程轨迹和检验的真实运行",
    preregDataFrom: localDateTime(prior),
    preregDataTo: localDateTime(now),
    preregEdgeId: workspace?.project?.context?.edge_id || "",
    preregEquipmentId: workspace?.project?.context?.equipment_id || "",
    preregMaximumRuns: "2000",
    preregInclusionMethod: "按运行身份关联实际参数、过程数据、上下文和有效检验结果后纳入",
    preregInclusionRules: "运行开始与完成边界完整\n实际参数来自设备回读\n运行与检验唯一关联",
    preregExclusionRules: "运行身份冲突\n关键过程数据缺失\n检验无效或无法唯一关联",
    preregMatchingRules: "同产品比较\n按设备、材料批次和工装分层\n保留上下文重叠与混杂结论",
    preregBaselineMethods: "工程师当前流程\n历史工程师顺序\n适用的传统 DOE 或响应面\n随机或空间填充基线",
    preregPrimaryMetrics: "从异常到首个可执行假设的时间\n达到并重复确认规格的有效实验数",
    preregGuardrailMetrics: "运行—检验唯一关联率\n预测区间覆盖率\n已知安全边界违规数为零",
    preregStopConditions: "数据链无法稳定关联\n预测长期失准\n发生已知安全边界违规",
    preregFalsificationConditions: "Ingot 未缩短形成可执行假设的时间\n序贯建议不优于适用简单基线",
    preregWorkflowName: "工程师当前找数、分析和形成假设流程",
    preregWorkflowStart: localDateTime(workflowStart),
    preregWorkflowEnd: localDateTime(workflowEnd),
    preregWorkflowSteps: "查找并导出运行记录|20\n关联质量与上下文|20\n建立比较并形成假设|20",
    preregWorkflowNotes: "",
  };
}
