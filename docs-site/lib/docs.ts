import { readFileSync, readdirSync, statSync } from "node:fs";
import path from "node:path";
import GithubSlugger from "github-slugger";
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

const docsDir = path.resolve(process.cwd(), "../docs");
const repositoryDir = path.resolve(docsDir, "..");
const repositoryUrl = "https://github.com/liuweichaox/Ingot";
const files = readdirSync(docsDir).filter((name) => name.endsWith(".md")).sort();

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
  { key: "start", zh: "开始使用", en: "Get started", slugs: ["", "tutorial-getting-started"] },
  { key: "chat", zh: "Chat", en: "Chat", slugs: ["chat"] },
  { key: "agent", zh: "Ingot Agent 桌面端", en: "Ingot Agent desktop", slugs: ["desktop-agent"] },
  { key: "ops", zh: "部署运维", en: "Deployment & operations", slugs: ["tutorial-deployment", "tutorial-configuration", "faq"] },
  { key: "architecture", zh: "架构开发", en: "Architecture & development", slugs: ["architecture", "design", "modules", "tutorial-development"] },
  { key: "reference", zh: "参考资料", en: "References", slugs: ["rfc-production-events", "brand"] },
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
  const slugger = new GithubSlugger();
  const toc = [...rewritten.matchAll(/^(#{2,3})\s+(.+)$/gm)].map((match) => ({
    depth: match[1].length,
    title: match[2].replace(/[`*_]/g, ""),
    id: slugger.slug(match[2].replace(/[`*_]/g, "")),
  }));
  return { html: String(html), toc };
}
