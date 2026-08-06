/**
 * apps/platform/src/App.jsx —— Ingot 工程工作台 Shell v2.1
 *
 * 结构：顶栏（品牌 + 全局分析范围轴 + 搜索 + 系统）
 *      · 图标轨（5 个一级模块：总览 · 运行 · 追因 · 优化 · 配置，带 Evidence Gold 待决角标）
 *      · 二级导航（分组，⌘B 折叠）
 *      · 主内容区
 *      · 证据面板（⌘I）
 *      · 状态栏（平台健康 / 节点 / 上行 / 准入率 / 对比暂存区）
 *
 * 本文件只负责导航与外壳；页面内容仍复用现有 pages/*。
 * 尚未实现的新页面渲染 <PagePlaceholder>，不阻塞上线。
 */
import { Dialog, DialogBackdrop, DialogPanel, Menu, MenuButton, MenuItem, MenuItems } from "@headlessui/react";
import {
  ArchiveBoxIcon,
  BellIcon,
  Bars3Icon,
  ChevronLeftIcon,
  ChevronRightIcon,
  Cog6ToothIcon,
  CubeTransparentIcon,
  LockClosedIcon,
  LockOpenIcon,
  MagnifyingGlassIcon,
  MoonIcon,
  QuestionMarkCircleIcon,
  SunIcon,
  XMarkIcon,
} from "@heroicons/react/24/outline";
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { Link, Navigate, Route, Routes, useLocation, useNavigate } from "react-router";
import * as Pages from "./pages";
import { AcquisitionProfilePage, AcquisitionProfilesPage } from "./acquisition/AcquisitionProfilePage";
import { cx, ToastHost } from "./ui/components";
import {
  findPageDetail,
  findSection,
  legacyRedirects,
  searchEntries,
  sectionItems,
  sections,
  systemSection,
} from "./navigation";

/* ------------------------------------------------------------------ *
 * 全局分析范围（对象 / 设备 / 配方版本 / 时间）
 * 系统的可比性、分层与混杂判断都建立在"我在看哪一批数据"之上。
 * 锁定后跨页面保持不变；解锁则各页面可独立筛选。
 * ------------------------------------------------------------------ */
const ScopeContext = createContext(null);
export const useScope = () => useContext(ScopeContext);

const SCOPE_FIELDS = [
  { key: "object", label: "对象" },
  { key: "equipment", label: "设备" },
  { key: "recipe", label: "配方" },
  { key: "range", label: "时间" },
];

function ScopeProvider({ children }) {
  const [scope, setScope] = useState({
    object: null,
    equipment: null,
    recipe: null,
    range: { preset: "last30d", label: "近 30 天" },
    locked: true,
  });
  const value = useMemo(
    () => ({
      scope,
      setScopeField: (key, next) => setScope(prev => ({ ...prev, [key]: next })),
      toggleLock: () => setScope(prev => ({ ...prev, locked: !prev.locked })),
    }),
    [scope],
  );
  return <ScopeContext.Provider value={value}>{children}</ScopeContext.Provider>;
}

/* ------------------------------------------------------------------ *
 * 证据面板（⌘I）
 * 界面上任何一个数字都必须能追回它的来源、版本与限制。
 * ------------------------------------------------------------------ */
const EvidenceContext = createContext(null);
export const useEvidence = () => useContext(EvidenceContext);

function EvidenceProvider({ children }) {
  const [subject, setSubject] = useState(null);
  const value = useMemo(
    () => ({ subject, showEvidence: setSubject, closeEvidence: () => setSubject(null) }),
    [subject],
  );
  return <EvidenceContext.Provider value={value}>{children}</EvidenceContext.Provider>;
}

/* ------------------------------------------------------------------ *
 * 对比暂存区：看到可疑运行的时刻，和想做对比的时刻往往隔着几个页面
 * ------------------------------------------------------------------ */
const BasketContext = createContext(null);
export const useComparisonBasket = () => useContext(BasketContext);

function BasketProvider({ children }) {
  const [items, setItems] = useState([]);
  const value = useMemo(
    () => ({
      items,
      add: item => setItems(prev => (prev.some(x => x.id === item.id) ? prev : [...prev, item])),
      remove: id => setItems(prev => prev.filter(x => x.id !== id)),
      clear: () => setItems([]),
    }),
    [items],
  );
  return <BasketContext.Provider value={value}>{children}</BasketContext.Provider>;
}

/* ================================================================== */

