// A node workspace for the real Edge, acquisition-task, model, and value-mapping references.

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  Background, BackgroundVariant, Controls, Handle, MarkerType, MiniMap, Panel, Position,
  ReactFlow, ReactFlowProvider, useNodesInitialized, useNodesState, useReactFlow,
} from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import "./IngestionTaskCanvas.css";
import { Button } from "../ui/components";
import { edgeStatus } from "../pages/shared";
import { deviceProfileOption } from "./deviceProfileRegistry";
import { isValueMappingComplete, modelValue, validateIngestionTask } from "./ingestionTaskForm";
import { protocolDescriptor } from "./protocolRegistry";

const PALETTE_MIME = "application/x-ingot-acquisition-node";
const INSPECTOR_TABS = [
  { id: "device", label: "设备" },
  { id: "connection", label: "连接" },
  { id: "mappings", label: "点位映射" },
  { id: "advanced", label: "高级" },
];
const SUBJECT_LABELS = {
  equipment: "生产设备",
  quality_instrument: "检测仪器",
  line: "生产线",
  utility_meter: "公用工程仪表",
  system: "业务系统",
};
const MAPPING_RENDER_LIMIT = 8;
const MAPPING_SEARCH_RENDER_LIMIT = 8;
const TOPIC_RENDER_LIMIT = 8;
const PROBE_POINT_RENDER_LIMIT = 8;
const EMPTY_LIST = Object.freeze([]);
const EMPTY_RECORD = Object.freeze({});
const MODBUS_AREA_LABELS = {
  "holding-register": "保持寄存器",
  "input-register": "输入寄存器",
  coil: "线圈",
  "discrete-input": "离散输入",
};

const nodeTypes = {
  edge: EdgeCanvasNode,
  task: AcquisitionTaskNode,
  model: ProcessModelNode,
  topic: MqttTopicNode,
  mapping: ValueMappingNode,
  probePoint: UnmappedProbePointNode,
};

function edgeNodeId(edgeId) {
  return `edge:${encodeURIComponent(edgeId)}`;
}

function taskNodeId(taskKey) {
  return `task:${encodeURIComponent(taskKey)}`;
}

function modelNodeId(value) {
  return `model:${encodeURIComponent(value)}`;
}

function topicNodeId(taskKey, topicIndex) {
  return `topic:${encodeURIComponent(taskKey)}:${topicIndex}`;
}

function mappingNodeId(taskKey, mappingIndex) {
  return `mapping:${encodeURIComponent(taskKey)}:${mappingIndex}`;
}

function probePointNodeId(taskKey, pointIndex) {
  return `probe:${encodeURIComponent(taskKey)}:${pointIndex}`;
}

function normalizeEdge(edge) {
  if (!edge?.edgeId) return null;
  const edgeId = String(edge.edgeId);
  return {
    edgeId,
    label: edge.displayName || edge.hostname || edgeId,
    healthStatus: edgeStatus(edge),
    unavailable: false,
  };
}

function connectionSummary(form, descriptor) {
  const connection = form?.[descriptor.section] || {};
  const endpoint = connection.host || connection.baseUrl || connection.endpointUrl || connection.portName || "端点待填写";
  const port = connection.port ? `:${connection.port}` : "";
  return `${endpoint}${port}`;
}

function pointSource(row, form) {
  const descriptor = protocolDescriptor(form?.protocol);
  if (descriptor.addressing === "modbus-register" && row.modbusAddress !== "" && row.modbusAddress != null)
    return `${row.modbusArea || "holding-register"}:${row.modbusAddress}${row.bitIndex === "" || row.bitIndex == null ? "" : `.${row.bitIndex}`}:${row.sourceDataType}`;
  if (descriptor.addressing === "melsec-device" && row.melsecAddress !== "" && row.melsecAddress != null)
    return `${row.melsecDevice || "D"}:${row.melsecAddress}${row.bitIndex === "" || row.bitIndex == null ? "" : `.${row.bitIndex}`}:${row.sourceDataType}`;
  return String(row.sourcePath || "").trim();
}

function pointAddress(row, form) {
  const descriptor = protocolDescriptor(form?.protocol);
  if (descriptor.addressing === "modbus-register" && row.modbusAddress !== "" && row.modbusAddress != null) {
    const area = MODBUS_AREA_LABELS[row.modbusArea] || row.modbusArea || "寄存器";
    const bit = row.bitIndex === "" || row.bitIndex == null ? "" : `.${row.bitIndex}`;
    return `${area} ${row.modbusAddress}${bit}`;
  }
  if (descriptor.addressing === "melsec-device" && row.melsecAddress !== "" && row.melsecAddress != null) {
    const bit = row.bitIndex === "" || row.bitIndex == null ? "" : `.${row.bitIndex}`;
    return `${row.melsecDevice || "D"}${row.melsecAddress}${bit}`;
  }
  return String(row.sourcePath || "").trim();
}

function pointSummary(form) {
  const rows = (form?.valueMappings || []).filter(row => row.dataItemCode || pointSource(row, form));
  return { configured: rows.length, mapped: rows.filter(row => row.dataItemCode).length };
}

function connectionStatus(device, active, probePassed) {
  if (device.validationFailed) return { label: "需要修正", tone: "error" };
  if (device.error) return { label: "保存失败", tone: "error" };
  if (device.form?.status === "published") return { label: "配置已发布", tone: "success" };
  if (device.form?.status === "retired") return { label: "已停用", tone: "idle" };
  if (device.saved) return { label: probePassed ? "已保存 · 本会话验证" : "已保存草稿", tone: "success" };
  if (probePassed) return { label: "本会话探查通过", tone: "success" };
  const points = pointSummary(device.form);
  if (points.mapped > 0) return { label: "待探查", tone: "working" };
  return active ? { label: "配置中", tone: "working" } : { label: "待配置", tone: "idle" };
}

function profileName(form) {
  const profile = deviceProfileOption(form?.deviceProfile?.kind);
  return [profile.label, form?.deviceProfile?.manufacturer, form?.deviceProfile?.model].filter(Boolean).join(" · ");
}

function EdgeCanvasNode({ data, selected }) {
  const healthLabels = { online: "在线", offline: "心跳过期", degraded: "运行异常", unknown: "状态未知" };
  const healthTone = data.healthStatus === "online" ? "is-ok" : data.healthStatus === "degraded" ? "is-error" : "is-idle";
  return (
    <article className={`acq-flow-node acq-flow-node--edge ${selected ? "is-selected" : ""} ${data.unavailable ? "is-unavailable" : ""}`}>
      <Handle type="source" position={Position.Right} id="edge-out" isConnectable={!data.unavailable} aria-label="连接到采集任务" />
      <div className="acq-flow-node__eyebrow"><span>部署位置</span><span className={`acq-flow-node__state ${data.unavailable ? "is-error" : healthTone}`}>{data.unavailable ? "未登记" : `已登记 · ${healthLabels[data.healthStatus] || "状态未知"}`}</span></div>
      <h3 className="acq-flow-node__title">{data.label}</h3>
      <p className="acq-flow-node__meta">Edge ID · {data.edgeId}</p>
      <p className="acq-flow-node__description">部署 {data.taskCount} 个采集任务</p>
      {data.unavailable && <p className="acq-flow-node__alert">此 Edge 当前未在站点登记清单中</p>}
      <span className="acq-flow-node__port-label acq-flow-node__port-label--right">部署目标</span>
    </article>
  );
}

function AcquisitionTaskNode({ data, selected }) {
  const status = connectionStatus(data.device, data.active, data.probePassed);
  const form = data.device.form || {};
  const descriptor = protocolDescriptor(form.protocol);
  const profile = profileName(form);
  const points = pointSummary(form);

  return (
    <article className={`acq-flow-node acq-flow-node--task ${selected ? "is-selected" : ""}`}>
      {data.showAssociatedResources && <Handle type="target" position={Position.Left} id="edge-in" aria-label="由 Edge 部署" />}
      {data.showAssociatedResources && <Handle type="source" position={Position.Right} id="model-out" aria-label="引用工艺模型" />}
      <Handle type="source" position={Position.Bottom} id="mapping-out" isConnectable={false} aria-label="点位映射支路来源" />
      <div className="acq-flow-node__eyebrow"><span>采集任务</span><span className={`acq-flow-node__state is-${status.tone}`}>{status.label}</span></div>
      <h3 className="acq-flow-node__title">{form.name || form.taskId || "新采集任务"}</h3>
      <p className="acq-flow-node__meta">{form.taskId || "任务编号待填写"} · {profile}</p>
      <div className="acq-flow-node__divider" />
      <p className="acq-flow-node__line"><span>驱动</span><strong>{descriptor.label}</strong></p>
      <p className="acq-flow-node__line"><span>端点</span><strong title={connectionSummary(form, descriptor)}>{connectionSummary(form, descriptor)}</strong></p>
      <p className="acq-flow-node__line"><span>运行于 Edge</span><strong>{data.edgeLabel || "未分配"}</strong></p>
      <p className="acq-flow-node__line"><span>归属</span><strong>{SUBJECT_LABELS[form.subjectType] || form.subjectType || "数据对象"} · {form.subjectId || "待填写"}</strong></p>
      <p className="acq-flow-node__line"><span>工艺模型</span><strong>{data.modelLabel || "尚未引用模型"}</strong></p>
      <div className="acq-flow-node__mapping-heading">
        <span>点位映射支路</span><strong>{points.mapped}/{points.configured}</strong>
      </div>
      <p className="acq-flow-node__empty-mapping">{points.configured ? "源点位与目标变量详情见映射支路" : "尚未配置变量映射"}</p>
      {data.device.error && <p className="acq-flow-node__alert" role="alert">{data.device.error}</p>}
      {data.device.validationFailed && data.device.validationTab && <p className="acq-flow-node__alert" role="status">校验错误显示在检查器的“{data.validationTabLabel}”页签</p>}
      {data.showAssociatedResources && <span className="acq-flow-node__port-label acq-flow-node__port-label--left">运行于 Edge</span>}
      {data.showAssociatedResources && <span className="acq-flow-node__port-label acq-flow-node__port-label--right">引用模型</span>}
    </article>
  );
}

