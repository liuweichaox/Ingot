// 验证 demo API 的鉴权、核心业务数据与可切换故障场景。
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { once } from "node:events";
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
