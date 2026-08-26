// 提供管理员维护用户角色与站点授权的受控页面。
import { ArrowPathIcon } from "@heroicons/react/24/outline";
import { useState } from "react";
import { Link } from "react-router";
import { postJson } from "../api/http";
import { extractRows, useApi } from "../hooks/useApi";
import { Alert, Button, Card, DataTable, Drawer, EmptyState, Field, Input, Metric, Page, RequestError, Select, StatusBadge, notify, useConfirmDialog } from "../ui/components";
import { formatTime, formatInteger, formatBytes, metricTotal, formatDuration, edgeStatus, LoadingCard } from "./shared";
import { formatRoleSummary, formatSiteScope, platformRoleOptions } from "../auth/identityPresentation";

export function UsersPage() {
  const { data, loading, error, reload } = useApi("/api/v1/users");
  const [createOpen, setCreateOpen] = useState(false);
  const [manageOpen, setManageOpen] = useState(false);
  const [selected, setSelected] = useState(null);
  const [createForm, setCreateForm] = useState({ username: "", displayName: "", password: "", roles: ["quality.inspector"], siteIdsText: "" });
  const [roles, setRoles] = useState([]);
  const [siteIdsText, setSiteIdsText] = useState("");
  const [password, setPassword] = useState("");
  const [actionError, setActionError] = useState("");
  const [busy, setBusy] = useState(false);
  const { confirm, confirmationDialog } = useConfirmDialog();

  function startCreate() {
    setCreateForm({ username: "", displayName: "", password: "", roles: ["quality.inspector"], siteIdsText: "" });
    setActionError("");
    setCreateOpen(true);
  }

  function startManage(user) {
    setSelected(user);
    setRoles(user.roles || []);
    setSiteIdsText((user.siteIds || []).join(", "));
    setPassword("");
    setActionError("");
    setManageOpen(true);
  }

  function toggleRole(role, enabled, target = "manage") {
    if (target === "create") {
      setCreateForm(current => ({
        ...current,
        roles: enabled ? [...current.roles, role] : current.roles.filter(value => value !== role),
      }));
      return;
    }
    setRoles(current => enabled ? [...current, role] : current.filter(value => value !== role));
  }

  async function runAction(action) {
    setBusy(true);
    setActionError("");
    try {
      await action();
      await reload();
      return true;
    } catch (requestError) {
      setActionError(requestError.message);
      return false;
    } finally {
      setBusy(false);
    }
  }

  async function createUser() {
    const { siteIdsText: rawSiteIds, ...fields } = createForm;
    const siteIds = rawSiteIds.split(",").map(value => value.trim()).filter(Boolean);
    const saved = await runAction(() => postJson("/api/v1/users", { ...fields, siteIds }));
    if (saved) {
      setCreateOpen(false);
      notify(`用户 ${createForm.displayName || createForm.username} 已创建。`);
    }
  }

  async function saveRoles() {
    const saved = await runAction(() => postJson(`/api/v1/users/${encodeURIComponent(selected.userId)}:set-roles`, { roles }));
    if (saved) notify("岗位权限已更新。");
  }

  async function saveSiteAccess() {
    const siteIds = siteIdsText.split(",").map(value => value.trim()).filter(Boolean);
    const saved = await runAction(() => postJson(
      `/api/v1/users/${encodeURIComponent(selected.userId)}:set-site-access`,
      { siteIds },
    ));
    if (saved) notify("站点访问范围已更新。");
  }

  async function savePassword() {
    const saved = await runAction(() => postJson(`/api/v1/users/${encodeURIComponent(selected.userId)}:set-password`, { password }));
    if (saved) {
      setPassword("");
      notify("密码已更新，该用户的其他会话已退出。");
    }
  }

  async function changeDisabled() {
    if (!selected.disabled && !await confirm({
      title: `停用账户 ${selected.username}`,
      description: "该用户现有会话将失效，并且不能再次登录。请先确认至少保留一个可用的平台管理员账户。",
      confirmLabel: "确认停用",
      tone: "danger",
    })) return;
    const saved = await runAction(() => postJson(
      `/api/v1/users/${encodeURIComponent(selected.userId)}:set-disabled`,
      { disabled: !selected.disabled },
    ));
    if (saved) {
      setSelected(current => ({ ...current, disabled: !current.disabled }));
      notify(selected.disabled ? "账户已恢复。" : "账户已停用。");
    }
  }

  const users = extractRows(data);
  const enabledAdministrators = users.filter(user => !user.disabled && (user.roles || []).includes("platform.admin"));
  const namedDemoAccounts = users.filter(user => !user.disabled && /^(demo|test|admin123)$/i.test(user.username || ""));
  return (
    <Page
      title="用户权限"
      actions={<Button variant="primary" onClick={startCreate}>创建用户</Button>}
    >
      <Alert tone="info" title="岗位分权">
        质量录入与复核应由不同人员承担；配置、生产操作和工艺改进也建议使用独立账户。账户、岗位、站点范围、密码和停用变更会写入 Platform 结构化审计日志。
      </Alert>
      <RequestError error={error} title="用户列表不可用" onRetry={reload} />
      {!loading && data && (namedDemoAccounts.length > 0 || enabledAdministrators.length < 2) && (
        <Alert tone="warning" title="上线前账户检查">
          {namedDemoAccounts.length > 0 ? `发现演示命名账户：${namedDemoAccounts.map(user => user.username).join("、")}；受控试点前应停用或替换。` : ""}
          {enabledAdministrators.length < 2 ? " 建议保留至少两个由不同人员持有的可用管理员账户，避免单一账户失效后无法恢复管理。" : ""}
        </Alert>
      )}
      {loading && !data ? <LoadingCard /> : (
        <Card title="平台用户" description={`共 ${users.length} 个账户`}>
          {users.length ? (
            <DataTable
              rows={users}
              keyField="userId"
              onRowClick={startManage}
              columns={[
                { key: "username", label: "用户名" },
                { key: "displayName", label: "姓名", render: value => value || "—" },
                { key: "roles", label: "岗位权限", render: formatRoleSummary },
                { key: "siteIds", label: "站点范围", render: (value, row) => formatSiteScope(value, row.roles) },
                { key: "disabled", label: "状态", render: value => <StatusBadge value={value ? "disabled" : "active"} /> },
                { key: "createdAt", label: "创建时间", render: formatTime },
                { key: "_action", label: "操作", render: (_value, row) => <Button variant="ghost" onClick={event => { event.stopPropagation(); startManage(row); }}>管理</Button> },
              ]}
            />
          ) : <EmptyState title="还没有本地账户" description="创建首个岗位账户后，可用于生产环境登录。" />}
        </Card>
      )}

      <Drawer
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        closeOnBackdrop={false}
        title="创建用户"
        description="用户名创建后不可修改；密码至少 8 位。"
        footer={<><Button onClick={() => setCreateOpen(false)}>取消</Button><Button variant="primary" disabled={busy || !createForm.username.trim() || createForm.password.length < 8 || createForm.roles.length === 0} onClick={createUser}>{busy ? "创建中" : "创建"}</Button></>}
      >
        {actionError && <Alert tone="danger">{actionError}</Alert>}
        <div className="grid gap-4">
          <Field label="用户名"><Input required autoComplete="off" value={createForm.username} onChange={event => setCreateForm({ ...createForm, username: event.target.value })} /></Field>
          <Field label="姓名"><Input value={createForm.displayName} onChange={event => setCreateForm({ ...createForm, displayName: event.target.value })} /></Field>
          <Field label="初始密码" hint="至少 8 位"><Input required type="password" autoComplete="new-password" value={createForm.password} onChange={event => setCreateForm({ ...createForm, password: event.target.value })} /></Field>
          <Field label="站点范围" hint="多个 SiteId 用英文逗号分隔；平台管理员可留空"><Input value={createForm.siteIdsText} onChange={event => setCreateForm({ ...createForm, siteIdsText: event.target.value })} placeholder="SITE-001, SITE-002" /></Field>
          <RoleSelector value={createForm.roles} onChange={(role, enabled) => toggleRole(role, enabled, "create")} />
        </div>
      </Drawer>

      <Drawer
        open={manageOpen}
        onClose={() => setManageOpen(false)}
        closeOnBackdrop={false}
        title={selected ? `管理用户 · ${selected.displayName || selected.username}` : "管理用户"}
        description="角色和密码变更立即生效；修改密码会注销该用户的其他会话。"
        footer={<Button onClick={() => setManageOpen(false)}>关闭</Button>}
      >
        {actionError && <Alert tone="danger">{actionError}</Alert>}
        {selected && (
          <div className="grid gap-5">
            <Card title="账户状态">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div><p className="font-medium">{selected.username}</p><p className="mt-1 text-sm text-slate-500">{selected.disabled ? "该账户目前不能登录。" : "该账户可以正常登录。"}</p></div>
                <Button variant={selected.disabled ? "primary" : "danger"} disabled={busy} onClick={changeDisabled}>{selected.disabled ? "恢复账户" : "停用账户"}</Button>
              </div>
            </Card>
            <Card title="岗位权限">
              <div className="grid gap-4">
                <RoleSelector value={roles} onChange={(role, enabled) => toggleRole(role, enabled)} />
                <div><Button variant="primary" disabled={busy || roles.length === 0} onClick={saveRoles}>保存权限</Button></div>
              </div>
            </Card>
            <Card title="站点访问范围" description="平台管理员拥有全部站点权限；其他岗位空值表示不授予生产站点读取权限。">
              <div className="grid gap-3 sm:grid-cols-[1fr_auto] sm:items-end">
                <Field label="SiteId"><Input value={siteIdsText} onChange={event => setSiteIdsText(event.target.value)} placeholder="SITE-001, SITE-002" /></Field>
                <Button variant="primary" disabled={busy} onClick={saveSiteAccess}>保存站点范围</Button>
              </div>
            </Card>
            <Card title="重置密码" description="新密码至少 8 位。">
              <div className="grid gap-3 sm:grid-cols-[1fr_auto] sm:items-end">
                <Field label="新密码"><Input type="password" autoComplete="new-password" value={password} onChange={event => setPassword(event.target.value)} /></Field>
                <Button variant="primary" disabled={busy || password.length < 8} onClick={savePassword}>更新密码</Button>
              </div>
            </Card>
          </div>
        )}
      </Drawer>
      {confirmationDialog}
    </Page>
  );
}
function RoleSelector({ value, onChange }) {
  return (
    <fieldset>
      <legend className="text-sm font-medium text-slate-700">岗位权限</legend>
      <div className="mt-2 grid gap-2">
        {platformRoleOptions.map(([role, label, description]) => (
          <label key={role} className="flex cursor-pointer gap-3 rounded-xl border border-slate-200 p-3 hover:bg-slate-50">
            <input type="checkbox" className="mt-1" checked={value.includes(role)} onChange={event => onChange(role, event.target.checked)} />
            <span><span className="block text-sm font-medium text-slate-800">{label}</span><span className="mt-0.5 block text-xs text-slate-500">{description}</span></span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}
export function MetricsPage() {
  const edgeResponse = useApi("/api/edges", { interval: 10000 });
  const metricResponse = useApi("/api/metrics-data?names=event_ingest_total,process_start_time_seconds,process_working_set_bytes,system_runtime_dotnet_thread_pool_queue_length", { interval: 30000 });
  const executionResponse = useApi("/api/v1/process-executions?limit=100", { interval: 10000 });
  const qualityResponse = useApi("/api/v1/inspection-tasks/summary", { interval: 10000 });
  const profileResponse = useApi("/api/v1/ingestion-tasks", { interval: 10000 });
  const contextResponse = useApi("/api/v1/production-contexts", { interval: 10000 });
  const inspectionResponse = useApi("/api/v1/inspection-records", { interval: 10000 });
  const reliabilityResponse = useApi("/api/v1/data-reliability/baseline?maximumRuns=2000", { interval: 30000 });
  const rows = extractRows(edgeResponse.data);
  const online = rows.filter(row => edgeStatus(row) === "online").length;
  const offline = rows.filter(row => edgeStatus(row) === "offline").length;
  const unknown = Math.max(0, rows.length - online - offline);
  const metrics = metricResponse.data;
  const ingested = metricTotal(metrics, "event_ingest_total");
  const startedAtSeconds = metricTotal(metrics, "process_start_time_seconds");
  const uptime = startedAtSeconds ? Date.now() - startedAtSeconds * 1000 : null;
  const memory = metricTotal(metrics, "process_working_set_bytes");
  const threadQueue = metricTotal(metrics, "system_runtime_dotnet_thread_pool_queue_length");
  const publishedProfiles = extractRows(profileResponse.data).filter(row => row.status === "published").length;
  const contexts = extractRows(contextResponse.data);
  const executions = extractRows(executionResponse.data);
  const inspections = extractRows(inspectionResponse.data);
  const activeContext = contexts.find(item => !item.validTo && item.status !== "closed");
  const completeContext = activeContext && [activeContext.equipmentId, activeContext.productCode, activeContext.processSpecificationId, activeContext.toolingAssemblyId || activeContext.toolingInstallationId, activeContext.externalBatchRef, activeContext.materialLotRef].every(Boolean);
  const completedExecutions = executions.filter(item => item.status === "completed" && item.lifecycleComplete !== false);
  const linkedExecution = completedExecutions.find(execution => inspections.some(record => record.executionId === execution.executionId));
  const admissionRate = (reliabilityResponse.data?.rates || []).find(item => item.code === "analysis_admission")?.rate || 0;
  const pilotChecks = [
    { title: "现场来源运行", passed: publishedProfiles > 0 && online > 0, detail: `${publishedProfiles} 个已发布数据源 · ${online} 个节点在线`, to: "/configuration/ingestion-tasks" },
    { title: "生产上下文完整", passed: Boolean(completeContext), detail: completeContext ? `${activeContext.equipmentId} · ${activeContext.externalBatchRef}` : "缺少设备、产品、工艺、工装、批次或材料", to: "/production/changeover" },
    { title: "运行—检验已关联", passed: Boolean(linkedExecution), detail: linkedExecution?.executionId || "尚无完整运行关联检验", to: "/process-executions" },
    { title: "正式分析可准入", passed: Number(reliabilityResponse.data?.analyzedRunCount || 0) > 0 && admissionRate > 0, detail: `${reliabilityResponse.data?.analyzedRunCount || 0} 条运行 · ${Math.round(admissionRate * 100)}% 准入`, to: "/data-quality" },
  ];
  const pilotReady = pilotChecks.every(item => item.passed);
  const actionRequired = qualityResponse.data?.actionRequired ?? 0;
  const error = edgeResponse.error || metricResponse.error || executionResponse.error || qualityResponse.error || profileResponse.error || contextResponse.error || inspectionResponse.error || reliabilityResponse.error;
  const healthy = offline === 0 && unknown === 0 && threadQueue === 0;
  return (
    <Page title="平台状态">
      <RequestError
        error={error}
        onRetry={() => Promise.all([edgeResponse.reload(), metricResponse.reload(), executionResponse.reload(), qualityResponse.reload(), profileResponse.reload(), contextResponse.reload(), inspectionResponse.reload(), reliabilityResponse.reload()])}
      />
      <Alert tone={healthy ? "success" : "warning"} title={healthy ? "平台运行正常" : "平台存在需要关注的项目"}>
        {healthy ? "中心服务和现场节点均在正常工作。" : `离线节点 ${offline} 个，待确认节点 ${unknown} 个，后台排队 ${formatInteger(threadQueue)} 项。`}
      </Alert>
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Metric label="已保存运行" value={formatInteger(executionResponse.data?.total)} hint="可追溯生产运行" />
        <Metric label="待处理质量任务" value={formatInteger(actionRequired)} hint="录入与复核合计" />
        <Metric label="已发布采集任务" value={formatInteger(publishedProfiles)} hint="正在向现场下发" />
        <Metric label="已摄入事件" value={formatInteger(ingested)} hint="本次平台运行累计" />
      </div>
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Metric label="平台运行时间" value={uptime == null ? "—" : formatDuration(uptime)} />
        <Metric label="当前内存" value={formatBytes(memory)} />
        <Metric label="后台排队" value={formatInteger(threadQueue)} />
        <Metric label="现场节点在线" value={`${online}/${rows.length}`} hint={`${offline} 个离线`} />
      </div>
      <Card
        title="受控试点业务闭环"
        description={pilotReady ? "业务数据门槛已满足；仍需独立完成备份恢复、故障、容量、告警送达和连续观察验收。" : "先关闭未通过项，再生成只读业务闭环验收工件。"}
        actions={<span className={`text-sm font-semibold ${pilotReady ? "text-emerald-700" : "text-amber-700"}`}>{pilotReady ? "业务闭环可验收" : `${pilotChecks.filter(item => !item.passed).length} 项待完成`}</span>}
      >
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          {pilotChecks.map(item => (
            <Link key={item.title} to={item.to} className={`rounded-lg border p-4 transition ${item.passed ? "border-emerald-200 bg-emerald-50" : "border-amber-200 bg-amber-50"}`}>
              <p className="flex items-center justify-between gap-2 font-semibold text-slate-950"><span>{item.title}</span><span className={item.passed ? "text-emerald-700" : "text-amber-700"}>{item.passed ? "通过" : "待完成"}</span></p>
              <p className="mt-2 text-sm leading-6 text-slate-600">{item.detail}</p>
            </Link>
          ))}
        </div>
        <div className="mt-4 rounded-xl border border-slate-200 bg-slate-50 p-4">
          <p className="text-sm font-semibold text-slate-900">生成验收工件</p>
          <p className="mt-1 text-sm leading-6 text-slate-600">由管理员在部署主机运行 <code className="rounded bg-white px-1.5 py-0.5 text-xs">node scripts/verify-pilot-workflow.mjs --output artifacts/pilot-workflow.json</code>。脚本只读取业务 API，不会修改生产记录。</p>
        </div>
      </Card>
      <Card title="现场节点" description="点击诊断可查看采集任务、上行积压和最近日志。">
        <DataTable rows={rows} keyField="edgeId" columns={[
          { key: "edgeId", label: "节点" },
          { key: "_status", label: "状态", render: (_value, row) => <StatusBadge value={edgeStatus(row)} /> },
          { key: "lastSeen", label: "最后心跳", render: formatTime },
          { key: "lastError", label: "最近问题", render: value => value || "无" },
          { key: "_action", label: "操作", render: (_value, row) => <Link className="font-medium text-blue-600 hover:text-blue-700" to={`/edges/${encodeURIComponent(row.edgeId)}`}>查看诊断</Link> },
        ]} />
      </Card>
    </Page>
  );
}

export function LogsPage() {
  const edgeResponse = useApi("/api/edges");
  const { data: edges } = edgeResponse;
  const edgeRows = extractRows(edges);
  const [edgeId, setEdgeId] = useState("");
  const [level, setLevel] = useState("");
  const endpoint = edgeId ? `/api/edges/${encodeURIComponent(edgeId)}/logs?pageSize=200${level ? `&level=${level}` : ""}` : null;
  const logs = useApi(endpoint, { enabled: Boolean(edgeId), interval: 5000 });
  return (
    <Page title="平台日志">
      <Card title="查询条件">
        <div className="grid gap-3 md:grid-cols-2">
          <Field label="边缘节点"><Select value={edgeId} onChange={event => setEdgeId(event.target.value)}><option value="">选择节点</option>{edgeRows.map(row => <option key={row.edgeId} value={row.edgeId}>{row.edgeId}</option>)}</Select></Field>
          <Field label="级别"><Select value={level} onChange={event => setLevel(event.target.value)}><option value="">全部</option><option value="Information">信息</option><option value="Warning">警告</option><option value="Error">错误</option></Select></Field>
        </div>
      </Card>
      <RequestError error={edgeResponse.error || logs.error} onRetry={() => Promise.all([edgeResponse.reload(), logs.reload()])} />
      <Card title="日志记录">
        {edgeId
          ? logs.loading && !logs.data
            ? <div className="inline-flex items-center gap-2 text-sm text-slate-500"><ArrowPathIcon className="size-4 animate-spin" />正在读取日志</div>
            : <DataTable
              rows={extractRows(logs.data)}
              getRowKey={(row, index) => `${row.timestamp}:${row.source}:${index}`}
              columns={[
                { key: "timestamp", label: "时间", render: formatTime },
                { key: "level", label: "级别", render: value => <StatusBadge value={value} /> },
                { key: "source", label: "来源", render: value => String(value || "—").replace(/^"|"$/g, "") },
                { key: "message", label: "消息" },
              ]}
            />
          : <EmptyState title="请选择边缘节点" description="选择后日志会自动加载并持续更新。" />}
      </Card>
    </Page>
  );
}

export function NotFoundPage() {
  return (
    <div className="grid min-h-[60vh] place-items-center text-center">
      <div>
        <p className="text-sm font-semibold text-blue-600">404</p>
        <h1 className="mt-2 text-3xl font-semibold text-slate-950">页面不存在</h1>
        <p className="mt-2 text-slate-500">地址可能已经变更，回到工作台继续。</p>
        <Link to="/workbench" className="mt-6 inline-flex rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white">返回工作台</Link>
      </div>
    </div>
  );
}
