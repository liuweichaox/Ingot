import { Dialog, DialogBackdrop, DialogPanel, Menu, MenuButton, MenuItem, MenuItems } from "@headlessui/react";
import {
  BoltIcon,
  BeakerIcon,
  CircleStackIcon,
  Cog6ToothIcon,
  MagnifyingGlassIcon,
  RectangleGroupIcon,
  UserCircleIcon,
  WrenchScrewdriverIcon,
  XMarkIcon,
} from "@heroicons/react/24/outline";
import { useEffect, useMemo, useRef, useState } from "react";
import { Link, Navigate, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import { getAuthToken, getJson, postJson, setAuthToken } from "./api/http";
import * as Pages from "./pages";
import { cx, ToastHost } from "./ui/components";

const sections = [
  {
    id: "workbench", label: "决策工作台", icon: BoltIcon, path: "/workbench", items: [
      ["/workbench", "运行概览"], ["/cycles", "运行记录"],
      ["/inspections", "质量任务"], ["/events", "生产事件"],
    ],
  },
  {
    id: "research", label: "洞察与优化", icon: BeakerIcon, path: "/research-projects", items: [
      ["/research-projects", "优化项目"], ["/comparisons", "周期对比与验证"],
      ["/quality-analysis", "质量洞察"], ["/chat", "AI 研发助手"],
    ],
  },
  {
    id: "context", label: "工业上下文", icon: CircleStackIcon, path: "/explorer", items: [
      ["/explorer", "工业对象"], ["/data-quality", "数据可信度"],
      ["/configuration/process-analysis-plans", "分析语义"], ["/configuration/process-data-models", "工艺数据模型"],
      ["/configuration/recipe-versions", "配方版本"], ["/configuration/inspection-definitions", "检测定义"],
      ["/configuration/quality-plans", "质量方案"],
    ],
  },
  {
    id: "implementation", label: "连接与实施", icon: WrenchScrewdriverIcon, path: "/edges", items: [
      ["/edges", "现场节点"], ["/configuration/acquisition-profiles", "数据连接"],
      ["/production/changeover", "生产上下文"], ["/production/tooling-installations", "工装装卸"],
      ["/configuration/component-types", "组件类型"], ["/configuration/components", "组件台账"],
      ["/configuration/tooling-types", "工装类型"], ["/configuration/tooling-assemblies", "工装组合"],
    ],
  },
  {
    id: "administration", label: "系统", icon: Cog6ToothIcon, path: "/platform-metrics", items: [
      ["/platform-metrics", "平台运行状态"], ["/users", "用户与权限"], ["/subscriptions", "事件订阅"], ["/logs", "运行日志"],
    ],
  },
];

const pageDetails = {
  "/research-projects": ["工艺优化工作台", "从问题、证据与实验推进到经过验证的工艺窗口"],
  "/workbench": ["工业决策工作台", "实时运行、质量、数据可信度与优化行动的统一入口"],
  "/chat": ["AI 研发助手", "结合实验、过程数据、机理和知识推进研发任务"],
  "/research-assets": ["研发资产", "查看项目可复用的数据集、模型、机理和知识"],
  "/explorer": ["工业对象", "以设备、工件和运行对象组织有业务语义的工业数据"],
  "/cycles": ["运行记录", "查看生产周期及其数据、工艺与质量上下文"],
  "/events": ["生产事件", "查询、追溯并关联运行上下文"],
  "/production/changeover": ["生产上下文", "让设备、产品、配方和已装工装对接下来的周期生效"],
  "/production/tooling-installations": ["工装装卸", "记录工装组合版本在设备上的装入与卸下区间"],
  "/inspections": ["质量任务", "处理视觉检查、人工质检与原图复核"],
  "/quality-analysis": ["质量洞察", "按产品、配方、运行对象和分析范围查看质量结果"],
  "/comparisons": ["周期对比与验证", "比较同类生产周期、运行段或时间窗口，生成待验证的候选原因"],
  "/data-quality": ["数据可信度", "检查运行对象的数据范围、采样连续性与周期完整性"],
  "/configuration/process-analysis-plans": ["分析语义", "配置分析范围、对齐方式、质量分组和数据项"],
  "/configuration/process-data-models": ["工艺数据模型", "定义采集数据项、配方参数结构和工艺阶段"],
  "/configuration/recipe-versions": ["配方版本", "维护引用数据模型的完整配方有效值"],
  "/configuration/acquisition-profiles": ["数据连接", "选择现场节点、设备和采集方式，让数据持续进入平台"],
  "/configuration/inspection-definitions": ["检测定义", "定义要检测的特性、录入类型和判定规则"],
  "/configuration/quality-plans": ["质量方案", "配置产品适用的检测项目与复核规则"],
  "/configuration/component-types": ["组件类型", "配置组件台账的分类来源"],
  "/configuration/components": ["组件台账", "登记可更换、复用和追溯的物理组件"],
  "/configuration/tooling-types": ["工装类型", "配置装配位置及允许的组件类型"],
  "/configuration/tooling-assemblies": ["工装组合", "维护工装身份与不可变组件组合版本"],
  "/edges": ["现场节点", "查看负责连接设备、仪器、系统并上报数据的现场节点"],
  "/platform-metrics": ["平台运行状态", "确认中心服务、现场节点和数据上行是否正常"],
  "/users": ["用户与权限", "管理本地账户、角色和停用状态"],
  "/subscriptions": ["事件订阅", "维护向外部系统投递的事件订阅"],
  "/logs": ["运行日志", "查询平台运行记录"],
};

const roleLabels = {
  "platform.admin": "平台管理员",
  "quality.inspector": "质量检验员",
  "quality.reviewer": "质量复核员",
  "process.engineer": "工艺工程师",
};

const globalSearchEntries = sections.flatMap(section => section.items.map(([path, label]) => ({
  path,
  label,
  section: section.label,
  description: pageDetails[path]?.[1] || "打开功能页面",
})));

export default function App() {
  const location = useLocation();
  const navigate = useNavigate();
  const [mobileOpen, setMobileOpen] = useState(false);
  const [globalSearchOpen, setGlobalSearchOpen] = useState(false);
  const [authState, setAuthState] = useState("checking");
  const [identity, setIdentity] = useState(null);

  useEffect(() => {
    let alive = true;
    async function loadIdentity() {
      try {
        const current = await getJson("/api/v1/auth/me");
        if (alive) {
          setIdentity(current);
          setAuthState("ready");
        }
      } catch {
        if (alive) {
          setIdentity(null);
          setAuthState("required");
        }
      }
    }
    function handleUnauthorized() {
      setAuthToken("");
      setIdentity(null);
      setAuthState("required");
    }
    window.addEventListener("ingot:unauthorized", handleUnauthorized);
    loadIdentity();
    return () => {
      alive = false;
      window.removeEventListener("ingot:unauthorized", handleUnauthorized);
    };
  }, []);

  useEffect(() => {
    function handleShortcut(event) {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        setGlobalSearchOpen(true);
      }
    }
    window.addEventListener("keydown", handleShortcut);
    return () => window.removeEventListener("keydown", handleShortcut);
  }, []);

  async function logout() {
    try {
      await postJson("/api/v1/auth/logout", {});
    } finally {
      setAuthToken("");
      setIdentity(null);
      setAuthState("required");
    }
  }

  const visibleSections = useMemo(
    () => sections.map(item => item.id === "administration" && !(identity?.roles || []).includes("platform.admin")
      ? { ...item, items: item.items.filter(([path]) => path !== "/users") }
      : item),
    [identity],
  );
  const section = useMemo(
    () => visibleSections.find(item => item.path === location.pathname || item.items.some(([path]) => location.pathname === path || location.pathname.startsWith(`${path}/`))) ?? visibleSections[0],
    [location.pathname, visibleSections],
  );
  const page = location.pathname.startsWith("/cycles/")
    ? ["周期详情", "查看单次生产运行的过程、质量和数据完整性"]
    : location.pathname.startsWith("/edges/")
      ? ["节点诊断", "查看现场节点的连接、采集、上行和最近日志"]
    : pageDetails[location.pathname] ?? ["Ingot", "AI 工艺研发系统"];

  if (authState === "checking") return <AuthenticationLoading />;
  if (authState === "required") {
    return <LoginPage onAuthenticated={current => {
      setIdentity(current);
      setAuthState("ready");
    }} />;
  }

  return (
    <div className="min-h-screen bg-slate-50 text-slate-900">
      <header className="fixed inset-x-0 top-0 z-50 flex h-16 items-stretch border-b border-slate-200 bg-white/95 shadow-sm backdrop-blur">
        <button className="flex w-16 shrink-0 items-center gap-3 border-r border-slate-100 px-3 text-left sm:w-55 sm:px-5" onClick={() => navigate("/workbench")}>
          <span className="grid size-9 place-items-center rounded-xl bg-amber-50 ring-1 ring-amber-200">
            <img src="/ingot-mark.svg" alt="" className="size-7" />
          </span>
          <span className="hidden sm:grid">
            <strong className="text-base leading-5 text-slate-950">Ingot</strong>
            <small className="text-[10px] text-slate-500">工业数据与工艺决策平台</small>
          </span>
        </button>
        <Menu as="div" className="relative flex min-w-0 flex-1 xl:hidden">
          <MenuButton className="flex min-w-0 flex-1 items-center gap-2 px-3 text-sm font-medium text-slate-700 hover:bg-slate-50 sm:px-4" aria-label="打开全局模块导航">
            <section.icon className="size-5 shrink-0 text-blue-600" />
            <span className="truncate">{section.label}</span>
          </MenuButton>
          <MenuItems transition anchor="bottom start" className="z-100 mt-2 w-64 origin-top-left rounded-xl border border-slate-200 bg-white p-2 text-sm shadow-xl transition data-closed:scale-95 data-closed:opacity-0">
            {visibleSections.map(item => {
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
          {visibleSections.map(item => {
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
        <button className="flex items-center gap-2 border-l border-slate-100 px-3 text-sm text-slate-600 hover:bg-slate-50 sm:px-4" onClick={() => setGlobalSearchOpen(true)} aria-label="打开全局搜索" aria-keyshortcuts="Control+K Meta+K">
          <MagnifyingGlassIcon className="size-5" /><span className="hidden lg:inline">全局搜索</span><kbd className="hidden rounded border border-slate-200 bg-slate-50 px-1.5 py-0.5 text-[10px] text-slate-400 2xl:inline">⌘K</kbd>
        </button>
        <Menu as="div" className="relative flex border-l border-slate-100">
          <MenuButton className="grid w-14 place-items-center text-slate-600 hover:bg-slate-50" aria-label="用户菜单">
            <UserCircleIcon className="size-6" />
          </MenuButton>
          <MenuItems transition anchor="bottom end" className="z-100 mt-2 w-48 origin-top-right rounded-xl border border-slate-200 bg-white p-1 text-sm shadow-xl transition data-closed:scale-95 data-closed:opacity-0">
            <div className="border-b border-slate-100 px-3 py-2">
              <p className="truncate font-medium text-slate-900">{identity?.username || "当前用户"}</p>
              <p className="mt-0.5 truncate text-xs text-slate-500">{(identity?.roles || []).map(role => roleLabels[role] || role).join("、") || "已登录"}</p>
            </div>
            <MenuItem><Link to="/platform-metrics" className="block rounded-lg px-3 py-2 text-slate-700 data-focus:bg-slate-100">平台运行状态</Link></MenuItem>
            {(identity?.roles || []).includes("platform.admin") && <MenuItem><Link to="/users" className="block rounded-lg px-3 py-2 text-slate-700 data-focus:bg-slate-100">用户与权限</Link></MenuItem>}
            <MenuItem><a href="https://docs.ingotstack.com/zh" className="block rounded-lg px-3 py-2 text-slate-700 data-focus:bg-slate-100">产品文档</a></MenuItem>
            {getAuthToken() && <MenuItem><button type="button" className="block w-full rounded-lg px-3 py-2 text-left text-rose-700 data-focus:bg-rose-50" onClick={logout}>退出登录</button></MenuItem>}
          </MenuItems>
        </Menu>
      </header>

      <div className="pt-16">
        {section.items.length > 0 && (
          <aside className="fixed inset-y-16 left-0 z-30 hidden w-55 border-r border-slate-200 bg-white lg:block">
            <div className="flex h-16 items-center gap-2 border-b border-slate-100 px-5">
              <section.icon className="size-5 text-blue-600" />
              <strong className="text-sm">{section.label}</strong>
            </div>
            <nav className="grid gap-1 p-3" aria-label={`${section.label}导航`}>
              {section.items.map(([path, label]) => (
                <Link key={path} to={path} className={cx("rounded-lg px-3 py-2.5 text-sm", (path === location.pathname || location.pathname.startsWith(`${path}/`)) ? "bg-blue-50 font-medium text-blue-700" : "text-slate-600 hover:bg-slate-50 hover:text-slate-950")}>
                  {label}
                </Link>
              ))}
            </nav>
          </aside>
        )}

        <div className={cx(section.items.length > 0 && "lg:ml-55")}>
          <div className="sticky top-16 z-20 flex min-h-16 items-center gap-3 border-b border-slate-200 bg-white/90 px-4 backdrop-blur sm:px-6">
            {section.items.length > 0 && (
              <button className="grid size-9 place-items-center rounded-lg text-slate-600 hover:bg-slate-100 lg:hidden" onClick={() => setMobileOpen(true)} aria-label="打开模块导航">
                <RectangleGroupIcon className="size-5" />
              </button>
            )}
            <div>
              <p className="font-semibold text-slate-950">{page[0]}</p>
              <p className="text-xs text-slate-500">{page[1]}</p>
            </div>
          </div>
          <main className="mx-auto w-full max-w-[1600px] p-4 sm:p-6">
            <AppRoutes />
          </main>
        </div>
      </div>

      <Dialog open={mobileOpen} onClose={setMobileOpen} className="relative z-80 lg:hidden">
        <DialogBackdrop className="fixed inset-0 bg-slate-950/30" />
        <DialogPanel className="fixed inset-y-0 left-0 w-72 bg-white shadow-2xl">
          <div className="flex h-16 items-center justify-between border-b border-slate-200 px-4">
            <strong>{section.label}</strong>
            <button className="grid size-9 place-items-center rounded-lg hover:bg-slate-100" onClick={() => setMobileOpen(false)} aria-label="关闭模块导航">
              <XMarkIcon className="size-5" />
            </button>
          </div>
          <nav className="grid gap-1 p-3">
            {section.items.map(([path, label]) => (
              <Link key={path} to={path} onClick={() => setMobileOpen(false)} className={cx("rounded-lg px-3 py-3 text-sm", (path === location.pathname || location.pathname.startsWith(`${path}/`)) ? "bg-blue-50 font-medium text-blue-700" : "text-slate-700 hover:bg-slate-50")}>
                {label}
              </Link>
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
            <p className="text-sm font-semibold text-slate-950">全局搜索</p>
            <p className="mt-1 text-xs text-slate-500">查找研发、追因、生产、质量、接入和系统功能。</p>
            <input
              ref={inputRef}
              value={query}
              onChange={event => setQuery(event.target.value)}
              placeholder="例如：研发项目、历史对比、设备采集、运行记录"
              className="mt-4 min-h-11 w-full rounded-xl border border-slate-300 bg-slate-50 px-4 text-sm text-slate-900 outline-none transition placeholder:text-slate-400 focus:border-blue-500 focus:bg-white focus:ring-3 focus:ring-blue-100"
            />
          </div>
          <div className="max-h-[55vh] overflow-y-auto p-2">
            {results.length ? results.map(item => (
              <button key={item.path} type="button" onClick={() => select(item.path)} className="flex w-full items-start gap-3 rounded-xl px-3 py-3 text-left hover:bg-blue-50 focus:bg-blue-50 focus:outline-none">
                <span className="mt-0.5 rounded-md bg-slate-100 px-2 py-1 text-[11px] font-medium text-slate-600">{item.section}</span>
                <span className="min-w-0"><span className="block text-sm font-medium text-slate-900">{item.label}</span><span className="mt-0.5 block text-xs leading-5 text-slate-500">{item.description}</span></span>
              </button>
            )) : <div className="px-4 py-10 text-center text-sm text-slate-500">没有匹配的功能。请换一个关键词。</div>}
          </div>
          <div className="flex items-center justify-between border-t border-slate-100 px-5 py-3 text-xs text-slate-500"><span>搜索用于定位产品功能，不会把你直接带到某个数据资产。</span><span>Esc 关闭</span></div>
        </DialogPanel>
      </div>
    </Dialog>
  );
}

function AppRoutes() {
  return (
    <Routes>
      <Route path="/" element={<Navigate to="/workbench" replace />} />
      <Route path="/research-projects" element={<Pages.ResearchProjectsPage />} />
      <Route path="/workbench" element={<Pages.WorkbenchPage />} />
      <Route path="/chat" element={<Pages.ChatPage />} />
      <Route path="/research-assets" element={<Pages.ResearchAssetsPage />} />
      <Route path="/explorer" element={<Pages.ObjectExplorerPage />} />
      <Route path="/cycles" element={<Pages.CyclesPage />} />
      <Route path="/cycles/:correlationId" element={<Pages.CycleDetailPage />} />
      <Route path="/events" element={<Pages.EventsPage />} />
      <Route path="/production/changeover" element={<Pages.ProductionSetupPage section="context" />} />
      <Route path="/production/tooling-installations" element={<Pages.ProductionSetupPage section="installation" />} />
      <Route path="/configuration/component-types" element={<Pages.ProductionSetupPage section="componentType" />} />
      <Route path="/configuration/components" element={<Pages.ProductionSetupPage section="component" />} />
      <Route path="/configuration/tooling-types" element={<Pages.ProductionSetupPage section="type" />} />
      <Route path="/configuration/tooling-assemblies" element={<Pages.ProductionSetupPage section="assembly" />} />
      <Route path="/production-setup" element={<Navigate to="/production/changeover" replace />} />
      <Route path="/inspections" element={<Pages.InspectionsPage />} />
      <Route path="/quality-analysis" element={<Pages.QualityAnalysisPage />} />
      <Route path="/quality-plans" element={<Navigate to="/configuration/quality-plans" replace />} />
      <Route path="/configuration/inspection-definitions" element={<Pages.InspectionDefinitionsPage />} />
      <Route path="/configuration/quality-plans" element={<Pages.QualityPlansPage />} />
      <Route path="/comparisons" element={<Pages.CycleComparisonPage />} />
      <Route path="/data-quality" element={<Pages.DataQualityPage />} />
      <Route path="/process-improvement" element={<Navigate to="/research-projects" replace />} />
      <Route path="/configuration/process-analysis-plans" element={<Pages.ProcessAnalysisPlansPage />} />
      <Route path="/profiles" element={<Navigate to="/configuration/process-data-models" replace />} />
      <Route path="/configuration/process-data-models" element={<Pages.ProcessDataModelsPage />} />
      <Route path="/configuration/recipe-versions" element={<Pages.RecipeVersionsPage />} />
      <Route path="/configuration/acquisition-profiles" element={<Pages.AcquisitionProfilesPage />} />
      <Route path="/edges" element={<Pages.EdgesPage />} />
      <Route path="/edges/:edgeId" element={<Pages.EdgeDetailPage />} />
      <Route path="/platform-metrics" element={<Pages.MetricsPage />} />
      <Route path="/users" element={<Pages.UsersPage />} />
      <Route path="/subscriptions" element={<Pages.SubscriptionsPage />} />
      <Route path="/logs" element={<Pages.LogsPage />} />
      <Route path="*" element={<Pages.NotFoundPage />} />
    </Routes>
  );
}

function AuthenticationLoading() {
  return (
    <div className="grid min-h-screen place-items-center bg-slate-50">
      <div className="text-center">
        <img src="/ingot-mark.svg" alt="" className="mx-auto size-12" />
        <p className="mt-4 text-sm text-slate-500">正在确认登录状态…</p>
      </div>
    </div>
  );
}

function LoginPage({ onAuthenticated }) {
  const [form, setForm] = useState({ username: "", password: "" });
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function submit(event) {
    event.preventDefault();
    setBusy(true);
    setError("");
    try {
      const session = await postJson("/api/v1/auth/login", form);
      setAuthToken(session.token);
      onAuthenticated({
        userId: session.userId,
        username: session.displayName || session.username,
        roles: session.roles || [],
      });
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="grid min-h-screen bg-slate-100 lg:grid-cols-[minmax(0,1fr)_minmax(420px,0.72fr)]">
      <section className="hidden items-end bg-slate-950 p-12 text-white lg:flex">
        <div className="max-w-xl">
          <div className="mb-8 flex items-center gap-3">
            <span className="grid size-12 place-items-center rounded-2xl bg-amber-50"><img src="/ingot-mark.svg" alt="" className="size-9" /></span>
            <div><strong className="text-2xl">Ingot</strong><p className="text-sm text-slate-400">AI 工艺研发系统</p></div>
          </div>
          <h1 className="text-4xl font-semibold leading-tight">让每一轮实验都更接近经过验证的工艺窗口。</h1>
          <p className="mt-5 leading-7 text-slate-300">融合实验数据、实时过程数据、物理机理和专家知识，帮助工艺工程师更快完成工艺研发。</p>
        </div>
      </section>
      <main className="grid place-items-center p-6 sm:p-10">
        <form className="w-full max-w-md rounded-2xl border border-slate-200 bg-white p-7 shadow-xl shadow-slate-200/60 sm:p-9" onSubmit={submit}>
          <div className="mb-7 lg:hidden">
            <img src="/ingot-mark.svg" alt="" className="size-11" />
          </div>
          <h1 className="text-2xl font-semibold text-slate-950">登录平台</h1>
          <p className="mt-2 text-sm leading-6 text-slate-500">使用管理员分配的本地账户进入系统。</p>
          {error && <div role="alert" className="mt-5 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">{error}</div>}
          <label className="mt-6 block text-sm font-medium text-slate-700">
            用户名
            <input autoComplete="username" required className="mt-2 min-h-11 w-full rounded-xl border border-slate-300 px-3 outline-none focus:border-blue-500 focus:ring-2 focus:ring-blue-100" value={form.username} onChange={event => setForm({ ...form, username: event.target.value })} />
          </label>
          <label className="mt-4 block text-sm font-medium text-slate-700">
            密码
            <input type="password" autoComplete="current-password" required className="mt-2 min-h-11 w-full rounded-xl border border-slate-300 px-3 outline-none focus:border-blue-500 focus:ring-2 focus:ring-blue-100" value={form.password} onChange={event => setForm({ ...form, password: event.target.value })} />
          </label>
          <button type="submit" disabled={busy || !form.username.trim() || !form.password} className="mt-6 min-h-11 w-full rounded-xl bg-blue-600 px-4 font-medium text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50">
            {busy ? "正在登录…" : "登录"}
          </button>
        </form>
      </main>
    </div>
  );
}
