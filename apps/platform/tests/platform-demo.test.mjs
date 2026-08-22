// 验证 demo API 的鉴权、核心业务数据与可切换故障场景。
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { once } from "node:events";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";

const root = new URL("../../..", import.meta.url);

async function waitForHealth(baseUrl, child) {
  for (let attempt = 0; attempt < 80; attempt += 1) {
    if (child.exitCode !== null) throw new Error(`模拟服务提前退出：${child.exitCode}`);
    try {
      const response = await fetch(`${baseUrl}/health`);
      if (response.ok) return;
    } catch {
      // 服务仍在启动。
    }
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  throw new Error("模拟服务未在预期时间内启动");
}

async function runNode(args, options) {
  const child = spawn(process.execPath, args, { ...options, stdio: ["ignore", "pipe", "pipe"] });
  let stdout = "";
  let stderr = "";
  child.stdout.on("data", chunk => { stdout += chunk; });
  child.stderr.on("data", chunk => { stderr += chunk; });
  const [code] = await once(child, "exit");
  return { code, stdout, stderr };
}

test("platform demo serves authenticated workflow data and deterministic failure modes", async () => {
  const demoPort = 44110;
  const baseUrl = `http://127.0.0.1:${demoPort}`;
  const child = spawn(process.execPath, ["scripts/platform-demo.mjs"], {
    cwd: root,
    env: { ...process.env, INGOT_DEMO_PORT: String(demoPort) },
    stdio: ["ignore", "pipe", "pipe"],
  });
  try {
    await waitForHealth(baseUrl, child);
    const login = await fetch(`${baseUrl}/api/v1/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ username: "demo", password: "demo" }),
    });
    assert.equal(login.status, 200);
    const identity = await login.json();
    assert.deepEqual(identity.roles, ["process.engineer"]);
    const headers = { Authorization: `Bearer ${identity.token}` };

    for (const endpoint of ["/api/v1/process-data-models", "/api/v1/process-executions", "/api/v1/inspection-tasks?status=all", "/api/edges"]) {
      const response = await fetch(`${baseUrl}${endpoint}`, { headers });
      assert.equal(response.status, 200, endpoint);
      const payload = await response.json();
      assert.ok(payload.data.length > 0, `${endpoint} should contain demo data`);
    }

    const mechanismResponse = await fetch(
      `${baseUrl}/api/v1/research-projects/research-active/mechanism-claims`,
      { headers },
    );
    assert.equal(mechanismResponse.status, 200);
    const mechanisms = (await mechanismResponse.json()).data;
    for (const status of ["draft", "reviewed", "active", "falsified", "retired"]) {
      assert.ok(mechanisms.some(claim => claim.status === status), `missing mechanism status ${status}`);
    }
    const applied = mechanisms.find(claim => claim.name === "已激活的模压安全窗口");
    assert.ok(applied.constraints.some(constraint => constraint.severity === "hard"));
    assert.ok(applied.constraints.some(constraint => constraint.severity === "soft"));
    assert.equal(applied.forbiddenCombinations.length, 1);

    const conflictsResponse = await fetch(
      `${baseUrl}/api/v1/research-projects/research-active/mechanism-claims/conflicts`,
      { headers },
    );
    assert.equal(conflictsResponse.status, 200);
    assert.equal((await conflictsResponse.json()).data[0].status, "open");

    await fetch(`${baseUrl}/__demo/state?mode=empty`);
    assert.equal((await (await fetch(`${baseUrl}/api/v1/process-executions`, { headers })).json()).data.length, 0);
    await fetch(`${baseUrl}/__demo/state?mode=forbidden`);
    assert.equal((await fetch(`${baseUrl}/api/v1/process-executions`, { headers })).status, 403);
    await fetch(`${baseUrl}/__demo/state?mode=error`);
    assert.equal((await fetch(`${baseUrl}/api/v1/process-executions`, { headers })).status, 503);
    await fetch(`${baseUrl}/__demo/state?mode=normal`);
  } finally {
    child.kill("SIGTERM");
    if (child.exitCode === null) await once(child, "exit");
  }
});

test("controlled-pilot verifier produces a checksummed read-only workflow artifact", async () => {
  const demoPort = 44111;
  const baseUrl = `http://127.0.0.1:${demoPort}`;
  const temporary = await mkdtemp(join(tmpdir(), "ingot-pilot-verifier-"));
  const output = join(temporary, "pilot-workflow.json");
  const demo = spawn(process.execPath, ["scripts/platform-demo.mjs"], {
    cwd: root,
    env: { ...process.env, INGOT_DEMO_PORT: String(demoPort) },
    stdio: ["ignore", "pipe", "pipe"],
  });
  try {
    await waitForHealth(baseUrl, demo);
    const result = await runNode(["scripts/verify-pilot-workflow.mjs", "--output", output], {
      cwd: root,
      env: {
        ...process.env,
        INGOT_PLATFORM_URL: baseUrl,
        INGOT_ACCEPTANCE_USERNAME: "admin",
        INGOT_ACCEPTANCE_PASSWORD: "admin12345",
      },
    });
    assert.equal(result.code, 0, result.stderr || result.stdout);
    assert.match(result.stdout, /business-workflow-passed/);
    const artifact = JSON.parse(await readFile(output, "utf8"));
    assert.equal(artifact.result, "business-workflow-passed");
    assert.ok(artifact.checks.length >= 10);
    assert.ok(artifact.checks.every(check => check.passed));
    assert.match(artifact.boundary, /不等于生产准入/);
    assert.match(await readFile(`${output}.sha256`, "utf8"), /^[a-f0-9]{64}\s+/);
  } finally {
    demo.kill("SIGTERM");
    if (demo.exitCode === null) await once(demo, "exit");
    await rm(temporary, { recursive: true, force: true });
  }
});
