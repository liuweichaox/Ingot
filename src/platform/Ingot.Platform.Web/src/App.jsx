import { Dialog, DialogBackdrop, DialogPanel, Menu, MenuButton, MenuItem, MenuItems } from "@headlessui/react";
import {
  BoltIcon,
  ChartBarIcon,
  ChatBubbleLeftRightIcon,
  CircleStackIcon,
  ClipboardDocumentCheckIcon,
  Cog6ToothIcon,
  MagnifyingGlassIcon,
  RectangleGroupIcon,
  Squares2X2Icon,
  UserCircleIcon,
  WrenchScrewdriverIcon,
  XMarkIcon,
} from "@heroicons/react/24/outline";
import { useMemo, useState } from "react";
import { Link, Navigate, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import * as Pages from "./pages";
import { cx } from "./ui/components";

const sections = [
  { id: "workbench", label: "工作台", icon: Squares2X2Icon, path: "/workbench", items: [] },
  { id: "chat", label: "AI 助手", icon: ChatBubbleLeftRightIcon, path: "/chat", items: [] },
  {
    id: "operations", label: "运行与追溯", icon: BoltIcon, path: "/cycles", items: [
      ["/cycles", "运行记录"], ["/events", "生产事件"], ["/production/changeover", "生产切换"],
      ["/production/tooling-installations", "工装装卸"],
    ],
  },
  {
    id: "quality", label: "质量管理", icon: ClipboardDocumentCheckIcon, path: "/inspections", items: [
      ["/inspections", "质量任务"], ["/quality-analysis", "质量分析"],
      ["/configuration/inspection-definitions", "检测定义"], ["/configuration/quality-plans", "质量方案"],
    ],
  },
  {
    id: "analysis", label: "分析中心", icon: ChartBarIcon, path: "/comparisons", items: [
      ["/comparisons", "历史对比"], ["/data-quality", "数据健康"], ["/process-improvement", "工艺改进"],
      ["/configuration/process-analysis-plans", "分析方案"],
    ],
  },
  {
    id: "data", label: "数据资产", icon: CircleStackIcon, path: "/explorer", items: [
      ["/explorer", "对象目录"], ["/configuration/process-data-models", "工艺数据模型"],
      ["/configuration/recipe-versions", "配方版本"], ["/configuration/acquisition-profiles", "采集任务"],
      ["/edges", "采集节点"],
    ],
  },
  {
    id: "tooling", label: "工装管理", icon: WrenchScrewdriverIcon, path: "/configuration/components", items: [
      ["/configuration/component-types", "组件类型"], ["/configuration/components", "组件台账"],
      ["/configuration/tooling-types", "工装类型"], ["/configuration/tooling-assemblies", "工装组合"],
    ],
  },
  {
    id: "administration", label: "系统管理", icon: Cog6ToothIcon, path: "/platform-metrics", items: [
      ["/platform-metrics", "平台指标"], ["/subscriptions", "事件订阅"], ["/logs", "运行日志"],
    ],
  },
];

const pageDetails = {
  "/workbench": ["工作台", "生产、质量与数据状态"],
  "/chat": ["Ingot Chat", "查询与分析已保存的生产数据"],
  "/explorer": ["对象目录", "从运行对象进入数据、上下文与关联关系"],
  "/cycles": ["运行记录", "查看生产周期及其数据、工艺与质量上下文"],
  "/events": ["生产事件", "查询、追溯并关联运行上下文"],
  "/production/changeover": ["生产切换", "让设备、产品、配方和已装工装对接下来的周期生效"],
  "/production/tooling-installations": ["工装装卸", "记录工装组合版本在设备上的装入与卸下区间"],
  "/inspections": ["质量任务", "处理视觉检查、人工质检与原图复核"],
  "/quality-analysis": ["质量分析", "按产品、配方、运行对象和分析范围查看质量结果"],
  "/comparisons": ["历史对比", "比较同类生产周期、运行段或时间窗口"],
  "/data-quality": ["数据健康", "检查运行对象的数据范围、采样连续性与周期完整性"],
  "/process-improvement": ["工艺改进", "管理模型、调查试验、现场知识与参数建议闭环"],
  "/configuration/process-analysis-plans": ["分析方案", "配置分析范围、对齐方式、质量分组和数据项"],
  "/configuration/process-data-models": ["工艺数据模型", "定义采集数据项、配方参数结构和工艺阶段"],
  "/configuration/recipe-versions": ["配方版本", "维护引用数据模型的完整配方有效值"],
  "/configuration/acquisition-profiles": ["采集任务", "管理数据源连接、采集对象、字段映射与发布版本"],
  "/configuration/inspection-definitions": ["检测定义", "定义要检测的特性、录入类型和判定规则"],
  "/configuration/quality-plans": ["质量方案", "配置产品适用的检测项目与复核规则"],
  "/configuration/component-types": ["组件类型", "配置组件台账的分类来源"],
  "/configuration/components": ["组件台账", "登记可更换、复用和追溯的物理组件"],
  "/configuration/tooling-types": ["工装类型", "配置装配位置及允许的组件类型"],
  "/configuration/tooling-assemblies": ["工装组合", "维护工装身份与不可变组件组合版本"],
  "/edges": ["采集节点", "查看现场采集节点及运行状态"],
  "/platform-metrics": ["平台指标", "查看平台与边缘节点运行指标"],
  "/subscriptions": ["事件订阅", "维护向外部系统投递的事件订阅"],
  "/logs": ["运行日志", "查询平台运行记录"],
};

export default function App() {
  const location = useLocation();
  const navigate = useNavigate();
  const [mobileOpen, setMobileOpen] = useState(false);
  const section = useMemo(
    () => sections.find(item => item.path === location.pathname || item.items.some(([path]) => path === location.pathname)) ?? sections[0],
    [location.pathname],
  );
  const page = pageDetails[location.pathname] ?? ["Ingot", "制造数据平台"];

  return (
    <div className="min-h-screen bg-slate-50 text-slate-900">
      <header className="fixed inset-x-0 top-0 z-50 flex h-16 items-stretch border-b border-slate-200 bg-white/95 shadow-sm backdrop-blur">
        <button className="flex w-55 shrink-0 items-center gap-3 border-r border-slate-100 px-5 text-left" onClick={() => navigate("/workbench")}>
          <span className="grid size-9 place-items-center rounded-xl bg-amber-50 ring-1 ring-amber-200">
            <img src="/ingot-mark.svg" alt="" className="size-7" />
          </span>
          <span className="hidden sm:grid">
            <strong className="text-base leading-5 text-slate-950">Ingot</strong>
            <small className="text-[10px] text-slate-500">制造数据平台</small>
          </span>
        </button>
        <nav className="flex min-w-0 flex-1 overflow-x-auto" aria-label="全局导航">
          {sections.map(item => {
            const Icon = item.icon;
            const active = item.id === section.id;
            return (
              <Link
                key={item.id}
                to={item.path}
                className={cx(
                  "relative flex shrink-0 items-center gap-2 px-3 text-xs font-medium transition md:px-4 md:text-sm",
                  active ? "bg-blue-50/70 text-blue-700 after:absolute after:inset-x-4 after:bottom-0 after:h-0.5 after:bg-blue-600" : "text-slate-600 hover:bg-slate-50 hover:text-slate-950",
                )}
              >
                <Icon className="size-4.5" />
                <span>{item.label}</span>
              </Link>
            );
          })}
        </nav>
        <button className="hidden items-center gap-2 border-l border-slate-100 px-4 text-sm text-slate-600 hover:bg-slate-50 md:flex" onClick={() => navigate("/explorer")}>
          <MagnifyingGlassIcon className="size-5" />搜索
        </button>
        <Menu as="div" className="relative flex border-l border-slate-100">
          <MenuButton className="grid w-14 place-items-center text-slate-600 hover:bg-slate-50" aria-label="用户菜单">
            <UserCircleIcon className="size-6" />
          </MenuButton>
          <MenuItems transition anchor="bottom end" className="z-100 mt-2 w-48 origin-top-right rounded-xl border border-slate-200 bg-white p-1 text-sm shadow-xl transition data-closed:scale-95 data-closed:opacity-0">
            <MenuItem><a href="/health" className="block rounded-lg px-3 py-2 text-slate-700 data-focus:bg-slate-100">服务健康状态</a></MenuItem>
            <MenuItem><a href="https://docs.ingotstack.com/zh" className="block rounded-lg px-3 py-2 text-slate-700 data-focus:bg-slate-100">产品文档</a></MenuItem>
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
                <Link key={path} to={path} className={cx("rounded-lg px-3 py-2.5 text-sm", path === location.pathname ? "bg-blue-50 font-medium text-blue-700" : "text-slate-600 hover:bg-slate-50 hover:text-slate-950")}>
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
              <Link key={path} to={path} onClick={() => setMobileOpen(false)} className={cx("rounded-lg px-3 py-3 text-sm", path === location.pathname ? "bg-blue-50 font-medium text-blue-700" : "text-slate-700 hover:bg-slate-50")}>
                {label}
              </Link>
            ))}
          </nav>
        </DialogPanel>
      </Dialog>
    </div>
  );
}

