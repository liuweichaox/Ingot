// Keep Edge placement, device selection, and the complete selected-device editor in one workspace.

import { useEffect, useMemo, useState } from "react";
import { Button } from "../ui/components";
import { deviceProfileOptions, deviceProfileOption } from "./deviceProfileRegistry";
import { protocolDescriptor } from "./protocolRegistry";

const DEVICE_DRAG_TYPE = "application/x-ingot-device-key";
const UNASSIGNED_EDGE_KEY = "__unassigned_edge__";
const SUBJECT_LABELS = {
  equipment: "生产设备",
  quality_instrument: "检测仪器",
  line: "生产线",
  utility_meter: "公用工程仪表",
  system: "业务系统",
};

function normalizeEdge(edge) {
  if (!edge?.edgeId) return null;
  const edgeId = String(edge.edgeId);
  return { edgeId, label: edge.hostname || edge.displayName || edgeId, unavailable: false };
}

function edgeAnchor(edgeId) {
  return edgeId ? `edge-${encodeURIComponent(edgeId)}` : UNASSIGNED_EDGE_KEY;
}

function connectionSummary(form, descriptor) {
  const connection = form?.[descriptor.section] || {};
  const endpoint = connection.host || connection.baseUrl || connection.endpointUrl || connection.portName || "连接参数待填写";
  const port = connection.port ? `:${connection.port}` : "";
  return `${endpoint}${port}`;
}

function pointSummary(form) {
  const configured = (form?.valueMappings || []).filter(row => row.dataItemCode || pointSource(row, form));
  const mapped = configured.filter(row => row.dataItemCode).length;
  return { configured: configured.length, mapped };
}

function pointSource(row, form) {
  const descriptor = protocolDescriptor(form?.protocol);
  if (descriptor.addressing === "modbus-register" && row.modbusAddress !== "" && row.modbusAddress != null)
    return `${row.modbusArea || "holding-register"}:${row.modbusAddress}${row.bitIndex === "" || row.bitIndex == null ? "" : `.${row.bitIndex}`}:${row.sourceDataType}`;
  if (descriptor.addressing === "melsec-device" && row.melsecAddress !== "" && row.melsecAddress != null)
    return `${row.melsecDevice || "D"}:${row.melsecAddress}${row.bitIndex === "" || row.bitIndex == null ? "" : `.${row.bitIndex}`}:${row.sourceDataType}`;
  return String(row.sourcePath || "").trim();
}

function statusFor(device, active, probePassed) {
  if (device.error) return { label: "保存失败", tone: "rose" };
  if (device.saved) return { label: "已保存草稿", tone: "emerald" };
  const points = pointSummary(device.form);
  if (active && probePassed) return { label: "探查通过", tone: "emerald" };
  if (points.mapped > 0) return { label: "待探查", tone: "blue" };
  return active ? { label: "正在配置", tone: "blue" } : { label: "待配置", tone: "amber" };
}

const statusTone = {
  rose: "bg-rose-100 text-rose-800",
  emerald: "bg-emerald-100 text-emerald-800",
  blue: "bg-blue-100 text-blue-800",
  amber: "bg-amber-100 text-amber-800",
};

function profileName(form) {
  const profile = deviceProfileOption(form?.deviceProfile?.kind);
  return [profile.label, form?.deviceProfile?.manufacturer, form?.deviceProfile?.model].filter(Boolean).join(" · ");
}

