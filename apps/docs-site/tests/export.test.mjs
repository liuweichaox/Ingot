import assert from "node:assert/strict";
import { readFile, readdir, stat } from "node:fs/promises";
import path from "node:path";
import test from "node:test";

const root = path.resolve(import.meta.dirname, "../../..");
const out = path.join(root, "apps/docs-site/out");

test("exports the bilingual product documentation journey", async () => {
  for (const file of ["zh/index.html", "en/index.html", "zh/design/index.html", "en/design/index.html", "zh/optimization/index.html", "en/optimization/index.html", "zh/mechanism-knowledge/index.html", "en/mechanism-knowledge/index.html", "zh/rollout/index.html", "en/rollout/index.html", "search-index.json", "sitemap.xml", "robots.txt"])
    assert.ok((await readFile(path.join(out, file))).length > 0, file);
  for (const slug of ["getting-started", "design", "optimization", "mechanism-knowledge", "data-connection", "production-architecture", "project-plan", "rollout", "deployment", "faq", "brand", "open-source-dependencies"])
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

test("publishes the experiment-decision journey and public references without interface documentation", async () => {
  const search = JSON.parse(await readFile(path.join(out, "search-index.json"), "utf8"));
  assert.equal(search.length, 26);
  assert.deepEqual(
    [...new Set(search.map((item) => item.slug))].sort(),
    ["", "brand", "data-connection", "deployment", "design", "faq", "getting-started", "mechanism-knowledge", "open-source-dependencies", "optimization", "production-architecture", "project-plan", "rollout"],
  );

  for (const lang of ["zh", "en"]) {
    const index = await readFile(path.join(out, lang, "index.html"), "utf8");
    const design = await readFile(path.join(out, lang, "design", "index.html"), "utf8");
    assert.match(index, lang === "zh" ? /减少无效实验/ : /avoid unproductive experiments/i);
    assert.match(design, lang === "zh" ? /设计目标/ : /Design objective/i);
    assert.match(index, lang === "zh" ? /更快找到达到目标的工艺条件/ : /reach target process conditions faster/i);
    assert.match(index, lang === "zh" ? /工艺配置.*现场接入.*生产运行.*质量管理.*工艺追因.*工艺研发/s : /process configuration.*field integration.*production runs.*quality management.*diagnosis.*process R(?:&amp;|&#x26;)D/is);
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
