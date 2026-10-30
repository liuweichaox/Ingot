// 展示 Edge 注册、心跳和采集运行状态，不推断未上报健康信息。
import { useState } from "react";
import { Link, useParams } from "react-router";
import { extractRows, useApi } from "../hooks/useApi";
import { useSiteScope } from "../hooks/useSiteScope";
import { Alert, Badge, Card, DataTable, LinkButton, Metric, Page, RequestError, StatusBadge, WorkflowGuide } from "../ui/components";
import { formatTime, formatInteger, formatDuration, formatBytes, edgeStatus, edgeConnectionStatus, countOnlineEdges, acquisitionProtocolLabels, objectTypeLabel, LoadingCard, SiteScopeField } from "./shared";

function edgeReportedSnapshotAges(lastValidSnapshotAt, reportedAt, statusReceivedAt, nowMs) {
  if (!lastValidSnapshotAt || !reportedAt || !statusReceivedAt || nowMs == null) return null;
  const snapshotMs = new Date(lastValidSnapshotAt).getTime();
  const reportedMs = new Date(reportedAt).getTime();
  const receivedMs = new Date(statusReceivedAt).getTime();
  const now = Number(nowMs);
  const ageAtReportMs = reportedMs - snapshotMs;
  const timeSinceStatusMs = now - receivedMs;
  if (![snapshotMs, reportedMs, receivedMs, now, ageAtReportMs, timeSinceStatusMs].every(Number.isFinite) ||
      ageAtReportMs < 0 || timeSinceStatusMs < 0) return null;
  return { ageAtReportMs, timeSinceStatusMs, estimatedAgeMs: ageAtReportMs + timeSinceStatusMs };
}

function edgeReportedSnapshotAgeMs(lastValidSnapshotAt, reportedAt, statusReceivedAt, nowMs) {
  return edgeReportedSnapshotAges(lastValidSnapshotAt, reportedAt, statusReceivedAt, nowMs)?.estimatedAgeMs ?? null;
}

function isSnapshotTimeInvalidAtReport(lastValidSnapshotAt, reportedAt) {
  const snapshotMs = new Date(lastValidSnapshotAt).getTime();
  const reportedMs = new Date(reportedAt).getTime();
  return !Number.isFinite(snapshotMs) || (Number.isFinite(reportedMs) && snapshotMs > reportedMs);
}

function snapshotSilenceMonitor(task, runtime, reportedAt, statusReceivedAt, nowMs) {
  if (!task)
    return { status: "unknown", label: "未知", tone: "warning", detail: "没有匹配到采集配置版本，无法读取静默时限", thresholdMs: 0 };
  const thresholdMs = Number(task?.execution?.maximumValidSnapshotSilenceMs ?? 0);
  const ages = edgeReportedSnapshotAges(runtime?.lastValidSnapshotAt, reportedAt, statusReceivedAt, nowMs);
  const ageMs = ages?.estimatedAgeMs ?? null;
  const invalidTime = runtime?.lastValidSnapshotAt && isSnapshotTimeInvalidAtReport(runtime.lastValidSnapshotAt, reportedAt);
  const ageText = ageMs == null
    ? invalidTime ? "快照时间无效或晚于节点状态报告" : runtime?.lastValidSnapshotAt ? "最近快照时效无法核实" : "尚无有效快照"
    : `最后一次状态报告估算最近快照 ${formatDuration(ageMs)} 前`;
  if (!Number.isInteger(thresholdMs) || thresholdMs < 0 || thresholdMs > 86_400_000)
    return { status: "unknown", label: "未知", tone: "warning", detail: `${ageText} · 配置的静默时限无效`, thresholdMs: 0 };
  if (thresholdMs === 0)
    return { status: "unmonitored", label: "未监控", tone: "neutral", detail: `${ageText} · 未配置静默时限`, thresholdMs };
  if (ages == null)
    return { status: "unknown", label: "未知", tone: "warning", detail: `${ageText} · 静默时限 ${formatDuration(thresholdMs)}`, thresholdMs };
  if (ages.ageAtReportMs > thresholdMs)
    return { status: "timeout", label: "超时", tone: "danger", detail: `节点报告已确认静默 ${formatDuration(ages.ageAtReportMs)} · 时限 ${formatDuration(thresholdMs)} · 检出精度约 10 秒`, thresholdMs };
  if (ages.timeSinceStatusMs > 30_000)
    return { status: "unknown", label: "未知", tone: "warning", detail: "状态报告已超过 30 秒，最近报告未确认超时；报告和 Platform 评估周期约 10 秒，等待新报告核实", thresholdMs };
  if (ageMs > thresholdMs)
    return { status: "unknown", label: "待核实", tone: "warning", detail: `按最后报告估算已静默 ${formatDuration(ageMs)}，可能超过时限；报告和 Platform 评估周期约 10 秒，等待下次报告确认`, thresholdMs };
  return { status: "within-limit", label: "未超时", tone: "success", detail: `${ageText} · 时限 ${formatDuration(thresholdMs)}`, thresholdMs };
}

function freshnessTransitionState(state) {
  const labels = {
    unknown: "未知",
    unmonitored: "未监控",
    "within-limit": "未超时",
    timeout: "超时",
    removed: "已移除",
  };
  return labels[state] || state || "未知";
}

function freshnessTransitionTone(state) {
  if (state === "timeout") return "danger";
  if (state === "within-limit") return "success";
  if (state === "unknown") return "warning";
  return "neutral";
}

function freshnessThresholdLabel(value) {
  if (value == null) return "未知";
  return Number(value) === 0 ? "未监控" : formatDuration(Number(value));
}

function freshnessTransitionThresholdLabel(event) {
  const current = freshnessThresholdLabel(event.thresholdMs);
  if (event.previousState && event.previousThresholdMs !== event.thresholdMs)
    return `${freshnessThresholdLabel(event.previousThresholdMs)} → ${current}`;
  return current;
}

