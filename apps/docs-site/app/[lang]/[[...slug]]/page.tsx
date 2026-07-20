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

  return (
    <div className="shell">
      <header>
        <a className="brand" href={routeFor(lang, "")}><Image src="/brand/ingot-lockup-dark.svg" alt="Ingot" width={142} height={36} priority /></a>
        <Search lang={lang} />
        <nav><a href="https://ingotstack.com">Website</a><a href="https://github.com/liuweichaox/Ingot">GitHub</a>{alternate && <a href={routeFor(alternate.lang, slug)}>{lang === "zh" ? "English" : "中文"}</a>}</nav>
      </header>
      <aside className="sidebar">
        {groups.map((group) => <section key={group.key}><h2>{group[lang]}</h2>{group.slugs.map((item) => {
          const target = getDoc(lang, item) || (lang === "en" ? getDoc("zh", item) : undefined);
          if (!target) return null;
          return <a className={item === slug ? "active" : ""} key={item} href={routeFor(target.lang, item)}>{target.title}{lang === "en" && target.lang === "zh" ? " · Chinese reference" : ""}</a>;
        })}</section>)}
      </aside>
      <main>
        <article dangerouslySetInnerHTML={{ __html: html }} />
        <footer className="pager">
          {previous && <a href={routeFor(lang, previous.slug)}>← {previous.title}</a>}
          {next && <a href={routeFor(lang, next.slug)}>{next.title} →</a>}
        </footer>
        <a className="source" href={`https://github.com/liuweichaox/Ingot/blob/main/docs/${doc.file}`}>{lang === "zh" ? "在 GitHub 查看源文件" : "View source on GitHub"}</a>
      </main>
      <aside className="toc"><strong>{lang === "zh" ? "本页目录" : "On this page"}</strong>{toc.map((item) => <a className={`depth-${item.depth}`} key={item.id} href={`#${item.id}`}>{item.title}</a>)}</aside>
    </div>
  );
}
