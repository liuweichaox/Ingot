import assert from "node:assert/strict";
import { readdir, readFile } from "node:fs/promises";
import test from "node:test";

const packageJson = JSON.parse(await readFile(new URL("../package.json", import.meta.url), "utf8"));
const app = await readFile(new URL("../src/App.jsx", import.meta.url), "utf8");
const pages = await readFile(new URL("../src/pages/index.jsx", import.meta.url), "utf8");
const components = await readFile(new URL("../src/ui/components.jsx", import.meta.url), "utf8");
const registryEditor = await readFile(new URL("../src/components/RegistryBusinessEditor.jsx", import.meta.url), "utf8");
const businessObjectEditor = await readFile(new URL("../src/components/BusinessObjectEditor.jsx", import.meta.url), "utf8");
const styles = await readFile(new URL("../src/styles/global.css", import.meta.url), "utf8");
const vite = await readFile(new URL("../vite.config.mjs", import.meta.url), "utf8");

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
    "/workbench", "/chat", "/explorer", "/cycles", "/events", "/production/changeover",
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
  assert.match(app, /Navigate to="\/workbench"/);
  assert.match(app, /Navigate to="\/configuration\/process-data-models"/);
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

test("versioned registries use composite row keys and statuses are localized", () => {
  assert.match(pages, /getRowKey=\{row => `\$\{row\[definition\.key\]\}:\$\{row\.version \?\? 1\}`\}/);
  assert.match(pages, /label="待上报状态"/);
  assert.match(app, /section\.label/);
  assert.match(components, /pending: "待处理"/);
  assert.match(components, /published: "已发布"/);
  assert.match(components, /review_pending: "待复核"/);
  assert.match(components, /unknown: "待上报"/);
});

test("global search focuses the object query and table columns keep stable unique keys", () => {
  assert.match(app, /navigate\("\/explorer", \{ state: \{ focusSearch: true \} \}\)/);
  assert.match(app, /to="\/platform-metrics"[^>]*>平台运行状态/);
  assert.match(pages, /if \(location\.state\?\.focusSearch\) searchInput\.current\?\.focus\(\)/);
  assert.match(pages, /<Input ref=\{searchInput\}/);
  assert.match(components, /key=\{column\.id \?\? `\$\{column\.key\}:\$\{columnIndex\}`\}/);
});

test("versioned tooling and improvement rows remain unique while hidden tabs stay idle", () => {
  assert.match(pages, /getRowKey=\{section === "type" \? row => `\$\{row\[resource\.key\]\}:\$\{row\.version \?\? 1\}` : undefined\}/);
  assert.match(pages, /<TabGroup selectedIndex=\{selectedTab\} onChange=\{setSelectedTab\}>/);
  assert.match(pages, /index === selectedTab && <ImprovementPanel definition=\{tab\} \/>/);
  assert.match(pages, /definition\.columns\.some\(\(\[key\]\) => key === "version"\)/);
  assert.match(pages, /<option value="Information">信息<\/option>/);
});

test("forms expose clear labels, edit intent, and required upload fields", () => {
  assert.match(pages, /const chatModeLabels = \{/);
  assert.match(pages, /quick: "快速分析"/);
  assert.match(pages, /<Field label="调查问题">/);
  assert.match(pages, /<Field label="分析模式">/);
  assert.match(pages, /setEditorMode\(row \? \(section === "type" \? "version" : "edit"\) : "create"\)/);
  assert.match(pages, /editorMode === "create" \? resource\.createLabel/);
  assert.match(pages, /<Input required value=\{title\}/);
  assert.match(pages, /<Input required type="file"/);
  assert.match(pages, /definition\.upload === "validation" && !file/);
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

test("tooling, subscriptions, and improvement workflows avoid editable JSON fields", () => {
  assert.match(pages, /function AttributeFields/);
  assert.match(pages, /function ToolingRoleFields/);
  assert.match(pages, /function SubscriptionContextFields/);
  assert.match(pages, /<BusinessObjectEditor value=\{editor\} onChange=\{setEditor\}/);
  assert.match(businessObjectEditor, /function ObjectList/);
  assert.match(businessObjectEditor, /function MapFields/);
  assert.doesNotMatch(pages, /数据清单 JSON|执行请求 JSON|上下文过滤" hint="JSON|（JSON）/);
});
