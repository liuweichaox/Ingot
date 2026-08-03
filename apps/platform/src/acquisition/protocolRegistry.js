// 采集协议描述符注册表。
//
// 以前 5 个协议的差异散落在 RegistryBusinessEditor 的 5 处条件分支里
// （changeProtocol、configurationError、ConnectionFields、MappingRows、acquisitionPayload），
// 新增一个驱动要同时改 5 个地方，而且哪些字段对哪个协议真正生效只存在于人的记忆里。
//
// 现在每个协议是一份声明式描述符：连接字段、寻址方式、数据类型、能力开关、校验与提示
// 都在同一个对象里。界面按描述符渲染，校验按描述符执行。
//
// 能力开关（capabilities）与后端 AcquisitionProtocolCapabilities 一一对应，
// 并在页面加载时用 GET /api/v1/acquisition-protocols 的返回值覆盖，
// 保证"这个字段是否真的生效"永远以后端 Runner 的实际行为为准。

export const ADDRESSING = {
  jsonPath: "json-path",
  nodeId: "node-id",
  modbusRegister: "modbus-register",
  melsecDevice: "melsec-device",
};

export const PROBE_MODE = {
  discover: "discover",
  configuredPointsOnly: "configured-points-only",
};

const REGISTER_TYPES = [
  "int16", "uint16", "int32", "uint32", "float32",
  "int64", "uint64", "float64", "string", "boolean",
];

const DOCUMENT_TYPES = ["auto", ...REGISTER_TYPES];

export const MELSEC_DEVICES = [
  { code: "D", bit: false, radix: 10, description: "数据寄存器" },
  { code: "R", bit: false, radix: 10, description: "扩展文件寄存器" },
  { code: "W", bit: false, radix: 16, description: "链接寄存器" },
  { code: "T", bit: false, radix: 10, description: "定时器当前值" },
  { code: "C", bit: false, radix: 10, description: "计数器当前值" },
  { code: "M", bit: true, radix: 10, description: "辅助继电器" },
  { code: "L", bit: true, radix: 10, description: "锁存继电器" },
  { code: "S", bit: true, radix: 10, description: "状态继电器" },
  { code: "X", bit: true, radix: 8, description: "输入继电器" },
  { code: "Y", bit: true, radix: 8, description: "输出继电器" },
  { code: "B", bit: true, radix: 16, description: "链接继电器" },
];

export const MODBUS_AREAS = [
  { value: "holding-register", label: "保持寄存器（03）", bit: false },
  { value: "input-register", label: "输入寄存器（04）", bit: false },
  { value: "coil", label: "线圈（01）", bit: true },
  { value: "discrete-input", label: "离散输入（02）", bit: true },
];

export const melsecDevice = code => MELSEC_DEVICES.find(item => item.code === (code || "").toUpperCase());
export const modbusArea = value => MODBUS_AREAS.find(item => item.value === value);
export const isModbusBitArea = value => Boolean(modbusArea(value)?.bit);

const RADIX_LABEL = { 8: "八进制", 10: "十进制", 16: "十六进制" };

/** 把按软元件进制书写的编号换算成真正写进 1E 帧的数字，界面同时显示两者便于对照手册。 */
export function melsecWireAddress(deviceCode, address) {
  const device = melsecDevice(deviceCode);
  if (!device || !address) return null;
  const text = String(address).trim();
  if (!text) return null;
  const digits = { 8: /^[0-7]+$/, 10: /^[0-9]+$/, 16: /^[0-9a-fA-F]+$/ }[device.radix];
  if (!digits.test(text)) return null;
  const value = parseInt(text, device.radix);
  return Number.isFinite(value) ? value : null;
}

export function melsecAddressHint(deviceCode) {
  const device = melsecDevice(deviceCode);
  if (!device) return "";
  const radix = RADIX_LABEL[device.radix];
  return device.radix === 10
    ? `${device.description} · ${radix}编号`
    : `${device.description} · 按手册的${radix}编号填写，界面会显示换算后的软元件号`;
}

function wordCount(dataType, byteLength) {
  if (dataType === "string") return Math.max(1, Math.ceil((Number(byteLength) || 2) / 2));
  if (["int32", "uint32", "float32"].includes(dataType)) return 2;
  if (["int64", "uint64", "float64"].includes(dataType)) return 4;
  return 1;
}