function MqttTopicNode({ data, selected }) {
  return (
    <article className={`acq-flow-node acq-flow-node--topic ${selected ? "is-selected" : ""}`}>
      <Handle type="target" position={Position.Top} id="topic-in" isConnectable={false} aria-label="来自采集任务的 MQTT 订阅" />
      <Handle type="source" position={Position.Bottom} id="topic-out" isConnectable={false} aria-label="连接到绑定此主题的点位" />
      <div className="acq-flow-node__eyebrow"><span>MQTT 订阅主题</span><span className="acq-flow-node__state is-working">{data.pointCount} 个点位</span></div>
      <h3 className="acq-flow-node__title" title={data.topic}>{data.topic}</h3>
      {data.channel && <p className="acq-flow-node__meta">通道 · {data.channel}</p>}
      <p className="acq-flow-node__description">{data.qos != null ? `QoS ${data.qos}` : "按配置订阅"} · 仅连接实际绑定此主题的点位</p>
      <span className="acq-flow-node__port-label acq-flow-node__port-label--left">订阅</span>
      <span className="acq-flow-node__port-label acq-flow-node__port-label--right">对应点位</span>
    </article>
  );
}

function ValueMappingNode({ data, selected }) {
  const status = data.complete ? "已配置"
    : !data.source ? "待补来源"
      : !data.targetCode ? "待选变量" : "目标待确认";
  const displayStatus = !data.complete && data.topicLabel && !data.topicBound ? "主题未配置" : status;
  const tone = data.complete ? "is-success" : "is-error";
  return (
    <article className={`acq-flow-node acq-flow-node--mapping ${selected ? "is-selected" : ""} ${data.complete ? "" : "is-incomplete"}`}>
      <Handle type="target" position={Position.Top} id="mapping-in" isConnectable={false} aria-label="映射来源" />
      <div className="acq-flow-node__eyebrow"><span>ValueMapping · 第 {data.index + 1} 行</span><span className={`acq-flow-node__state ${tone}`}>{displayStatus}</span></div>
      <h3 className={`acq-flow-node__point-address ${data.source ? "" : "is-missing"}`} title={data.source || "来源地址待填写"}>
        {data.source || "来源地址待填写"}
      </h3>
      <p className="acq-flow-node__point-target" title={data.targetLabel}>{data.targetLabel || "目标变量待填写"}{data.targetCode && <code>{data.targetCode}</code>}</p>
      <div className="acq-flow-node__mapping-meta">
        <span>{data.sourceType || "类型待填写"}{data.sourceUnit ? ` · 源 ${data.sourceUnit}` : data.targetUnit ? ` · ${data.targetUnit}` : ""}{data.sourceUnit && data.targetUnit && data.sourceUnit !== data.targetUnit ? ` → ${data.targetUnit}` : ""}</span>
        <span>× {data.scale} {data.offset >= 0 ? "+" : "−"} {Math.abs(data.offset)}</span>
      </div>
      {data.topicLabel && <p className={`acq-flow-node__mapping-topic ${data.topicBound ? "" : "is-missing"}`}>
        {data.topicBound ? `主题 · ${data.topicLabel}` : `主题未匹配订阅 · ${data.topicLabel}`}
      </p>}
      {data.isMqtt && !data.topicLabel && <p className="acq-flow-node__mapping-topic is-unbound">未绑定订阅主题（按任意主题解析）</p>}
      {data.modelMessage && <p className="acq-flow-node__mapping-topic is-missing">{data.modelMessage}</p>}
    </article>
  );
}

function UnmappedProbePointNode({ data, selected }) {
  return (
    <article className={`acq-flow-node acq-flow-node--probe ${selected ? "is-selected" : ""} ${data.unsupported ? "is-unavailable" : ""}`}>
      <Handle type="target" position={Position.Top} id="probe-in" isConnectable={false} aria-label="探查发现的设备点位来源" />
      <div className="acq-flow-node__eyebrow"><span>探查点位 · 待映射</span>
        <span className={`acq-flow-node__state ${data.unsupported ? "is-error" : "is-working"}`}>{data.unsupported ? "不支持映射" : "可映射"}</span>
      </div>
      <h3 className="acq-flow-node__mapping-target" title={data.name}>{data.name || data.path || "未命名点位"}</h3>
      <p className="acq-flow-node__mapping-source" title={data.path}>{data.path || "来源路径未返回"}</p>
      <div className="acq-flow-node__mapping-meta"><span>{data.dataType || "类型未知"}{data.unit ? ` · ${data.unit}` : ""}</span>
        {data.stale && <span className="is-stale">清单已过期</span>}
      </div>
      {data.topic && <p className="acq-flow-node__mapping-topic">MQTT · {data.topic}</p>}
    </article>
  );
}

function ProcessModelNode({ data, selected }) {
  return (
    <article className={`acq-flow-node acq-flow-node--model ${selected ? "is-selected" : ""}`}>
      <Handle type="target" position={Position.Left} id="model-in" aria-label="采集任务引用模型" />
      <div className="acq-flow-node__eyebrow"><span>版本化工艺模型</span><span className={`acq-flow-node__state ${data.published ? "is-ok" : "is-error"}`}>{data.published ? "已发布" : "不可用"}</span></div>
      <h3 className="acq-flow-node__title">{data.name}</h3>
      <p className="acq-flow-node__meta">{data.modelId} · v{data.version}</p>
      <div className="acq-flow-node__divider" />
      <p className="acq-flow-node__description">{data.dataItemCount} 个工艺变量 · {data.requiredCount} 个必需项</p>
      <p className="acq-flow-node__description">变量映射保存在采集任务中</p>
      <span className="acq-flow-node__port-label acq-flow-node__port-label--left">被任务引用</span>
    </article>
  );
}

function PaletteItem({ children, icon, description, disabled, dragData, onClick, active = false }) {
  return (
    <button type="button" draggable={!disabled && Boolean(dragData)}
      onDragStart={event => {
        if (!dragData) return;
        event.dataTransfer.effectAllowed = "copy";
        event.dataTransfer.setData(PALETTE_MIME, JSON.stringify(dragData));
      }} onClick={onClick} disabled={disabled}
      className={`acq-palette-item ${active ? "is-active" : ""}`}>
      <span className="acq-palette-item__icon" aria-hidden="true">{icon}</span>
      <span className="acq-palette-item__text"><strong>{children}</strong>{description && <small>{description}</small>}</span>
      <span className="acq-palette-item__action">{active ? "已添加" : "＋"}</span>
    </button>
  );
}

