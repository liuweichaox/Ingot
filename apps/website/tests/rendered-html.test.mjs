import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { join } from "node:path";
import test from "node:test";

const siteRoot = fileURLToPath(new URL("..", import.meta.url));

async function html(pathname = "/") {
  const relative = pathname === "/" ? "index.html" : join(pathname.replace(/^\/|\/$/g, ""), "index.html");
  return readFile(join(siteRoot, "out", relative), "utf8");
}

const retired = /Ingot Agent|desktop Agent|connector-workspaces|awaiting-package-approval|FactoryScene3D|制造生产数据与工艺分析系统|Connected production history/i;

test("Chinese home presents the final closed-loop product capability", async () => {
  const source = await html();
  assert.match(source, /<title>Ingot — AI 闭环工艺优化系统<\/title>/i);
  assert.match(source, /让每一次试验/);
  assert.match(source, /MANUFACTURING R&amp;D · CAMPAIGN-042/);
  assert.match(source, /qLogNEI/);
  assert.match(source, /工艺追因/);
  assert.match(source, /工艺优化/);
  assert.match(source, /换场景，不换优化系统/);
  assert.match(source, /知识迁移/);
  assert.doesNotMatch(source, /待真实证明|现在能做什么|FX3U|光学镜片|模压/);
  assert.match(source, /docker compose -f docker-compose\.app\.yml/);
  assert.match(source, /https:\/\/docs\.ingotstack\.com\/zh\/getting-started/);
  assert.doesNotMatch(source, retired);
});

test("English home carries the same final-product vision", async () => {
  const source = await html("/en/");
  assert.match(source, /<html lang="en">/);
  assert.match(source, /Make every experiment/);
  assert.match(source, /physical priors with Bayesian optimization/);
  assert.match(source, /optimization brain built for expensive, small-data experiments/);
  assert.match(source, /Change the scenario, not the optimization system/);
  assert.match(source, /Knowledge transfer/);
  assert.doesNotMatch(source, /Not yet proven|What works now|FX3U|Optical lens|molding|one real lens/i);
  assert.match(source, /rel="canonical" href="https:\/\/ingotstack\.com\/en\/"/i);
  assert.doesNotMatch(source, retired);
});

test("public source uses brand assets instead of an inline logo and links project surfaces", async () => {
  const source = await readFile(new URL("../app/IngotSite.tsx", import.meta.url), "utf8");
  assert.match(source, /ingot-lockup-dark\.svg/);
  assert.match(source, /github\.com\/liuweichaox\/Ingot/);
  assert.match(source, /docs\.ingotstack\.com/);
  assert.doesNotMatch(source, /function Mark|<svg/i);
  assert.doesNotMatch(source, retired);
});
