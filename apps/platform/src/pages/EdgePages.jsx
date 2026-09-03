// 展示 Edge 注册、心跳和采集运行状态，不推断未上报健康信息。
import { useState } from "react";
import { Link, useParams } from "react-router";
import { extractRows, useApi } from "../hooks/useApi";
import { Alert, Badge, Card, DataTable, Metric, Page, RequestError, StatusBadge, WorkflowGuide } from "../ui/components";
import { formatTime, formatInteger, formatDuration, formatBytes, edgeStatus, acquisitionProtocolLabels, objectTypeLabel, LoadingCard } from "./shared";

export function EdgesPage() {
  const { data, loading, error, reload } = useApi("/api/edges", { interval: 10000 });
  const rows = extractRows(data);
  const online = rows.filter(row => edgeStatus(row) === "online").length;
  return (
    <Page
      title="现场节点"
      actions={<Link className="inline-flex min-h-9 items-center rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700" to="/configuration/ingestion-tasks">配置数据源</Link>}
    >
      <RequestError error={error} title="现场节点暂不可用" onRetry={reload} />
      <div className="grid gap-4 sm:grid-cols-3">
        <Metric label="现场节点" value={rows.length} hint="已登记" />
        <Metric label="当前在线" value={online} hint="30 秒内有心跳" />
        <Metric label="需要处理" value={rows.filter(row => edgeStatus(row) !== "online").length} hint="离线或运行异常" />
      </div>
      {loading && !data ? <LoadingCard /> : (
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
              { key: "_action", label: "操作", render: (_value, row) => <Link className="font-medium text-blue-600 hover:text-blue-700" to={`/edges/${encodeURIComponent(row.edgeId)}`}>查看诊断</Link> },
            ]}
          />
        </Card>
      )}
    </Page>
  );
}
export function EdgeDetailPage() {
  const [showSystemLogs, setShowSystemLogs] = useState(false);
  const { edgeId = "" } = useParams();
  const encodedId = encodeURIComponent(edgeId);
  const edges = useApi("/api/edges", { interval: 10000 });
  const acquisition = useApi(`/api/edges/${encodedId}/acquisition/status`, { interval: 5000 });
  const statusIntervals = useApi(`/api/edges/${encodedId}/status-intervals?limit=24`, { interval: 30000 });
  const logAudience = showSystemLogs ? "" : "&audience=operator";
  const logs = useApi(`/api/edges/${encodedId}/logs?page=1&pageSize=50${logAudience}`, { interval: 10000 });
  const ingestionTasks = useApi("/api/v1/ingestion-tasks", { interval: 10000 });
  const edge = extractRows(edges.data).find(row => row.edgeId === edgeId);
  const tasks = acquisition.data?.tasks || [];
  const deploymentStates = acquisition.data?.deployments || [];
  const edgeTasks = extractRows(ingestionTasks.data).filter(task => task.edgeId === edgeId);
  const tasksByKey = new Map(edgeTasks.map(task => [`${task.taskId}@${task.version}`, task]));
  const tasksByVersion = new Map(edgeTasks.map(task => [`${task.taskId}@${task.version}`, task]));
  const taskRows = tasks.map(task => ({ ...task, ingestionTask: tasksByKey.get(task.configurationKey) }));
  const deploymentRows = deploymentStates.map(deployment => ({
    ...deployment,
    ingestionTask: tasksByVersion.get(`${deployment.taskId}@${deployment.desiredVersion}`),
  }));
  const runningTasks = tasks.filter(task => task.state === "running").length;
  const convergedDeployments = deploymentStates.filter(deployment =>
    deployment.state === "applied" &&
    deployment.desiredVersion === deployment.appliedVersion &&
    deployment.desiredConfigurationHash === deployment.appliedConfigurationHash).length;
  const publishedTasks = edgeTasks.filter(task => task.status === "published");
  const processSignalCount = publishedTasks.reduce((total, task) => total + (task.valueMappings?.length || 0), 0);
  const controlParameterMappingCount = publishedTasks.reduce((total, task) => total + (task.processSpecification?.parameterMappings?.length || 0), 0);
  const lifecycleTaskCount = publishedTasks.filter(task => task.lifecycle).length;
  const allTaskDefinitionsResolved = tasks.length > 0 && taskRows.every(task => task.ingestionTask);
  const error = edges.error || acquisition.error || statusIntervals.error || logs.error || ingestionTasks.error;
  const delivery = edge?.delivery;
  const outboxBacklog = Number(delivery?.pendingEventCount || 0);
  const shipped = Number(delivery?.eventsShipped || 0);
  const staleSnapshotRejections = Number(acquisition.data?.staleSnapshotRejectionCount || 0);
  const recentLogs = extractRows(logs.data);
  const deliveryReady = runningTasks > 0 && processSignalCount > 0 && controlParameterMappingCount > 0 && lifecycleTaskCount > 0 && outboxBacklog === 0;

  return (
    <Page
      title={edge?.hostname || edgeId || "数据源节点"}
      actions={(
        <>
          <Link className="inline-flex min-h-9 items-center rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50" to="/edges">返回现场节点</Link>
          <Link className="inline-flex min-h-9 items-center rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700" to="/configuration/ingestion-tasks">配置数据源</Link>
        </>
      )}
    >
      <RequestError
        error={error}
        title="部分诊断信息暂不可用"
        onRetry={() => Promise.all([edges.reload(), acquisition.reload(), statusIntervals.reload(), logs.reload(), ingestionTasks.reload()])}
      />
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Metric label="设备连接" value={<StatusBadge value={edgeStatus(edge)} />} hint={edge?.lastSeen ? `最后心跳 ${formatTime(edge.lastSeen)}` : "尚未收到心跳"} />
        <Metric label="配置收敛" value={<StatusBadge value={acquisition.data?.state || "unknown"} />} hint={`${convergedDeployments} 个已应用 / ${deploymentStates.length} 个期望配置`} />
        <Metric label="数据上行" value={<StatusBadge value={delivery?.state || "unknown"} />} hint={delivery ? `积压 ${formatInteger(outboxBacklog)} · ACK ${formatInteger(delivery.lastAcknowledgedSequence)}` : "等待节点主动上报"} />
        <Metric label="工艺建模" value={controlParameterMappingCount > 0 ? "工艺规范已映射" : "待映射"} hint={`${processSignalCount} 条过程信号 · ${controlParameterMappingCount} 个控制参数`} />
      </div>
      {(edge?.lastError || acquisition.data?.lastError || outboxBacklog > 0) ? (
        <Alert tone="warning" title="节点需要关注">
          <ul className="list-disc space-y-1 pl-5">
            {edge?.lastError && <li>{edge.lastError}</li>}
            {acquisition.data?.lastError && <li>{acquisition.data.lastError}</li>}
            {outboxBacklog > 0 && <li>仍有 {formatInteger(outboxBacklog)} 条事件等待上行。</li>}
          </ul>
        </Alert>
      ) : !deliveryReady ? (
        <Alert tone="warning" title="数据源尚未具备工艺闭环条件">
          <ul className="list-disc space-y-1 pl-5">
            {runningTasks === 0 && <li>尚无运行中的采集任务，请先发布并下发数据源配置。</li>}
            {processSignalCount === 0 && <li>尚未映射过程信号，无法形成可分析的过程曲线。</li>}
            {controlParameterMappingCount === 0 && <li>尚未回读实际控制参数，无法区分真实执行条件。</li>}
            {lifecycleTaskCount === 0 && <li>尚未映射过程执行边界，连续数据无法自动归属到一次运行。</li>}
          </ul>
        </Alert>
      ) : <Alert tone="success" title="采集端已具备交付条件">过程信号、实际工艺规范、过程执行边界与数据上行均已就绪；请继续确认质检结果已关联到相同运行。</Alert>}
      <WorkflowGuide
        title="从设备数据到工艺证据"
        description="确认设备连接、采集上行、工艺映射和运行关联。"
        compact
        steps={[
          { title: "连接数据源", description: edgeStatus(edge) === "online" ? "现场节点持续在线。" : "等待节点恢复心跳。", state: edgeStatus(edge) === "online" ? "done" : "current" },
          { title: "采集并上行", description: runningTasks > 0 ? `${runningTasks} 个任务正在采集，${outboxBacklog > 0 ? `${formatInteger(outboxBacklog)} 条事件等待上行。` : "当前没有积压事件。"}` : "尚无运行中的采集任务。", state: runningTasks > 0 && outboxBacklog === 0 ? "done" : "current" },
          { title: "映射工艺语义", description: `${processSignalCount} 条过程信号、${controlParameterMappingCount} 个控制参数${lifecycleTaskCount > 0 ? "，已配置过程执行边界。" : "；尚未配置过程执行边界。"}`, state: processSignalCount > 0 && controlParameterMappingCount > 0 && lifecycleTaskCount > 0 ? "done" : "current" },
          { title: "验证闭环证据", description: deliveryReady ? "采集端条件已具备；请在运行记录与质量任务中确认实际关联，再进入追因。" : "补齐当前步骤后，再用运行记录与质量任务验证证据是否完整。", state: deliveryReady ? "current" : "upcoming" },
        ]}
      />
      <Card
        title="数据源交付情况"
        description="查看已发布数据源的过程信号、控制参数和运行边界映射。"
        actions={<Link className="text-sm font-medium text-blue-600 hover:text-blue-700" to="/configuration/ingestion-tasks">查看数据源配置</Link>}
      >
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <Metric label="已发布数据源" value={publishedTasks.length} hint={allTaskDefinitionsResolved ? "运行任务已关联配置版本" : tasks.length ? "有运行任务尚未匹配配置版本" : "尚未加载运行任务"} />
          <Metric label="过程信号映射" value={processSignalCount} hint="用于形成过程曲线和特征" />
          <Metric label="控制参数回读" value={controlParameterMappingCount} hint={controlParameterMappingCount ? "用于区分实际执行条件" : "追因与优化需要实际控制参数回读"} />
          <Metric label="过程执行边界映射" value={lifecycleTaskCount} hint={lifecycleTaskCount ? "可生成离散运行过程执行" : "连续数据尚不能自动形成过程执行"} />
        </div>
        <p className="mt-5 rounded-xl bg-slate-50 px-4 py-3 text-sm leading-6 text-slate-600">
          {deliveryReady
            ? "数据源已具备过程分析条件。下一步确认运行曲线与质量结果已关联。"
            : "补齐运行任务、过程信号、实际控制参数和运行边界后，即可形成可分析的运行证据。"}
        </p>
      </Card>
      <Card title="采集与上行状态区间" description="连续相同状态自动合并；原始心跳仍在 Platform 保存七天，用于审计和复算。">
        <DataTable
          rows={extractRows(statusIntervals.data)}
          getRowKey={row => `${row.startedAt}:${row.endedAt}`}
          columns={[
            { key: "startedAt", label: "持续区间", render: (_value, row) => <div><p>{formatTime(row.startedAt)} – {formatTime(row.endedAt)}</p><p className="mt-0.5 text-xs text-slate-500">持续 {formatDuration(Math.max(0, new Date(row.endedAt) - new Date(row.startedAt)))} · {formatInteger(row.sampleCount)} 次心跳</p></div> },
            { key: "acquisitionState", label: "采集", render: value => <StatusBadge value={value || "unknown"} /> },
            { key: "endingValidSnapshotCount", label: "新增快照", render: (_value, row) => formatInteger(Math.max(0, Number(row.endingValidSnapshotCount) - Number(row.startingValidSnapshotCount))) },
            { key: "endingEmittedEventCount", label: "新增事件", render: (_value, row) => formatInteger(Math.max(0, Number(row.endingEmittedEventCount) - Number(row.startingEmittedEventCount))) },
            { key: "deliveryState", label: "上行", render: value => <StatusBadge value={value || "unknown"} /> },
            { key: "maximumPendingEventCount", label: "最高积压", render: formatInteger },
            { key: "_error", label: "问题", render: (_value, row) => row.acquisitionError || row.deliveryError || "无" },
          ]}
        />
      </Card>
      <Card title="上送恢复基线" description="查看积压容量、上送速率和最近恢复情况。">
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <Metric label="当前积压" value={formatInteger(outboxBacklog)} hint="未收到平台确认的事件" />
          <Metric label="最老积压" value={delivery?.oldestPendingEventAt ? formatTime(delivery.oldestPendingEventAt) : "—"} hint={delivery?.backlogCapacityUsedPercent == null ? "容量尚未上报" : `容量使用 ${Number(delivery.backlogCapacityUsedPercent).toFixed(1)}%`} />
          <Metric label="本地存储" value={formatBytes(delivery?.localStorageBytes)} hint={delivery?.backlogCapacityRows ? `硬上限 ${formatInteger(delivery.backlogCapacityRows)} 条` : "未配置条数上限"} />
          <Metric label="上送速率" value={delivery?.shipmentRatePerSecond == null ? "—" : `${Number(delivery.shipmentRatePerSecond).toFixed(1)} 条/s`} hint={delivery?.estimatedDrainSeconds == null ? "暂无清空估算" : `预计 ${formatDuration(Number(delivery.estimatedDrainSeconds) * 1000)} 清空`} />
          <Metric label="累计已确认" value={formatInteger(shipped)} hint={`最后 ACK ${formatInteger(delivery?.lastAcknowledgedSequence)}`} />
          <Metric label="恢复次数" value={formatInteger(delivery?.recoveryCount)} hint={`${formatInteger(delivery?.consecutiveFailures)} 次连续失败`} />
          <Metric label="最近恢复耗时" value={delivery?.lastRecoveryDurationMs == null ? "—" : formatDuration(delivery.lastRecoveryDurationMs)} hint={delivery?.lastSuccessfulShipmentAt ? `恢复于 ${formatTime(delivery.lastSuccessfulShipmentAt)}` : "尚无恢复记录"} />
          <Metric label="陈旧快照拒绝" value={formatInteger(staleSnapshotRejections)} hint={`${formatInteger(acquisition.data?.staleValueRejectionCount)} 个必填字段命中过期`} />
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
      <Card title="运行中的数据源" description="每行对应一份已下发到节点的不可变数据源配置版本。">
        <DataTable
          rows={taskRows}
          keyField="configurationKey"
          columns={[
            { key: "ingestionTask", label: "数据源", render: (_value, row) => row.ingestionTask ? <div><p className="font-medium text-slate-900">{row.ingestionTask.name}</p><p className="text-xs text-slate-500">{objectTypeLabel(row.ingestionTask.subjectType)} · {row.ingestionTask.subjectId}</p></div> : <span className="text-slate-500">{row.configurationKey}</span> },
            { key: "_protocol", label: "接入协议", render: (_value, row) => row.ingestionTask ? acquisitionProtocolLabels[row.ingestionTask.protocol] || row.ingestionTask.protocol : "配置未匹配" },
            { key: "_coverage", label: "采集内容", render: (_value, row) => row.ingestionTask ? `${row.ingestionTask.valueMappings?.length || 0} 信号 · ${row.ingestionTask.processSpecification?.parameterMappings?.length || 0} 控制参数${row.ingestionTask.lifecycle ? " · 过程执行" : ""}` : "—" },
            { key: "state", label: "状态", render: value => <StatusBadge value={value} /> },
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
