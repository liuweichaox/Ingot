// Edit common topology-node properties through the same form state used by the full acquisition configuration.

import { Fragment, useMemo, useState } from "react";
import { Button, Field, Input, Select, Textarea } from "../ui/components";
import { modelValue } from "./ingestionTaskForm";
import { mqttTopicError, protocolOptionGroups } from "./protocolRegistry";
import { deviceProfileOption, deviceProfileOptions } from "./deviceProfileRegistry";
import { ProbePointMappingCanvas } from "./ProbePointMappingCanvas";

const subjectTypeLabels = {
  equipment: "生产设备",
  quality_instrument: "检测仪器",
  line: "生产线",
  utility_meter: "公用工程仪表",
  system: "业务系统",
};

function DescriptorConnectionEditor({ descriptor, connection, errors, readOnly, onChange }) {
  const groups = [];
  descriptor.fields.forEach(field => {
    if (field.when && !field.when(connection)) return;
    const groupName = typeof field.group === "function" ? field.group(connection) : field.group;
    const name = groupName || (field.type === "textarea" ? "高级参数" : "常用参数");
    const group = groups.find(item => item.name === name);
    if (group) group.fields.push(field);
    else groups.push({ name, fields: [field] });
  });

  return (
    <div className="grid gap-3">
      {groups.map(group => {
        const fields = group.fields.map(field => ({
          field,
          label: typeof field.label === "function" ? field.label(connection) : field.label,
          hint: typeof field.hint === "function" ? field.hint(connection) : field.hint,
          options: typeof field.options === "function" ? field.options(connection) : field.options,
          error: errors[field.name] || "",
        }));
        const alwaysVisible = ["常用参数", "目标设备"].includes(group.name);
        const hasError = fields.some(item => item.error);
        const contents = (
          <div className="grid gap-3">
            {fields.map(({ field, label, hint, options, error }) => {
              const value = connection[field.name] ?? (field.type === "checkbox" ? false : "");
              const ariaLabel = `画布 ${label}`;
              return (
                <Field key={field.name} label={label} hint={hint} hintVisible error={error}>
                  {field.type === "select" ? (
                    <Select aria-label={ariaLabel} value={value} disabled={readOnly}
                      onChange={event => onChange(field.name, event.target.value)}>
                      {(options || []).map(([optionValue, optionLabel]) => (
                        <option key={optionValue} value={optionValue}>{optionLabel}</option>
                      ))}
                    </Select>
                  ) : field.type === "checkbox" ? (
                    <span className="flex min-h-9 items-center gap-2 rounded-md border border-slate-300 bg-white px-3 text-sm font-normal text-slate-700">
                      <input type="checkbox" aria-label={ariaLabel} checked={Boolean(value)} disabled={readOnly}
                        onChange={event => onChange(field.name, event.target.checked)} />
                      {value ? "已启用" : "未启用"}
                    </span>
                  ) : field.type === "textarea" ? (
                    <Textarea aria-label={ariaLabel} value={value} placeholder={field.placeholder} disabled={readOnly}
                      onChange={event => onChange(field.name, event.target.value)} />
                  ) : (
                    <Input aria-label={ariaLabel} type={field.type === "number" ? "number" : "text"}
                      min={field.min} max={field.max} value={value} placeholder={field.placeholder} disabled={readOnly}
                      onChange={event => onChange(field.name, event.target.value)} />
                  )}
                </Field>
              );
            })}
          </div>
        );

        if (alwaysVisible) return <section key={group.name} className="grid gap-3">{contents}</section>;
        return (
          <details key={group.name} className="rounded-md border border-slate-200 bg-white px-3 py-2" open={hasError || undefined}>
            <summary className="cursor-pointer py-1 text-xs font-semibold tracking-wide text-slate-600">{group.name}</summary>
            <div className="pb-1 pt-2">{contents}</div>
          </details>
        );
      })}
    </div>
  );
}

