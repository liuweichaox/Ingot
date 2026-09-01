// 汇总分析入口，并在站点和执行上下文间提供可追踪导航。
import { ArrowRightIcon, CircleStackIcon, ShieldExclamationIcon } from "@heroicons/react/24/outline";
import { Link } from "react-router";
import { extractRows, useApi } from "../hooks/useApi";
import { Card, DataTable, EmptyState, Page, RequestError, StatusBadge } from "../ui/components";
import { formatTime } from "./shared";

const needsAnalysis = execution => {
  const quality = String(execution.qualityStatus || "").toLowerCase();
  const data = String(execution.processDataQuality?.status || "").toLowerCase();
  return ["fail", "failed", "inconclusive", "not_analyzable"].includes(quality)
    || ["degraded", "unavailable", "blocked", "forbidden"].includes(data)
    || execution.status === "failed";
};

export function AnalysisHubPage() {
  const executionsResponse = useApi("/api/v1/process-executions?status=completed&limit=50");
  const executions = extractRows(executionsResponse.data);
  const targetExecutions = executions.filter(needsAnalysis);
  const dataIssueCount = executions.filter(item =>
    ["degraded", "unavailable", "blocked", "forbidden"].includes(
      String(item.processDataQuality?.status || "").toLowerCase(),
    )).length;
  const error = executionsResponse.error;
  const summaryItems = [
    { label: "待分析运行", value: targetExecutions.length, hint: "质量异常或数据不可用", icon: CircleStackIcon, accent: "text-rose-700 bg-rose-50 ring-rose-200" },
    { label: "数据问题", value: dataIssueCount, hint: "需要补齐或降级处理", icon: ShieldExclamationIcon, accent: "text-amber-700 bg-amber-50 ring-amber-200" },
  ];

  return (
    <Page
      title="追因总览"
      description="先确认数据是否足以比较，再把质量偏差收敛为可复核的候选原因。"
      actions={<Link to="/comparisons" className="inline-flex min-h-10 items-center gap-2 rounded-lg border border-evidence-500 bg-evidence-500 px-4 py-2 text-sm font-semibold text-coal-950 shadow-sm transition hover:border-evidence-400 hover:bg-evidence-400">新建运行对比<ArrowRightIcon className="size-4" /></Link>}
    >
      <RequestError
        error={error}
        onRetry={executionsResponse.reload}
      />

      <section className="product-panel grid grid-cols-1 divide-y divide-slate-200 overflow-hidden rounded-xl sm:grid-cols-2 sm:divide-x sm:divide-y-0" aria-label="追因任务摘要">
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

      <div className="grid gap-6">
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
      </div>
    </Page>
  );
}
