// 定义产品壳层、规范路由、导航结构、权限入口和全局功能搜索。
import { Dialog, DialogBackdrop, DialogPanel, Menu, MenuButton, MenuItem, MenuItems } from "@headlessui/react";
import {
  AdjustmentsHorizontalIcon,
  Bars3Icon,
  BoltIcon,
  ClipboardDocumentCheckIcon,
  ChevronDoubleLeftIcon,
  ChevronDoubleRightIcon,
  ChevronDownIcon,
  ChevronRightIcon,
  CircleStackIcon,
  Cog6ToothIcon,
  MagnifyingGlassIcon,
  MagnifyingGlassCircleIcon,
  SignalIcon,
  XMarkIcon,
} from "@heroicons/react/24/outline";
import { useEffect, useMemo, useState } from "react";
import { Link, Navigate, Route, Routes, useLocation, useNavigate } from "react-router";
import * as Pages from "./pages";
import { IngestionTaskPage, IngestionTasksPage } from "./acquisition/IngestionTaskPage";
import { extractRows, useApi } from "./hooks/useApi";
import { edgeStatus } from "./pages/shared";
import { cx, LinkButton, ToastHost } from "./ui/components";
import GlobalSearchDialog from "./components/GlobalSearchDialog";
import { formatRoleSummary, formatSiteScope } from "./auth/identityPresentation";

const sections = [
  {
    id: "overview", label: "工作台", icon: BoltIcon, path: "/workbench", groups: [
      { items: [["/workbench", "工作台"]] },
    ],
  },
  {
    id: "equipment-connection", label: "现场接入", icon: SignalIcon, path: "/edges", groups: [
      { label: "接入配置", items: [["/edges", "现场节点"], ["/configuration/ingestion-tasks", "采集配置"]] },
    ],
  },
  {
    id: "process-definition", label: "工艺配置", icon: AdjustmentsHorizontalIcon, path: "/configuration", groups: [
      { label: "基础配置", items: [["/configuration", "配置总览"], ["/configuration/process-data-models", "数据字典"], ["/configuration/process-specifications", "工艺规范"], ["/configuration/process-analysis-plans", "分析规则"]] },
      { label: "质量配置", items: [["/configuration/inspection-definitions", "检测定义"], ["/configuration/quality-plans", "质量方案"]] },
      { label: "工装配置", items: [["/configuration/component-types", "组件分类"], ["/configuration/components", "组件台账"], ["/configuration/tooling-types", "工装结构"], ["/configuration/tooling-assemblies", "工装总成"]] },
    ],
  },
  {
    id: "evidence", label: "生产运行", icon: CircleStackIcon, path: "/process-executions", groups: [
      { label: "生产准备", items: [["/production/changeover", "生产切换"], ["/production/tooling-installations", "工装装卸"]] },
      { label: "运行追溯", items: [["/process-executions", "运行记录"], ["/explorer", "对象目录"], ["/events", "运行事件"]] },
    ],
  },
  {
    id: "quality", label: "质量管理", icon: ClipboardDocumentCheckIcon, path: "/inspections", groups: [
      { label: "检验执行", items: [["/inspections", "检验任务"]] },
      { label: "问题分析", items: [["/quality-analysis", "偏差分析"]] },
    ],
  },
  {
    id: "diagnosis", label: "工艺追因", icon: MagnifyingGlassCircleIcon, path: "/analysis", groups: [
      { label: "追因分析", items: [["/analysis", "追因总览"], ["/data-quality", "数据质量"], ["/comparisons", "运行对比"]] },
      { label: "辅助研判", items: [["/chat", "分析助手"]] },
    ],
  },
];

const systemSection = {
  id: "system", label: "系统管理", icon: Cog6ToothIcon, path: "/identity/users", groups: [
    { label: "身份权限", items: [["/identity/users", "用户权限"]] },
    { label: "平台运维", items: [["/platform-metrics", "平台状态"], ["/logs", "平台日志"]] },
    { label: "助手治理", items: [["/model-service", "模型服务"]] },
  ],
};

export const sectionsForIdentity = identity => (identity?.roles || []).includes("platform.admin")
  ? [...sections, systemSection]
  : sections;

const sectionItems = section => section.groups.flatMap(group => group.items);

