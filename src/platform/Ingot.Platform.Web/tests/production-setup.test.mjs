import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const view = await readFile(new URL("../src/views/ProductionSetupView.vue", import.meta.url), "utf8");
const app = await readFile(new URL("../src/App.vue", import.meta.url), "utf8");
const router = await readFile(new URL("../src/router/index.js", import.meta.url), "utf8");
const manufacturingControllers = await readFile(
  new URL("../../Ingot.Platform.Api/Controllers/ManufacturingContextControllers.cs", import.meta.url),
  "utf8",
);

test("production setup is divided into shop-floor and configuration menus", () => {
  assert.match(app, /path: "\/production\/changeover"/);
  assert.match(app, /path: "\/production\/tooling-installations"/);
  assert.match(app, /path: "\/configuration\/component-types"/);
  assert.match(app, /path: "\/configuration\/components"/);
  assert.match(app, /path: "\/configuration\/tooling-types"/);
  assert.match(app, /path: "\/configuration\/tooling-assemblies"/);
  assert.match(app, /运行与追溯/);
  assert.match(app, /工装管理/);
  assert.match(router, /path: "\/production-setup"/);
  assert.match(router, /redirect: "\/production\/changeover"/);
  assert.match(router, /ProductionSetupView\.vue/);
});

test("production setup keeps tooling lifecycle and MES boundary explicit", () => {
  assert.match(view, /工装类型/);
  assert.match(view, /组件台账/);
  assert.match(view, /不可变组合版本/);
  assert.match(view, /装模记录/);
  assert.match(view, /MES 由接口写入/);
  assert.match(view, /\/api\/v1\/production-contexts/);
  assert.match(view, /\/api\/v1\/tooling-installations/);
  assert.match(view, /\/api\/v1\/tooling-component-types/);
  assert.match(view, /externalOrderRef/);
});

test("components are classified independently and receive roles only in assembly revisions", () => {
  assert.match(view, /componentTypeCode/);
  assert.match(view, /revisionForm\.members/);
  assert.match(view, /currentRoleLabels/);
  assert.match(view, /acceptedComponentTypeCodes/);
  assert.match(view, /componentTypeLabel/);
  assert.doesNotMatch(view, /componentForm\.roleCode/);
  assert.doesNotMatch(view, /componentForm\.toolingTypeCode/);
  assert.doesNotMatch(view, /加载.*示例|载入.*样例|loadOpticalExample/);
});

test("component registry is list-first and maintains records in a right drawer", () => {
  const componentPane = view.match(/<el-tab-pane label="组件台账"[\s\S]*?<el-tab-pane label="工装类型"/)?.[0] || "";
  assert.match(componentPane, /<el-table :data="pagedComponents"/);
  assert.match(componentPane, /<TablePagination/);
  assert.doesNotMatch(componentPane, /split-layout|form-panel/);
  assert.match(view, /<el-drawer[\s\S]*v-model="componentDrawerVisible"/);
  assert.match(view, /:title="editingComponentId \? '编辑组件' : '登记组件'"/);
  assert.match(view, /placeholder="选择已配置的组件类型"/);
  assert.doesNotMatch(view, /也可输入新的类型/);
});

test("every tooling master-data creation entry has a maintenance path", () => {
  assert.match(view, /editComponentType/);
  assert.match(view, /editComponent\(row\)/);
  assert.match(view, /editAssembly/);
  assert.match(view, /baseToolingTypeOn/);
  assert.match(view, /deleteComponentType/);
  assert.match(view, /deleteComponent\(row\)/);
  assert.match(view, /deleteAssembly/);
  assert.match(view, /deleteRevision/);
  assert.match(view, /deleteToolingType/);
  assert.match(view, /deleteInstallation/);
  assert.match(view, /deleteContext/);
  assert.match(view, /deleteJson/);
  assert.match(view, /保存修改/);
  assert.match(view, /新版本维护/);
  assert.match(view, /装模历史/);
  assert.match(view, /生产配置历史/);
  assert.match(view, /v-model="contextDrawerVisible"/);
  assert.match(view, /v-model="installationDrawerVisible"/);
  assert.match(view, /v-model="editorVisible"/);
  assert.match(manufacturingControllers, /DeleteComponentTypeAsync/);
  assert.match(manufacturingControllers, /DeleteToolingTypeAsync/);
  assert.match(manufacturingControllers, /DeleteComponentAsync/);
  assert.match(manufacturingControllers, /DeleteAssemblyAsync/);
  assert.match(manufacturingControllers, /DeleteAssemblyRevisionAsync/);
  assert.match(manufacturingControllers, /DeleteInstallationAsync/);
  assert.match(manufacturingControllers, /DeleteProductionContextAsync/);
});