export const registerWordCount = wordCount;

// ---------------------------------------------------------------- 协议描述符

const httpPolling = {
  id: "http-polling",
  label: "HTTP 轮询",
  section: "connection",
  addressing: ADDRESSING.jsonPath,
  probeMode: PROBE_MODE.discover,
  summary: "设备或网关以 HTTP 提供一份 JSON 快照，采集节点按间隔读取。",
  pointLabel: "JSON 字段",
  probeViewLabel: "JSON 字段树",
  dataTypes: DOCUMENT_TYPES,
  capabilities: {
    sourceTimestamp: true,
    sequencePath: true,
    recipeParametersPath: true,
    connectTimeout: true,
    reconnectDelay: true,
    perTopicMapping: false,
    registerByteOrder: false,
    bitAddressing: false,
  },
  constraints: [
    "当前驱动使用 GET 读取，不支持自定义请求头、请求体或 HTTP 鉴权。",
    "一次请求必须返回包含全部必需字段的完整快照。",
  ],
  defaults: () => ({ baseUrl: "", snapshotPath: "/api/v1/snapshot", pollIntervalMs: 1000 }),
  fields: [
    { name: "baseUrl", label: "服务地址", type: "text", placeholder: "http://192.168.1.10",
      hint: "设备或网关提供 HTTP 服务的根地址。" },
    { name: "snapshotPath", label: "数据路径", type: "text", placeholder: "/api/v1/snapshot",
      hint: "采集节点向该路径发起 GET 请求。" },
    { name: "pollIntervalMs", label: "轮询间隔（ms）", type: "number", min: 1,
      hint: "一次请求完成后等待多久再发起下一次；不是固定采样周期。" },
  ],
  validateConnection(connection) {
    const errors = {};
    if (!/^https?:\/\/\S+$/.test((connection.baseUrl || "").trim()))
      errors.baseUrl = "必须是 http:// 或 https:// 开头的绝对地址。";
    if (!(connection.snapshotPath || "").trim()) errors.snapshotPath = "数据路径不能为空。";
    if (!(Number(connection.pollIntervalMs) >= 1)) errors.pollIntervalMs = "必须大于 0。";
    return errors;
  },
  validatePoint: (row) => (row.sourcePath || "").trim() ? {} : { sourcePath: "字段路径不能为空。" },
  probeReadiness(form) {
    const connection = form.connection || {};
    return /^https?:\/\/\S+$/.test((connection.baseUrl || "").trim()) && (connection.snapshotPath || "").trim()
      ? "" : "请填写有效的设备 HTTP 地址和数据路径。";
  },
};

