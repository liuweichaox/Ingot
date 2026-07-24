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
const manufacturingStore = await readFile(
  new URL("../../Ingot.Platform.Infrastructure/Manufacturing/PostgresManufacturingContextStore.cs", import.meta.url),
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
  assert.match(view, /装卸记录/);
  assert.doesNotMatch(view, /<el-form-item label="来源"/);
  assert.match(view, /source: "manual"/);
  assert.match(view, /\/api\/v1\/production-contexts/);
  assert.match(view, /\/api\/v1\/tooling-installations/);
  assert.match(view, /\/api\/v1\/tooling-component-types/);
  assert.doesNotMatch(view, /外部工单（可选）|外部生产批次（可选）/);
  assert.match(manufacturingStore, /ExternalOrderRef/);
  assert.match(manufacturingStore, /ExternalBatchRef/);
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
  assert.match(view, /deleteJson/);
  assert.match(view, /保存修改/);
  assert.match(view, /新版本维护/);
  assert.match(view, /装卸历史/);
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

test("tooling installation is a physical interval rather than a per-cycle action", () => {
  const installationPane = view.match(/<el-tab-pane label="装卸记录"[\s\S]*?<el-tab-pane label="模具组合"/)?.[0] || "";

  assert.match(installationPane, /当前安装/);
  assert.match(installationPane, /装卸历史/);
  assert.match(installationPane, /使用区间/);
  assert.match(installationPane, /生产状态/);
  assert.match(installationPane, /待生产切换/);
  assert.match(installationPane, /卸下/);
  assert.doesNotMatch(installationPane, />删除</);
  assert.match(view, /同时结束.*当前生产配置/);
  assert.match(view, /installableRevisions/);
  assert.match(manufacturingStore, /idx_tooling_installations_active_revision/);
  assert.match(manufacturingStore, /物理组件已装在设备/);
  assert.match(manufacturingStore, /组件 .*不能装入设备/);
});
