import { Dialog, DialogBackdrop, DialogPanel, Menu, MenuButton, MenuItem, MenuItems } from "@headlessui/react";
import {
  AdjustmentsHorizontalIcon,
  BoltIcon,
  BeakerIcon,
  ClipboardDocumentCheckIcon,
  ChevronRightIcon,
  CircleStackIcon,
  Cog6ToothIcon,
  MagnifyingGlassIcon,
  MagnifyingGlassCircleIcon,
  RectangleGroupIcon,
  XMarkIcon,
} from "@heroicons/react/24/outline";
import { useEffect, useMemo, useRef, useState } from "react";
import { Link, Navigate, Route, Routes, useLocation, useNavigate } from "react-router";
import * as Pages from "./pages";
import { IngestionTaskPage, IngestionTasksPage } from "./acquisition/IngestionTaskPage";
import { cx, Input, ToastHost } from "./ui/components";

const sections = [
  {
    id: "overview", label: "工作台", icon: BoltIcon, path: "/workbench", groups: [
      { items: [["/workbench", "我的工作台"]] },
    ],
  },
  {
    id: "evidence", label: "生产运行", icon: CircleStackIcon, path: "/process-executions", groups: [
      { label: "运行与追溯", items: [["/process-executions", "运行记录"], ["/events", "运行事件"], ["/explorer", "工业对象"]] },
      { label: "生产准备", items: [["/production/changeover", "生产上下文"], ["/production/tooling-installations", "工装装卸"]] },
    ],
  },
  {
    id: "quality", label: "质量管理", icon: ClipboardDocumentCheckIcon, path: "/inspections", groups: [
      { label: "日常质量", items: [["/inspections", "质量任务"]] },
      { label: "问题分析", items: [["/quality-analysis", "质量偏差分析"]] },
    ],
  },
  {
    id: "diagnosis", label: "工艺分析", icon: MagnifyingGlassCircleIcon, path: "/analysis", groups: [
      { label: "开始分析", items: [["/analysis", "分析总览"]] },
      { label: "分析工作", items: [["/comparisons", "运行对比"], ["/chat", "分析助手"]] },
      { label: "分析基础", items: [["/data-quality", "数据可信度"]] },
    ],
  },
  {
    id: "optimization", label: "工艺优化", icon: BeakerIcon, path: "/research-projects", groups: [
      { label: "优化工作", items: [["/research-projects", "研发项目"]] },
      { label: "复用资产", items: [["/research-assets", "研发资产"]] },
    ],
  },
  {
    id: "configuration", label: "配置中心", icon: AdjustmentsHorizontalIcon, path: "/configuration", groups: [
      { label: "开始配置", items: [["/configuration", "配置总览"]] },
      { label: "数据标准", items: [["/configuration/process-data-models", "工艺数据模型"], ["/configuration/process-specifications", "工艺规范"]] },
      { label: "现场接入", items: [["/edges", "现场节点"], ["/configuration/ingestion-tasks", "设备接入"]] },
      { label: "分析与质量", items: [["/configuration/process-analysis-plans", "运行分析方案"], ["/configuration/inspection-definitions", "检测定义"], ["/configuration/quality-plans", "质量方案"]] },
      { label: "工装定义", items: [["/configuration/component-types", "组件分类"], ["/configuration/components", "组件资产"], ["/configuration/tooling-types", "装配模板"], ["/configuration/tooling-assemblies", "工装总成"]] },
      { label: "组合发布", items: [["/configuration/scenario-packages", "工艺配置方案"]] },
    ],
  },
];

const systemSection = {
  id: "system", label: "系统管理", icon: Cog6ToothIcon, path: "/identity/users", groups: [
    { items: [["/identity/users", "用户与权限"], ["/platform-metrics", "平台运行状态"], ["/logs", "运行日志"], ["/golden-questions", "助手评测"]] },
  ],
};

const allSections = [...sections, systemSection];

const sectionItems = section => section.groups.flatMap(group => group.items);

