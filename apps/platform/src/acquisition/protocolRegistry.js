
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

const DOCUMENT_TYPES = ["auto"];

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

const httpPolling = {
  id: "http-polling",
  label: "HTTP 轮询",
  section: "httpPolling",
  addressing: ADDRESSING.jsonPath,
  probeMode: PROBE_MODE.discover,
  summary: "设备或网关以 HTTP 提供一份 JSON 快照，采集节点按间隔读取。",
  pointLabel: "JSON 字段",
  probeViewLabel: "JSON 字段树",
  dataTypes: DOCUMENT_TYPES,
  capabilities: {
    sourceTimestamp: true,
    sequencePath: true,
    parameterObjectPath: true,
    connectTimeout: true,
    reconnectDelay: true,
    perTopicMapping: false,
    registerByteOrder: false,
    bitAddressing: false,
  },
  constraints: [
    "固定请求头可直接配置；敏感请求头必须引用现场节点密钥库，不会保存在平台配置中。",
    "一次请求必须返回包含全部必需字段的完整快照。",
  ],
  defaults: () => ({
    baseUrl: "", snapshotPath: "/api/v1/snapshot", pollIntervalMs: 1000,
    method: "get", contentType: "application/json", requestBody: "", headersText: "", headerSecretRefsText: "",
  }),
  fields: [
    { name: "baseUrl", label: "服务地址", type: "text", placeholder: "http://192.168.1.10",
      hint: "设备或网关提供 HTTP 服务的根地址。" },
    { name: "snapshotPath", label: "数据路径", type: "text", placeholder: "/api/v1/snapshot",
      hint: "采集节点向该路径发起 GET 请求。" },
    { name: "pollIntervalMs", label: "轮询间隔（ms）", type: "number", min: 1,
      hint: "一次请求完成后等待多久再发起下一次；不是固定采样周期。" },
    { name: "method", label: "请求方法", type: "select", options: [["get", "GET"], ["post", "POST"]] },
    { name: "contentType", label: "请求体类型", type: "text", when: connection => connection.method === "post" },
    { name: "requestBody", label: "请求体", type: "textarea", when: connection => connection.method === "post" },
    { name: "headersText", label: "固定请求头", type: "textarea", hint: "每行 Name: Value；敏感值请使用密钥请求头。" },
    { name: "headerSecretRefsText", label: "密钥请求头", type: "textarea", hint: "每行 Name: secret-ref，值从 Edge 密钥库解析。" },
  ],
  validateConnection(connection) {
    const errors = {};
    const baseUrl = (connection.baseUrl || "").trim();
    if (!/^https?:\/\/\S+$/.test(baseUrl))
      errors.baseUrl = "必须是 http:// 或 https:// 开头的绝对地址。";
    else {
      try {
        const parsed = new URL(baseUrl);
        if (parsed.username || parsed.password || parsed.search || parsed.hash)
          errors.baseUrl = "基础地址不能包含凭据、查询参数或片段。";
      } catch { errors.baseUrl = "设备地址格式无效。"; }
    }
    const snapshotPath = (connection.snapshotPath || "").trim();
    if (!snapshotPath) errors.snapshotPath = "数据路径不能为空。";
    else if (/^(?:[a-z][a-z0-9+.-]*:)?\/\
      errors.snapshotPath = "必须填写相对于设备基础地址的安全路径。";
    if (!(Number(connection.pollIntervalMs) >= 1)) errors.pollIntervalMs = "必须大于 0。";
    return errors;
  },
  validatePoint: (row) => (row.sourcePath || "").trim() ? {} : { sourcePath: "字段路径不能为空。" },
  probeReadiness(form) {
    const connection = form.httpPolling || {};
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
    parameterObjectPath: true,
    connectTimeout: true,
    reconnectDelay: true,
    perTopicMapping: true,
    registerByteOrder: false,
    bitAddressing: false,
  },
  constraints: [
    "订阅多个主题时，请为每个点位指定来源主题；未指定的点位按任意主题的报文解析。",
    "跨主题的值会合并成一份快照，只有全部必需点位都收到过报文后才会产生采样。",
    "订阅过滤器不得重叠；同一报文只能归属一个报文根和稳定通道。",
  ],
  defaults: () => ({
    host: "", port: 1883, protocolVersion: "5.0", clientId: "", username: "", passwordSecretRef: "",
    useTls: false, caCertificatePath: "", clientCertificatePath: "", clientCertificatePasswordSecretRef: "",
    resetSessionOnConnect: true, keepAliveSeconds: 30, snapshotMaxAgeSeconds: 0,
    payloadCompression: "none", payloadEncoding: "utf-8",
    topics: [{ channel: "", topic: "", qos: 0, payloadRoot: "", topicVariables: "" }],
  }),
  fields: [
    { name: "host", label: "消息服务器", type: "text", placeholder: "192.168.1.20" },
    { name: "port", label: "端口", type: "number", min: 1, max: 65535 },
    { name: "protocolVersion", label: "协议版本", type: "select",
      options: [["5.0", "MQTT 5.0"], ["3.1.1", "MQTT 3.1.1"]] },
    { name: "clientId", label: "客户端编号", type: "text", hint: "留空时由采集节点生成唯一编号。" },
    { name: "keepAliveSeconds", label: "保活时间（秒）", type: "number", min: 1 },
    { name: "snapshotMaxAgeSeconds", label: "值的最大陈旧时间（秒）", type: "number", min: 0,
      hint: "跨主题合并时，超过该时间未更新的值视为缺失；订阅多个主题时必须大于 0。" },
    { name: "payloadCompression", label: "报文压缩", type: "select",
      options: [["none", "无"], ["gzip", "GZip"], ["deflate", "Deflate"], ["brotli", "Brotli"]] },
    { name: "payloadEncoding", label: "字符编码", type: "select",
      options: [["utf-8", "UTF-8"], ["gbk", "GBK"], ["gb18030", "GB 18030"], ["big5", "Big5"]] },
    { name: "resetSessionOnConnect", label: form => form.protocolVersion === "5.0" ? "重新开始会话（Clean Start）" : "清理旧会话（Clean Session）",
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
    if (!(connection.username || "").trim() && (connection.passwordSecretRef || "").trim())
      errors.passwordSecretRef = "配置密码凭据时必须同时填写用户名。";
    if (Number(connection.snapshotMaxAgeSeconds) < 0) errors.snapshotMaxAgeSeconds = "不能为负数。";
    const topics = connection.topics || [];
    if (topics.length > 1 && Number(connection.snapshotMaxAgeSeconds) <= 0)
      errors.snapshotMaxAgeSeconds = "订阅多个主题时必须大于 0。";
    if (!topics.some(item => (item.topic || "").trim())) errors.topics = "至少需要一个订阅主题。";
    const seen = new Set();
    const channels = new Set();
    topics.forEach((item, index) => {
      const topic = (item.topic || "").trim();
      if (!topic) return;
      const message = mqttTopicError(topic);
      if (message) errors[`topics[${index}].topic`] = message;
      else if (seen.has(topic)) errors[`topics[${index}].topic`] = "订阅主题不能重复。";
      seen.add(topic);
      const channel = (item.channel || "").trim().toLowerCase();
      if (channel && !/^[a-z0-9][a-z0-9._-]{0,127}$/.test(channel))
        errors[`topics[${index}].channel`] = "通道代码格式无效。";
      else if (channel && channels.has(channel)) errors[`topics[${index}].channel`] = "通道代码不能重复。";
      if (channel) channels.add(channel);
      (item.topicVariables || "").split(",").map(value => value.trim()).filter(Boolean).forEach(variable => {
        if (!/^[a-z0-9][a-z0-9._-]{0,127}:\d+$/.test(variable))
          errors[`topics[${index}].topicVariables`] = "主题变量格式应为 name:层级，例如 equipment:1。";
        else if (Number(variable.slice(variable.lastIndexOf(":") + 1)) >= topic.split("/").length)
          errors[`topics[${index}].topicVariables`] = "主题变量的层级索引超出过滤器范围。";
      });
    });
    topics.forEach((item, left) => topics.slice(left + 1).forEach((candidate, offset) => {
      const first = (item.topic || "").trim();
      const second = (candidate.topic || "").trim();
      if (!mqttTopicError(first) && !mqttTopicError(second) && mqttTopicFiltersIntersect(first, second))
        errors[`topics[${left + offset + 1}].topic`] = `与订阅过滤器 ${first} 会命中同一报文。`;
    }));
    return errors;
  },
  validatePoint: (row) => (row.sourcePath || "").trim() ? {} : { sourcePath: "字段路径不能为空。" },
  probeReadiness(form) {
    const connection = form.mqtt || {};
    return (connection.host || "").trim() && (connection.topics || []).some(item => (item.topic || "").trim())
      ? "" : "请填写消息服务器地址和至少一个订阅主题。";
  },
  advisories(form) {
    const notes = [];
    const topics = (form.mqtt?.topics || []).filter(item => (item.topic || "").trim());
    const points = (form.valueMappings || []).filter(item => item.dataItemCode);
    const unbound = points.filter(item => !(item.topic || "").trim());
    if (topics.length > 1 && unbound.length > 0) {
      notes.push({
        tone: "warning",
        message: `订阅了 ${topics.length} 个主题，但有 ${unbound.length} 个点位没有绑定来源主题。` +
          "未绑定的点位会接受任意主题的报文，主题之间字段重名时会互相覆盖。",
      });
    }
    if (topics.length > 1 && !(Number(form.mqtt?.snapshotMaxAgeSeconds) > 0)) {
      notes.push({
        tone: "warning",
        message: "订阅了多个主题但没有设置值的最大陈旧时间。某个主题停止发布时，" +
          "合并快照会一直沿用它最后一次的值并继续产生采样——这会把过期数据当成当前状态。",
      });
    }
    if (topics.length > 1 && points.length > 0) {
      const bound = points.length - unbound.length;
      notes.push({
        tone: "info",
        message: `跨主题合并：${bound} 个点位已绑定来源主题。只有全部必需点位都收到过报文后才会产生采样；` +
          "只携带上下文的主题会更新快照但不触发采样。",
      });
    }
    return notes;
  },
};

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

export function mqttTopicFiltersIntersect(first, second) {
  const left = first.split("/");
  const right = second.split("/");
  let index = 0;
  while (index < left.length && index < right.length) {
    if (left[index] === "#" || right[index] === "#") return true;
    if (left[index] !== "+" && right[index] !== "+" && left[index] !== right[index]) return false;
    index += 1;
  }
  if (index === left.length && index === right.length) return true;
  return index === left.length ? right[index] === "#" : left[index] === "#";
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
    sourceTimestamp: true,
    intrinsicSourceTimestamp: true,
    sequencePath: false,
    parameterObjectPath: false,
    connectTimeout: true,
    reconnectDelay: true,
    perTopicMapping: false,
    registerByteOrder: false,
    bitAddressing: false,
  },
  constraints: [
    "采样时间固定使用服务器提供的 SourceTimestamp，不能改用采集节点接收时间。",
    "NodeId 中的命名空间序号由服务器分配；服务器重排命名空间后需要重新验证配置。",
    "当前驱动订阅变量节点，不采集 OPC UA 事件和报警。",
  ],
  defaults: () => ({
    endpointUrl: "", securityMode: "none", securityPolicy: "None", authenticationType: "anonymous",
    username: "", passwordSecretRef: "", clientCertificatePath: "", clientCertificatePasswordSecretRef: "",
    trustServerCertificate: false, publishingIntervalMs: 1000, samplingIntervalMs: 1000,
    maximumValueAgeMs: 30000, maximumTimestampSkewMs: 10000,
  }),
  fields: [
    { name: "endpointUrl", label: "服务器端点", type: "text", placeholder: "opc.tcp://192.168.1.10:4840",
      hint: "验证连接时会发现并校验服务器实际提供的安全组合。" },
    { name: "publishingIntervalMs", label: "发布间隔（ms）", type: "number", min: 1,
      hint: "服务器向客户端发送订阅通知的节奏。" },
    { name: "samplingIntervalMs", label: "采样间隔（ms）", type: "number", min: 1,
      hint: "服务器检查变量变化的最快节奏，也是本驱动唯一的节流手段。" },
    { name: "maximumValueAgeMs", label: "必需点位最大值龄（ms）", type: "number", min: 1,
      hint: "超过该时长未更新的必需点位不会进入快照。" },
    { name: "maximumTimestampSkewMs", label: "快照最大时间跨度（ms）", type: "number", min: 0,
      hint: "防止把来源时间相差过大的点位拼成同一条样本。" },
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
    if (!(Number(connection.maximumValueAgeMs) >= 1)) errors.maximumValueAgeMs = "必须大于 0。";
    if (!(Number(connection.maximumTimestampSkewMs) >= 0) ||
        Number(connection.maximumTimestampSkewMs) > Number(connection.maximumValueAgeMs))
      errors.maximumTimestampSkewMs = "必须在 0 到最大值龄之间。";
    if (connection.securityMode !== "none" && connection.securityPolicy === "None")
      errors.securityPolicy = "启用签名或加密时必须选择一个具体的安全策略。";
    if (connection.securityMode !== "none" && !(connection.clientCertificatePath || "").trim())
      errors.clientCertificatePath = "启用安全通道时必须配置客户端证书路径。";
    if (connection.authenticationType === "username" && !(connection.username || "").trim())
      errors.username = "用户名认证需要填写用户名。";
    if (connection.authenticationType === "username" && !(connection.passwordSecretRef || "").trim())
      errors.passwordSecretRef = "用户名认证必须填写密码凭据。";
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
    parameterObjectPath: false,
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
  defaults: () => ({ host: "", port: 502, unitId: 1, addressBase: "zero-based", pollIntervalMs: 1000, maxMergeGap: 8 }),
  fields: [
    { name: "host", label: "设备地址", type: "text", placeholder: "192.168.1.30" },
    { name: "port", label: "端口", type: "number", min: 1, max: 65535 },
    { name: "unitId", label: "从站编号", type: "number", min: 0, max: 255,
      hint: "直连设备通常为 1；经网关访问多个从站时必须按现场设置填写。" },
    { name: "addressBase", label: "地址起点", type: "select",
      options: [["zero-based", "从 0 开始（线缆地址）"], ["one-based", "从 1 开始（手册地址）"]],
      hint: "必须与设备手册的寄存器编号方式一致，填错会整体偏移一个地址。" },
    { name: "pollIntervalMs", label: "轮询间隔（ms）", type: "number", min: 1 },
    { name: "maxMergeGap", label: "合并读取最大间隙", type: "number", min: 0, max: 125,
      hint: "超过该间隙的地址拆成不同请求，避免跨越设备未实现的寄存器区。" },
  ],
  validateConnection(connection) {
    const errors = {};
    if (!(connection.host || "").trim()) errors.host = "设备地址不能为空。";
    const port = Number(connection.port);
    if (!(port >= 1 && port <= 65535)) errors.port = "端口必须在 1-65535 之间。";
    const unit = Number(connection.unitId);
    if (!(unit >= 0 && unit <= 255)) errors.unitId = "从站编号必须在 0-255 之间。";
    if (!(Number(connection.pollIntervalMs) >= 1)) errors.pollIntervalMs = "必须大于 0。";
    ["headersText", "headerSecretRefsText"].forEach(field => {
      const invalid = (connection[field] || "").split(/\r?\n/).map(line => line.trim()).filter(Boolean)
        .find(line => !/^[^\s:]+:\s*\S/.test(line));
      if (invalid) errors[field] = "每行必须是 Name: Value。";
      const restricted = (connection[field] || "").split(/\r?\n/).map(line => line.split(":", 1)[0].trim())
        .find(name => /^(connection|content-length|content-type|host|keep-alive|proxy-connection|te|trailer|transfer-encoding|upgrade)$/i.test(name));
      if (restricted) errors[field] = `${restricted} 由传输层管理，不能在这里设置。`;
      if (field === "headersText") {
        const sensitive = (connection[field] || "").split(/\r?\n/).map(line => line.split(":", 1)[0].trim())
          .find(name => /^(authorization|cookie|proxy-authorization|x-api-key|api-key|x-auth-token)$/i.test(name));
        if (sensitive) errors[field] = `${sensitive} 必须配置为密钥请求头。`;
      }
    });
    const gap = Number(connection.maxMergeGap);
    if (!(gap >= 0 && gap <= 125)) errors.maxMergeGap = "合并读取间隙必须在 0-125 之间。";
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
      const length = Number(row.sourceByteLength);
      if (!(Number.isInteger(length) && length >= 1 && length <= 128))
        errors.sourceByteLength = "文本长度必须在 1-128 字节之间。";
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
    parameterObjectPath: false,
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
      const length = Number(row.sourceByteLength);
      if (!(Number.isInteger(length) && length >= 1 && length <= 128))
        errors.sourceByteLength = "文本长度必须在 1-128 字节之间。";
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

export function mergeServerCapabilities(serverCapabilities) {
  if (!Array.isArray(serverCapabilities)) return;
  serverCapabilities.forEach(entry => {
    const descriptor = DESCRIPTORS.find(item => item.id === entry.protocol);
    if (!descriptor) return;
    descriptor.capabilities = {
      ...descriptor.capabilities,
      sourceTimestamp: Boolean(entry.supportsSourceTimestamp),
      intrinsicSourceTimestamp: Boolean(entry.usesIntrinsicSourceTimestamp),
      sequencePath: Boolean(entry.supportsSequencePath),
      parameterObjectPath: Boolean(entry.supportsControlParametersPath),
      connectTimeout: Boolean(entry.supportsConnectTimeout),
      reconnectDelay: Boolean(entry.supportsReconnectDelay),
      perTopicMapping: Boolean(entry.supportsPerTopicMapping),
      registerByteOrder: Boolean(entry.supportsRegisterByteOrder),
      bitAddressing: Boolean(entry.supportsBitAddressing),
    };
    if (Array.isArray(entry.sourceDataTypes) && entry.sourceDataTypes.length)
      descriptor.dataTypes = [...entry.sourceDataTypes];
    if (Array.isArray(entry.constraints) && entry.constraints.length)
      descriptor.constraints = entry.constraints;
  });
}