function timeoutDurationLabel(event, events, latestTransitionsByTask, historyMayBeLimited) {
  const durationText = value => value != null && Number.isFinite(Number(value))
    ? formatDuration(Number(value))
    : null;
  if (event.state === "timeout") {
    const eventDetectedAtMs = new Date(event.detectedAt).getTime();
    const exit = events
      .filter(candidate => candidate.configurationKey === event.configurationKey &&
        candidate.previousState === "timeout" && candidate.state !== "timeout" &&
        new Date(candidate.detectedAt).getTime() > eventDetectedAtMs)
      .sort((left, right) => new Date(left.detectedAt).getTime() - new Date(right.detectedAt).getTime())[0];
    if (exit?.state === "within-limit") {
      if (exit.previousThresholdMs !== exit.thresholdMs)
        return "时限变更，采集恢复未确认";
      const duration = durationText(exit.durationMs);
      return duration ? `持续 ${duration}` : "恢复时长无法判断";
    }
    if (exit) return "监控中断，恢复未确认";
    const latest = latestTransitionsByTask.get(event.configurationKey);
    if (latest?.state === "timeout" && latest.detectedAt === event.detectedAt)
      return event.previousThresholdMs !== event.thresholdMs && event.previousState
        ? "仍在超时；时限已调整"
        : "仍在超时";
    return historyMayBeLimited
      ? "超出最近 100 条或缺少恢复记录，无法判断"
      : "缺少可核实的恢复记录";
  }
  if (event.previousState === "timeout" && event.previousThresholdMs !== event.thresholdMs)
    return "时限变更，采集恢复未确认";
  if (event.previousState === "timeout" && event.state !== "within-limit")
    return "监控中断，恢复未确认";
  if (event.previousState === "timeout") {
    const duration = durationText(event.durationMs);
    return duration ? `结束超时 ${duration}` : "恢复时长无法判断";
  }
  return "—";
}

