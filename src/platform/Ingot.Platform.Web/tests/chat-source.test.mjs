import assert from "node:assert/strict";
import { readFile, readdir } from "node:fs/promises";
import test from "node:test";

const sourceRoot = new URL("../src/", import.meta.url);
const view = await readFile(new URL("../src/views/ChatView.vue", import.meta.url), "utf8");
const http = await readFile(new URL("../src/api/http.js", import.meta.url), "utf8");
const app = await readFile(new URL("../src/App.vue", import.meta.url), "utf8");
const router = await readFile(new URL("../src/router/index.js", import.meta.url), "utf8");
const edges = await readFile(new URL("../src/views/EdgesView.vue", import.meta.url), "utf8");
const inspections = await readFile(new URL("../src/views/InspectionsView.vue", import.meta.url), "utf8");
const events = await readFile(new URL("../src/views/EventsView.vue", import.meta.url), "utf8");
const comparisons = await readFile(new URL("../src/views/CycleComparisonView.vue", import.meta.url), "utf8");
const qualityPlans = await readFile(new URL("../src/views/QualityPlansView.vue", import.meta.url), "utf8");
const cycles = await readFile(new URL("../src/views/CyclesView.vue", import.meta.url), "utf8");
const dataQuality = await readFile(new URL("../src/views/DataQualityView.vue", import.meta.url), "utf8");

async function readSources(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  return (await Promise.all(entries.map(async (entry) => {
    const url = new URL(`${entry.name}${entry.isDirectory() ? "/" : ""}`, directory);
    return entry.isDirectory() ? readSources(url) : readFile(url, "utf8");
  }))).flat();
}