const pageDetails = {
  "/workbench": ["工作台", "集中查看待办、生产状态与质量风险"],
  "/chat": ["工艺分析助手", "用自然语言查询运行、质量与配置证据"],
  "/analysis": ["追因总览", "从生产运行和可信证据进入差异比较、候选原因与工程验证"],
  "/explorer": ["对象目录", "选择真实业务对象，再进入它的运行、事件、质量与数据健康视图"],
  "/process-executions": ["运行记录", "查看生产运行及其数据、工艺与质量上下文"],
  "/events": ["运行事件", "查询、追溯并关联运行上下文"],
  "/production/changeover": ["生产切换", "让设备、产品、工艺规范和已装工装对接下来的运行生效"],
  "/production/tooling-installations": ["工装装卸", "记录工装组合版本在设备上的装入与卸下区间"],
  "/inspections": ["检验任务", "处理视觉检查、人工质检与原图复核"],
  "/quality-analysis": ["偏差分析", "按产品、工艺规范和运行上下文定位质量偏差并追溯证据"],
  "/comparisons": ["运行对比", "比较同类生产运行、运行段或时间窗口，生成待验证的候选原因"],
  "/model-service": ["模型服务", "配置 OpenAI-compatible 供应商、协议、模型和加密 API key"],
  "/data-quality": ["数据质量", "检查运行对象的数据范围、采样连续性与运行完整性"],
  "/configuration": ["配置总览", "按依赖顺序完成数据、接入、分析、质量与工装配置"],
  "/configuration/process-analysis-plans": ["分析规则", "版本化定义同类比较条件、对齐方式、质量分组和数据项"],
  "/configuration/process-data-models": ["数据字典", "定义工艺变量、阶段号和控制参数，供现场数据源统一映射"],
  "/configuration/process-specifications": ["工艺规范", "维护引用数据模型的完整工艺规范版本"],
  "/configuration/ingestion-tasks": ["采集配置", "选择现场节点和通信驱动，将来源字段映射到工艺变量"],
  "/configuration/inspection-definitions": ["检测定义", "定义要检测的特性、录入类型和判定规则"],
  "/configuration/quality-plans": ["质量方案", "配置产品适用的检测项目与复核规则"],
  "/configuration/component-types": ["组件分类", "维护可复用物理资产的业务分类；装配位置由工装结构定义"],
  "/configuration/components": ["组件台账", "登记具有独立资产编号和序列号的可更换物理组件"],
  "/configuration/tooling-types": ["工装结构", "定义工装总成结构、装配位置和各位置允许的组件分类"],
  "/configuration/tooling-assemblies": ["工装总成", "查看工装总成身份、不可变配置版本及每个位置的实际成员"],
  "/edges": ["现场节点", "查看负责连接设备、仪器、系统并上报数据的现场节点"],
  "/platform-metrics": ["平台状态", "确认中心服务、现场节点和数据上行是否正常"],
  "/logs": ["平台日志", "查询平台运行记录"],
  "/identity/users": ["用户权限", "管理本地账户、岗位权限、密码和启停状态"],
};

const searchAliases = {
  "/explorer": "工业对象 对象目录 设备 工件",
  "/production/changeover": "生产上下文 换产 产品切换 工艺切换",
  "/inspections": "质量任务 质检 检测任务",
  "/quality-analysis": "质量偏差分析 不良 偏差",
  "/analysis": "工艺分析 分析总览 原因分析",
  "/data-quality": "数据质量 完整性 分析准入",
  "/configuration": "配置中心",
  "/configuration/process-specifications": "配方 参数版本",
  "/configuration/ingestion-tasks": "采集 PLC 点位映射",
  "/platform-metrics": "平台运行状态 系统状态",
  "/logs": "运行日志 系统日志",
  "/model-service": "AI 大模型 API key DeepSeek OpenAI Qwen",
};

const searchEntriesForSections = navigationSections => navigationSections.flatMap(section => sectionItems(section).map(([path, label]) => ({
  path,
  label,
  section: section.label,
  description: pageDetails[path]?.[1] || "打开功能页面",
  aliases: searchAliases[path] || "",
})));

function isApplePlatform() {
  if (typeof navigator === "undefined") return false;
  const platform = navigator.userAgentData?.platform || navigator.platform || navigator.userAgent;
  return /mac|iphone|ipad|ipod|ios/i.test(platform);
}

