import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { join } from "node:path";
import test from "node:test";

const siteRoot = fileURLToPath(new URL("..", import.meta.url));

async function render(pathname = "/") {
  const relativePath = pathname === "/"
    ? "index.html"
    : join(pathname.replace(/^\/|\/$/g, ""), "index.html");
  const html = await readFile(join(siteRoot, "out", relativePath), "utf8");
  return new Response(html, {
    status: 200,
    headers: { "content-type": "text/html; charset=utf-8" },
  });
}

// Terms from earlier, retired product framings that must never resurface.
const retiredTerms = new RegExp([
  ["Ingot", "Agent"].join("\\s+"),
  ["desktop", "Agent"].join("\\s+"),
  "awaiting-package-approval",
  "connector-workspaces",
  "Tauri\\s+2",
  "SHA256SUMS",
  "PRODUCTION INTELLIGENCE",
  "FactoryScene3D",
].join("|"), "i");

test("renders the Chinese root as a product introduction", async () => {
  const response = await render();
  assert.equal(response.status, 200);
  assert.match(response.headers.get("content-type") ?? "", /^text\/html\b/i);

  const html = await response.text();
  assert.match(html, /<title>Ingot — AI 工艺研发系统<\/title>/i);
  assert.match(html, /Ingot · AI 工艺研发系统/);
  assert.match(html, /用更少的实验/);
  assert.match(html, /缩短工艺研发周期/);
  assert.match(html, /示例 · 研发项目/);
  assert.match(html, /数据 · 机理 · 知识融合/);
  assert.match(html, /下一组最值得做/);
  assert.match(html, /怎样更快找到满足规格的工艺窗口/);
  assert.match(html, /https:\/\/docs\.ingotstack\.com\/zh\/rollout/);
  assert.doesNotMatch(html, /ProductionEvent|InspectionRecord|\/api\/|不做这些|接口/);
  assert.doesNotMatch(html, /制造生产数据与工艺分析系统|生产履历贯通|工艺调查/);
  assert.doesNotMatch(html, retiredTerms);
  assert.doesNotMatch(html, /untrusted\.invalid|codex-preview|Your site is taking shape/i);
});

test("renders the English route with equivalent product scope", async () => {
  const response = await render("/en/");
  assert.equal(response.status, 200);
  const html = await response.text();
  assert.match(html, /<title>Ingot — AI Process R&amp;D for Manufacturing<\/title>/i);
  assert.match(html, /Ingot · AI Process R&amp;D for Manufacturing/);
  assert.match(html, /Use fewer experiments/i);
  assert.match(html, /shorten development cycles/i);
  assert.match(html, /DEMO · R&amp;D PROJECT/);
  assert.match(html, /Data · mechanisms · knowledge/i);
  assert.match(html, /next most valuable/i);
  assert.match(html, /reach a process window that meets specification faster/i);
  assert.match(html, /https:\/\/docs\.ingotstack\.com\/en\/rollout/);
  assert.match(html, /<html lang="en">/);
  assert.match(html, /rel="canonical" href="https:\/\/ingotstack\.com\/en\/"/i);
  assert.match(html, /hreflang="zh-CN"/i);
  assert.doesNotMatch(html, /ProductionEvent|InspectionRecord|\/api\/|does not do|public API/i);
  assert.doesNotMatch(html, /Manufacturing Production Data|Connected production history|process investigation/i);
  assert.doesNotMatch(html, retiredTerms);
});

test("keeps the public source aligned with the product narrative", async () => {
  const pageSource = await readFile(new URL("../app/IngotSite.tsx", import.meta.url), "utf8");

  assert.match(pageSource, /Ingot 研发助手/);
  assert.match(pageSource, /Sequential Optimization/);
  assert.match(pageSource, /缩短工艺研发周期/);
  assert.match(pageSource, /shorten development cycles/);
  assert.match(pageSource, /实验集 · 有效运行/);
  assert.match(pageSource, /Experiment set · valid runs/);
  assert.doesNotMatch(pageSource, /ProductionEvent|InspectionRecord|\/api\/|check_data_quality|get_cycle_trace|Ingot 不做这些|Ingot does not do this/i);
  assert.doesNotMatch(pageSource, /制造生产数据与工艺分析系统|Production History|Connected production history/);
  assert.doesNotMatch(pageSource, retiredTerms);
});
