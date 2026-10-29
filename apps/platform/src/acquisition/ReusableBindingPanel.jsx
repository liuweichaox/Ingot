// 为已发布的数据源和模板建立逻辑采集任务，并展示已有绑定关系。
import { useState } from "react";
import { Link } from "react-router";
import { postJson } from "../api/http";
import { Alert, Button, Field, Input, Select, StatusBadge } from "../ui/components";
import { protocolDescriptor } from "./protocolRegistry";

const assetKey = (id, version) => `${id}::${version}`;
const subjectLabels = {
  equipment: "生产设备",
  quality_instrument: "检测仪器",
  line: "生产线",
  utility_meter: "公用工程仪表",
  system: "业务系统",
};
const subjectLabel = (type, id) => `${subjectLabels[type] || type || "对象"} · ${id || "未指定"}`;
const codePattern = /^[a-z0-9][a-z0-9._-]{0,127}$/;
const registerEndpoint = source => {
  if (["modbus-rtu", "modbus-ascii", "bacnet-mstp"].includes(source.protocol)) {
    const connection = source.protocol === "modbus-ascii" ? source.modbusAscii
      : source.protocol === "bacnet-mstp" ? source.bacnetMstp : source.modbusRtu;
    const portName = connection?.portName?.trim();
    if (!portName) return null;
    const physicalPort = /^(?:COM\d+|\\\\\.\\COM\d+)$/i.test(portName) ? portName.toUpperCase() : portName;
    const serialSettings = source.protocol === "bacnet-mstp"
      ? [source.protocol, connection.baudRate, connection.localStationAddress, connection.maxMaster, connection.maxInfoFrames, "8", "none", "one"]
      : [source.protocol, connection.baudRate, connection.dataBits, connection.parity, connection.stopBits];
    const completeSettings = serialSettings
      .map(value => String(value ?? "").trim().toLowerCase()).join(":");
    return {
      key: `${physicalPort}|${completeSettings}`,
      intervalMs: connection.pollIntervalMs || 1000,
    };
  }
  const connection = source.protocol === "melsec-a1e" ? source.melsecA1E
    : source.protocol === "modbus-tcp" ? source.modbusTcp : null;
  if (!connection?.host || !connection.port) return null;
  return { key: `${connection.host.trim().replace(/\.+$/, "").toLowerCase()}:${connection.port}`, intervalMs: connection.pollIntervalMs || 1000 };
};
const registerLoad = (source, sources, bindings, tasks) => {
  const endpoint = registerEndpoint(source);
  if (!endpoint) return null;
  const endpointSources = sources.filter(candidate => {
    const candidateEndpoint = registerEndpoint(candidate);
    return candidate.protocol === source.protocol && candidateEndpoint?.key === endpoint.key;
  });
  const sourceKeys = new Set(endpointSources.map(candidate => assetKey(candidate.dataSourceId, candidate.version)));
  const related = bindings.filter(binding => sourceKeys.has(assetKey(binding.dataSourceId, binding.dataSourceVersion)));
  const published = related.filter(binding => binding.status === "published" &&
    tasks.some(task => task.taskId === binding.taskId && Number(task.version) === Number(binding.version) && task.status === "published"));
  const drafts = related.filter(binding => binding.status === "draft");
  const snapshotsPerMinute = Math.round(published.reduce((total, binding) => {
    const boundSource = endpointSources.find(candidate => candidate.dataSourceId === binding.dataSourceId && Number(candidate.version) === Number(binding.dataSourceVersion));
    return total + 60000 / (registerEndpoint(boundSource)?.intervalMs || 1000);
  }, 0));
  return { publishedCount: published.length, draftCount: drafts.length, snapshotsPerMinute };
};
const connectionLabel = capability => !capability ? "连接复用能力未确认" : capability.sharesConnectionBetweenTasks
  ? "同一端点的兼容任务共享连接，读取与探查串行；连接参数冲突会被拒绝"
  : "各任务独立采集，不共享采集会话";