function CanvasWorkspace({
  devices = EMPTY_LIST, edges = EMPTY_LIST, models = EMPTY_LIST, activeDeviceKey = "", activeInspectorTab = "device", onInspectorTabChange, onSelect, onAddDevice,
  onAssignEdge, onAssignModel, onRemove, inspectorPanels = EMPTY_RECORD, probe, edgeError, canAddDevice, disabled, id,
  onFocusMappingIssues, onFocusMappingRow, onFocusProbePoint,
}) {
  const { screenToFlowPosition, fitView } = useReactFlow();
  const nodesInitialized = useNodesInitialized();
  const [visibleEdgeIds, setVisibleEdgeIds] = useState([]);
  const [visibleModelValues, setVisibleModelValues] = useState([]);
  const [showAssociatedResources, setShowAssociatedResources] = useState(false);
  const [editorWideView, setEditorWideView] = useState(false);
  const [nodePositions, setNodePositions] = useState({});
  const [paletteSearch, setPaletteSearch] = useState("");
  const [mappingSearch, setMappingSearch] = useState("");
  const [mappingStatus, setMappingStatus] = useState("all");
  const [mappingTaskFilter, setMappingTaskFilter] = useState(activeDeviceKey || "all");
  const [mappingPage, setMappingPage] = useState(0);
  const [probePage, setProbePage] = useState(0);
  const [selectedGraphNodeId, setSelectedGraphNodeId] = useState("");
  const fitAllAfterTopologyChange = useRef(false);
  const fitAfterLayoutReset = useRef(false);
  const [contextMenu, setContextMenu] = useState(null);
  const contextMenuRef = useRef(null);
  const dropzoneRef = useRef(null);
  const inspectorTabRefs = useRef({});
  const activeDevice = devices.find(device => device.key === activeDeviceKey) || devices[0];
  const defaultTaskKey = activeDeviceKey || devices[0]?.key || "";
  const canvasDevices = useMemo(() => mappingTaskFilter === "all"
    ? devices
    : devices.filter(device => device.key === mappingTaskFilter), [devices, mappingTaskFilter]);
  const canvasEdgeIds = useMemo(() => new Set(canvasDevices.map(device => String(device.form?.edgeId || "").trim()).filter(Boolean)), [canvasDevices]);
  const canvasModelValues = useMemo(() => new Set(canvasDevices.map(device => String(device.form?.dataModel || "")).filter(Boolean)), [canvasDevices]);
  const canvasModelReferences = useMemo(() => showAssociatedResources
    ? [...new Set([...visibleModelValues, ...canvasModelValues])]
    : [], [canvasModelValues, showAssociatedResources, visibleModelValues]);

  function handleInspectorTabKeyDown(event, index) {
    let nextIndex;
    if (event.key === "ArrowRight") nextIndex = (index + 1) % INSPECTOR_TABS.length;
    else if (event.key === "ArrowLeft") nextIndex = (index - 1 + INSPECTOR_TABS.length) % INSPECTOR_TABS.length;
    else if (event.key === "Home") nextIndex = 0;
    else if (event.key === "End") nextIndex = INSPECTOR_TABS.length - 1;
    else return;

    event.preventDefault();
    const nextTab = INSPECTOR_TABS[nextIndex];
    onInspectorTabChange?.(nextTab.id);
    inspectorTabRefs.current[nextTab.id]?.focus();
  }

  useEffect(() => {
    if (activeDeviceKey) setMappingTaskFilter(activeDeviceKey);
  }, [activeDeviceKey]);

  useEffect(() => {
    if (mappingTaskFilter !== "all" && !devices.some(device => device.key === mappingTaskFilter))
      setMappingTaskFilter(activeDeviceKey && devices.some(device => device.key === activeDeviceKey) ? activeDeviceKey : "all");
  }, [activeDeviceKey, devices, mappingTaskFilter]);

  const knownEdges = useMemo(() => {
    const byId = new Map();
    for (const rawEdge of edges) {
      const edge = normalizeEdge(rawEdge);
      if (edge && !byId.has(edge.edgeId)) byId.set(edge.edgeId, edge);
    }
    for (const device of devices) {
      const edgeId = String(device.form?.edgeId || "").trim();
      if (edgeId && !byId.has(edgeId)) byId.set(edgeId, { edgeId, label: edgeId, unavailable: true });
    }
    return [...byId.values()];
  }, [devices, edges]);
  const canvasEdges = useMemo(() => showAssociatedResources
    ? knownEdges.filter(edge => visibleEdgeIds.includes(edge.edgeId) || canvasEdgeIds.has(edge.edgeId))
    : [], [canvasEdgeIds, knownEdges, showAssociatedResources, visibleEdgeIds]);

  const publishedModels = useMemo(() => models.filter(model => model.status === "published"), [models]);
  const allModels = useMemo(() => {
    const values = new Map(models.map(model => [modelValue(model.modelId, model.version), model]));
    for (const device of devices) {
      const value = String(device.form?.dataModel || "");
      if (value && !values.has(value)) {
        const [modelId, version] = value.split("::");
        values.set(value, { modelId, version, status: "unavailable", acquisition: { dataItems: [] } });
      }
    }
    return [...values.entries()].map(([value, model]) => ({ value, model }));
  }, [devices, models]);

  const search = paletteSearch.trim().toLowerCase();
  const assignedEdgeIds = useMemo(() => devices.map(device => String(device.form?.edgeId || "").trim()).filter(Boolean), [devices]);
  const assignedModelValues = useMemo(() => devices.map(device => String(device.form?.dataModel || "")).filter(Boolean), [devices]);
  const filteredEdges = knownEdges.filter(edge => (!disabled || assignedEdgeIds.includes(edge.edgeId))
    && (!search || `${edge.label} ${edge.edgeId}`.toLowerCase().includes(search)));
  const filteredModels = publishedModels.filter(model => {
    const value = modelValue(model.modelId, model.version);
    return (!disabled || assignedModelValues.includes(value))
      && (!search || `${model.name || ""} ${model.modelId}`.toLowerCase().includes(search));
  });
  const mappingEntries = useMemo(() => devices.flatMap(device => {
    const form = device.form || {};
    const isMqtt = form.protocol === "mqtt";
    const topics = isMqtt ? (form.mqtt?.topics || []).map((topic, index) => ({
      ...topic,
      topic: String(topic.topic || "").trim(),
      index,
      id: topicNodeId(device.key, index),
      taskKey: device.key,
    })).filter(topic => topic.topic) : [];
    const modelValue = String(form.dataModel || "");
    const model = allModels.find(item => item.value === modelValue)?.model;
    const items = model?.acquisition?.dataItems || [];
    const validationErrors = validateIngestionTask(form, { dataItems: items }).errors;
    const itemByCode = new Map(items.map(item => [String(item.code || ""), item]));
    return (form.valueMappings || []).flatMap((row, index) => {
      const source = pointAddress(row, form);
      const targetCode = String(row.dataItemCode || "").trim();
      const requestedTopic = isMqtt ? String(row.topic || "").trim() : "";
      if (!source && !targetCode && !requestedTopic) return [];
      const topicEntry = requestedTopic ? topics.find(topic => topic.topic === requestedTopic) : null;
      const targetItem = targetCode ? itemByCode.get(targetCode) : null;
      const modelMessage = !targetCode ? "目标变量未填写"
        : !modelValue ? "尚未选择工艺模型，目标变量未确认"
          : !targetItem ? "目标变量不在当前模型中" : "";
      return [{
        id: mappingNodeId(device.key, index),
        taskKey: device.key,
        taskLabel: form.name || form.taskId || "采集任务",
        index,
        form,
        source,
        sourceType: String(row.sourceDataType || "").trim(),
        sourceUnit: String(row.sourceUnit || "").trim(),
        targetCode,
        targetItem,
        targetLabel: targetItem?.displayName || targetCode,
        targetUnit: String(targetItem?.unit || "").trim(),
        scale: Number.isFinite(Number(row.scale)) ? Number(row.scale) : 1,
        offset: Number.isFinite(Number(row.offset)) ? Number(row.offset) : 0,
        topicLabel: requestedTopic,
        topicEntry,
        topicBound: !requestedTopic || Boolean(topicEntry),
        isMqtt,
        modelValue,
        modelMessage,
        complete: Boolean(modelValue && targetItem && isValueMappingComplete(form, row, index, items, validationErrors)
          && (!requestedTopic || topicEntry)),
      }];
    });
  }), [allModels, devices]);
  const mappingCoverage = useMemo(() => devices.reduce((total, device) => {
    const form = device.form || {};
    const model = allModels.find(item => item.value === String(form.dataModel || ""))?.model;
    const required = (model?.acquisition?.dataItems || []).filter(item => !item.nullable);
    const mapped = new Set(mappingEntries
      .filter(entry => entry.taskKey === device.key && entry.complete)
      .map(entry => entry.targetCode));
    total.required += required.length;
    total.mappedRequired += required.filter(item => mapped.has(item.code)).length;
    return total;
  }, { required: 0, mappedRequired: 0 }), [allModels, devices, mappingEntries]);
  const incompleteMappingCount = mappingEntries.filter(entry => !entry.complete).length;
  const mappingIssuesByTask = useMemo(() => devices.flatMap(device => {
    const form = device.form || {};
    const model = allModels.find(item => item.value === String(form.dataModel || ""))?.model;
    const required = (model?.acquisition?.dataItems || []).filter(item => !item.nullable);
    const completeEntries = mappingEntries.filter(entry => entry.taskKey === device.key && entry.complete);
    const mappedRequired = new Set(completeEntries.map(entry => entry.targetCode));
    const missingRequiredCount = required.filter(item => !mappedRequired.has(item.code)).length;
    const incompleteMappingCount = mappingEntries.filter(entry => entry.taskKey === device.key && !entry.complete).length;
    return missingRequiredCount || incompleteMappingCount ? [{
      taskKey: device.key,
      taskLabel: form.name || form.taskId || "采集任务",
      missingRequiredCount,
      incompleteMappingCount,
    }] : [];
  }), [allModels, devices, mappingEntries]);
  const saveCheckFailuresByTask = useMemo(() => devices
    .filter(device => device.validationFailed)
    .map(device => {
      const form = device.form || {};
      const validationTab = INSPECTOR_TABS.find(tab => tab.id === device.validationTab) || INSPECTOR_TABS[0];
      return {
        taskKey: device.key,
        taskLabel: form.name || form.taskId || "采集任务",
        validationTab,
        detail: device.error || "最近一次保存检查未通过，请查看对应检查器页签。",
      };
    }), [devices]);

  const unmappedProbePoints = useMemo(() => {
    const activeForm = devices.find(device => device.key === activeDeviceKey)?.form || {};
    const points = probe?.points || [];
    if (!points.length) return [];
    const mappedPathsForAnyTopic = new Set();
    const mappedTopicPaths = new Set();
    (activeForm.valueMappings || []).forEach(row => {
      const sourcePath = pointSource(row, activeForm);
      if (!sourcePath) return;
      const topic = String(row.topic || "").trim();
      if (topic) mappedTopicPaths.add(`${topic}\u001f${sourcePath}`);
      else mappedPathsForAnyTopic.add(sourcePath);
    });
    const configuredTopics = new Map((activeForm.protocol === "mqtt" ? activeForm.mqtt?.topics || [] : [])
      .map((topic, index) => [String(topic.topic || "").trim(), { ...topic, index, id: topicNodeId(activeDeviceKey, index), taskKey: activeDeviceKey }]));
    return points.flatMap((point, index) => {
      const path = String(point.path || "").trim();
      if (!path || mappedPathsForAnyTopic.has(path) || mappedTopicPaths.has(`${String(point.topic || "").trim()}\u001f${path}`)) return [];
      const topic = String(point.topic || "").trim();
      return [{
        id: probePointNodeId(activeDeviceKey, index),
        taskKey: activeDeviceKey,
        index,
        point,
        name: String(point.name || "").trim(),
        path,
        topic,
        topicEntry: topic ? configuredTopics.get(topic) || null : null,
        dataType: String(point.dataType || "").trim(),
        unit: String(point.unit || "").trim(),
        unsupported: point.metadata?.supported === "false",
        stale: probe?.catalogCurrent === false,
      }];
    });
  }, [activeDeviceKey, devices, probe]);
  const probeSearch = mappingSearch.trim().toLowerCase();
  const filteredProbePoints = useMemo(() => unmappedProbePoints.filter(point => !probeSearch
    || `${point.name} ${point.path} ${point.topic} ${point.dataType} ${point.unit}`.toLowerCase().includes(probeSearch)), [probeSearch, unmappedProbePoints]);
  const probePageCount = Math.max(1, Math.ceil(filteredProbePoints.length / PROBE_POINT_RENDER_LIMIT));
  useEffect(() => setProbePage(page => Math.min(page, probePageCount - 1)), [probePageCount]);
  useEffect(() => setProbePage(0), [activeDeviceKey, mappingSearch]);
  const probePageStart = filteredProbePoints.length ? probePage * PROBE_POINT_RENDER_LIMIT + 1 : 0;
  const probePageEnd = Math.min((probePage + 1) * PROBE_POINT_RENDER_LIMIT, filteredProbePoints.length);
  const visibleProbePoints = useMemo(() => filteredProbePoints.slice(
    probePage * PROBE_POINT_RENDER_LIMIT,
    (probePage + 1) * PROBE_POINT_RENDER_LIMIT,
  ), [filteredProbePoints, probePage]);

  const filteredMappingEntries = useMemo(() => {
    const query = mappingSearch.trim().toLowerCase();
    const filtered = mappingEntries.filter(entry => {
      if (mappingTaskFilter !== "all" && mappingTaskFilter !== entry.taskKey) return false;
      if (mappingStatus === "mapped" && !entry.complete) return false;
      if (mappingStatus === "needs-attention" && entry.complete) return false;
      if (!query) return true;
      return `${entry.taskLabel} ${entry.source} ${entry.sourceType} ${entry.sourceUnit} ${entry.targetCode} ${entry.targetLabel} ${entry.topicLabel} ${entry.scale} ${entry.offset}`
        .toLowerCase().includes(query);
    });
    const byTask = new Map();
    filtered.forEach(entry => {
      const rows = byTask.get(entry.taskKey) || [];
      rows.push(entry);
      byTask.set(entry.taskKey, rows);
    });
    const taskOrder = devices.map(device => device.key);
    const fairOrder = [];
    for (let index = 0; ; index += 1) {
      let found = false;
      for (const taskKey of taskOrder) {
        const entry = byTask.get(taskKey)?.[index];
        if (entry) { fairOrder.push(entry); found = true; }
      }
      if (!found) break;
    }
    return fairOrder;
  }, [devices, mappingEntries, mappingSearch, mappingStatus, mappingTaskFilter]);
  const mappingRenderLimit = mappingSearch.trim() ? MAPPING_SEARCH_RENDER_LIMIT : MAPPING_RENDER_LIMIT;
  const mappingPageCount = Math.max(1, Math.ceil(filteredMappingEntries.length / mappingRenderLimit));
  useEffect(() => setMappingPage(page => Math.min(page, mappingPageCount - 1)), [mappingPageCount]);
  useEffect(() => setMappingPage(0), [mappingSearch, mappingStatus, mappingTaskFilter]);
  const mappingPageStart = filteredMappingEntries.length ? mappingPage * mappingRenderLimit + 1 : 0;
  const mappingPageEnd = Math.min((mappingPage + 1) * mappingRenderLimit, filteredMappingEntries.length);
  const visibleMappingEntries = useMemo(() => filteredMappingEntries.slice(
    mappingPage * mappingRenderLimit,
    (mappingPage + 1) * mappingRenderLimit,
  ), [filteredMappingEntries, mappingPage, mappingRenderLimit]);
  const mappingRowsByTask = useMemo(() => {
    const rowsByTask = new Map();
    visibleMappingEntries.forEach(entry => {
      const rows = rowsByTask.get(entry.taskKey) || [];
      rows.push(entry);
      rowsByTask.set(entry.taskKey, rows);
    });
    return rowsByTask;
  }, [visibleMappingEntries]);
  const visibleTopics = useMemo(() => {
    const topicById = new Map();
    for (const entry of visibleMappingEntries) {
      if (entry.topicEntry) topicById.set(entry.topicEntry.id, entry.topicEntry);
    }
    for (const point of visibleProbePoints) {
      if (mappingTaskFilter !== "all" && point.taskKey !== mappingTaskFilter) continue;
      if (point.topicEntry) topicById.set(point.topicEntry.id, point.topicEntry);
    }
    const referencedTopicIds = new Set(topicById.keys());
    for (const device of devices) {
      if (mappingTaskFilter !== "all" && device.key !== mappingTaskFilter) continue;
      if (device.form?.protocol !== "mqtt") continue;
      const topics = (device.form?.mqtt?.topics || []).map((topic, index) => ({
        ...topic,
        topic: String(topic.topic || "").trim(),
        index,
        id: topicNodeId(device.key, index),
        taskKey: device.key,
      })).filter(topic => topic.topic);
      const existingForTask = [...topicById.values()].filter(topic => topic.taskKey === device.key).length;
      const extraLimit = Math.max(0, TOPIC_RENDER_LIMIT - existingForTask);
      topics.filter(topic => !topicById.has(topic.id)).slice(0, extraLimit).forEach(topic => topicById.set(topic.id, topic));
    }
    return [...topicById.values()].filter(topic => referencedTopicIds.has(topic.id)
      || mappingTaskFilter === "all" || topic.taskKey === mappingTaskFilter);
  }, [devices, mappingTaskFilter, visibleMappingEntries, visibleProbePoints]);

  useEffect(() => {
    setSelectedGraphNodeId(current => {
      const encodedTaskKey = encodeURIComponent(defaultTaskKey);
      if (current.startsWith(`mapping:${encodedTaskKey}:`) || current.startsWith(`topic:${encodedTaskKey}:`) || current.startsWith(`probe:${encodedTaskKey}:`)) return current;
      return defaultTaskKey ? taskNodeId(defaultTaskKey) : "";
    });
  }, [defaultTaskKey]);

  const graphNodeDefinitions = useMemo(() => {
    const edgeTaskCounts = new Map();
    for (const device of canvasDevices) {
      const edgeId = String(device.form?.edgeId || "").trim();
      if (edgeId) edgeTaskCounts.set(edgeId, (edgeTaskCounts.get(edgeId) || 0) + 1);
    }
    const nodes = [];
    const taskLayouts = new Map();
    let cursorY = 64;
    canvasDevices.forEach(device => {
      const rows = mappingRowsByTask.get(device.key) || [];
      const topics = visibleTopics.filter(topic => topic.taskKey === device.key);
      const probeRows = device.key === activeDeviceKey ? visibleProbePoints : [];
      const form = device.form || {};
      const mqtt = form.protocol === "mqtt";
      let mappingBottom = 150;
      const topicOffsets = new Map();
      if (mqtt) {
        let topicCursor = 130;
        topics.forEach(topic => {
          const count = rows.filter(row => row.topicEntry?.id === topic.id).length;
          topicOffsets.set(topic.id, topicCursor);
          topicCursor += 110 + Math.max(1, Math.ceil(count / 2)) * 120;
        });
        const unboundCount = rows.filter(row => !row.topicEntry).length;
        const unboundBottom = unboundCount ? topicCursor + Math.ceil(unboundCount / 2) * 120 : 0;
        mappingBottom = Math.max(topicCursor, unboundBottom);
      } else {
        mappingBottom = Math.max(190, 150 + Math.ceil(rows.length / 2) * 120);
      }
      const probeStartOffset = mappingBottom + 78;
      const branchBottom = probeRows.length
        ? probeStartOffset + Math.ceil(probeRows.length / 2) * 120
        : mappingBottom;
      const laneHeight = Math.max(390, branchBottom + 72);
      taskLayouts.set(device.key, { y: cursorY, laneHeight, rows, topics, topicOffsets, probeRows, probeStartOffset });
      cursorY += laneHeight;
    });
    const firstTaskYByEdge = new Map();
    for (const device of canvasDevices) {
      const edgeId = String(device.form?.edgeId || "").trim();
      if (!edgeId) continue;
      const y = taskLayouts.get(device.key)?.y ?? 64;
      firstTaskYByEdge.set(edgeId, Math.min(firstTaskYByEdge.get(edgeId) ?? y, y));
    }
    canvasEdges.forEach((edge, index) => nodes.push({
      id: edgeNodeId(edge.edgeId),
      type: "edge",
      position: { x: 30, y: firstTaskYByEdge.get(edge.edgeId) ?? 64 + index * 180 },
      data: { ...edge, taskCount: edgeTaskCounts.get(edge.edgeId) || 0 },
    }));
    canvasDevices.forEach(device => {
      const form = device.form || {};
      const modelEntry = allModels.find(item => item.value === String(form.dataModel || ""));
      const model = modelEntry?.model;
      const layout = taskLayouts.get(device.key);
      nodes.push({
        id: taskNodeId(device.key),
        type: "task",
        position: { x: 390, y: layout?.y ?? 64 },
        data: {
          device,
          active: device.key === activeDeviceKey,
          probePassed: Boolean(device.probeVerified ||
            (device.key === activeDeviceKey && probe?.success && probe?.mappingsValidated)),
          edgeLabel: knownEdges.find(edge => edge.edgeId === String(form.edgeId || ""))?.label || "",
          modelLabel: model ? `${model.name || model.modelId} · v${model.version}` : "",
          showAssociatedResources,
          validationTabLabel: INSPECTOR_TABS.find(tab => tab.id === device.validationTab)?.label || "设备",
        },
      });
    });
    const modelY = new Map();
    for (const device of canvasDevices) {
      const value = String(device.form?.dataModel || "");
      if (!value || !canvasModelReferences.includes(value)) continue;
      const ys = modelY.get(value) || [];
      ys.push(taskLayouts.get(device.key)?.y ?? 64);
      modelY.set(value, ys);
    }
    visibleTopics.forEach(topic => {
      const layout = taskLayouts.get(topic.taskKey);
      if (!layout) return;
      const rows = layout.rows.filter(row => row.topicEntry?.id === topic.id);
      const probeCount = visibleProbePoints.filter(point => point.topicEntry?.id === topic.id).length;
      nodes.push({
        id: topic.id,
        type: "topic",
        position: { x: 760, y: layout.y + (layout.topicOffsets.get(topic.id) ?? 130) },
        data: { ...topic, pointCount: rows.length + probeCount },
      });
    });
    visibleMappingEntries.forEach(entry => {
      const layout = taskLayouts.get(entry.taskKey);
      if (!layout) return;
      const rows = layout.rows;
      const pageIndex = Math.max(0, rows.findIndex(row => row.id === entry.id));
      let position;
      if (entry.isMqtt && entry.topicEntry) {
        const topicRows = rows.filter(row => row.topicEntry?.id === entry.topicEntry.id);
        const pointIndex = topicRows.findIndex(row => row.id === entry.id);
        position = {
          x: 960 + (pointIndex % 2) * 220,
          y: layout.y + (layout.topicOffsets.get(entry.topicEntry.id) ?? 130) + 112 + Math.floor(pointIndex / 2) * 120,
        };
      } else if (entry.isMqtt) {
        const unboundRows = rows.filter(row => !row.topicEntry);
        const pointIndex = unboundRows.findIndex(row => row.id === entry.id);
        position = {
          x: 760 + (pointIndex % 2) * 220,
          y: layout.y + 130 + layout.topics.reduce((sum, topic) => {
            const count = rows.filter(row => row.topicEntry?.id === topic.id).length;
            return sum + 110 + Math.max(1, Math.ceil(count / 2)) * 120;
          }, 0) + Math.floor(pointIndex / 2) * 120,
        };
      } else {
        position = { x: 760 + (pageIndex % 2) * 220, y: layout.y + 150 + Math.floor(pageIndex / 2) * 120 };
      }
      nodes.push({ id: entry.id, type: "mapping", position, data: entry });
    });
    visibleProbePoints.forEach((point, index) => {
      const layout = taskLayouts.get(point.taskKey);
      if (!layout) return;
      nodes.push({
        id: point.id,
        type: "probePoint",
        position: {
          x: 760 + (index % 2) * 220,
          y: layout.y + layout.probeStartOffset + Math.floor(index / 2) * 120,
        },
        data: point,
      });
    });
    canvasModelReferences.forEach((value, index) => {
      const entry = allModels.find(item => item.value === value);
      if (!entry) return;
      const model = entry.model;
      const items = model.acquisition?.dataItems || [];
      const referencedY = modelY.get(value) || [];
      const y = referencedY.length ? Math.min(...referencedY) - 160 - index * 165 : -100 - index * 165;
      nodes.push({
        id: modelNodeId(value),
        type: "model",
        position: { x: 760, y },
        data: {
          value,
          modelId: model.modelId,
          version: model.version,
          name: model.name || model.modelId,
          published: model.status === "published",
          dataItemCount: items.length,
          requiredCount: items.filter(item => !item.nullable).length,
        },
      });
    });
    return nodes;
  }, [activeDeviceKey, allModels, canvasDevices, canvasEdges, canvasModelReferences, devices, knownEdges, mappingRowsByTask, probe, showAssociatedResources, visibleMappingEntries, visibleProbePoints, visibleTopics]);

  const graphTopology = graphNodeDefinitions.map(node => node.id).join("|");
  const [nodes, setNodes, onNodesChange] = useNodesState([]);
  const renderedNodeTopology = nodes.map(node => node.id).join("|");
  useEffect(() => {
    setNodes(current => {
      const oldNodes = new Map(current.map(node => [node.id, node]));
      return graphNodeDefinitions.map(node => {
        const previous = oldNodes.get(node.id);
        return {
          ...previous,
          ...node,
          position: nodePositions[node.id] || node.position,
          selected: node.id === selectedGraphNodeId,
        };
      });
    });
  }, [graphNodeDefinitions, nodePositions, selectedGraphNodeId, setNodes]);

  useEffect(() => {
    if (!fitAfterLayoutReset.current || !nodesInitialized) return undefined;
    let settleFrame = 0;
    const fitFrame = requestAnimationFrame(() => {
      settleFrame = requestAnimationFrame(() => {
        fitAfterLayoutReset.current = false;
        void fitView({
          nodes: graphNodeDefinitions.map(node => ({ id: node.id })),
          padding: 0.08,
          maxZoom: 1.08,
          duration: 180,
        });
      });
    });
    return () => {
      cancelAnimationFrame(fitFrame);
      if (settleFrame) cancelAnimationFrame(settleFrame);
    };
  }, [fitView, graphNodeDefinitions, nodePositions, nodesInitialized]);

  useEffect(() => {
    if (!graphTopology || graphTopology !== renderedNodeTopology || !nodesInitialized) return undefined;
    const graphNodeIds = graphTopology.split("|").filter(Boolean);
    const pointFlowNodes = graphNodeIds
      .filter(id => ["task:", "topic:", "mapping:", "probe:"].some(prefix => id.startsWith(prefix)))
      .map(id => ({ id }));
    let settleFrame = 0;
    const fitFrame = requestAnimationFrame(() => {
      settleFrame = requestAnimationFrame(() => {
        const fitAllNodes = fitAllAfterTopologyChange.current;
        fitAllAfterTopologyChange.current = false;
        void fitView({
          nodes: fitAllNodes ? graphNodeIds.map(id => ({ id }))
            : pointFlowNodes.length ? pointFlowNodes : graphNodeIds.map(id => ({ id })),
          padding: fitAllNodes ? 0.08 : 0.045,
          maxZoom: 1.08,
          duration: 180,
        });
      });
    });
    return () => {
      cancelAnimationFrame(fitFrame);
      if (settleFrame) cancelAnimationFrame(settleFrame);
    };
  }, [fitView, graphTopology, nodesInitialized, renderedNodeTopology]);

  const graphEdges = useMemo(() => {
    const result = [];
    for (const device of canvasDevices) {
      const form = device.form || {};
      const edgeId = String(form.edgeId || "").trim();
      if (edgeId && canvasEdges.some(edge => edge.edgeId === edgeId)) {
        const unavailable = knownEdges.find(edge => edge.edgeId === edgeId)?.unavailable;
        result.push({
          id: `deploy:${encodeURIComponent(edgeId)}:${encodeURIComponent(device.key)}`,
          source: edgeNodeId(edgeId),
          sourceHandle: "edge-out",
          target: taskNodeId(device.key),
          targetHandle: "edge-in",
          label: "部署到",
          type: "smoothstep",
          markerEnd: { type: MarkerType.ArrowClosed, color: unavailable ? "#e11d48" : "#2563eb" },
          style: { stroke: unavailable ? "#e11d48" : "#2563eb", strokeWidth: 2, strokeDasharray: unavailable ? "5 4" : undefined },
          labelStyle: { fill: unavailable ? "#be123c" : "#1d4ed8", fontSize: 10, fontWeight: 600 },
          labelBgStyle: { fill: "#fff", fillOpacity: 0.92 },
          selectable: false,
          reconnectable: false,
        });
      }
      const modelValueForTask = String(form.dataModel || "");
      if (modelValueForTask && canvasModelReferences.includes(modelValueForTask)) {
        result.push({
          id: `model-ref:${encodeURIComponent(device.key)}:${encodeURIComponent(modelValueForTask)}`,
          source: taskNodeId(device.key),
          sourceHandle: "model-out",
          target: modelNodeId(modelValueForTask),
          targetHandle: "model-in",
          label: "引用版本",
          type: "smoothstep",
          markerEnd: { type: MarkerType.ArrowClosed, color: "#059669" },
          style: { stroke: "#059669", strokeWidth: 2 },
          labelStyle: { fill: "#047857", fontSize: 10, fontWeight: 600 },
          labelBgStyle: { fill: "#fff", fillOpacity: 0.92 },
          selectable: false,
          reconnectable: false,
        });
      }
    }
    for (const topic of visibleTopics) {
      result.push({
        id: `mqtt-subscribe:${topic.id}`,
        source: taskNodeId(topic.taskKey),
        sourceHandle: "mapping-out",
        target: topic.id,
        targetHandle: "topic-in",
        label: "订阅主题",
        type: "smoothstep",
        markerEnd: { type: MarkerType.ArrowClosed, color: "#7c3aed" },
        style: { stroke: "#7c3aed", strokeWidth: 1.8 },
        labelStyle: { fill: "#6d28d9", fontSize: 9, fontWeight: 600 },
        labelBgStyle: { fill: "#fff", fillOpacity: 0.94 },
        selectable: false,
        reconnectable: false,
      });
    }
    for (const entry of visibleMappingEntries) {
      const boundTopic = entry.topicEntry && visibleTopics.some(topic => topic.id === entry.topicEntry.id);
      result.push({
        id: `mapping-source:${entry.id}`,
        source: boundTopic ? entry.topicEntry.id : taskNodeId(entry.taskKey),
        sourceHandle: boundTopic ? "topic-out" : "mapping-out",
        target: entry.id,
        targetHandle: "mapping-in",
        type: "smoothstep",
        markerEnd: { type: MarkerType.ArrowClosed, color: boundTopic ? "#7c3aed" : "#0f766e" },
        style: { stroke: boundTopic ? "#7c3aed" : "#0f766e", strokeWidth: 1.6, strokeDasharray: boundTopic ? undefined : "5 4" },
        selectable: false,
        reconnectable: false,
      });
    }
    for (const point of visibleProbePoints) {
      const boundTopic = point.topicEntry && visibleTopics.some(topic => topic.id === point.topicEntry.id);
      result.push({
        id: `probe-source:${point.id}`,
        source: boundTopic ? point.topicEntry.id : taskNodeId(point.taskKey),
        sourceHandle: boundTopic ? "topic-out" : "mapping-out",
        target: point.id,
        targetHandle: "probe-in",
        type: "smoothstep",
        markerEnd: { type: MarkerType.ArrowClosed, color: boundTopic ? "#7c3aed" : "#d97706" },
        style: { stroke: boundTopic ? "#7c3aed" : "#d97706", strokeWidth: 1.5, strokeDasharray: "5 4" },
        selectable: false,
        reconnectable: false,
      });
    }
    return result;
  }, [canvasDevices, canvasEdges, canvasModelReferences, devices, knownEdges, visibleMappingEntries, visibleProbePoints, visibleTopics]);

  const selectedNode = nodes.find(node => node.id === selectedGraphNodeId);
  const selectedTaskKey = selectedNode?.type === "task"
    ? selectedNode.data.device.key
    : selectedNode?.type === "mapping" || selectedNode?.type === "topic" || selectedNode?.type === "probePoint" ? selectedNode.data.taskKey : "";
  const selectedTaskDevice = selectedTaskKey ? devices.find(device => device.key === selectedTaskKey) : null;
  const inspectedDevice = selectedTaskDevice || activeDevice;
  const selectedEdge = selectedNode?.type === "edge"
    ? knownEdges.find(edge => edge.edgeId === selectedNode.data.edgeId)
    : null;
  const selectedModel = selectedNode?.type === "model"
    ? allModels.find(item => item.value === selectedNode.data.value)?.model
    : null;
  const contextNode = contextMenu?.node || null;
  const contextTaskKey = contextNode?.type === "task" ? contextNode.data.device.key
    : ["mapping", "topic", "probePoint"].includes(contextNode?.type) ? contextNode.data.taskKey : "";
  const contextTaskDevice = contextTaskKey ? devices.find(device => device.key === contextTaskKey) : null;
  const contextAssignmentDevice = contextTaskDevice || selectedTaskDevice || activeDevice;

  function openTaskInspector(taskKey, tab, nodeId = taskNodeId(taskKey), focusTarget = null) {
    setMappingTaskFilter(taskKey);
    if (focusTarget?.type === "mapping" && onFocusMappingRow) {
      onFocusMappingRow(taskKey, focusTarget.index);
      setEditorWideView(true);
    } else if (focusTarget?.type === "probePoint" && onFocusProbePoint) {
      onFocusProbePoint(taskKey, focusTarget.point);
      setEditorWideView(true);
    } else {
      onSelect?.(taskKey);
      onInspectorTabChange?.(tab);
    }
    setSelectedGraphNodeId(nodeId);
    setContextMenu(null);
  }

  function openCanvasContextMenu(event, node = null) {
    event.preventDefault();
    event.stopPropagation();
    const bounds = dropzoneRef.current?.getBoundingClientRect();
    if (!bounds) return;
    const width = 240;
    const estimatedHeight = !node ? (canAddDevice && !disabled ? 112 : 76) : node.type === "task" ? 232 : 168;
    const left = Math.max(8, Math.min(event.clientX - bounds.left, bounds.width - width - 8));
    const top = Math.max(8, Math.min(event.clientY - bounds.top, bounds.height - estimatedHeight - 8));
    setSelectedGraphNodeId(node?.id || "");
    setContextMenu({
      node: node ? { id: node.id, type: node.type, data: node.data } : null,
      left,
      top,
      flowPosition: screenToFlowPosition({ x: event.clientX, y: event.clientY }),
    });
  }

  function fitCurrentFlow() {
    const flowNodes = nodes.filter(node => ["task", "topic", "mapping", "probePoint"].includes(node.type));
    void fitView({ nodes: flowNodes.length ? flowNodes : nodes, padding: 0.045, maxZoom: 1.08, duration: 180 });
    setContextMenu(null);
  }

  function fitAllGraph() {
    void fitView({ nodes, padding: 0.08, maxZoom: 1.08, duration: 180 });
    setContextMenu(null);
  }

  function resetAutoLayout() {
    fitAfterLayoutReset.current = true;
    setNodePositions({});
    setContextMenu(null);
  }

  useEffect(() => {
    if (!contextMenu) return undefined;
    contextMenuRef.current?.querySelector('[role="menuitem"]:not(:disabled)')?.focus();
    const dismissOutside = event => {
      if (!contextMenuRef.current?.contains(event.target)) setContextMenu(null);
    };
    const dismissOnEscape = event => {
      if (event.key === "Escape") setContextMenu(null);
    };
    document.addEventListener("pointerdown", dismissOutside, true);
    document.addEventListener("keydown", dismissOnEscape);
    return () => {
      document.removeEventListener("pointerdown", dismissOutside, true);
      document.removeEventListener("keydown", dismissOnEscape);
    };
  }, [contextMenu]);

  const addVisibleEdge = (edgeId, position = null) => {
    if (!showAssociatedResources || !canvasEdges.some(edge => edge.edgeId === edgeId))
      fitAllAfterTopologyChange.current = true;
    setShowAssociatedResources(true);
    setVisibleEdgeIds(current => [...new Set([...current, edgeId])]);
    setSelectedGraphNodeId(edgeNodeId(edgeId));
    if (position) setNodePositions(current => ({ ...current, [edgeNodeId(edgeId)]: position }));
  };

  const addVisibleModel = (value, position = null) => {
    if (!showAssociatedResources || !canvasModelReferences.includes(value))
      fitAllAfterTopologyChange.current = true;
    setShowAssociatedResources(true);
    setVisibleModelValues(current => [...new Set([...current, value])]);
    setSelectedGraphNodeId(modelNodeId(value));
    if (position) setNodePositions(current => ({ ...current, [modelNodeId(value)]: position }));
  };

  function toggleAssociatedResources() {
    const next = !showAssociatedResources;
    fitAllAfterTopologyChange.current = next;
    setShowAssociatedResources(next);
    if (!next && (selectedGraphNodeId.startsWith("edge:") || selectedGraphNodeId.startsWith("model:")))
      setSelectedGraphNodeId(defaultTaskKey ? taskNodeId(defaultTaskKey) : "");
  }

  function addTask(kind = activeDevice?.form?.deviceProfile?.kind || "other", edgeId = activeDevice?.form?.edgeId || null, position = null) {
    setContextMenu(null);
    const deviceKey = onAddDevice?.(kind, edgeId);
    if (!deviceKey) return;
    const id = taskNodeId(deviceKey);
    setMappingTaskFilter(deviceKey);
    setSelectedGraphNodeId(id);
    if (position) setNodePositions(current => ({ ...current, [id]: position }));
  }

  const isValidConnection = useCallback(connection => {
    if (disabled || !connection.source || !connection.target) return false;
    const source = nodes.find(node => node.id === connection.source);
    const target = nodes.find(node => node.id === connection.target);
    if (!source || !target) return false;
    return (source.type === "edge" && target.type === "task" && source.data.unavailable !== true
      && connection.sourceHandle === "edge-out" && connection.targetHandle === "edge-in")
      || (source.type === "task" && target.type === "model" && target.data.published === true
        && connection.sourceHandle === "model-out" && connection.targetHandle === "model-in");
  }, [disabled, nodes]);

  const onConnect = useCallback(connection => {
    if (!isValidConnection(connection)) return;
    const source = nodes.find(node => node.id === connection.source);
    const target = nodes.find(node => node.id === connection.target);
    if (source?.type === "edge" && target?.type === "task") {
      const taskKey = target.data.device.key;
      setSelectedGraphNodeId(target.id);
      onSelect?.(taskKey);
      onAssignEdge?.(taskKey, source.data.edgeId);
    } else if (source?.type === "task" && target?.type === "model") {
      const taskKey = source.data.device.key;
      setSelectedGraphNodeId(source.id);
      onSelect?.(taskKey);
      onAssignModel?.(taskKey, target.data.value);
    }
  }, [isValidConnection, nodes, onAssignEdge, onAssignModel, onSelect]);

  function handlePaletteDrop(event) {
    event.preventDefault();
    const encoded = event.dataTransfer.getData(PALETTE_MIME);
    if (!encoded) return;
    let payload;
    try { payload = JSON.parse(encoded); }
    catch { return; }
    const position = screenToFlowPosition({ x: event.clientX, y: event.clientY });
    if (payload.kind === "edge" && knownEdges.some(edge => edge.edgeId === payload.edgeId)) {
      addVisibleEdge(payload.edgeId, position);
    } else if (payload.kind === "model" && publishedModels.some(model => modelValue(model.modelId, model.version) === payload.value)) {
      addVisibleModel(payload.value, position);
    } else if (payload.kind === "task" && canAddDevice && onAddDevice) {
      addTask(payload.deviceKind || activeDevice?.form?.deviceProfile?.kind || "other", payload.edgeId || activeDevice?.form?.edgeId || null, position);
    }
  }

  function connectFromPalette(node) {
    if (node.type === "task") {
      setMappingTaskFilter(node.data.device.key);
      onSelect?.(node.data.device.key);
      setSelectedGraphNodeId(node.id);
    } else if (node.type === "mapping" || node.type === "topic" || node.type === "probePoint") {
      const taskKey = node.data.taskKey;
      setMappingTaskFilter(taskKey);
      if (node.type === "mapping" && onFocusMappingRow) {
        onFocusMappingRow(taskKey, node.data.index);
        setEditorWideView(true);
      } else if (node.type === "probePoint" && onFocusProbePoint) {
        onFocusProbePoint(taskKey, node.data.point);
        setEditorWideView(true);
      } else {
        onSelect?.(taskKey);
        if (node.type === "mapping") onInspectorTabChange?.("mappings");
        if (node.type === "probePoint") onInspectorTabChange?.("connection");
      }
      setSelectedGraphNodeId(node.id);
    } else setSelectedGraphNodeId(node.id);
  }

  const canvasMinHeight = Math.max(520, 480 + Math.max(0, devices.length - 1) * 350);

  return (
    <section id={id} className="acq-workbench" aria-label="采集任务节点工作区"
      style={{ "--acq-canvas-min-height": `${canvasMinHeight}px` }}>
      <div className={`acq-workbench__layout ${editorWideView ? "is-editor-wide" : ""}`}>
        <aside className="acq-palette" aria-label="节点面板">
          <div className="acq-palette__heading"><div><span className="acq-palette__kicker">WORKSPACE</span><h2>节点面板</h2></div><span className="acq-palette__tip">{disabled ? "当前配置只读" : "点击或拖入画布"}</span></div>
          <label className="acq-palette__search"><span className="sr-only">筛选节点</span><span aria-hidden="true">⌕</span><input value={paletteSearch} onChange={event => setPaletteSearch(event.target.value)} placeholder="搜索 Edge 或模型" /></label>

          {canAddDevice && <section className="acq-palette__section" aria-labelledby="palette-tasks-title">
            <div className="acq-palette__section-heading"><h3 id="palette-tasks-title">采集任务</h3></div>
            <PaletteItem icon="＋" description="新增一个独立采集任务" disabled={disabled}
              dragData={{ kind: "task", deviceKind: activeDevice?.form?.deviceProfile?.kind || "other", edgeId: activeDevice?.form?.edgeId || "" }}
              onClick={() => addTask()}>
              添加采集任务
            </PaletteItem>
          </section>}

          <section className="acq-palette__section" aria-labelledby="palette-edges-title">
            <div className="acq-palette__section-heading"><h3 id="palette-edges-title">现场 Edge</h3><span>{filteredEdges.length}</span></div>
            {filteredEdges.length === 0 && <p className="acq-palette__empty">没有可用 Edge 记录。</p>}
            {filteredEdges.map(edge => <PaletteItem key={edge.edgeId} icon="E" description={edge.edgeId}
                active={canvasEdges.some(item => item.edgeId === edge.edgeId)} disabled={disabled || edge.unavailable}
              dragData={{ kind: "edge", edgeId: edge.edgeId }} onClick={() => addVisibleEdge(edge.edgeId)}>
              {edge.label}{edge.unavailable ? " · 未登记" : ` · ${({ online: "在线", offline: "心跳过期", degraded: "运行异常", unknown: "状态未知" })[edge.healthStatus] || "状态未知"}`}
            </PaletteItem>)}
          </section>

          <section className="acq-palette__section" aria-labelledby="palette-models-title">
            <div className="acq-palette__section-heading"><h3 id="palette-models-title">工艺模型</h3><span>{filteredModels.length}</span></div>
            {filteredModels.length === 0 && <p className="acq-palette__empty">没有匹配的已发布模型。</p>}
            {filteredModels.map(model => {
              const value = modelValue(model.modelId, model.version);
              return <PaletteItem key={value} icon="M" description={`${model.modelId} · v${model.version}`}
                active={canvasModelReferences.includes(value)} disabled={disabled}
                dragData={{ kind: "model", value }} onClick={() => addVisibleModel(value)}>
                {model.name || model.modelId}
              </PaletteItem>;
            })}
          </section>

        </aside>

        <main className="acq-canvas" aria-label="Edge、采集任务、点位映射与工艺模型画布">
          <div className="acq-canvas__toolbar">
            <div><strong>关系画布</strong><span>用于查看部署关系与定位缺项；批量编辑请使用右侧点位表格。连线不代表执行流。</span></div>
            <div className="acq-canvas__summary">
              <strong>{devices.length} 个采集任务</strong>
              <span>{mappingCoverage.required > 0 ? `全部任务必需变量 ${mappingCoverage.mappedRequired}/${mappingCoverage.required} 已映射` : "尚无可统计的运行必需变量"}</span>
              <span>{mappingCoverage.required === 0
                ? "选择有效工艺模型后可统计必需项覆盖"
                : incompleteMappingCount || mappingCoverage.required > mappingCoverage.mappedRequired
                  ? `${mappingCoverage.required - mappingCoverage.mappedRequired} 个必需项未映射 · ${incompleteMappingCount} 条映射行需补全`
                  : "必需项覆盖，已配置映射均有来源与目标"}</span>
              {mappingIssuesByTask.length > 0 && <details className="acq-canvas__issue-list">
                <summary>查看映射异常任务 {mappingIssuesByTask.length} 项</summary>
                <ul>
                  {mappingIssuesByTask.map(issue => <li key={issue.taskKey}>
                    <button type="button" onClick={() => {
                      setMappingTaskFilter(issue.taskKey);
                      setMappingStatus("needs-attention");
                      setMappingSearch("");
                      setSelectedGraphNodeId(taskNodeId(issue.taskKey));
                      onSelect?.(issue.taskKey);
                      onInspectorTabChange?.("mappings");
                      onFocusMappingIssues?.(issue.taskKey);
                    }}>
                      <strong>{issue.taskLabel}</strong>
                      <span>{issue.missingRequiredCount ? `${issue.missingRequiredCount} 个必需项未映射` : ""}
                        {issue.missingRequiredCount && issue.incompleteMappingCount ? " · " : ""}
                        {issue.incompleteMappingCount ? `${issue.incompleteMappingCount} 条映射行需补全` : ""}</span>
                    </button>
                  </li>)}
                </ul>
              </details>}
              {saveCheckFailuresByTask.length > 0 && <details className="acq-canvas__issue-list">
                <summary>最近一次保存检查未通过 {saveCheckFailuresByTask.length} 个任务</summary>
                <p>列表显示最近一次检查未通过且尚未修改的任务；点击后定位到对应检查器页签。</p>
                <ul>
                  {saveCheckFailuresByTask.map(issue => <li key={issue.taskKey}>
                    <button type="button" onClick={() => openTaskInspector(issue.taskKey, issue.validationTab.id)}>
                      <strong>{issue.taskLabel}</strong>
                      <span>查看“{issue.validationTab.label}”页 · {issue.detail}</span>
                    </button>
                  </li>)}
                </ul>
              </details>}
            </div>
            <div className="acq-canvas__actions">
              <button type="button" className={`acq-canvas__resource-toggle ${showAssociatedResources ? "is-active" : ""}`}
                aria-pressed={showAssociatedResources} onClick={toggleAssociatedResources}
                disabled={!canvasEdgeIds.size && !canvasModelValues.size && !visibleEdgeIds.length && !visibleModelValues.length}>
                {showAssociatedResources ? "隐藏关联 Edge / 模型" : "显示关联 Edge / 模型"}
              </button>
              <button type="button" onClick={fitCurrentFlow} disabled={nodes.length === 0} title="适配任务与当前页点位支路">适配点位</button>
              <button type="button" onClick={fitAllGraph} disabled={nodes.length === 0} title="适配画布当前显示的全部节点">适配全图</button>
              <button type="button" onClick={resetAutoLayout} disabled={nodes.length === 0} title="恢复自动生成的节点位置，不修改采集配置">重置自动布局</button>
            </div>
            <span className="acq-canvas__shortcut">布局仅保留在当前编辑 · 刷新后恢复自动布局 · 右键更多操作</span>
          </div>
          <div className="acq-canvas__mapping-tools" aria-label="点位映射支路筛选">
            <label className="acq-canvas__mapping-search">
              <span aria-hidden="true">⌕</span><span className="sr-only">搜索映射点位</span>
              <input value={mappingSearch} onChange={event => setMappingSearch(event.target.value)}
                placeholder="搜索映射 / 探查点的地址、变量或主题" />
            </label>
            <label className="acq-canvas__mapping-filter">
              <span>任务</span><select value={mappingTaskFilter} onChange={event => {
                const taskKey = event.target.value;
                setMappingTaskFilter(taskKey);
                if (taskKey !== "all") onSelect?.(taskKey);
              }}>
                <option value="all">所有任务</option>
                {devices.map(device => <option key={device.key} value={device.key}>{device.form?.name || device.form?.taskId || "采集任务"}</option>)}
              </select>
            </label>
            <label className="acq-canvas__mapping-filter">
              <span>状态</span><select value={mappingStatus} onChange={event => setMappingStatus(event.target.value)}>
                <option value="all">全部</option><option value="mapped">映射完整</option><option value="needs-attention">需补全</option>
              </select>
            </label>
            <span className="acq-canvas__mapping-count">
              显示 {mappingPageStart}–{mappingPageEnd}/{filteredMappingEntries.length} 条 · 共 {mappingEntries.length} 条
            </span>
            <nav className="acq-canvas__mapping-pages" aria-label="点位映射分页">
              <button type="button" aria-label="点位映射：上一批" disabled={mappingPage <= 0} onClick={() => setMappingPage(page => Math.max(0, page - 1))}>上一批</button>
              <span>{mappingPage + 1}/{mappingPageCount}</span>
              <button type="button" aria-label="点位映射：下一批" disabled={mappingPage >= mappingPageCount - 1}
                onClick={() => setMappingPage(page => Math.min(mappingPageCount - 1, page + 1))}>下一批</button>
            </nav>
          </div>
          {unmappedProbePoints.length > 0 && <div className="acq-canvas__probe-tools" aria-label="未映射探查点位分页">
            <strong>待映射探查点位 · {activeDevice?.form?.name || activeDevice?.form?.taskId || "当前任务"}</strong>
            <span>显示 {probePageStart}–{probePageEnd}/{filteredProbePoints.length} 条 · 共 {unmappedProbePoints.length} 条</span>
            {probe?.catalogCurrent === false && <span className="is-stale">来自旧探查清单</span>}
            <nav className="acq-canvas__mapping-pages" aria-label="未映射探查点位分页">
              <button type="button" aria-label="未映射探查点位：上一批" disabled={probePage <= 0} onClick={() => setProbePage(page => Math.max(0, page - 1))}>上一批</button>
              <span>{probePage + 1}/{probePageCount}</span>
              <button type="button" aria-label="未映射探查点位：下一批" disabled={probePage >= probePageCount - 1}
                onClick={() => setProbePage(page => Math.min(probePageCount - 1, page + 1))}>下一批</button>
            </nav>
            <small>点选支路可回到该任务的连接与点位区。</small>
          </div>}
          {probe?.points?.length === 0 && <div className="acq-canvas__probe-empty">当前设备的探查没有返回点位。</div>}
          <div ref={dropzoneRef} className="acq-canvas__dropzone" onDragOver={event => {
            if (event.dataTransfer.types.includes(PALETTE_MIME)) { event.preventDefault(); event.dataTransfer.dropEffect = "copy"; }
          }} onDrop={handlePaletteDrop}>
            <ReactFlow
              nodes={nodes}
              edges={graphEdges}
              nodeTypes={nodeTypes}
              zoomOnScroll
              onNodesChange={onNodesChange}
              onNodeClick={(_event, node) => { setContextMenu(null); connectFromPalette(node); }}
              onNodeContextMenu={(event, node) => openCanvasContextMenu(event, node)}
              onPaneContextMenu={event => openCanvasContextMenu(event)}
              onPaneClick={() => { setContextMenu(null); setSelectedGraphNodeId(""); }}
              onNodeDragStop={(_event, node) => setNodePositions(current => ({ ...current, [node.id]: node.position }))}
              isValidConnection={isValidConnection}
              onConnect={onConnect}
              nodesDraggable={!disabled}
              nodesConnectable={!disabled}
              elementsSelectable
              deleteKeyCode={null}
              edgesReconnectable={false}
              minZoom={0.2}
              maxZoom={1.6}
              proOptions={{ hideAttribution: true }}
              aria-label="交互式采集任务关系画布"
            >
              <Background variant={BackgroundVariant.Dots} gap={22} size={1} color="#cbd5e1" />
              <Controls showInteractive={false} position="bottom-left" />
              <MiniMap position="bottom-right" pannable zoomable nodeColor={node => {
                if (node.type === "edge") return "#3b82f6";
                if (node.type === "model") return "#10b981";
                if (node.type === "topic") return "#8b5cf6";
                if (node.type === "mapping") return "#f59e0b";
                if (node.type === "probePoint") return "#0ea5e9";
                return "#475569";
              }} />
              <Panel position="top-left" className="acq-canvas__legend">
                {showAssociatedResources && <span><i className="is-edge" /> Edge</span>}<span><i className="is-task" /> 采集任务</span>
                <span><i className="is-topic" /> MQTT 主题</span><span><i className="is-mapping" /> 点位映射</span>
                {unmappedProbePoints.length > 0 && <span><i className="is-probe" /> 待映射点位</span>}{showAssociatedResources && <span><i className="is-model" /> 工艺模型</span>}
              </Panel>
              {nodes.length === 0 && <Panel position="top-center" className="acq-canvas__empty">
                <strong>画布尚无节点</strong><span>从左侧添加一个 Edge 或工艺模型。</span>
              </Panel>}
              {mappingEntries.length === 0 && unmappedProbePoints.length === 0 && probe?.points?.length !== 0 && <Panel position="top-center" className="acq-canvas__mapping-empty">
                <strong>还没有配置点位映射</strong><span>选择采集任务，在右侧“点位映射”中添加源点位和目标变量。</span>
              </Panel>}
            </ReactFlow>
            {contextMenu && <div ref={contextMenuRef} className="acq-canvas__context-menu" role="menu" aria-label="画布右键菜单"
              style={{ left: contextMenu.left, top: contextMenu.top }} onContextMenu={event => event.preventDefault()}
              onPointerDown={event => event.stopPropagation()}>
              <div className="acq-canvas__context-heading" role="presentation">
                <span>{contextNode ? (contextNode.type === "task" ? "采集任务"
                  : contextNode.type === "mapping" ? "设备点位"
                    : contextNode.type === "probePoint" ? "探查点位"
                      : contextNode.type === "topic" ? "MQTT 主题"
                        : contextNode.type === "edge" ? "现场 Edge" : "工艺模型") : "画布操作"}</span>
                <strong title={contextNode?.type === "task" ? contextNode.data.device.form?.name
                  : contextNode?.type === "mapping" ? contextNode.data.source
                    : contextNode?.type === "probePoint" ? contextNode.data.path
                      : contextNode?.type === "topic" ? contextNode.data.topic
                        : contextNode?.type === "edge" ? contextNode.data.label
                          : contextNode?.type === "model" ? contextNode.data.name : undefined}>
                  {contextNode?.type === "task" ? contextNode.data.device.form?.name || contextNode.data.device.form?.taskId
                    : contextNode?.type === "mapping" ? contextNode.data.source || contextNode.data.targetLabel
                      : contextNode?.type === "probePoint" ? contextNode.data.name || contextNode.data.path
                        : contextNode?.type === "topic" ? contextNode.data.topic
                          : contextNode?.type === "edge" ? contextNode.data.label
                            : contextNode?.type === "model" ? contextNode.data.name : "在此处调整画布或添加任务"}
                </strong>
              </div>
              {contextNode?.type === "task" && <>
                <button type="button" role="menuitem" onClick={() => openTaskInspector(contextNode.data.device.key, "device", contextNode.id)}>设备信息</button>
                <button type="button" role="menuitem" onClick={() => openTaskInspector(contextNode.data.device.key, "connection", contextNode.id)}>通信驱动与连接</button>
                <button type="button" role="menuitem" onClick={() => openTaskInspector(contextNode.data.device.key, "mappings", contextNode.id)}>点位映射</button>
                <button type="button" role="menuitem" onClick={() => openTaskInspector(contextNode.data.device.key, "advanced", contextNode.id)}>高级参数</button>
              </>}
              {contextNode?.type === "mapping" && <button type="button" role="menuitem"
                onClick={() => openTaskInspector(contextNode.data.taskKey, "mappings", contextNode.id,
                  { type: "mapping", index: contextNode.data.index })}>编辑此点位映射</button>}
              {contextNode?.type === "probePoint" && <button type="button" role="menuitem"
                onClick={() => openTaskInspector(contextNode.data.taskKey, "connection", contextNode.id,
                  { type: "probePoint", point: contextNode.data.point })}>查看探查点位与连接</button>}
              {contextNode?.type === "topic" && <button type="button" role="menuitem"
                onClick={() => openTaskInspector(contextNode.data.taskKey, "connection", contextNode.id)}>查看 MQTT 订阅配置</button>}
              {contextNode?.type === "edge" && <>
                <button type="button" role="menuitem" onClick={() => setContextMenu(null)}>查看 Edge 属性</button>
                {contextAssignmentDevice && <button type="button" role="menuitem" disabled={disabled || contextNode.data.unavailable}
                  onClick={() => {
                    onAssignEdge?.(contextAssignmentDevice.key, contextNode.data.edgeId);
                    setSelectedGraphNodeId(taskNodeId(contextAssignmentDevice.key));
                    setContextMenu(null);
                  }}>分配给当前任务</button>}
              </>}
              {contextNode?.type === "model" && <>
                <button type="button" role="menuitem" onClick={() => setContextMenu(null)}>查看模型属性</button>
                {contextAssignmentDevice && <button type="button" role="menuitem" disabled={disabled || !contextNode.data.published}
                  onClick={() => {
                    onAssignModel?.(contextAssignmentDevice.key, contextNode.data.value);
                    setSelectedGraphNodeId(taskNodeId(contextAssignmentDevice.key));
                    setContextMenu(null);
                  }}>应用到当前任务</button>}
              </>}
              {!contextNode && canAddDevice && <button type="button" role="menuitem" disabled={disabled}
                onClick={() => addTask(undefined, undefined, contextMenu.flowPosition)}>在此处添加采集任务</button>}
              {(!contextNode || ["edge", "model"].includes(contextNode.type)) && <div className="acq-canvas__context-divider" role="separator" />}
              <button type="button" role="menuitem" onClick={fitCurrentFlow}>适配任务与点位</button>
            </div>}
          </div>
        </main>

        <aside className="acq-inspector" aria-label="对象检查器">
          <div className="acq-inspector__heading"><div><span>INSPECTOR</span><h2>检查器</h2></div>
            <div className="flex flex-wrap items-center gap-1">
              {selectedTaskDevice && (activeInspectorTab === "mappings" || (activeInspectorTab === "connection" && probe?.points?.length) || editorWideView) && <Button variant="ghost" aria-pressed={editorWideView}
                onClick={() => setEditorWideView(value => !value)} className="text-xs text-blue-700 hover:bg-blue-50">
                {editorWideView ? "返回关系画布" : "展开点位表格"}
              </Button>}
              {selectedTaskDevice && canAddDevice && onRemove && <Button variant="ghost" disabled={disabled || devices.length <= 1}
                onClick={() => onRemove(selectedTaskDevice.key)} className="text-xs text-rose-700 hover:bg-rose-50">移除任务</Button>}
            </div>
          </div>
          {selectedTaskDevice ? (
            <>
              <div className="acq-inspector__object"><span className="acq-inspector__object-icon">TASK</span><div><strong>{selectedTaskDevice.form?.name || selectedTaskDevice.form?.taskId || "采集任务"}</strong><small>{selectedTaskDevice.form?.taskId || "任务编号待填写"}</small></div></div>
              <div role="tablist" aria-orientation="horizontal" aria-label="采集任务配置页签" className="acq-inspector__tabs">
                {INSPECTOR_TABS.map((tab, index) => <button key={tab.id} type="button" role="tab"
                  ref={element => { if (element) inspectorTabRefs.current[tab.id] = element; else delete inspectorTabRefs.current[tab.id]; }}
                  id={`acquisition-tab-${tab.id}`} aria-controls="acquisition-tabpanel" aria-selected={activeInspectorTab === tab.id}
                  tabIndex={activeInspectorTab === tab.id ? 0 : -1}
                  onClick={() => onInspectorTabChange?.(tab.id)} onKeyDown={event => handleInspectorTabKeyDown(event, index)}
                  className={activeInspectorTab === tab.id ? "is-active" : ""}>{tab.label}</button>)}
              </div>
              <div key={selectedTaskDevice.key} id="acquisition-tabpanel" role="tabpanel" aria-labelledby={`acquisition-tab-${activeInspectorTab}`} className="acq-inspector__panel">
                {inspectorPanels[activeInspectorTab]}
              </div>
            </>
          ) : selectedEdge ? (
            <div className="acq-inspector__reference">
              <span className="acq-inspector__object-icon is-edge">EDGE</span><h3>{selectedEdge.label}</h3><p>{selectedEdge.edgeId}</p>
              <div className="acq-inspector__reference-card"><strong>部署关系</strong><span>{selectedEdge.unavailable ? "该 Edge 当前不可用。" : "采集任务通过 EdgeId 引用此运行位置。"}</span></div>
              {inspectedDevice && <Button variant="primary" disabled={disabled || selectedEdge.unavailable} onClick={() => onAssignEdge?.(inspectedDevice.key, selectedEdge.edgeId)}>
                分配给当前任务 · {inspectedDevice.form?.name || inspectedDevice.form?.taskId}
              </Button>}
            </div>
          ) : selectedModel ? (
            <div className="acq-inspector__reference">
              <span className="acq-inspector__object-icon is-model">MODEL</span><h3>{selectedModel.name || selectedModel.modelId}</h3><p>{selectedModel.modelId} · v{selectedModel.version}</p>
              <div className="acq-inspector__reference-card"><strong>模型版本内容</strong><span>{(selectedModel.acquisition?.dataItems || []).length} 个采集变量；每个任务保存自己的源点位映射。</span></div>
              {inspectedDevice && <Button variant="primary" disabled={disabled || selectedModel.status !== "published"}
                onClick={() => onAssignModel?.(inspectedDevice.key, modelValue(selectedModel.modelId, selectedModel.version))}>
                用于当前任务 · {inspectedDevice.form?.name || inspectedDevice.form?.taskId}
              </Button>}
            </div>
          ) : <div className="acq-inspector__empty"><span>◇</span><strong>选择画布对象</strong><p>选择采集任务编辑完整配置，或选择 Edge、模型查看其实际引用信息。</p></div>}
          {edgeError && selectedTaskDevice && <p role="alert" className="acq-inspector__error">{edgeError}</p>}
        </aside>
      </div>
    </section>
  );
}

export function IngestionDeviceGroupCanvas({ canvasInstanceKey, ...props }) {
  return <ReactFlowProvider key={canvasInstanceKey || "default"}><CanvasWorkspace {...props} /></ReactFlowProvider>;
}