const mqtt = {
  id: "mqtt",
  label: "MQTT 订阅",
  section: "mqtt",
  addressing: ADDRESSING.jsonPath,
  probeMode: PROBE_MODE.discover,
  summary: "设备或网关主动向消息服务器发布 JSON 报文，采集节点订阅接收。",
  pointLabel: "JSON 字段",
  probeViewLabel: "报文字段树",
  dataTypes: DOCUMENT_TYPES,
  capabilities: {
    sourceTimestamp: true,
    sequencePath: true,
    recipeParametersPath: true,
    connectTimeout: false,
    reconnectDelay: true,
    perTopicMapping: true,
    registerByteOrder: false,
    bitAddressing: false,
  },
  constraints: [
    "订阅多个主题时，请为每个点位指定来源主题；未指定的点位按任意主题的报文解析。",
    "跨主题的值会合并成一份快照；主题数据超过最大年龄或时间偏差过大时不会产生采样。",
  ],
  defaults: () => ({
    host: "", port: 1883, protocolVersion: "5.0", clientId: "", username: "", passwordSecretRef: "",
    useTls: false, caCertificatePath: "", clientCertificatePath: "", clientCertificatePasswordSecretRef: "",
    cleanSession: true, keepAliveSeconds: 30, snapshotMaxAgeSeconds: 30, snapshotMaxSkewSeconds: 5,
    topics: [{ topic: "", qos: 0, payloadRoot: "" }],
  }),
  fields: [
    { name: "host", label: "消息服务器", type: "text", placeholder: "192.168.1.20" },
    { name: "port", label: "端口", type: "number", min: 1, max: 65535 },
    { name: "protocolVersion", label: "协议版本", type: "select",
      options: [["5.0", "MQTT 5.0"], ["3.1.1", "MQTT 3.1.1"]] },
    { name: "clientId", label: "客户端编号", type: "text", hint: "留空时由采集节点生成唯一编号。" },
    { name: "keepAliveSeconds", label: "保活时间（秒）", type: "number", min: 1 },
    { name: "snapshotMaxAgeSeconds", label: "快照最大年龄（秒）", type: "number", min: 1,
      hint: "多主题数据超过此年龄仍未更新时，禁止继续拼接旧值。" },
    { name: "snapshotMaxSkewSeconds", label: "主题最大时间偏差（秒）", type: "number", min: 0,
      hint: "多主题数据最早与最晚到达时间超过此值时，等待重新形成快照。" },
    { name: "cleanSession", label: form => form.protocolVersion === "5.0" ? "重新开始会话（Clean Start）" : "清理旧会话（Clean Session）",
      type: "checkbox" },
    { name: "username", label: "用户名", type: "text", group: "认证" },
    { name: "passwordSecretRef", label: "密码凭据", type: "text", group: "认证",
      hint: "填写采集节点密钥库中的名称，不在配置中保存明文。" },
    { name: "useTls", label: "启用 TLS", type: "checkbox", group: "认证" },
    { name: "caCertificatePath", label: "CA 证书", type: "text", group: "认证",
      when: connection => connection.useTls, hint: "采集节点上的证书文件路径。" },
    { name: "clientCertificatePath", label: "客户端证书", type: "text", group: "认证",
      when: connection => connection.useTls },
    { name: "clientCertificatePasswordSecretRef", label: "客户端证书密码凭据", type: "text", group: "认证",
      when: connection => connection.useTls },
  ],
  validateConnection(connection) {
    const errors = {};
    if (!(connection.host || "").trim()) errors.host = "消息服务器地址不能为空。";
    const port = Number(connection.port);
    if (!(port >= 1 && port <= 65535)) errors.port = "端口必须在 1-65535 之间。";
    if (!(Number(connection.keepAliveSeconds) >= 1)) errors.keepAliveSeconds = "保活时间必须大于 0 秒。";
    if (!(Number(connection.snapshotMaxAgeSeconds) >= 1)) errors.snapshotMaxAgeSeconds = "快照最大年龄必须大于 0 秒。";
    if (!(Number(connection.snapshotMaxSkewSeconds) >= 0)) errors.snapshotMaxSkewSeconds = "主题最大时间偏差不能小于 0 秒。";
    if (connection.useTls && !(connection.caCertificatePath || "").trim() && !(connection.clientCertificatePath || "").trim())
      errors.caCertificatePath = "启用 TLS 时至少需要一个证书路径。";
    const topics = connection.topics || [];
    if (!topics.some(item => (item.topic || "").trim())) errors.topics = "至少需要一个订阅主题。";
    const seen = new Set();
    topics.forEach((item, index) => {
      const topic = (item.topic || "").trim();
      if (!topic) return;
      const message = mqttTopicError(topic);
      if (message) errors[`topics[${index}].topic`] = message;
      else if (seen.has(topic)) errors[`topics[${index}].topic`] = "订阅主题不能重复。";
      seen.add(topic);
    });
    return errors;
  },
  validatePoint: (row) => (row.sourcePath || "").trim() ? {} : { sourcePath: "字段路径不能为空。" },
  probeReadiness(form) {
    const connection = form.mqtt || {};
    return (connection.host || "").trim() && (connection.topics || []).some(item => (item.topic || "").trim())
      ? "" : "请填写消息服务器地址和至少一个订阅主题。";
  },
  advisories(form) {
    const topics = (form.mqtt?.topics || []).filter(item => (item.topic || "").trim());
    const unbound = (form.valueMappings || []).filter(item => item.dataItemCode && !(item.topic || "").trim());
    if (topics.length > 1 && unbound.length > 0) {
      return [{
        tone: "warning",
        message: `订阅了 ${topics.length} 个主题，但有 ${unbound.length} 个点位没有绑定来源主题。` +
          "未绑定的点位会接受任意主题的报文，主题之间字段重名时会互相覆盖。",
      }];
    }
    return [];
  },
};