export default function App() {
  return (
    <ScopeProvider>
      <EvidenceProvider>
        <BasketProvider>
          <AppShell />
        </BasketProvider>
      </EvidenceProvider>
    </ScopeProvider>
  );
}

function AppShell() {
  const location = useLocation();
  const navigate = useNavigate();
  const { subject, closeEvidence, showEvidence } = useEvidence();
  const { items: basket } = useComparisonBasket();

  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  const [basketOpen, setBasketOpen] = useState(false);
  const [searchOpen, setSearchOpen] = useState(false);
  const [navCollapsed, setNavCollapsed] = useState(false);
  const [theme, setTheme] = useState(() => document.documentElement.dataset.theme ?? "light");

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
  }, [theme]);

  const section = useMemo(() => findSection(location.pathname), [location.pathname]);
  const page = useMemo(() => findPageDetail(location.pathname), [location.pathname]);
  const showSubNav = sectionItems(section).length > 1 && !navCollapsed;

  useEffect(() => {
    function onKeyDown(event) {
      const mod = event.metaKey || event.ctrlKey;
      if (!mod) return;
      const key = event.key.toLowerCase();
      if (key === "k") {
        event.preventDefault();
        setSearchOpen(open => !open);
      } else if (key === "b") {
        event.preventDefault();
        setNavCollapsed(value => !value);
      } else if (key === "i") {
        event.preventDefault();
        if (subject) closeEvidence();
        else showEvidence({ kind: "context", title: "当前页面上下文" });
      }
    }
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [subject, closeEvidence, showEvidence]);

  return (
    <div className="grid h-screen grid-rows-[52px_minmax(0,1fr)_28px] bg-app text-ink-1">
      <TopBar
        theme={theme}
        onToggleTheme={() => setTheme(value => (value === "dark" ? "light" : "dark"))}
        onOpenSearch={() => setSearchOpen(true)}
      />

      <div className="flex min-h-0 overflow-hidden">
        <IconRail current={section.id} />

        {showSubNav && (
          <SubNav
            section={section}
            pathname={location.pathname}
            onCollapse={() => setNavCollapsed(true)}
          />
        )}

        <main className="flex min-w-0 flex-1 flex-col">
          <PageHeader
            section={section}
            page={page}
            collapsed={navCollapsed || sectionItems(section).length <= 1}
            onExpandNav={() => setNavCollapsed(false)}
            onOpenMobileNav={() => setMobileNavOpen(true)}
            hasSubNav={sectionItems(section).length > 1}
          />
          <div className="scrollbar-thin min-h-0 flex-1 overflow-auto p-4 sm:p-5">
            <div className="mx-auto w-full max-w-[1600px]">
              <AppRoutes />
            </div>
          </div>
        </main>

        {subject && <EvidencePanel subject={subject} onClose={closeEvidence} />}
      </div>

      <StatusBar basketCount={basket.length} onOpenBasket={() => setBasketOpen(true)} />

      <MobileNavDialog
        open={mobileNavOpen}
        onClose={() => setMobileNavOpen(false)}
        section={section}
        pathname={location.pathname}
      />
      <CommandPalette open={searchOpen} onClose={() => setSearchOpen(false)} navigate={navigate} />
      <BasketDrawer open={basketOpen} onClose={() => setBasketOpen(false)} />
      <ToastHost />
    </div>
  );
}

/* ----------------------------- 顶栏 ----------------------------- */

