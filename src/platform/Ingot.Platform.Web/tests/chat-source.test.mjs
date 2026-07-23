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
const workbench = await readFile(new URL("../src/views/WorkbenchView.vue", import.meta.url), "utf8");
const explorer = await readFile(new URL("../src/views/ObjectExplorerView.vue", import.meta.url), "utf8");
const qualityAnalysis = await readFile(new URL("../src/views/QualityAnalysisView.vue", import.meta.url), "utf8");
const subscriptions = await readFile(new URL("../src/views/SubscriptionsView.vue", import.meta.url), "utf8");
const acquisition = await readFile(new URL("../src/views/AcquisitionProfilesView.vue", import.meta.url), "utf8");

async function readSources(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  return (await Promise.all(entries.map(async (entry) => {
    const url = new URL(`${entry.name}${entry.isDirectory() ? "/" : ""}`, directory);
    return entry.isDirectory() ? readSources(url) : readFile(url, "utf8");
  }))).flat();
}

test("the operational workbench is primary and AI remains a first-class entry", () => {
  assert.match(app, /path: "\/chat"/);
  assert.match(app, /AI 助手/);
  assert.match(app, /path: "\/workbench"/);
  assert.match(router, /redirect: "\/workbench"/);
  assert.match(router, /WorkbenchView\.vue/);
  assert.match(workbench, /需要关注/);
  assert.match(workbench, /当前生产配置/);
  assert.match(workbench, /最近运行记录/);
  assert.match(workbench, /\/api\/v1\/inspection-tasks\/summary/);
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
  assert.match(app, /class="global-nav"/);
  assert.match(app, /class="context-sidebar"/);
  assert.match(app, /运行与追溯/);
  assert.match(app, /质量管理/);
  assert.match(app, /分析中心/);
  assert.match(app, /数据资产/);
  assert.match(app, /currentSection/);
  assert.match(app, /质量任务/);
  assert.match(router, /ObjectExplorerView\.vue/);
  assert.match(explorer, /运行对象/);
  assert.match(explorer, /关联关系/);
  assert.doesNotMatch(app, /<template #title>检测录入<\/template>/);
});

test("quality inspection is cycle-linked and original images remain reviewable", () => {
  assert.match(events, /进入质量检验/);
  assert.match(events, /历史对比/);
  assert.match(events, /operationRunId: result\.correlationId/);
  assert.match(inspections, /\/api\/v1\/inspection-tasks/);
  assert.match(inspections, /contextLocked/);
  assert.match(inspections, /placeholder="从待检任务带入" readonly/);
  assert.match(inspections, /打开原图/);
  assert.match(inspections, /inspection-attachments\/\$\{item\.attachmentId\}\/content/);
  assert.match(inspections, /\/api\/v1\/inspection-reviews/);
  assert.match(inspections, /reviewForm/);
  assert.match(inspections, /v-model="entryVisible"/);
  assert.match(inspections, /配置质量方案/);
  assert.doesNotMatch(inspections, /解除周期关联|submittedBy/);
  assert.doesNotMatch(events, /slice\(0, 500\)/);
});

test("production cycles and data quality are first-class configurable pages", () => {
  assert.match(app, /path: "\/cycles"/);
  assert.match(app, /path: "\/data-quality"/);
  assert.match(app, /生产事件/);
  assert.match(router, /CyclesView\.vue/);
  assert.match(router, /DataQualityView\.vue/);
  assert.match(cycles, /\/api\/v1\/cycles/);
  assert.match(cycles, /sampleCompleteness/);
  assert.match(cycles, /requiredPhaseCount/);
  assert.match(cycles, /qualityStatus/);
  assert.match(dataQuality, /dataIssues/);
  assert.match(dataQuality, /\/api\/v1\/data-objects/);
  assert.match(dataQuality, /运行对象/);
  assert.doesNotMatch(cycles, /upper_mold|lower_mold|press\.load|chamber\.vacuum|servo\.position/);
});

test("historical comparison uses the full same-series cycle trace", () => {
  assert.match(app, /path: "\/comparisons"/);
  assert.match(app, /历史对比/);
  assert.match(router, /path: "\/comparisons"/);
  assert.match(router, /CycleComparisonView\.vue/);
  assert.match(comparisons, /postJson\("\/api\/v1\/cycle-comparisons"/);
  assert.match(comparisons, /postJson\("\/api\/v1\/process-window-comparisons"/);
  assert.match(comparisons, /v-model="selectedCycleIds"/);
  assert.match(comparisons, /multiple/);
  assert.match(comparisons, /baselineCycleId: baselineCycleId\.value/);
  assert.match(comparisons, /采样完整率/);
  assert.match(comparisons, /阶段完整/);
  assert.match(comparisons, /sampleCompleteness/);
  assert.match(comparisons, /cycle\.signals/);
  assert.match(comparisons, /class="signal-list"/);
  assert.match(comparisons, /selectedSignalRows/);
  assert.match(comparisons, /class="baseline-signal-card"/);
  assert.match(comparisons, /class="baseline-cycle-card"/);
  assert.match(comparisons, /comparisonSignalRows/);
  assert.match(comparisons, /comparisonCycles/);
  assert.match(comparisons, /相对基准/);
  assert.doesNotMatch(comparisons, /<el-table-column\s+v-for="cycle in comparisonRows"/);
  assert.doesNotMatch(comparisons, /upper_mold|lower_mold|press\.load|chamber\.vacuum|servo\.position/);
});

test("event subscriptions have complete create, edit, enable, and delete maintenance", () => {
  assert.match(app, /path: "\/subscriptions"/);
  assert.match(subscriptions, /openCreate/);
  assert.match(subscriptions, /openEdit\(row\)/);
  assert.match(subscriptions, /putJson\(`\/api\/v1\/subscriptions\/\$\{editingId\.value\}`/);
  assert.match(subscriptions, /setEnabled/);
  assert.match(subscriptions, /deleteJson/);
  assert.match(subscriptions, /留空保留现有密钥/);
});

test("all product controls used by the views are registered", () => {
  assert.match(comparisons, /import \{ ElDatePicker, ElRadio \} from "element-plus"/);
  assert.match(subscriptions, /ElCheckbox/);
  assert.match(cycles, /ElDatePicker, ElDescriptions, ElDescriptionsItem, ElProgress/);
  assert.match(dataQuality, /ElDatePicker/);
  assert.match(inspections, /ElDatePicker, ElMessage, ElMessageBox/);
  assert.match(acquisition, /ElCheckbox, ElMessage, ElMessageBox/);
});

test("quality requirements are configured by versioned plans", () => {
  assert.match(app, /path: "\/configuration\/quality-plans"/);
  assert.match(router, /path: "\/configuration\/quality-plans"/);
  assert.match(router, /QualityPlansView\.vue/);
  assert.match(qualityPlans, /\/api\/v1\/inspection-plans/);
  assert.match(qualityPlans, /适用范围/);
  assert.match(qualityPlans, /必检项目/);
  assert.match(qualityPlans, /requiresAttachment/);
  assert.match(qualityPlans, /requiresReview/);
  assert.match(qualityPlans, /创建新版本/);
});

test("quality is a first-class analysis domain joined to operating context", () => {
  assert.match(app, /path: "\/quality-analysis"/);
  assert.match(router, /QualityAnalysisView\.vue/);
  assert.match(qualityAnalysis, /按产品系列/);
  assert.match(qualityAnalysis, /按配方版本/);
  assert.match(qualityAnalysis, /row\.outcome/);
  assert.match(qualityAnalysis, /\/api\/v1\/quality-analysis/);
  assert.match(qualityAnalysis, /analysisScopeType/);
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
  assert.match(view, /删除对话/);
  assert.match(view, /deleteJson\(`\/api\/v1\/chat\/runs\/\$\{item\.runId\}`\)/);
  assert.match(view, /conversation-stream/);
  assert.match(view, /composer-card/);
  assert.match(view, /el-drawer/);
  assert.doesNotMatch(view, /<el-(?:row|col)(?:\s|>)/);
  assert.doesNotMatch(view, /roleLabel|usageText|stageLabel/);
  assert.match(http, /Platform API 暂不可用/);
  assert.match(http, /PostgreSQL\/TimescaleDB/);
});

test("data ingress is presented as user-owned collection nodes", () => {
  assert.match(app, /采集节点/);
  assert.match(edges, /window\.setInterval\(\(\) => load\(\{ silent: true \}\), 15000\)/);
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