const pageDetails = {
  "/research-projects": ["研发项目", "从问题、证据与实验推进到经过验证的工艺窗口"],
  "/workbench": ["我的工作台", "集中查看待办、生产状态、质量风险与研发进展"],
  "/chat": ["工艺分析助手", "用自然语言查询运行、质量、配置、研发与知识证据"],
  "/analysis": ["工艺分析", "从生产运行和可信证据进入差异比较、候选原因与工程验证"],
  "/research-assets": ["研发资产", "查看项目可复用的数据集、模型、机理和知识"],
  "/explorer": ["工业对象", "选择真实业务对象，再进入它的运行、事件、质量与数据健康视图"],
  "/process-executions": ["运行记录", "查看生产运行及其数据、工艺与质量上下文"],
  "/events": ["运行事件", "查询、追溯并关联运行上下文"],
  "/production/changeover": ["生产上下文", "让设备、产品、工艺规范和已装工装对接下来的运行生效"],
  "/production/tooling-installations": ["工装装卸", "记录工装组合版本在设备上的装入与卸下区间"],
  "/inspections": ["质量任务", "处理视觉检查、人工质检与原图复核"],
  "/quality-analysis": ["质量偏差分析", "按产品、工艺规范和运行上下文定位质量偏差并追溯证据"],
  "/comparisons": ["运行对比", "比较同类生产运行、运行段或时间窗口，生成待验证的候选原因"],
  "/golden-questions": ["评测问题集", "用真实问题持续核对事实、记录引用、正确拒绝和因果边界"],
  "/data-quality": ["数据可信度", "检查运行对象的数据范围、采样连续性与运行完整性"],
  "/configuration": ["配置总览", "按依赖顺序完成数据、接入、分析、质量、工装与最终发布"],
  "/configuration/scenario-packages": ["工艺配置方案", "版本化组合工艺数据、采集、分析、质量、上下文和约束"],
  "/configuration/process-analysis-plans": ["运行分析方案", "版本化定义同类比较条件、对齐方式、质量分组和数据项"],
  "/configuration/process-data-models": ["工艺数据模型", "定义工艺变量、阶段号和控制参数，供设备点位统一映射"],
  "/configuration/process-specifications": ["工艺规范", "维护引用数据模型的完整工艺规范版本"],
  "/configuration/ingestion-tasks": ["设备接入", "选择采集节点和通信驱动，将设备点位映射到工艺变量"],
  "/configuration/inspection-definitions": ["检测定义", "定义要检测的特性、录入类型和判定规则"],
  "/configuration/quality-plans": ["质量方案", "配置产品适用的检测项目与复核规则"],
  "/configuration/component-types": ["组件分类", "维护模芯、模架等物理资产类别；上模和下模由装配位置决定"],
  "/configuration/components": ["组件资产", "登记具有独立资产编号和序列号的可更换物理组件"],
  "/configuration/tooling-types": ["装配模板", "定义工装总成结构、装配位置和各位置允许的组件分类"],
  "/configuration/tooling-assemblies": ["工装总成", "查看工装总成身份、不可变配置版本及每个位置的实际成员"],
  "/edges": ["现场节点", "查看负责连接设备、仪器、系统并上报数据的现场节点"],
  "/platform-metrics": ["平台运行状态", "确认中心服务、现场节点和数据上行是否正常"],
  "/logs": ["运行日志", "查询平台运行记录"],
  "/identity/users": ["用户与权限", "管理本地账户、岗位权限、密码和启停状态"],
};

const globalSearchEntries = allSections.flatMap(section => sectionItems(section).map(([path, label]) => ({
  path,
  label,
  section: section.label,
  description: pageDetails[path]?.[1] || "打开功能页面",
})));

function isApplePlatform() {
  if (typeof navigator === "undefined") return false;
  const platform = navigator.userAgentData?.platform || navigator.platform || navigator.userAgent;
  return /mac|iphone|ipad|ipod|ios/i.test(platform);
}