function TopBar({ theme, onToggleTheme, onOpenSearch }) {
  const navigate = useNavigate();
  const { scope, toggleLock } = useScope();
  const ThemeIcon = theme === "dark" ? SunIcon : MoonIcon;

  return (
    <header className="z-40 flex items-stretch border-b border-line bg-surface">
      <button
        type="button"
        className="flex w-14 shrink-0 items-center justify-center"
        onClick={() => navigate("/overview")}
        aria-label="回到决策工作台"
      >
        <span className="grid size-7 place-items-center rounded-lg bg-brand text-[13px] font-bold text-white">
          <img src="/ingot-mark.svg" alt="" className="size-5" />
        </span>
      </button>
      <div className="hidden shrink-0 flex-col justify-center border-r border-line pr-3.5 sm:flex">
        <strong className="text-[13px] leading-[1.1]">Ingot</strong>
        <small className="text-[10px] leading-[1.3] text-ink-3">工艺追因与优化</small>
      </div>

      {/* 全局分析范围轴 */}
      <div className="flex min-w-0 flex-1 items-center gap-0 overflow-hidden px-2">
        <span className="hidden whitespace-nowrap px-2 text-[10px] uppercase tracking-[.07em] text-ink-4 xl:inline">
          分析范围
        </span>
        {SCOPE_FIELDS.map((field, index) => (
          <ScopeChip key={field.key} field={field} value={scope[field.key]} showDivider={index > 0} />
        ))}
        <button
          type="button"
          onClick={toggleLock}
          title={scope.locked ? "已锁定：切换页面保持同一分析范围" : "未锁定：各页面可独立筛选"}
          className="ml-1 hidden h-6.5 items-center gap-1.5 rounded px-2 text-[11px] text-ink-3 hover:bg-surface-3 hover:text-ink-1 lg:flex"
        >
          {scope.locked ? <LockClosedIcon className="size-3.5" /> : <LockOpenIcon className="size-3.5" />}
          {scope.locked ? "已锁定" : "未锁定"}
        </button>
      </div>

      <div className="flex shrink-0 items-center gap-0.5 border-l border-line px-2">
        <button
          type="button"
          onClick={onOpenSearch}
          aria-keyshortcuts="Control+K Meta+K"
          className="flex h-7 min-w-[150px] items-center gap-2 rounded border border-line bg-surface-2 px-2 text-[12px] text-ink-3 hover:border-line-2"
        >
          <MagnifyingGlassIcon className="size-3.5" />
          <span className="hidden lg:inline">搜索对象、运行、动作</span>
          <kbd className="ml-auto hidden rounded border border-line px-1 py-0.5 text-[10px] text-ink-4 2xl:inline">⌘K</kbd>
        </button>
        <IconButton label="通知" badge>
          <BellIcon className="size-4.5" />
        </IconButton>
        <IconButton label="切换主题" onClick={onToggleTheme}>
          <ThemeIcon className="size-4.5" />
        </IconButton>
        <SystemMenu />
        <Menu as="div" className="relative flex">
          <MenuButton className="grid size-8 place-items-center" aria-label="用户菜单">
            <span className="grid size-6.5 place-items-center rounded-full border border-accent-line bg-accent-wash text-[10px] font-bold text-accent-ink">
              OP
            </span>
          </MenuButton>
          <MenuItems
            transition
            anchor="bottom end"
            className="z-[100] mt-2 w-52 origin-top-right rounded-xl border border-line bg-surface p-1 text-sm shadow-xl transition data-closed:scale-95 data-closed:opacity-0"
          >
            <div className="border-b border-line px-3 py-2">
              <p className="truncate font-medium">当前操作员</p>
              <p className="mt-0.5 truncate text-xs text-ink-3">开发模式 · operator</p>
            </div>
            <MenuItem>
              <Link to="/system/users" className="block rounded-lg px-3 py-2 data-focus:bg-surface-3">
                用户与权限
              </Link>
            </MenuItem>
            <MenuItem>
              <a
                href="https://docs.ingotstack.com/zh"
                className="block rounded-lg px-3 py-2 data-focus:bg-surface-3"
              >
                产品文档
              </a>
            </MenuItem>
          </MenuItems>
        </Menu>
      </div>
    </header>
  );
}

function ScopeChip({ field, value, showDivider }) {
  return (
    <>
      {showDivider && <span className="mx-1.5 h-4 w-px shrink-0 bg-line" />}
      <button
        type="button"
        className="flex h-7 max-w-[220px] items-center gap-1.5 whitespace-nowrap rounded border border-line bg-surface-2 px-2 transition hover:border-line-2 hover:bg-surface-3"
        title={`选择${field.label}`}
      >
        <span className="text-[10px] text-ink-4">{field.label}</span>
        <span className="overflow-hidden text-ellipsis text-[12px] font-medium">
          {value?.label ?? "全部"}
        </span>
        <ChevronRightIcon className="size-3 rotate-90 text-ink-4" />
      </button>
    </>
  );
}

function IconButton({ children, label, badge, onClick }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label={label}
      title={label}
      className="relative grid size-7.5 place-items-center rounded text-ink-2 hover:bg-surface-3 hover:text-ink-1"
    >
      {children}
      {badge && <span className="absolute right-1 top-1 size-1.5 rounded-full bg-status-critical ring-2 ring-surface" />}
    </button>
  );
}

