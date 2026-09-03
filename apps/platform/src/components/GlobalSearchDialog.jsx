// 提供角色范围内的功能搜索、键盘选择和可访问的结果提示。
import { Dialog, DialogBackdrop, DialogPanel, DialogTitle } from "@headlessui/react";
import { XMarkIcon } from "@heroicons/react/24/outline";
import { useEffect, useId, useMemo, useRef, useState } from "react";
import { cx, Input } from "../ui/components";

export default function GlobalSearchDialog({ open, onClose, navigate, entries }) {
  const [query, setQuery] = useState("");
  const [activeIndex, setActiveIndex] = useState(0);
  const inputRef = useRef(null);
  const activeOptionRef = useRef(null);
  const resultsId = useId();
  useEffect(() => {
    if (!open) return;
    setQuery("");
    setActiveIndex(0);
  }, [open]);
  const results = useMemo(() => {
    const keyword = query.trim().toLowerCase();
    if (!keyword) return entries;
    return entries.filter(item => `${item.label} ${item.section} ${item.description} ${item.aliases}`.toLowerCase().includes(keyword));
  }, [entries, query]);
  const selectedIndex = Math.min(activeIndex, results.length - 1);
  const selectedPath = results[selectedIndex]?.path;
  useEffect(() => {
    if (open) activeOptionRef.current?.scrollIntoView?.({ block: "nearest" });
  }, [open, selectedIndex, selectedPath]);

  function select(path) {
    onClose();
    navigate(path);
  }
  function handleKeyDown(event) {
    if (event.nativeEvent.isComposing || event.nativeEvent.keyCode === 229) return;
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      if (!results.length) return;
      const direction = event.key === "ArrowDown" ? 1 : -1;
      setActiveIndex((selectedIndex + direction + results.length) % results.length);
    } else if (event.key === "Enter") {
      event.preventDefault();
      if (selectedPath) select(selectedPath);
    }
  }

  return (
    <Dialog open={open} onClose={onClose} initialFocus={inputRef} className="relative z-100">
      <DialogBackdrop className="fixed inset-0 bg-slate-950/35 backdrop-blur-sm" />
      <div className="fixed inset-0 overflow-y-auto p-4 pt-[12vh] sm:p-6 sm:pt-[14vh]">
        <DialogPanel className="mx-auto w-full max-w-2xl overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl">
          <div className="border-b border-slate-100 p-4 sm:p-5">
            <div className="flex items-center justify-between gap-3">
              <DialogTitle className="text-sm font-semibold text-slate-950">功能搜索</DialogTitle>
              <button type="button" onClick={onClose} aria-label="关闭功能搜索" className="grid size-8 place-items-center rounded-lg text-slate-500 hover:bg-slate-100"><XMarkIcon className="size-4" /></button>
            </div>
            <p className="mt-1 text-xs text-slate-500">查找现场接入、工艺配置、生产运行、质量管理、工艺追因和系统功能。</p>
            <Input
              ref={inputRef}
              role="combobox"
              aria-label="搜索产品功能"
              aria-autocomplete="list"
              aria-expanded={open}
              aria-controls={resultsId}
              aria-activedescendant={selectedPath ? `${resultsId}-${selectedIndex}` : undefined}
              value={query}
              onChange={event => { setQuery(event.target.value); setActiveIndex(0); }}
              onKeyDown={handleKeyDown}
              placeholder="例如：采集配置、工艺规范、运行对比、检验任务"
              className="mt-4 h-11 rounded-xl bg-slate-50 px-4 focus:bg-white"
            />
          </div>
          <div id={resultsId} role="listbox" aria-label="匹配的功能" className="max-h-[55vh] overflow-y-auto p-2">
            {results.map((item, index) => (
              <button
                key={item.path}
                id={`${resultsId}-${index}`}
                ref={index === selectedIndex ? activeOptionRef : null}
                type="button"
                role="option"
                aria-selected={index === selectedIndex}
                tabIndex={-1}
                onMouseDown={event => event.preventDefault()}
                onMouseMove={() => setActiveIndex(index)}
                onClick={() => select(item.path)}
                className={cx("flex w-full items-start gap-3 rounded-xl px-3 py-3 text-left hover:bg-trajectory-50", index === selectedIndex && "bg-trajectory-50 ring-1 ring-inset ring-trajectory-500/30")}
              >
                <span className="mt-0.5 shrink-0 whitespace-nowrap rounded-md bg-slate-100 px-2 py-1 text-[11px] font-medium text-slate-600">{item.section}</span>
                <span className="min-w-0"><span className="block text-sm font-medium text-slate-900">{item.label}</span><span className="mt-0.5 block text-xs leading-5 text-slate-500">{item.description}</span></span>
              </button>
            ))}
          </div>
          {!results.length && <div className="px-4 py-10 text-center text-sm text-slate-500" role="status">没有匹配的功能。请换一个关键词。</div>}
          <div className="flex flex-wrap items-center justify-between gap-2 border-t border-slate-100 px-5 py-3 text-xs text-slate-500"><span>↑ ↓ 选择 · Enter 打开</span><span>Esc 关闭</span></div>
        </DialogPanel>
      </div>
    </Dialog>
  );
}
