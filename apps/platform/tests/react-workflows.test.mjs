import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const pages = await readFile(new URL("../src/pages/index.jsx", import.meta.url), "utf8");
const http = await readFile(new URL("../src/api/http.js", import.meta.url), "utf8");
const ui = await readFile(new URL("../src/ui/components.jsx", import.meta.url), "utf8");
const researchProjects = await readFile(new URL("../src/pages/ResearchProjectsPage.jsx", import.meta.url), "utf8");
const researchAssets = await readFile(new URL("../src/pages/ResearchAssetsPage.jsx", import.meta.url), "utf8");
const goldenQuestions = await readFile(new URL("../src/pages/GoldenQuestionsPage.jsx", import.meta.url), "utf8");

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

test("data health exposes reproducible reliability baselines and strict admission", () => {
  assert.match(pages, /\/api\/v1\/data-reliability\/baseline/);
  assert.match(pages, /process_data_completeness/);
  assert.match(pages, /actual_parameter_coverage/);
  assert.match(pages, /minimal_context_coverage/);
  assert.match(pages, /run_quality_association/);
  assert.match(pages, /analysis_admission/);
  assert.match(pages, /只认设备回读，不使用计划值/);
  assert.match(pages, /排除原因/);
  assert.match(pages, /上下文分层统计/);
  assert.match(pages, /因素重叠与混杂/);
  assert.match(pages, /unidentifiableConfoundingCount/);
  assert.match(pages, /完全混杂/);
  assert.match(pages, /时间、顺序与上送质量/);
  assert.match(pages, /maximumAbsoluteSourceClockOffsetMs/);
  assert.match(pages, /worstRunP95PlatformIngestLatencyMs/);
  assert.match(pages, /陈旧快照拒绝/);
  assert.match(pages, /跨设备批次编号/);
  assert.match(pages, /externalBatchRef/);
  assert.match(pages, /多个设备填写相同生产批次/);
  assert.match(researchProjects, /可跨节点多选/);
  assert.match(researchProjects, /cycle\.edgeIds/);
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
  assert.match(pages, /数据源交付情况/);
  assert.match(pages, /从设备数据到工艺证据/);
  assert.match(pages, /\/api\/v1\/acquisition-profiles/);
  assert.match(pages, /配方参数回读/);
  assert.match(pages, /周期边界映射/);
  assert.match(pages, /节点诊断日志/);
  assert.match(pages, /数据源尚未具备工艺闭环条件/);
});

