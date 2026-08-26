// 汇总分析入口，并在站点和执行上下文间提供可追踪导航。
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
    ["待分析运行", targetExecutions.length, "质量异常或数据不可用"],
    ["数据问题", dataIssueCount, "需要补齐或降级处理"],
    ...(canAccessResearch ? [["验证中项目", activeProjects.length, "仍需工程决策或实验"]] : []),
  ];

  return (
    <Page
      title="追因总览"
      actions={<Link to="/comparisons" className="inline-flex min-h-9 items-center rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700">新建运行对比</Link>}
    >
      <RequestError
        error={error}
        onRetry={() => Promise.all([
          executionsResponse.reload(),
          ...(canAccessResearch ? [projectsResponse.reload()] : []),
        ])}
      />

      <section className={`grid grid-cols-1 divide-y divide-slate-200 rounded-lg border border-slate-200 bg-white sm:divide-x sm:divide-y-0 ${canAccessResearch ? "sm:grid-cols-3" : "sm:grid-cols-2"}`} aria-label="追因任务摘要">
        {summaryItems.map(([label, value, hint]) => (
          <div key={label} className="px-4 py-3.5">
            <p className="text-[13px] font-medium text-slate-500">{label}</p>
            <div className="mt-1 flex items-baseline gap-2">
              <strong className="text-2xl font-semibold text-slate-950 tabular-nums">{value}</strong>
              <span className="text-[13px] text-slate-500">{hint}</span>
            </div>
          </div>
        ))}
      </section>

      <div className={`grid gap-5 ${canAccessResearch ? "xl:grid-cols-[minmax(0,1fr)_22rem]" : ""}`}>
        <Card title="待分析运行" actions={<Link className="text-sm font-medium text-blue-700" to="/process-executions">查看全部运行</Link>}>
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
                      <Link className="font-medium text-blue-700 hover:text-blue-900" to={`/comparisons?executionId=${encodeURIComponent(value)}`}>{value}</Link>
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
          <Card title="待验证项目" actions={<Link className="text-sm font-medium text-blue-700" to="/research-projects">查看全部项目</Link>}>
            {projectsResponse.loading && !projectsResponse.data ? (
              <p className="py-10 text-center text-sm text-slate-500">正在读取研发项目…</p>
            ) : activeProjects.length ? (
              <div className="divide-y divide-slate-200">
                {activeProjects.slice(0, 6).map(project => (
                  <Link key={project.projectId} to={`/research-projects/${encodeURIComponent(project.projectId)}`} className="flex items-center gap-3 py-3 first:pt-0 last:pb-0 hover:text-blue-800">
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-sm font-medium text-slate-900">{project.name}</p>
                      <p className="mt-0.5 truncate text-[13px] text-slate-500">{project.processName || project.processCode || "工艺未记录"}</p>
                    </div>
                    <StatusBadge value={project.status} label={statusLabels[project.status] || project.status} />
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