/** MQTT 主题过滤器语法：+ 必须独占一层，# 只能在最后一层。与后端校验保持一致。 */
export function mqttTopicError(topic) {
  if (!topic) return "主题不能为空。";
  const levels = topic.split("/");
  for (let index = 0; index < levels.length; index += 1) {
    const level = levels[index];
    if (level.includes("+") && level !== "+") return "通配符 + 必须独占一个层级，例如 plant/+/line。";
    if (!level.includes("#")) continue;
    if (level !== "#") return "通配符 # 必须独占一个层级。";
    if (index !== levels.length - 1) return "通配符 # 只能出现在最后一个层级。";
  }
  return "";
}

const opcUa = {
  id: "opc-ua",
  label: "OPC UA",
  section: "opcUa",
  addressing: ADDRESSING.nodeId,
  probeMode: PROBE_MODE.discover,
  summary: "通过 OPC UA 会话订阅变量节点，由服务器按变化推送。",
  pointLabel: "节点编号",
  probeViewLabel: "节点浏览器",
  dataTypes: DOCUMENT_TYPES,
  capabilities: {
    sourceTimestamp: false,
    sequencePath: false,
    recipeParametersPath: false,
    connectTimeout: true,
    reconnectDelay: true,
    perTopicMapping: false,
    registerByteOrder: false,
    bitAddressing: false,
  },
  constraints: [
    "采样时间固定使用服务器提供的 SourceTimestamp，不能改用采集节点接收时间。",
    "NodeId 中的命名空间序号由服务器分配；服务器重排命名空间后需要重新验证配置。",
  ],
  defaults: () => ({
    endpointUrl: "", securityMode: "none", securityPolicy: "None", authenticationType: "anonymous",
    username: "", passwordSecretRef: "", clientCertificatePath: "", clientCertificatePasswordSecretRef: "",
    trustServerCertificate: false, publishingIntervalMs: 1000, samplingIntervalMs: 1000,
  }),
  fields: [
    { name: "endpointUrl", label: "服务器端点", type: "text", placeholder: "opc.tcp://192.168.1.10:4840",
      hint: "验证连接时会发现并校验服务器实际提供的安全组合。" },
    { name: "publishingIntervalMs", label: "发布间隔（ms）", type: "number", min: 1,
      hint: "服务器向客户端发送订阅通知的节奏。" },
    { name: "samplingIntervalMs", label: "采样间隔（ms）", type: "number", min: 1,
      hint: "服务器检查变量变化的最快节奏，也是本驱动唯一的节流手段。" },
    { name: "securityMode", label: "安全模式", type: "select", group: "安全",
      options: [["none", "无"], ["sign", "签名"], ["sign-and-encrypt", "签名并加密"]] },
    { name: "securityPolicy", label: "安全策略", type: "select", group: "安全",
      options: [["None", "无"], ["Basic256Sha256", "Basic256Sha256"],
        ["Aes128_Sha256_RsaOaep", "Aes128_Sha256_RsaOaep"], ["Aes256_Sha256_RsaPss", "Aes256_Sha256_RsaPss"]] },
    { name: "authenticationType", label: "登录方式", type: "select", group: "安全",
      options: [["anonymous", "匿名"], ["username", "用户名密码"], ["certificate", "用户证书"]] },
    { name: "username", label: "用户名", type: "text", group: "安全",
      when: connection => connection.authenticationType === "username" },
    { name: "passwordSecretRef", label: "密码凭据", type: "text", group: "安全",
      when: connection => connection.authenticationType === "username" },
    { name: "clientCertificatePath", label: "客户端证书", type: "text", group: "安全",
      when: connection => connection.securityMode !== "none" || connection.authenticationType === "certificate" },
    { name: "clientCertificatePasswordSecretRef", label: "客户端证书密码凭据", type: "text", group: "安全",
      when: connection => connection.securityMode !== "none" || connection.authenticationType === "certificate" },
    { name: "trustServerCertificate", label: "自动信任未登记的服务器证书（仅调试）", type: "checkbox", group: "安全", tone: "warning" },
  ],
  validateConnection(connection) {
    const errors = {};
    if (!/^(opc\.tcp|https):\/\/\S+$/.test((connection.endpointUrl || "").trim()))
      errors.endpointUrl = "端点必须以 opc.tcp:// 或 https:// 开头。";
    if (!(Number(connection.publishingIntervalMs) >= 1)) errors.publishingIntervalMs = "必须大于 0。";
    if (!(Number(connection.samplingIntervalMs) >= 1)) errors.samplingIntervalMs = "必须大于 0。";
    if (connection.securityMode !== "none" && connection.securityPolicy === "None")
      errors.securityPolicy = "启用签名或加密时必须选择一个具体的安全策略。";
    if (connection.securityMode !== "none" && !(connection.clientCertificatePath || "").trim())
      errors.clientCertificatePath = "启用安全通道时必须配置客户端证书路径。";
    if (connection.authenticationType === "username" && !(connection.username || "").trim())
      errors.username = "用户名认证需要填写用户名。";
    if (connection.authenticationType === "certificate" && !(connection.clientCertificatePath || "").trim())
      errors.clientCertificatePath = "证书认证需要配置客户端证书路径。";
    return errors;
  },
  validatePoint(row) {
    const message = nodeIdError((row.sourcePath || "").trim());
    return message ? { sourcePath: message } : {};
  },
  probeReadiness(form) {
    return /^(opc\.tcp|https):\/\/\S+$/.test((form.opcUa?.endpointUrl || "").trim())
      ? "" : "请填写有效的 OPC UA 端点。";
  },
  advisories(form) {
    return form.opcUa?.trustServerCertificate
      ? [{ tone: "warning", message: "已开启自动信任未登记证书。该选项会跳过服务器身份校验，只应在调试环境使用。" }]
      : [];
  },
};

