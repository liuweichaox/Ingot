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

test("Chinese and English homes expose the workbench story and engineering loop", async () => {
  for (const pathname of ["/", "/en/"]) {
    const source = await html(pathname);
    assert.match(source, /<div class="product-frame hero-product">[\s\S]*?<img[^>]+src="\/screenshots\/workbench\.png"/i);
    assert.match(source, /<section class="story section" id="product"/i);
    assert.match(source, /class="product-frame story-product"[^>]*data-step="0"/i);
    assert.match(source, /<section class="closed-loop section" id="loop"/i);
    assert.match(source, /class="loop-rail"[^>]*aria-label="(?:工程闭环|Engineering loop)"/i);
    assert.match(source, /class="[^"]*\bloop-step\b[^"]*"/i);
    assert.match(source, /<section class="screen-gallery" id="screenshots">/i);
    for (const screenshot of ["production-run.png", "diagnosis.png", "optimization.png", "next-recipe.png"]) {
      assert.match(source, new RegExp(`/screenshots/${screenshot}`));
    }
  }
});

test("source does not enable mandatory scroll snap", async () => {
  const [tsx, css] = await Promise.all([
    readFile(new URL("../app/IngotSite.tsx", import.meta.url), "utf8"),
    readFile(new URL("../app/globals.css", import.meta.url), "utf8"),
  ]);
  assert.doesNotMatch(`${tsx}\n${css}`, /scroll-snap-type\s*:[^;]*\bmandatory\b/i);
});

test("Chinese home presents the production-to-specification-revision flow", async () => {
  const source = await html();
  assert.match(source, /<title>Ingot — 开源工艺追因与优化系统<\/title>/i);
  assert.match(source, /开源工艺追因与优化系统。把设备、生产和检验数据关联成可信证据，支持工程师修订下一版工艺规范/);
  assert.doesNotMatch(source, /面向工艺工程师的开源工艺追因与优化系统/);
  assert.match(source, /从真实运行/);
  assert.match(source, /到下一版工艺规范/);
  assert.match(source, /SPECIFICATION REVISION · RUN-042/);
  assert.match(source, /先确认数据是否可靠，再形成可审计的工艺修订/);
  assert.match(source, /无需先建立实验，也无需工程师重新归类配方/);
  assert.match(source, /已复核工艺资料片段/);
  assert.match(source, /片段级引用/);
  for (const stage of ["建立运行证据", "完成工艺追因", "修订下一版规范", "继续从生产回流"]) {
    assert.match(source, new RegExp(stage));
  }
  assert.match(source, /工艺能力持续升级，证据边界始终不变/);
  assert.match(source, /可在厂内自托管/);
  assert.match(source, /真实工厂收益验证尚未完成/);
  assert.doesNotMatch(source, /自动发现确定根因|已经减少\s*\d+%|FX3U|光学镜片|模压/);
  assert.match(source, /docker compose -f docker-compose\.app\.yml/);
  assert.match(source, /https:\/\/docs\.ingotstack\.com\/zh\/getting-started/);
  assert.doesNotMatch(source, retired);
});

test("English home presents the production-to-specification-revision flow", async () => {
  const source = await html("/en/");
  assert.match(source, /<html lang="en">/);
  assert.match(source, /<title>Ingot — Open-source Process Diagnosis &amp; Optimization<\/title>/i);
  assert.match(source, /system that turns linked equipment, production, and inspection data into trustworthy evidence/i);
  assert.doesNotMatch(source, /system for process engineers/i);
  assert.match(source, /From real runs/);
  assert.match(source, /to the next process specification/);
  assert.match(source, /SPECIFICATION REVISION · RUN-042/);
  assert.match(source, /Confirm that data are trustworthy before forming an auditable revision/);
  assert.match(source, /No experiment setup or manual recipe reclassification is required/);
  assert.match(source, /reviewed process-document references/i);
  assert.match(source, /Fragment citations/);
  for (const stage of ["Build run evidence", "Complete process diagnosis", "Revise the next specification", "Return through production"]) {
    assert.match(source, new RegExp(stage));
  }
  assert.match(source, /Process capabilities evolve/);
  assert.match(source, /self-hostable inside the plant/);
  assert.match(source, /real-factory benefit validation remains incomplete/i);
  assert.doesNotMatch(source, /automatically discovered root cause|already reduced\s*\d+%|FX3U|Optical lens|molding|one real lens/i);
  assert.match(source, /rel="canonical" href="https:\/\/ingotstack\.com\/en\/"/i);
  assert.doesNotMatch(source, retired);
});

test("public source uses brand assets instead of an inline logo and links project surfaces", async () => {
  const source = await readFile(new URL("../app/IngotSite.tsx", import.meta.url), "utf8");
    assert.match(source, /ingot-lockup-dark\.svg/);
    assert.match(source, /ingot-lockup\.svg/);
  assert.match(source, /github\.com\/liuweichaox\/Ingot/);
  assert.match(source, /docs\.ingotstack\.com/);
  assert.doesNotMatch(source, /function Mark|<svg/i);
  assert.doesNotMatch(source, retired);
});