function SystemMenu() {
  return (
    <Menu as="div" className="relative flex">
      <MenuButton
        className="grid size-7.5 place-items-center rounded text-ink-2 hover:bg-surface-3 hover:text-ink-1"
        aria-label="系统管理"
        title="系统管理"
      >
        <Cog6ToothIcon className="size-4.5" />
      </MenuButton>
      <MenuItems
        transition
        anchor="bottom end"
        className="z-[100] mt-2 w-44 origin-top-right rounded-xl border border-line bg-surface p-1 text-sm shadow-xl transition data-closed:scale-95 data-closed:opacity-0"
      >
        {systemSection.groups[0].items.map(([path, label]) => (
          <MenuItem key={path}>
            <Link to={path} className="block rounded-lg px-3 py-2 data-focus:bg-surface-3">
              {label}
            </Link>
          </MenuItem>
        ))}
      </MenuItems>
    </Menu>
  );
}

/* ----------------------------- 导航 ----------------------------- */

function IconRail({ current }) {
  return (
    <nav
      className="z-30 flex w-14 shrink-0 flex-col items-center gap-0.5 border-r border-line bg-surface py-2"
      aria-label="模块导航"
    >
      {sections.map(section => {
        const Icon = section.icon;
        const active = section.id === current;
        return (
          <Link
            key={section.id}
            to={section.path}
            aria-current={active ? "page" : undefined}
            className={cx(
              "group relative grid size-10 place-items-center rounded-lg transition",
              active ? "bg-accent-wash text-accent-ink" : "text-ink-3 hover:bg-surface-3 hover:text-ink-1",
            )}
          >
            {active && <span className="absolute -left-2 inset-y-2.5 w-[3px] rounded-r bg-accent" />}
            <Icon className="size-5" />
            <span className="pointer-events-none absolute left-13 z-50 hidden whitespace-nowrap rounded bg-ink-1 px-2 py-1 text-[11px] text-surface group-hover:block">
              <b>{section.label}</b> · {section.desc}
            </span>
          </Link>
        );
      })}
      <span className="flex-1" />
      <a
        href="https://docs.ingotstack.com/zh"
        className="grid size-10 place-items-center rounded-lg text-ink-3 hover:bg-surface-3 hover:text-ink-1"
        aria-label="帮助与文档"
      >
        <QuestionMarkCircleIcon className="size-5" />
      </a>
    </nav>
  );
}

function SubNav({ section, pathname, onCollapse }) {
  return (
    <nav
      className="hidden w-57 shrink-0 flex-col border-r border-line bg-surface lg:flex"
      aria-label={`${section.label}导航`}
    >
      <div className="flex h-9.5 shrink-0 items-center justify-between border-b border-line pl-3.5 pr-2">
        <strong className="text-xs tracking-wide">{section.label}</strong>
        <button
          type="button"
          onClick={onCollapse}
          aria-label="收起二级导航"
          aria-keyshortcuts="Control+B Meta+B"
          className="grid size-6 place-items-center rounded text-ink-3 hover:bg-surface-3 hover:text-ink-1"
        >
          <ChevronLeftIcon className="size-3.5" />
        </button>
      </div>
      <div className="scrollbar-thin min-h-0 flex-1 overflow-auto p-2 pb-4">
        <NavGroups section={section} pathname={pathname} />
      </div>
    </nav>
  );
}

function NavGroups({ section, pathname, onNavigate }) {
  return section.groups.map((group, index) => (
    <div key={group.label ?? index} className="mb-2.5 grid gap-px">
      {group.label && (
        <p className="px-2 pb-1 pt-1.5 text-[10px] font-semibold tracking-wide text-ink-4">{group.label}</p>
      )}
      {group.items.map(([path, label]) => {
        const active = pathname === path || pathname.startsWith(`${path}/`);
        return (
          <Link
            key={path}
            to={path}
            onClick={onNavigate}
            aria-current={active ? "page" : undefined}
            className={cx(
              "flex h-7 items-center rounded px-2 text-[12.5px] transition",
              active ? "bg-accent-wash font-semibold text-accent-ink" : "text-ink-2 hover:bg-surface-3 hover:text-ink-1",
            )}
          >
            {label}
          </Link>
        );
      })}
    </div>
  ));
}

function MobileNavDialog({ open, onClose, section, pathname }) {
  return (
    <Dialog open={open} onClose={onClose} className="relative z-80 lg:hidden">
      <DialogBackdrop className="fixed inset-0 bg-slate-950/35" />
      <DialogPanel className="fixed inset-y-0 left-0 w-72 bg-surface shadow-2xl">
        <div className="flex h-12 items-center justify-between border-b border-line px-4">
          <strong>{section.label}</strong>
          <button type="button" onClick={onClose} aria-label="关闭模块导航" className="grid size-8 place-items-center rounded hover:bg-surface-3">
            <XMarkIcon className="size-5" />
          </button>
        </div>
        <nav className="p-2">
          <NavGroups section={section} pathname={pathname} onNavigate={onClose} />
        </nav>
      </DialogPanel>
    </Dialog>
  );
}

