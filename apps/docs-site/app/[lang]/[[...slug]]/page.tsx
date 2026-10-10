// Renders bilingual public documents with navigation, reading paths, and source links.

import type { Metadata } from "next";
import Image from "next/image";
import { notFound } from "next/navigation";
import { docs, getDoc, groups, renderDoc, routeFor, type Lang } from "@/lib/docs";
import Search from "@/components/Search";

type Props = { params: Promise<{ lang: string; slug?: string[] }> };
export const dynamicParams = false;

export function generateStaticParams() {
  return docs.map((doc) => ({ lang: doc.lang, slug: doc.slug ? [doc.slug] : [] }));
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const value = await params;
  const lang = value.lang as Lang;
  const slug = value.slug?.join("/") || "";
  const doc = getDoc(lang, slug);
  if (!doc) return {};
  const alternate = getDoc(lang === "zh" ? "en" : "zh", slug);
  return {
    title: slug ? doc.title : { absolute: doc.title },
    alternates: {
      canonical: routeFor(lang, slug),
      languages: alternate ? { [lang]: routeFor(lang, slug), [alternate.lang]: routeFor(alternate.lang, slug) } : { [lang]: routeFor(lang, slug) },
    },
  };
}

export default async function DocPage({ params }: Props) {
  const value = await params;
  if (value.lang !== "zh" && value.lang !== "en") notFound();
  const lang = value.lang as Lang;
  const slug = value.slug?.join("/") || "";
  const doc = getDoc(lang, slug);
  if (!doc) notFound();
  const { html, toc } = await renderDoc(doc);
  const ordered = groups.flatMap((group) => group.slugs.map((item) => getDoc(lang, item)).filter(Boolean));
  const currentIndex = ordered.findIndex((item) => item?.slug === slug);
  const previous = currentIndex > 0 ? ordered[currentIndex - 1] : undefined;
  const next = currentIndex >= 0 && currentIndex < ordered.length - 1 ? ordered[currentIndex + 1] : undefined;
  const alternate = getDoc(lang === "zh" ? "en" : "zh", slug);

  const group = groups.find((item) => item.slugs.includes(slug));

  return (
    <div className="shell">
      <a className="skip-link" href="#doc-content">{lang === "zh" ? "跳到正文" : "Skip to content"}</a>
      <header>
        <a className="brand" href={routeFor(lang, "")}><Image src="/brand/ingot-lockup-dark.svg" alt="Ingot" width={142} height={36} priority /></a>
        <Search lang={lang} />
        <nav aria-label={lang === "zh" ? "站点导航" : "Site navigation"}><a href="https://github.com/liuweichaox/Ingot">GitHub</a><a href="https://ingotstack.com">{lang === "zh" ? "官网" : "Website"}</a>{alternate && <a href={routeFor(alternate.lang, slug)}>{lang === "zh" ? "English" : "中文"}</a>}</nav>
        <details className="mobile-doc-nav">
          <summary>{lang === "zh" ? "目录" : "Menu"}</summary>
          <div>
            {groups.map((group) => <section key={group.key}><h2>{group[lang]}</h2>{group.slugs.map((item) => {
              const target = getDoc(lang, item) || (lang === "en" ? getDoc("zh", item) : undefined);
              if (!target) return null;
              return <a className={item === slug ? "active" : ""} aria-current={item === slug ? "page" : undefined} key={item} href={routeFor(target.lang, item)}>{target.title}</a>;
            })}</section>)}
          </div>
        </details>
      </header>
      <aside className="sidebar" aria-label={lang === "zh" ? "文档导航" : "Documentation navigation"}>
        {groups.map((group) => <section key={group.key}><h2>{group[lang]}</h2>{group.slugs.map((item) => {
          const target = getDoc(lang, item) || (lang === "en" ? getDoc("zh", item) : undefined);
          if (!target) return null;
          return <a className={item === slug ? "active" : ""} aria-current={item === slug ? "page" : undefined} key={item} href={routeFor(target.lang, item)}>{target.title}{lang === "en" && target.lang === "zh" ? " · Chinese reference" : ""}</a>;
        })}</section>)}
      </aside>
      <main id="doc-content" tabIndex={-1}>
        <nav className="breadcrumb" aria-label={lang === "zh" ? "当前位置" : "Breadcrumb"}><a href={routeFor(lang, "")}>{lang === "zh" ? "文档" : "Documentation"}</a>{slug && <><span aria-hidden="true"> / </span><span>{group?.[lang]}</span></>}</nav>
        {!slug && <nav className="journeys" aria-label={lang === "zh" ? "按任务阅读" : "Read by task"}>
          {[
            { slug: "getting-started", zh: "运行第一个环境", en: "Run your first environment", detailZh: "准备依赖、启动服务并验证运行结果。", detailEn: "Prepare dependencies, start services, and verify the result." },
            { slug: "data-connection", zh: "连接现场数据", en: "Connect field data", detailZh: "了解接入约束与数据验证流程。", detailEn: "Understand integration boundaries and data validation." },
            { slug: "design", zh: "理解系统设计", en: "Understand the system", detailZh: "查阅核心概念、证据边界与职责划分。", detailEn: "Review core concepts, evidence boundaries, and responsibilities." },
            { slug: "troubleshooting", zh: "排查运行问题", en: "Troubleshoot a problem", detailZh: "按症状定位问题并收集诊断信息。", detailEn: "Investigate symptoms and collect diagnostic information." },
          ].filter((item) => getDoc(lang, item.slug)).map((item) => <a key={item.slug} href={routeFor(lang, item.slug)}><strong>{item[lang]}</strong><span>{lang === "zh" ? item.detailZh : item.detailEn}</span></a>)}
        </nav>}
        <article dangerouslySetInnerHTML={{ __html: html }} />
        <footer className="pager" aria-label={lang === "zh" ? "前后文档" : "Adjacent documents"}>
          {previous && <a href={routeFor(lang, previous.slug)}>← {previous.title}</a>}
          {next && <a href={routeFor(lang, next.slug)}>{next.title} →</a>}
        </footer>
        <p className="source"><a href={`https://github.com/liuweichaox/Ingot/blob/main/docs/${doc.file}`}>{lang === "zh" ? "在 GitHub 上编辑此页" : "Edit this page on GitHub"}</a><span> · </span><a href="https://github.com/liuweichaox/Ingot/issues/new">{lang === "zh" ? "反馈文档问题" : "Report a documentation issue"}</a></p>
      </main>
      <aside className="toc" aria-label={lang === "zh" ? "本页目录" : "On this page"}><strong>{lang === "zh" ? "本页目录" : "On this page"}</strong>{toc.map((item) => <a className={`depth-${item.depth}`} key={item.id} href={`#${item.id}`}>{item.title}</a>)}</aside>
    </div>
  );
}
