// Checks the exported bilingual documentation, product scope, and link integrity.
import assert from "node:assert/strict";
import { readFile, readdir, stat } from "node:fs/promises";
import path from "node:path";
import test from "node:test";

const root = path.resolve(import.meta.dirname, "../../..");
const out = path.join(root, "apps/docs-site/out");

test("exports the bilingual product documentation journey", async () => {
  for (const file of ["zh/index.html", "en/index.html", "zh/getting-started/index.html", "en/getting-started/index.html", "zh/status/index.html", "en/status/index.html", "zh/pilot/index.html", "en/pilot/index.html", "zh/design/index.html", "en/design/index.html", "zh/optimization/index.html", "en/optimization/index.html", "zh/mechanism-knowledge/index.html", "en/mechanism-knowledge/index.html", "zh/rollout/index.html", "en/rollout/index.html", "search-index.json", "sitemap.xml", "robots.txt"])
    assert.ok((await readFile(path.join(out, file))).length > 0, file);
  for (const slug of ["getting-started", "status", "pilot", "design", "optimization", "mechanism-knowledge", "data-connection", "production-architecture", "project-plan", "rollout", "deployment", "faq", "brand", "open-source-dependencies", "glossary", "data-model", "troubleshooting", "documentation-guide"])
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

test("publishes the approved documentation catalogue and product boundaries", async () => {
  const search = JSON.parse(await readFile(path.join(out, "search-index.json"), "utf8"));
  assert.equal(search.length, 38);
  assert.deepEqual(
    [...new Set(search.map((item) => item.slug))].sort(),
    ["", "brand", "data-connection", "data-model", "deployment", "design", "documentation-guide", "faq", "getting-started", "glossary", "mechanism-knowledge", "open-source-dependencies", "optimization", "pilot", "production-architecture", "project-plan", "rollout", "status", "troubleshooting"],
  );

  for (const lang of ["zh", "en"]) {
    const index = await readFile(path.join(out, lang, "index.html"), "utf8");
    const design = await readFile(path.join(out, lang, "design", "index.html"), "utf8");
    assert.match(index, lang === "zh" ? /组织配方版本、实验记录与运行证据，支持质量分析、工艺追因和配方优化/ : /Organize recipe versions, experiment records, and run evidence to support quality analysis, process diagnosis, and recipe optimization/i);
    assert.match(design, lang === "zh" ? /设计目标/ : /Design objective/i);
    assert.match(index, lang === "zh" ? /下一份配方建议/ : /next recipe recommendation/i);
    assert.match(index, lang === "zh" ? /工艺配置.*现场接入.*生产运行.*质量管理.*工艺追因.*配方优化/s : /process configuration.*field integration.*production runs.*quality management.*diagnosis.*recipe optimization/is);
    assert.doesNotMatch(`${index}${design}`, /\/api\/|curl|ProductionEvent|InspectionRecord|endpoint|HTTP API/i);
  }


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

test("search indexes complete documents and navigation exposes current pages", async () => {
  const search = JSON.parse(await readFile(path.join(out, "search-index.json"), "utf8"));
  for (const item of search) {
    const source = await readFile(path.join(root, "docs", `${item.slug || "index"}${item.lang === "en" ? ".en" : ""}.md`), "utf8");
    const expected = source.replace(/[`#>*_[\]()|-]/g, " ").replace(/\s+/g, " ");
    assert.equal(item.text, expected, `${item.lang}/${item.slug}`);
  }
  const page = await readFile(path.join(out, "en", "data-model", "index.html"), "utf8");
  assert.match(page, /aria-current="page"/);
  assert.match(page, /Edit this page on GitHub/);
  assert.match(page, /Concepts &amp; architecture/);
});

test("table of contents links point to rendered article headings", async () => {
  const files = (await readdir(out, { recursive: true })).filter((file) => file.endsWith("index.html"));
  for (const file of files) {
    const html = await readFile(path.join(out, file), "utf8");
    const toc = html.match(/<aside class="toc"[\s\S]*?<\/aside>/)?.[0] || "";
    const article = html.match(/<article[\s\S]*?<\/article>/)?.[0] || "";
    const ids = new Set([...article.matchAll(/\bid="([^"]+)"/g)].map((match) => match[1]));
    for (const match of toc.matchAll(/href="#([^"]+)"/g))
      assert.ok(ids.has(match[1]), `${file} -> #${match[1]}`);
  }
});
