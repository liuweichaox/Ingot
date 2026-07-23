import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const acquisition = JSON.parse(await readFile(
  new URL("../../../../docs/samples/optical-molding-flexible/acquisition-profile.v1.json", import.meta.url),
  "utf8",
));
const recipeProfile = JSON.parse(await readFile(
  new URL("../../../../docs/samples/optical-molding-flexible/recipe-profile.v1.json", import.meta.url),
  "utf8",
));
const recipeInstance = JSON.parse(await readFile(
  new URL("../../../../docs/samples/optical-molding-flexible/recipe-instance.example.json", import.meta.url),
  "utf8",
));
const stageMapping = JSON.parse(await readFile(
  new URL("../../../../docs/samples/optical-molding-flexible/phase-mapping.v1.json", import.meta.url),
  "utf8",
));
const plan = JSON.parse(await readFile(
  new URL("../../../../docs/samples/optical-molding-flexible/process-analysis-plan.v1.json", import.meta.url),
  "utf8",
));

const dataModelView = await readFile(new URL("../src/views/ProcessDataModelsView.vue", import.meta.url), "utf8");
const recipeView = await readFile(new URL("../src/views/RecipeVersionsView.vue", import.meta.url), "utf8");
const analysisView = await readFile(new URL("../src/views/ProcessAnalysisPlansView.vue", import.meta.url), "utf8");
const acquisitionView = await readFile(new URL("../src/views/AcquisitionProfilesView.vue", import.meta.url), "utf8");
const app = await readFile(new URL("../src/App.vue", import.meta.url), "utf8");
const router = await readFile(new URL("../src/router/index.js", import.meta.url), "utf8");

test("optical molding sample contains the complete analytical configuration", () => {
  assert.equal(acquisition.samplePeriodMs, 1000);
  assert.equal(acquisition.fields.length, 13);
  assert.equal(recipeProfile.parameters.length, 31);
  assert.equal(Object.keys(recipeInstance.resolvedParameters).length, 31);
  assert.equal(stageMapping.mappings.length, 5);
  assert.equal(
    stageMapping.mappings.reduce((total, stage) => total + stage.exampleDurationSeconds, 0),
    600,
  );
  assert.ok(acquisition.fields.every((field) => field.code && field.sourceField && field.unit));
  assert.equal(plan.signals.length, 5);
  assert.ok(acquisition.fields.every((field) => !("transform" in field) && !("quantityKind" in field)));
  assert.ok(recipeProfile.parameters.every((field) => !field.code.endsWith(".core") && !field.code.endsWith(".holder")));
});

test("process configuration separates model definitions, recipe values, and analysis choices", () => {
  assert.equal(acquisition.fields.length, 13);
  assert.equal(recipeProfile.parameters.length, 31);
  assert.ok(recipeProfile.parameters.every((item) => !("value" in item)));
  assert.equal(Object.keys(recipeInstance.resolvedParameters).length, 31);
  assert.equal(plan.signals.length, 5);
  assert.ok(acquisition.fields.every((item) => !("useInComparison" in item)));
  assert.equal(plan.cohortDimension, "quality.outcome");
});

test("three process configuration pages use versioned platform APIs and legacy route redirects", () => {
  assert.match(app, /工艺数据模型/);
  assert.match(app, /配方版本/);
  assert.match(app, /分析方案/);
  assert.match(router, /path: "\/profiles",\s+redirect: "\/configuration\/process-data-models"/);
  assert.match(router, /ProcessDataModelsView\.vue/);
  assert.match(router, /RecipeVersionsView\.vue/);
  assert.match(router, /ProcessAnalysisPlansView\.vue/);
  assert.match(dataModelView, /\/api\/v1\/process-data-models/);
  assert.match(recipeView, /\/api\/v1\/recipe-versions/);
  assert.match(analysisView, /\/api\/v1\/process-analysis-plans/);
  assert.doesNotMatch(`${dataModelView}${recipeView}${analysisView}`, /语义状态|待确认语义|Profile ID/);
  assert.doesNotMatch(`${dataModelView}${recipeView}${analysisView}`, /载入.*样例|加载.*示例|restoreSample|profileSamples/);
  assert.doesNotMatch(dataModelView, /localStorage/);
});

test("versioned process configuration is list-first and supports create, maintain, retire, and draft deletion", () => {
  for (const view of [dataModelView, recipeView, analysisView]) {
    assert.match(view, /创建新版本|沿用为新版本/);
    assert.match(view, /停用/);
    assert.match(view, /删除草稿/);
    assert.match(view, /deleteJson/);
    assert.match(view, /registry-card/);
    assert.match(view, /<el-drawer/);
    assert.match(view, /selectExisting/);
    assert.doesNotMatch(view, /configuration-layout/);
  }
});

test("acquisition is maintained as a versioned task with staged validation", () => {
  assert.match(app, /采集任务/);
  assert.match(app, /采集节点/);
  assert.match(acquisitionView, /任务信息/);
  assert.match(acquisitionView, /数据源与采集对象/);
  assert.match(acquisitionView, /采集字段映射/);
  assert.match(acquisitionView, /发布检查/);
  assert.match(acquisitionView, /validationIssues/);
  assert.match(acquisitionView, /TablePagination/);
  assert.match(acquisitionView, /createVersion/);
  assert.match(acquisitionView, /removeProfile/);
  assert.match(acquisitionView, /retireProfile/);
  for (const protocol of ["HTTP 轮询", "MQTT", "OPC UA", "Modbus TCP"]) {
    assert.match(acquisitionView, new RegExp(protocol));
  }
  assert.match(acquisitionView, /passwordSecretRef/);
  assert.doesNotMatch(acquisitionView, /v-model="editor\.(mqtt|opcUa)\.password"/);
  assert.match(acquisitionView, /modbusAddress/);
  assert.match(acquisitionView, /byteOrder/);
  assert.match(acquisitionView, /wordOrder/);
});