export function ReusableBindingPanel({ tasks, templates, sources, bindings, capabilities = [], onChanged, canWrite = true }) {
  const [sourceKey, setSourceKey] = useState("");
  const [templateKey, setTemplateKey] = useState("");
  const [taskId, setTaskId] = useState("");
  const [name, setName] = useState("");
  const [subjectType, setSubjectType] = useState("");
  const [subjectId, setSubjectId] = useState("");
  const [equipmentId, setEquipmentId] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  const publishedSources = sources.filter(source => source.status === "published");
  const source = publishedSources.find(item => assetKey(item.dataSourceId, item.version) === sourceKey);
  const capability = capabilities.find(item => item.protocol === source?.protocol);
  const matchingTemplates = templates.filter(item => item.status === "published" && item.protocol === source?.protocol);
  const template = matchingTemplates.find(item => assetKey(item.templateId, item.version) === templateKey);
  const normalizedTaskId = taskId.trim().toLowerCase();
  const duplicateTask = normalizedTaskId && [...tasks, ...bindings].some(item =>
    item.taskId?.trim().toLowerCase() === normalizedTaskId);
  const hasSubjectOverride = Boolean(subjectType || subjectId.trim());
  const subjectValid = !hasSubjectOverride || Boolean(subjectType && subjectId.trim());
  const canCreate = Boolean(source && template && codePattern.test(normalizedTaskId) &&
    name.trim() && subjectValid && !duplicateTask && !busy);
  const mqttClientId = source?.protocol === "mqtt" && source.mqtt?.clientId?.trim();

  async function createBinding() {
    if (!canCreate) return;
    setBusy(true);
    setMessage("");
    setError("");
    try {
      await postJson("/api/v1/ingestion-configuration/materialize", {
        bindings: [{
          taskId: normalizedTaskId,
          version: 1,
          name: name.trim(),
          status: "draft",
          templateId: template.templateId,
          templateVersion: template.version,
          dataSourceId: source.dataSourceId,
          dataSourceVersion: source.version,
          subjectType: subjectType || null,
          subjectId: subjectId.trim() || null,
          equipmentId: equipmentId.trim() || null,
        }],
      });
      await onChanged();
      setMessage(`已创建 ${normalizedTaskId} 草稿，请查看任务并验证连接后发布。`);
      setTaskId("");
      setName("");
      setSubjectType("");
      setSubjectId("");
      setEquipmentId("");
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="grid gap-4 border-t border-slate-100 pt-4" aria-label="采集数据源与任务关系">
      {canWrite && <>
      <div>
        <p className="text-sm font-semibold text-slate-900">为数据源建立采集任务</p>
        <p className="mt-1 text-xs leading-5 text-slate-600">数据源保存连接和默认数据归属对象；默认对象表示数据归属，不能据此推断连接指向的物理设备。多个任务可选择同一数据源，每个任务可以指定自己的逻辑对象与生产设备单元。</p>
      </div>
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        <Field label="已发布数据源">
          <Select value={sourceKey} onChange={event => { setSourceKey(event.target.value); setTemplateKey(""); }}>
            <option value="">请选择数据源</option>
            {publishedSources.map(item => <option key={assetKey(item.dataSourceId, item.version)} value={assetKey(item.dataSourceId, item.version)}>{item.name} · {item.dataSourceId} v{item.version}</option>)}
          </Select>
        </Field>
        <Field label="已发布任务模板" hint="只显示与数据源使用相同通信驱动的模板。">
          <Select value={templateKey} onChange={event => setTemplateKey(event.target.value)} disabled={!source}>
            <option value="">请选择任务模板</option>
            {matchingTemplates.map(item => <option key={assetKey(item.templateId, item.version)} value={assetKey(item.templateId, item.version)}>{item.name} · {item.templateId} v{item.version}</option>)}
          </Select>
        </Field>
        <Field label="新任务编号" error={duplicateTask ? "任务编号已存在，请使用新编号。" : taskId && !codePattern.test(normalizedTaskId) ? "只能包含字母、数字、点、下划线和短横线。" : ""}>
          <Input value={taskId} onChange={event => setTaskId(event.target.value)} placeholder="device-task-01" />
        </Field>
        <Field label="新任务名称"><Input value={name} onChange={event => setName(event.target.value)} placeholder="设备采集任务" /></Field>
        <Field label="逻辑对象类型" hint="不选择时使用数据源的默认对象。">
          <Select value={subjectType} onChange={event => { setSubjectType(event.target.value); if (!event.target.value) setSubjectId(""); }}>
            <option value="">使用数据源默认对象</option>
            {Object.entries(subjectLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
          </Select>
        </Field>
        <Field label="逻辑对象编号" hint="选择对象类型后填写对应编号；留空则使用数据源默认对象。" error={!subjectValid ? "请填写逻辑对象编号。" : ""}>
          <Input value={subjectId} disabled={!subjectType} onChange={event => setSubjectId(event.target.value)} placeholder="SUBJECT-01" />
        </Field>
        <Field label="生产设备单元编号（可选）" hint="按现场资产与生产上下文填写；没有独立设备单元时可留空。系统不会从逻辑对象推导设备层级。">
          <Input value={equipmentId} onChange={event => setEquipmentId(event.target.value)} placeholder="EQUIPMENT-01" />
        </Field>
      </div>
      {source && (
        <Alert tone="info" title="关系预览">
          数据源 {source.dataSourceId} v{source.version} · {protocolDescriptor(source.protocol).label} · 现场节点 {source.edgeId}；
          任务归属 {hasSubjectOverride ? subjectLabel(subjectType, subjectId.trim()) : `${subjectLabel(source.subjectType, source.subjectId)}（数据源默认）`}；
          设备单元 {equipmentId.trim() || "未指定"}；模板 {template ? `${template.templateId} v${template.version}` : "尚未选择"}。
        </Alert>
      )}
      {source && <p className="text-xs text-slate-600">{connectionLabel(capability)}。{capability?.supportedWireEncodings?.length > 0 && ` 驱动支持 ${capability.supportedWireEncodings.map(value => value === "binary" ? "二进制" : value === "ascii" ? "ASCII 码" : value).join("、")}。`}{source.protocol === "melsec-a1e" && ` 当前来源通信数据码：${source.melsecA1E?.dataCode === "ascii" ? "ASCII 码" : "二进制"}。`}</p>}
      {mqttClientId && <Alert tone="warning">该 MQTT 数据源配置了固定客户端编号。多个任务使用相同编号会互相断开；请使用未指定客户端编号的数据源，使每个任务生成独立编号。</Alert>}
      <p className="text-xs leading-5 text-slate-500">模板固定点位与运行边界。逻辑对象需要不同点位定义时，请选择对应模板；创建任务不会自动复制或改写地址。</p>
      <div><Button variant="primary" disabled={!canCreate} onClick={createBinding}>{busy ? "创建中…" : "创建任务草稿"}</Button></div>
      {message && <Alert tone="success">{message}</Alert>}
      {error && <Alert tone="danger">{error}</Alert>}
      </>}

      <div className="grid gap-3">
        <p className="text-sm font-semibold text-slate-900">数据源与任务关系</p>
        {sources.length === 0 && <p className="text-sm text-slate-500">尚无数据源；可先提取已发布任务或导入数据源。</p>}
        {sources.map(item => {
          const related = bindings.filter(binding => binding.dataSourceId === item.dataSourceId && Number(binding.dataSourceVersion) === Number(item.version));
          const sourceCapability = capabilities.find(value => value.protocol === item.protocol);
          const load = sourceCapability?.sharesConnectionBetweenTasks ? registerLoad(item, sources, bindings, tasks) : null;
          return (
            <div key={assetKey(item.dataSourceId, item.version)} className="rounded-lg border border-slate-200 p-3">
              <div className="flex flex-wrap items-center gap-2 text-sm">
                <strong>{item.name || item.dataSourceId}</strong>
                <span>{item.dataSourceId} v{item.version} · {protocolDescriptor(item.protocol).label} · {item.edgeId}</span>
                <StatusBadge value={item.status} />
              </div>
              <p className="mt-1 text-xs text-slate-500">数据源默认对象：{subjectLabel(item.subjectType, item.subjectId)}</p>
              <p className="mt-1 text-xs text-slate-500">{connectionLabel(sourceCapability)}</p>
              {load && <p className="mt-1 text-xs text-slate-600">同端点已发布 {load.publishedCount} 个任务，按配置周期预计约 {load.snapshotsPerMinute} 份快照/分钟；另有 {load.draftCount} 个草稿。读取会在任务间串行，实际周期需按现场设备响应时间验收。</p>}
              {related.length === 0 ? <p className="mt-2 text-xs text-slate-500">尚未绑定采集任务。</p> : (
                <ul className="mt-3 grid gap-2">
                  {related.map(binding => {
                    const task = tasks.find(value => value.taskId === binding.taskId && Number(value.version) === Number(binding.version));
                    const inherited = !binding.subjectType && !binding.subjectId;
                    const type = binding.subjectType || task?.subjectType || item.subjectType;
                    const id = binding.subjectId || task?.subjectId || item.subjectId;
                    return (
                      <li key={assetKey(binding.taskId, binding.version)} className="flex flex-wrap items-center gap-x-3 gap-y-1 rounded bg-slate-50 px-3 py-2 text-xs text-slate-700">
                        <span>{binding.name || binding.taskId} · {binding.taskId} v{binding.version}</span>
                        <span>模板 {binding.templateId} v{binding.templateVersion}</span>
                        <span>对象 {subjectLabel(type, id)}{inherited ? "（默认）" : ""}</span>
                        <span>设备单元 {binding.equipmentId || task?.equipmentId || "未指定"}</span>
                        <StatusBadge value={binding.status} />
                        <Link className="font-medium text-blue-700" to={`/configuration/ingestion-tasks/${encodeURIComponent(binding.taskId)}?version=${binding.version}`}>查看任务</Link>
                      </li>
                    );
                  })}
                </ul>
              )}
            </div>
          );
        })}
      </div>
    </section>
  );
}