function MqttTopicEditor({ topics, errors, readOnly, onChange }) {
  const update = (index, patch) => onChange(topics.map((topic, rowIndex) =>
    rowIndex === index ? { ...topic, ...patch } : topic));

  return (
    <section className="grid gap-3 border-t border-slate-200 pt-3">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h4 className="text-xs font-semibold uppercase tracking-wide text-slate-600">MQTT 订阅主题</h4>
          <p className="mt-1 text-xs leading-5 text-slate-500">在画布中添加或编辑主题、通道、QoS、报文根路径和主题变量。</p>
        </div>
        {!readOnly && <Button onClick={() => onChange([...topics, {
          channel: "", topic: "", qos: 0, payloadRoot: "", topicVariables: "",
        }])}>添加主题</Button>}
      </div>
      {errors.topics && <p className="text-xs text-rose-600" role="alert">{errors.topics}</p>}
      {topics.length === 0 && <p className="rounded-md bg-amber-50 px-3 py-2 text-xs text-amber-800">尚未配置订阅主题；添加至少一个主题后才能保存。</p>}
      {topics.map((topic, index) => {
        const topicError = errors[`topics[${index}].topic`] ||
          ((topic.topic || "").trim() ? mqttTopicError(topic.topic.trim()) : "");
        return (
          <div key={index} className="grid gap-2 rounded-lg border border-cyan-200 bg-white p-3">
            <div className="flex items-center justify-between gap-2">
              <p className="text-xs font-semibold text-slate-600">主题 {index + 1}</p>
              {!readOnly && <Button variant="ghost" className="text-rose-700"
                onClick={() => onChange(topics.filter((_item, rowIndex) => rowIndex !== index))}>移除</Button>}
            </div>
            <Field label="通道代码" error={errors[`topics[${index}].channel`]}>
              <Input aria-label={`画布 MQTT 通道 ${index + 1}`} value={topic.channel || ""} disabled={readOnly}
                placeholder="telemetry" onChange={event => update(index, { channel: event.target.value })} />
            </Field>
            <Field label="主题过滤器" hint="支持 + 和 # 通配符；+ 必须独占一层，# 只能在最后一层。" hintVisible error={topicError}>
              <Input aria-label={`画布 MQTT 主题 ${index + 1}`} value={topic.topic || ""} disabled={readOnly}
                placeholder="plant/line1/press01/telemetry" onChange={event => update(index, { topic: event.target.value })} />
            </Field>
            <Field label="QoS">
              <Select aria-label={`画布 MQTT QoS ${index + 1}`} value={topic.qos ?? 0} disabled={readOnly}
                onChange={event => update(index, { qos: Number(event.target.value) })}>
                <option value="0">0</option><option value="1">1</option><option value="2">2</option>
              </Select>
            </Field>
            <Field label="报文根路径" hint="报文包含信封时填写，例如 payload；留空表示报文根即数据。" hintVisible>
              <Input aria-label={`画布 MQTT 报文根 ${index + 1}`} value={topic.payloadRoot || ""} disabled={readOnly}
                placeholder="留空表示报文根" onChange={event => update(index, { payloadRoot: event.target.value })} />
            </Field>
            <Field label="主题变量" hint="name:层级，多个用逗号分隔；映射时使用 $topic.name。" hintVisible
              error={errors[`topics[${index}].topicVariables`]}>
              <Input aria-label={`画布 MQTT 主题变量 ${index + 1}`} value={topic.topicVariables || ""} disabled={readOnly}
                placeholder="equipment:1" onChange={event => update(index, { topicVariables: event.target.value })} />
            </Field>
          </div>
        );
      })}
    </section>
  );
}

