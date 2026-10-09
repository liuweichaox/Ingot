// 验证配方版本上线检查只按该版本自己的采集、分析和质量依赖判断。
import { describe, expect, it } from "vitest";
import { recipeLaunchChecks } from "../src/recipe/launchReadiness.js";

const specification = {
  processSpecificationId: "spec-lens-a",
  version: 2,
  dataModelId: "model-lens",
  dataModelVersion: 3,
  status: "published",
  contextSelector: { product_family_code: "LENS", product_code: "LENS-A" },
};

const model = {
  modelId: "model-lens",
  version: 3,
  status: "published",
  controlParameters: [{ code: "holding.temperature-set", displayName: "保压温度设定" }],
  acquisition: { dataItems: [{ code: "mold.temperature", displayName: "模具温度", category: "process" }, { code: "stage", category: "stage" }] },
};

const task = {
  taskId: "press-01",
  dataModelId: "model-lens",
  dataModelVersion: 3,
  status: "published",
  valueMappings: [{ dataItemCode: "mold.temperature" }],
  processSpecification: { parameterMappings: [{ dataItemCode: "holding.temperature-set" }] },
};

const analysisPlan = { planId: "analysis-01", status: "published", analysisScope: "production-execution", dataModelId: "model-lens", dataModelVersion: 3, signals: [{ dataItemCode: "mold.temperature" }] };
const qualityPlan = { planId: "quality-01", status: "published", scope: { productFamilyCode: "lens" }, items: [{ definitionCode: "surface", definitionVersion: 1 }] };
const definition = { code: "surface", version: 1, characteristics: [{ code: "pv", name: "面形 PV", inputType: "numeric", unit: "μm", lowerLimit: 0, upperLimit: 1 }] };

const checks = overrides => recipeLaunchChecks({
  specification,
  models: [model],
  tasks: [task],
  analysisPlans: [analysisPlan],
  qualityPlans: [qualityPlan],
  definitions: [definition],
  ...overrides,
});

const byKey = (items, key) => items.find(item => item.key === key);

describe("配方版本上线检查", () => {
  it("依赖齐全时全部通过", () => {
    expect(checks().every(item => item.ready)).toBe(true);
  });

  it("指出采集配置缺少的控制参数和过程量", () => {
    const acquisition = byKey(checks({ tasks: [{ ...task, valueMappings: [], processSpecification: { parameterMappings: [] } }] }), "acquisition");
    expect(acquisition.ready).toBe(false);
    expect(acquisition.detail).toContain("保压温度设定");
    expect(acquisition.detail).toContain("模具温度");
    expect(acquisition.detail).not.toContain("stage");
  });

  it("只有草稿采集配置或引用其他工艺变量版本时不通过", () => {
    expect(byKey(checks({ tasks: [{ ...task, status: "draft" }] }), "acquisition").detail).toContain("还没有发布");
    expect(byKey(checks({ tasks: [{ ...task, dataModelVersion: 2 }] }), "acquisition").detail).toContain("还没有引用");
  });

  it("过程分析的上下文和配方冲突时不通过", () => {
    const conflicting = { ...analysisPlan, contextSelector: { product_code: "LENS-B" } };
    expect(byKey(checks({ analysisPlans: [conflicting] }), "analysis").ready).toBe(false);
    expect(byKey(checks({ analysisPlans: [{ ...analysisPlan, signals: [] }] }), "analysis").ready).toBe(false);
  });

  it("质量方案范围不覆盖或没有可判定的数值指标时不通过", () => {
    expect(byKey(checks({ qualityPlans: [{ ...qualityPlan, scope: { productCode: "LENS-B" } }] }), "quality").ready).toBe(false);
    const noLimits = { ...definition, characteristics: [{ code: "pv", inputType: "numeric", unit: "μm" }] };
    expect(byKey(checks({ definitions: [noLimits] }), "quality").detail).toContain("没有带单位和判定范围");
  });

  it("草稿配方或未发布工艺变量不通过", () => {
    expect(byKey(checks({ specification: { ...specification, status: "draft" } }), "recipe").ready).toBe(false);
    expect(byKey(checks({ models: [{ ...model, status: "draft" }] }), "model").ready).toBe(false);
    expect(byKey(checks({ models: [] }), "model").detail).toContain("不存在");
  });
});
