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
  assert.match(html, /<title>Ingot — 制造生产数据与工艺分析系统<\/title>/i);
  assert.match(html, /Ingot · 制造生产数据与工艺分析系统/);
  assert.match(html, /专家方法可以复用/);
  assert.match(html, /示例 · 工艺调查/);
  assert.match(html, /变成可追溯的工程依据/);
  assert.match(html, /生产履历贯通/);
  assert.match(html, /分析结果可回查/);
  assert.match(html, /从生产履历到/);
  assert.match(html, /良率为什么突然下滑/);
  assert.match(html, /https:\/\/docs\.ingotstack\.com\/zh\/rollout/);
  assert.match(html, /https:\/\/ingotstack\.com\/og\.png/i);
  assert.doesNotMatch(html, /ProductionEvent|InspectionRecord|\/api\/|不做这些|接口/);
  assert.doesNotMatch(html, retiredTerms);
  assert.doesNotMatch(html, /untrusted\.invalid|codex-preview|Your site is taking shape/i);
});

test("renders the English route with equivalent product scope", async () => {
  const response = await render("/en/");
  assert.equal(response.status, 200);
  const html = await response.text();
  assert.match(html, /<title>Ingot — Manufacturing Production Data &amp; Process Analysis<\/title>/i);
  assert.match(html, /Ingot · Manufacturing Production Data &amp; Process Analysis/);
  assert.match(html, /Expert methods become reusable/);
  assert.match(html, /DEMO · INVESTIGATION/);
  assert.match(html, /traceable engineering evidence/i);
  assert.match(html, /Connected production history/i);
  assert.match(html, /Why did yield suddenly drop/i);
  assert.match(html, /https:\/\/docs\.ingotstack\.com\/en\/rollout/);
  assert.match(html, /<html lang="en">/);
  assert.match(html, /rel="canonical" href="https:\/\/ingotstack\.com\/en\/"/i);
  assert.match(html, /hreflang="zh-CN"/i);
  assert.doesNotMatch(html, /ProductionEvent|InspectionRecord|\/api\/|does not do|public API/i);
  assert.doesNotMatch(html, retiredTerms);
});

test("keeps the public source aligned with the product narrative", async () => {
  const pageSource = await readFile(new URL("../app/IngotSite.tsx", import.meta.url), "utf8");

  assert.match(pageSource, /Ingot Chat/);
  assert.match(pageSource, /Production History/);
  assert.match(pageSource, /生产履历贯通/);
  assert.match(pageSource, /Connected production history/);
  assert.match(pageSource, /批次覆盖 · LOT-0716/);
  assert.match(pageSource, /Batch coverage · LOT-0716/);
  assert.doesNotMatch(pageSource, /ProductionEvent|InspectionRecord|\/api\/|check_data_quality|get_cycle_trace|Ingot 不做这些|Ingot does not do this/i);
  assert.doesNotMatch(pageSource, retiredTerms);
});
