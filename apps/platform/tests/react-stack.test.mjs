import assert from "node:assert/strict";
import { readdir, readFile } from "node:fs/promises";
import test from "node:test";

const packageJson = JSON.parse(await readFile(new URL("../package.json", import.meta.url), "utf8"));
const app = await readFile(new URL("../src/App.jsx", import.meta.url), "utf8");
const pages = await readFile(new URL("../src/pages/index.jsx", import.meta.url), "utf8");
const http = await readFile(new URL("../src/api/http.js", import.meta.url), "utf8");
const components = await readFile(new URL("../src/ui/components.jsx", import.meta.url), "utf8");
const registryEditor = await readFile(new URL("../src/components/RegistryBusinessEditor.jsx", import.meta.url), "utf8");
const researchProjects = await readFile(new URL("../src/pages/ResearchProjectsPage.jsx", import.meta.url), "utf8");
const researchAssets = await readFile(new URL("../src/pages/ResearchAssetsPage.jsx", import.meta.url), "utf8");
const styles = await readFile(new URL("../src/styles/global.css", import.meta.url), "utf8");
const vite = await readFile(new URL("../vite.config.mjs", import.meta.url), "utf8");
const html = await readFile(new URL("../index.html", import.meta.url), "utf8");

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
    "/research-projects", "/workbench", "/chat", "/explorer", "/cycles", "/events", "/production/changeover",
    "/production/tooling-installations", "/configuration/component-types", "/configuration/components",
    "/configuration/tooling-types", "/configuration/tooling-assemblies", "/inspections",
    "/quality-analysis", "/configuration/inspection-definitions", "/configuration/quality-plans",
    "/comparisons", "/data-quality", "/process-improvement",
    "/configuration/process-analysis-plans", "/configuration/process-data-models",
    "/configuration/recipe-versions", "/configuration/acquisition-profiles", "/edges",
    "/platform-metrics", "/subscriptions", "/logs",
  ]) {
    assert.match(app, new RegExp(route.replaceAll("/", "\\/")));
  }
  assert.match(app, /\/research-projects\/:projectId/);
  assert.match(app, /Navigate to="\/research-projects"/);
  assert.match(app, /Navigate to="\/configuration\/process-data-models"/);
});

test("platform identity presents Ingot as an AI process research system", () => {
  assert.match(html, /Ingot · AI 工艺研发系统/);
  assert.match(html, /实验数据、实时过程数据、物理机理和专家知识/);
  assert.doesNotMatch(html, /制造数据采集与工艺分析平台/);
});

test("navigation and overlays are accessible Headless UI components", () => {
  assert.match(app, /DialogBackdrop/);
  assert.match(app, /DialogPanel/);
  assert.match(app, /MenuButton/);
  assert.match(app, /aria-label="全局导航"/);
  assert.match(app, /aria-label="打开全局模块导航"/);
  assert.match(app, /xl:hidden/);
  assert.match(app, /xl:flex/);
  assert.match(app, /aria-label="打开模块导航"/);
});

test("production login and local account administration are usable from the web app", () => {
  assert.match(app, /\/api\/v1\/auth\/me/);
  assert.match(app, /\/api\/v1\/auth\/login/);
  assert.match(app, /\/api\/v1\/auth\/logout/);
  assert.match(app, /function LoginPage/);
  assert.match(app, /path="\/users"/);
  assert.match(http, /Authorization: `Bearer \$\{token\}`/);
  assert.match(http, /ingot:unauthorized/);
  assert.match(pages, /export function UsersPage/);
  assert.match(pages, /:set-roles/);
  assert.match(pages, /:set-password/);
  assert.match(pages, /:set-disabled/);
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
});