function connectionSummary(form, descriptor) {
  const value = form[descriptor.section] || {};
  switch (descriptor.id) {
    case "http-polling":
      return `${value.baseUrl || "未填写服务地址"}${value.snapshotPath || ""} · ${(value.method || "get").toUpperCase()} · ${value.pollIntervalMs || "—"} ms`;
    case "mtconnect":
      return `${value.baseUrl || "未填写 Agent 地址"} · ${value.deviceNameOrUuid || "未选择设备"} · ${value.pollIntervalMs || "—"} ms`;
    case "mqtt": {
      const topics = (value.topics || []).filter(item => (item.topic || "").trim());
      return `${value.host || "未填写服务器"}:${value.port || "—"} · MQTT ${value.protocolVersion || "5.0"} · ${topics.length} 个主题`;
    }
    case "opc-ua":
      return `${value.endpointUrl || "未填写 OPC UA 端点"} · ${value.securityMode || "none"} · ${value.publishingIntervalMs || "—"} ms`;
    case "modbus-tcp":
      return `${value.host || "未填写设备地址"}:${value.port || "—"} · Unit ${value.unitId ?? "—"} · ${value.pollIntervalMs || "—"} ms`;
    case "modbus-rtu":
      return `${value.portName || "未填写串口名"} · ${value.baudRate || "—"} ${String(value.parity || "even").toUpperCase()} · Unit ${value.unitId ?? "—"} · ${value.pollIntervalMs || "—"} ms`;
    case "modbus-ascii":
      return `${value.portName || "未填写串口名"} · ASCII ${value.dataBits ?? 7}-${String(value.parity || "even").toUpperCase()}-${value.stopBits || "one"} · Unit ${value.unitId ?? "—"} · ${value.pollIntervalMs || "—"} ms`;
    case "melsec-a1e":
      return `${value.host || "未填写 PLC 地址"}:${value.port || "—"} · ${(value.dataCode || "binary").toUpperCase()} · ${value.pollIntervalMs || "—"} ms`;
    case "melsec-mc-3e": {
      const family = { "q-l": "Q / L", "iq-r": "iQ-R", "iq-f": "iQ-F" }[value.family] || "未选择系列";
      return `${value.host || "未填写 PLC 地址"}:${value.port || "未填写端口"} · ${family} · ${value.pollIntervalMs || "—"} ms`;
    }
    case "siemens-s7":
      return `${value.host || "未填写 PLC 地址"}:${value.port || "—"} · ${value.cpuType || "S71200"} · Rack ${value.rack ?? 0} / Slot ${value.slot ?? 0}`;
    case "ethernet-ip":
      return `${value.host || "未填写 PLC 地址"}:${value.port || "—"} · ${value.controllerFamily === "micro800" ? "Micro800" : "ControlLogix / CompactLogix"} · ${value.controllerFamily === "micro800" ? "无路由路径" : `Path ${value.path || "—"}`}`;
    case "omron-fins-tcp":
      return `${value.host || "未填写 PLC 地址"}:${value.port || "—"} · FINS/TCP · ${value.pollIntervalMs || "—"} ms`;
    case "omron-fins-udp":
      return `${value.host || "未填写 PLC 地址"}:${value.port || "—"} · 本机节点 ${value.clientNodeAddress || "未填写"} / PLC 节点 ${value.serverNodeAddress || "未填写"} · ${value.pollIntervalMs || "—"} ms`;
    case "bacnet-ip":
      return `${value.host || "未填写 BACnet/IP 地址"}:${value.port || "—"} · Device ${value.deviceInstance === "" || value.deviceInstance === null ? "未填写" : value.deviceInstance} · ${value.pollIntervalMs || "—"} ms`;
    case "bacnet-mstp":
      return `${value.portName || "未填写串口名"} · ${value.baudRate || "—"} baud · 本机站 ${value.localStationAddress === "" || value.localStationAddress === null ? "未填写" : value.localStationAddress} · Device ${value.deviceInstance === "" || value.deviceInstance === null ? "未填写" : value.deviceInstance}`;
    case "snmp":
      return `${value.host || "未填写 SNMP 设备地址"}:${value.port || "—"} · ${value.version || "v3"} · OID ${value.browseRootOid || "未填写"} · ${value.pollIntervalMs || "—"} ms`;
    default:
      return descriptor.label;
  }
}

function pointSelector(row, descriptor) {
  if (descriptor.addressing === "modbus-register")
    return row.modbusAddress === "" ? "" : `${row.modbusArea || "holding-register"}:${row.modbusAddress}${row.bitIndex === "" ? "" : `.${row.bitIndex}`}`;
  if (descriptor.addressing === "melsec-device")
    return row.melsecAddress === "" ? "" : `${row.melsecDevice || "D"}:${row.melsecAddress}${row.bitIndex === "" ? "" : `.${row.bitIndex}`}`;
  return String(row.sourcePath || "").trim();
}