export function EdgesPage() {
  const siteScope = useSiteScope({ edgeInterval: 10000 });
  const { siteId, siteChoices, selectSite, invalidRequestedSite, edgeResponse } = siteScope;
  const { data, loading, error, reload } = edgeResponse;
  const rows = extractRows(data).filter(row => !siteId || row.siteId === siteId);
  const online = countOnlineEdges(rows);
  return (
    <Page
      title="现场节点"
      actions={<LinkButton to={`/configuration/ingestion-tasks${siteId ? `?siteId=${encodeURIComponent(siteId)}` : ""}`}>配置采集</LinkButton>}
    >
      <div className="mb-5 max-w-md"><SiteScopeField siteId={siteId} siteChoices={siteChoices} onChange={selectSite} loading={siteScope.loading} label="现场范围（未选择时显示全部可访问节点）" /></div>
      {invalidRequestedSite && <Alert tone="warning">URL 中的站点不在当前账号可访问范围内。</Alert>}
      {!siteId && !siteScope.loading && !data && <Alert tone="warning">{siteChoices.length ? "请选择站点后查看现场节点。" : "当前账号没有可访问站点。"}</Alert>}
      <RequestError error={siteScope.error || error} title="现场节点暂不可用" onRetry={siteScope.error ? siteScope.reload : reload} />
      {data && <div className="grid gap-4 sm:grid-cols-3">
        <Metric label="现场节点" value={rows.length} hint="已登记" />
        <Metric label="当前在线" value={online} hint="30 秒内有心跳" />
        <Metric label="需要处理" value={rows.filter(row => edgeStatus(row) !== "online").length} hint="离线或运行异常" />
      </div>}
      {loading && !data ? <LoadingCard /> : data ? (
        <Card title="节点状态" description="节点在线后即可承载一个或多个数据源采集任务。">
          <DataTable
            rows={rows}
            keyField="edgeId"
            columns={[
              { key: "edgeId", label: "节点" },
              { key: "hostname", label: "名称" },
              { key: "_status", label: "状态", render: (_value, row) => <StatusBadge value={edgeStatus(row)} /> },
              { key: "lastSeen", label: "最后心跳", render: formatTime },
              { key: "lastError", label: "最近问题", render: value => value || "无" },
              { key: "version", label: "版本" },
              { key: "_action", label: "操作", render: (_value, row) => <Link className="font-medium text-blue-600 hover:text-blue-700" to={`/edges/${encodeURIComponent(row.edgeId)}${row.siteId ? `?siteId=${encodeURIComponent(row.siteId)}` : ""}`}>查看诊断</Link> },
            ]}
          />
        </Card>
      ) : null}
    </Page>
  );
}
export function EdgeDetailPage() {
  const [showSystemLogs, setShowSystemLogs] = useState(false);
  const { edgeId = "" } = useParams();
  const encodedId = encodeURIComponent(edgeId);
  const siteScope = useSiteScope({ edgeInterval: 10000 });
  const { siteId, siteChoices, selectSite, invalidRequestedSite, edgeResponse } = siteScope;
  const edges = edgeResponse;
  const acquisition = useApi(`/api/edges/${encodedId}/acquisition/status`, { interval: 5000 });
  const statusIntervals = useApi(`/api/edges/${encodedId}/status-intervals?limit=24`, { interval: 30000 });
  const taskFreshnessHistory = useApi(`/api/edges/${encodedId}/task-freshness-transitions?limit=100`, { interval: 30000 });
  const logAudience = showSystemLogs ? "" : "&audience=operator";
  const logs = useApi(`/api/edges/${encodedId}/logs?page=1&pageSize=50${logAudience}`, { interval: 10000 });
  const ingestionTasks = useApi("/api/v1/ingestion-tasks", { interval: 10000 });
  const edge = extractRows(edges.data).find(row => row.edgeId === edgeId);
  const scopedSiteId = edge?.siteId || siteId;
  const scopedSiteQuery = scopedSiteId ? `?siteId=${encodeURIComponent(scopedSiteId)}` : "";
  const error = siteScope.error || edges.error || acquisition.error || statusIntervals.error || logs.error || ingestionTasks.error;
  if (!edge && !siteScope.loading) {
    return (
      <Page title={edgeId || "现场节点"}>
        {siteChoices.length > 1 && <div className="mb-5 max-w-md"><SiteScopeField siteId={siteId} siteChoices={siteChoices} onChange={selectSite} loading={siteScope.loading} /></div>}
        <RequestError error={error} title="现场节点诊断暂不可用" onRetry={() => Promise.all([siteScope.reload(), acquisition.reload(), statusIntervals.reload(), taskFreshnessHistory.reload(), logs.reload(), ingestionTasks.reload()])} />
        <Alert tone="warning">{siteId ? "所选站点下找不到该节点，或当前身份没有访问权限。" : siteChoices.length ? "请先选择节点所属站点，再查看诊断信息。" : "当前身份没有可访问的现场站点。"}</Alert>
        <Link className="inline-flex min-h-9 items-center rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50" to={`/edges${scopedSiteQuery}`}>返回现场节点</Link>
      </Page>
    );
  }
  const tasks = acquisition.data?.tasks || [];
  const taskFreshnessEvents = extractRows(taskFreshnessHistory.data);
  const latestTransitionsByTask = new Map();
  for (const event of taskFreshnessEvents) {
    const existing = latestTransitionsByTask.get(event.configurationKey);
    if (!existing || new Date(event.detectedAt).getTime() > new Date(existing.detectedAt).getTime())
      latestTransitionsByTask.set(event.configurationKey, event);
  }
  const deploymentStates = acquisition.data?.deployments || [];
  const edgeTasks = extractRows(ingestionTasks.data).filter(task => task.edgeId === edgeId);
  const tasksByKey = new Map(edgeTasks.map(task => [`${task.taskId}@${task.version}`, task]));
  const taskRows = tasks.map(task => ({ ...task, ingestionTask: tasksByKey.get(task.configurationKey) }));
  const deploymentRows = deploymentStates.map(deployment => ({
    ...deployment,
    ingestionTask: tasksByKey.get(`${deployment.taskId}@${deployment.desiredVersion}`),
  }));
  const runningTasks = tasks.filter(task => task.state === "running").length;
  const convergedDeployments = deploymentStates.filter(deployment =>
    deployment.state === "applied" &&
    deployment.desiredVersion === deployment.appliedVersion &&
    deployment.desiredConfigurationHash === deployment.appliedConfigurationHash).length;
  const publishedTasks = edgeTasks.filter(task => task.status === "published");
  const serverObservedAtMs = acquisition.data?.platformObservedAt ? new Date(acquisition.data.platformObservedAt).getTime() : null;
  const nowMs = Number.isFinite(serverObservedAtMs) ? serverObservedAtMs : null;
  const runtimeReportedAt = acquisition.data?.reportedAt;
  const acquisitionStatusReceivedAt = acquisition.data?.platformReceivedAt;
  const snapshotAgeFor = timestamp => edgeReportedSnapshotAgeMs(timestamp, runtimeReportedAt, acquisitionStatusReceivedAt, nowMs);
  const snapshotTimes = tasks.map(task => task.lastValidSnapshotAt).filter(Boolean)
    .map(value => ({ value, timeMs: new Date(value).getTime(), ageMs: snapshotAgeFor(value) }));
  const validSnapshotTimes = snapshotTimes.filter(item => item.ageMs != null);
  const latestSnapshot = validSnapshotTimes.sort((left, right) => left.ageMs - right.ageMs)[0];
  const latestValidSnapshotAt = latestSnapshot?.value || null;
  const latestSnapshotAgeMs = latestSnapshot?.ageMs ?? null;
  const invalidSnapshotTimeReported = snapshotTimes.some(item => isSnapshotTimeInvalidAtReport(item.value, runtimeReportedAt));
  const snapshotAgeReferenceUnavailable = snapshotTimes.some(item => Number.isFinite(item.timeMs) &&
    item.ageMs == null && !isSnapshotTimeInvalidAtReport(item.value, runtimeReportedAt));
  const publishedRuntimeStates = publishedTasks.map(task =>
    tasks.find(runtime => runtime.configurationKey === `${task.taskId}@${task.version}`));
  const publishedSnapshotMonitors = publishedTasks.map((task, index) => snapshotSilenceMonitor(
    task, publishedRuntimeStates[index], runtimeReportedAt, acquisitionStatusReceivedAt, nowMs));
  const monitoredSnapshotCount = publishedSnapshotMonitors.filter(item => item.thresholdMs > 0).length;
  const timedOutSnapshotCount = publishedSnapshotMonitors.filter(item => item.status === "timeout").length;
  const unknownSnapshotCount = publishedSnapshotMonitors.filter(item => item.status === "unknown").length;
  const publishedTasksWithoutRuntimeCount = publishedRuntimeStates.filter(runtime => !runtime).length;
  const publishedTasksNotRunningCount = publishedRuntimeStates.filter(runtime => runtime && runtime.state !== "running").length;
  const publishedTasksWithoutSnapshotCount = publishedRuntimeStates.filter(runtime => !runtime?.lastValidSnapshotAt).length;
  const publishedTasksWithInvalidSnapshotTimeCount = publishedRuntimeStates.filter(runtime => {
    if (!runtime?.lastValidSnapshotAt) return false;
    return isSnapshotTimeInvalidAtReport(runtime.lastValidSnapshotAt, runtimeReportedAt);
  }).length;
  const publishedTasksWithUnverifiableSnapshotTimeCount = publishedRuntimeStates.filter(runtime =>
    Boolean(runtime?.lastValidSnapshotAt) && !isSnapshotTimeInvalidAtReport(runtime.lastValidSnapshotAt, runtimeReportedAt) &&
    snapshotAgeFor(runtime.lastValidSnapshotAt) == null).length;
  const processSignalCount = publishedTasks.reduce((total, task) => total + (task.valueMappings?.length || 0), 0);
  const controlParameterMappingCount = publishedTasks.reduce((total, task) => total + (task.processSpecification?.parameterMappings?.length || 0), 0);
  const lifecycleTaskCount = publishedTasks.filter(task => task.lifecycle).length;
  const allTaskDefinitionsResolved = tasks.length > 0 && taskRows.every(task => task.ingestionTask);
  const delivery = edge?.delivery;
  const deliveryObservedAtMs = edge?.serverObservedAt ? new Date(edge.serverObservedAt).getTime() : null;
  const deliveryNowMs = Number.isFinite(deliveryObservedAtMs) ? deliveryObservedAtMs : null;
  const lastShipmentTimeMs = delivery?.lastSuccessfulShipmentAt ? new Date(delivery.lastSuccessfulShipmentAt).getTime() : null;
  const lastShipmentAgeMs = deliveryNowMs != null && lastShipmentTimeMs != null && Number.isFinite(lastShipmentTimeMs) && lastShipmentTimeMs <= deliveryNowMs
    ? deliveryNowMs - lastShipmentTimeMs
    : null;
  const shipmentRateValue = Number(delivery?.shipmentRatePerSecond);
  const shipmentRateFresh = lastShipmentAgeMs != null && lastShipmentAgeMs <= 30_000
    && Number(delivery?.consecutiveFailures || 0) === 0 && delivery?.state !== "blocked"
    && Number.isFinite(shipmentRateValue) && shipmentRateValue > 0;
  const backlogObservedAt = delivery?.backlogObservedAt || null;
  const backlogObservedAgeMs = edge?.backlogStatisticsAgeMs != null && Number.isFinite(Number(edge.backlogStatisticsAgeMs))
    ? Number(edge.backlogStatisticsAgeMs)
    : null;
  const backlogStatisticsFresh = edge?.backlogStatisticsFresh === true;
  const oldestPendingTimeMs = delivery?.oldestPendingEventAt ? new Date(delivery.oldestPendingEventAt).getTime() : null;
  const oldestPendingAgeMs = deliveryNowMs != null && oldestPendingTimeMs != null && Number.isFinite(oldestPendingTimeMs) && oldestPendingTimeMs <= deliveryNowMs
    ? deliveryNowMs - oldestPendingTimeMs
    : null;
  const lastKnownBacklogValue = delivery?.backlogObservedAt ? delivery.pendingEventCount : null;
  const lastKnownBacklog = lastKnownBacklogValue == null || !Number.isFinite(Number(lastKnownBacklogValue)) ? null : Number(lastKnownBacklogValue);
  const outboxBacklog = backlogStatisticsFresh ? lastKnownBacklog : null;
  const backlogCapacityRaw = delivery?.backlogCapacityUsedPercent;
  const backlogCapacityValue = backlogCapacityRaw == null ? null : Number(backlogCapacityRaw);
  const backlogCapacityUsedPercent = backlogStatisticsFresh && backlogCapacityValue != null && Number.isFinite(backlogCapacityValue)
    ? backlogCapacityValue
    : null;
  const backlogNearCapacity = backlogCapacityUsedPercent != null && backlogCapacityUsedPercent >= 80;
  const backlogEvidenceHint = !delivery
    ? "等待节点上报"
    : backlogStatisticsFresh
      ? `最近核实于 ${formatDuration(backlogObservedAgeMs)} 前`
      : edge?.backlogObservationInvalid
        ? "Edge 上报的队列观察间隔无效；当前队列未知"
      : backlogObservedAt && backlogObservedAgeMs != null
        ? `上次核实于 ${formatDuration(backlogObservedAgeMs)} 前；当前队列未知`
        : backlogObservedAt
          ? "统计时间无效；当前队列未知"
          : "尚无成功的队列统计；当前队列未知";
  const shippedValue = delivery?.eventsShipped;
  const shipped = !delivery?.lastSuccessfulShipmentAt || shippedValue == null || !Number.isFinite(Number(shippedValue)) ? null : Number(shippedValue);
  const staleSnapshotRejections = acquisition.data == null ? null : Number(acquisition.data.staleSnapshotRejectionCount || 0);
  const recentLogs = extractRows(logs.data);
  const recentDeliveryEvidence = backlogStatisticsFresh && outboxBacklog === 0
    && Number(delivery?.consecutiveFailures || 0) === 0 && delivery?.state !== "blocked";
  const deliveryReady = runningTasks > 0 && processSignalCount > 0 && controlParameterMappingCount > 0 && lifecycleTaskCount > 0
    && publishedTasksWithoutRuntimeCount === 0 && publishedTasksNotRunningCount === 0
    && publishedTasksWithoutSnapshotCount === 0 && publishedTasksWithInvalidSnapshotTimeCount === 0
    && publishedTasksWithUnverifiableSnapshotTimeCount === 0 && unknownSnapshotCount === 0
    && timedOutSnapshotCount === 0 && recentDeliveryEvidence;
  const displayedDeliveryState = delivery?.state === "blocked" || delivery?.state === "degraded" || backlogStatisticsFresh
    ? delivery.state
    : "unknown";
  const scopedAnalysisQuery = new URLSearchParams();
  if (edge?.siteId) scopedAnalysisQuery.set("siteId", edge.siteId);
  scopedAnalysisQuery.set("edgeId", edgeId);
  const dataQualityHref = `/data-quality?${scopedAnalysisQuery.toString()}`;
  const processExecutionsHref = `/process-executions?${scopedAnalysisQuery.toString()}&attention=1`;

  return (
    <Page
      title={edge?.hostname || edgeId || "数据源节点"}
      actions={(
        <>
          <Link className="inline-flex min-h-9 items-center rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50" to={`/edges${scopedSiteQuery}`}>返回现场节点</Link>
          <LinkButton to={`/configuration/ingestion-tasks${scopedSiteQuery}`}>配置采集</LinkButton>
        </>
      )}
    >
      {siteChoices.length > 1 && <div className="mb-5 max-w-md"><SiteScopeField siteId={siteId} siteChoices={siteChoices} onChange={selectSite} loading={siteScope.loading} /></div>}
      {invalidRequestedSite && <Alert tone="warning">URL 中的站点不在当前账号可访问范围内。</Alert>}
      {!edge && !siteScope.loading && siteChoices.length > 1 && <Alert tone="warning">请选择该节点所属站点后查看诊断。</Alert>}
      <RequestError
        error={error}
        title="部分诊断信息暂不可用"
        onRetry={() => Promise.all([siteScope.reload(), acquisition.reload(), statusIntervals.reload(), taskFreshnessHistory.reload(), logs.reload(), ingestionTasks.reload()])}
      />
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-6">
        <Metric label="设备连接" value={<StatusBadge value={edgeConnectionStatus(edge)} />} hint={edge?.lastSeen ? `最后心跳 ${formatTime(edge.lastSeen)} · ${edge.serverObservedAt ? `Platform ${formatTime(edge.serverObservedAt)} 校验` : "按本地时间判断"}` : "尚未收到心跳"} />
        <Metric label="配置收敛" value={<StatusBadge value={acquisition.data?.state || "unknown"} />} hint={`${convergedDeployments} 个已应用 / ${deploymentStates.length} 个期望配置`} />
        <Metric label="数据上行" value={<StatusBadge value={displayedDeliveryState} />} hint={delivery ? `待补传 ${outboxBacklog == null ? "未知" : formatInteger(outboxBacklog)} · ${backlogEvidenceHint} · 最近成功 ${lastShipmentAgeMs == null ? delivery.lastSuccessfulShipmentAt ? "时间无效" : "暂无" : `${formatDuration(lastShipmentAgeMs)} 前`} · ACK ${formatInteger(delivery.lastAcknowledgedSequence)}` : "等待节点主动上报"} />
        <Metric label="最近有效采样" value={!acquisition.data ? "等待上报" : latestValidSnapshotAt ? formatTime(latestValidSnapshotAt) : invalidSnapshotTimeReported ? "时间无效" : snapshotAgeReferenceUnavailable ? "时效未知" : "尚无记录"} hint={latestSnapshotAgeMs == null ? invalidSnapshotTimeReported ? "采样时间无效或晚于节点状态报告" : snapshotAgeReferenceUnavailable ? "缺少 Platform 配对的接收与观测时间，无法计算快照静默时长" : "尚无成功采样记录" : `距最近有效快照 ${formatDuration(latestSnapshotAgeMs)} · 按节点报告与 Platform 接收时间校正`} />
        <Metric label="有效快照静默监控" value={monitoredSnapshotCount ? `${timedOutSnapshotCount}/${monitoredSnapshotCount} 超时` : publishedTasks.length ? "未配置" : "无发布任务"} hint={monitoredSnapshotCount ? `未知 ${unknownSnapshotCount} 项 · 按任务设置时限；不代表端到端上行延迟` : "为任务配置静默时限后，可在此判断是否超过预期"} />
        <Metric label="工艺建模" value={controlParameterMappingCount > 0 ? "配方版本已映射" : "待映射"} hint={`${processSignalCount} 条过程信号 · ${controlParameterMappingCount} 个控制参数`} />
      </div>
      {(edge?.lastError || acquisition.data?.lastError || outboxBacklog > 0 || (delivery && outboxBacklog == null) || backlogNearCapacity || delivery?.state === "blocked" || Number(delivery?.consecutiveFailures || 0) > 0) ? (
        <Alert tone="warning" title="节点需要关注">
          <ul className="list-disc space-y-1 pl-5">
            {edge?.lastError && <li>{edge.lastError}</li>}
            {acquisition.data?.lastError && <li>{acquisition.data.lastError}</li>}
            {outboxBacklog > 0 && <li>最近一次有效队列统计确认有 {formatInteger(outboxBacklog)} 条事件等待平台确认。</li>}
            {delivery && outboxBacklog == null && <li>{lastKnownBacklog == null ? "当前没有有效的待补传统计。" : `当前待补传数量未知；上次已核实 ${formatInteger(lastKnownBacklog)} 条。`}{backlogEvidenceHint}</li>}
            {backlogNearCapacity && <li>本地待上行队列已使用 {backlogCapacityUsedPercent.toFixed(1)}% 容量；请处理网络或平台确认问题，避免触及队列上限。</li>}
            {delivery?.state === "blocked" && delivery.lastError && <li>{delivery.lastError}</li>}
            {Number(delivery?.consecutiveFailures || 0) > 0 && delivery?.state !== "blocked" && <li>最近连续上行失败 {formatInteger(delivery.consecutiveFailures)} 次；核对节点日志与平台确认状态。</li>}
          </ul>
        </Alert>
      ) : !deliveryReady ? (
        <Alert tone="warning" title="数据源尚未具备工艺闭环条件">
          <ul className="list-disc space-y-1 pl-5">
            {runningTasks === 0 && <li>尚无运行中的采集任务，请先发布采集配置。</li>}
            {edgeConnectionStatus(edge) !== "online" && <li>现场节点当前未确认在线；节点离线时，历史采集和上行信息不能代表当前状态。</li>}
            {!latestValidSnapshotAt && !invalidSnapshotTimeReported && <li>当前没有已记录的有效快照时间，尚不能确认是否曾产生可用采样。</li>}
            {invalidSnapshotTimeReported && <li>有采样时间晚于当前时间或无法解析，相关时间不能用于时效判断。</li>}
            {latestSnapshotAgeMs != null && <li>最近一次有效采样约 {formatDuration(latestSnapshotAgeMs)} 前；系统未提供该数据源的预期采样时限，当前只展示时间事实。</li>}
            {publishedTasksWithoutRuntimeCount > 0 && <li>{publishedTasksWithoutRuntimeCount} 个已发布任务没有匹配到当前节点运行状态。</li>}
            {publishedTasksNotRunningCount > 0 && <li>{publishedTasksNotRunningCount} 个已发布任务当前不处于运行状态。</li>}
            {publishedTasksWithoutSnapshotCount > 0 && <li>{publishedTasksWithoutSnapshotCount} 个已发布任务尚无有效采样记录。</li>}
            {publishedTasksWithInvalidSnapshotTimeCount > 0 && <li>{publishedTasksWithInvalidSnapshotTimeCount} 个已发布任务的采样时间无效。</li>}
            {publishedTasksWithUnverifiableSnapshotTimeCount > 0 && <li>{publishedTasksWithUnverifiableSnapshotTimeCount} 个已发布任务的采样静默时长无法按节点报告和 Platform 接收时间核实。</li>}
            {delivery && !backlogStatisticsFresh && <li>待补传队列统计无法确认当前值；{backlogEvidenceHint}。</li>}
            {delivery?.state !== "synchronized" && <li>上行状态为“{delivery?.state || "未知"}”；状态标签不能替代带时间的积压统计和失败记录。</li>}
            {processSignalCount === 0 && <li>尚未映射过程信号，无法形成可分析的过程曲线。</li>}
            {controlParameterMappingCount === 0 && <li>尚未回读实际控制参数，无法区分真实执行条件。</li>}
            {lifecycleTaskCount === 0 && <li>尚未映射过程执行边界，连续数据无法自动归属到一次运行。</li>}
          </ul>
        </Alert>
      ) : <Alert tone="success" title="当前队列检查已核实无待补传">现场节点在线，最近一次成功读取的队列统计显示 0 条待补传。最近采样时间已单独列出；系统没有该数据源的预期采样时限，因此此结果不代表采样持续正常，也不证明数据已关联到具体运行。</Alert>}
      <WorkflowGuide
        title="从设备数据到工艺证据"
        description="确认设备连接、采集上行、工艺映射和运行关联。"
        compact
        steps={[
          { title: "连接数据源", description: edgeConnectionStatus(edge) === "online" ? "现场节点在 Platform 的心跳窗口内。" : "等待节点恢复或确认心跳状态。", state: edgeConnectionStatus(edge) === "online" ? "done" : "current" },
          { title: "采集并上行", description: runningTasks > 0 ? `${runningTasks} 个任务正在采集，${latestSnapshotAgeMs == null ? "最近采样时间未知。" : `最近有效采样约 ${formatDuration(latestSnapshotAgeMs)} 前。`}${outboxBacklog == null ? `待补传数量未知（${backlogEvidenceHint}）。` : outboxBacklog > 0 ? `${formatInteger(outboxBacklog)} 条事件等待平台确认。` : `队列于 ${formatTime(backlogObservedAt)} 核实为空。`}` : "尚无运行中的采集任务。", state: recentDeliveryEvidence ? "done" : "current" },
          { title: "映射工艺语义", description: `${processSignalCount} 条过程信号、${controlParameterMappingCount} 个控制参数${lifecycleTaskCount > 0 ? "，已配置过程执行边界。" : "；尚未配置过程执行边界。"}`, state: processSignalCount > 0 && controlParameterMappingCount > 0 && lifecycleTaskCount > 0 ? "done" : "current" },
          { title: "验证闭环证据", description: deliveryReady ? "采集端条件已具备；请在运行记录与质量任务中确认实际关联，再进入追因。" : "补齐当前步骤后，再用运行记录与质量任务验证证据是否完整。", state: deliveryReady ? "current" : "upcoming" },
        ]}
      />
      <Card title="采集状态对业务分析的影响" description="节点诊断提供汇总证据；具体运行是否被排除，以数据质量与运行记录中的业务记录为准。">
        <ul className="list-disc space-y-2 pl-5 text-sm leading-6 text-slate-600">
          {outboxBacklog > 0
            ? <li>最近一次有效队列统计确认有 {formatInteger(outboxBacklog)} 条事件尚未收到平台确认；相关运行和曲线可能延迟进入分析。</li>
            : outboxBacklog == null
              ? <li>当前无法确认待补传数量；{backlogEvidenceHint}。历史发送记录不能替代当前队列统计。</li>
              : <li>最近一次有效队列统计显示 0 条待补传；这表示该时点队列为空，不代表每个运行都完整或可进入分析。</li>}
          {staleSnapshotRejections > 0 && <li>已有 {formatInteger(staleSnapshotRejections)} 次陈旧快照被拒绝；对应字段不会作为有效过程样本。</li>}
          {lifecycleTaskCount === 0
            ? <li>尚无已发布任务配置运行边界；连续样本无法据此自动切分为单次运行，需先补齐配置或核对人工关联路径。</li>
            : <li>已发布任务配置了运行边界；仍需确认实际运行与质量记录关联后，才能进入比较和配方校正。</li>}
        </ul>
        <div className="mt-3 flex flex-wrap gap-4 text-sm">
          <Link className="font-medium text-blue-700 hover:text-blue-800" to={dataQualityHref}>查看基线条件缺口与运行明细</Link>
          <Link className="font-medium text-blue-700 hover:text-blue-800" to={processExecutionsHref}>查看此节点需处理的运行</Link>
        </div>
      </Card>
      <Card
        title="数据源交付情况"
        description="查看已发布数据源的过程信号、控制参数和运行边界映射。"
        actions={<Link className="text-sm font-medium text-blue-600 hover:text-blue-700" to="/configuration/ingestion-tasks">查看采集配置</Link>}
      >
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <Metric label="已发布数据源" value={publishedTasks.length} hint={allTaskDefinitionsResolved ? "运行任务已关联配置版本" : tasks.length ? "有运行任务尚未匹配配置版本" : "尚未加载运行任务"} />
          <Metric label="过程信号映射" value={processSignalCount} hint="用于形成过程曲线和特征" />
          <Metric label="控制参数回读" value={controlParameterMappingCount} hint={controlParameterMappingCount ? "用于区分实际执行条件" : "追因与优化需要实际控制参数回读"} />
          <Metric label="过程执行边界映射" value={lifecycleTaskCount} hint={lifecycleTaskCount ? "可生成离散运行过程执行" : "连续数据尚不能自动形成过程执行"} />
        </div>
        <p className="mt-5 rounded-xl bg-slate-50 px-4 py-3 text-sm leading-6 text-slate-600">
          {deliveryReady
            ? "当前节点运行、采集配置与队列统计具备检查条件。采样时间只表示最近一次有效样本；仍需按运行核对样本、配方回读与质量结果是否完整关联。"
            : "补齐运行任务、过程信号、实际控制参数和运行边界后，即可形成可分析的运行证据。"}
        </p>
      </Card>
      <Card title="采集与上行状态区间" description="连续相同状态自动合并；队列最大值仅统计带独立观察时间的记录，复用同一统计值的多次心跳会去重。没有有效队列统计的区间显示未知，不把默认 0 当作已核实为空。原始心跳在 Platform 保存七天。">
        <DataTable
          rows={extractRows(statusIntervals.data)}
          getRowKey={row => `${row.startedAt}:${row.endedAt}`}
          columns={[
            { key: "startedAt", label: "持续区间", render: (_value, row) => <div><p>{formatTime(row.startedAt)} – {formatTime(row.endedAt)}</p><p className="mt-0.5 text-xs text-slate-500">持续 {formatDuration(Math.max(0, new Date(row.endedAt) - new Date(row.startedAt)))} · {formatInteger(row.sampleCount)} 次心跳</p></div> },
            { key: "acquisitionState", label: "采集", render: value => <StatusBadge value={value || "unknown"} /> },
            { key: "endingValidSnapshotCount", label: "新增快照", render: (_value, row) => formatInteger(Math.max(0, Number(row.endingValidSnapshotCount) - Number(row.startingValidSnapshotCount))) },
            { key: "endingEmittedEventCount", label: "新增事件", render: (_value, row) => formatInteger(Math.max(0, Number(row.endingEmittedEventCount) - Number(row.startingEmittedEventCount))) },
            { key: "deliveryState", label: "上行", render: value => <StatusBadge value={value || "unknown"} /> },
            {
              key: "maximumPendingEventCount",
              label: "最高已核验积压",
              render: (value, row) => (
                <div>
                  <p>{value == null ? "未知" : formatInteger(value)}</p>
                  <p className="mt-0.5 text-xs text-slate-500">
                    {formatInteger(row.distinctBacklogObservationCount)} 次独立统计 · {formatInteger(row.backlogObservationHeartbeatCount)} / {formatInteger(row.sampleCount)} 次心跳含统计
                    {row.lastBacklogObservedAt ? ` · 最近 ${formatTime(row.lastBacklogObservedAt)}` : ""}
                  </p>
                </div>
              ),
            },
            { key: "_error", label: "问题", render: (_value, row) => row.acquisitionError || row.deliveryError || "无" },
          ]}
        />
      </Card>
      <Card title="任务采样时效历史" description="仅记录有效快照静默状态或静默时限发生变化的任务；超时起止时刻按 Edge 同源时间估算，精度受约 10 秒状态报告和 10 秒 Platform 评估周期限制。迟到报告可能把已确认超时回溯到较早时刻，所以估算生效时间可能早于 Platform 检出时间或先前的未知状态。时限调整和任务重新出现按 Platform 收到新报告的时间记为生效，不回溯到新策略可能开始生效的时刻；调整时限不能单独证明采集恢复。最多显示最近 100 条；若列表缺少恢复事件，时长可能超出当前范围而无法判断。">
        <RequestError error={taskFreshnessHistory.error} title="任务时效历史暂不可用" onRetry={taskFreshnessHistory.reload} />
        {taskFreshnessHistory.loading && !taskFreshnessHistory.data ? (
          <p className="py-5 text-sm text-slate-500" role="status">正在读取任务时效历史…</p>
        ) : taskFreshnessHistory.error && taskFreshnessEvents.length === 0 ? (
          <p className="py-5 text-sm text-slate-500">请求失败，当前无法判断是否有任务时效变化记录。</p>
        ) : taskFreshnessEvents.length === 0 ? (
          <p className="py-5 text-sm text-slate-500">
            暂无任务时效变化记录。历史从该功能启用后开始积累；空列表不代表节点过去从未超时。
          </p>
        ) : (
          <DataTable
            rows={taskFreshnessEvents}
            getRowKey={row => `${row.configurationKey}:${row.detectedAt}`}
            columns={[
              { key: "configurationKey", label: "采集任务", render: value => {
                const task = tasksByKey.get(value);
                return <div><p className="font-medium text-slate-900">{task?.name || value}</p>{task && <p className="text-xs text-slate-500">{objectTypeLabel(task.subjectType)} · {task.subjectId}</p>}</div>;
              } },
              { key: "state", label: "状态变化", render: (value, row) => (
                <div className="flex flex-wrap items-center gap-1.5">
                  <Badge tone={freshnessTransitionTone(row.previousState)}>{row.previousState ? freshnessTransitionState(row.previousState) : "开始监控"}</Badge>
                  <span className="text-slate-400">→</span>
                  <Badge tone={freshnessTransitionTone(value)}>{freshnessTransitionState(value)}</Badge>
                </div>
              ) },
              { key: "thresholdMs", label: "静默时限", render: (_value, row) => freshnessTransitionThresholdLabel(row) },
              { key: "effectiveAt", label: "估算生效时间", render: value => value ? formatTime(value) : "无法估算" },
              { key: "detectedAt", label: "Platform 检出时间", render: formatTime },
              { key: "durationMs", label: "超时持续", render: (_value, row) => timeoutDurationLabel(
                row, taskFreshnessEvents, latestTransitionsByTask, taskFreshnessEvents.length >= 100) },
              { key: "lastValidSnapshotAt", label: "最近有效快照", render: value => value ? formatTime(value) : "无记录" },
            ]}
          />
        )}
      </Card>
      <Card title="上送恢复基线" description="查看积压容量、上送速率和最近恢复情况。">
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <Metric label="当前积压" value={outboxBacklog == null ? "未知" : formatInteger(outboxBacklog)} hint={outboxBacklog == null ? `${backlogEvidenceHint}${lastKnownBacklog == null ? "" : ` · 上次核实 ${formatInteger(lastKnownBacklog)} 条`}` : `${backlogEvidenceHint} · 未收到平台确认的事件`} />
          <Metric label="最老积压" value={outboxBacklog == null ? "未知" : outboxBacklog === 0 ? "无待补传" : delivery?.oldestPendingEventAt ? formatTime(delivery.oldestPendingEventAt) : "未知"} hint={outboxBacklog > 0 && oldestPendingAgeMs != null ? `等待确认约 ${formatDuration(oldestPendingAgeMs)} · ${backlogCapacityUsedPercent == null ? "容量未知" : `容量使用 ${backlogCapacityUsedPercent.toFixed(1)}%`}` : backlogEvidenceHint} />
          <Metric label="本地存储" value={backlogStatisticsFresh ? formatBytes(delivery?.localStorageBytes) : "未知"} hint={delivery?.backlogCapacityRows ? `硬上限 ${formatInteger(delivery.backlogCapacityRows)} 条 · ${backlogEvidenceHint}` : backlogEvidenceHint} />
          <Metric label="最近上送速率" value={shipmentRateFresh ? `${shipmentRateValue.toFixed(1)} 条/s` : "未知"} hint={!shipmentRateFresh ? "速率需要近期成功上行且没有连续失败才有效" : delivery?.estimatedDrainSeconds == null || outboxBacklog == null ? "缺少当前积压，无法估算清空时间" : `按当前已核实积压预计 ${formatDuration(Number(delivery.estimatedDrainSeconds) * 1000)} 清空`} />
          <Metric label="累计已确认" value={shipped == null ? "未知" : formatInteger(shipped)} hint={`最后 ACK ${formatInteger(delivery?.lastAcknowledgedSequence)}`} />
          <Metric label="恢复次数" value={formatInteger(delivery?.recoveryCount)} hint={`${formatInteger(delivery?.consecutiveFailures)} 次连续失败`} />
          <Metric label="最近成功上行" value={!delivery ? "等待上报" : delivery.lastSuccessfulShipmentAt ? formatTime(delivery.lastSuccessfulShipmentAt) : "尚无记录"} hint={`${lastShipmentAgeMs == null ? delivery?.lastSuccessfulShipmentAt ? "上行时间无效或晚于当前时间" : "尚未确认上行" : `约 ${formatDuration(lastShipmentAgeMs)} 前`} · ${delivery?.lastFailureAt ? `最近失败 ${formatTime(delivery.lastFailureAt)}` : "尚未记录上行失败"}`} />
          <Metric label="最近恢复耗时" value={delivery?.lastRecoveryDurationMs == null ? "—" : formatDuration(delivery.lastRecoveryDurationMs)} hint={delivery?.lastSuccessfulShipmentAt ? `成功上行于 ${formatTime(delivery.lastSuccessfulShipmentAt)}` : "尚无恢复记录"} />
          <Metric label="陈旧快照拒绝" value={staleSnapshotRejections == null ? "未知" : formatInteger(staleSnapshotRejections)} hint={acquisition.data ? `${formatInteger(acquisition.data.staleValueRejectionCount)} 个必填字段命中过期` : "等待采集状态上报"} />
        </div>
        {delivery?.lastError && <Alert tone="warning">{delivery.lastError}</Alert>}
      </Card>
      <Card title="采集配置应用状态" description="Platform 发布期望版本，Edge 主动拉取、验证并在安全过程执行边界应用；失败时保留上一成功版本。">
        <DataTable
          rows={deploymentRows}
          keyField="taskId"
          columns={[
            { key: "ingestionTask", label: "数据源", render: (_value, row) => row.ingestionTask?.name || row.taskId },
            { key: "desiredVersion", label: "期望版本", render: value => `v${value}` },
            { key: "appliedVersion", label: "应用版本", render: value => value ? `v${value}` : "尚未应用" },
            { key: "state", label: "应用状态", render: value => <StatusBadge value={value} /> },
            { key: "desiredConfigurationHash", label: "配置指纹", render: value => value ? <code title={value}>{String(value).slice(0, 12)}</code> : "—" },
            { key: "appliedAt", label: "应用时间", render: formatTime },
            { key: "lastError", label: "应用问题", render: value => value || "无" },
          ]}
        />
        <p className="mt-4 text-xs text-slate-500">
          配置来源：{acquisition.data?.configurationSource || "尚未上报"} · 期望集合 {acquisition.data?.desiredConfigurationSetHash?.slice(0, 12) || "—"} · 已应用集合 {acquisition.data?.appliedConfigurationSetHash?.slice(0, 12) || "—"}
        </p>
      </Card>
      <Card title="已下发的采集配置" description="每行对应一份已下发到节点的采集配置版本。">
        <DataTable
          rows={taskRows}
          keyField="configurationKey"
          columns={[
            { key: "ingestionTask", label: "数据源", render: (_value, row) => row.ingestionTask ? <div><p className="font-medium text-slate-900">{row.ingestionTask.name}</p><p className="text-xs text-slate-500">{objectTypeLabel(row.ingestionTask.subjectType)} · {row.ingestionTask.subjectId}</p></div> : <span className="text-slate-500">{row.configurationKey}</span> },
            { key: "_protocol", label: "接入协议", render: (_value, row) => row.ingestionTask ? acquisitionProtocolLabels[row.ingestionTask.protocol] || row.ingestionTask.protocol : "配置未匹配" },
            { key: "_coverage", label: "采集内容", render: (_value, row) => row.ingestionTask ? `${row.ingestionTask.valueMappings?.length || 0} 信号 · ${row.ingestionTask.processSpecification?.parameterMappings?.length || 0} 控制参数${row.ingestionTask.lifecycle ? " · 过程执行" : ""}` : "—" },
            { key: "state", label: "状态", render: value => <StatusBadge value={value} /> },
            { key: "_snapshotSilence", label: "有效快照静默", render: (_value, row) => {
              const monitor = snapshotSilenceMonitor(row.ingestionTask, row, runtimeReportedAt, acquisitionStatusReceivedAt, nowMs);
              return <div><Badge tone={monitor.tone}>{monitor.label}</Badge><p className="mt-1 min-w-36 text-xs text-slate-500">{monitor.detail}</p></div>;
            } },
            { key: "_flow", label: "数据流", render: (_value, row) => `${formatInteger(row.readSuccessCount)} 读取 · ${formatInteger(row.validSnapshotCount)} 有效快照 · ${formatInteger(row.emittedEventCount)} 事件` },
            { key: "_suppression", label: "抑制/停机", render: (_value, row) => `${formatInteger(row.duplicateSuppressionCount)} 重复 · ${formatInteger(row.inactiveSnapshotCount)} 非运行 · ${formatInteger(row.sourceIdentityStallCount)} 停滞` },
            { key: "staleSnapshotRejectionCount", label: "陈旧拒绝", render: (value, row) => `${formatInteger(value)} 次 · ${formatInteger(row.staleValueRejectionCount)} 字段` },
            { key: "observedIntervalMs", label: "实际间隔", render: value => formatDuration(value) },
            { key: "lastReadDurationMs", label: "最近读取耗时", render: value => formatDuration(value) },
            { key: "lastReadSuccessAt", label: "最近读取", render: formatTime },
            { key: "lastValidSnapshotAt", label: "最近有效快照", render: formatTime },
            { key: "lastError", label: "最近问题", render: value => value || "无" },
          ]}
        />
      </Card>
      <Card
        title={showSystemLogs ? "节点系统日志" : "节点操作日志"}
        description={`${showSystemLogs ? "包含框架启动和内部运行记录" : "默认仅显示连接、采集、配置、上行问题与可操作事件"} · 最近 ${recentLogs.length} 条 / 共 ${logs.data?.total ?? recentLogs.length} 条`}
        actions={<button type="button" className="text-sm font-medium text-blue-600 hover:text-blue-700" onClick={() => setShowSystemLogs(value => !value)}>{showSystemLogs ? "返回操作日志" : "展开系统日志"}</button>}
      >
        <DataTable
          rows={recentLogs}
          getRowKey={(row, index) => `${row.timestamp}:${index}`}
          columns={[
            { key: "timestamp", label: "时间", render: formatTime },
            { key: "level", label: "级别", render: value => <Badge tone={["error", "fatal"].includes(String(value).toLowerCase()) ? "danger" : String(value).toLowerCase() === "warning" ? "warning" : "neutral"}>{value}</Badge> },
            { key: "message", label: "内容" },
            { key: "category", label: "类别", render: (value, row) => <div><p>{value || "节点服务"}</p>{showSystemLogs && <p className="mt-0.5 max-w-80 break-all text-xs text-slate-400">{String(row.source || "—").replaceAll("\"", "")}</p>}</div> },
          ]}
        />
      </Card>
    </Page>
  );
}
