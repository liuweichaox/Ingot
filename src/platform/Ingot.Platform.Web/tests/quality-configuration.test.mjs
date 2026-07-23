import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";

const app = await readFile(new URL("../src/App.vue", import.meta.url), "utf8");
const router = await readFile(new URL("../src/router/index.js", import.meta.url), "utf8");
const definitions = await readFile(new URL("../src/views/InspectionDefinitionsView.vue", import.meta.url), "utf8");
const plans = await readFile(new URL("../src/views/QualityPlansView.vue", import.meta.url), "utf8");
const inspections = await readFile(new URL("../src/views/InspectionsView.vue", import.meta.url), "utf8");

test("quality configuration exposes definitions and plans as separate closed-loop pages", () => {
  assert.match(app, /label: "质量管理"/);
  assert.match(app, /path: "\/configuration\/inspection-definitions"/);
  assert.match(app, /path: "\/configuration\/quality-plans"/);
  assert.match(router, /InspectionDefinitionsView\.vue/);
  assert.match(router, /path: "\/quality-plans",\s+redirect: "\/configuration\/quality-plans"/);
  assert.match(definitions, /被质量方案引用/);
  assert.match(definitions, /基于此版本新建/);
  assert.match(definitions, /allowedValuesText/);
  assert.match(definitions, /删除未引用版本/);
  assert.match(plans, /管理检测定义/);
  assert.match(plans, /删除草稿/);
});

test("inspection entry renders configured input types instead of a generic text box", () => {
  assert.match(inspections, /characteristic\.inputType === 'select'/);
  assert.match(inspections, /characteristic\.allowedValues/);
  assert.match(inspections, /characteristic\.inputType === 'boolean'/);
  assert.match(inspections, /item\.required/);
});
