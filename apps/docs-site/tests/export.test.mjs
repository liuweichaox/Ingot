import assert from "node:assert/strict";
import { readFile, readdir, stat } from "node:fs/promises";
import path from "node:path";
import test from "node:test";

const root = path.resolve(import.meta.dirname, "../../..");
const out = path.join(root, "apps/docs-site/out");

test("exports the bilingual product documentation journey", async () => {
  for (const file of ["zh/index.html", "en/index.html", "zh/design/index.html", "en/design/index.html", "zh/rollout/index.html", "en/rollout/index.html", "search-index.json", "sitemap.xml", "robots.txt"])
    assert.ok((await readFile(path.join(out, file))).length > 0, file);
  for (const slug of ["design", "rollout", "faq"])
    for (const lang of ["zh", "en"])
      assert.ok((await readFile(path.join(out, lang, slug, "index.html"))).length > 0, `${lang}/${slug}`);

  const zh = await readFile(path.join(out, "zh/index.html"), "utf8");
  const en = await readFile(path.join(out, "en/index.html"), "utf8");
  assert.match(zh, /<html lang="zh-CN">/);
  assert.match(en, /<html lang="en">/);
  assert.match(zh, /hrefLang="en"/i);
  assert.match(en, /hrefLang="zh"/i);
});

test("uses the exact official brand assets", async () => {
  for (const name of await readdir(path.join(root, "apps/website/public/brand"))) {
    const official = await readFile(path.join(root, "apps/website/public/brand", name));
    const docs = await readFile(path.join(root, "apps/docs-site/public/brand", name));
    assert.deepEqual(docs, official, name);
  }
});

test("publishes the AI process R&D product journey without interface or developer documentation", async () => {
  const search = JSON.parse(await readFile(path.join(out, "search-index.json"), "utf8"));
  assert.equal(search.length, 8);
  assert.deepEqual(
    [...new Set(search.map((item) => item.slug))].sort(),
    ["", "design", "faq", "rollout"],
  );

  for (const lang of ["zh", "en"]) {
    const index = await readFile(path.join(out, lang, "index.html"), "utf8");
    const design = await readFile(path.join(out, lang, "design", "index.html"), "utf8");
    assert.match(index, lang === "zh" ? /AI 工艺研发系统/ : /AI Process R&amp;D/i);
    assert.match(design, lang === "zh" ? /工艺研发闭环/ : /Process R&amp;D Loop/i);
    assert.match(index, lang === "zh" ? /缩短工艺研发周期/ : /shorten development cycles/i);
    assert.doesNotMatch(`${index}${design}`, /\/api\/|curl|ProductionEvent|InspectionRecord|endpoint|HTTP API/i);
  }
  assert.doesNotMatch(JSON.stringify(search), /\/api\/|curl|ProductionEvent|InspectionRecord|endpoint|HTTP API/i);

  for (const slug of ["rfc-production-events", "tutorial-development", "architecture", "modules", "ingot-chat", "use-cases"])
    await assert.rejects(readFile(path.join(out, "zh", slug, "index.html")));
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
