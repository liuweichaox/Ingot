// Searches complete public documents in the current language and opens the selected result.

"use client";

import { Combobox, ComboboxInput, ComboboxOption, ComboboxOptions } from "@headlessui/react";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import type { Lang } from "@/lib/docs";

type Item = { lang: Lang; slug: string; title: string; text: string };

export default function Search({ lang }: { lang: Lang }) {
  const router = useRouter();
  const [query, setQuery] = useState("");
  const [items, setItems] = useState<Item[]>([]);
  const [status, setStatus] = useState<"loading" | "ready" | "error">("loading");
  useEffect(() => {
    const controller = new AbortController();
    fetch("/search-index.json", { signal: controller.signal })
      .then((response) => { if (!response.ok) throw new Error("Search index unavailable"); return response.json(); })
      .then((data: Item[]) => { setItems(data); setStatus("ready"); })
      .catch(() => { if (!controller.signal.aborted) setStatus("error"); });
    return () => controller.abort();
  }, []);
  const normalized = query.trim().toLowerCase();
  const terms = normalized.split(/\s+/).filter(Boolean);
  const results = normalized ? items.filter((item) => item.lang === lang && terms.every((term) => `${item.title} ${item.text}`.toLowerCase().includes(term)))
    .sort((a, b) => Number(b.title.toLowerCase().includes(normalized)) - Number(a.title.toLowerCase().includes(normalized)))
    .slice(0, 8) : [];
  const message = status === "error" ? (lang === "zh" ? "搜索暂不可用，请使用目录。" : "Search unavailable. Use the navigation.")
    : status === "loading" ? (lang === "zh" ? "正在加载搜索索引…" : "Loading search index…")
    : normalized && !results.length ? (lang === "zh" ? "没有匹配结果，请尝试其他关键词。" : "No results. Try another keyword.") : "";
  const snippet = (item: Item) => {
    const position = item.text.toLowerCase().indexOf(terms[0]);
    const start = Math.max(0, position - 35);
    return `${start ? "…" : ""}${item.text.slice(start, start + 130)}${item.text.length > start + 130 ? "…" : ""}`;
  };
  return (
    <Combobox
      value={null}
      onChange={(item: Item | null) => {
        if (item) { setQuery(""); router.push(`/${lang}${item.slug ? `/${item.slug}` : ""}`); }
      }}
    >
      <div className="search">
        <ComboboxInput
          autoComplete="off"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder={lang === "zh" ? "搜索文档" : "Search docs"}
          aria-label={lang === "zh" ? "搜索文档" : "Search docs"}
        />
        {message && normalized && <div className="results search-status" role="status">{message}</div>}
        {results.length > 0 && (
          <ComboboxOptions anchor="bottom" className="z-20 mt-1 w-(--input-width) rounded-lg border border-[#253a35] bg-[#10201c] p-2 shadow-2xl [--anchor-gap:4px]">
            {results.map((item) => (
              <ComboboxOption
                key={`${item.lang}-${item.slug}`}
                value={item}
                className="cursor-pointer rounded-md px-3 py-2 text-sm text-[#cad8d3] outline-none data-focus:bg-[#172824] data-focus:text-white"
              >
                <strong>{item.title}</strong>
                <span className="search-snippet">{snippet(item)}</span>
              </ComboboxOption>
            ))}
          </ComboboxOptions>
        )}
      </div>
    </Combobox>
  );
}