export default function App({ identity, logout }) {
  const location = useLocation();
  const navigate = useNavigate();
  const [mobileOpen, setMobileOpen] = useState(false);
  const [globalSearchOpen, setGlobalSearchOpen] = useState(false);
  const displayName = identity?.displayName || identity?.username || "当前操作员";
  const userInitials = displayName.trim().slice(0, 2).toUpperCase();
  const canConfigure = (identity?.roles || []).some(role => role === "process.engineer" || role === "platform.admin");
  const usesAppleShortcut = useMemo(isApplePlatform, []);
  const searchShortcutLabel = usesAppleShortcut ? "⌘ K" : "Ctrl K";
  const isChatWorkspace = location.pathname === "/chat";

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

  const section = useMemo(
    () => allSections.find(item => item.path === location.pathname || sectionItems(item).some(([path]) => location.pathname === path || location.pathname.startsWith(`${path}/`))) ?? sections[0],
    [location.pathname],
  );
  const activeNavigationPath = useMemo(() => sectionItems(section)
    .map(([path]) => path)
    .filter(path => path === location.pathname || location.pathname.startsWith(`${path}/`))
    .sort((left, right) => right.length - left.length)[0], [location.pathname, section]);
  const page = location.pathname.startsWith("/process-executions/")
    ? ["运行详情", "查看单次生产运行的过程、质量和数据完整性"]
    : location.pathname.startsWith("/edges/")
      ? ["节点诊断", "查看现场节点的连接、采集、上行和最近日志"]
      : location.pathname.startsWith("/research-projects/")
        ? ["研发项目工作区", "围绕当前问题推进假设、实验、验证和知识复用"]
        : location.pathname.startsWith("/configuration/ingestion-tasks/")
          ? ["配置数据源", "配置设备连接、工艺映射和发布前验证"]
          : pageDetails[location.pathname] ?? ["页面不存在", "地址可能已经变更，请返回可用功能页面"];
  return (
    <div className="min-h-screen bg-slate-50 text-slate-900">
      <header className="fixed inset-x-0 top-0 z-50 flex h-16 items-stretch border-b border-slate-200 bg-white/95 shadow-sm backdrop-blur">
        <button className="flex w-16 shrink-0 items-center gap-3 border-r border-slate-100 px-3 text-left sm:w-55 sm:px-5" onClick={() => navigate("/workbench")}>
          <span className="grid size-9 place-items-center rounded-xl bg-amber-50 ring-1 ring-amber-200">
            <img src="/ingot-mark.svg" alt="" className="size-7" />
          </span>
          <span className="hidden sm:grid">
            <strong className="text-base leading-5 text-slate-950">Ingot</strong>
            <small className="text-[10px] text-slate-500">工艺追因与优化系统</small>
          </span>
        </button>
        <Menu as="div" className="relative flex min-w-0 flex-1 xl:hidden">
          <MenuButton className="flex min-w-0 flex-1 items-center gap-2 px-3 text-sm font-medium text-slate-700 hover:bg-slate-50 sm:px-4" aria-label="打开全局模块导航">
            <section.icon className="size-5 shrink-0 text-blue-600" />
            <span className="truncate">{section.label}</span>
          </MenuButton>
          <MenuItems transition anchor="bottom start" className="z-100 mt-2 w-64 origin-top-left rounded-xl border border-slate-200 bg-white p-2 text-sm shadow-xl transition data-closed:scale-95 data-closed:opacity-0">
            {allSections.map(item => {
              const Icon = item.icon;
              const active = item.id === section.id;
              return (
                <MenuItem key={item.id}>
                  <Link
                    to={item.path}
                    className={cx(
                      "flex items-center gap-3 rounded-lg px-3 py-2.5 text-slate-700 data-focus:bg-slate-100",
                      active && "bg-blue-50 font-medium text-blue-700",
                    )}
                  >
                    <Icon className="size-5" />
                    {item.label}
                  </Link>
                </MenuItem>
              );
            })}
          </MenuItems>
        </Menu>
        <nav className="hidden min-w-0 flex-1 xl:flex" aria-label="全局导航">
          {sections.map(item => {
            const Icon = item.icon;
            const active = item.id === section.id;
            return (
              <Link
                key={item.id}
                to={item.path}
                className={cx(
                  "relative flex min-w-0 flex-1 items-center justify-center gap-2 px-2 text-xs font-medium transition 2xl:px-4 2xl:text-sm",
                  active ? "bg-blue-50/70 text-blue-700 after:absolute after:inset-x-4 after:bottom-0 after:h-0.5 after:bg-blue-600" : "text-slate-600 hover:bg-slate-50 hover:text-slate-950",
                )}
              >
                <Icon className="size-4.5 shrink-0" />
                <span className="whitespace-nowrap">{item.label}</span>
              </Link>
            );
          })}
        </nav>
        <button className="flex items-center gap-2 border-l border-slate-100 px-3 text-sm text-slate-600 hover:bg-slate-50 sm:px-4" onClick={() => setGlobalSearchOpen(true)} aria-label="打开功能搜索" aria-keyshortcuts={usesAppleShortcut ? "Meta+K" : "Control+K"}>
          <MagnifyingGlassIcon className="size-5" /><span className="hidden lg:inline">功能搜索</span><kbd className="hidden rounded border border-slate-200 bg-slate-50 px-1.5 py-0.5 text-[10px] text-slate-400 2xl:inline">{searchShortcutLabel}</kbd>
        </button>
        <Menu as="div" className="relative hidden border-l border-slate-100 sm:flex">
          <MenuButton className="grid w-12 place-items-center text-slate-600 hover:bg-slate-50" aria-label="打开系统管理">
            <Cog6ToothIcon className="size-5" />
          </MenuButton>
          <MenuItems transition anchor="bottom end" className="z-100 mt-2 w-52 origin-top-right rounded-xl border border-slate-200 bg-white p-1 text-sm shadow-xl transition data-closed:scale-95 data-closed:opacity-0">
            <p className="px-3 py-2 text-xs font-semibold text-slate-400">系统管理</p>
            {sectionItems(systemSection).map(([path, label]) => (
              <MenuItem key={path}><Link to={path} className="block rounded-lg px-3 py-2 text-slate-700 data-focus:bg-slate-100">{label}</Link></MenuItem>
            ))}
          </MenuItems>
        </Menu>
        <Menu as="div" className="relative flex border-l border-slate-100">
          <MenuButton className="grid w-14 place-items-center text-slate-600 hover:bg-slate-50" aria-label="用户菜单">
            <span className="text-xs font-semibold">{userInitials}</span>
          </MenuButton>
          <MenuItems transition anchor="bottom end" className="z-100 mt-2 w-48 origin-top-right rounded-xl border border-slate-200 bg-white p-1 text-sm shadow-xl transition data-closed:scale-95 data-closed:opacity-0">
            <div className="border-b border-slate-100 px-3 py-2">
              <p className="truncate font-medium text-slate-900">{displayName}</p>
              <p className="mt-0.5 truncate text-xs text-slate-500">{identity?.username || "当前账户"}</p>
            </div>
            <MenuItem><a href="https://docs.ingotstack.com/zh" className="block rounded-lg px-3 py-2 text-slate-700 data-focus:bg-slate-100">产品文档</a></MenuItem>
            <MenuItem><button type="button" onClick={logout} className="block w-full rounded-lg px-3 py-2 text-left text-slate-700 data-focus:bg-slate-100">退出登录</button></MenuItem>
          </MenuItems>
        </Menu>
      </header>

      {isChatWorkspace ? (
        <main className="h-[100dvh] overflow-hidden pt-16">
          <AppRoutes identity={identity} canConfigure={canConfigure} />
        </main>
      ) : (
      <div className="pt-16">
          <aside className="fixed inset-y-16 left-0 z-30 hidden w-55 overflow-y-auto border-r border-slate-200 bg-white lg:block">
            <div className="flex h-16 items-center gap-2 border-b border-slate-100 px-5">
              <section.icon className="size-5 text-blue-600" />
              <strong className="text-sm">{section.label}</strong>
            </div>
            <nav className="grid gap-4 p-3" aria-label={`${section.label}导航`}>
              {section.groups.map((group, groupIndex) => (
                <div key={group.label || groupIndex} className="grid gap-1">
                  {section.groups.length > 1 && <p className="px-3 pt-1 text-[11px] font-semibold tracking-wide text-slate-400">{group.label}</p>}
                  {group.items.map(([path, label]) => (
                    <Link key={path} to={path} className={cx("rounded-lg px-3 py-2.5 text-sm", path === activeNavigationPath ? "bg-blue-50 font-medium text-blue-700" : "text-slate-600 hover:bg-slate-50 hover:text-slate-950")}>
                      {label}
                    </Link>
                  ))}
                </div>
              ))}
            </nav>
          </aside>

        <div className="lg:ml-55">
          <div className="sticky top-16 z-20 flex min-h-16 items-center gap-3 border-b border-slate-200 bg-white/90 px-4 backdrop-blur sm:px-6">
              <button className="grid size-9 place-items-center rounded-lg text-slate-600 hover:bg-slate-100 lg:hidden" onClick={() => setMobileOpen(true)} aria-label="打开模块导航">
                <RectangleGroupIcon className="size-5" />
              </button>
            <div className="min-w-0">
              <nav aria-label="面包屑" className="flex items-center gap-1.5 text-xs text-slate-500">
                <Link to={section.path} className="shrink-0 hover:text-blue-700">{section.label}</Link>
                <ChevronRightIcon className="size-3.5 shrink-0" aria-hidden="true" />
                <span className="truncate font-medium text-slate-700">{page[0]}</span>
              </nav>
              <p className="mt-1 truncate text-xs text-slate-500">{page[1]}</p>
            </div>
          </div>
          <main className="mx-auto w-full max-w-[1600px] p-4 sm:p-6">
            <AppRoutes identity={identity} canConfigure={canConfigure} />
          </main>
        </div>
      </div>
      )}

      <Dialog open={mobileOpen} onClose={setMobileOpen} className="relative z-80 lg:hidden">
        <DialogBackdrop className="fixed inset-0 bg-slate-950/30" />
        <DialogPanel className="fixed inset-y-0 left-0 w-72 overflow-y-auto bg-white shadow-2xl">
          <div className="flex h-16 items-center justify-between border-b border-slate-200 px-4">
            <strong>{section.label}</strong>
            <button className="grid size-9 place-items-center rounded-lg hover:bg-slate-100" onClick={() => setMobileOpen(false)} aria-label="关闭模块导航">
              <XMarkIcon className="size-5" />
            </button>
          </div>
          <nav className="grid gap-4 p-3">
            {section.groups.map((group, groupIndex) => (
              <div key={group.label || groupIndex} className="grid gap-1">
                {section.groups.length > 1 && <p className="px-3 pt-1 text-[11px] font-semibold tracking-wide text-slate-400">{group.label}</p>}
                {group.items.map(([path, label]) => (
                  <Link key={path} to={path} onClick={() => setMobileOpen(false)} className={cx("rounded-lg px-3 py-3 text-sm", path === activeNavigationPath ? "bg-blue-50 font-medium text-blue-700" : "text-slate-700 hover:bg-slate-50")}>
                    {label}
                  </Link>
                ))}
              </div>
            ))}
          </nav>
        </DialogPanel>
      </Dialog>
      <GlobalSearchDialog
        open={globalSearchOpen}
        onClose={() => setGlobalSearchOpen(false)}
        navigate={navigate}
      />
      <ToastHost />
    </div>
  );
}

