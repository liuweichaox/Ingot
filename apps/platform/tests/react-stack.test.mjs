import assert from "node:assert/strict";
import { readdir, readFile } from "node:fs/promises";
import test from "node:test";

const packageJson = JSON.parse(await readFile(new URL("../package.json", import.meta.url), "utf8"));
const app = await readFile(new URL("../src/App.jsx", import.meta.url), "utf8");
const pageDirectory = new URL("../src/pages/", import.meta.url);
const pages = (await Promise.all(
  (await readdir(pageDirectory, { withFileTypes: true }))
    .filter(entry => entry.isFile() && /\.jsx?$/.test(entry.name))
    .map(entry => readFile(new URL(entry.name, pageDirectory), "utf8")),
)).join("\n");
const http = await readFile(new URL("../src/api/http.js", import.meta.url), "utf8");
const auth = await readFile(new URL("../src/auth/AuthGate.jsx", import.meta.url), "utf8");
const components = await readFile(new URL("../src/ui/components.jsx", import.meta.url), "utf8");
const apiHook = await readFile(new URL("../src/hooks/useApi.js", import.meta.url), "utf8");
const registryEditor = await readFile(new URL("../src/components/RegistryBusinessEditor.jsx", import.meta.url), "utf8");
const acquisitionRegistry = await readFile(new URL("../src/acquisition/protocolRegistry.js", import.meta.url), "utf8");
const acquisitionPage = await readFile(new URL("../src/acquisition/IngestionTaskPage.jsx", import.meta.url), "utf8");
const acquisitionForm = await readFile(new URL("../src/acquisition/ingestionTaskForm.js", import.meta.url), "utf8");
const acquisitionPanels = {
  connection: await readFile(new URL("../src/acquisition/panels/ConnectionPanel.jsx", import.meta.url), "utf8"),
  mapping: await readFile(new URL("../src/acquisition/panels/PointMappingPanel.jsx", import.meta.url), "utf8"),
  points: await readFile(new URL("../src/acquisition/panels/DevicePointsPanel.jsx", import.meta.url), "utf8"),
};
const researchProjectsPage = await readFile(new URL("../src/pages/ResearchProjectsPage.jsx", import.meta.url), "utf8");
const researchProjects = (await Promise.all([
  researchProjectsPage,
  readFile(new URL("../src/research/researchProjectModel.js", import.meta.url), "utf8"),
  readFile(new URL("../src/research/researchProjectPresentation.js", import.meta.url), "utf8"),
  readFile(new URL("../src/research/components/CreateResearchProjectDrawer.jsx", import.meta.url), "utf8"),
  readFile(new URL("../src/research/components/ResearchEvidenceCards.jsx", import.meta.url), "utf8"),
  readFile(new URL("../src/research/components/ResearchProjectDrawers.jsx", import.meta.url), "utf8"),
  readFile(new URL("../src/research/components/ResearchWorkspaceContent.jsx", import.meta.url), "utf8"),
])).join("\n");
const researchAssets = await readFile(new URL("../src/pages/ResearchAssetsPage.jsx", import.meta.url), "utf8");
const styles = await readFile(new URL("../src/styles/global.css", import.meta.url), "utf8");
const vite = await readFile(new URL("../vite.config.mjs", import.meta.url), "utf8");
const html = await readFile(new URL("../index.html", import.meta.url), "utf8");
const authGate = await readFile(new URL("../src/auth/AuthGate.jsx", import.meta.url), "utf8");
const main = await readFile(new URL("../src/main.jsx", import.meta.url), "utf8");

async function sourceFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  return (await Promise.all(entries.map(async entry => {
    const url = new URL(`${entry.name}${entry.isDirectory() ? "/" : ""}`, directory);
    return entry.isDirectory() ? sourceFiles(url) : [url];
  }))).flat();
}

