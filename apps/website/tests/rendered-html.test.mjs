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

test("Chinese home presents the stable data-supported engineering value", async () => {
  const source = await html();
  assert.match(source, /<title>Ingot — 开源工艺追因与优化系统<\/title>/i);
  assert.match(source, /让真实数据/);
  assert.match(source, /帮助工艺工程师抉择/);
  assert.match(source, /PROCESS R&amp;D · RUN-042/);
  assert.match(source, /不迷信单一算法/);
  for (const stage of ["工艺定义", "设备接入", "生产采集", "数据闭环", "工艺追因", "工艺研发"]) {
    assert.match(source, new RegExp(stage));
  }
  assert.match(source, /核心价值不变，方法随真实证据升级/);
  assert.match(source, /代码能力与真实收益明确分开/);
  assert.doesNotMatch(source, /自动发现确定根因|已经减少\s*\d+%|FX3U|光学镜片|模压/);
  assert.match(source, /docker compose -f docker-compose\.app\.yml/);
  assert.match(source, /https:\/\/docs\.ingotstack\.com\/zh\/getting-started/);
  assert.doesNotMatch(source, retired);
});

test("English home carries the same data-supported engineering value", async () => {
  const source = await html("/en/");
  assert.match(source, /<html lang="en">/);
  assert.match(source, /<title>Ingot — Open-source Process Diagnosis &amp; Optimization<\/title>/i);
  assert.match(source, /Help process engineers decide/);
  assert.match(source, /decisions grounded in real runs/);
  assert.match(source, /Choose an effective method/);
  for (const stage of ["Define the process", "Connect equipment", "Collect production data", "Close the data loop", "Diagnose the process"]) {
    assert.match(source, new RegExp(stage));
  }
  assert.match(source, /Process R(?:&amp;|&#x26;)D/);
  assert.match(source, /Keep the core value stable while methods improve/);
  assert.match(source, /Code capability is separated from proven benefit/);
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