function GlobalSearchDialog({ open, onClose, navigate }) {
  const [query, setQuery] = useState("");
  const inputRef = useRef(null);
  useEffect(() => {
    if (!open) return;
    setQuery("");
    window.setTimeout(() => inputRef.current?.focus(), 0);
  }, [open]);
  const results = useMemo(() => {
    const keyword = query.trim().toLowerCase();
    if (!keyword) return globalSearchEntries;
    return globalSearchEntries.filter(item => `${item.label} ${item.section} ${item.description}`.toLowerCase().includes(keyword));
  }, [query]);
  function select(path) {
    onClose();
    navigate(path);
  }
  return (
    <Dialog open={open} onClose={onClose} className="relative z-100">
      <DialogBackdrop className="fixed inset-0 bg-slate-950/35 backdrop-blur-sm" />
      <div className="fixed inset-0 overflow-y-auto p-4 pt-[12vh] sm:p-6 sm:pt-[14vh]">
        <DialogPanel className="mx-auto w-full max-w-2xl overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl">
          <div className="border-b border-slate-100 p-4 sm:p-5">
            <p className="text-sm font-semibold text-slate-950">功能搜索</p>
            <p className="mt-1 text-xs text-slate-500">查找生产运行、质量管理、工艺分析、工艺优化、配置和系统功能。</p>
            <Input
              ref={inputRef}
              value={query}
              onChange={event => setQuery(event.target.value)}
              placeholder="例如：研发项目、运行对比、设备接入、质量任务"
              className="mt-4 h-11 rounded-xl bg-slate-50 px-4 focus:bg-white"
            />
          </div>
          <div className="max-h-[55vh] overflow-y-auto p-2">
            {results.length ? results.map(item => (
              <button key={item.path} type="button" onClick={() => select(item.path)} className="flex w-full items-start gap-3 rounded-xl px-3 py-3 text-left hover:bg-blue-50 focus-visible:bg-blue-50">
                <span className="mt-0.5 rounded-md bg-slate-100 px-2 py-1 text-[11px] font-medium text-slate-600">{item.section}</span>
                <span className="min-w-0"><span className="block text-sm font-medium text-slate-900">{item.label}</span><span className="mt-0.5 block text-xs leading-5 text-slate-500">{item.description}</span></span>
              </button>
            )) : <div className="px-4 py-10 text-center text-sm text-slate-500">没有匹配的功能。请换一个关键词。</div>}
          </div>
          <div className="flex items-center justify-between border-t border-slate-100 px-5 py-3 text-xs text-slate-500"><span>搜索产品功能</span><span>Esc 关闭</span></div>
        </DialogPanel>
      </div>
    </Dialog>
  );
}