export function IngestionDeviceGroupCanvas({
  devices = [],
  edges = [],
  models = [],
  activeDeviceKey,
  activeEdgeId,
  onSelect,
  onAdd,
  onAddDeviceProfile,
  onAssignEdge,
  onRemove,
  children,
  probe,
  edgeError,
  canAddDevice = false,
  groupSaving = false,
  disabled = false,
}) {
  const [dropTargetKey, setDropTargetKey] = useState("");
  const [selectedProfileKind, setSelectedProfileKind] = useState(deviceProfileOptions[0]?.value || "other");
  const [selectedTargetEdgeId, setSelectedTargetEdgeId] = useState(null);
  const activeDevice = devices.find(device => device.key === activeDeviceKey) || devices[0];
  const knownEdges = useMemo(() => {
    const partitions = new Map();
    for (const rawEdge of edges) {
      const edge = normalizeEdge(rawEdge);
      if (edge && !partitions.has(edge.edgeId)) partitions.set(edge.edgeId, edge);
    }
    for (const device of devices) {
      const assignedEdgeId = String(device.form?.edgeId || "").trim();
      if (assignedEdgeId && !partitions.has(assignedEdgeId))
        partitions.set(assignedEdgeId, { edgeId: assignedEdgeId, label: assignedEdgeId, unavailable: true });
    }
    return [...partitions.values()];
  }, [devices, edges]);
  const edgePartitions = useMemo(() => knownEdges.filter(edge =>
    devices.some(device => String(device.form?.edgeId || "") === edge.edgeId)), [knownEdges, devices]);
  useEffect(() => { setSelectedTargetEdgeId(null); }, [activeDeviceKey]);
  const currentEdgeId = activeEdgeId ?? activeDevice?.form?.edgeId ?? "";
  const currentEdgeAvailable = knownEdges.some(edge => edge.edgeId === currentEdgeId && !edge.unavailable);
  const selectedEdgeId = selectedTargetEdgeId === null
    ? currentEdgeAvailable ? currentEdgeId : ""
    : selectedTargetEdgeId;
  const totalMapped = devices.reduce((sum, device) => sum + pointSummary(device.form).mapped, 0);
  const actualEdgeCount = edgePartitions.filter(edge =>
    devices.some(device => String(device.form?.edgeId || "") === edge.edgeId)).length;
  const addHandler = onAddDeviceProfile || onAdd;

  function startDeviceDrag(event, deviceKey) {
    event.dataTransfer.effectAllowed = "move";
    event.dataTransfer.setData(DEVICE_DRAG_TYPE, deviceKey);
    event.dataTransfer.setData("text/plain", `device:${deviceKey}`);
  }

  function allowDrop(event, unavailable = false) {
    if (disabled || unavailable) return;
    const canMove = event.dataTransfer.types.includes(DEVICE_DRAG_TYPE);
    if (!canMove) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = "move";
  }

  function dropOnEdge(event, edgeId, unavailable = false) {
    event.preventDefault();
    setDropTargetKey("");
    if (disabled || unavailable || !onAssignEdge) return;
    const deviceKey = event.dataTransfer.getData(DEVICE_DRAG_TYPE);
    if (deviceKey && devices.some(device => device.key === deviceKey)) onAssignEdge(deviceKey, edgeId || null);
  }

  function renderDevice(device, index) {
    const form = device.form || {};
    const descriptor = protocolDescriptor(form.protocol);
    const points = pointSummary(form);
    const isActive = device.key === activeDevice?.key;
    const status = statusFor(device, isActive, Boolean(probe?.success && probe?.mappingsValidated));
    const assignedEdgeId = String(form.edgeId || "");
    const hasUnknownEdge = Boolean(assignedEdgeId && !knownEdges.some(edge => edge.edgeId === assignedEdgeId));
    const model = models.find(item => `${item.modelId}::${item.version}` === form.dataModel);
    const modelLabel = model ? `${model.name || model.modelId} · v${model.version}`
      : form.dataModel ? String(form.dataModel).replace("::", " · v") : "工艺变量模型待选";
    const dataItems = model?.acquisition?.dataItems || [];
    const configuredMappings = (form.valueMappings || []).filter(row => row.dataItemCode || pointSource(row, form));

    return (
      <article key={device.key}
        className={`relative min-w-0 rounded-2xl border bg-white p-3 shadow-sm transition ${isActive
          ? "border-blue-500 ring-2 ring-blue-100"
          : "border-slate-200 hover:border-blue-300 hover:shadow-md"}`}>
        <button type="button" draggable={!disabled && Boolean(onAssignEdge)}
          aria-label={`选择设备 ${form.name || form.taskId || `设备 ${index + 1}`}；可拖动到另一个 Edge`}
          aria-pressed={isActive} disabled={disabled} onDragStart={event => startDeviceDrag(event, device.key)}
          onDragEnd={() => setDropTargetKey("")} onClick={() => onSelect?.(device.key)}
          className="w-full min-w-0 text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500">
          <span className="flex items-start justify-between gap-2">
            <span className="min-w-0">
              <span className="block truncate text-sm font-semibold text-slate-950">
                {form.name || form.taskId || `设备 ${index + 1}`}
              </span>
              <span className="mt-1 block truncate text-[11px] text-slate-500">{form.taskId || "任务代码待填写"}</span>
              <span className="mt-1 block truncate text-[11px] text-slate-600">
                归属 · {SUBJECT_LABELS[form.subjectType] || form.subjectType || "数据对象"} · {form.subjectId || "对象编号待填写"}
              </span>
            </span>
            <span className={`shrink-0 rounded-full px-2 py-1 text-[10px] font-semibold ${statusTone[status.tone]}`}>{status.label}</span>
          </span>
          <span className="mt-3 flex items-center gap-2 rounded-xl bg-slate-50 px-3 py-2">
            <span className="grid size-8 shrink-0 place-items-center rounded-lg bg-indigo-100 text-[10px] font-bold tracking-wide text-indigo-800">DEV</span>
            <span className="min-w-0 flex-1">
              <span className="block truncate text-xs font-semibold text-slate-800">{profileName(form)}</span>
              <span className="mt-0.5 block truncate text-[11px] text-slate-500">{descriptor.label} · {connectionSummary(form, descriptor)}</span>
            </span>
          </span>
          <span className="my-2 flex items-center gap-2 pl-3 text-[10px] font-medium text-slate-500" aria-hidden="true">
            <span className="h-4 border-l-2 border-dashed border-indigo-300" />设备点位读取并映射到工艺变量
          </span>
          <span className="flex items-center justify-between gap-2 rounded-xl border border-emerald-100 bg-emerald-50/70 px-3 py-2">
            <span className="min-w-0">
              <span className="block text-[10px] font-semibold uppercase tracking-wide text-emerald-800">采集数据 → 工艺映射</span>
              <span className="mt-0.5 block truncate text-[11px] text-slate-600">{modelLabel}</span>
            </span>
            <span className="shrink-0 text-right text-xs font-semibold text-emerald-800">{points.mapped}<span className="font-normal text-slate-500"> / {points.configured} 项</span></span>
          </span>
          <span className="mt-2 block space-y-1 rounded-xl border border-slate-200 bg-white p-2.5">
            {configuredMappings.length === 0 ? (
              <span className="block text-[11px] text-slate-500">尚无点位映射</span>
            ) : configuredMappings.slice(0, 4).map((row, mappingIndex) => {
              const source = pointSource(row, form) || "来源待填写";
              const target = row.dataItemCode
                ? dataItems.find(item => item.code === row.dataItemCode)?.name
                  ? `${row.dataItemCode} · ${dataItems.find(item => item.code === row.dataItemCode).name}`
                  : row.dataItemCode
                : "未映射";
              return <span key={`${source}-${row.dataItemCode}-${mappingIndex}`}
                className="flex min-w-0 items-center gap-2 text-[11px] leading-4">
                <code className="min-w-0 flex-1 truncate rounded bg-slate-50 px-1.5 py-1 text-slate-700" title={source}>{source}</code>
                <span className="shrink-0 text-slate-400" aria-hidden="true">→</span>
                <span className={`min-w-0 flex-1 truncate rounded px-1.5 py-1 ${row.dataItemCode ? "bg-emerald-50 font-medium text-emerald-800" : "bg-amber-50 text-amber-800"}`}
                  title={target}>{target}</span>
              </span>;
            })}
            {configuredMappings.length > 4 && <span className="block pt-0.5 text-[10px] text-slate-500">另有 {configuredMappings.length - 4} 条映射</span>}
          </span>
          {device.error && <span role="alert" className="mt-2 block break-words text-xs text-rose-700">{device.error}</span>}
        </button>

        <label className="mt-3 block border-t border-slate-100 pt-2 text-[11px] text-slate-600">
          <span className="mb-1 block font-medium">运行此采集任务的 Edge</span>
          <select aria-label={`为 ${form.name || form.taskId || `设备 ${index + 1}`} 指定 Edge`}
            value={assignedEdgeId} disabled={disabled || !onAssignEdge}
            onChange={event => onAssignEdge?.(device.key, event.target.value || null)}
            className="w-full rounded-lg border border-slate-200 bg-white px-2.5 py-2 text-xs text-slate-700 focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-100 disabled:cursor-not-allowed disabled:bg-slate-100">
            <option value="">未分配 Edge</option>
            {hasUnknownEdge && <option value={assignedEdgeId}>未知 Edge · {assignedEdgeId}</option>}
            {knownEdges.map(edge => <option key={edge.edgeId} value={edge.edgeId} disabled={edge.unavailable}>
              {edge.label}{edge.unavailable ? " · 当前不可用" : ""}
            </option>)}
          </select>
        </label>
        {isActive && edgeError && <p role="alert" className="mt-1 text-xs text-rose-700">{edgeError}</p>}
      </article>
    );
  }

  function renderEdgePartition(edge) {
    const edgeId = edge?.edgeId || null;
    const partitionKey = edgeId ? `edge:${edgeId}` : UNASSIGNED_EDGE_KEY;
    const targetKey = edgeId || UNASSIGNED_EDGE_KEY;
    const edgeDevices = devices.filter(device => String(device.form?.edgeId || "") === String(edgeId || ""));
    const dragActive = dropTargetKey === targetKey;
    const label = edge?.label || "未分配现场节点";

    return (
      <section key={partitionKey} id={edgeAnchor(edgeId)} aria-label={`${label}设备分区`}
        onDragOver={event => allowDrop(event, edge?.unavailable)}
        onDragEnter={event => {
          if (!disabled && !edge?.unavailable && event.dataTransfer.types.includes(DEVICE_DRAG_TYPE)) setDropTargetKey(targetKey);
        }}
        onDragLeave={event => {
          if (!event.currentTarget.contains(event.relatedTarget)) setDropTargetKey("");
        }}
        onDrop={event => dropOnEdge(event, edgeId, edge?.unavailable)}
        className={`rounded-2xl border p-3 transition sm:p-4 ${dragActive
          ? "border-blue-500 bg-blue-50 ring-2 ring-blue-100"
          : edge?.unavailable ? "border-amber-200 bg-amber-50/60" : "border-slate-200 bg-white"}`}>
        <div className="flex items-center justify-between gap-3 border-b border-slate-100 pb-3">
          <div className="flex min-w-0 items-center gap-3">
            <span className="grid size-9 shrink-0 place-items-center rounded-xl bg-slate-900 text-[10px] font-bold tracking-wide text-white">EDGE</span>
            <span className="min-w-0">
              <span className="block truncate text-sm font-semibold text-slate-950">{label}</span>
              <span className="mt-0.5 block truncate text-[10px] text-slate-500">{edgeId ? `节点 ID · ${edgeId}` : "任务尚未分配运行节点"}</span>
            </span>
          </div>
          <span className="shrink-0 rounded-full bg-slate-100 px-2.5 py-1 text-[10px] font-semibold text-slate-600">{edgeDevices.length} 台设备</span>
        </div>
        {edge?.unavailable && <p className="mt-2 text-xs text-amber-800">这个任务引用的 Edge 当前不在可用清单中。</p>}
        <div className="mt-3 grid grid-cols-[repeat(auto-fit,minmax(min(100%,26rem),1fr))] gap-3">
          {edgeDevices.map(device => renderDevice(device, devices.indexOf(device) + 1))}
          {edgeDevices.length === 0 && (
            <div className="grid min-h-28 place-items-center rounded-xl border border-dashed border-slate-300 bg-slate-50/60 px-4 py-5 text-center">
              <p className="max-w-sm text-xs leading-5 text-slate-500">
                {edgeId ? "这个 Edge 还没有采集设备。可以从上方选择设备类型后添加。" : "尚无未分配设备；选择设备类型后可建立草稿，再选择运行 Edge。"}
              </p>
            </div>
          )}
        </div>
        {dragActive && <p className="mt-3 rounded-lg bg-blue-100 px-3 py-2 text-center text-xs font-semibold text-blue-800" aria-live="polite">松开以分配到这个 Edge</p>}
      </section>
    );
  }

  const showUnassignedPartition = devices.some(device => !String(device.form?.edgeId || "").trim()) || edgePartitions.length === 0;

  return (
    <section aria-label="采集任务画布与设备编辑器" className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
      <header className="flex flex-wrap items-start justify-between gap-4 border-b border-slate-200 bg-gradient-to-r from-slate-50 to-white px-4 py-4 sm:px-6">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="text-base font-semibold text-slate-950">采集工作区</h2>
            {groupSaving && <span className="rounded-full bg-blue-100 px-2.5 py-1 text-[10px] font-semibold text-blue-800">逐设备保存中</span>}
          </div>
          <p className="mt-1 max-w-3xl text-xs leading-5 text-slate-600">
            Edge 执行设备通信；设备点位读取后映射到工艺变量。每台设备的连接和映射都在右侧编辑器中配置。
          </p>
          {canAddDevice && <p className="mt-1 text-[11px] text-slate-500">添加设备后，每台设备分别保存为独立草稿；保存失败的设备可单独修正后重试。</p>}
        </div>
        {canAddDevice && (
          <div className="flex w-full flex-wrap items-end gap-2 sm:w-auto">
            <label className="min-w-40 flex-1 text-[11px] font-medium text-slate-600 sm:w-48 sm:flex-none">
              <span className="mb-1 block">添加设备类型</span>
              <select value={selectedProfileKind} disabled={disabled} onChange={event => setSelectedProfileKind(event.target.value)}
                className="w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-xs text-slate-700 focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-100 disabled:bg-slate-100">
                {deviceProfileOptions.map(profile => <option key={profile.value} value={profile.value}>{profile.label}</option>)}
              </select>
            </label>
            <label className="min-w-40 flex-1 text-[11px] font-medium text-slate-600 sm:w-48 sm:flex-none">
              <span className="mb-1 block">新设备运行 Edge</span>
              <select value={selectedEdgeId} disabled={disabled} onChange={event => setSelectedTargetEdgeId(event.target.value)}
                className="w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-xs text-slate-700 focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-100 disabled:bg-slate-100">
                <option value="">未分配 Edge</option>
                {knownEdges.map(edge => <option key={edge.edgeId} value={edge.edgeId} disabled={edge.unavailable}>
                  {edge.label}{edge.unavailable ? " · 当前不可用" : ""}
                </option>)}
              </select>
            </label>
            <Button variant="primary" disabled={disabled || !addHandler}
              onClick={() => addHandler?.(selectedProfileKind, selectedEdgeId || null)}>＋ 添加设备</Button>
          </div>
        )}
      </header>

      <div className="grid items-start xl:grid-cols-[minmax(0,1.3fr)_minmax(24rem,0.7fr)]">
        <div className="min-w-0 border-b border-slate-200 bg-slate-50/70 p-4 sm:p-5 xl:border-b-0 xl:border-r">
          <div className="mb-3 flex flex-wrap items-end justify-between gap-3">
            <div>
              <h3 className="text-sm font-semibold text-slate-900">设备与 Edge 关系</h3>
              <p className="mt-1 text-[11px] text-slate-500">已分配到 {actualEdgeCount} 个 Edge · {devices.length} 台设备 · {totalMapped} 个已映射变量</p>
            </div>
            <span className="rounded-lg border border-slate-200 bg-white px-2.5 py-1.5 text-[10px] text-slate-500">卡片下拉可分配到任意可用 Edge</span>
          </div>

          {edgePartitions.length + Number(showUnassignedPartition) > 1 && (
            <nav aria-label="画布 Edge 导航" className="mb-3 flex flex-wrap gap-1.5 rounded-xl border border-slate-200 bg-white p-2">
              <span className="px-1.5 py-1 text-[10px] font-semibold text-slate-500">定位</span>
              {edgePartitions.map(edge => <button key={edge.edgeId} type="button"
                onClick={() => document.getElementById(edgeAnchor(edge.edgeId))?.scrollIntoView({ behavior: "smooth", block: "nearest" })}
                className="max-w-full truncate rounded-md bg-slate-100 px-2 py-1 text-[10px] font-medium text-slate-700 hover:bg-blue-100 hover:text-blue-800">
                {edge.label} · {devices.filter(device => String(device.form?.edgeId || "") === edge.edgeId).length}
              </button>)}
              {showUnassignedPartition && <button type="button" onClick={() => document.getElementById(UNASSIGNED_EDGE_KEY)?.scrollIntoView({ behavior: "smooth", block: "nearest" })}
                className="rounded-md bg-slate-100 px-2 py-1 text-[10px] font-medium text-slate-700 hover:bg-blue-100 hover:text-blue-800">未分配</button>
              }
            </nav>
          )}

          <div className="grid max-h-[min(68vh,54rem)] content-start gap-3 overflow-y-auto rounded-xl bg-slate-100/80 p-2.5 sm:p-3" aria-label="Edge 分区画布">
            {edgePartitions.map(renderEdgePartition)}
            {showUnassignedPartition && renderEdgePartition({ edgeId: null, label: "未分配现场节点", unavailable: false })}
          </div>
          <p className="mt-3 text-[10px] leading-4 text-slate-500">
            画布按运行 Edge 分区显示设备，不代表设备之间存在数据连线。拖动和下拉选择都可重新分配 Edge。
          </p>
        </div>

        <aside aria-label="所选设备完整配置" className="min-w-0 p-4 sm:p-5 xl:sticky xl:top-4 xl:max-h-[calc(100vh-7rem)] xl:overflow-y-auto">
          {activeDevice ? (
            <div className="mb-4 flex flex-wrap items-center justify-between gap-2 rounded-xl border border-blue-100 bg-blue-50/70 px-3 py-2.5">
              <div className="min-w-0">
                <p className="text-[10px] font-semibold uppercase tracking-wide text-blue-700">设备检查器</p>
                <p className="mt-0.5 truncate text-sm font-semibold text-slate-900">{activeDevice.form?.name || activeDevice.form?.taskId || "未命名设备"}</p>
              </div>
              {canAddDevice && onRemove && <Button variant="ghost" disabled={disabled || devices.length <= 1}
                onClick={() => onRemove(activeDevice.key)} className="text-rose-700 hover:bg-rose-100">移除设备</Button>}
            </div>
          ) : (
            <div className="mb-4 rounded-xl border border-dashed border-slate-300 px-4 py-5 text-center text-xs text-slate-500">从画布选择设备以配置采集任务。</div>
          )}
          {children}
        </aside>
      </div>
    </section>
  );
}