test("global search opens a cross-product command palette and table columns keep stable unique keys", () => {
  assert.match(app, /<GlobalSearchDialog/);
  assert.match(app, /setGlobalSearchOpen\(true\)/);
  assert.match(app, /event\.key\.toLowerCase\(\) === "k"/);
  assert.match(app, /全局搜索/);
  assert.doesNotMatch(app, /navigate\("\/explorer", \{ state: \{ focusSearch: true \} \}\)/);
  assert.match(app, /to="\/platform-metrics"[^>]*>平台运行状态/);
  assert.match(components, /key=\{column\.id \?\? `\$\{column\.key\}:\$\{columnIndex\}`\}/);
});

test("industrial object pages use the event summary contract and show an initial loading state", () => {
  assert.match(app, /\["\/explorer", "工业对象"\]/);
  assert.match(app, /对象与模型/);
  assert.match(pages, /title="工业对象"/);
  assert.match(pages, /objects\.loading && !objects\.data \? <LoadingCard \/>/);
  assert.match(pages, /title="对象目录"/);
  assert.match(pages, /在这个对象中继续工作/);
  assert.match(pages, /\/cycles\?machineId=/);
  assert.match(pages, /\/events\?subjectId=/);
  assert.match(pages, /\/quality-analysis\?subjectType=/);
  assert.match(pages, /\/data-quality\?subjectType=/);
  assert.doesNotMatch(pages, /key: "objectType", label: "对象类型"/);
});

test("core workflows tell new users what to do next and confirm completed actions", () => {
  assert.match(components, /export function WorkflowGuide/);
  assert.match(components, /export function ToastHost/);
  assert.match(pages, /今天先做这些/);
  assert.match(pages, /配置下一批生产/);
  assert.match(researchProjects, /发现偏差 → 找到原因 → 设计实验 → 验证并固化窗口/);
  assert.match(researchProjects, /优化模型准备度/);
  assert.match(app, /<ToastHost \/>/);
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
  assert.match(pages, /quick: "快速分析"/);
  assert.match(pages, /<Field label="调查问题">/);
  assert.match(pages, /<Field label="分析模式">/);
  assert.match(pages, /setEditorMode\(row \? \(section === "type" \? "version" : "edit"\) : "create"\)/);
  assert.match(pages, /editorMode === "create" \? resource\.createLabel/);
  assert.match(researchProjects, /<Field label="项目名称">/);
  assert.match(researchProjects, /<Field label="实验名称">/);
  assert.match(researchAssets, /<Field label="当前研发项目">/);
  assert.match(researchAssets, /\$\{definition\.endpoint\}\?projectId=/);
});

test("production forms use business fields and paginate long histories", () => {
  assert.match(pages, /function ProductionRecordForm/);
  assert.match(pages, /function isProductionEditorValid/);
  assert.match(pages, /machineId: "设备编号"/);
  assert.match(pages, /recipeId: "配方编号"/);
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
  for (const kind of ["processModel", "recipeVersion", "analysisPlan", "qualityPlan", "acquisitionProfile"]) {
    assert.match(pages, new RegExp(`kind: "${kind}"`));
    assert.match(registryEditor, new RegExp(`kind === "${kind}"`));
  }
  assert.match(registryEditor, /function QualityPlanEditor/);
  assert.match(registryEditor, /function ProcessModelEditor/);
  assert.match(registryEditor, /function RecipeEditor/);
  assert.match(registryEditor, /function AnalysisPlanEditor/);
  assert.match(registryEditor, /function AcquisitionEditor/);
  assert.match(registryEditor, /requiresAttachment: item\.requiresAttachment \|\| item\.requiresReview/);
  assert.doesNotMatch(pages, /label="版本定义"/);
});

test("tooling, subscriptions, and research workflows avoid editable JSON fields", () => {
  assert.match(pages, /function AttributeFields/);
  assert.match(pages, /function ToolingRoleFields/);
  assert.match(pages, /function SubscriptionContextFields/);
  assert.doesNotMatch(pages, /BusinessObjectEditor|ImprovementPanel/);
  assert.doesNotMatch(researchProjects, /JSON\.stringify|JSON\.parse|manifestJson/);
  assert.doesNotMatch(pages, /数据清单 JSON|执行请求 JSON|上下文过滤" hint="JSON|（JSON）/);
});
