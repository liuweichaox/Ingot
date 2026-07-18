import assert from "node:assert/strict";
import { readFile, readdir, stat } from "node:fs/promises";
import path from "node:path";
import test from "node:test";

const root = path.resolve(import.meta.dirname, "../..");
const out = path.join(root, "docs-site/out");

test("exports bilingual core pages and search", async () => {
  for (const file of ["zh/index.html", "en/index.html", "zh/chat/index.html", "en/chat/index.html", "zh/desktop-agent/index.html", "en/desktop-agent/index.html", "search-index.json", "sitemap.xml", "robots.txt"])
    assert.ok((await readFile(path.join(out, file))).length > 0, file);
  for (const slug of ["chat", "desktop-agent", "architecture", "design", "modules", "tutorial-getting-started", "tutorial-configuration", "tutorial-deployment", "faq"])
    for (const lang of ["zh", "en"])
      assert.ok((await readFile(path.join(out, lang, slug, "index.html"))).length > 0, `${lang}/${slug}`);

  const zh = await readFile(path.join(out, "zh/index.html"), "utf8");
  const en = await readFile(path.join(out, "en/index.html"), "utf8");
  assert.match(zh, /<html lang="zh-CN">/);
  assert.match(en, /<html lang="en">/);
  assert.match(zh, /hrefLang="en"/i);
  assert.match(en, /hrefLang="zh"/i);
  assert.match(zh, /https:\/\/github\.com\/liuweichaox\/Ingot\/blob\/main\/CONTRIBUTING\.md/);
  assert.match(en, /https:\/\/github\.com\/liuweichaox\/Ingot\/blob\/main\/CONTRIBUTING\.en\.md/);
  assert.doesNotMatch(zh, /blob\/main\/CONTRIBUTING(?:["#?])/);
  assert.doesNotMatch(en, /blob\/main\/CONTRIBUTING\.en(?:["#?])/);
});

test("uses the exact official brand assets", async () => {
  for (const name of await readdir(path.join(root, "site/public/brand"))) {
    const official = await readFile(path.join(root, "site/public/brand", name));
    const docs = await readFile(path.join(root, "docs-site/public/brand", name));
    assert.deepEqual(docs, official, name);
    const source = await readFile(path.join(root, "images/logo", name));
    assert.deepEqual(official, source, `${name} source`);
  }
});

test("uses the canonical repository links", async () => {
  const html = await readFile(path.join(out, "zh", "index.html"), "utf8");
  const brand = await readFile(path.join(out, "zh", "brand", "index.html"), "utf8");
  assert.match(html, /https:\/\/github\.com\/liuweichaox\/Ingot/);
  assert.doesNotMatch(html, /github\.com\/IngotStack\/Ingot/);
  assert.match(brand, /github\.com\/liuweichaox\/Ingot\/blob\/main\/images\/logo\/ingot-mark\.svg/);
  assert.match(brand, /github\.com\/liuweichaox\/Ingot\/tree\/main\/images\/logo/);
});

test("publishes separate Chat and desktop Agent contracts", async () => {
  for (const lang of ["zh", "en"]) {
    const chat = await readFile(path.join(out, lang, "chat", "index.html"), "utf8");
    const agent = await readFile(path.join(out, lang, "desktop-agent", "index.html"), "utf8");
    assert.match(chat, /\/api\/v1\/chat\/runs/);
    assert.match(chat, /check_data_quality/);
    assert.match(chat, /get_cycle_trace/);
    assert.doesNotMatch(chat, /connector-workspaces/);
    assert.match(agent, /Ingot Agent/);
    assert.match(agent, /Tauri 2/);
    assert.match(agent, /X-Ingot-Client/);
    assert.match(agent, /ingot-agent-desktop/);
    assert.match(agent, /awaiting-package-approval/);
    assert.match(agent, /approve-package/);
    assert.match(agent, /GET \/api\/v1\/connector-workspaces\/\{id\}\/package/);
    assert.match(agent, /stdin\/json-lines/);
    assert.match(agent, /stdout\/production-event-json-lines/);
    assert.match(agent, /github\.com\/liuweichaox\/Ingot\/releases\/latest/);
    assert.doesNotMatch(agent, /awaiting-publish-approval/);
  }
});

test("all exported internal document links resolve", async () => {
  const files = (await readdir(out, { recursive: true })).filter((file) => file.endsWith("index.html"));
  for (const file of files) {
    const html = await readFile(path.join(out, file), "utf8");
    for (const match of html.matchAll(/href="\/(zh|en)(?:\/([^"#?]*))?/g)) {
      const target = path.join(out, match[1], match[2] || "", "index.html");
      assert.ok((await readFile(target)).length > 0, `${file} -> ${target}`);
    }
  }
});

test("all exported local links and assets resolve", async () => {
  const files = (await readdir(out, { recursive: true })).filter((file) => file.endsWith(".html"));
  for (const file of files) {
    const html = await readFile(path.join(out, file), "utf8");
    assert.doesNotMatch(html, /\b(?:href|src)="\.\.?\//, file);
    for (const match of html.matchAll(/\b(?:href|src)="(\/[^"#?]*)(?:[?#][^"]*)?"/g)) {
      const urlPath = decodeURIComponent(match[1]);
      const target = path.join(out, urlPath);
      const candidates = path.extname(urlPath) ? [target] : [target, path.join(target, "index.html")];
      let resolved = false;
      for (const candidate of candidates) {
        try {
          const info = await stat(candidate);
          resolved ||= info.isFile();
        } catch {
          // Try the next static-export representation.
        }
      }
      assert.ok(resolved, `${file} -> ${urlPath}`);
    }
  }
});

test("does not publish Agent data-source network testing", async () => {
  const files = (await readdir(out, { recursive: true })).filter((file) => file.endsWith(".html"));
  for (const file of files) {
    const html = await readFile(path.join(out, file), "utf8");
    assert.doesNotMatch(html, /test_http_connector|ConnectorTest|AllowedNetworkTargets/i, file);
  }
});
