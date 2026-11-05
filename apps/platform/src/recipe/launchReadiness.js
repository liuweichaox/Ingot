// 只按这一版配方自己的依赖判断能否上线：工艺变量、过程分析和质量方案。
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

export function recipeLaunchChecks({ specification, models, analysisPlans, qualityPlans, definitions }) {
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
    {
      key: "analysis",
      title: "分析规则",
      ready: analysis.length > 0,
      detail: analysis.length
        ? `${analysis.map(plan => plan.planId).join("、")} 覆盖这版工艺变量。`
        : "没有引用这版工艺变量、并包含过程曲线的已发布分析规则。",
      to: "/configuration/process-analysis-plans",
      action: "配置分析规则",
    },
    qualityCheck(specification, qualityPlans, definitions),
  ];
}
