// 只按这一版配方自己的依赖判断能否上线：工艺变量、采集映射、过程分析和质量方案。
import { inferObjective } from "./proposalScope";

const same = (left, right) => String(left ?? "").trim().toLowerCase() === String(right ?? "").trim().toLowerCase();

const sameModel = (item, specification) =>
  same(item?.dataModelId, specification.dataModelId) &&
  Number(item?.dataModelVersion || 1) === Number(specification.dataModelVersion || 1);

function selectorFits(selector, recipeSelector) {
  return Object.entries(selector || {}).every(([key, value]) => {
    const recipeValue = recipeSelector?.[key] ?? recipeSelector?.[key.replaceAll(".", "_")];
    return recipeValue === undefined || same(recipeValue, value);
  });
}

function scopeFits(scope, specification) {
  const recipeSelector = specification.contextSelector || {};
  const fits = (scoped, key) => !String(scoped || "").trim() || recipeSelector[key] === undefined || same(scoped, recipeSelector[key]);
  return (!String(scope?.processSpecificationId || "").trim() || same(scope.processSpecificationId, specification.processSpecificationId)) &&
    fits(scope?.productFamilyCode, "product_family_code") &&
    fits(scope?.productCode, "product_code") &&
    selectorFits(scope?.contextSelector, recipeSelector);
}

const missingLabel = (codes, names) => codes.slice(0, 3).map(code => names.get(code) || code).join("、") + (codes.length > 3 ? ` 等 ${codes.length} 项` : "");

function acquisitionCheck(specification, model, tasks) {
  const candidates = (tasks || []).filter(task => sameModel(task, specification));
  const published = candidates.filter(task => task.status === "published");
  const base = { key: "acquisition", title: "采集配置", to: "/configuration/ingestion-tasks", action: "配置采集" };
  if (!candidates.length) return { ...base, ready: false, detail: "还没有引用这版工艺变量的采集配置。" };
  if (!published.length) return { ...base, ready: false, detail: `采集配置 ${candidates.map(task => task.taskId).join("、")} 还没有发布。` };
  if (!model) return { ...base, ready: false, detail: "工艺变量不可用，无法核对点位映射。" };
  const names = new Map([
    ...(model.controlParameters || []).map(item => [item.code, item.displayName || item.code]),
    ...(model.acquisition?.dataItems || []).map(item => [item.code, item.displayName || item.name || item.code]),
  ]);
  const mappedParameters = new Set(published.flatMap(task => (task.processSpecification?.parameterMappings || []).map(item => item.dataItemCode)));
  const mappedValues = new Set(published.flatMap(task => (task.valueMappings || []).map(item => item.dataItemCode)));
  const missingParameters = (model.controlParameters || []).map(item => item.code).filter(code => !mappedParameters.has(code));
  const missingValues = (model.acquisition?.dataItems || [])
    .filter(item => (item.category || "process") === "process")
    .map(item => item.code)
    .filter(code => !mappedValues.has(code));
  const gaps = [
    missingParameters.length && `控制参数 ${missingLabel(missingParameters, names)} 没有映射实际值`,
    missingValues.length && `过程量 ${missingLabel(missingValues, names)} 没有映射点位`,
  ].filter(Boolean);
  return gaps.length
    ? { ...base, ready: false, detail: `${gaps.join("；")}。` }
    : { ...base, ready: true, detail: `${published.map(task => task.taskId).join("、")} 已映射全部控制参数和过程量。` };
}

function qualityCheck(specification, plans, definitions) {
  const base = { key: "quality", title: "质量方案", to: "/configuration/quality-plans", action: "配置质量方案" };
  const covering = (plans || []).filter(plan => plan.status === "published" && scopeFits(plan.scope, specification));
  if (!covering.length) return { ...base, ready: false, detail: "没有覆盖这版配方的已发布质量方案。" };
  const numeric = covering.flatMap(plan => (plan.items || []).flatMap(item => {
    const definition = (definitions || []).find(candidate =>
      candidate.code === item.definitionCode &&
      Number(candidate.version) === Number(item.definitionVersion || candidate.version));
    return (definition?.characteristics || [])
      .filter(characteristic => (characteristic.inputType || "numeric") === "numeric" && inferObjective(characteristic) && characteristic.unit)
      .map(characteristic => characteristic.name || characteristic.code);
  }));
  return numeric.length
    ? { ...base, ready: true, detail: `${covering.map(plan => plan.planId).join("、")} 覆盖这版配方，质量目标 ${numeric[0]}。` }
    : { ...base, ready: false, detail: `${covering.map(plan => plan.planId).join("、")} 没有带单位和判定范围的数值指标，无法作为校正目标。` };
}

export function recipeLaunchChecks({ specification, models, tasks, analysisPlans, qualityPlans, definitions }) {
  const model = (models || []).find(item =>
    same(item.modelId, specification.dataModelId) &&
    Number(item.version) === Number(specification.dataModelVersion || 1));
  const analysis = (analysisPlans || []).filter(plan =>
    plan.status === "published" &&
    (plan.analysisScope || "production-execution") === "production-execution" &&
    sameModel(plan, specification) &&
    selectorFits(plan.contextSelector, specification.contextSelector) &&
    (plan.signals || []).length > 0);
  return [
    {
      key: "recipe",
      title: "配方版本",
      ready: specification.status === "published",
      detail: specification.status === "published" ? "这一版已发布。" : "这一版还是草稿，发布后才能用于生产切换。",
      to: "/configuration/process-specifications",
      action: "发布配方版本",
    },
    {
      key: "model",
      title: "工艺变量",
      ready: model?.status === "published",
      detail: !model
        ? `引用的工艺变量 ${specification.dataModelId} v${specification.dataModelVersion} 不存在。`
        : model.status === "published" ? `${model.modelId} v${model.version} 已发布。` : `${model.modelId} v${model.version} 还没有发布。`,
      to: "/configuration/process-data-models",
      action: "检查工艺变量",
    },
    acquisitionCheck(specification, model, tasks),
    {
      key: "analysis",
      title: "过程分析",
      ready: analysis.length > 0,
      detail: analysis.length
        ? `${analysis.map(plan => plan.planId).join("、")} 覆盖这版工艺变量。`
        : "没有引用这版工艺变量、并包含过程曲线的已发布过程分析。",
      to: "/configuration/process-analysis-plans",
      action: "配置过程分析",
    },
    qualityCheck(specification, qualityPlans, definitions),
  ];
}
