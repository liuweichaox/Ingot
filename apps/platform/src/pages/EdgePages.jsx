import { Link, useParams } from "react-router";
import { extractRows, useApi } from "../hooks/useApi";
import { Alert, Badge, Card, DataTable, Metric, Page, StatusBadge, WorkflowGuide } from "../ui/components";
import { formatTime, formatInteger, formatDuration, edgeStatus, acquisitionProtocolLabels, objectTypeLabel, LoadingCard } from "./shared";

export function EdgesPage() {
  const { data, loading, error } = useApi("/api/edges", { interval: 10000 });
  const rows = extractRows(data);
  const online = rows.filter(row => edgeStatus(row) === "online").length;
  return (
    <Page
      title="现场节点"
      description="查看部署在现场、负责连接设备并上报数据的节点。"
      actions={<Link className="inline-flex min-h-9 items-center rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700" to="/configuration/acquisition-profiles">配置数据源</Link>}
    >
      {error && <Alert tone="danger" title="现场节点暂不可用">{error}</Alert>}
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
  const { edgeId = "" } = useParams();
  const encodedId = encodeURIComponent(edgeId);
  const edges = useApi("/api/edges", { interval: 10000 });
  const acquisition = useApi(`/api/edges/${encodedId}/acquisition/status`, { interval: 5000 });
  const logs = useApi(`/api/edges/${encodedId}/logs?page=1&pageSize=50`, { interval: 10000 });
  const profiles = useApi("/api/v1/acquisition-profiles", { interval: 10000 });
  const edge = extractRows(edges.data).find(row => row.edgeId === edgeId);
  const tasks = acquisition.data?.tasks || [];
  const deploymentStates = acquisition.data?.deployments || [];
  const edgeProfiles = extractRows(profiles.data).filter(profile => profile.edgeId === edgeId);
  const profilesByTaskKey = new Map(edgeProfiles.map(profile => [`${profile.profileId}@${profile.version}`, profile]));
  const profilesByVersion = new Map(edgeProfiles.map(profile => [`${profile.profileId}@${profile.version}`, profile]));
  const taskRows = tasks.map(task => ({ ...task, profile: profilesByTaskKey.get(task.configurationKey) }));
  const deploymentRows = deploymentStates.map(deployment => ({
    ...deployment,
    profile: profilesByVersion.get(`${deployment.profileId}@${deployment.desiredVersion}`),
  }));
  const runningTasks = tasks.filter(task => task.state === "running").length;
  const convergedDeployments = deploymentStates.filter(deployment =>
    deployment.state === "applied" &&
    deployment.desiredVersion === deployment.appliedVersion &&
    deployment.desiredConfigurationHash === deployment.appliedConfigurationHash).length;
  const publishedProfiles = edgeProfiles.filter(profile => profile.status === "published");
  const processSignalCount = publishedProfiles.reduce((total, profile) => total + (profile.valueMappings?.length || 0), 0);
  const controlParameterMappingCount = publishedProfiles.reduce((total, profile) => total + (profile.processSpecification?.parameterMappings?.length || 0), 0);
  const lifecycleProfileCount = publishedProfiles.filter(profile => profile.lifecycle).length;
  const allTaskProfilesResolved = tasks.length > 0 && taskRows.every(task => task.profile);
  const error = edges.error || acquisition.error || logs.error || profiles.error;
  const delivery = edge?.delivery;
  const outboxBacklog = Number(delivery?.pendingEventCount || 0);
  const shipped = Number(delivery?.eventsShipped || 0);
  const staleSnapshotRejections = Number(acquisition.data?.staleSnapshotRejectionCount || 0);
  const recentLogs = extractRows(logs.data);
  const deliveryReady = runningTasks > 0 && processSignalCount > 0 && controlParameterMappingCount > 0 && lifecycleProfileCount > 0 && outboxBacklog === 0;

  return (
    <Page
      title={edge?.hostname || edgeId || "数据源节点"}
      description="确认现场数据是否已从设备连接、采集和上行，交付为可用于工艺追因与优化的过程证据。"
      actions={(
        <>
          <Link className="inline-flex min-h-9 items-center rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50" to="/edges">返回现场节点</Link>
          <Link className="inline-flex min-h-9 items-center rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700" to="/configuration/acquisition-profiles">配置数据源</Link>
        </>
      )}
    >
      {error && <Alert tone="danger" title="部分诊断信息暂不可用">{error}</Alert>}
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
            {lifecycleProfileCount === 0 && <li>尚未映射过程执行边界，连续数据无法自动归属到一次运行。</li>}
          </ul>
        </Alert>
      ) : <Alert tone="success" title="采集端已具备交付条件">过程信号、实际工艺规范、过程执行边界与数据上行均已就绪；请继续确认质检结果已关联到相同运行。</Alert>}
      <WorkflowGuide
        title="从设备数据到工艺证据"
        description="节点只负责可靠交付数据；完整闭环还必须在平台把生产运行、实际工艺规范、过程曲线与质量结果关联起来。"
        compact
        steps={[
          { title: "连接数据源", description: edgeStatus(edge) === "online" ? "现场节点持续在线。" : "等待节点恢复心跳。", state: edgeStatus(edge) === "online" ? "done" : "current" },
          { title: "采集并上行", description: runningTasks > 0 ? `${runningTasks} 个任务正在采集，${outboxBacklog > 0 ? `${formatInteger(outboxBacklog)} 条事件等待上行。` : "当前没有积压事件。"}` : "尚无运行中的采集任务。", state: runningTasks > 0 && outboxBacklog === 0 ? "done" : "current" },
          { title: "映射工艺语义", description: `${processSignalCount} 条过程信号、${controlParameterMappingCount} 个控制参数${lifecycleProfileCount > 0 ? "，已配置过程执行边界。" : "；尚未配置过程执行边界。"}`, state: processSignalCount > 0 && controlParameterMappingCount > 0 && lifecycleProfileCount > 0 ? "done" : "current" },
          { title: "验证闭环证据", description: deliveryReady ? "采集端条件已具备；请在运行记录与质量任务中确认实际关联，再进入追因和实验。" : "补齐当前步骤后，再用运行记录与质量任务验证证据是否完整。", state: deliveryReady ? "current" : "upcoming" },
        ]}
      />
      <Card
        title="数据源交付情况"
        description="这里显示已发布配置所承诺的工艺语义，不把节点运行指标误当成已经完成的质量或追因结论。"
        actions={<Link className="text-sm font-medium text-blue-600 hover:text-blue-700" to="/configuration/acquisition-profiles">查看数据源配置</Link>}
      >
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <Metric label="已发布数据源" value={publishedProfiles.length} hint={allTaskProfilesResolved ? "运行任务已关联配置版本" : tasks.length ? "有运行任务尚未匹配配置版本" : "尚未加载运行任务"} />
          <Metric label="过程信号映射" value={processSignalCount} hint="用于形成过程曲线和特征" />
          <Metric label="控制参数回读" value={controlParameterMappingCount} hint={controlParameterMappingCount ? "用于区分实际执行条件" : "追因与优化需要实际控制参数回读"} />
          <Metric label="过程执行边界映射" value={lifecycleProfileCount} hint={lifecycleProfileCount ? "可生成离散运行过程执行" : "连续数据尚不能自动形成过程执行"} />
        </div>
        <p className="mt-5 rounded-xl bg-slate-50 px-4 py-3 text-sm leading-6 text-slate-600">
          {deliveryReady
            ? "采集端已满足过程信号、实际工艺规范与过程执行边界的交付条件。下一步在“运行记录”和“质量任务”中确认同一运行的曲线与结果已关联，随后再发起追因或优化实验。"
            : "这不是追因结论。请先补齐运行任务、过程信号、实际控制参数回读和过程执行边界；质量结果由质检流程关联后，才形成可用于追因和优化的完整证据。"}
        </p>
      </Card>
      <Card title="上送恢复基线" description="状态由 Edge 随心跳主动上报，不要求 Platform 反向访问 OT 网络。">
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5">
          <Metric label="当前积压" value={formatInteger(outboxBacklog)} hint="未收到平台确认的事件" />
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
          keyField="profileId"
          columns={[
            { key: "profile", label: "数据源", render: (_value, row) => row.profile?.name || row.profileId },
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
            { key: "profile", label: "数据源", render: (_value, row) => row.profile ? <div><p className="font-medium text-slate-900">{row.profile.name}</p><p className="text-xs text-slate-500">{objectTypeLabel(row.profile.subjectType)} · {row.profile.subjectId}</p></div> : <span className="text-slate-500">{row.configurationKey}</span> },
            { key: "_protocol", label: "接入协议", render: (_value, row) => row.profile ? acquisitionProtocolLabels[row.profile.protocol] || row.profile.protocol : "配置未匹配" },
            { key: "_coverage", label: "采集内容", render: (_value, row) => row.profile ? `${row.profile.valueMappings?.length || 0} 信号 · ${row.profile.processSpecification?.parameterMappings?.length || 0} 控制参数${row.profile.lifecycle ? " · 过程执行" : ""}` : "—" },
            { key: "state", label: "状态", render: value => <StatusBadge value={value} /> },
            { key: "samplesCollected", label: "已采样", render: formatInteger },
            { key: "staleSnapshotRejectionCount", label: "陈旧拒绝", render: (value, row) => `${formatInteger(value)} 次 · ${formatInteger(row.staleValueRejectionCount)} 字段` },
            { key: "observedIntervalMs", label: "实际间隔", render: value => formatDuration(value) },
            { key: "lastReadDurationMs", label: "最近读取耗时", render: value => formatDuration(value) },
            { key: "lastSuccessAt", label: "最近成功", render: formatTime },
            { key: "lastError", label: "最近问题", render: value => value || "无" },
          ]}
        />
      </Card>
      <Card title="节点诊断日志" description={`仅在排查连接、协议或上行问题时使用 · 最近 ${recentLogs.length} 条 / 共 ${logs.data?.total ?? recentLogs.length} 条`}>
        <DataTable
          rows={recentLogs}
          getRowKey={(row, index) => `${row.timestamp}:${index}`}
          columns={[
            { key: "timestamp", label: "时间", render: formatTime },
            { key: "level", label: "级别", render: value => <Badge tone={["error", "fatal"].includes(String(value).toLowerCase()) ? "danger" : String(value).toLowerCase() === "warning" ? "warning" : "neutral"}>{value}</Badge> },
            { key: "message", label: "内容" },
            { key: "source", label: "来源", render: value => String(value || "—").replaceAll("\"", "") },
          ]}
        />
      </Card>
    </Page>
  );
}