function SidebarSection({ section, activeSectionId, activeNavigationPath, expanded, compact = false, onToggle, onNavigate }) {
  const Icon = section.icon;
  const active = section.id === activeSectionId;
  const items = sectionItems(section);
  const hasNestedItems = items.length > 1 || items[0]?.[0] !== section.path || items[0]?.[1] !== section.label;

  if (compact) {
    return (
      <Link
        to={section.path}
        onClick={onNavigate}
        className={cx("grid h-11 place-items-center rounded-xl text-slate-400 transition hover:bg-white/8 hover:text-white", active && "bg-trajectory-500/15 text-trajectory-100 ring-1 ring-inset ring-trajectory-500/20")}
        aria-label={section.label}
        title={section.label}
      >
        <Icon className="size-5" />
      </Link>
    );
  }

  return (
    <div>
      <div className={cx("flex items-center rounded-xl transition", active ? "bg-trajectory-500/12 text-trajectory-100 ring-1 ring-inset ring-trajectory-500/15" : "text-slate-300 hover:bg-white/6 hover:text-white")}>
        <Link to={section.path} onClick={onNavigate} className="flex min-w-0 flex-1 items-center gap-3 px-3 py-2.5 text-[15px] font-semibold leading-5">
          <Icon className="size-5 shrink-0" />
          <span className="truncate">{section.label}</span>
        </Link>
        {hasNestedItems && (
          <button
            type="button"
            className="mr-1 grid size-8 shrink-0 place-items-center rounded-lg hover:bg-white/8"
            onClick={() => onToggle(section.id)}
            aria-label={`${expanded ? "收起" : "展开"}${section.label}`}
            aria-expanded={expanded}
          >
            <ChevronDownIcon className={cx("size-4 transition-transform", !expanded && "-rotate-90")} />
          </button>
        )}
      </div>
      {hasNestedItems && expanded && (
        <div className="mt-1 ml-5 grid gap-0.5 border-l border-white/10">
          {items.map(([path, label]) => (
            <Link
              key={path}
              to={path}
              onClick={onNavigate}
              className={cx("flex min-h-9 items-center rounded-lg px-3 py-2 text-sm font-medium leading-5", path === activeNavigationPath ? "bg-white/8 font-semibold text-white" : "text-slate-400 hover:bg-white/5 hover:text-slate-100")}
            >
              {label}
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}

function SidebarNavigation({ navigationSections, activeSectionId, activeNavigationPath, expandedSectionId, compact = false, onToggle, onNavigate }) {
  return (
    <nav className={cx("scrollbar-thin grid flex-1 content-start overflow-y-auto", compact ? "gap-1 p-3" : "gap-1.5 p-3")} aria-label="主导航">
      {navigationSections.map(item => (
        <SidebarSection
          key={item.id}
          section={item}
          activeSectionId={activeSectionId}
          activeNavigationPath={activeNavigationPath}
          expanded={expandedSectionId === item.id}
          compact={compact}
          onToggle={onToggle}
          onNavigate={onNavigate}
        />
      ))}
    </nav>
  );
}

export function SystemStatusIndicator() {
  const { data, loading, error } = useApi("/api/edges", { interval: 30000 });
  const edges = extractRows(data);
  const online = edges.filter(item => edgeStatus(item) === "online").length;
  const needsAttention = edges.filter(item => edgeStatus(item) !== "online").length;
  const presentation = error
    ? { label: "状态不可用", compactLabel: "!", detail: "平台状态暂时无法读取", dot: "bg-rose-500", text: "text-rose-700", background: "hover:bg-rose-50" }
    : loading && !data
      ? { label: "检查中", compactLabel: "…", detail: "正在检查平台和现场节点", dot: "bg-slate-400", text: "text-slate-600", background: "hover:bg-slate-50" }
      : edges.length === 0
        ? { label: "待接入", compactLabel: "0", detail: "平台正常，尚未登记现场节点", dot: "bg-amber-500", text: "text-amber-700", background: "hover:bg-amber-50" }
        : needsAttention > 0
          ? { label: `${needsAttention} 项关注`, compactLabel: String(needsAttention), detail: `平台正常，现场节点 ${online}/${edges.length} 在线`, dot: "bg-amber-500", text: "text-amber-700", background: "hover:bg-amber-50" }
          : { label: "运行正常", compactLabel: "✓", detail: `平台正常，现场节点 ${online}/${edges.length} 在线`, dot: "bg-emerald-500", text: "text-emerald-700", background: "hover:bg-emerald-50" };
  return (
    <Link
      to="/platform-metrics"
      className={cx("flex shrink-0 items-center gap-2 border-l border-slate-100 px-3 text-sm font-medium sm:px-4", presentation.text, presentation.background)}
      aria-label={`系统状态：${presentation.detail}`}
      title={presentation.detail}
    >
      <span className={cx("size-2.5 rounded-full ring-4 ring-current/10", presentation.dot)} aria-hidden="true" />
      <span className="text-xs font-bold md:hidden" aria-hidden="true">{presentation.compactLabel}</span>
      <span className="hidden xl:inline">{presentation.label}</span>
    </Link>
  );
}

export default function App({ identity, logout }) {
  const location = useLocation();
  const navigate = useNavigate();
  const [mobileOpen, setMobileOpen] = useState(false);
  const [globalSearchOpen, setGlobalSearchOpen] = useState(false);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(() => typeof window !== "undefined" && window.localStorage.getItem("ingot.sidebar.collapsed") === "true");
  const [expandedSectionId, setExpandedSectionId] = useState("overview");
  const displayName = identity?.displayName || identity?.username || "当前操作员";
  const userInitials = displayName.trim().slice(0, 2).toUpperCase();
  const canConfigure = (identity?.roles || []).some(role => role === "process.engineer" || role === "platform.admin");
  const navigationSections = useMemo(() => sectionsForIdentity(identity), [identity]);
  const globalSearchEntries = useMemo(() => searchEntriesForSections(navigationSections), [navigationSections]);
  const usesAppleShortcut = useMemo(isApplePlatform, []);
  const searchShortcutLabel = usesAppleShortcut ? "⌘ K" : "Ctrl K";
  const isChatWorkspace = location.pathname === "/chat" || location.pathname.startsWith("/chat/");

  useEffect(() => {
    function handleShortcut(event) {
      const modifierPressed = usesAppleShortcut ? event.metaKey : event.ctrlKey;
      if (modifierPressed && event.key.toLowerCase() === "k") {
        event.preventDefault();
        setGlobalSearchOpen(true);
      }
    }
    window.addEventListener("keydown", handleShortcut);
    return () => window.removeEventListener("keydown", handleShortcut);
  }, [usesAppleShortcut]);

  const section = useMemo(() => navigationSections
    .map(item => ({
      item,
      matchLength: sectionItems(item)
        .map(([path]) => path)
        .filter(path => location.pathname === path || location.pathname.startsWith(`${path}/`))
        .reduce((longest, path) => Math.max(longest, path.length), item.path === location.pathname ? item.path.length : -1),
    }))
    .filter(candidate => candidate.matchLength >= 0)
    .sort((left, right) => right.matchLength - left.matchLength)[0]?.item ?? sections[0], [location.pathname, navigationSections]);
  const activeNavigationPath = useMemo(() => sectionItems(section)
    .map(([path]) => path)
    .filter(path => path === location.pathname || location.pathname.startsWith(`${path}/`))
    .sort((left, right) => right.length - left.length)[0], [location.pathname, section]);
  useEffect(() => {
    setExpandedSectionId(section.id);
  }, [section.id]);
  useEffect(() => {
    window.localStorage.setItem("ingot.sidebar.collapsed", String(sidebarCollapsed));
  }, [sidebarCollapsed]);
  function toggleSection(sectionId) {
    setExpandedSectionId(current => current === sectionId ? null : sectionId);
  }
  const page = location.pathname.startsWith("/chat/")
    ? pageDetails["/chat"]
    : location.pathname.startsWith("/process-executions/")
    ? ["运行详情", "查看单次生产运行的过程、质量和数据完整性"]
    : location.pathname.startsWith("/edges/")
      ? ["节点诊断", "查看现场节点的连接、采集、上行和最近日志"]
      : location.pathname.startsWith("/configuration/ingestion-tasks/")
          ? ["配置数据源", "配置设备连接、工艺映射和发布前验证"]
          : pageDetails[location.pathname] ?? ["页面不存在", "地址可能已经变更，请返回可用功能页面"];
  return (
    <div className="app-canvas min-h-screen text-slate-900">
      <aside className={cx("fixed inset-y-0 left-0 z-50 hidden flex-col border-r border-white/8 bg-coal-950 text-white shadow-[12px_0_40px_rgba(7,16,14,.08)] transition-[width] duration-200 lg:flex", sidebarCollapsed ? "w-18" : "w-64")}>
        <div className={cx("flex h-16 shrink-0 items-center border-b border-white/8", sidebarCollapsed ? "justify-center px-3" : "justify-between px-4")}>
          <button className={cx("flex min-w-0 items-center gap-3 text-left", sidebarCollapsed && "justify-center")} onClick={() => navigate("/workbench")} aria-label="返回工作台">
            <span className="grid size-9 shrink-0 place-items-center rounded-xl bg-white/6 ring-1 ring-white/12">
              <img src="/ingot-mark.svg" alt="" className="size-7" />
            </span>
            {!sidebarCollapsed && <span className="grid min-w-0"><strong className="text-base leading-5 text-white">Ingot</strong><small className="truncate text-xs text-slate-400">工艺证据工作台</small></span>}
          </button>
          {!sidebarCollapsed && <button type="button" className="grid size-9 place-items-center rounded-lg text-slate-500 hover:bg-white/8 hover:text-white" onClick={() => setSidebarCollapsed(true)} aria-label="收起侧边栏"><ChevronDoubleLeftIcon className="size-4.5" /></button>}
        </div>
        <SidebarNavigation
          navigationSections={navigationSections}
          activeSectionId={section.id}
          activeNavigationPath={activeNavigationPath}
          expandedSectionId={expandedSectionId}
          compact={sidebarCollapsed}
          onToggle={toggleSection}
        />
        {sidebarCollapsed && <button type="button" className="mx-3 mb-3 grid h-10 place-items-center rounded-xl text-slate-500 hover:bg-white/8 hover:text-white" onClick={() => setSidebarCollapsed(false)} aria-label="展开侧边栏"><ChevronDoubleRightIcon className="size-4.5" /></button>}
      </aside>

      <header className={cx("fixed inset-x-0 top-0 z-40 flex h-16 items-stretch border-b border-slate-200/80 bg-white/92 shadow-[0_1px_0_rgba(7,16,14,.02)] backdrop-blur-xl transition-[left] duration-200 lg:right-0", sidebarCollapsed ? "lg:left-18" : "lg:left-64")}>
        <button className="grid w-14 shrink-0 place-items-center text-slate-600 hover:bg-slate-50 lg:hidden" onClick={() => setMobileOpen(true)} aria-label="打开主导航"><Bars3Icon className="size-5" /></button>
        <div className="flex min-w-0 flex-1 items-center px-3 sm:px-5">
          <div className="flex min-w-0 items-center gap-3">
            <nav aria-label="面包屑" className="flex items-center gap-1.5 text-[13px] text-slate-500">
              <Link to={section.path} className="shrink-0 hover:text-blue-700">{section.label}</Link>
              <ChevronRightIcon className="size-3.5 shrink-0" aria-hidden="true" />
              <span className="truncate font-medium text-slate-700">{page[0]}</span>
            </nav>
            <span className="hidden h-4 w-px bg-slate-200 2xl:block" aria-hidden="true" />
            <p className="hidden truncate text-xs text-slate-400 2xl:block">{page[1]}</p>
          </div>
        </div>
        <SystemStatusIndicator />
        <button className="flex items-center gap-2 border-l border-slate-100 px-3 text-sm text-slate-600 hover:bg-slate-50 sm:px-4" onClick={() => setGlobalSearchOpen(true)} aria-label="打开功能搜索" aria-keyshortcuts={usesAppleShortcut ? "Meta+K" : "Control+K"}>
          <MagnifyingGlassIcon className="size-5" /><span className="hidden md:inline">功能搜索</span><kbd className="hidden rounded border border-slate-200 bg-slate-50 px-1.5 py-0.5 text-[10px] text-slate-400 xl:inline">{searchShortcutLabel}</kbd>
        </button>
        <Menu as="div" className="relative flex border-l border-slate-100">
          <MenuButton className="grid w-16 place-items-center text-slate-600 hover:bg-slate-50" aria-label="用户菜单">
            <span className="grid size-8 place-items-center rounded-full bg-coal-900 text-[11px] font-bold text-white ring-2 ring-white">{userInitials}</span>
          </MenuButton>
          <MenuItems transition anchor="bottom end" className="z-100 mt-2 w-64 origin-top-right rounded-xl border border-slate-200 bg-white p-1 text-sm shadow-xl transition data-closed:scale-95 data-closed:opacity-0">
            <div className="border-b border-slate-100 px-3 py-2">
              <p className="truncate font-medium text-slate-900">{displayName}</p>
              {displayName !== identity?.username && <p className="mt-0.5 truncate text-[13px] text-slate-500">{identity?.username || "当前账户"}</p>}
              <dl className="mt-2 grid gap-1 text-[13px] text-slate-500">
                <div className="grid grid-cols-[3rem_1fr] gap-2"><dt>岗位</dt><dd className="text-slate-700">{formatRoleSummary(identity?.roles)}</dd></div>
                <div className="grid grid-cols-[3rem_1fr] gap-2"><dt>站点</dt><dd className="break-words text-slate-700">{formatSiteScope(identity?.siteIds, identity?.roles)}</dd></div>
              </dl>
            </div>
            <MenuItem><a href="https://docs.ingotstack.com/zh" className="block rounded-lg px-3 py-2 text-slate-700 data-focus:bg-slate-100">产品文档</a></MenuItem>
            <MenuItem><button type="button" onClick={logout} className="block w-full rounded-lg px-3 py-2 text-left text-slate-700 data-focus:bg-slate-100">退出登录</button></MenuItem>
          </MenuItems>
        </Menu>
      </header>

      {isChatWorkspace ? (
        <main className={cx("h-[100dvh] overflow-hidden pt-16 transition-[margin] duration-200", sidebarCollapsed ? "lg:ml-18" : "lg:ml-64")}>
          <AppRoutes identity={identity} canConfigure={canConfigure} />
        </main>
      ) : (
        <div className={cx("pt-16 transition-[margin] duration-200", sidebarCollapsed ? "lg:ml-18" : "lg:ml-64")}>
          <main className="mx-auto w-full max-w-[1600px] p-4 sm:p-7 lg:p-8">
            <AppRoutes identity={identity} canConfigure={canConfigure} />
          </main>
        </div>
      )}

      <Dialog open={mobileOpen} onClose={setMobileOpen} className="relative z-80 lg:hidden">
        <DialogBackdrop className="fixed inset-0 bg-coal-950/55 backdrop-blur-sm" />
        <DialogPanel className="fixed inset-y-0 left-0 flex w-80 max-w-[88vw] flex-col bg-coal-950 text-white shadow-2xl">
          <div className="flex h-16 items-center justify-between border-b border-white/8 px-4">
            <button className="flex items-center gap-3 text-left" onClick={() => { setMobileOpen(false); navigate("/workbench"); }}><img src="/ingot-mark.svg" alt="" className="size-8" /><span><strong className="block">Ingot</strong><small className="text-xs text-slate-400">工艺证据工作台</small></span></button>
            <button className="grid size-9 place-items-center rounded-lg text-slate-400 hover:bg-white/8 hover:text-white" onClick={() => setMobileOpen(false)} aria-label="关闭模块导航">
              <XMarkIcon className="size-5" />
            </button>
          </div>
          <SidebarNavigation navigationSections={navigationSections} activeSectionId={section.id} activeNavigationPath={activeNavigationPath} expandedSectionId={expandedSectionId} onToggle={toggleSection} onNavigate={() => setMobileOpen(false)} />
        </DialogPanel>
      </Dialog>
      <GlobalSearchDialog
        open={globalSearchOpen}
        onClose={() => setGlobalSearchOpen(false)}
        navigate={navigate}
        entries={globalSearchEntries}
      />
      <ToastHost />
    </div>
  );
}

export function RequireRole({ identity, roles, children }) {
  const allowed = (identity?.roles || []).some(role => roles.includes(role));
  if (allowed) return children;
  return (
    <div className="grid min-h-[55vh] place-items-center rounded-lg border border-slate-200 bg-white p-8 text-center" role="alert">
      <div className="max-w-lg">
        <p className="text-sm font-semibold text-amber-700">权限不足</p>
        <h1 className="mt-2 text-2xl font-semibold text-slate-950">当前岗位不能访问此功能</h1>
        <p className="mt-3 text-sm leading-6 text-slate-600">该页面仅向平台管理员开放。你仍可继续处理已授权的生产、质量和工艺任务。</p>
        <LinkButton className="mt-5" to="/workbench">返回工作台</LinkButton>
      </div>
    </div>
  );
}

function AppRoutes({ identity, canConfigure }) {
  return (
    <Routes>
      <Route path="/" element={<Navigate to="/workbench" replace />} />
      <Route path="/workbench" element={<Pages.WorkbenchPage identity={identity} />} />
      <Route path="/analysis" element={<Pages.AnalysisHubPage identity={identity} />} />
      <Route path="/chat" element={<Pages.ChatPage />} />
      <Route path="/chat/:conversationId" element={<Pages.ChatPage />} />
      <Route path="/explorer" element={<Pages.ObjectExplorerPage />} />
      <Route path="/process-executions" element={<Pages.ProcessExecutionsPage />} />
      <Route path="/process-executions/:executionId" element={<Pages.ProcessExecutionDetailPage />} />
      <Route path="/events" element={<Pages.EventsPage />} />
      <Route path="/production/changeover" element={<Pages.ProductionSetupPage section="context" canWrite={canConfigure} />} />
      <Route path="/production/tooling-installations" element={<Pages.ProductionSetupPage section="installation" canWrite={canConfigure} />} />
      <Route path="/configuration/component-types" element={<Pages.ProductionSetupPage section="componentType" canWrite={canConfigure} />} />
      <Route path="/configuration/components" element={<Pages.ProductionSetupPage section="component" canWrite={canConfigure} />} />
      <Route path="/configuration/tooling-types" element={<Pages.ProductionSetupPage section="type" canWrite={canConfigure} />} />
      <Route path="/configuration/tooling-assemblies" element={<Pages.ProductionSetupPage section="assembly" canWrite={canConfigure} />} />
      <Route path="/inspections" element={<Pages.InspectionsPage />} />
      <Route path="/quality-analysis" element={<Pages.QualityAnalysisPage />} />
      <Route path="/configuration/inspection-definitions" element={<Pages.InspectionDefinitionsPage canWrite={canConfigure} />} />
      <Route path="/configuration/quality-plans" element={<Pages.QualityPlansPage canWrite={canConfigure} />} />
      <Route path="/comparisons" element={<Pages.ExecutionComparisonPage />} />
      <Route path="/model-service" element={<RequireRole identity={identity} roles={["platform.admin"]}><Pages.ModelServiceConfigurationPage /></RequireRole>} />
      <Route path="/data-quality" element={<Pages.DataQualityPage />} />
      <Route path="/configuration" element={<Pages.ConfigurationHubPage canWrite={canConfigure} />} />
      <Route path="/configuration/process-analysis-plans" element={<Pages.ProcessAnalysisPlansPage canWrite={canConfigure} />} />
      <Route path="/configuration/process-data-models" element={<Pages.ProcessDataModelsPage canWrite={canConfigure} />} />
      <Route path="/configuration/process-specifications" element={<Pages.ProcessSpecificationsPage canWrite={canConfigure} />} />
      <Route path="/configuration/ingestion-tasks" element={<IngestionTasksPage canWrite={canConfigure} />} />
      <Route path="/configuration/ingestion-tasks/:taskId" element={<IngestionTaskPage canWrite={canConfigure} />} />
      <Route path="/edges" element={<Pages.EdgesPage />} />
      <Route path="/edges/:edgeId" element={<Pages.EdgeDetailPage />} />
      <Route path="/platform-metrics" element={<Pages.MetricsPage />} />
      <Route path="/logs" element={<RequireRole identity={identity} roles={["platform.admin"]}><Pages.LogsPage /></RequireRole>} />
      <Route path="/identity/users" element={<RequireRole identity={identity} roles={["platform.admin"]}><Pages.UsersPage /></RequireRole>} />
      <Route path="*" element={<Pages.NotFoundPage />} />
    </Routes>
  );
}