test("workbench and logs use current response contracts without misleading placeholders", () => {
  assert.match(pages, /\/api\/v1\/cycles\?limit=8/);
  assert.match(pages, /\/api\/v1\/research-projects\?limit=100/);
  assert.match(pages, /从现场问题进入可验证决策/);
  assert.match(pages, /cycleOverview: cycles\.overview/);
  assert.match(pages, /state\.loading \? <LoadingCard \/>/);
  assert.match(pages, /logs\?pageSize=200/);
  assert.match(pages, /\{ key: "source", label: "来源"/);
  assert.doesNotMatch(pages, /keyField="id" columns=\{\[\s*\{ key: "timestamp"/);
  assert.match(ui, /not_applicable: "无需质检"/);
});

test("cycle comparison submits the selection contract and renders business results", () => {
  assert.match(pages, /new URLSearchParams\(\{ status: "completed", limit: "200" \}\)/);
  assert.match(pages, /query\.set\("search", search\)/);
  assert.match(pages, /exploratory: "探索性证据"/);
  assert.match(pages, /label="基准运行"/);
  assert.match(pages, /label="对比范围"/);
  assert.match(pages, /label="对比运行"/);
  assert.match(pages, /comparisonScope === "cohort"/);
  assert.match(pages, /cycle-comparisons\/\$\{encodeURIComponent\(baselineCycleId\)\}\?limit=24/);
  assert.match(pages, /cycleIds: \[baselineCycleId, candidate\]/);
  assert.match(pages, /correlationId=\$\{encodeURIComponent\(baseline\)\}&limit=1/);
  assert.match(pages, /title="周期概况"/);
  assert.match(pages, /title="质量候选原因"/);
  assert.match(pages, /title="确定性调查报告"/);
  assert.match(pages, /首次阶段偏离/);
  assert.match(pages, /反证与边界/);
  assert.match(pages, /下一步验证实验/);
  assert.match(pages, /result\?\.diagnosis\?\.candidates/);
  assert.match(pages, /实际配方/);
  assert.match(pages, /可直接实验/);
  assert.match(pages, /诊断边界/);
  assert.match(pages, /title="信号差异"/);
  assert.match(pages, /label="周期完整"/);
  assert.match(pages, /同时具有生产开始与结束事件/);
  assert.match(pages, /key: "lifecycleComplete", label: "周期边界"/);
  assert.doesNotMatch(pages, /label="阶段完整"|phaseCompleteCycleCount/);
  assert.doesNotMatch(pages, /JSON\.stringify\(result, null, 2\)/);
});

test("cycle detail presents actual recipe, source curves, phase features, and inspection measurements", () => {
  assert.match(pages, /\/api\/v1\/cycles\/\$\{encodedId\}\/analysis/);
  assert.match(pages, /title="实际执行配方"/);
  assert.match(pages, /title="全过程曲线"/);
  assert.match(pages, /processSignalTraces\(chartRun, samplesByRun, signal\.code\)/);
  assert.match(pages, /title="阶段特征"/);
  assert.match(pages, /阶段号用于过程对齐，不参与周期完整性判定/);
  assert.match(pages, /cycle\.lifecycleComplete/);
  assert.doesNotMatch(pages, /cycle\.phaseComplete|key: "isComplete", label: "状态"/);
  assert.match(pages, /keyField="recordId"/);
  assert.match(pages, /\{ key: "outcome", label: "判定"/);
  assert.match(pages, /测量值与规格/);
});

test("mechanism assets are presented as business fields instead of raw JSON", () => {
  assert.match(researchAssets, /title: "机理模型"/);
  assert.match(researchAssets, /key: "outputCode", label: "输出"/);
  assert.match(researchAssets, /title: "融合定义"/);
  assert.match(researchAssets, /key: "mode", label: "融合方式"/);
  assert.doesNotMatch(researchAssets, /JSON\.stringify|JSON\.parse/);
});

test("research projects expose the evidence-backed experiment and process-window workflow", () => {
  assert.match(researchProjects, /project-definition/);
  assert.match(researchProjects, /project-validation/);
  assert.match(researchProjects, /返回项目列表/);
  assert.match(researchProjects, /提出研发假设/);
  assert.match(researchProjects, /设计验证实验/);
  assert.match(researchProjects, /导入历史运行/);
  assert.match(researchProjects, /experiments\/import-history/);
  assert.match(researchProjects, /实际配方回读、过程特征和检验记录/);
  assert.match(researchProjects, /materialize-result/);
  assert.match(researchProjects, /立即检查数据回收/);
  assert.match(researchProjects, /采集和检验齐全后自动完成/);
  assert.match(researchProjects, /replicatesPerCondition: 2/);
  assert.match(researchProjects, /设备无关执行指令/);
  assert.match(researchProjects, /onClick=\{\(\) => onGenerateOptimizationSuggestions\(\)\}/);
  assert.match(researchProjects, /design-validation/);
  assert.match(researchProjects, /设计独立验证实验/);
  assert.match(researchProjects, /95% 效果区间/);
  assert.match(researchProjects, /baselineRunKeys/);
  assert.match(researchProjects, /独立对照运行（可选）/);
  assert.doesNotMatch(researchProjects, /calculatedFromSource: true/);
  assert.doesNotMatch(researchProjects, /记录实验计算结果/);
  assert.match(researchProjects, /审核独立验证结果/);
  assert.match(researchProjects, /发布生产/);
  assert.match(researchProjects, /等待其他成员批准/);
  assert.doesNotMatch(researchProjects, /项目代码|指标代码|变量代码|AnalysisHash|GUID/);
});

test("research project setup reuses configured industrial definitions instead of retyping identifiers", () => {
  assert.match(researchProjects, /\/api\/v1\/inspection-definitions/);
  assert.match(researchProjects, /\/api\/v1\/process-data-models/);
  assert.match(researchProjects, /\/api\/v1\/scenario-packages/);
  assert.match(researchProjects, /scenario_package/);
  assert.match(researchProjects, /label="质量指标"/);
  assert.match(researchProjects, /label="可控配方参数"/);
  assert.match(researchProjects, /选择质量指标后自动带入/);
  assert.match(researchProjects, /选择配方参数后自动带入/);
});

test("research projects turn optimization into the existing experiment workflow", () => {
  assert.match(researchProjects, /\/optimize/);
  assert.match(researchProjects, /batchSize:\s*2/);
  assert.match(researchProjects, /智能设计下一组实验/);
  assert.match(researchProjects, /现有流程审核后执行/);
  assert.doesNotMatch(researchProjects, /processProfile|optical-lens-molding-v1/);
  assert.doesNotMatch(researchProjects, /optimization-observations|optimization-suggestions/);
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

test("shadow recommendations preregister engineer choices and freeze source outcomes", () => {
  assert.match(researchProjects, /登记影子选择/);
  assert.match(researchProjects, /shadow-decision/);
  assert.match(researchProjects, /actualRunKey/);
  assert.match(researchProjects, /engineerSelectedFactors/);
  assert.match(researchProjects, /rejectionReason/);
  assert.match(researchProjects, /siteLimitations/);
  assert.match(researchProjects, /contextSnapshot/);
  assert.match(researchProjects, /materialize-outcome/);
  assert.match(researchProjects, /模型建议不会下发设备/);
  assert.match(researchProjects, /实际设置偏差/);
  assert.match(researchProjects, /影子评估触发停止信号/);
  assert.match(researchProjects, /预测区间覆盖/);
  assert.match(researchProjects, /上下文变化/);
  assert.match(researchProjects, /参数外推/);
  assert.match(researchProjects, /安全事件/);
});

test("historical replay reports preserve production-equivalent comparisons and failures", () => {
  assert.match(researchProjects, /运行历史回放/);
  assert.match(researchProjects, /historical-replays/);
  assert.match(researchProjects, /生产等价历史回放/);
  assert.match(researchProjects, /历史原顺序/);
  assert.match(researchProjects, /优化器中位数/);
  assert.match(researchProjects, /随机中位数/);
  assert.match(researchProjects, /优化器安全违规/);
  assert.match(researchProjects, /失败与限制/);
  assert.match(researchProjects, /审核完整报告/);
});

test("controlled online suggestions fail closed and require a frozen engineer decision", () => {
  assert.match(researchProjects, /\/online-admission/);
  assert.match(researchProjects, /mode === "controlled"/);
  assert.match(researchProjects, /batchSize: 1, replicatesPerCondition: 1/);
  assert.match(researchProjects, /controlled-decision/);
  assert.match(researchProjects, /接受 \/ 修改 \/ 拒绝/);
  assert.match(researchProjects, /这不是自动控制命令/);
  assert.match(researchProjects, /任何门禁失败均按失败关闭/);
  assert.match(researchProjects, /建议值和工程师批准值均已保留/);
  assert.match(researchProjects, /rollback-drills/);
  assert.match(researchProjects, /纸面回退方案不能放行受控在线/);
  assert.match(researchProjects, /证据 SHA-256/);
  assert.match(researchProjects, /停止与回退演练已由另一名工程师复核并冻结/);
  assert.match(researchProjects, /受控在线监控/);
  assert.match(researchProjects, /已停止生成下一条建议/);
  assert.match(researchProjects, /在线与影子残差的差异只作为停止与复核信号/);
});

test("transfer evaluation compares target evidence with a cold-start control and exposes negative transfer", () => {
  assert.match(researchProjects, /\/transfer-sources/);
  assert.match(researchProjects, /\/transfer-assessments/);
  assert.match(researchProjects, /迁移组实测结果/);
  assert.match(researchProjects, /从零对照组实测结果/);
  assert.match(researchProjects, /相对从零收益/);
  assert.match(researchProjects, /检测到负迁移/);
  assert.match(researchProjects, /不会向设备下发源参数/);
  assert.match(researchProjects, /单次有收益也不能直接沉淀为通用知识/);
  assert.match(researchProjects, /transferAssessmentId/);
});

test("engineer golden questions freeze reviewed evidence and evaluate actual agent runs", () => {
  const page = goldenQuestions;
  assert.match(page, /录入真实问题/);
  assert.match(page, /expectedFacts/);
  assert.match(page, /expectedRecordReferences/);
  assert.match(page, /:review/);
  assert.match(page, /:evaluate/);
  assert.match(page, /Agent 运行 ID/);
  assert.doesNotMatch(page, /JSON\.stringify\(form/);
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
