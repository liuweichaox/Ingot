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

test("renders the Chinese root around the root-cause positioning and trust guarantees", async () => {
  const response = await render();
  assert.equal(response.status, 200);
  assert.match(response.headers.get("content-type") ?? "", /^text\/html\b/i);

  const html = await response.text();
  assert.match(html, /<title>Ingot — 工艺追因引擎 · 可核对、不编造数字的生产分析<\/title>/i);
  assert.match(html, /Ingot · 工艺追因引擎/);
  assert.match(html, /和上批不一样/);
  assert.match(html, /永不编造数字/);
  assert.match(html, /永不触碰设备/);
  assert.match(html, /工厂对 AI 的两个恐惧/);
  assert.match(html, /良率为什么突然下滑/);
  assert.match(html, /ProductionEvent/);
  assert.match(html, /https:\/\/docs\.ingotstack\.com\/zh\/rfc-production-events/);
  assert.match(html, /https:\/\/ingotstack\.com\/og\.png/i);
  assert.doesNotMatch(html, retiredTerms);
  assert.doesNotMatch(html, /untrusted\.invalid|codex-preview|Your site is taking shape/i);
});

test("renders the stable English route with equivalent scope", async () => {
  const response = await render("/en/");
  assert.equal(response.status, 200);
  const html = await response.text();
  assert.match(html, /<title>Ingot — Process Root-Cause Engine · verifiable answers, no hallucinated numbers<\/title>/i);
  assert.match(html, /Ingot · Process Root-Cause Engine/);
  assert.match(html, /different from the last/i);
  assert.match(html, /Never invents a number/i);
  assert.match(html, /Why did yield suddenly drop/i);
  assert.match(html, /https:\/\/docs\.ingotstack\.com\/en\/rfc-production-events/);
  assert.match(html, /<html lang="en">/);
  assert.match(html, /rel="canonical" href="https:\/\/ingotstack\.com\/en\/"/i);
  assert.match(html, /hreflang="zh-CN"/i);
  assert.doesNotMatch(html, retiredTerms);
});

test("keeps the public source aligned with the published boundaries", async () => {
  const pageSource = await readFile(new URL("../app/IngotSite.tsx", import.meta.url), "utf8");

  assert.match(pageSource, /Ingot Chat/);
  assert.match(pageSource, /Number Grounding/);
  assert.match(pageSource, /永不编造数字/);
  assert.match(pageSource, /Never invents a number/);
  assert.match(pageSource, /check_data_quality/);
  assert.match(pageSource, /get_cycle_trace/);
  assert.match(pageSource, /永不写 PLC \/ CNC \/ 机器人/);
  assert.match(pageSource, /it never writes to a PLC \/ CNC \/ robot/i);
  assert.doesNotMatch(pageSource, retiredTerms);
});
