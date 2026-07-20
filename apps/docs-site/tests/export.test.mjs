import assert from "node:assert/strict";
import { readFile, readdir, stat } from "node:fs/promises";
import path from "node:path";
import test from "node:test";

const root = path.resolve(import.meta.dirname, "../../..");
const out = path.join(root, "apps/docs-site/out");

test("exports bilingual Chat and event-ingestion documentation", async () => {
  for (const file of ["zh/index.html", "en/index.html", "zh/chat/index.html", "en/chat/index.html", "zh/rfc-production-events/index.html", "en/rfc-production-events/index.html", "search-index.json", "sitemap.xml", "robots.txt"])
    assert.ok((await readFile(path.join(out, file))).length > 0, file);
  for (const slug of ["chat", "rfc-production-events", "architecture", "design", "modules", "tutorial-getting-started", "tutorial-configuration", "tutorial-deployment", "faq", "brand"])
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
});

test("uses the exact official brand assets", async () => {
  for (const name of await readdir(path.join(root, "apps/website/public/brand"))) {
    const official = await readFile(path.join(root, "apps/website/public/brand", name));
    const docs = await readFile(path.join(root, "apps/docs-site/public/brand", name));
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

test("publishes only Ingot Chat and the standard event contract as public AI and ingestion entry points", async () => {
  for (const lang of ["zh", "en"]) {
    const chat = await readFile(path.join(out, lang, "chat", "index.html"), "utf8");
    const events = await readFile(path.join(out, lang, "rfc-production-events", "index.html"), "utf8");
    assert.match(chat, /\/api\/v1\/chat\/runs/);
    assert.match(chat, /check_data_quality/);
    assert.match(chat, /get_cycle_trace/);
    assert.match(events, /\/api\/v1\/events:batch/);
    assert.match(events, /ackSeq/);
    assert.match(events, /edge\/\{edgeId\}\//);
    assert.doesNotMatch(chat, /connector-workspaces|awaiting-package-approval|Tauri 2/i);
    assert.doesNotMatch(events, /connector-workspaces|awaiting-package-approval|Tauri 2/i);
  }
});

test("does not publish legacy desktop or code-generation product copy", async () => {
  const files = (await readdir(out, { recursive: true })).filter((file) => file.endsWith(".html"));
  for (const file of files) {
    const html = await readFile(path.join(out, file), "utf8");
    assert.doesNotMatch(html, /Ingot Agent|desktop Agent|desktop-agent|code generation|code-generation|connector-workspaces|awaiting-package-approval|SHA256SUMS|AppImage|SmartScreen|notarized/i, file);
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