/* --------------------------- 页头 / 状态栏 --------------------------- */

function PageHeader({ section, page, collapsed, onExpandNav, onOpenMobileNav, hasSubNav }) {
  return (
    <div className="shrink-0 border-b border-line bg-surface px-4 pb-2.5 pt-2.5 sm:px-5">
      <div className="mb-1 flex items-center gap-1.5 text-[11px] text-ink-3">
        {hasSubNav && collapsed && (
          <button
            type="button"
            onClick={onExpandNav}
            aria-label="展开二级导航"
            className="-ml-1 hidden size-5.5 place-items-center rounded hover:bg-surface-3 lg:grid"
          >
            <Bars3Icon className="size-3.5" />
          </button>
        )}
        {hasSubNav && (
          <button
            type="button"
            onClick={onOpenMobileNav}
            aria-label="打开模块导航"
            className="-ml-1 grid size-5.5 place-items-center rounded hover:bg-surface-3 lg:hidden"
          >
            <Bars3Icon className="size-3.5" />
          </button>
        )}
        <Link to={section.path} className="shrink-0 hover:text-accent">
          {section.label}
        </Link>
        <span className="text-ink-4">/</span>
        <span className="truncate text-ink-2">{page[0]}</span>
      </div>
      <h1 className="text-[17px] font-semibold leading-tight tracking-tight">{page[0]}</h1>
      <p className="mt-0.5 max-w-[80ch] truncate text-[11.5px] text-ink-3">{page[1]}</p>
    </div>
  );
}

function StatusBar({ basketCount, onOpenBasket }) {
  return (
    <footer className="z-40 flex items-center border-t border-line bg-surface px-2 text-[11px] text-ink-3">
      <Link to="/system/health" className="flex h-7 items-center gap-1.5 px-2 hover:bg-surface-3 hover:text-ink-1">
        <span className="size-1.5 rounded-full bg-status-good" />
        平台正常
      </Link>
      <span className="h-3 w-px bg-line" />
      <Link to="/configure/edges" className="flex h-7 items-center gap-1.5 px-2 hover:bg-surface-3 hover:text-ink-1">
        现场节点 <b className="tabular-nums text-ink-1">7/8</b> 在线
      </Link>
      <span className="h-3 w-px bg-line" />
      <span className="flex h-7 items-center gap-1.5 px-2">
        数据上行 <b className="tabular-nums text-ink-1">18s</b> 前
      </span>
      <span className="h-3 w-px bg-line" />
      <Link to="/diagnose/data-health" className="flex h-7 items-center gap-1.5 px-2 hover:bg-surface-3 hover:text-ink-1">
        进入分析 <b className="tabular-nums text-status-caution-ink">88.5%</b>
      </Link>
      <span className="flex-1" />
      <button
        type="button"
        onClick={onOpenBasket}
        className="flex h-7 items-center gap-1.5 px-2 hover:bg-surface-3 hover:text-ink-1"
      >
        <ArchiveBoxIcon className="size-3" />
        对比暂存区 <b className="tabular-nums text-accent-ink">{basketCount}</b>
      </button>
    </footer>
  );
}

/* --------------------------- 证据面板 --------------------------- */

function EvidencePanel({ subject, onClose }) {
  return (
    <aside className="hidden w-94 shrink-0 flex-col border-l border-line bg-surface xl:flex">
      <div className="flex h-9.5 shrink-0 items-center gap-2 border-b border-line pl-3.5 pr-2">
        <CubeTransparentIcon className="size-3.5 text-accent" />
        <b className="text-xs">证据面板</b>
        <button
          type="button"
          onClick={onClose}
          aria-label="关闭证据面板"
          aria-keyshortcuts="Control+I Meta+I"
          className="ml-auto grid size-6 place-items-center rounded text-ink-3 hover:bg-surface-3 hover:text-ink-1"
        >
          <XMarkIcon className="size-3.5" />
        </button>
      </div>
      <div className="scrollbar-thin min-h-0 flex-1 overflow-auto p-3.5 text-[12px]">
        {/*
          固定四段：
            1. 这条结果从哪来 —— 运行 / 实际条件 / 质量结果 / 上下文快照
            2. 版本与可复现 —— 分析模型、工艺模型、场景包、输入哈希、冻结数据集
            3. 这条证据不能说明什么 —— 主动声明边界
            4. 导出证据包 / 复制引用
        */}
        <p className="text-ink-3">{subject?.title ?? "选择一个数字或记录以查看它的来源"}</p>
      </div>
    </aside>
  );
}