test("Ingot Chat is the primary production-record dialogue entry", () => {
  assert.match(app, /index="\/chat"/);
  assert.match(app, /Ingot Chat/);
  assert.match(router, /redirect: "\/chat"/);
  assert.match(router, /path: "\/chat"/);
  assert.match(router, /ChatView\.vue/);
  assert.match(view, /Ingot Chat/);
  assert.match(view, /生产数据/);
  assert.match(view, /查看相关生产记录/);
  assert.match(view, /\/api\/v1\/chat\/capabilities/);
  assert.match(view, /\/api\/v1\/chat\/runs\?\$\{query\}/);
  assert.match(view, /onMounted\(loadChat\)/);
  assert.doesNotMatch(view, /访问密码|v-model="form\.user"|v-model="form\.token"|Authorization:\s*`Bearer|X-Ingot-User/);
  assert.doesNotMatch(view, /Chat 工艺分析|多角色调查|深度协作调查|模型用量|角色状态/);
  assert.match(app, /mode="vertical"|sidebar-menu/);
  assert.match(app, /日常工作/);
  assert.match(app, /配置管理/);
  assert.match(app, /系统运维/);
  assert.match(app, /sidebarCollapsed/);
  assert.doesNotMatch(app, /mode="horizontal"/);
  assert.match(app, /质量检验/);
  assert.match(app, /分析与治理/);
  assert.doesNotMatch(app, /<template #title>检测录入<\/template>/);
});

test("quality inspection is cycle-linked and original images remain reviewable", () => {
  assert.match(events, /进入质量检验/);
  assert.match(events, /历史对比/);
  assert.match(events, /operationRunId: result\.correlationId/);
  assert.match(inspections, /\/api\/v1\/inspection-tasks/);
  assert.match(inspections, /已关联生产周期/);
  assert.match(inspections, /打开原图/);
  assert.match(inspections, /inspection-attachments\/\$\{item\.attachmentId\}\/content/);
  assert.match(inspections, /\/api\/v1\/inspection-reviews/);
  assert.match(inspections, /复核结论/);
  assert.match(inspections, /v-model="entryVisible"/);
  assert.match(inspections, /配置质量方案/);
  assert.doesNotMatch(inspections, /解除周期关联|submittedBy/);
  assert.doesNotMatch(events, /slice\(0, 500\)/);
});

test("production cycles and data quality are first-class configurable pages", () => {
  assert.match(app, /index="\/cycles"/);
  assert.match(app, /index="\/data-quality"/);
  assert.match(app, /事件查询/);
  assert.match(router, /CyclesView\.vue/);
  assert.match(router, /DataQualityView\.vue/);
  assert.match(cycles, /\/api\/v1\/cycles/);
  assert.match(cycles, /sampleCompleteness/);
  assert.match(cycles, /requiredPhaseCount/);
  assert.match(cycles, /qualityStatus/);
  assert.match(dataQuality, /dataIssues/);
  assert.match(dataQuality, /阶段映射/);
  assert.doesNotMatch(cycles, /upper_mold|lower_mold|press\.load|chamber\.vacuum|servo\.position/);
});

test("historical comparison uses the full same-series cycle trace", () => {
  assert.match(app, /index="\/comparisons"/);
  assert.match(app, /历史对比/);
  assert.match(router, /path: "\/comparisons"/);
  assert.match(router, /CycleComparisonView\.vue/);
  assert.match(comparisons, /\/api\/v1\/cycle-comparisons\/\$\{encodeURIComponent\(baselineCycleId\.value\.trim\(\)\)\}/);
  assert.match(comparisons, /采样完整率/);
  assert.match(comparisons, /阶段完整/);
  assert.match(comparisons, /sampleCompleteness/);
  assert.match(comparisons, /row\.signals/);
  assert.doesNotMatch(comparisons, /upper_mold|lower_mold|press\.load|chamber\.vacuum|servo\.position/);
});

test("quality requirements are configured by versioned plans", () => {
  assert.match(app, /index="\/quality-plans"/);
  assert.match(router, /path: "\/quality-plans"/);
  assert.match(router, /QualityPlansView\.vue/);
  assert.match(qualityPlans, /\/api\/v1\/inspection-plans/);
  assert.match(qualityPlans, /适用范围/);
  assert.match(qualityPlans, /必检项目/);
  assert.match(qualityPlans, /requiresAttachment/);
  assert.match(qualityPlans, /requiresReview/);
  assert.match(qualityPlans, /创建新版本/);
});

test("Ingot Chat resumes SSE and renders record queries, relatedRecords, and charts", () => {
  assert.match(http, /Last-Event-ID/);
  assert.match(view, /lastEventId/);
  assert.match(view, /discussion\.participant_failed/);
  assert.match(view, /调查说明/);
  assert.match(view, /生产记录查询/);
  assert.match(view, /查看相关生产记录/);
  assert.match(view, /snapshot\?\.answer\?\.charts\?\.length/);
  assert.match(view, /对话记录/);
  assert.match(view, /conversation-stream/);
  assert.match(view, /composer-card/);
  assert.match(view, /el-drawer/);
  assert.doesNotMatch(view, /<el-(?:row|col)(?:\s|>)/);
  assert.doesNotMatch(view, /roleLabel|usageText|stageLabel/);
});

test("data ingress is presented as user-owned adapter status", () => {
  assert.match(app, /数据接入节点/);
  assert.match(edges, /用户自行部署的数据适配器接入状态/);
  assert.match(edges, /数据适配器地址/);
  assert.doesNotMatch(edges, /生成连接器|连接器打包|连接器源码工作区/);
});

test("public source is Chat-only and has no desktop, code-generation, or Agent product surface", async () => {
  const source = (await readSources(sourceRoot)).join("\n");
  assert.doesNotMatch(source, /\/api\/v1\/agent(?:\/|\b)/);
  assert.doesNotMatch(source, /\/api\/v1\/agent\/details/);
  assert.doesNotMatch(source, /\/api\/v1\/connector-workspaces/);
  assert.doesNotMatch(source, /approve-package/);
  assert.doesNotMatch(source, /下载连接器包|生成连接器包|连接器源码工作区/);
  assert.doesNotMatch(source, /桌面(?:端|版)?|代码生成|生成代码|\b[Aa]gent\b/);
  assert.doesNotMatch(router, /path: "\/agent"/);
  assert.doesNotMatch(app, /连接器工程 Agent/);
});