function AppRoutes() {
  return (
    <Routes>
      <Route path="/" element={<Navigate to="/workbench" replace />} />
      <Route path="/workbench" element={<Pages.WorkbenchPage />} />
      <Route path="/chat" element={<Pages.ChatPage />} />
      <Route path="/explorer" element={<Pages.ObjectExplorerPage />} />
      <Route path="/cycles" element={<Pages.CyclesPage />} />
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
      <Route path="/process-improvement" element={<Pages.ProcessImprovementPage />} />
      <Route path="/configuration/process-analysis-plans" element={<Pages.ProcessAnalysisPlansPage />} />
      <Route path="/profiles" element={<Navigate to="/configuration/process-data-models" replace />} />
      <Route path="/configuration/process-data-models" element={<Pages.ProcessDataModelsPage />} />
      <Route path="/configuration/recipe-versions" element={<Pages.RecipeVersionsPage />} />
      <Route path="/configuration/acquisition-profiles" element={<Pages.AcquisitionProfilesPage />} />
      <Route path="/edges" element={<Pages.EdgesPage />} />
      <Route path="/platform-metrics" element={<Pages.MetricsPage />} />
      <Route path="/subscriptions" element={<Pages.SubscriptionsPage />} />
      <Route path="/logs" element={<Pages.LogsPage />} />
      <Route path="*" element={<Pages.NotFoundPage />} />
    </Routes>
  );
}
