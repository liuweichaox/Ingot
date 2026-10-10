// Loads the bilingual docs, rewrites cross-references for the docs site, and renders Markdown to HTML with a table of contents.

import { readFileSync, readdirSync, statSync } from "node:fs";
import path from "node:path";
import publicSlugs from "./public-docs.json";
import { unified } from "unified";
import remarkParse from "remark-parse";
import remarkGfm from "remark-gfm";
import remarkRehype from "remark-rehype";
import rehypeSlug from "rehype-slug";
import rehypeAutolinkHeadings from "rehype-autolink-headings";
import rehypeHighlight from "rehype-highlight";
import rehypeStringify from "rehype-stringify";

export type Lang = "zh" | "en";
export type Doc = { lang: Lang; slug: string; file: string; title: string; source: string };

const docsDir = path.resolve(process.cwd(), "../../docs");
const repositoryDir = path.resolve(docsDir, "..");
const repositoryUrl = "https://github.com/liuweichaox/Ingot";
const publicFiles = new Set(publicSlugs.flatMap((slug) =>
  slug === "index" ? ["index.md", "index.en.md"] : [`${slug}.md`, `${slug}.en.md`]));
const files = readdirSync(docsDir)
  .filter((name) => publicFiles.has(name))
  .sort();

export const docs: Doc[] = files.map((file) => {
  const source = readFileSync(path.join(docsDir, file), "utf8");
  const lang: Lang = file.endsWith(".en.md") ? "en" : "zh";
  const base = file.replace(/\.en\.md$|\.md$/g, "");
  return {
    lang,
    slug: base === "index" ? "" : base,
    file,
    title: source.match(/^#\s+(.+)$/m)?.[1]?.trim() || base,
    source,
  };
});

export const groups = [
  { key: "start", zh: "开始使用", en: "Getting started", slugs: ["", "getting-started", "status", "pilot"] },
  { key: "guides", zh: "操作指南", en: "Guides", slugs: ["data-connection", "deployment", "rollout", "troubleshooting"] },
  { key: "concepts", zh: "概念与架构", en: "Concepts & architecture", slugs: ["design", "optimization", "mechanism-knowledge", "production-architecture"] },
  { key: "reference", zh: "参考资料", en: "Reference", slugs: ["data-model", "faq", "glossary", "project-plan", "brand", "open-source-dependencies", "documentation-guide"] },
];

export const routeFor = (lang: Lang, slug: string) => `/${lang}${slug ? `/${slug}` : ""}`;
export const getDoc = (lang: Lang, slug: string) => docs.find((doc) => doc.lang === lang && doc.slug === slug);

function repositoryLink(target: string, kind: "link" | "image") {
  const [pathname, hash = ""] = target.split(/(?=#)/, 2);
  const resolved = path.resolve(docsDir, pathname);
  const relative = path.relative(repositoryDir, resolved).split(path.sep).join("/");
  if (!relative || relative.startsWith("../")) return target;

  let isDirectory = false;
  try {
    isDirectory = statSync(resolved).isDirectory();
  } catch {
    return target;
  }

  if (kind === "image" && !isDirectory)
    return `https://raw.githubusercontent.com/liuweichaox/Ingot/main/${relative}${hash}`;
  return `${repositoryUrl}/${isDirectory ? "tree" : "blob"}/main/${relative}${hash}`;
}

function rewriteDestination(doc: Doc, target: string, kind: "link" | "image") {
  if (/^(?:[a-z][a-z\d+.-]*:|\/|#)/i.test(target)) return target;
  const [pathname, hash = ""] = target.split(/(?=#)/, 2);

  if (pathname.endsWith(".md")) {
    if (pathname.startsWith("../") || pathname.startsWith("./")) {
      const resolved = path.resolve(docsDir, pathname);
      if (!resolved.startsWith(`${docsDir}${path.sep}`)) return repositoryLink(target, kind);
    }

    const file = path.basename(pathname);
    const linkedDoc = docs.find((candidate) => candidate.file === file);
    if (linkedDoc) return `${routeFor(linkedDoc.lang, linkedDoc.slug)}${hash}`;
  }

  if (pathname.startsWith("../") || pathname.startsWith("./")) return repositoryLink(target, kind);
  return target;
}

export async function renderDoc(doc: Doc) {
  const rewritten = doc.source.replace(/(!?\[[^\]]*\]\()([^\s)]+)([^)]*\))/g,
    (_match, prefix: string, target: string, suffix: string) =>
      `${prefix}${rewriteDestination(doc, target, prefix.startsWith("!") ? "image" : "link")}${suffix}`);
  const html = await unified().use(remarkParse).use(remarkGfm).use(remarkRehype, { allowDangerousHtml: false })
    .use(rehypeSlug).use(rehypeAutolinkHeadings, { behavior: "wrap" }).use(rehypeHighlight).use(rehypeStringify).process(rewritten);
  // Read headings from the rendered tree so code fences, inline markup, and
  // duplicate headings use exactly the same anchors as the article.
  const tree = unified().use(remarkParse).use(remarkGfm).parse(rewritten);
  const headings: { depth: number; title: string; id: string }[] = [];
  const renderedHeadings = [...String(html).matchAll(/<h([23]) id="([^"]+)"[^>]*>([\s\S]*?)<\/h[23]>/g)];
  const textOf = (node: { value?: string; children?: unknown[] }): string =>
    node.value || node.children?.map((child) => textOf(child as typeof node)).join("") || "";
  for (const node of tree.children) {
    if (node.type !== "heading" || (node.depth !== 2 && node.depth !== 3)) continue;
    const rendered = renderedHeadings[headings.length];
    if (rendered) headings.push({ depth: node.depth, title: textOf(node), id: rendered[2] });
  }
  return { html: String(html), toc: headings };
}