function AppRoutes({ identity, canConfigure }) {
  return (
    <Routes>
      <Route path="/" element={<Navigate to="/workbench" replace />} />
      <Route path="/research-projects" element={<Pages.ResearchProjectsPage identity={identity} />} />
      <Route path="/research-projects/:projectId" element={<Pages.ResearchProjectsPage identity={identity} />} />
      <Route path="/workbench" element={<Pages.WorkbenchPage identity={identity} />} />
      <Route path="/analysis" element={<Pages.AnalysisHubPage />} />
      <Route path="/chat" element={<Pages.ChatPage />} />
      <Route path="/research-assets" element={<Pages.ResearchAssetsPage />} />
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
      <Route path="/production-setup" element={<Navigate to="/production/changeover" replace />} />
      <Route path="/inspections" element={<Pages.InspectionsPage />} />
      <Route path="/quality-analysis" element={<Pages.QualityAnalysisPage />} />
      <Route path="/quality-plans" element={<Navigate to="/configuration/quality-plans" replace />} />
      <Route path="/configuration/inspection-definitions" element={<Pages.InspectionDefinitionsPage canWrite={canConfigure} />} />
      <Route path="/configuration/quality-plans" element={<Pages.QualityPlansPage canWrite={canConfigure} />} />
      <Route path="/comparisons" element={<Pages.ExecutionComparisonPage />} />
      <Route path="/golden-questions" element={<Pages.GoldenQuestionsPage />} />
      <Route path="/data-quality" element={<Pages.DataQualityPage />} />
      <Route path="/process-improvement" element={<Navigate to="/research-projects" replace />} />
      <Route path="/configuration" element={<Pages.ConfigurationHubPage canWrite={canConfigure} />} />
      <Route path="/configuration/scenario-packages" element={<Pages.ScenarioPackagesPage canWrite={canConfigure} />} />
      <Route path="/configuration/process-analysis-plans" element={<Pages.ProcessAnalysisPlansPage canWrite={canConfigure} />} />
      <Route path="/profiles" element={<Navigate to="/configuration/process-data-models" replace />} />
      <Route path="/configuration/process-data-models" element={<Pages.ProcessDataModelsPage canWrite={canConfigure} />} />
      <Route path="/configuration/process-specifications" element={<Pages.ProcessSpecificationsPage canWrite={canConfigure} />} />
      <Route path="/configuration/ingestion-tasks" element={<IngestionTasksPage canWrite={canConfigure} />} />
      <Route path="/configuration/ingestion-tasks/:taskId" element={<IngestionTaskPage canWrite={canConfigure} />} />
      <Route path="/edges" element={<Pages.EdgesPage />} />
      <Route path="/edges/:edgeId" element={<Pages.EdgeDetailPage />} />
      <Route path="/platform-metrics" element={<Pages.MetricsPage />} />
      <Route path="/logs" element={<Pages.LogsPage />} />
      <Route path="/identity/users" element={<Pages.UsersPage />} />
      <Route path="/users" element={<Navigate to="/identity/users" replace />} />
      <Route path="*" element={<Pages.NotFoundPage />} />
    </Routes>
  );
}
