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
  assert.match(source, /<title>Ingot — 开源工艺追因与优化系统<\/title>/i);
  assert.match(source, /看清这次运行/);
  assert.match(source, /MANUFACTURING R&amp;D · CAMPAIGN-042/);
  assert.match(source, /qLogNEI/);
  for (const stage of ["工艺定义", "设备接入", "生产采集", "数据闭环", "工艺追因", "工艺优化"]) {
    assert.match(source, new RegExp(stage));
  }
  assert.match(source, /换场景，不换追因与优化内核/);
  assert.doesNotMatch(source, /待真实证明|现在能做什么|FX3U|光学镜片|模压/);
  assert.match(source, /docker compose -f docker-compose\.app\.yml/);
  assert.match(source, /https:\/\/docs\.ingotstack\.com\/zh\/getting-started/);
  assert.doesNotMatch(source, retired);
});

test("English home carries the same final-product vision", async () => {
  const source = await html("/en/");
  assert.match(source, /<html lang="en">/);
  assert.match(source, /<title>Ingot — Open-source Process Diagnosis &amp; Optimization<\/title>/i);
  assert.match(source, /Explain this run/);
  assert.match(source, /physical priors and Bayesian optimization/);
  assert.match(source, /optimization brain built for expensive, small-data experiments/);
  for (const stage of ["Define the process", "Connect equipment", "Collect production data", "Close the data loop", "Diagnose the process", "Process optimization"]) {
    assert.match(source, new RegExp(stage));
  }
  assert.match(source, /Change the scenario, not the diagnosis and optimization core/);
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