/* --------------------------- 对比暂存区 --------------------------- */

function BasketDrawer({ open, onClose }) {
  const { items, remove, clear } = useComparisonBasket();
  const navigate = useNavigate();
  if (!open) return null;
  return (
    <div className="fixed bottom-9 right-3.5 z-[150] w-85 overflow-hidden rounded-lg border border-line-2 bg-surface shadow-2xl">
      <div className="flex items-center gap-2 border-b border-line px-3 py-2">
        <b className="text-[12.5px]">对比暂存区</b>
        <span className="text-[11px] text-ink-3">跨页面保留，随时开始对比</span>
        <button type="button" onClick={onClose} aria-label="关闭" className="ml-auto grid size-5.5 place-items-center rounded hover:bg-surface-3">
          <XMarkIcon className="size-3.5" />
        </button>
      </div>
      {items.length === 0 ? (
        <p className="px-3 py-6 text-center text-[12px] text-ink-3">
          还没有加入任何运行。在运行记录里点「加入对比」。
        </p>
      ) : (
        <ul className="max-h-72 overflow-auto">
          {items.map(item => (
            <li key={item.id} className="flex items-center gap-2 border-b border-line px-3 py-2 last:border-0">
              <div className="min-w-0 flex-1">
                <p className="truncate text-[12.5px] font-medium">{item.id}</p>
                <p className="truncate text-[11px] text-ink-3">{item.summary}</p>
              </div>
              <button type="button" onClick={() => remove(item.id)} className="text-[11px] text-ink-3 hover:text-ink-1">
                移出
              </button>
            </li>
          ))}
        </ul>
      )}
      <div className="flex items-center gap-2 border-t border-line px-3 py-2">
        <button type="button" onClick={clear} className="text-[11px] text-ink-3 hover:text-ink-1">
          清空
        </button>
        <button
          type="button"
          disabled={items.length < 2}
          onClick={() => {
            onClose();
            navigate("/diagnose/compare");
          }}
          className="ml-auto h-6 rounded bg-accent px-2.5 text-[12px] font-medium text-white disabled:opacity-45"
        >
          开始对比
        </button>
      </div>
    </div>
  );
}

/* --------------------------- 命令面板 --------------------------- */

/** 动作条目的描述里写明它是否需要批准 —— 机器提议与人的决定必须可分辨。 */
const commandActions = [
  { label: "发起运行对比", hint: "用对比暂存区里的运行开始", to: "/diagnose/compare" },
  { label: "批准待决实验", hint: "需要你决定 —— 批准会冻结当时的证据快照", to: "/research/experiments" },
  { label: "新建课题", hint: "声明问题、目标与安全边界", to: "/research/projects" },
  { label: "检查数据可信度", hint: "这些运行够不够格进入正式分析", to: "/diagnose/data-health" },
  { label: "查看工艺窗口", hint: "已验证结论、适用范围与失效条件 —— 系统的最终产出", to: "/research/windows" },
];