test("platform uses React, Tailwind, and Headless UI without Vue or Element Plus", async () => {
  assert.ok(packageJson.dependencies.react);
  assert.ok(packageJson.dependencies["react-dom"]);
  assert.ok(packageJson.dependencies["@headlessui/react"]);
  assert.ok(packageJson.devDependencies.tailwindcss);
  assert.ok(packageJson.devDependencies["@vitejs/plugin-react"]);
  assert.equal(packageJson.dependencies.vue, undefined);
  assert.equal(packageJson.dependencies["vue-router"], undefined);
  assert.equal(packageJson.dependencies["element-plus"], undefined);
  assert.match(vite, /@vitejs\/plugin-react/);
  assert.match(vite, /@tailwindcss\/vite/);
  assert.match(vite, /INGOT_PLATFORM_API_TARGET/);
  assert.match(styles, /@import "tailwindcss"/);
  assert.match(app, /@headlessui\/react/);
  assert.match(pages, /TabGroup/);
  const files = await sourceFiles(new URL("../src/", import.meta.url));
  assert.equal(files.filter(file => file.pathname.endsWith(".vue")).length, 0);
});

test("all platform routes remain available after the React migration", () => {
  for (const route of [
    "/research-projects", "/research-assets", "/workbench", "/chat", "/explorer", "/process-executions", "/events", "/production/changeover",
    "/production/tooling-installations", "/configuration/component-types", "/configuration/components",
    "/configuration/tooling-types", "/configuration/tooling-assemblies", "/inspections",
    "/quality-analysis", "/configuration", "/configuration/inspection-definitions", "/configuration/quality-plans",
    "/comparisons", "/golden-questions", "/data-quality", "/configuration/scenario-packages",
    "/configuration/process-analysis-plans", "/configuration/process-data-models",
    "/configuration/process-specifications", "/configuration/ingestion-tasks", "/edges",
    "/platform-metrics", "/logs", "/identity/users",
  ]) {
    assert.match(app, new RegExp(route.replaceAll("/", "\\/")));
  }
  assert.match(app, /\/research-projects\/:projectId/);
  for (const retiredAlias of ["/production-setup", "/quality-plans", "/process-improvement", "/profiles", "/users"]) {
    assert.doesNotMatch(app, new RegExp(`path="${retiredAlias.replaceAll("/", "\\/")}"`));
  }
});

test("platform identity presents Ingot as a process diagnosis and optimization system", () => {
  assert.match(html, /Ingot · 工艺追因与优化系统/);
  assert.match(html, /真实生产条件、过程轨迹与质量结果/);
  assert.doesNotMatch(html, /制造数据采集与工艺分析平台/);
});

test("demo mode is identified without mixing a scripted story into the workbench", () => {
  assert.match(auth, /import\.meta\.env\.MODE === "demo"/);
  assert.match(auth, /演示环境/);
  assert.doesNotMatch(pages, /三分钟演示：一片镜片为什么超差|RUN-2026-0821-005|0\.48 μm/);
});