function connectionSecuritySummary(form, descriptor) {
  const value = form[descriptor.section] || {};
  switch (descriptor.id) {
    case "http-polling":
      return `${(value.method || "get").toUpperCase()} · 密钥请求头 ${(value.headerSecretRefsText || "").split(/\r?\n/).filter(line => line.trim()).length} 项`;
    case "mtconnect":
      return `MTConnect GET /probe、/current · 密钥请求头 ${(value.headerSecretRefsText || "").split(/\r?\n/).filter(line => line.trim()).length} 项 · 只读`;
    case "mqtt":
      return `${value.useTls ? "TLS 已启用" : "未启用 TLS"} · ${value.username ? "用户名认证" : "匿名认证"} · MQTT ${value.protocolVersion || "5.0"}`;
    case "opc-ua":
      return `${value.securityMode || "none"} · ${value.securityPolicy || "None"} · ${value.authenticationType || "anonymous"}${value.trustServerCertificate ? " · 信任服务器证书" : ""}`;
    case "modbus-tcp":
      return `Unit ID ${value.unitId ?? "—"} · ${value.addressBase || "zero-based"}`;
    case "modbus-rtu":
      return `${value.dataBits ?? 8}-${String(value.parity || "even").toUpperCase()}-${value.stopBits || "one"} · Unit ID ${value.unitId ?? "—"} · ${value.addressBase || "zero-based"}`;
    case "modbus-ascii":
      return `ASCII ${value.dataBits ?? 7}-${String(value.parity || "even").toUpperCase()}-${value.stopBits || "one"} · Unit ID ${value.unitId ?? "—"} · ${value.addressBase || "zero-based"} · 只读已配置寄存器`;
    case "melsec-a1e":
      return `${(value.dataCode || "binary").toUpperCase()} · PC ${value.pcNumber ?? 255} · 布局 ${value.wordOrderLayout || "A"}`;
    case "melsec-mc-3e":
      return `二进制 3E · 网络 ${value.networkNumber ?? 0} / PC ${value.pcNumber ?? 255} / I/O ${value.moduleIoNumber ?? 1023} / 站 ${value.stationNumber ?? 0} · 只读已配置点位`;
    case "siemens-s7":
      return "S7 ISO-on-TCP · 只读已配置地址";
    case "ethernet-ip":
      return "EtherNet/IP 显式标签读取 · 只读已配置标量标签";
    case "omron-fins-tcp":
      return "Omron FINS/TCP · 只读已配置内存区地址";
    case "omron-fins-udp":
      return "Omron FINS/UDP 单播 · 只读已配置内存区地址 · 不扫描网络、不写 PLC";
    case "bacnet-ip":
      return "BACnet/IP 单播对象目录 · 只读 present-value 标量 · 不广播、不写入";
    case "bacnet-mstp":
      return `BACnet MS/TP ${value.baudRate || "—"} baud · ${value.localStationAddress === "" || value.localStationAddress === null ? "站地址未填写" : `本机站 ${value.localStationAddress}`} · 8N1 · 串口共享检查 · 只读`;
    case "snmp": {
      const level = value.securityLevel || "authPriv";
      if (value.version === "v2c") return "SNMPv2c · Community 密钥引用 · 限定 OID 子树 · 只读";
      const algorithms = [
        ...(["authNoPriv", "authPriv"].includes(level) ? [String(value.authenticationProtocol || "sha256").toUpperCase()] : []),
        ...(level === "authPriv" ? [String(value.privacyProtocol || "aes128").toUpperCase()] : []),
      ];
      return [`SNMPv3 ${level}`, ...algorithms, "限定 OID 子树", "只读"].join(" · ");
    }
    default:
      return descriptor.label;
  }
}

