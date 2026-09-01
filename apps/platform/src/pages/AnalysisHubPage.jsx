// 汇总分析入口，并在站点和执行上下文间提供可追踪导航。
import { ArrowRightIcon, BeakerIcon, CircleStackIcon, ShieldExclamationIcon } from "@heroicons/react/24/outline";
import { Link } from "react-router";
import { extractRows, useApi } from "../hooks/useApi";
import { statusLabels } from "../research/researchProjectModel";
import { Card, DataTable, EmptyState, Page, RequestError, StatusBadge } from "../ui/components";
import { formatTime } from "./shared";

const needsAnalysis = execution => {
  const quality = String(execution.qualityStatus || "").toLowerCase();
  const data = String(execution.processDataQuality?.status || "").toLowerCase();
  return ["fail", "failed", "inconclusive", "not_analyzable"].includes(quality)
    || ["degraded", "unavailable", "blocked", "forbidden"].includes(data)
    || execution.status === "failed";
};

export function AnalysisHubPage({ identity }) {
  const canAccessResearch = (identity?.roles || []).some(role =>
    role === "process.engineer" || role === "platform.admin");
  const executionsResponse = useApi("/api/v1/process-executions?status=completed&limit=50");
  const projectsResponse = useApi("/api/v1/research-projects?limit=100", { enabled: canAccessResearch });
  const executions = extractRows(executionsResponse.data);
  const projects = extractRows(projectsResponse.data);
  const targetExecutions = executions.filter(needsAnalysis);
  const dataIssueCount = executions.filter(item =>
    ["degraded", "unavailable", "blocked", "forbidden"].includes(
      String(item.processDataQuality?.status || "").toLowerCase(),
    )).length;
  const activeProjects = projects.filter(item =>
    ["active", "investigating", "trialing", "validating", "proposed"].includes(item.status));
  const error = executionsResponse.error || (canAccessResearch ? projectsResponse.error : "");
  const summaryItems = [
    { label: "待分析运行", value: targetExecutions.length, hint: "质量异常或数据不可用", icon: CircleStackIcon, accent: "text-rose-700 bg-rose-50 ring-rose-200" },
    { label: "数据问题", value: dataIssueCount, hint: "需要补齐或降级处理", icon: ShieldExclamationIcon, accent: "text-amber-700 bg-amber-50 ring-amber-200" },
    ...(canAccessResearch ? [{ label: "进行中项目", value: activeProjects.length, hint: "等待工程师决定或运行结果", icon: BeakerIcon, accent: "text-trajectory-700 bg-trajectory-50 ring-trajectory-100" }] : []),
  ];

  return (
    <Page
      title="追因总览"
      description="先确认数据是否足以比较，再把质量偏差推进为候选原因和验证任务。"
      actions={<Link to="/comparisons" className="inline-flex min-h-10 items-center gap-2 rounded-lg border border-evidence-500 bg-evidence-500 px-4 py-2 text-sm font-semibold text-coal-950 shadow-sm transition hover:border-evidence-400 hover:bg-evidence-400">新建运行对比<ArrowRightIcon className="size-4" /></Link>}
    >
      <RequestError
        error={error}
        onRetry={() => Promise.all([
          executionsResponse.reload(),
          ...(canAccessResearch ? [projectsResponse.reload()] : []),
        ])}
      />

      <section className={`product-panel grid grid-cols-1 divide-y divide-slate-200 overflow-hidden rounded-xl sm:divide-x sm:divide-y-0 ${canAccessResearch ? "sm:grid-cols-3" : "sm:grid-cols-2"}`} aria-label="追因任务摘要">
        {summaryItems.map(({ label, value, hint, icon: Icon, accent }) => (
          <div key={label} className="group px-5 py-5">
            <div className="flex items-center justify-between gap-3">
              <p className="data-label">{label}</p>
              <span className={`grid size-9 place-items-center rounded-lg ring-1 ring-inset ${accent}`}><Icon className="size-4.5" /></span>
            </div>
            <div className="mt-4 flex items-end gap-3">
              <strong className="data-value text-3xl font-semibold text-slate-950">{value}</strong>
              <span className="pb-0.5 text-xs leading-5 text-slate-500">{hint}</span>
            </div>
          </div>
        ))}
      </section>

      <div className={`grid gap-6 ${canAccessResearch ? "xl:grid-cols-[minmax(0,1fr)_20rem]" : ""}`}>
        <Card title="待分析运行" description="这里仅汇总质量异常、数据降级或分析准入受阻的已完成运行。" actions={<Link className="text-sm font-semibold text-trajectory-700" to="/process-executions">查看全部运行</Link>}>
          {executionsResponse.loading && !executionsResponse.data ? (
            <p className="py-10 text-center text-sm text-slate-500">正在读取生产运行…</p>
          ) : targetExecutions.length ? (
            <DataTable
              rows={targetExecutions.slice(0, 10)}
              keyField="executionId"
              columns={[
                {
                  key: "executionId",
                  label: "运行",
                  render: (value, row) => (
                    <div>
                      <Link className="font-semibold text-trajectory-700 hover:text-trajectory-600" to={`/comparisons?executionId=${encodeURIComponent(value)}`}>{value}</Link>
                      <p className="mt-0.5 text-[13px] text-slate-500">{row.productCode || "产品未记录"}</p>
                    </div>
                  ),
                },
                { key: "equipmentId", label: "设备" },
                { key: "qualityStatus", label: "质量", render: value => <StatusBadge value={value} /> },
                { key: "startedAt", label: "开始", render: formatTime },
              ]}
            />
          ) : <EmptyState title="没有待分析运行" description="当前已完成运行未发现质量异常或数据准入问题。" />}
        </Card>

        {canAccessResearch && (
          <Card className="xl:self-start" title="进行中项目" description="建议需要经过工程师决定、实际生产运行和质量结果闭环，才能沉淀为可靠结论。" actions={<Link className="text-sm font-semibold text-trajectory-700" to="/research-projects">查看全部项目</Link>}>
            {projectsResponse.loading && !projectsResponse.data ? (
              <p className="py-10 text-center text-sm text-slate-500">正在读取研发项目…</p>
            ) : activeProjects.length ? (
              <div className="grid gap-2">
                {activeProjects.slice(0, 6).map(project => (
                  <Link key={project.projectId} to={`/research-projects/${encodeURIComponent(project.projectId)}`} className="group flex items-center gap-3 rounded-lg border border-slate-200 bg-slate-50/60 px-3.5 py-3 transition hover:border-slate-300 hover:bg-white">
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-sm font-medium text-slate-900">{project.name}</p>
                      <p className="mt-0.5 truncate text-[13px] text-slate-500">{project.processName || project.processCode || "工艺未记录"}</p>
                    </div>
                    <StatusBadge value={project.status} label={statusLabels[project.status] || project.status} />
                    <ArrowRightIcon className="size-4 shrink-0 text-slate-300 transition group-hover:translate-x-0.5 group-hover:text-trajectory-700" />
                  </Link>
                ))}
              </div>
            ) : <EmptyState title="没有待验证项目" description="需要验证的候选原因会显示在这里。" />}
          </Card>
        )}
      </div>
    </Page>
  );
}