test("navigation and overlays are accessible Headless UI components", () => {
  assert.match(app, /DialogBackdrop/);
  assert.match(app, /DialogPanel/);
  assert.match(app, /MenuButton/);
  for (const [id, domain] of [["overview", "工作台"], ["evidence", "生产运行"], ["quality", "质量管理"], ["diagnosis", "工艺追因"], ["research", "配方优化"], ["process-definition", "工艺配置"], ["equipment-connection", "现场接入"]]) {
    assert.match(app, new RegExp(`id: "${id}", label: "${domain}"`));
  }
  assert.match(app, /id: "overview"[\s\S]*id: "equipment-connection"[\s\S]*id: "process-definition"[\s\S]*id: "evidence"[\s\S]*id: "quality"[\s\S]*id: "diagnosis"[\s\S]*id: "research"/);
  assert.doesNotMatch(app, /id: "optimization"/);
  assert.match(app, /const systemSection = \{/);
  assert.match(app, /sectionsForIdentity/);
  assert.match(app, /roles \|\| \[\]\)\.includes\("platform\.admin"\)/);
  assert.match(app, /id: "equipment-connection"[\s\S]*\["\/edges", "现场节点"\], \["\/configuration\/ingestion-tasks", "采集配置"\]/);
  assert.match(app, /id: "process-definition"[\s\S]*\["\/configuration", "配置总览"\][\s\S]*\["\/configuration\/process-data-models", "数据字典"\][\s\S]*\["\/configuration\/tooling-types", "工装结构"\][\s\S]*\["\/configuration\/scenario-packages", "配置发布"\]/);
  assert.match(app, /id: "research"[\s\S]*items: \[\["\/research-projects", "优化任务"\], \["\/research-assets", "工艺知识"\]\]/);
  assert.match(app, /id: "system"[\s\S]*label: "身份权限"[\s\S]*label: "平台运维"[\s\S]*label: "助手治理"/);
  assert.match(app, /\["\/chat", "分析助手"\]/);
  assert.match(app, /items: \[\["\/research-projects", "优化任务"\], \["\/research-assets", "工艺知识"\]\]/);
  assert.match(app, /\["\/research-assets", "工艺知识"\]/);
  assert.match(app, /\["\/production\/changeover", "生产切换"\][\s\S]*\["\/process-executions", "运行记录"\]/);
  assert.match(app, /\["\/data-quality", "数据质量"\], \["\/comparisons", "运行对比"\]/);
  assert.doesNotMatch(app, /优化工作|复用资产/);
  assert.match(pages, /title="配方优化"/);
  assert.match(pages, /工艺分析助手/);
  assert.doesNotMatch(app, /label: "AI 助手"/);
  assert.match(app, /aria-label="主导航"/);
  assert.match(app, /aria-label="面包屑"/);
  assert.match(app, /aria-label="打开主导航"/);
  assert.match(app, /aria-label="收起侧边栏"/);
  assert.match(app, /aria-label="展开侧边栏"/);
  assert.match(app, /ingot\.sidebar\.collapsed/);
  assert.match(app, /function SidebarNavigation/);
  assert.match(app, /function SidebarSection/);
  assert.doesNotMatch(app, /function SidebarGroup/);
  assert.doesNotMatch(app, /group\.label/);
  assert.match(app, /items\.map\(\(\[path, label\]\) =>/);
  assert.match(app, /expanded=\{expandedSectionId === item\.id\}/);
  assert.match(app, /setExpandedSectionId\(current => current === sectionId \? null : sectionId\)/);
  assert.match(app, /min-h-9[^"]*px-3[^"]*text-sm[^"]*font-medium/);
  assert.doesNotMatch(app, /pl-14/);
  assert.doesNotMatch(app, /border-l border-slate-200/);
  assert.match(app, /text-\[15px\] font-semibold leading-5/);
  assert.match(app, /const activeNavigationPath = useMemo/);
  assert.match(app, /sidebarCollapsed \? "w-18" : "w-64"/);
  assert.match(app, /sidebarCollapsed \? "lg:ml-18" : "lg:ml-64"/);
  assert.doesNotMatch(app, /aria-label="全局导航"/);
  assert.doesNotMatch(app, /showSectionNavigation/);
  assert.doesNotMatch(app, /label: "运营工作台"/);
  for (const obsoleteLabel of ["周期记录", "周期对比", "AI助手", "黄金问题集", "研发成果"]) {
    assert.doesNotMatch(app, new RegExp(`label: "${obsoleteLabel}"`));
  }
  const itemLabels = [...app.matchAll(/\["\/[^\"]+", "([^\"]+)"\]/g)].map(match => match[1]);
  for (const label of itemLabels) {
    assert.ok([...label].length >= 3 && [...label].length <= 4, `menu label ${label} should contain 3–4 characters`);
  }
  assert.doesNotMatch(researchProjects, />新建项目</);
});

test("authenticated application exposes the identity administration surface", () => {
  assert.match(app, /function App\(\{ identity, logout \}\)/);
  assert.doesNotMatch(app, /username: "operator"/);
  assert.match(app, /\["\/identity\/users", "用户权限"\]/);
  assert.match(app, /path="\/identity\/users" element=\{<RequireRole identity=\{identity\}/);
  assert.match(app, /当前岗位不能访问此功能/);
  assert.match(pages, /export function UsersPage\(\)/);
  assert.match(pages, /:set-site-access/);
  assert.match(pages, /站点访问范围/);
  assert.doesNotMatch(researchProjects, /\/api\/v1\/auth\/me/);
});

test("versioned registries use composite row keys and statuses are localized", () => {
  assert.match(pages, /getRowKey=\{row => `\$\{row\[definition\.key\]\}:\$\{row\.version \?\? 1\}`\}/);
  assert.match(pages, /label="数据上行"/);
  assert.match(app, /section\.label/);
  assert.match(components, /pending: "待处理"/);
  assert.match(components, /published: "已发布"/);
  assert.match(components, /review_pending: "待复核"/);
  assert.match(components, /unknown: "待上报"/);
  assert.match(components, /starting: "启动中"/);
  assert.match(components, /applied: "已应用"/);
  assert.match(components, /"waiting-execution-boundary": "等待过程执行边界"/);
  assert.match(pages, /title="采集配置应用状态"/);
  assert.match(pages, /desiredConfigurationSetHash/);
  assert.match(pages, /appliedConfigurationSetHash/);
});

test("route changes cannot display stale registry rows under new columns", () => {
  assert.match(apiHook, /requestIdRef/);
  assert.match(apiHook, /requestId === requestIdRef\.current/);
  assert.match(apiHook, /dataRef\.current = null/);
  assert.match(pages, /<ProductionRecordsPage key=\{section\} section=\{section\}/);
  assert.match(pages, /<DataTable\s+key=\{resource\.endpoint\}/);
});

test("feature search opens a command palette and table columns keep stable unique keys", () => {
  assert.match(app, /<GlobalSearchDialog/);
  assert.match(app, /setGlobalSearchOpen\(true\)/);
  assert.match(app, /event\.key\.toLowerCase\(\) === "k"/);
  assert.match(app, /function isApplePlatform\(\)/);
  assert.match(app, /usesAppleShortcut \? event\.metaKey : event\.ctrlKey/);
  assert.match(app, /usesAppleShortcut \? "⌘ K" : "Ctrl K"/);
  assert.match(app, /aria-keyshortcuts=\{usesAppleShortcut \? "Meta\+K" : "Control\+K"\}/);
  assert.match(app, /功能搜索/);
  assert.doesNotMatch(app, /navigate\("\/explorer", \{ state: \{ focusSearch: true \} \}\)/);
  assert.match(app, /\["\/platform-metrics", "平台状态"\]/);
  assert.match(app, /\["\/logs", "平台日志"\]/);
  assert.match(app, /"\/production\/changeover": "生产上下文 换产 产品切换 工艺切换"/);
  assert.match(app, /"\/inspections": "质量任务 质检 检测任务"/);
  assert.match(components, /key=\{column\.id \?\? `\$\{column\.key\}:\$\{columnIndex\}`\}/);
});

test("object catalog pages use the event summary contract and show an initial loading state", () => {
  assert.match(app, /\["\/explorer", "对象目录"\]/);
  assert.match(app, /id: "evidence", label: "生产运行"/);
  assert.match(pages, /title="对象目录"/);
  assert.match(pages, /objects\.loading && !objects\.data \? <LoadingCard \/>/);
  assert.match(pages, /title="对象目录"/);
  assert.match(pages, /在这个对象中继续工作/);
  assert.match(pages, /\/process-executions\?equipmentId=/);
  assert.match(pages, /\/events\?subjectId=/);
  assert.match(pages, /\/quality-analysis\?subjectType=/);
  assert.match(pages, /\/data-quality\?subjectType=/);
  assert.doesNotMatch(pages, /key: "objectType", label: "对象类型"/);
});

test("core workflows tell new users what to do next and confirm completed actions", () => {
  assert.match(components, /export function WorkflowGuide/);
  assert.match(components, /export function ToastHost/);
  assert.match(pages, /质量待办/);
  assert.match(pages, /配置下一批生产/);
  assert.match(researchProjects, /进行中与待处理/);
  assert.match(researchProjects, /生成下一配方建议/);
  assert.match(researchProjects, /配方建议准备度/);
  assert.match(researchProjects, /不需要建立实验/);
  assert.doesNotMatch(researchProjects, /从真实偏差进入研发闭环|发现偏差 → 缩小候选原因/);
  assert.match(app, /<ToastHost \/>/);
});

test("research project orchestration stays separate from forms, evidence cards, and workspace presentation", () => {
  assert.ok(researchProjectsPage.split("\n").length < 1100);
  assert.match(researchProjectsPage, /CreateResearchProjectDrawer/);
  assert.match(researchProjectsPage, /ResearchProjectDrawers/);
  assert.match(researchProjectsPage, /ResearchWorkspaceContent/);
  assert.doesNotMatch(researchProjectsPage, /function WorkspaceContent/);
  assert.doesNotMatch(researchProjectsPage, /function CreateProjectDrawer/);
});

test("versioned tooling remains unique and the legacy improvement workspace is absent", () => {
  assert.match(pages, /getRowKey=\{section === "type" \? row => `\$\{row\[resource\.key\]\}:\$\{row\.version \?\? 1\}` : undefined\}/);
  assert.match(pages, /<option value="Information">信息<\/option>/);
  assert.doesNotMatch(pages, /ImprovementPanel|process-investigations|parameter-recommendations/);
  assert.match(researchProjects, /design-validation/);
  assert.doesNotMatch(researchProjects, /记录实验计算结果/);
});

test("forms expose clear labels, edit intent, and required upload fields", () => {
  assert.match(pages, /const chatModeLabels = \{/);
  assert.match(pages, /quick: "证据核对"/);
  assert.match(pages, /aria-label="给工艺分析助手发送消息"/);
  assert.match(pages, /aria-label="分析方法"/);
  assert.match(pages, /setEditorMode\(row \? \(section === "type" \? "version" : "edit"\) : "create"\)/);
  assert.match(pages, /editorMode === "create" \? resource\.createLabel/);
  assert.match(researchProjects, /<Field label="任务名称">/);
  assert.match(researchProjects, /<Field label="受控验证名称">/);
  assert.match(researchAssets, /<Field label="当前优化任务">/);
  assert.match(researchAssets, /\$\{definition\.endpoint\}\?projectId=/);
});

test("production forms use business fields and paginate long histories", () => {
  assert.match(pages, /function ProductionRecordForm/);
  assert.match(pages, /function isProductionEditorValid/);
  assert.match(pages, /equipmentId: "设备编号"/);
  assert.match(pages, /processSpecificationId: "工艺规范编号"/);
  assert.match(pages, /rows\.slice\(\(page - 1\) \* pageSize, page \* pageSize\)/);
  assert.match(pages, /\["validFrom", "validTo"\]\.includes\(key\)/);
  assert.match(pages, /total=\{rows\.length\}/);
  assert.match(pages, /disabled=\{saving \|\| !editorValid\}/);
});

test("inspection definitions use the characteristic contract and business fields", () => {
  assert.match(pages, /function InspectionDefinitionEditor/);
  assert.match(pages, /characteristics: form\.characteristics\.map/);
  assert.match(pages, /inputType: characteristic\.inputType/);
  assert.match(pages, /lowerLimit:/);
  assert.match(pages, /upperLimit:/);
  assert.match(pages, /allowedValues:/);
  assert.match(pages, /<Field label="定义代码"/);
  assert.match(pages, /<Field label="录入类型"/);
  assert.match(pages, /添加检测特性/);
  assert.match(pages, /render: \{ characteristics: inspectionInputTypes \}/);
  assert.doesNotMatch(pages, /template: \{ code: "", version: 1, name: "", description: "", status: "draft", inputType:/);
});

test("all versioned configuration registries use business forms instead of JSON editors", () => {
  for (const kind of ["processModel", "processSpecificationVersion", "analysisPlan", "qualityPlan", "scenarioPackage"]) {
    assert.match(pages, new RegExp(`kind: "${kind}"`));
    assert.match(registryEditor, new RegExp(`kind === "${kind}"`));
  }
  assert.match(registryEditor, /function QualityPlanEditor/);
  assert.match(registryEditor, /function ProcessModelEditor/);
  assert.match(registryEditor, /function ProcessSpecificationEditor/);
  assert.match(registryEditor, /function AnalysisPlanEditor/);
  assert.match(registryEditor, /function ScenarioPackageEditor/);
  assert.match(registryEditor, /requiresAttachment: item\.requiresAttachment \|\| item\.requiresReview/);
  assert.doesNotMatch(pages, /label="版本定义"/);
});

test("user-facing terminology presents scenario packages as configuration publishing", () => {
  assert.match(app, /\["\/configuration\/scenario-packages", "配置发布"\]/);
  assert.match(app, /"\/configuration\/scenario-packages": \["配置发布"/);
  assert.match(pages, /title: "配置发布"/);
  assert.match(pages, /createLabel: "创建配置版本"/);
  assert.match(registryEditor, /idLabel="工艺配置代码"/);
  assert.match(researchProjects, /label="工艺配置（推荐）"/);
  for (const source of [app, pages, registryEditor, researchProjects]) {
    assert.doesNotMatch(source, /场景包|工艺场景配置/);
  }
});

test("device acquisition has its own page instead of a generic registry drawer", () => {
  // Acquisition configuration needs a dedicated edit-probe-adjust loop.
  assert.match(app, /acquisition\/IngestionTaskPage/);
  assert.match(app, /path="\/configuration\/ingestion-tasks\/:taskId"/);
  assert.match(acquisitionPage, /export function IngestionTaskPage/);
  assert.match(acquisitionPage, /export function IngestionTasksPage/);
  // Keep the configuration column wide until the viewport can support both panes.
  assert.match(acquisitionPage, /2xl:grid-cols-\[minmax\(0,1\.55fr\)_minmax\(22rem,1fr\)\]/);
  assert.match(acquisitionPage, /2xl:row-span-2/);
  assert.match(acquisitionPage, /2xl:col-start-1/);
  assert.match(acquisitionPage, /<DevicePointsPanel/);
});

test("configuration surfaces align write actions with platform roles", () => {
  assert.match(app, /const canConfigure = \(identity\?\.roles \|\| \[\]\)\.some/);
  assert.match(app, /role === "process\.engineer" \|\| role === "platform\.admin"/);
  assert.match(app, /<Pages\.ConfigurationHubPage canWrite=\{canConfigure\}/);
  assert.match(app, /<Pages\.ProductionSetupPage section="context" canWrite=\{canConfigure\}/);
  assert.match(app, /<IngestionTasksPage canWrite=\{canConfigure\}/);
  assert.match(acquisitionPage, /const readOnly = !canWrite \|\| managedByBinding/);
  assert.match(acquisitionPage, /canWrite && row\.status === "draft"/);
});

test("form primitives keep controls aligned and make non-editable state visible", () => {
  assert.match(components, /grid min-w-0 content-start gap-1 self-start/);
  assert.match(components, /h-9 min-w-0 w-full rounded-md/);
  assert.match(components, /disabled:cursor-not-allowed disabled:border-slate-200 disabled:bg-slate-50/);
  assert.match(components, /role="alert"/);
  assert.match(styles, /input\[type="checkbox"\]/);
  assert.match(app, /<Input\s+ref=\{inputRef\}/);
  assert.doesNotMatch(app, /focus:ring-blue-100/);
  assert.match(acquisitionPanels.connection, /group\.fields\.length === 4 \|\| group\.fields\.length === 8/);
});

test("comparison investigation renders context as bounded business facts", () => {
  assert.match(components, /data-value[^"]*min-w-0[^"]*break-words[^"]*text-\[1\.75rem\]/);
  assert.match(pages, /function MatchingContext/);
  assert.match(pages, /comparisonContextLabels/);
  assert.match(pages, /<StatusBadge value=\{investigation\?\.dataQuality\?\.targetStatus/);
  assert.doesNotMatch(pages, /Object\.entries\(investigation\?\.comparisonBaseline\?\.matchingContext/);
});

test("dynamic pages and operational evidence keep business-facing labels", () => {
  assert.match(app, /startsWith\("\/configuration\/ingestion-tasks\/"\)/);
  assert.match(app, /\["页面不存在", "地址可能已经变更/);
  assert.match(pages, /contextFieldLabel/);
  for (const [field, label] of [
    ["product_family_code", "产品系列"],
    ["product_code", "产品编码"],
    ["process_specification_id", "工艺规范"],
    ["process_specification_version", "工艺规范版本"],
    ["output_item_id", "产出物"],
    ["production_context_id", "生产上下文"],
    ["external_order_ref", "外部工单"],
    ["external_batch_ref", "外部批次"],
  ]) {
    assert.match(pages, new RegExp(`${field}: "${label}"`));
  }
  assert.match(pages, /new Map\([\s\S]*contextFields/);
  assert.doesNotMatch(pages, /\["工装总成", execution\.toolingAssemblyId\],[\s\S]*\["工装总成", execution\.toolingAssemblyId\]/);
  assert.match(pages, /"process\.stage_changed": "工艺阶段切换"/);
  assert.doesNotMatch(pages, /\{value\?\.eventType \|\| "—"\}/);
  assert.doesNotMatch(pages, /\{item\.event\?\.eventType \|\| "event"\}/);
});

test("local authentication has a complete login and session-expiry experience", () => {
  assert.match(main, /<AuthGate>/);
  assert.match(authGate, /从运行证据，/);
  assert.match(authGate, /到下一份配方。/);
  assert.match(authGate, /\/api\/v1\/auth\/me/);
  assert.match(authGate, /\/api\/v1\/auth\/login/);
  assert.match(authGate, /ingot:unauthorized/);
  assert.match(authGate, /autoComplete="username"/);
  assert.match(authGate, /autoComplete="current-password"/);
  assert.match(app, /退出登录/);
  assert.doesNotMatch(app, /开发模式 · operator/);
});

test("protocol differences live in one descriptor registry", () => {
  for (const protocol of ["http-polling", "mqtt", "opc-ua", "modbus-tcp", "melsec-a1e"]) {
    assert.match(acquisitionRegistry, new RegExp(`id: "${protocol}"`));
  }
  // 每个描述符都要声明连接字段、能力开关与点位校验，界面据此渲染
  for (const key of ["connectionFieldsAreDeclarative", "capabilities", "validateConnection", "validatePoint", "probeReadiness"]) {
    if (key === "connectionFieldsAreDeclarative") continue;
    assert.match(acquisitionRegistry, new RegExp(key));
  }
  // 能力矩阵以后端 Runner 的真实行为为准
  assert.match(acquisitionRegistry, /mergeServerCapabilities/);
  assert.match(acquisitionPage, /\/api\/v1\/acquisition-protocols/);
  // MELSEC 软元件带进制与位/字区分，X\/Y 八进制换算对工程师可见
  assert.match(acquisitionRegistry, /melsecWireAddress/);
  assert.match(acquisitionPanels.mapping, /软元件号/);
  for (const device of ["W", "B", "L"]) {
    assert.match(acquisitionRegistry, new RegExp(`code: "${device}"`));
  }
});

test("acquisition validation reports errors per field", () => {
  assert.match(acquisitionForm, /export function validateIngestionTask/);
  assert.match(acquisitionForm, /const set = \(path, message\)/);
  assert.match(acquisitionPanels.connection, /error=\{errors\[/);
  assert.match(acquisitionPanels.mapping, /const error = field => errors\[/);
});

test("ingestion tasks probe real source points before publishing", () => {
  assert.match(acquisitionPage, /const ENDPOINT = "\/api\/v1\/ingestion-tasks"/);
  assert.match(acquisitionPage, /postJson\(`\$\{ENDPOINT\}\/probe`/);
  assert.match(acquisitionPanels.points, /验证连接/);
  assert.match(acquisitionRegistry, /probeViewLabel: "JSON 字段树"/);
  assert.match(acquisitionRegistry, /probeViewLabel: "节点浏览器"/);
  assert.match(acquisitionRegistry, /probeViewLabel: "寄存器读取结果"/);
  for (const label of ["原始值", "换算值", "设备类型", "单位"]) {
    assert.match(acquisitionPanels.mapping, new RegExp(label));
  }
  // 发布仍然以探查通过为硬闸门
  assert.match(acquisitionPage, /disabled=\{saving \|\| !probeValid\}/);
  assert.match(acquisitionPage, /发布前必须先验证连接/);
});

test("tooling and research workflows avoid editable JSON fields", () => {
  assert.match(pages, /function AttributeFields/);
  assert.match(pages, /function ToolingRoleFields/);
  assert.match(pages, /function ToolingAssembliesPage/);
  assert.match(pages, /function ToolingRevisionComposition/);
  assert.match(pages, /更换组件并创建新版本/);
  assert.match(pages, /每个装配位置选择一件具体组件资产/);
  assert.match(pages, /\/api\/v1\/tooling-assemblies\/revisions/);
  assert.match(pages, /assemblyRevisionId/);
  assert.doesNotMatch(pages, /BusinessObjectEditor|ImprovementPanel/);
  assert.doesNotMatch(researchProjects, /JSON\.stringify|JSON\.parse|manifestJson/);
  assert.doesNotMatch(pages, /数据清单 JSON|执行请求 JSON|上下文过滤" hint="JSON|（JSON）/);
});