export function IngestionTaskTopologyCanvas({
  form,
  descriptor,
  edge,
  edges = [],
  models = [],
  dataModelName,
  dataItems = [],
  validationErrors = {},
  probe = null,
  readOnly = false,
  onUpdate,
  onUpdateSection,
  onProtocolChange,
  onMapPoint,
  onFocusSection,
}) {
  const [selectedNodeId, setSelectedNodeId] = useState("source");
  const sourceDescription = connectionSummary(form, descriptor);
  const deviceProfile = form.deviceProfile || { kind: "other", manufacturer: "", model: "" };
  const selectedDeviceProfile = deviceProfileOption(deviceProfile.kind);
  const recommendedDriverGroups = protocolOptionGroups(selectedDeviceProfile.drivers);
  const recommendedDrivers = recommendedDriverGroups
    .flatMap(group => group.options.map(([, label]) => label))
    .filter(Boolean);
  const changeDeviceKind = kind => {
    const nextProfile = deviceProfileOption(kind);
    onUpdate?.({ deviceProfile: { ...deviceProfile, kind } });
    if (!nextProfile.drivers.includes(form.protocol) && nextProfile.drivers.length > 0)
      onProtocolChange?.(nextProfile.drivers[0]);
  };
  const deviceIdentity = [deviceProfile.manufacturer, deviceProfile.model].filter(Boolean).join(" · ");
  const configuredPoints = useMemo(() => form.valueMappings.filter(row =>
    row.dataItemCode || pointSelector(row, descriptor)), [form.valueMappings, descriptor]);
  const mappedPoints = configuredPoints.filter(row => row.dataItemCode);
  const requiredCount = dataItems.filter(item => !item.nullable).length;
  const connectionErrors = descriptor.validateConnection(form[descriptor.section] || {}) || {};
  const connection = form[descriptor.section] || {};
  const publishedModels = models.filter(item => item.status === "published");
  const requiredMappedCount = dataItems.filter(item => !item.nullable && mappedPoints.some(row => row.dataItemCode === item.code)).length;
  const mappingStatus = probe
    ? probe.validationCurrent === false ? "需重新探查" : probe.success ? probe.mappingsValidated ? "探查通过" : "换算未通过" : "探查失败"
    : mappedPoints.length ? "待探查" : "待映射";
  const topics = descriptor.id === "mqtt"
    ? (form.mqtt?.topics || []).filter(item => (item.topic || "").trim())
    : [];
  const topicMappings = topics.map(topic => ({
    topic,
    rows: form.valueMappings.filter(row => row.topic === topic.topic),
  }));
  const unassignedTopicMappings = descriptor.id === "mqtt"
    ? form.valueMappings.filter(row => !(row.topic || "").trim() && (row.dataItemCode || pointSelector(row, descriptor)))
    : [];

  const nodes = [
    {
      id: "edge",
      title: "现场节点",
      value: edge?.hostname || edge?.displayName || form.edgeId || "待选择 Edge",
      detail: form.edgeId || "采集进程运行位置",
      target: "acquisition-basic-info",
      focusSelector: "#acquisition-edge-id",
      status: form.edgeId ? "已选择" : "待选择",
    },
    {
      id: "source",
      title: "采集设备与驱动",
      value: selectedDeviceProfile.label,
      detail: `${deviceIdentity || "厂商与型号待填写"} · ${descriptor.label} · ${sourceDescription}`,
      target: "acquisition-connection",
      focusSelector: "#acquisition-connection input:not([disabled]), #acquisition-connection select:not([disabled]), #acquisition-connection textarea:not([disabled])",
      security: connectionSecuritySummary(form, descriptor),
      status: Object.keys(connectionErrors).length ? "待补充" : "参数完整",
    },
    {
      id: "subject",
      title: "对象与设备单元",
      value: form.subjectId || "待填写归属对象",
      detail: `${subjectTypeLabels[form.subjectType] || form.subjectType || "未选择类型"}${form.equipmentId ? ` · ${form.equipmentId}` : " · 未填写设备单元"}`,
      target: "acquisition-basic-info",
      focusSelector: "#acquisition-subject-id",
      status: form.subjectId.trim() ? "已配置" : "待填写",
    },
    {
      id: "points",
      title: "点位与工艺变量",
      value: `${mappedPoints.length}/${configuredPoints.length} 个点位已映射`,
      detail: `${dataModelName || "待选择工艺数据模型"} · 必需项 ${requiredMappedCount}/${requiredCount} 个已覆盖`,
      target: "acquisition-mappings",
      focusSelector: "#acquisition-mappings select:not([disabled]), #acquisition-mappings input:not([disabled]), #acquisition-mappings textarea:not([disabled])",
      status: mappingStatus,
    },
  ];
  const selectedNode = nodes.find(node => node.id === selectedNodeId) || nodes[1];

  function openSection() {
    onFocusSection?.(selectedNode.target, selectedNode.focusSelector);
  }

  return (
    <section className="overflow-hidden rounded-xl border border-slate-200 bg-white" aria-label="当前采集任务拓扑">
      <div className="border-b border-slate-200 px-4 py-3 sm:px-5">
        <h2 className="font-semibold text-slate-950">采集拓扑配置关系</h2>
        <p className="mt-1 text-xs text-slate-600">箭头表示配置关联：Edge 执行协议连接，采集值归属到对象，再映射到工艺变量。{readOnly ? "点击节点可查看属性。" : "点击节点可在属性面板直接编辑常用字段。"}</p>
      </div>
      <div className="grid gap-4 p-4 sm:p-5 xl:grid-cols-[minmax(0,1fr)_18rem]">
        <div className="grid content-start gap-3">
          <div className="grid gap-2 md:grid-cols-[minmax(0,1fr)_2rem_minmax(0,1fr)_2rem_minmax(0,1fr)_2rem_minmax(0,1fr)]">
            {nodes.map((node, index) => (
              <Fragment key={node.id}>
                <button type="button" aria-pressed={selectedNodeId === node.id}
                  onClick={() => setSelectedNodeId(node.id)}
                  className={`min-w-0 rounded-lg border p-3 text-left transition ${selectedNodeId === node.id
                    ? "border-blue-500 bg-blue-50 ring-2 ring-blue-100"
                    : "border-slate-200 bg-white hover:border-blue-300"}`}>
                  <span className="text-[11px] font-semibold uppercase tracking-wide text-slate-500">{node.title}</span>
                  <span className={`float-right rounded-full px-2 py-0.5 text-[10px] font-medium ${node.status === "探查失败"
                    ? "bg-rose-100 text-rose-800"
                    : node.status === "换算未通过"
                      ? "bg-amber-100 text-amber-800"
                      : node.status === "探查通过" || node.status === "参数完整" || node.status === "已配置" || node.status === "已选择"
                        ? "bg-emerald-100 text-emerald-800"
                        : "bg-amber-100 text-amber-800"}`}>{node.status}</span>
                  <span className="mt-1 block break-all text-sm font-semibold text-slate-950">{node.value}</span>
                  <span className="mt-1 block break-all text-xs leading-5 text-slate-600">{node.detail}</span>
                </button>
                {index < nodes.length - 1 && (
                  <span className="hidden items-center justify-center text-lg text-slate-400 md:flex" aria-hidden="true">→</span>
                )}
              </Fragment>
            ))}
          </div>
          {topics.length > 0 && (
            <div className="rounded-lg border border-cyan-200 bg-cyan-50 p-3">
              <p className="text-[11px] font-semibold uppercase tracking-wide text-cyan-800">MQTT 订阅通道</p>
              <div className="mt-2 flex flex-wrap gap-2">
                {topics.map((topic, index) => (
                  <span key={`${topic.topic}:${index}`} className="max-w-full rounded-md bg-white px-2 py-1 text-xs text-slate-700">
                    <strong>{topic.channel || `通道 ${index + 1}`}</strong> · <code className="break-all">{topic.topic}</code>
                  </span>
                ))}
              </div>
            </div>
          )}
          <p className="text-[11px] leading-5 text-slate-500">
            这是当前任务的单连接视图。创建多个设备的配置可在新建采集配置时切换到多设备画布；MQTT 多主题在此展开显示。
          </p>
          {selectedNode.id === "points" && (
            <ProbePointMappingCanvas descriptor={descriptor} form={form} probe={probe} dataItems={dataItems}
              readOnly={readOnly} onMapPoint={onMapPoint} />
          )}
        </div>

        <aside className="rounded-lg border border-slate-200 bg-slate-50 p-4">
          <p className="text-[11px] font-semibold uppercase tracking-wide text-slate-500">节点属性</p>
          <h3 className="mt-1 text-base font-semibold text-slate-950">{selectedNode.title}</h3>
          <p className="mt-2 break-all text-sm font-medium text-slate-800">{selectedNode.value}</p>
          <p className="mt-1 break-all text-xs leading-5 text-slate-600">{selectedNode.detail}</p>
          <p className="mt-2 text-xs font-medium text-slate-700">状态：{selectedNode.status}</p>
          {selectedNode.id === "edge" && (
            <div className="mt-4 grid gap-3">
              <Field label="现场节点">
                <Select aria-label="画布 现场节点" value={form.edgeId} disabled={readOnly || !onUpdate}
                  onChange={event => onUpdate?.({ edgeId: event.target.value })}>
                  <option value="">请选择现场节点</option>
                  {form.edgeId && !edges.some(item => item.edgeId === form.edgeId) && (
                    <option value={form.edgeId}>{form.edgeId}（当前值）</option>
                  )}
                  {edges.map(item => <option key={item.edgeId} value={item.edgeId}>
                    {item.hostname || item.displayName || item.edgeId} · {item.edgeId}
                  </option>)}
                </Select>
              </Field>
              <p className="text-xs leading-5 text-slate-500">选择能访问设备网络的现场节点，修改会同步到任务表单。</p>
            </div>
          )}
          {selectedNode.id === "subject" && (
            <div className="mt-4 grid gap-3">
              <Field label="数据归属类型">
                <Select aria-label="画布 数据归属类型" value={form.subjectType} disabled={readOnly || !onUpdate}
                  onChange={event => onUpdate?.({ subjectType: event.target.value })}>
                  {Object.entries(subjectTypeLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
                  {!Object.hasOwn(subjectTypeLabels, form.subjectType) && <option value={form.subjectType}>{form.subjectType}（历史值）</option>}
                </Select>
              </Field>
              <Field label="归属对象编号">
                <Input aria-label="画布 归属对象编号" value={form.subjectId} disabled={readOnly || !onUpdate}
                  onChange={event => onUpdate?.({ subjectId: event.target.value })} />
              </Field>
              <Field label="生产设备单元编号（可选）">
                <Input aria-label="画布 生产设备单元编号" value={form.equipmentId} disabled={readOnly || !onUpdate}
                  onChange={event => onUpdate?.({ equipmentId: event.target.value })} />
              </Field>
              <p className="text-xs leading-5 text-slate-500">归属对象和生产设备单元分别填写；各腔独立换配方或工装时，可分别填写设备单元编号。</p>
            </div>
          )}
          {selectedNode.id === "source" && (
            <div className="mt-4 grid gap-3">
              <Field label="设备类型">
                <Select aria-label="画布 设备类型" value={deviceProfile.kind} disabled={readOnly || !onUpdate}
                  onChange={event => changeDeviceKind(event.target.value)}>
                  {deviceProfileOptions.map(item => <option key={item.value} value={item.value}>{item.label}</option>)}
                </Select>
              </Field>
              <Field label="设备厂商（可选）">
                <Input aria-label="画布 设备厂商" value={deviceProfile.manufacturer || ""} disabled={readOnly || !onUpdate}
                  placeholder="例如 Mitsubishi、Siemens、Fanuc"
                  onChange={event => onUpdate?.({ deviceProfile: { ...deviceProfile, manufacturer: event.target.value } })} />
              </Field>
              <Field label="设备型号（可选）">
                <Input aria-label="画布 设备型号" value={deviceProfile.model || ""} disabled={readOnly || !onUpdate}
                  placeholder="填写现场铭牌型号或系列"
                  onChange={event => onUpdate?.({ deviceProfile: { ...deviceProfile, model: event.target.value } })} />
              </Field>
              <p className="text-xs leading-5 text-slate-500">
                设备类别、厂商和型号用于识别设备，不会据此判断型号兼容性；实际连接取决于设备接口、通信驱动和 Edge 配置，并需从目标 Edge 探查确认。
                此设备类型的已登记驱动（仍需设备支持）：{recommendedDrivers.join("、")}。
              </p>
              <Field label="通信驱动">
                <Select aria-label="画布 通信驱动" value={form.protocol} disabled={readOnly || !onProtocolChange}
                  onChange={event => onProtocolChange?.(event.target.value)}>
                  {recommendedDriverGroups.map(group => (
                    <optgroup key={group.label} label={`${selectedDeviceProfile.label} · ${group.label}`}>
                      {group.options.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
                    </optgroup>
                  ))}
                </Select>
              </Field>
              <DescriptorConnectionEditor descriptor={descriptor} connection={connection}
                errors={Object.fromEntries(descriptor.fields.map(field => [
                  field.name,
                  validationErrors[`${descriptor.section}.${field.name}`] || connectionErrors[field.name] || "",
                ]))}
                readOnly={readOnly || !onUpdateSection}
                onChange={(name, value) => onUpdateSection?.(descriptor.section, { ...connection, [name]: value })} />
              {descriptor.id === "mqtt" && (
                <MqttTopicEditor topics={connection.topics || []}
                  errors={Object.fromEntries(Object.entries(connectionErrors)
                    .filter(([name]) => name === "topics" || name.startsWith("topics[")))}
                  readOnly={readOnly || !onUpdateSection}
                  onChange={topics => onUpdateSection?.(descriptor.section, { ...connection, topics })} />
              )}
              <p className="text-xs leading-5 text-slate-500">已登记的协议连接字段均可在此配置；任务级点位映射等高级参数在对应配置中编辑。</p>
            </div>
          )}
          {selectedNode.id === "points" && (
            <div className="mt-4 grid gap-3">
              <Field label="工艺变量模型">
                <Select aria-label="画布 工艺变量模型" value={form.dataModel} disabled={readOnly || !onUpdate}
                  onChange={event => onUpdate?.({ dataModel: event.target.value })}>
                  <option value="">请选择已发布模型</option>
                  {form.dataModel && !publishedModels.some(item => modelValue(item.modelId, item.version) === form.dataModel) && (
                    <option value={form.dataModel}>{dataModelName || form.dataModel}（当前模型）</option>
                  )}
                  {publishedModels.map(item => <option key={modelValue(item.modelId, item.version)} value={modelValue(item.modelId, item.version)}>
                    {item.name || item.modelId} · v{item.version}
                  </option>)}
                </Select>
              </Field>
              <p className="text-xs leading-5 text-slate-500">返回主画布后可把探查到的设备点位拖到工艺变量；完整换算、质量和缺失值策略在点位配置表中编辑。</p>
            </div>
          )}
          {selectedNode.id === "points" && configuredPoints.length > 0 && (
            <div className="mt-3 space-y-1.5">
              {configuredPoints.slice(0, 4).map((row, index) => (
                <div key={`${row.dataItemCode}:${pointSelector(row, descriptor)}:${index}`} className="break-all rounded-md bg-white px-2 py-1.5 text-[11px] text-slate-700">
                  {row.dataItemCode || "未映射"} · {pointSelector(row, descriptor) || "未设置来源"}
                </div>
              ))}
              {configuredPoints.length > 4 && <p className="text-[11px] text-slate-500">还有 {configuredPoints.length - 4} 个点位</p>}
            </div>
          )}
          {selectedNode.id === "source" && (
            <div className="mt-3 space-y-2 rounded-md bg-white p-2.5 text-[11px] text-slate-700">
              <p><span className="font-medium">连接安全与路由：</span>{selectedNode.security}</p>
              {topicMappings.length > 0 && (
                <>
                  <p className="font-semibold text-cyan-800">主题 → 映射点位</p>
                  {topicMappings.map(({ topic, rows }) => (
                    <div key={topic.topic} className="border-l-2 border-cyan-300 pl-2">
                      <p className="break-all font-medium">{topic.channel || "未命名通道"} · {topic.topic}</p>
                      <p className="mt-0.5 break-all text-slate-500">
                        {rows.length ? rows.slice(0, 3).map(row => `${row.dataItemCode || "未映射"} ← ${pointSelector(row, descriptor) || row.sourcePath || "未设来源"}`).join("；") : "尚无点位绑定此主题"}
                        {rows.length > 3 ? `；另有 ${rows.length - 3} 个` : ""}
                      </p>
                    </div>
                  ))}
                  {unassignedTopicMappings.length > 0 && (
                    <p className="break-all text-slate-500">任意主题：{unassignedTopicMappings.slice(0, 3).map(row => row.dataItemCode || "未映射").join("、")}{unassignedTopicMappings.length > 3 ? ` 等 ${unassignedTopicMappings.length} 项` : ""}</p>
                  )}
                </>
              )}
            </div>
          )}
          <Button className="mt-4 w-full" onClick={openSection}>
            {readOnly ? "查看完整配置" : selectedNode.id === "points" ? "编辑完整点位映射" : "编辑完整配置"}
          </Button>
        </aside>
      </div>
    </section>
  );
}