function CommandPalette({ open, onClose, navigate }) {
  const [query, setQuery] = useState("");
  const [cursor, setCursor] = useState(0);
  const inputRef = useRef(null);

  useEffect(() => {
    if (!open) return;
    setQuery("");
    setCursor(0);
    const id = window.setTimeout(() => inputRef.current?.focus(), 0);
    return () => window.clearTimeout(id);
  }, [open]);

  const groups = useMemo(() => {
    const keyword = query.trim().toLowerCase();
    const match = text => !keyword || text.toLowerCase().includes(keyword);
    const actions = commandActions
      .filter(action => match(`${action.label} ${action.hint}`))
      .map(action => ({ label: action.label, hint: action.hint, path: action.to }));
    const pages = searchEntries
      .filter(entry => match(`${entry.label} ${entry.section} ${entry.group} ${entry.description}`))
      .map(entry => ({
        label: entry.label,
        hint: `${entry.section}${entry.group ? ` · ${entry.group}` : ""} — ${entry.description}`,
        path: entry.path,
      }));
    return [
      ["动作", actions],
      ["页面", pages],
    ].filter(([, list]) => list.length > 0);
  }, [query]);

  const flat = useMemo(() => groups.flatMap(([, list]) => list), [groups]);

  const run = useCallback(
    index => {
      const entry = flat[index];
      if (!entry) return;
      onClose();
      navigate(entry.path);
    },
    [flat, navigate, onClose],
  );

  return (
    <Dialog open={open} onClose={onClose} className="relative z-[200]">
      <DialogBackdrop className="fixed inset-0 bg-slate-950/40 backdrop-blur-sm" />
      <div className="fixed inset-0 overflow-y-auto p-4 pt-[11vh]">
        <DialogPanel className="mx-auto w-full max-w-2xl overflow-hidden rounded-xl border border-line-2 bg-surface shadow-2xl">
          <div className="flex items-center gap-2.5 border-b border-line px-3.5 py-2.5">
            <MagnifyingGlassIcon className="size-4 text-ink-3" />
            <input
              ref={inputRef}
              value={query}
              onChange={event => {
                setQuery(event.target.value);
                setCursor(0);
              }}
              onKeyDown={event => {
                if (event.key === "ArrowDown") {
                  event.preventDefault();
                  setCursor(value => Math.min(flat.length - 1, value + 1));
                } else if (event.key === "ArrowUp") {
                  event.preventDefault();
                  setCursor(value => Math.max(0, value - 1));
                } else if (event.key === "Enter") {
                  event.preventDefault();
                  run(cursor);
                }
              }}
              placeholder="搜索页面、对象、运行，或输入动作"
              className="flex-1 bg-transparent text-[15px] outline-none placeholder:text-ink-4"
              autoComplete="off"
            />
            <kbd className="rounded border border-line px-1.5 py-0.5 text-[10px] text-ink-4">Esc</kbd>
          </div>
          <div className="scrollbar-thin max-h-[52vh] overflow-auto p-1.5">
            {flat.length === 0 && (
              <p className="px-4 py-10 text-center text-[12.5px] text-ink-3">
                没有匹配项。试试「对比」「实验」「模型」「证据」。
              </p>
            )}
            {groups.map(([groupLabel, list]) => (
              <div key={groupLabel}>
                <p className="px-2 pb-1 pt-2 text-[10px] font-semibold tracking-wide text-ink-4">{groupLabel}</p>
                {list.map(entry => {
                  const index = flat.indexOf(entry);
                  return (
                    <button
                      key={`${groupLabel}-${entry.path}-${entry.label}`}
                      type="button"
                      onMouseMove={() => setCursor(index)}
                      onClick={() => run(index)}
                      className={cx(
                        "flex w-full items-center gap-2.5 rounded px-2 py-1.5 text-left",
                        index === cursor && "bg-accent-wash",
                      )}
                    >
                      <span className="shrink-0 text-[13px]">{entry.label}</span>
                      <span className="truncate text-[11px] text-ink-3">{entry.hint}</span>
                    </button>
                  );
                })}
              </div>
            ))}
          </div>
          <div className="flex gap-3.5 border-t border-line px-3.5 py-1.5 text-[10.5px] text-ink-4">
            <span>↑↓ 选择</span>
            <span>↵ 打开</span>
            <span className="ml-auto">动作会说明是否需要你批准</span>
          </div>
        </DialogPanel>
      </div>
    </Dialog>
  );
}

/* --------------------------- 路由 --------------------------- */

/** 尚未实现的新页面。保留位置与命名，不阻塞导航层上线。 */
function PagePlaceholder({ title, note }) {
  return (
    <div className="rounded-lg border border-line bg-surface p-10 text-center">
      <p className="text-[13.5px] font-semibold">{title}</p>
      <p className="mx-auto mt-1.5 max-w-[60ch] text-[12px] text-ink-3">{note}</p>
    </div>
  );
}

