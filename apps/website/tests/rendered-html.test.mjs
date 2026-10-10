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

function visibleText(source) {
  return source.replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, "")
    .replace(/<[^>]+>/g, " ").replace(/\s+/g, " ");
}

function assertInOrder(source, steps) {
  let previous = -1;
  for (const step of steps) {
    const index = source.indexOf(step, previous + 1);
    assert.ok(index > previous, `Expected deployment step after its prerequisite: ${step}`);
    previous = index;
  }
}

test("Chinese and English homes expose the current workflow without obsolete screenshots", async () => {
  for (const pathname of ["/", "/en/"]) {
    const source = await html(pathname);
    assert.match(source, /<section class="story section" id="product"/i);
    assert.match(source, /<section class="closed-loop section" id="loop"/i);
    assert.match(source, /class="loop-rail"[^>]*aria-label="(?:工程闭环|Engineering loop)"/i);
    assert.match(source, /class="[^"]*\bloop-step\b[^"]*"/i);
    assert.doesNotMatch(source, /(?:src|href)="[^"]*\/screenshots\//i);
    assert.doesNotMatch(source, /独立优化任务|优化任务工作区|Independent optimization tasks|optimization task workspace/i);
  }
});

test("both locales describe run experiments and the two recipe decision paths", async () => {
  const expectations = [
    ["/", /每次工艺运行就是一次实验/, /小校正[^。]*当前(?:配方)?版本/, /显著变更[^。]*修订草稿/],
    ["/en/", /Each process run is an experiment/i, /Small corrections[^.]*current (?:recipe )?version/i, /Significant changes[^.]*revision draft/i],
  ];
  for (const [pathname, experiment, correction, revision] of expectations) {
    const text = visibleText(await html(pathname));
    assert.match(text, experiment);
    assert.match(text, correction);
    assert.match(text, revision);
    assert.doesNotMatch(text, /修订下一配方版本|Revise the next recipe version/i);
  }
});

test("both locales require replacing placeholder secrets before validation and startup", async () => {
  for (const pathname of ["/", "/en/"]) {
    const source = await html(pathname);
    const command = source.match(/<pre\b[^>]*>\s*<code\b[^>]*>([\s\S]*?)<\/code>\s*<\/pre>/i)?.[1];
    assert.ok(command, "Expected a copyable deployment command block");
    assertInOrder(visibleText(command), [
      "cp .env.example .env",
      "change-this",
      "docker compose -f docker-compose.app.yml config --quiet",
      "docker compose -f docker-compose.app.yml up -d --build",
    ]);
    const text = visibleText(source);
    assert.match(text, pathname === "/" ? /替换|修改/ : /replace/i);
  }
});

test("maturity and deployment references are clickable and locale specific", async () => {
  for (const [pathname, locale] of [["/", "zh"], ["/en/", "en"]]) {
    const source = await html(pathname);
    for (const document of ["status", "deployment"]) {
      const href = `https://docs.ingotstack.com/${locale}/${document}`;
      const anchor = source.match(new RegExp(`<a\\b[^>]*href="${href.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}"[^>]*>([\\s\\S]*?)<\\/a>`, "i"));
      assert.ok(anchor, `Expected a clickable ${document} reference for ${pathname}`);
      assert.ok(visibleText(anchor[1]).trim(), "Reference must have a visible label");
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

test("Chinese home presents R&D, analysis, and recipe decisions", async () => {
  const source = await html();
  assert.match(source, /<title>Ingot — 开源工艺研发与优化系统<\/title>/i);
  assert.match(source, /组织配方版本、实验记录与运行证据，支持质量分析、工艺追因和配方优化/);
  assert.match(source, /从工艺数据，/);
  assert.match(source, /到有依据的研发决策。/);
  assert.match(source, /已复核工艺资料片段/);
  assert.match(source, /片段级引用/);
  for (const stage of ["组织研发记录", "核对运行与质量", "分析工艺差异", "审核配方建议"]) {
    assert.match(source, new RegExp(stage));
  }
  assert.match(source, /工艺能力持续升级，证据边界始终不变/);
  assert.match(source, /可在厂内自托管/);
  assert.match(source, /仓库不提供现场收益验证结果/);
  assert.match(source, /每次工艺运行就是一次实验/);
  assert.match(source, /具体场景评估由部署方用自己的数据完成/);
  assert.doesNotMatch(source, /公开验证协议与结果可以独立复现/);
  assert.doesNotMatch(source, /自动发现确定根因|已经减少\s*\d+%|FX3U|光学镜片|模压/);
  assert.match(source, /docker compose -f docker-compose\.app\.yml/);
  assert.match(source, /https:\/\/docs\.ingotstack\.com\/zh\/getting-started/);
  assert.doesNotMatch(source, retired);
});

test("English home presents R&D, analysis, and recipe decisions", async () => {
  const source = await html("/en/");
  assert.match(source, /<html lang="en">/);
  assert.match(source, /<title>Ingot — Open-source Process R&amp;D and Optimization System<\/title>/i);
  assert.match(source, /Organize recipe versions, experiment records, and run evidence to support quality analysis, process diagnosis, and recipe optimization/i);
  assert.match(source, /From process data/);
  assert.match(source, /to evidence-based R&amp;D decisions/);
  assert.match(source, /reviewed process-document references/i);
  assert.match(source, /Fragment citations/);
  for (const stage of ["Organize research records", "Check runs and quality", "Analyze process differences", "Review recipe recommendations"]) {
    assert.match(source, new RegExp(stage));
  }
  assert.match(source, /Process capabilities evolve/);
  assert.match(source, /self-hostable inside the plant/);
  assert.match(source, /repository provides no field-benefit validation results/i);
  assert.match(source, /Each process run is an experiment/i);
  assert.match(source, /scenario-specific evaluation belongs to the deployer(?:'|&#x27;)s own data/i);
  assert.doesNotMatch(source, /public validation protocols and results are independently reproducible/i);
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

// Guard the public-to-documentation path and the evidence boundary of recommendations.
test("both locales link the pilot and troubleshooting with explicit admission boundaries", async () => {
  for (const [pathname, locale] of [["/", "zh"], ["/en/", "en"]]) {
    const source = await html(pathname);
    const text = visibleText(source);
    for (const slug of ["pilot", "troubleshooting"])
      assert.ok(source.includes(`href="https://docs.ingotstack.com/${locale}/${slug}"`));
    assert.match(text, locale === "zh" ? /至少三条有效运行、两种不同实际配方只是最低门槛/ : /three valid runs and two distinct actual recipes are minimum requirements/i);
    assert.match(text, locale === "zh" ? /Web、API、Worker、数据库/ : /Web app, API, Worker, database/);
    assert.doesNotMatch(text, /下一轮同版本运行自动接续|next same-version run links automatically/i);
    assert.match(text, locale === "zh" ? /按站点与配方归属管理/ : /by site and recipe/);
  }
});
