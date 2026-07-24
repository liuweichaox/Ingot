import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const pages = await readFile(new URL("../src/pages/index.jsx", import.meta.url), "utf8");
const http = await readFile(new URL("../src/api/http.js", import.meta.url), "utf8");

test("operations retain server pagination and resumable live events", () => {
  assert.match(pages, /offset: String\(\(page - 1\) \* pageSize\)/);
  assert.match(pages, /makeCycleQuery\(appliedFilters, value, pageSize\)/);
  assert.match(pages, /makeEventQuery\(appliedFilters, value, pageSize\)/);
  assert.match(pages, /Object\.entries\(appliedFilters\)/);
  assert.match(pages, /afterIngestId/);
  assert.match(pages, /new EventSource\(`\/api\/v1\/events\/stream/);
  assert.match(pages, /<Pagination/);
  assert.doesNotMatch(pages, /加载更早记录|beforeIngestId/);
});

test("configuration registries keep create, version, retire, and draft deletion workflows", () => {
  for (const endpoint of [
    "/api/v1/process-data-models", "/api/v1/recipe-versions",
    "/api/v1/process-analysis-plans", "/api/v1/inspection-definitions",
    "/api/v1/inspection-plans", "/api/v1/acquisition-profiles",
  ]) {
    assert.match(pages, new RegExp(endpoint.replaceAll("/", "\\/")));
  }
  assert.match(pages, /创建新版本/);
  assert.match(pages, /沿用为新版本/);
  assert.match(pages, /停用/);
  assert.match(pages, /删除草稿/);
  assert.match(pages, /<Drawer/);
});

test("tooling and production lifecycle operations remain explicit", () => {
  for (const endpoint of [
    "/api/v1/tooling-component-types", "/api/v1/tooling-components", "/api/v1/tooling-types",
    "/api/v1/tooling-assemblies", "/api/v1/tooling-installations", "/api/v1/production-contexts",
  ]) {
    assert.match(pages, new RegExp(endpoint.replaceAll("/", "\\/")));
  }
  assert.match(pages, /:remove/);
  assert.match(pages, /:close/);
  assert.match(pages, /installedAt: new Date\(\)\.toISOString\(\)/);
  assert.match(pages, /source: "manual"/);
});

test("quality entry supports configured input types, attachments, and human review", () => {
  assert.match(pages, /characteristic\.inputType === "select"/);
  assert.match(pages, /characteristic\.allowedValues/);
  assert.match(pages, /characteristic\.inputType === "boolean"/);
  assert.match(pages, /\/api\/v1\/inspection-attachments/);
  assert.match(pages, /\/api\/v1\/inspection-records/);
  assert.match(pages, /\/api\/v1\/inspection-reviews/);
  assert.match(pages, /REINSPECTION_REQUIRED/);
  assert.match(pages, /inspection-tasks\?status=all&limit=\$\{inspectionPageSize\}&offset=\$\{\(taskPage - 1\) \* inspectionPageSize\}/);
  assert.match(pages, /inspection-records\?limit=\$\{inspectionPageSize\}&offset=\$\{\(recordPage - 1\) \* inspectionPageSize\}/);
  assert.match(pages, /page=\{taskPage\}/);
  assert.match(pages, /page=\{recordPage\}/);
});

test("process improvement exposes mechanism fusion, knowledge extraction, and scientific validation", () => {
  assert.match(pages, /\/api\/v1\/mechanism-models/);
  assert.match(pages, /\/api\/v1\/mechanism-fusions/);
  assert.match(pages, /mechanism-as-feature/);
  assert.match(pages, /\/api\/v1\/process-knowledge/);
  assert.match(pages, /accept="\.pdf,\.xlsx,\.xlsm/);
  assert.match(pages, /\/api\/v1\/scientific-validation/);
  assert.match(pages, /manifestJson/);
  assert.match(pages, /有效范围/);
});

test("event subscriptions retain create, edit, enable, signed-secret, and delete operations", () => {
  assert.match(pages, /新建订阅/);
  assert.match(pages, /putJson\(`\/api\/v1\/subscriptions\/\$\{editing\.subscriptionId\}`/);
  assert.match(pages, /\/enabled/);
  assert.match(pages, /clearSecret/);
  assert.match(pages, /HMAC-SHA256/);
  assert.match(pages, /deleteJson\(`\/api\/v1\/subscriptions/);
});

test("Chat remains read-only, streams with resume support, and handles API outages clearly", () => {
  assert.match(pages, /\/api\/v1\/chat\/capabilities/);
  assert.match(pages, /\/api\/v1\/chat\/runs/);
  assert.match(pages, /streamSse/);
  assert.match(pages, /不写 PLC、CNC 或机器人/);
  assert.match(http, /Last-Event-ID/);
  assert.match(http, /PostgreSQL\/TimescaleDB/);
  assert.doesNotMatch(pages, /\/api\/v1\/agent/);
});
