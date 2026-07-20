import { cp, mkdir, readFile, readdir, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");
const docsDir = path.join(root, "docs");
const publicDir = path.join(root, "apps/docs-site/public");
const brandDir = path.join(publicDir, "brand");
await mkdir(brandDir, { recursive: true });
for (const name of ["ingot-mark-dark.svg", "ingot-lockup-dark.svg"])
  await cp(path.join(root, "apps/website/public/brand", name), path.join(brandDir, name));

const files = (await readdir(docsDir)).filter((name) => name.endsWith(".md")).sort();
const index = [];
for (const file of files) {
  const source = await readFile(path.join(docsDir, file), "utf8");
  const lang = file.endsWith(".en.md") ? "en" : "zh";
  const base = file.replace(/\.en\.md$|\.md$/g, "");
  const slug = base === "index" ? "" : base;
  const title = source.match(/^#\s+(.+)$/m)?.[1]?.trim() || base;
  index.push({ lang, slug, title, text: source.replace(/[`#>*_[\]()|-]/g, " ").replace(/\s+/g, " ").slice(0, 1200) });
}
await writeFile(path.join(publicDir, "search-index.json"), JSON.stringify(index), "utf8");
