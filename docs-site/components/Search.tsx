"use client";

import { useEffect, useState } from "react";
import type { Lang } from "@/lib/docs";

type Item = { lang: Lang; slug: string; title: string; text: string };

export default function Search({ lang }: { lang: Lang }) {
  const [query, setQuery] = useState("");
  const [items, setItems] = useState<Item[]>([]);
  useEffect(() => { fetch("/search-index.json").then((response) => response.json()).then(setItems).catch(() => setItems([])); }, []);
  const normalized = query.trim().toLowerCase();
  const results = normalized ? items.filter((item) => item.lang === lang && `${item.title} ${item.text}`.toLowerCase().includes(normalized)).slice(0, 6) : [];
  return <div className="search"><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder={lang === "zh" ? "搜索文档" : "Search docs"} />{results.length > 0 && <div className="results">{results.map((item) => <a key={item.slug} href={`/${lang}${item.slug ? `/${item.slug}` : ""}`}>{item.title}</a>)}</div>}</div>;
}
