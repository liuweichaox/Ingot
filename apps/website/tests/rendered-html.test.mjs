// 验证构建后的公开页面、链接和产品语言边界。

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

test("Chinese home leads with the experiment-reduction outcome", async () => {
  const source = await html();
  assert.match(source, /<title>Ingot — 开源工艺追因与优化系统<\/title>/i);
  assert.match(source, /少做无效实验/);
  assert.match(source, /更快找到达标工艺/);
  assert.match(source, /PROCESS R&amp;D · RUN-042/);
  assert.match(source, /简单方法先行/);
  for (const stage of ["还原运行", "比较差异", "设计验证", "选择下一项"]) {
    assert.match(source, new RegExp(stage));
  }
  assert.match(source, /目标固定：减少无效实验/);
  assert.match(source, /可厂内自托管/);
  assert.doesNotMatch(source, /自动发现确定根因|已经减少\s*\d+%|FX3U|光学镜片|模压/);
  assert.match(source, /docker compose -f docker-compose\.app\.yml/);
  assert.match(source, /https:\/\/docs\.ingotstack\.com\/zh\/getting-started/);
  assert.doesNotMatch(source, retired);
});

test("English home leads with the same experiment-reduction outcome", async () => {
  const source = await html("/en/");
  assert.match(source, /<html lang="en">/);
  assert.match(source, /<title>Ingot — Open-source Process Diagnosis &amp; Optimization<\/title>/i);
  assert.match(source, /Avoid unproductive experiments/);
  assert.match(source, /Reach target conditions faster/);
  assert.match(source, /Simple methods go first/);
  for (const stage of ["Reconstruct the run", "Compare differences", "Design validation", "Select what comes next"]) {
    assert.match(source, new RegExp(stage));
  }
  assert.match(source, /One fixed outcome: fewer unproductive experiments/);
  assert.match(source, /self-hostable inside the plant/);
  assert.doesNotMatch(source, /automatically discovered root cause|already reduced\s*\d+%|FX3U|Optical lens|molding|one real lens/i);
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
