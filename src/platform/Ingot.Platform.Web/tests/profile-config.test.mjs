import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { createOpticalMoldingSample } from "../src/data/profileSamples.js";

const view = await readFile(new URL("../src/views/ProfileConfigView.vue", import.meta.url), "utf8");
const app = await readFile(new URL("../src/App.vue", import.meta.url), "utf8");
const router = await readFile(new URL("../src/router/index.js", import.meta.url), "utf8");

test("optical molding sample contains the complete analytical configuration", () => {
  const sample = createOpticalMoldingSample();
  assert.equal(sample.acquisition.samplePeriodMs, 1000);
  assert.equal(sample.acquisition.fields.length, 13);
  assert.equal(sample.recipe.parameters.length, 35);
  assert.equal(sample.recipe.changeReasonRequired, false);
  assert.equal(sample.phases.mappings.length, 5);
  assert.equal(
    sample.phases.mappings.reduce((total, phase) => total + phase.expectedDurationSeconds, 0),
    600,
  );
  assert.ok(sample.acquisition.fields.every((field) => field.code && field.sourceField && field.unit));
  assert.equal(sample.acquisition.fields.filter((field) => field.useInComparison).length, 5);
  assert.ok(sample.acquisition.fields.every((field) => !("transform" in field) && !("quantityKind" in field)));
});

test("profile configuration is reachable and supports local persistence and JSON exchange", () => {
  assert.match(app, /index="\/profiles"/);
  assert.match(app, /工艺配置/);
  assert.match(router, /path: "\/profiles"/);
  assert.match(router, /ProfileConfigView\.vue/);
  assert.match(view, /localStorage\.setItem/);
  assert.match(view, /导入 JSON/);
  assert.match(view, /导出 JSON/);
  assert.match(view, /采集字段/);
  assert.match(view, /配方参数/);
  assert.match(view, /阶段映射/);
  assert.match(view, /发布到平台/);
  assert.match(view, /useInComparison/);
  assert.match(view, /\/api\/v1\/feature-definitions/);
  assert.match(view, /\/api\/v1\/phase-mappings/);
});
