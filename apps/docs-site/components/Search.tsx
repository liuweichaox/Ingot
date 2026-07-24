"use client";

import { Combobox, ComboboxInput, ComboboxOption, ComboboxOptions } from "@headlessui/react";
import { useEffect, useState } from "react";
import type { Lang } from "@/lib/docs";

type Item = { lang: Lang; slug: string; title: string; text: string };

export default function Search({ lang }: { lang: Lang }) {
  const [query, setQuery] = useState("");
  const [items, setItems] = useState<Item[]>([]);
  useEffect(() => { fetch("/search-index.json").then((response) => response.json()).then(setItems).catch(() => setItems([])); }, []);
  const normalized = query.trim().toLowerCase();
  const results = normalized ? items.filter((item) => item.lang === lang && `${item.title} ${item.text}`.toLowerCase().includes(normalized)).slice(0, 6) : [];
  return (
    <Combobox
      value={null}
      onChange={(item: Item | null) => {
        if (item) window.location.assign(`/${lang}${item.slug ? `/${item.slug}` : ""}`);
      }}
    >
      <div className="search">
        <ComboboxInput
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder={lang === "zh" ? "搜索文档" : "Search docs"}
          aria-label={lang === "zh" ? "搜索文档" : "Search docs"}
        />
        {results.length > 0 && (
          <ComboboxOptions anchor="bottom" className="z-20 mt-1 w-(--input-width) rounded-lg border border-[#253a35] bg-[#10201c] p-2 shadow-2xl [--anchor-gap:4px]">
            {results.map((item) => (
              <ComboboxOption
                key={`${item.lang}-${item.slug}`}
                value={item}
                className="cursor-pointer rounded-md px-3 py-2 text-sm text-[#cad8d3] outline-none data-focus:bg-[#172824] data-focus:text-white"
              >
                {item.title}
              </ComboboxOption>
            ))}
          </ComboboxOptions>
        )}
      </div>
    </Combobox>
  );
}
