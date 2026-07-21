import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import test, { after } from "node:test";

const siteRoot = fileURLToPath(new URL("..", import.meta.url));
const testPort = 3127;
let serverProcess;
let serverOutput = "";

async function render(pathname = "/") {
  if (!serverProcess) {
    const nextBin = fileURLToPath(new URL("../node_modules/next/dist/bin/next", import.meta.url));
    serverProcess = spawn(
      process.execPath,
      [nextBin, "start", "--hostname", "127.0.0.1", "--port", String(testPort)],
      {
        cwd: siteRoot,
        env: { ...process.env, NODE_ENV: "production" },
        stdio: ["ignore", "pipe", "pipe"],
      },
    );
    serverProcess.stdout.on("data", (chunk) => { serverOutput += chunk; });
    serverProcess.stderr.on("data", (chunk) => { serverOutput += chunk; });
  }

  const deadline = Date.now() + 20_000;
  while (Date.now() < deadline) {
    if (serverProcess.exitCode !== null)
      throw new Error(`Next.js exited before the test request:\n${serverOutput}`);
    try {
      return await fetch(`http://127.0.0.1:${testPort}${pathname}`, {
        headers: {
          accept: "text/html",
          "x-forwarded-host": "untrusted.invalid",
          "x-forwarded-proto": "https",
        },
      });
    } catch {
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
  }
  throw new Error(`Next.js did not become ready:\n${serverOutput}`);
}

after(() => serverProcess?.kill("SIGTERM"));

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
  assert.equal(response.headers.get("x-content-type-options"), "nosniff");
  assert.equal(response.headers.get("x-frame-options"), "DENY");
  assert.equal(response.headers.get("referrer-policy"), "strict-origin-when-cross-origin");
  assert.equal(response.headers.get("permissions-policy"), "camera=(), microphone=(), geolocation=()");

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
