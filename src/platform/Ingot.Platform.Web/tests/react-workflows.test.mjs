import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const pages = await readFile(new URL("../src/pages/index.jsx", import.meta.url), "utf8");
const http = await readFile(new URL("../src/api/http.js", import.meta.url), "utf8");
const ui = await readFile(new URL("../src/ui/components.jsx", import.meta.url), "utf8");
const researchProjects = await readFile(new URL("../src/pages/ResearchProjectsPage.jsx", import.meta.url), "utf8");
const researchAssets = await readFile(new URL("../src/pages/ResearchAssetsPage.jsx", import.meta.url), "utf8");

test("operations retain server pagination and resumable live events", () => {
  assert.match(pages, /offset: String\(\(page - 1\) \* pageSize\)/);
  assert.match(pages, /makeCycleQuery\(appliedFilters, value, pageSize\)/);
  assert.match(pages, /makeEventQuery\(appliedFilters, value, pageSize\)/);
  assert.match(pages, /Object\.entries\(appliedFilters\)/);
  assert.match(pages, /afterIngestId/);
  assert.match(pages, /new EventSource\(`\/api\/v1\/events\/stream/);
  assert.match(pages, /<Pagination/);
  assert.doesNotMatch(pages, /加载更早记录|beforeIngestId/);
  assert.match(pages, /\{ key: "completedAt", label: "结束"/);
  assert.match(pages, /navigate\(`\/cycles\/\$\{encodeURIComponent\(row\.correlationId\)\}`\)/);
  assert.match(pages, /export function CycleDetailPage/);
  assert.match(pages, /processDataQuality/);
  assert.match(pages, /operationRunId=\$\{encodedId\}/);
  assert.match(pages, /历史对比/);
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
  assert.match(pages, /inspection-tasks\?status=\$\{taskStatus\}&limit=\$\{inspectionPageSize\}&offset=\$\{\(taskPage - 1\) \* inspectionPageSize\}/);
  assert.match(pages, /inspection-records\?limit=\$\{inspectionPageSize\}&offset=\$\{\(recordPage - 1\) \* inspectionPageSize\}/);
  assert.match(pages, /\{ key: "inspectionPlanName", label: "质量方案" \}/);
  assert.match(pages, /\{ key: "attachments", label: "附件"/);
  assert.match(pages, /title="原始附件"/);
  assert.match(pages, /title="测量结果"/);
  assert.match(pages, /inspection-reviews\?inspectionRecordId=/);
  assert.match(pages, /page=\{taskPage\}/);
  assert.match(pages, /page=\{recordPage\}/);
});

test("edge pages use the registry heartbeat contract for status", () => {
  assert.match(pages, /const edgeStatus = edge =>/);
  assert.match(pages, /edge\?\.lastSeen/);
  assert.match(pages, /edge\.lastError/);
  assert.match(pages, /\{ key: "lastSeen", label: "最后心跳"/);
  assert.doesNotMatch(pages, /\{ key: "lastSeenAt", label: "最后心跳"/);
  assert.match(pages, /state\.edges\.filter\(item => edgeStatus\(item\) === "online"\)/);
});

test("workbench and logs use current response contracts without misleading placeholders", () => {
  assert.match(pages, /\/api\/v1\/cycles\?limit=8/);
  assert.match(pages, /cycleOverview: cycles\.overview/);
  assert.match(pages, /state\.loading \? <LoadingCard \/>/);
  assert.match(pages, /logs\?pageSize=200/);
  assert.match(pages, /\{ key: "source", label: "来源"/);
  assert.doesNotMatch(pages, /keyField="id" columns=\{\[\s*\{ key: "timestamp"/);
  assert.match(ui, /not_applicable: "无需质检"/);
});

test("cycle comparison submits the selection contract and renders business results", () => {
  assert.match(pages, /cycleIds: \[baselineCycleId, candidate\.trim\(\)\]/);
  assert.match(pages, /title="周期概况"/);
  assert.match(pages, /title="信号差异"/);
  assert.doesNotMatch(pages, /JSON\.stringify\(result, null, 2\)/);
});

test("mechanism assets are presented as business fields instead of raw JSON", () => {
  assert.match(researchAssets, /title: "机理模型"/);
  assert.match(researchAssets, /key: "outputCode", label: "输出"/);
  assert.match(researchAssets, /title: "融合定义"/);
  assert.match(researchAssets, /key: "mode", label: "融合方式"/);
  assert.doesNotMatch(researchAssets, /JSON\.stringify|JSON\.parse/);
});

test("research projects expose the evidence-backed experiment and process-window workflow", () => {
  assert.match(researchProjects, /提出研发假设/);
  assert.match(researchProjects, /设计验证实验/);
  assert.match(researchProjects, /记录实验计算结果/);
  assert.match(researchProjects, /calculatedFromSource: true/);
  assert.match(researchProjects, /supportingResultIds/);
  assert.match(researchProjects, /独立验证/);
  assert.match(researchProjects, /等待其他成员批准/);
  assert.doesNotMatch(researchProjects, /项目代码|指标代码|变量代码|AnalysisHash|GUID/);
});

test("research assets retain mechanism fusion, project-scoped knowledge, and dataset quality results", () => {
  assert.match(researchAssets, /\/api\/v1\/mechanism-models/);
  assert.match(researchAssets, /\/api\/v1\/mechanism-fusions/);
  assert.match(researchAssets, /\/api\/v1\/process-knowledge/);
  assert.match(researchAssets, /\/api\/v1\/dataset-quality-validations/);
  assert.match(researchAssets, /\/api\/v1\/training-datasets/);
  assert.match(researchAssets, /\/api\/v1\/process-models/);
  assert.match(researchAssets, /\/api\/v1\/research-projects\?limit=100/);
  assert.match(researchAssets, /encodeURIComponent\(projectId\)/);
  assert.match(researchAssets, /知识来源严格按研发项目隔离/);
});

test("event subscriptions retain create, edit, enable, signed-secret, and delete operations", () => {
  assert.match(pages, /新建订阅/);
  assert.match(pages, /putJson\(`\/api\/v1\/subscriptions\/\$\{editing\.subscriptionId\}`/);
  assert.match(pages, /\/enabled/);
  assert.match(pages, /clearSecret/);
  assert.match(pages, /HMAC-SHA256/);
  assert.match(pages, /deleteJson\(`\/api\/v1\/subscriptions/);
});

test("Chat renders structured answers, exposes progress and cancellation, and keeps recent history", () => {
  assert.match(pages, /\/api\/v1\/chat\/capabilities/);
  assert.match(pages, /\/api\/v1\/chat\/runs/);
  assert.match(pages, /streamSse/);
  assert.match(pages, /function ChatAnswer/);
  assert.match(pages, /answer\.summary/);
  assert.match(pages, /取消分析/);
  assert.match(pages, /最近问答/);
  assert.match(pages, /capabilitiesLoading/);
  assert.doesNotMatch(pages, /\{run\.answer\}<\/div>/);
  assert.match(http, /Last-Event-ID/);
  assert.match(http, /平台服务暂不可用/);
  assert.doesNotMatch(http, /PostgreSQL\/TimescaleDB|端口 8000/);
  assert.doesNotMatch(pages, /\/api\/v1\/agent/);
});