/** NodeId 结构检查。真正的合法性由服务器裁决，这里只拦住明显写错的形式。 */
export function nodeIdError(value) {
  if (!value) return "节点编号不能为空。";
  let body = value;
  if (value.startsWith("ns=")) {
    const separator = value.indexOf(";");
    if (separator < 0) return "缺少命名空间与标识之间的分号，例如 ns=2;s=Machine.Temperature。";
    if (!/^\d+$/.test(value.slice(3, separator))) return "命名空间序号必须是非负整数。";
    body = value.slice(separator + 1);
  }
  if (body.length < 3 || body[1] !== "=") return "标识必须以 i=、s=、g= 或 b= 开头。";
  const kind = body[0];
  const rest = body.slice(2);
  if (kind === "i" && !/^\d+$/.test(rest)) return "数字型 NodeId 的标识必须是非负整数。";
  if (kind === "g" && !/^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/.test(rest))
    return "GUID 型 NodeId 的标识不是合法的 GUID。";
  if (!["i", "s", "g", "b"].includes(kind)) return "标识类型必须是 i、s、g 或 b。";
  return "";
}

const modbusTcp = {
  id: "modbus-tcp",
  label: "Modbus TCP",
  section: "modbusTcp",
  addressing: ADDRESSING.modbusRegister,
  probeMode: PROBE_MODE.configuredPointsOnly,
  summary: "按寄存器地址读取，适用于仪表、变频器与通用 PLC 网关。",
  pointLabel: "寄存器",
  probeViewLabel: "寄存器读取结果",
  dataTypes: REGISTER_TYPES,
  capabilities: {
    sourceTimestamp: true,
    sequencePath: false,
    recipeParametersPath: false,
    connectTimeout: true,
    reconnectDelay: true,
    perTopicMapping: false,
    registerByteOrder: true,
    bitAddressing: true,
  },
  constraints: [
    "协议不能枚举地址空间，验证连接只会回读已经配置的寄存器。",
    "一个采集配置只能访问一个从站编号。",
  ],
  defaults: () => ({ host: "", port: 502, unitId: 1, addressBase: "zero-based", pollIntervalMs: 1000 }),
  fields: [
    { name: "host", label: "设备地址", type: "text", placeholder: "192.168.1.30" },
    { name: "port", label: "端口", type: "number", min: 1, max: 65535 },
    { name: "unitId", label: "从站编号", type: "number", min: 0, max: 255,
      hint: "直连设备通常为 1；经网关访问多个从站时必须按现场设置填写。" },
    { name: "addressBase", label: "地址起点", type: "select",
      options: [["zero-based", "从 0 开始（线缆地址）"], ["one-based", "从 1 开始（手册地址）"]],
      hint: "必须与设备手册的寄存器编号方式一致，填错会整体偏移一个地址。" },
    { name: "pollIntervalMs", label: "轮询间隔（ms）", type: "number", min: 1 },
  ],
  validateConnection(connection) {
    const errors = {};
    if (!(connection.host || "").trim()) errors.host = "设备地址不能为空。";
    const port = Number(connection.port);
    if (!(port >= 1 && port <= 65535)) errors.port = "端口必须在 1-65535 之间。";
    const unit = Number(connection.unitId);
    if (!(unit >= 0 && unit <= 255)) errors.unitId = "从站编号必须在 0-255 之间。";
    if (!(Number(connection.pollIntervalMs) >= 1)) errors.pollIntervalMs = "必须大于 0。";
    return errors;
  },
  validatePoint(row, form) {
    const errors = {};
    const area = modbusArea(row.modbusArea);
    if (!area) { errors.modbusArea = "请选择寄存器区。"; return errors; }
    const address = String(row.modbusAddress ?? "").trim();
    if (!/^\d+$/.test(address)) errors.modbusAddress = "地址必须是非负整数。";
    else {
      const numeric = Number(address);
      if (numeric > 65535) errors.modbusAddress = "地址不能超过 65535。";
      if (form.modbusTcp?.addressBase === "one-based" && numeric < 1)
        errors.modbusAddress = "使用手册地址（从 1 开始）时地址必须大于 0。";
    }
    if (area.bit) {
      if (row.sourceDataType !== "boolean")
        errors.sourceDataType = `${area.label}是位区，数据类型只能是 boolean。`;
      if (row.bitIndex !== "" && row.bitIndex !== null && row.bitIndex !== undefined)
        errors.bitIndex = "位区不需要再指定位偏移。";
    } else if (row.sourceDataType === "boolean") {
      const bit = Number(row.bitIndex);
      if (row.bitIndex === "" || row.bitIndex === null || !Number.isInteger(bit) || bit < 0 || bit > 15)
        errors.bitIndex = "从寄存器取布尔值时必须指定 0-15 之间的位偏移。";
    } else if (row.sourceDataType === "string") {
      const quantity = Number(row.modbusQuantity);
      if (!(Number.isInteger(quantity) && quantity >= 1 && quantity <= 64))
        errors.modbusQuantity = "寄存器数量必须在 1-64 之间。";
    }
    return errors;
  },
  probeReadiness(form) {
    if (!(form.modbusTcp?.host || "").trim()) return "请先填写设备地址。";
    return (form.valueMappings || []).some(item => String(item.modbusAddress ?? "").trim())
      ? "" : "寄存器协议不会盲扫地址，请先配置至少一个寄存器点位。";
  },
  advisories(form) {
    const notes = [];
    const seen = new Map();
    (form.valueMappings || []).forEach(item => {
      if (!String(item.modbusAddress ?? "").trim()) return;
      const key = `${item.modbusArea}:${item.modbusAddress}:${item.bitIndex ?? ""}`;
      seen.set(key, (seen.get(key) || 0) + 1);
    });
    const duplicated = [...seen.entries()].filter(([, count]) => count > 1);
    if (duplicated.length)
      notes.push({ tone: "warning", message: `有 ${duplicated.length} 组点位指向完全相同的寄存器地址，采集结果会互相覆盖。` });
    if (form.modbusTcp?.addressBase === "one-based")
      notes.push({ tone: "info", message: "当前按手册地址填写，采集节点发送请求前会自动减 1。" });
    return notes;
  },
};

