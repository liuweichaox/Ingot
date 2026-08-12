import { ArrowRightIcon, BeakerIcon, ChatBubbleLeftRightIcon, CheckBadgeIcon, ScaleIcon } from "@heroicons/react/24/outline";
import { Link } from "react-router";
import { Card, Page } from "../ui/components";

const steps = [
  ["1", "选择需要解释的运行", "从质量异常、参数偏离或最新完成运行开始，不从空白问题开始。"],
  ["2", "确认数据可信与可比", "检查运行边界、采样连续性和产品、设备、工装等同类条件。"],
  ["3", "比较差异并形成候选", "用同类历史运行识别首次偏离、支持证据、反证和混杂因素。"],
  ["4", "进入工程验证", "将仍需验证的候选转入研发项目，设计实验并固化经过验证的工艺窗口。"],
];

const tools = [
  { title: "运行对比", description: "适合已经知道哪次运行异常，需要系统匹配同类历史运行。", to: "/comparisons", action: "开始结构化对比", icon: ScaleIcon, primary: true },
  { title: "数据可信度", description: "先确认运行是否完整、采样是否连续，以及哪些证据可以正式使用。", to: "/data-quality", action: "检查分析准入", icon: CheckBadgeIcon },
  { title: "分析助手", description: "通过自然语言查询生产、质量和工艺记录，辅助定位下一步调查入口。", to: "/chat", action: "打开分析助手", icon: ChatBubbleLeftRightIcon },
  { title: "研发项目", description: "把待验证候选推进为假设、实验、影子评估和受控验证。", to: "/research-projects", action: "进入工程验证", icon: BeakerIcon },
];

export function AnalysisHubPage() {
  return (
    <Page title="工艺分析" description="从一次需要解释的生产运行出发，形成可追溯、可反驳、可验证的工程判断。">
      <section className="overflow-hidden rounded-2xl border border-blue-100 bg-gradient-to-br from-slate-950 via-slate-900 to-blue-950 p-6 text-white shadow-sm sm:p-8">
        <p className="text-sm font-semibold text-blue-200">证据驱动的工艺追因</p>
        <h2 className="mt-3 max-w-4xl text-2xl font-semibold tracking-tight sm:text-3xl">先确认哪次运行值得分析，再比较差异，最后决定如何验证。</h2>
        <p className="mt-4 max-w-3xl text-sm leading-7 text-slate-300">分析助手不会替代运行证据。Ingot 将生产条件、过程轨迹和质量结果放进同一上下文，帮助工程师把观察性候选推进到独立验证。</p>
        <div className="mt-6 flex flex-wrap gap-3">
          <Link to="/comparisons" className="inline-flex min-h-10 items-center gap-2 rounded-lg bg-white px-4 py-2 text-sm font-semibold text-slate-950 hover:bg-blue-50">开始运行对比<ArrowRightIcon className="size-4" /></Link>
          <Link to="/process-executions" className="inline-flex min-h-10 items-center rounded-lg border border-white/25 px-4 py-2 text-sm font-medium text-white hover:bg-white/10">查看生产运行</Link>
        </div>
      </section>

      <Card title="一次分析如何完成" description="每一步都有明确输入、判断和下一步，不需要先理解平台的数据结构。">
        <ol className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          {steps.map(([number, title, description]) => (
            <li key={number} className="rounded-xl border border-slate-200 bg-slate-50 p-4">
              <span className="grid size-7 place-items-center rounded-full bg-blue-600 text-xs font-semibold text-white">{number}</span>
              <p className="mt-3 font-semibold text-slate-950">{title}</p>
              <p className="mt-1 text-sm leading-6 text-slate-600">{description}</p>
            </li>
          ))}
        </ol>
      </Card>

      <div className="grid gap-4 md:grid-cols-2">
        {tools.map(item => {
          const Icon = item.icon;
          return (
            <Link key={item.to} to={item.to} className={`group rounded-2xl border bg-white p-5 shadow-sm transition hover:-translate-y-0.5 hover:shadow-md ${item.primary ? "border-blue-300 ring-1 ring-blue-100" : "border-slate-200"}`}>
              <span className={`grid size-10 place-items-center rounded-xl ${item.primary ? "bg-blue-600 text-white" : "bg-slate-100 text-slate-700"}`}><Icon className="size-5" /></span>
              <h2 className="mt-4 font-semibold text-slate-950">{item.title}</h2>
              <p className="mt-1 text-sm leading-6 text-slate-600">{item.description}</p>
              <p className="mt-4 inline-flex items-center gap-1 text-sm font-semibold text-blue-700">{item.action}<ArrowRightIcon className="size-4 transition group-hover:translate-x-0.5" /></p>
            </Link>
          );
        })}
      </div>
    </Page>
  );
}
