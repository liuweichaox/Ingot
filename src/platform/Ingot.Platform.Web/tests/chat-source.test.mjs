import assert from "node:assert/strict";
import { readFile, readdir } from "node:fs/promises";
import test from "node:test";

const sourceRoot = new URL("../src/", import.meta.url);
const view = await readFile(new URL("../src/views/ChatView.vue", import.meta.url), "utf8");
const http = await readFile(new URL("../src/api/http.js", import.meta.url), "utf8");
const app = await readFile(new URL("../src/App.vue", import.meta.url), "utf8");
const router = await readFile(new URL("../src/router/index.js", import.meta.url), "utf8");
const edges = await readFile(new URL("../src/views/EdgesView.vue", import.meta.url), "utf8");

async function readSources(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  return (await Promise.all(entries.map(async (entry) => {
    const url = new URL(`${entry.name}${entry.isDirectory() ? "/" : ""}`, directory);
    return entry.isDirectory() ? readSources(url) : readFile(url, "utf8");
  }))).flat();
}

test("Ingot Chat is the primary production-fact dialogue entry", () => {
  assert.match(app, /index="\/chat"/);
  assert.match(app, /Ingot Chat/);
  assert.match(router, /redirect: "\/chat"/);
  assert.match(router, /path: "\/chat"/);
  assert.match(router, /ChatView\.vue/);
  assert.match(view, /Ingot Chat/);
  assert.match(view, /生产数据/);
  assert.match(view, /证据抽屉/);
  assert.match(view, /\/api\/v1\/chat\/capabilities/);
  assert.match(view, /\/api\/v1\/chat\/runs\?\$\{query\}/);
  assert.match(view, /X-Ingot-Actor/);
  assert.doesNotMatch(view, /Chat 工艺分析|多角色调查|深度协作调查|模型用量|角色状态/);
});

test("Ingot Chat resumes SSE and renders fact queries, evidence, and charts", () => {
  assert.match(http, /Last-Event-ID/);
  assert.match(view, /lastEventId/);
  assert.match(view, /discussion\.participant_failed/);
  assert.match(view, /调查说明/);
  assert.match(view, /事实查询/);
  assert.match(view, /证据抽屉/);
  assert.match(view, /snapshot\?\.answer\?\.charts\?\.length/);
  assert.match(view, /对话记录/);
  assert.doesNotMatch(view, /roleLabel|usageText|stageLabel/);
});

test("data ingress is presented as user-owned adapter status", () => {
  assert.match(app, /数据接入节点/);
  assert.match(edges, /用户自行部署的数据适配器接入状态/);
  assert.match(edges, /数据适配器地址/);
  assert.doesNotMatch(edges, /生成连接器|连接器打包|连接器源码工作区/);
});

test("public source is Chat-only and has no desktop, code-generation, or Agent product surface", async () => {
  const source = (await readSources(sourceRoot)).join("\n");
  assert.doesNotMatch(source, /\/api\/v1\/agent(?:\/|\b)/);
  assert.doesNotMatch(source, /\/api\/v1\/agent\/artifacts/);
  assert.doesNotMatch(source, /\/api\/v1\/connector-workspaces/);
  assert.doesNotMatch(source, /approve-package/);
  assert.doesNotMatch(source, /下载连接器包|生成连接器包|连接器源码工作区/);
  assert.doesNotMatch(source, /桌面(?:端|版)?|代码生成|生成代码|\b[Aa]gent\b/);
  assert.doesNotMatch(router, /path: "\/agent"/);
  assert.doesNotMatch(app, /连接器工程 Agent/);
});
