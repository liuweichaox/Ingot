import assert from "node:assert/strict";
import { readFile, readdir } from "node:fs/promises";
import test from "node:test";

const sourceRoot = new URL("../src/", import.meta.url);
const view = await readFile(new URL("../src/views/ChatView.vue", import.meta.url), "utf8");
const http = await readFile(new URL("../src/api/http.js", import.meta.url), "utf8");
const app = await readFile(new URL("../src/App.vue", import.meta.url), "utf8");
const router = await readFile(new URL("../src/router/index.js", import.meta.url), "utf8");

async function readSources(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  return (await Promise.all(entries.map(async (entry) => {
    const url = new URL(`${entry.name}${entry.isDirectory() ? "/" : ""}`, directory);
    return entry.isDirectory() ? readSources(url) : readFile(url, "utf8");
  }))).flat();
}

test("Chat UI is the primary capability-driven, Actor-scoped entry", () => {
  assert.match(app, /index="\/chat"/);
  assert.match(app, /Chat 工艺分析/);
  assert.match(router, /redirect: "\/chat"/);
  assert.match(router, /path: "\/chat"/);
  assert.match(router, /ChatView\.vue/);
  assert.match(view, /\/api\/v1\/chat\/capabilities/);
  assert.match(view, /\/api\/v1\/chat\/runs\?\$\{query\}/);
  assert.match(view, /supportsMode\('deep'\)/);
  assert.match(view, /X-Ingot-Actor/);
});

test("Chat UI resumes SSE and renders analysis evidence, charts, roles, and usage", () => {
  assert.match(http, /Last-Event-ID/);
  assert.match(view, /lastEventId/);
  assert.match(view, /discussion\.participant_failed/);
  assert.match(view, /成本未知/);
  assert.match(view, /事实证据抽屉/);
  assert.match(view, /snapshot\?\.answer\?\.charts\?\.length/);
  assert.match(view, /多角色调查/);
  assert.match(view, /对话历史/);
});

test("public source is Chat-only and has no code-generation controls or restricted API calls", async () => {
  const source = (await readSources(sourceRoot)).join("\n");
  assert.doesNotMatch(source, /\/api\/v1\/agent(?:\/|\b)/);
  assert.doesNotMatch(source, /\/api\/v1\/agent\/artifacts/);
  assert.doesNotMatch(source, /\/api\/v1\/connector-workspaces/);
  assert.doesNotMatch(source, /approve-package/);
  assert.doesNotMatch(source, /下载连接器包|生成连接器包|连接器源码工作区/);
  assert.doesNotMatch(router, /path: "\/agent"/);
  assert.doesNotMatch(app, /连接器工程 Agent/);
});
