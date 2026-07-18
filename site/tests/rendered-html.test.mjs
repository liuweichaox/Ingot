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

const oldDesktopTerms = new RegExp([
  ["Ingot", "Agent"].join("\\s+"),
  ["desktop", "Agent"].join("\\s+"),
  ["code", "generation"].join("[-\\s]?"),
  "awaiting-package-approval",
  "connector-workspaces",
  "Tauri\\s+2",
  "SHA256SUMS",
].join("|"), "i");

test("renders the Chinese product site around Ingot Chat and standard event ingestion", async () => {
  const response = await render();
  assert.equal(response.status, 200);
  assert.match(response.headers.get("content-type") ?? "", /^text\/html\b/i);
  assert.equal(response.headers.get("x-content-type-options"), "nosniff");
  assert.equal(response.headers.get("x-frame-options"), "DENY");
  assert.equal(response.headers.get("referrer-policy"), "strict-origin-when-cross-origin");
  assert.equal(response.headers.get("permissions-policy"), "camera=(), microphone=(), geolocation=()");

  const html = await response.text();
  assert.match(html, /<title>Ingot — 可信生产事实、标准事件接入与 Ingot Chat<\/title>/i);
  assert.match(html, /TRUSTED FACTS \/ EVENT API \/ INGOT CHAT/);
  assert.match(html, /用标准事件接入数据/);
  assert.match(html, /用 Ingot Chat 查找问题/);
  assert.match(html, /可选 Connector Host/);
  assert.match(html, /用户自管 · 可选/);
  assert.match(html, /https:\/\/docs\.ingotstack\.com\/zh\/rfc-production-events/);
  assert.match(html, /cycle\.completed/);
  assert.match(html, /https:\/\/ingotstack\.com\/og\.png/i);
  assert.doesNotMatch(html, oldDesktopTerms);
  assert.doesNotMatch(html, /untrusted\.invalid|codex-preview|Your site is taking shape/i);
});

test("renders the stable English route with equivalent product scope", async () => {
  const response = await render("/en/");
  assert.equal(response.status, 200);
  const html = await response.text();
  assert.match(html, /<title>Ingot — Trusted production facts, standard event ingestion, and Ingot Chat<\/title>/i);
  assert.match(html, /TRUSTED FACTS \/ EVENT API \/ INGOT CHAT/);
  assert.match(html, /Ingest data through standard events/i);
  assert.match(html, /Use Ingot Chat to find problems/i);
  assert.match(html, /Optional Connector Host/);
  assert.match(html, /TEAM-OPERATED · OPTIONAL/);
  assert.match(html, /https:\/\/docs\.ingotstack\.com\/en\/rfc-production-events/);
  assert.match(html, /<html lang="en">/);
  assert.match(html, /rel="canonical" href="https:\/\/ingotstack\.com\/en\/"/i);
  assert.match(html, /hreflang="zh-CN"/i);
  assert.doesNotMatch(html, oldDesktopTerms);
});

test("keeps the public source aligned with the published boundaries", async () => {
  const [pageSource, stageSource] = await Promise.all([
    readFile(new URL("../app/IngotSite.tsx", import.meta.url), "utf8"),
    readFile(new URL("../app/factoryStages.ts", import.meta.url), "utf8"),
  ]);

  assert.match(pageSource, /Ingot Chat/);
  assert.match(pageSource, /POST \/api\/v1\/events:batch/);
  assert.match(pageSource, /Optional Connector Host/);
  assert.match(pageSource, /Chat 调用白名单中的只读事实工具/);
  assert.match(pageSource, /check_data_quality/);
  assert.match(pageSource, /get_cycle_trace/);
  assert.match(pageSource, /teams operate source programs and field systems retain equipment control/i);
  assert.doesNotMatch(pageSource, oldDesktopTerms);
  assert.match(stageSource, /cycle\.started/);
  assert.match(stageSource, /cycle\.completed/);
  assert.doesNotMatch(stageSource, /cycle\.aborted|inspection\.completed|x\.quality\./);
});