const melsecA1e = {
  id: "melsec-a1e",
  label: "三菱 MC 协议（A 兼容 1E 帧）",
  section: "melsecA1E",
  addressing: ADDRESSING.melsecDevice,
  probeMode: PROBE_MODE.configuredPointsOnly,
  summary: "直连 FX3U-ENET(-L/-ADP) 等以太网模块，按软元件编号读取。",
  pointLabel: "软元件",
  probeViewLabel: "软元件读取结果",
  dataTypes: REGISTER_TYPES,
  capabilities: {
    sourceTimestamp: true,
    sequencePath: false,
    recipeParametersPath: false,
    connectTimeout: true,
    reconnectDelay: true,
    perTopicMapping: false,
    registerByteOrder: false,
    bitAddressing: true,
  },
  constraints: [
    "协议不能枚举软元件，验证连接只会回读已经配置的点位。",
    "X / Y 在 FX 系列按八进制编号，编号中不能出现数字 8 和 9。",
    "位软元件读取布尔值时使用位单位批量读命令；按 int16 读取会把 16 个连续点打包成一个字。",
  ],
  defaults: () => ({
    host: "", port: 5551, pollIntervalMs: 1000, dataCode: "binary",
    pcNumber: 255, monitoringTimer: 16, wordOrderLayout: "A", maxMergeGap: 8,
  }),
  fields: [
    { name: "host", label: "PLC 地址", type: "text", placeholder: "192.168.1.40" },
    { name: "port", label: "开放端口", type: "number", min: 1, max: 65535,
      hint: "填写 FX 参数中设置为 MC 协议的端口，不是 GX Works / MELSOFT 端口。" },
    { name: "pollIntervalMs", label: "轮询间隔（ms）", type: "number", min: 1 },
    { name: "dataCode", label: "通信数据码", type: "select", group: "帧参数",
      options: [["binary", "二进制"], ["ascii", "ASCII 码"]],
      hint: "必须与 PLC 以太网模块的通信数据码设置一致，填错会读到乱码或超时。" },
    { name: "pcNumber", label: "目标站号", type: "number", min: 0, max: 255, group: "帧参数",
      hint: "直连 FX3U / FX3UC 通常为 255（FFH）。" },
    { name: "monitoringTimer", label: "监视定时器", type: "number", min: 0, max: 65535, group: "帧参数",
      hint: "单位 250ms；16 表示约 4 秒。" },
    { name: "maxMergeGap", label: "合并读取间隔", type: "number", min: 0, max: 256, group: "帧参数",
      hint: "相邻点位编号相差不超过该值时合并成一次读取。设为 0 表示逐点读取。" },
  ],
  validateConnection(connection) {
    const errors = {};
    if (!(connection.host || "").trim()) errors.host = "PLC 地址不能为空。";
    const port = Number(connection.port);
    if (!(port >= 1 && port <= 65535)) errors.port = "端口必须在 1-65535 之间。";
    if (!(Number(connection.pollIntervalMs) >= 1)) errors.pollIntervalMs = "必须大于 0。";
    const pc = Number(connection.pcNumber);
    if (!(pc >= 0 && pc <= 255)) errors.pcNumber = "目标站号必须在 0-255 之间。";
    const gap = Number(connection.maxMergeGap);
    if (!(gap >= 0 && gap <= 256)) errors.maxMergeGap = "合并读取间隔必须在 0-256 之间。";
    return errors;
  },
  validatePoint(row) {
    const errors = {};
    const device = melsecDevice(row.melsecDevice);
    if (!device) { errors.melsecDevice = "请选择软元件。"; return errors; }
    const address = String(row.melsecAddress ?? "").trim();
    if (!address) errors.melsecAddress = "软元件编号不能为空。";
    else if (melsecWireAddress(device.code, address) === null)
      errors.melsecAddress = `${device.code} 按${RADIX_LABEL[device.radix]}编号，当前写法不合法。`;
    if (row.sourceDataType === "boolean") {
      if (!device.bit) {
        const bit = Number(row.bitIndex);
        if (row.bitIndex === "" || row.bitIndex === null || !Number.isInteger(bit) || bit < 0 || bit > 15)
          errors.bitIndex = `${device.code} 是字软元件，读取布尔值时必须指定 0-15 之间的位偏移。`;
      } else if (row.bitIndex !== "" && row.bitIndex !== null && row.bitIndex !== undefined) {
        errors.bitIndex = `${device.code} 本身就是位软元件，不需要位偏移。`;
      }
    } else if (row.sourceDataType === "string") {
      const length = Number(row.melsecStringLength);
      if (!(Number.isInteger(length) && length >= 1 && length <= 128))
        errors.melsecStringLength = "文本长度必须在 1-128 字节之间。";
    }
    return errors;
  },
  probeReadiness(form) {
    if (!(form.melsecA1E?.host || "").trim()) return "请先填写 PLC 地址。";
    return (form.valueMappings || []).some(item => String(item.melsecAddress ?? "").trim())
      ? "" : "MC 协议不会盲扫软元件，请先配置至少一个点位。";
  },
  advisories(form) {
    const notes = [];
    const points = (form.valueMappings || []).filter(item => String(item.melsecAddress ?? "").trim());
    const packed = points.filter(item => melsecDevice(item.melsecDevice)?.bit && item.sourceDataType !== "boolean");
    if (packed.length)
      notes.push({
        tone: "warning",
        message: `有 ${packed.length} 个位软元件按数值类型读取。协议会把从该编号起的 16 个连续点打包成一个字返回，` +
          "这不是单点状态。要读单点请把数据类型改为 boolean。",
      });
    const gap = Number(form.melsecA1E?.maxMergeGap ?? 0);
    const interval = Number(form.melsecA1E?.pollIntervalMs ?? 0);
    if (points.length && interval) {
      const trips = gap > 0 ? Math.max(1, Math.ceil(points.length / 3)) : points.length;
      notes.push({
        tone: "info",
        message: `当前 ${points.length} 个点位，预计每周期约 ${trips} 次网络往返` +
          `${gap > 0 ? "（已启用合并读取）" : "（未启用合并读取，逐点往返）"}。` +
          `轮询间隔 ${interval}ms，请确认现场网络能在该间隔内完成。`,
      });
    }
    const octal = points.filter(item => {
      const device = melsecDevice(item.melsecDevice);
      return device?.radix === 8;
    });
    if (octal.length)
      notes.push({
        tone: "info",
        message: "X / Y 按八进制编号。点位行会显示换算后的软元件号，请对照 PLC 手册确认第一次接线。",
      });
    return notes;
  },
};