function AppRoutes() {
  return (
    <Routes>
      {/* 总览 */}
      <Route path="/overview" element={<Pages.WorkbenchPage />} />
      <Route path="/overview/live" element={<PagePlaceholder title="实时运行" note="正在跑的运行、设备占用与现场节点心跳。第二期实现。" />} />
      <Route path="/overview/tasks" element={<PagePlaceholder title="我的任务" note="等着你批准、复核或确认的事。需要后端任务聚合接口。" />} />

      {/* 运行（事实层）——静态兄弟路径必须声明在 :runId 之前 */}
      <Route path="/runs" element={<Pages.CyclesPage />} />
      <Route path="/runs/inspections" element={<Pages.InspectionsPage />} />
      <Route path="/runs/events" element={<Pages.EventsPage />} />
      <Route path="/runs/setup" element={<Pages.ProductionSetupPage section="context" />} />
      <Route path="/runs/tooling" element={<Pages.ProductionSetupPage section="installation" />} />
      {/* TODO(第三期)：待后端提供 GET /runs?correlationId= 反查后，参数改为 :runId。
          「周期 = 运行」已确认，运行号 OperationRunId 才是唯一主标识。 */}
      <Route path="/runs/:correlationId" element={<Pages.CycleDetailPage />} />

      {/* 追因（判断层） */}
      <Route path="/diagnose/data-health" element={<Pages.DataQualityPage />} />
      <Route path="/diagnose/basket" element={<PagePlaceholder title="对比暂存区" note="跨页面攒下的待比较运行。状态栏已可打开抽屉，独立页面第二期实现。" />} />
      <Route path="/diagnose/compare" element={<Pages.CycleComparisonPage />} />
      <Route path="/diagnose/distribution" element={<Pages.QualityAnalysisPage />} />
      <Route path="/diagnose/candidates" element={<PagePlaceholder title="候选原因" note="跨对比汇总所有待验证候选及其证据强度。第三期实现。" />} />
      <Route path="/diagnose/assistant" element={<Pages.ChatPage />} />
      <Route path="/diagnose/checkset" element={<Pages.GoldenQuestionsPage />} />

      {/* 优化（判断层） */}
      <Route path="/research/projects" element={<Pages.ResearchProjectsPage />} />
      <Route path="/research/projects/:projectId" element={<Pages.ResearchProjectsPage />} />
      <Route path="/research/experiments" element={<PagePlaceholder title="实验排程" note="跨课题的待批准、待执行与执行中实验。第三期实现。" />} />
      <Route path="/research/windows" element={<PagePlaceholder title="工艺窗口" note="已验证结论、适用范围与失效条件 —— 系统的最终产出。第三期实现。" />} />
      <Route path="/research/assets" element={<Pages.ResearchAssetsPage />} />

      {/* 配置 */}
      <Route path="/configure/objects" element={<Pages.ObjectExplorerPage />} />
      <Route path="/configure/scenarios" element={<Pages.ScenarioPackagesPage />} />
      <Route path="/configure/process-models" element={<Pages.ProcessDataModelsPage />} />
      <Route path="/configure/recipes" element={<Pages.RecipeVersionsPage />} />
      <Route path="/configure/analysis-plans" element={<Pages.ProcessAnalysisPlansPage />} />
      <Route path="/configure/inspection-definitions" element={<Pages.InspectionDefinitionsPage />} />
      <Route path="/configure/quality-plans" element={<Pages.QualityPlansPage />} />
      <Route path="/configure/tooling-types" element={<Pages.ProductionSetupPage section="type" />} />
      <Route path="/configure/component-types" element={<Pages.ProductionSetupPage section="componentType" />} />
      <Route path="/configure/components" element={<Pages.ProductionSetupPage section="component" />} />
      <Route path="/configure/tooling-assemblies" element={<Pages.ProductionSetupPage section="assembly" />} />
      <Route path="/configure/edges" element={<Pages.EdgesPage />} />
      <Route path="/configure/edges/:edgeId" element={<Pages.EdgeDetailPage />} />
      <Route path="/configure/acquisition" element={<AcquisitionProfilesPage />} />
      <Route path="/configure/acquisition/:profileId" element={<AcquisitionProfilePage />} />

      {/* 系统 */}
      <Route path="/system/users" element={<Pages.UsersPage />} />
      <Route path="/system/health" element={<Pages.MetricsPage />} />
      <Route path="/system/logs" element={<Pages.LogsPage />} />
      <Route path="/system/audit" element={<PagePlaceholder title="审计追踪" note="谁在什么时候批准了什么、改了什么。单人批准模式下这是唯一的控制手段，随第二期上线。" />} />

      {/* v1 路由重定向 */}
      {Object.entries(legacyRedirects).map(([from, to]) => (
        <Route key={from} path={from} element={<Navigate to={to} replace />} />
      ))}
      <Route path="/cycles/:correlationId" element={<LegacyParamRedirect to="/runs" />} />
      <Route path="/edges/:edgeId" element={<LegacyParamRedirect to="/configure/edges" />} />
      <Route path="/research-projects/:projectId" element={<LegacyParamRedirect to="/research/projects" />} />
      <Route path="/configuration/acquisition-profiles/:profileId" element={<LegacyParamRedirect to="/configure/acquisition" />} />

      <Route path="*" element={<Pages.NotFoundPage />} />
    </Routes>
  );
}

function LegacyParamRedirect({ to }) {
  const { pathname } = useLocation();
  const id = pathname.split("/").filter(Boolean).pop();
  return <Navigate to={`${to}/${id}`} replace />;
}