const DESCRIPTORS = [httpPolling, mqtt, opcUa, modbusTcp, melsecA1e];

export const protocolOptions = DESCRIPTORS.map(item => [item.id, item.label, item.summary]);

export function protocolDescriptor(id) {
  return DESCRIPTORS.find(item => item.id === id) || httpPolling;
}

/**
 * 用后端返回的能力矩阵覆盖本地默认值。
 * 后端是 Runner 行为的唯一事实来源，界面不应该自行猜测某个字段是否生效。
 */
export function mergeServerCapabilities(serverCapabilities) {
  if (!Array.isArray(serverCapabilities)) return;
  serverCapabilities.forEach(entry => {
    const descriptor = DESCRIPTORS.find(item => item.id === entry.protocol);
    if (!descriptor) return;
    descriptor.capabilities = {
      ...descriptor.capabilities,
      sourceTimestamp: Boolean(entry.supportsSourceTimestamp),
      sequencePath: Boolean(entry.supportsSequencePath),
      recipeParametersPath: Boolean(entry.supportsRecipeParametersPath),
      connectTimeout: Boolean(entry.supportsConnectTimeout),
      reconnectDelay: Boolean(entry.supportsReconnectDelay),
      perTopicMapping: Boolean(entry.supportsPerTopicMapping),
      registerByteOrder: Boolean(entry.supportsRegisterByteOrder),
      bitAddressing: Boolean(entry.supportsBitAddressing),
    };
    if (Array.isArray(entry.sourceDataTypes) && entry.sourceDataTypes.length)
      descriptor.dataTypes = entry.sourceDataTypes.filter(item => item !== "auto");
    if (Array.isArray(entry.constraints) && entry.constraints.length)
      descriptor.constraints = entry.constraints;
  });
}

export const isRegisterAddressing = protocol =>
  [ADDRESSING.modbusRegister, ADDRESSING.melsecDevice].includes(protocolDescriptor(protocol).addressing);
