// Register acquisition protocols, connection fields, point validation, and safe probe readiness.
export const ADDRESSING = {
  jsonPath: "json-path",
  mtConnectDataItem: "mtconnect-data-item",
  nodeId: "node-id",
  modbusRegister: "modbus-register",
  melsecDevice: "melsec-device",
  melsecMc3EDevice: "melsec-mc-3e-device",
  siemensS7Address: "siemens-s7-address",
  ethernetIpTag: "ethernet-ip-tag",
  omronFinsAddress: "omron-fins-address",
  bacnetObject: "bacnet-object",
  snmpOid: "snmp-oid",
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
const MTCONNECT_DATA_ITEM_ID = /^[\p{L}_][\p{L}\p{M}\p{N}_.\-\u00b7\u203f\u2040]{0,255}$/u;
const isMtConnectDataItemId = value => {
  const text = String(value ?? "");
  return text.length <= 256 && MTCONNECT_DATA_ITEM_ID.test(text);
};
const hasSafeMtConnectIdentifier = value => {
  const text = String(value ?? "");
  const hasControlCharacter = [...text].some(character => {
    const code = character.charCodeAt(0);
    return code <= 0x1f || (code >= 0x7f && code <= 0x9f);
  });
  return text.trim().length > 0 && text.length <= 256 && !hasControlCharacter;
};
const hasControlCharacters = value => [...String(value ?? "")].some(character => {
  const code = character.charCodeAt(0);
  return code <= 0x1f || (code >= 0x7f && code <= 0x9f);
});
const hasSafeMtConnectDeviceSegment = value => {
  const text = String(value ?? "");
  return hasSafeMtConnectIdentifier(text) && text === text.trim() &&
    text !== "." && text !== ".." && !text.includes("/") && !text.includes("\\");
};

// Canonicalize numeric SNMP OIDs while rejecting symbolic MIB names and invalid arcs.
export function normalizeSnmpOid(value) {
  const text = String(value ?? "").trim().replace(/^\./, "");
  if (!text || text.length > 1024) return null;
  const arcs = text.split(".");
  if (arcs.length < 2 || arcs.length > 128 || arcs.some(arc => !/^\d+$/.test(arc))) return null;
  const numbers = arcs.map(arc => {
    try {
      const number = BigInt(arc);
      return number <= 4294967295n ? number : null;
    } catch {
      return null;
    }
  });
  if (numbers.some(number => number === null)) return null;
  if (numbers[0] > 2n || (numbers[0] < 2n && numbers[1] > 39n)) return null;
  return numbers.map(number => number.toString()).join(".");
}

function snmpOidError(value) {
  return normalizeSnmpOid(value) ? "" : "请输入数字 OID，例如 1.3.6.1.2.1.1.5.0；不支持 MIB 符号名称。";
}

const isEnvironmentSecretReference = value => /^env:[A-Za-z_][A-Za-z0-9_]*$/.test(String(value ?? "").trim());

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

// Keep the binary MC 3E catalog separate from A-compatible 1E, whose X/Y selectors use octal notation.
export const MELSEC_MC3E_DEVICES = [
  { code: "D", bit: false, radix: 10, description: "数据寄存器" },
  { code: "W", bit: false, radix: 16, description: "链接寄存器" },
  { code: "M", bit: true, radix: 10, description: "辅助继电器" },
  { code: "L", bit: true, radix: 10, description: "锁存继电器" },
  { code: "X", bit: true, radix: 16, description: "输入继电器" },
  { code: "Y", bit: true, radix: 16, description: "输出继电器" },
  { code: "B", bit: true, radix: 16, description: "链接继电器" },
];

export const MODBUS_AREAS = [
  { value: "holding-register", label: "保持寄存器（03）", bit: false },
  { value: "input-register", label: "输入寄存器（04）", bit: false },
  { value: "coil", label: "线圈（01）", bit: true },
  { value: "discrete-input", label: "离散输入（02）", bit: true },
];

export const melsecDevice = code => MELSEC_DEVICES.find(item => item.code === (code || "").toUpperCase());
export const melsecMc3eDevice = code => MELSEC_MC3E_DEVICES.find(item => item.code === (code || "").toUpperCase());
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

// Validate one binary MC 3E source selector without inheriting A-compatible 1E address radix rules.
export function melsecMc3eSelectorError(selector, dataType, family) {
  const text = String(selector || "").trim();
  const match = /^([A-Za-z]+):([0-9A-Fa-f]+)(?:\.(\d{1,2}))?(?::([a-z0-9]+))?$/.exec(text);
  if (!match) return "格式应为 D:100:int16、X:1A:boolean 或 D:100.3:boolean。";
  const [, rawCode, addressText, bitText, rawType] = match;
  if (!rawType) return "MC 3E 选择器必须带数据类型后缀，例如 D:100:int16。";
  if (!["q-l", "iq-r", "iq-f"].includes(family)) return "请先选择 PLC 系列，才能校验该系列的地址上限。";
  const device = melsecMc3eDevice(rawCode);
  if (!device) return "请选择 MC 3E 支持的软元件区：D、W、M、L、X、Y、B。";
  const digits = device.radix === 16 ? /^[0-9a-f]+$/i : /^\d+$/;
  if (!digits.test(addressText)) return `${device.code} 地址应使用${RADIX_LABEL[device.radix]}编号。`;
  const address = Number.parseInt(addressText, device.radix);
  const maximumAddress = family === "iq-r" ? 0xFFFFFFFF : 0xFFFFFF;
  if (!Number.isSafeInteger(address) || address < 0 || address > maximumAddress)
    return `该系列地址必须在 0-${family === "iq-r" ? "0xFFFFFFFF" : "0xFFFFFF"} 范围内，实际有效范围还取决于 PLC 型号和参数。`;
  const bit = bitText === undefined ? null : Number(bitText);
  if (bit !== null && (!Number.isInteger(bit) || bit < 0 || bit > 15)) return "字软元件位偏移必须在 0-15 之间。";
  const normalizedType = String(dataType || rawType || "").toLowerCase();
  if (!MC3E_TYPES.includes(normalizedType)) return `MC 3E 不支持数据类型 ${normalizedType || "（空）"}。`;
  if (dataType && rawType !== normalizedType) return `地址类型 ${rawType} 与映射类型 ${dataType} 不一致。`;
  if (device.bit && bit !== null) return `${device.code} 本身就是位软元件，不应再填写位偏移。`;
  if (device.bit && normalizedType !== "boolean") return `${device.code} 是位软元件，只支持 boolean。`;
  if (!device.bit && normalizedType === "boolean" && bit === null) return "字软元件读取 boolean 时需要位偏移，例如 D:100.3:boolean。";
  if (normalizedType !== "boolean" && bit !== null) return "只有 boolean 点位可以填写字内位偏移。";
  const words = ["int32", "uint32", "float32"].includes(normalizedType) ? 2
    : ["int64", "uint64", "float64"].includes(normalizedType) ? 4 : 1;
  if (!device.bit && address + words - 1 > maximumAddress)
    return `多字点位的读取范围不能超过 ${family === "iq-r" ? "0xFFFFFFFF" : "0xFFFFFF"}。`;
  return "";
}

function wordCount(dataType, byteLength) {
  if (dataType === "string") return Math.max(1, Math.ceil((Number(byteLength) || 2) / 2));
  if (["int32", "uint32", "float32"].includes(dataType)) return 2;
  if (["int64", "uint64", "float64"].includes(dataType)) return 4;
  return 1;
}

export const registerWordCount = wordCount;

const MC3E_TYPES = ["boolean", "int16", "uint16", "int32", "uint32", "float32", "int64", "uint64", "float64"];

const httpPolling = {
  id: "http-polling",
  label: "HTTP 轮询",
  section: "httpPolling",
  addressing: ADDRESSING.jsonPath,
  probeMode: PROBE_MODE.discover,
  summary: "设备或网关以 HTTP 提供一份 JSON 快照，现场节点按间隔读取。",
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
      hint: "现场节点向该路径发起 GET 请求。" },
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
    else if (/^(?:[a-z][a-z0-9+.-]*:)?\/\//i.test(snapshotPath) || snapshotPath.includes("\n") || snapshotPath.includes("\r"))
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

const mtConnect = {
  id: "mtconnect",
  label: "MTConnect（CNC）",
  section: "mtConnect",
  addressing: ADDRESSING.mtConnectDataItem,
  probeMode: PROBE_MODE.discover,
  summary: "连接 CNC 的 MTConnect Agent，通过 /probe 浏览 DataItem，并按配置 ID 读取 /current。",
  pointLabel: "MTConnect DataItem ID",
  probeViewLabel: "CNC DataItem 目录",
  dataTypes: DOCUMENT_TYPES,
  capabilities: {
    sourceTimestamp: false,
    sequencePath: false,
    parameterObjectPath: false,
    connectTimeout: true,
    reconnectDelay: true,
    perTopicMapping: false,
    registerByteOrder: false,
    bitAddressing: false,
  },
  constraints: [
    "现场节点只发起 GET /probe 和 /current；不能写设备或扫描 CNC 地址。",
    "点位使用设备模型中的 DataItem ID；运行时只请求画布已配置的 ID。",
    "首期读取标量 Sample / Event；Condition 与复合值不进入普通工艺变量映射。",
    "/current 返回轮询时的最新快照，无法保证捕获两次轮询间短暂出现的状态。采样时间使用现场节点接收时间。",
  ],
  defaults: () => ({
    baseUrl: "", deviceNameOrUuid: "", pollIntervalMs: 1000, maximumResponseBytes: 4194304,
    headersText: "", headerSecretRefsText: "",
  }),
  fields: [
    { name: "baseUrl", label: "MTConnect Agent 地址", type: "text", placeholder: "http://192.168.1.20:5000",
      hint: "填写 Agent 根地址；可包含反向代理前缀，不能包含凭据、查询参数或片段。" },
    { name: "deviceNameOrUuid", label: "设备名称或 UUID", type: "text", placeholder: "CNC-01",
      hint: "指定 Agent 中的一台 CNC，避免多设备 Agent 返回其他设备的点位。" },
    { name: "pollIntervalMs", label: "轮询间隔（ms）", type: "number", min: 100, max: 3600000,
      hint: "读取 /current 最新快照的间隔。" },
    { name: "maximumResponseBytes", label: "响应上限（字节）", type: "number", min: 1024, max: 16777216,
      hint: "限制 /probe 和 /current XML 响应体；默认 4 MiB，最大 16 MiB。" },
    { name: "headersText", label: "固定请求头", type: "textarea", hint: "每行 Name: Value；敏感值请使用密钥请求头。Accept 固定为 application/xml。" },
    { name: "headerSecretRefsText", label: "密钥请求头", type: "textarea", hint: "每行 Name: secret-ref，值从 Edge 密钥库解析；Accept 由驱动固定。" },
  ],
  validateConnection(connection) {
    const errors = {};
    const baseUrl = (connection.baseUrl || "").trim();
    if (!/^https?:\/\/\S+$/i.test(baseUrl)) {
      errors.baseUrl = "必须填写 http:// 或 https:// 开头的 Agent 绝对地址。";
    } else {
      try {
        const parsed = new URL(baseUrl);
        if (parsed.username || parsed.password || parsed.search || parsed.hash)
          errors.baseUrl = "Agent 地址不能包含凭据、查询参数或片段。";
      } catch { errors.baseUrl = "Agent 地址格式无效。"; }
    }
    if (!hasSafeMtConnectDeviceSegment(connection.deviceNameOrUuid))
      errors.deviceNameOrUuid = "请填写 1-256 字符的设备名称或 UUID；不能包含控制字符、路径分隔符或首尾空白。";
    const interval = Number(connection.pollIntervalMs);
    if (!(Number.isInteger(interval) && interval >= 100 && interval <= 3600000))
      errors.pollIntervalMs = "轮询间隔必须是 100-3600000ms 之间的整数。";
    const maximumResponseBytes = Number(connection.maximumResponseBytes);
    if (!(Number.isInteger(maximumResponseBytes) && maximumResponseBytes >= 1024 && maximumResponseBytes <= 16777216))
      errors.maximumResponseBytes = "响应上限必须是 1024-16777216 字节之间的整数。";
    for (const field of ["headersText", "headerSecretRefsText"]) {
      const hasAccept = (connection[field] || "").split(/\r?\n/)
        .map(line => line.split(":", 1)[0].trim())
        .some(name => name.toLowerCase() === "accept");
      if (hasAccept) errors[field] = "MTConnect 固定使用 application/xml，不能配置自定义 Accept 请求头。";
    }
    return errors;
  },
  validatePoint(row) {
    return isMtConnectDataItemId(row.sourcePath)
      ? {}
      : { sourcePath: "请填写有效的 MTConnect DataItem ID（Unicode NCName，最多 256 字符）；ID 区分大小写并按 /probe 目录校验。" };
  },
  probeReadiness(form) {
    const connection = form.mtConnect || {};
    if (!/^https?:\/\/\S+$/i.test((connection.baseUrl || "").trim())) return "请填写 MTConnect Agent 地址。";
    if (!hasSafeMtConnectDeviceSegment(connection.deviceNameOrUuid)) return "请填写设备名称或 UUID（单一路径段，最多 256 字符，首尾不能有空白）。";
    return "";
  },
};

const mqtt = {
  id: "mqtt",
  label: "MQTT 订阅",
  section: "mqtt",
  addressing: ADDRESSING.jsonPath,
  probeMode: PROBE_MODE.discover,
  summary: "设备或网关主动向消息服务器发布 JSON 报文，现场节点订阅接收。",
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
    { name: "clientId", label: "客户端编号", type: "text", hint: "留空时由现场节点生成唯一编号。" },
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
      hint: "填写现场节点密钥库中的名称，不在配置中保存明文。" },
    { name: "useTls", label: "启用 TLS", type: "checkbox", group: "认证" },
    { name: "caCertificatePath", label: "CA 证书", type: "text", group: "认证",
      when: connection => connection.useTls, hint: "现场节点上的证书文件路径。" },
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
    "采样时间固定使用服务器提供的 SourceTimestamp，不能改用现场节点接收时间。",
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

function validateModbusPoint(row, connection) {
  const errors = {};
  const area = modbusArea(row.modbusArea);
  if (!area) { errors.modbusArea = "请选择寄存器区。"; return errors; }
  const address = String(row.modbusAddress ?? "").trim();
  if (!/^\d+$/.test(address)) errors.modbusAddress = "地址必须是非负整数。";
  else {
    const numeric = Number(address);
    if (numeric > 65535) errors.modbusAddress = "地址不能超过 65535。";
    if (connection?.addressBase === "one-based" && numeric < 1)
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
}

function modbusProbeReadiness(connection, mappings) {
  if (!connection?.host && !connection?.portName) return "请先填写设备地址或串口名。";
  return (mappings || []).some(item => String(item.modbusAddress ?? "").trim())
    ? "" : "寄存器协议不会盲扫地址，请先配置至少一个寄存器点位。";
}

function modbusAdvisories(form, connection) {
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
  if (connection?.addressBase === "one-based")
    notes.push({ tone: "info", message: "当前按手册地址填写，现场节点发送请求前会自动减 1。" });
  return notes;
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
  validatePoint(row, form) { return validateModbusPoint(row, form.modbusTcp); },
  probeReadiness(form) {
    return modbusProbeReadiness(form.modbusTcp, form.valueMappings);
  },
  advisories(form) { return modbusAdvisories(form, form.modbusTcp); },
};

const modbusRtu = {
  ...modbusTcp,
  id: "modbus-rtu",
  label: "Modbus RTU（串口 / RS-485）",
  section: "modbusRtu",
  summary: "通过串口或 RS-485 总线读取仪表、变频器、远程 I/O 和 PLC 上已配置的寄存器。",
  constraints: [
    "只回读明确配置的寄存器，不扫描地址或写入设备。",
    "同一串口总线可以配置多个从站；串口参数必须一致，现场节点会串行访问总线。",
    "串口名还必须加入现场节点的允许列表，并具有操作系统设备权限。",
  ],
  defaults: () => ({
    portName: "", baudRate: 9600, dataBits: 8, parity: "even", stopBits: "one",
    unitId: 1, addressBase: "zero-based", pollIntervalMs: 1000, maxMergeGap: 8,
  }),
  fields: [
    { name: "portName", label: "串口名", type: "text", placeholder: "COM3 或 /dev/ttyUSB0",
      hint: "填写现场节点上的串口名；容器运行时还需映射设备并配置权限。" },
    { name: "baudRate", label: "波特率", type: "number", min: 300, max: 4000000,
      hint: "必须与总线设备一致，常见值为 9600、19200、38400、57600 或 115200。" },
    { name: "dataBits", label: "数据位", type: "select", options: [[8, "8 位"]] },
    { name: "parity", label: "校验位", type: "select", options: [["none", "无校验"], ["even", "偶校验"], ["odd", "奇校验"]] },
    { name: "stopBits", label: "停止位", type: "select", options: [["one", "1 位"], ["two", "2 位"]] },
    { name: "unitId", label: "从站编号", type: "number", min: 1, max: 247,
      hint: "一条 RS-485 总线可配置多个不同从站编号；广播地址 0 不能用于读取。" },
    { name: "addressBase", label: "地址起点", type: "select",
      options: [["zero-based", "从 0 开始（线缆地址）"], ["one-based", "从 1 开始（手册地址）"]],
      hint: "必须与设备手册的寄存器编号方式一致，填错会整体偏移一个地址。" },
    { name: "pollIntervalMs", label: "轮询间隔（ms）", type: "number", min: 1, max: 3600000 },
    { name: "maxMergeGap", label: "合并读取最大间隙", type: "number", min: 0, max: 125,
      hint: "超过该间隙的地址拆成不同请求，避免跨越设备未实现的寄存器区。" },
  ],
  validateConnection(connection) {
    const errors = {};
    const portName = (connection.portName || "").trim();
    const containsControlCharacter = [...portName].some(character => {
      const code = character.charCodeAt(0);
      return code < 32 || code === 127;
    });
    if (!portName) errors.portName = "串口名不能为空。";
    else if (portName.length > 260 || containsControlCharacter ||
      !(/^(?:COM\d+|\\\\\.\\COM\d+)$/i.test(portName) || /^\/dev\/[A-Za-z0-9._/-]+$/.test(portName)) ||
      portName.split(/[\\/]/).some(segment => segment === ".."))
      errors.portName = "请填写 COMn、\\\\.\\COMn 或 /dev 下的串口路径。";
    const baudRate = Number(connection.baudRate);
    if (!(Number.isInteger(baudRate) && baudRate >= 300 && baudRate <= 4000000))
      errors.baudRate = "波特率必须是 300-4000000 之间的整数。";
    if (Number(connection.dataBits) !== 8) errors.dataBits = "Modbus RTU 固定使用 8 个数据位。";
    if (!["none", "even", "odd"].includes(connection.parity)) errors.parity = "校验位必须为无、偶或奇。";
    if (!["one", "two"].includes(connection.stopBits)) errors.stopBits = "停止位必须为 1 或 2。";
    const unit = Number(connection.unitId);
    if (!(Number.isInteger(unit) && unit >= 1 && unit <= 247)) errors.unitId = "从站编号必须在 1-247 之间，0 为广播地址，不能用于读取。";
    if (!["zero-based", "one-based"].includes(connection.addressBase)) errors.addressBase = "请选择合法的地址起点。";
    const interval = Number(connection.pollIntervalMs);
    if (!(Number.isInteger(interval) && interval >= 1 && interval <= 3600000)) errors.pollIntervalMs = "轮询间隔必须是 1-3600000ms 之间的整数。";
    const gap = Number(connection.maxMergeGap);
    if (!(Number.isInteger(gap) && gap >= 0 && gap <= 125)) errors.maxMergeGap = "合并读取间隙必须是 0-125 之间的整数。";
    return errors;
  },
  validatePoint(row, form) { return validateModbusPoint(row, form.modbusRtu); },
  probeReadiness(form) {
    if (!(form.modbusRtu?.portName || "").trim()) return "请先填写串口名。";
    return modbusProbeReadiness(form.modbusRtu, form.valueMappings);
  },
  advisories(form) { return modbusAdvisories(form, form.modbusRtu); },
};

const modbusAscii = {
  ...modbusRtu,
  id: "modbus-ascii",
  label: "Modbus ASCII（串口 / RS-485）",
  section: "modbusAscii",
  summary: "通过串口或 RS-485 总线读取称重仪表、过程仪表和旧设备上已配置的寄存器。",
  constraints: [
    "只回读用户明确配置的寄存器，不扫描地址或写入设备。",
    "Modbus ASCII 默认使用 7 个数据位、偶校验和 1 个停止位；无校验时使用 2 个停止位。",
    "同一串口总线可以访问不同从站，但串口参数和 RTU/ASCII 帧格式必须一致。",
    "串口名必须加入现场节点的允许列表，并具有操作系统设备权限。",
  ],
  defaults: () => ({
    portName: "", baudRate: 9600, dataBits: 7, parity: "even", stopBits: "one",
    unitId: 1, addressBase: "zero-based", pollIntervalMs: 1000, maxMergeGap: 8,
  }),
  fields: [
    { name: "portName", label: "串口名", type: "text", placeholder: "COM3 或 /dev/ttyUSB0",
      hint: "填写现场节点上的串口名；容器运行时还需映射设备并配置权限。" },
    { name: "baudRate", label: "波特率", type: "number", min: 300, max: 4000000,
      hint: "必须与总线设备一致，常见值为 9600、19200、38400、57600 或 115200。" },
    { name: "dataBits", label: "数据位", type: "select", options: [[7, "7 位（Modbus ASCII）"]] },
    { name: "parity", label: "校验位", type: "select", options: [["none", "无校验"], ["even", "偶校验"], ["odd", "奇校验"]] },
    { name: "stopBits", label: "停止位", type: "select", options: [["one", "1 位"], ["two", "2 位"]],
      hint: "奇偶校验启用时使用 1 位；无校验时使用 2 位。" },
    { name: "unitId", label: "从站编号", type: "number", min: 1, max: 247,
      hint: "一条 RS-485 总线可配置多个不同从站编号；广播地址 0 不能用于读取。" },
    { name: "addressBase", label: "地址起点", type: "select",
      options: [["zero-based", "从 0 开始（线缆地址）"], ["one-based", "从 1 开始（手册地址）"]],
      hint: "必须与设备手册的寄存器编号方式一致，填错会整体偏移一个地址。" },
    { name: "pollIntervalMs", label: "轮询间隔（ms）", type: "number", min: 1, max: 3600000 },
    { name: "maxMergeGap", label: "合并读取最大间隙", type: "number", min: 0, max: 125,
      hint: "超过该间隙的地址拆成不同请求，避免跨越设备未实现的寄存器区。" },
  ],
  validateConnection(connection) {
    const errors = modbusRtu.validateConnection({ ...connection, dataBits: 8 });
    delete errors.dataBits;
    if (Number(connection.dataBits) !== 7) errors.dataBits = "Modbus ASCII 使用 7 个数据位。";
    if (connection.parity === "none" && connection.stopBits !== "two")
      errors.stopBits = "无校验时必须使用 2 个停止位。";
    if (["even", "odd"].includes(connection.parity) && connection.stopBits !== "one")
      errors.stopBits = "启用奇偶校验时必须使用 1 个停止位。";
    return errors;
  },
  validatePoint(row, form) { return validateModbusPoint(row, form.modbusAscii); },
  probeReadiness(form) {
    if (!(form.modbusAscii?.portName || "").trim()) return "请先填写串口名。";
    return modbusProbeReadiness(form.modbusAscii, form.valueMappings);
  },
  advisories(form) { return modbusAdvisories(form, form.modbusAscii); },
};

const melsecA1e = {
  id: "melsec-a1e",
  label: "三菱 MC 协议（A 兼容 1E 帧）",
  section: "melsecA1E",
  addressing: ADDRESSING.melsecDevice,
  probeMode: PROBE_MODE.configuredPointsOnly,
  summary: "使用三菱 MC A 兼容 1E 帧，经以太网 TCP 接入；帧格式和软元件寻址不同于 Modbus TCP。直连 FX3U-ENET(-L/-ADP) 等模块读取已配置软元件。",
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

const melsecMc3E = {
  id: "melsec-mc-3e",
  label: "三菱 MC 协议（二进制 3E 帧）",
  section: "melsecMc3E",
  addressing: ADDRESSING.melsecMc3EDevice,
  probeMode: PROBE_MODE.configuredPointsOnly,
  summary: "使用三菱 MC 二进制 3E 帧，经以太网 TCP 接入；帧格式和软元件寻址不同于 Modbus TCP。只读回读已配置软元件，不扫描地址或写入 PLC。",
  pointLabel: "MC 3E 软元件选择器",
  probeViewLabel: "已配置软元件回读",
  defaultDataType: "int16",
  dataTypes: MC3E_TYPES,
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
    "仅支持二进制 MC 3E 帧和已配置点位；不会扫描地址，也不会写入 PLC。",
    "首期支持 X、Y、M、L、B、D、W；M/L/X/Y/B 只读 boolean，D/W 支持数字类型，D/W 的 boolean 需使用 .0-.15 位偏移。",
    "X/Y/B/W 地址按十六进制书写，M/L/D 按十进制书写；此规则与 A 兼容 1E 的 X/Y 八进制语义分开。",
    "地址上限按帧格式校验：Q/L 与 iQ-F 为 0xFFFFFF，iQ-R 为 0xFFFFFFFF；实际有效范围仍取决于具体 PLC 型号和设备参数。",
  ],
  defaults: () => ({
    host: "", port: "", family: "", networkNumber: 0, pcNumber: 255,
    moduleIoNumber: 1023, stationNumber: 0, monitoringTimer: 16,
    pollIntervalMs: 1000, maxMergeGap: 0,
  }),
  fields: [
    { name: "host", label: "PLC 地址", type: "text", placeholder: "192.168.1.40" },
    { name: "port", label: "MC 3E 端口", type: "number", min: 1, max: 65535,
      hint: "必须填写 PLC 工程中配置的 MC 协议端口；此协议没有通用默认端口。" },
    { name: "family", label: "PLC 系列", type: "select", options: [["", "请选择 PLC 系列"], ["q-l", "Q / L 系列"], ["iq-r", "iQ-R 系列"], ["iq-f", "iQ-F 系列"]],
      hint: "选择与 PLC 型号匹配的二进制 3E 设备代码和帧格式。" },
    { name: "pollIntervalMs", label: "轮询间隔（ms）", type: "number", min: 1, max: 3600000 },
    { name: "networkNumber", label: "网络号", type: "number", min: 0, max: 255, group: "路由参数" },
    { name: "pcNumber", label: "PC 号", type: "number", min: 0, max: 255, group: "路由参数" },
    { name: "moduleIoNumber", label: "模块 I/O 号", type: "number", min: 0, max: 65535, group: "路由参数",
      hint: "直连 CPU 模块常用 0x03FF（十进制 1023）；按 PLC 网络路由配置填写。" },
    { name: "stationNumber", label: "站号", type: "number", min: 0, max: 255, group: "路由参数" },
    { name: "monitoringTimer", label: "监视定时器", type: "number", min: 0, max: 65535, group: "帧参数",
      hint: "单位按 MC 协议帧定义；设置值需覆盖现场 PLC 的最长响应时间。" },
    { name: "maxMergeGap", label: "合并读取间隔", type: "number", min: 0, max: 256,
      hint: "地址间隔不超过该值时尝试合并读取；设为 0 表示逐段读取。" },
  ],
  validateConnection(connection) {
    const errors = {};
    if (!(connection.host || "").trim()) errors.host = "PLC 地址不能为空。";
    const port = Number(connection.port);
    if (!(Number.isInteger(port) && port >= 1 && port <= 65535)) errors.port = "端口必须是 1-65535 之间的整数。";
    if (!["q-l", "iq-r", "iq-f"].includes(connection.family)) errors.family = "请选择 PLC 系列。";
    for (const [field, label, maximum] of [
      ["networkNumber", "网络号", 255], ["pcNumber", "PC 号", 255],
      ["moduleIoNumber", "模块 I/O 号", 65535], ["stationNumber", "站号", 255],
      ["monitoringTimer", "监视定时器", 65535],
    ]) {
      const value = Number(connection[field]);
      if (!(Number.isInteger(value) && value >= 0 && value <= maximum)) errors[field] = `${label}必须是 0-${maximum} 之间的整数。`;
    }
    const interval = Number(connection.pollIntervalMs);
    if (!(Number.isInteger(interval) && interval >= 1 && interval <= 3600000)) errors.pollIntervalMs = "轮询间隔必须是 1-3600000ms 之间的整数。";
    const gap = Number(connection.maxMergeGap);
    if (!(Number.isInteger(gap) && gap >= 0 && gap <= 256)) errors.maxMergeGap = "合并读取间隔必须是 0-256 之间的整数。";
    return errors;
  },
  validatePoint(row, form) {
    const errors = {};
    if (!this.dataTypes.includes(row.sourceDataType))
      errors.sourceDataType = "请选择 MC 3E 支持的标量数据类型。";
    const selectorError = melsecMc3eSelectorError(row.sourcePath, row.sourceDataType, form.melsecMc3E?.family);
    if (selectorError) errors.sourcePath = selectorError;
    return errors;
  },
  probeReadiness(form) {
    if (!(form.melsecMc3E?.host || "").trim()) return "请先填写 PLC 地址。";
    if (!Number.isInteger(Number(form.melsecMc3E?.port))) return "请先填写 MC 3E 端口。";
    if (!(form.melsecMc3E?.family || "")) return "请先选择 PLC 系列。";
    return (form.valueMappings || []).some(row => (row.sourcePath || "").trim())
      ? ""
      : "MC 3E 只回读已配置软元件，请先填写至少一个点位。";
  },
};

const siemensS7 = {
  id: "siemens-s7",
  label: "Siemens S7",
  section: "siemensS7",
  addressing: ADDRESSING.siemensS7Address,
  probeMode: PROBE_MODE.configuredPointsOnly,
  summary: "通过 S7 ISO-on-TCP 读取已配置的 PLC 绝对地址。",
  pointLabel: "S7 地址",
  probeViewLabel: "S7 地址回读",
  dataTypes: [...REGISTER_TYPES, "byte"],
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
    "只读回读已声明的地址，不扫描 PLC，也不写入 PLC。",
    "S7-1200/1500 访问 DB 时需按 CPU 安全设置允许 PUT/GET，并为该 DB 使用非优化访问。",
    "CPU 系列、机架和槽位须与现场硬件配置一致；驱动按 S7 大端字节序解码。",
  ],
  defaults: () => ({
    host: "", port: 102, cpuType: "S71200", rack: 0, slot: 0, pollIntervalMs: 1000, maxMergeGap: 0,
  }),
  fields: [
    { name: "host", label: "PLC 地址", type: "text", placeholder: "192.168.1.50" },
    { name: "port", label: "S7 端口", type: "number", min: 1, max: 65535,
      hint: "默认 ISO-on-TCP 端口为 102；如经由网关转发，请填写网关映射端口。" },
    { name: "cpuType", label: "CPU 系列", type: "select", options: [
      ["S7200", "S7-200"], ["S7200Smart", "S7-200 SMART"], ["S7300", "S7-300"],
      ["S7400", "S7-400"], ["S71200", "S7-1200"], ["S71500", "S7-1500"], ["Logo0BA8", "LOGO! 0BA8"],
    ] },
    { name: "rack", label: "机架", type: "number", min: 0, max: 7 },
    { name: "slot", label: "CPU 槽位", type: "number", min: 0, max: 31,
      hint: "S7-1200/1500 常见为 0；S7-300/400 常见为 2，按实际 CPU 配置填写。" },
    { name: "pollIntervalMs", label: "轮询间隔（ms）", type: "number", min: 1 },
    { name: "maxMergeGap", label: "连续地址合并间隔（字节）", type: "number", min: 0, max: 256,
      hint: "默认 0，仅合并相邻或重叠点位，不读取未声明的地址间隙。" },
  ],
  validateConnection(connection) {
    const errors = {};
    if (!(connection.host || "").trim()) errors.host = "PLC 地址不能为空。";
    if (!(Number(connection.port) >= 1 && Number(connection.port) <= 65535)) errors.port = "端口必须在 1-65535 之间。";
    if (!["S7200", "S7200Smart", "S7300", "S7400", "S71200", "S71500", "Logo0BA8"].includes(connection.cpuType)) errors.cpuType = "请选择支持的 CPU 系列。";
    if (!(Number(connection.rack) >= 0 && Number(connection.rack) <= 7)) errors.rack = "机架号必须在 0-7 之间。";
    if (!(Number(connection.slot) >= 0 && Number(connection.slot) <= 31)) errors.slot = "CPU 槽位必须在 0-31 之间。";
    if (!(Number(connection.pollIntervalMs) >= 1)) errors.pollIntervalMs = "必须大于 0。";
    if (!(Number(connection.maxMergeGap) >= 0 && Number(connection.maxMergeGap) <= 256)) errors.maxMergeGap = "合并间隔必须在 0-256 字节之间。";
    return errors;
  },
  validatePoint(row) {
    const message = siemensS7AddressError(row.sourcePath, row.sourceDataType, row.sourceByteLength);
    return message ? { sourcePath: message } : {};
  },
  probeReadiness(form) {
    if (!(form.siemensS7?.host || "").trim()) return "请先填写 PLC 地址。";
    return (form.valueMappings || []).some(row => (row.sourcePath || "").trim())
      ? ""
      : "Siemens S7 不支持地址扫描，请先配置至少一个绝对地址，例如 DB1.DBW0。";
  },
};

export function siemensS7AddressError(value, dataType, sourceByteLength) {
  const text = String(value || "").trim().toUpperCase();
  const db = /^DB(\d+)\.DB([XBWDL])(\d+)(?:\.([0-7]))?$/.exec(text);
  const area = /^(I|Q|M)([BWDL]?)(\d+)(?:\.([0-7]))?$/.exec(text);
  if (!db && !area) return "地址格式无效；支持 DB1.DBW0、DB1.DBX0.0、MW10、I0.0 等绝对地址。";
  const match = db || area;
  const inDb = Boolean(db);
  const access = inDb ? match[2] : match[2];
  const offset = Number(inDb ? match[3] : match[3]);
  const bit = inDb ? match[4] : match[4];
  if (inDb && !(Number(match[1]) >= 1 && Number(match[1]) <= 65535)) return "DB 编号必须在 1-65535 之间。";
  if (!Number.isSafeInteger(offset) || offset < 0 || offset > 16777215) return "字节偏移必须在 0-16777215 之间。";
  if (bit && !["X", ""].includes(access)) return "位偏移只能用于 DBX 或 M/I/Q 位地址。";
  if (access === "X" && !bit) return "DBX 地址必须填写位偏移，例如 DB1.DBX0.0。";
  if (!access && !bit) return "M/I/Q 地址须指定字节、字、双字、长字或位偏移。";
  const inferred = bit ? "boolean" : ({ B: "byte", W: "uint16", D: "uint32", L: "uint64" }[access] || "");
  const type = dataType || inferred;
  const expected = ({
    byte: "B",
    boolean: "X", int16: "W", uint16: "W", int32: "D", uint32: "D", float32: "D",
    int64: "L", uint64: "L", float64: "L", string: "B",
  })[type];
  if (!expected) return `不支持 S7 源数据类型 ${type || "（空）"}。`;
  if (Boolean(bit) !== (type === "boolean") ||
      (bit && !["X", ""].includes(access)) ||
      (!bit && access !== expected)) return `地址 ${text} 与源数据类型 ${type} 不匹配。`;
  if (type === "string" && (!inDb || !(Number(sourceByteLength) >= 1 && Number(sourceByteLength) <= 198)))
    return "S7 STRING 仅支持 DBB 地址，长度必须为 1-198 个字符。";
  const byteCount = type === "string" ? Number(sourceByteLength) + 2
    : type === "boolean" || type === "byte" ? 1
      : ["int16", "uint16"].includes(type) ? 2
        : ["int32", "uint32", "float32"].includes(type) ? 4 : 8;
  if (offset + byteCount > 16777216) return "地址读取范围超出允许的字节地址边界。";
  return "";
}

const ethernetIp = {
  id: "ethernet-ip",
  label: "Rockwell EtherNet/IP",
  section: "ethernetIp",
  addressing: ADDRESSING.ethernetIpTag,
  probeMode: PROBE_MODE.configuredPointsOnly,
  summary: "通过 EtherNet/IP 显式读取 ControlLogix、CompactLogix 或 Micro800 中已配置的标签。",
  pointLabel: "PLC 标签",
  probeViewLabel: "已配置标签回读",
  defaultDataType: "int32",
  dataTypes: ["boolean", "int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64", "float32", "float64"],
  capabilities: {
    sourceTimestamp: true,
    sequencePath: false,
    parameterObjectPath: false,
    connectTimeout: true,
    reconnectDelay: true,
    perTopicMapping: false,
    registerByteOrder: false,
    bitAddressing: false,
  },
  constraints: [
    "仅读取已配置的标量标签，不枚举标签、不扫描地址、不写入 PLC。",
    "ControlLogix / CompactLogix 使用控制器路由路径；Micro800 不填写路由路径。",
    "当前支持 Boolean、8/16/32/64 位有符号及无符号整数、32/64 位浮点；不支持 STRING、UDT 与隐式 I/O。",
    "单任务最多读取 256 个不同标签；当前不支持动态上下文、质量路径或配方版本辅助标签。",
  ],
  defaults: () => ({
    host: "", port: 44818, controllerFamily: "control-logix", path: "1,0", pollIntervalMs: 1000,
  }),
  fields: [
    { name: "host", label: "PLC 地址", type: "text", placeholder: "192.168.1.50" },
    { name: "port", label: "EtherNet/IP 端口", type: "number", min: 1, max: 65535,
      hint: "默认 EtherNet/IP 显式消息端口为 44818；经网关转发时填写映射端口。" },
    { name: "controllerFamily", label: "控制器系列", type: "select", options: [
      ["control-logix", "ControlLogix / CompactLogix"], ["micro800", "Micro800"],
    ] },
    { name: "path", label: "控制器路由路径", type: "text", placeholder: "1,0",
      when: connection => connection.controllerFamily === "control-logix",
      hint: "填写直连背板与 CPU 槽位，例如 1,0；按现场网络桥接拓扑配置。" },
    { name: "pollIntervalMs", label: "轮询间隔（ms）", type: "number", min: 1,
      hint: "一次读取完成后等待多久再发起下一次；不是固定采样周期。" },
  ],
  validateConnection(connection) {
    const errors = {};
    if (!(connection.host || "").trim()) errors.host = "PLC 地址不能为空。";
    if (!(Number(connection.port) >= 1 && Number(connection.port) <= 65535)) errors.port = "端口必须在 1-65535 之间。";
    if (!(Number(connection.pollIntervalMs) >= 1)) errors.pollIntervalMs = "必须大于 0。";
    if (!["control-logix", "micro800"].includes(connection.controllerFamily)) errors.controllerFamily = "请选择支持的控制器系列。";
    if (connection.controllerFamily === "control-logix" && !/^1,(?:[0-9]|[12][0-9]|3[01])$/.test((connection.path || "").trim()))
      errors.path = "ControlLogix / CompactLogix 路径应为 1,0 至 1,31 的直连槽位。";
    return errors;
  },
  validatePoint(row) {
    const tag = String(row.sourcePath || "").trim();
    const errors = {};
    if (!tag) errors.sourcePath = "PLC 标签不能为空。";
    else if (tag.length > 255 || !/^(?:Program:[A-Za-z_][A-Za-z0-9_]*\.)?[A-Za-z_][A-Za-z0-9_]*(?:(?:\.[A-Za-z_][A-Za-z0-9_]*)|(?:\[\d+\]))*$/.test(tag))
      errors.sourcePath = "标签格式无效；支持 Tag、Program:MainProgram.Tag、Tag.Member 和 Tag[0]。";
    if (!this.dataTypes.includes(row.sourceDataType)) errors.sourceDataType = "请选择 EtherNet/IP 支持的标量数据类型。";
    return errors;
  },
  probeReadiness(form) {
    if (!(form.ethernetIp?.host || "").trim()) return "请先填写 PLC 地址。";
    return (form.valueMappings || []).some(row => (row.sourcePath || "").trim())
      ? ""
      : "EtherNet/IP 仅回读已配置标签，请先配置至少一个 PLC 标签。";
  },
};

const omronFinsTcp = {
  id: "omron-fins-tcp",
  label: "Omron FINS/TCP",
  section: "omronFinsTcp",
  addressing: ADDRESSING.omronFinsAddress,
  probeMode: PROBE_MODE.configuredPointsOnly,
  summary: "通过 FINS/TCP 只读回读 Omron PLC 中已配置的内存区地址。",
  pointLabel: "FINS 内存区地址",
  probeViewLabel: "已配置地址回读",
  defaultDataType: "int16",
  dataTypes: ["boolean", "int16", "uint16", "int32", "uint32", "int64", "uint64", "float32", "float64"],
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
    "只读回读已声明的 CIO、WR、HR、AR 和 DM 点位，不扫描地址、不写 PLC，也不走 FINS 路由。",
    "FINS/TCP 需由 PLC 型号支持并在设备设置中启用；内存区有效范围需以现场探查结果为准。",
    "支持 Boolean、16/32/64 位整数和 32/64 位浮点；字内固定高字节在前，多字点位可配置字序。",
    "首期不支持 EM 区、计时器、计数器和文本点位。",
  ],
  defaults: () => ({ host: "", port: 9600, pollIntervalMs: 1000 }),
  fields: [
    { name: "host", label: "PLC 地址", type: "text", placeholder: "192.168.1.60" },
    { name: "port", label: "FINS/TCP 端口", type: "number", min: 1, max: 65535,
      hint: "Omron 常见默认端口为 9600；请按 PLC 的网络设置填写。" },
    { name: "pollIntervalMs", label: "轮询间隔（ms）", type: "number", min: 1,
      hint: "一次读取完成后等待多久再发起下一次；不是固定采样周期。" },
  ],
  validateConnection(connection) {
    const errors = {};
    if (!(connection.host || "").trim()) errors.host = "PLC 地址不能为空。";
    if (!(Number(connection.port) >= 1 && Number(connection.port) <= 65535)) errors.port = "端口必须在 1-65535 之间。";
    if (!(Number(connection.pollIntervalMs) >= 1 && Number(connection.pollIntervalMs) <= 3600000)) errors.pollIntervalMs = "读取后等待时间必须是 1-3600000ms。";
    return errors;
  },
  validatePoint(row) {
    const errors = {};
    const selector = String(row.sourcePath || "").trim();
    const match = /^(DM|CIO|WR|W|HR|H|AR|A):(\d+)(?:\.(\d{1,2}))?(?::([a-z0-9]+))?$/i.exec(selector);
    if (!match) errors.sourcePath = "格式应为 DM:100、DM:100:int16 或 CIO:100.3:boolean。";
    else {
      const address = Number(match[2]);
      const bit = match[3] === undefined ? null : Number(match[3]);
      const embeddedType = (match[4] || "").toLowerCase();
      if (!Number.isSafeInteger(address) || address < 0 || address > 65535) errors.sourcePath = "字地址必须在 0-65535 之间。";
      if (bit !== null && (!Number.isInteger(bit) || bit < 0 || bit > 15)) errors.sourcePath = "位偏移必须在 0-15 之间。";
      if (embeddedType && embeddedType !== row.sourceDataType) errors.sourcePath = `地址类型 ${embeddedType} 与映射类型 ${row.sourceDataType} 不一致。`;
      if ((row.sourceDataType === "boolean") !== (bit !== null)) errors.sourcePath = row.sourceDataType === "boolean" ? "Boolean 点位必须填写位偏移，例如 CIO:100.3。" : "只有 Boolean 点位可以填写位偏移。";
      const words = ["int32", "uint32", "float32"].includes(row.sourceDataType) ? 2
        : ["int64", "uint64", "float64"].includes(row.sourceDataType) ? 4 : 1;
      if (address + words > 65536) errors.sourcePath = "多字点位的读取范围不能超过 65535。";
    }
    if (!this.dataTypes.includes(row.sourceDataType)) errors.sourceDataType = "请选择 FINS/TCP 支持的标量数据类型。";
    if (!["high-low", "low-high"].includes(row.wordOrder)) errors.wordOrder = "请选择有效的多字字序。";
    return errors;
  },
  probeReadiness(form) {
    if (!(form.omronFinsTcp?.host || "").trim()) return "请先填写 PLC 地址。";
    return (form.valueMappings || []).some(row => (row.sourcePath || "").trim())
      ? ""
      : "FINS/TCP 仅回读已配置地址，请先填写至少一个 PLC 点位。";
  },
};

const omronFinsUdp = {
  id: "omron-fins-udp",
  label: "Omron FINS/UDP",
  section: "omronFinsUdp",
  addressing: ADDRESSING.omronFinsAddress,
  probeMode: PROBE_MODE.configuredPointsOnly,
  summary: "通过 FINS/UDP 单播只读回读 Omron PLC 中已配置的内存区地址。",
  pointLabel: "FINS 内存区地址",
  probeViewLabel: "已配置地址回读",
  defaultDataType: "int16",
  dataTypes: ["boolean", "int16", "uint16", "int32", "uint32", "int64", "uint64", "float32", "float64"],
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
    "只读回读已声明的 CIO、WR、HR、AR 和 DM 点位；只向已填写的 PLC 地址发送 UDP 单播，不扫描网络、不写 PLC。",
    "PLC 地址和本机、PLC 的 FINS 节点地址必须与设备设置一致；本机和 PLC 节点地址取值为 1-254。",
    "FINS/UDP 默认端口为 9600；路由网络和特殊单元首期不支持，连接仅用于直连以太网节点。",
    "支持 Boolean、16/32/64 位整数和 32/64 位浮点；字内固定高字节在前，多字点位可配置字序。",
  ],
  defaults: () => ({
    host: "",
    port: 9600,
    clientNodeAddress: "",
    serverNodeAddress: "",
    pollIntervalMs: 1000,
  }),
  fields: [
    { name: "host", label: "PLC 地址", type: "text", placeholder: "192.168.1.60" },
    { name: "port", label: "FINS/UDP 端口", type: "number", min: 1, max: 65535,
      hint: "Omron 常见默认端口为 9600；请按 PLC 的网络设置填写。" },
    { name: "clientNodeAddress", label: "本机 FINS 节点地址", type: "number", min: 1, max: 254,
      hint: "填写 Edge 所在主机在 FINS 网络中的节点地址；必须和 PLC 网络配置一致。" },
    { name: "serverNodeAddress", label: "PLC FINS 节点地址", type: "number", min: 1, max: 254,
      hint: "填写目标 PLC 在 FINS 网络中的节点地址；不会自动扫描或推断。" },
    { name: "pollIntervalMs", label: "轮询间隔（ms）", type: "number", min: 1,
      hint: "一次读取完成后等待多久再发起下一次；不是固定采样周期。" },
  ],
  validateConnection(connection) {
    const errors = {};
    if (!(connection.host || "").trim()) errors.host = "PLC 地址不能为空。";
    if (!(Number(connection.port) >= 1 && Number(connection.port) <= 65535)) errors.port = "端口必须在 1-65535 之间。";
    if (!(Number.isInteger(Number(connection.clientNodeAddress)) && Number(connection.clientNodeAddress) >= 1 && Number(connection.clientNodeAddress) <= 254))
      errors.clientNodeAddress = "本机 FINS 节点地址必须是 1-254 的整数。";
    if (!(Number.isInteger(Number(connection.serverNodeAddress)) && Number(connection.serverNodeAddress) >= 1 && Number(connection.serverNodeAddress) <= 254))
      errors.serverNodeAddress = "PLC FINS 节点地址必须是 1-254 的整数。";
    if (!errors.clientNodeAddress && !errors.serverNodeAddress && Number(connection.clientNodeAddress) === Number(connection.serverNodeAddress))
      errors.serverNodeAddress = "PLC 节点地址不能与本机 FINS 节点地址相同。";
    if (!(Number(connection.pollIntervalMs) >= 1 && Number(connection.pollIntervalMs) <= 3600000)) errors.pollIntervalMs = "读取后等待时间必须是 1-3600000ms。";
    return errors;
  },
  validatePoint(row) {
    const errors = {};
    const selector = String(row.sourcePath || "").trim();
    const match = /^(DM|CIO|WR|W|HR|H|AR|A):(\d+)(?:\.(\d{1,2}))?(?::([a-z0-9]+))?$/i.exec(selector);
    if (!match) errors.sourcePath = "格式应为 DM:100、DM:100:int16 或 CIO:100.3:boolean。";
    else {
      const address = Number(match[2]);
      const bit = match[3] === undefined ? null : Number(match[3]);
      const embeddedType = (match[4] || "").toLowerCase();
      if (!Number.isSafeInteger(address) || address < 0 || address > 65535) errors.sourcePath = "字地址必须在 0-65535 之间。";
      if (bit !== null && (!Number.isInteger(bit) || bit < 0 || bit > 15)) errors.sourcePath = "位偏移必须在 0-15 之间。";
      if (embeddedType && embeddedType !== row.sourceDataType) errors.sourcePath = `地址类型 ${embeddedType} 与映射类型 ${row.sourceDataType} 不一致。`;
      if ((row.sourceDataType === "boolean") !== (bit !== null)) errors.sourcePath = row.sourceDataType === "boolean" ? "Boolean 点位必须填写位偏移，例如 CIO:100.3。" : "只有 Boolean 点位可以填写位偏移。";
      const words = ["int32", "uint32", "float32"].includes(row.sourceDataType) ? 2
        : ["int64", "uint64", "float64"].includes(row.sourceDataType) ? 4 : 1;
      if (address + words > 65536) errors.sourcePath = "多字点位的读取范围不能超过 65535。";
    }
    if (!this.dataTypes.includes(row.sourceDataType)) errors.sourceDataType = "请选择 FINS/UDP 支持的标量数据类型。";
    if (!["high-low", "low-high"].includes(row.wordOrder)) errors.wordOrder = "请选择有效的多字字序。";
    return errors;
  },
  probeReadiness(form) {
    if (!(form.omronFinsUdp?.host || "").trim()) return "请先填写 PLC 地址。";
    if (!form.omronFinsUdp?.clientNodeAddress || !form.omronFinsUdp?.serverNodeAddress) return "请先填写本机和 PLC 的 FINS 节点地址。";
    return (form.valueMappings || []).some(row => (row.sourcePath || "").trim())
      ? ""
      : "FINS/UDP 仅回读已配置地址，请先填写至少一个 PLC 点位。";
  },
};

const BACNET_PRESENT_VALUE_OBJECT_TYPES = new Set([
  "analog-input", "analog-output", "analog-value",
  "binary-input", "binary-output", "binary-value",
  "multi-state-input", "multi-state-output", "multi-state-value",
]);

const bacnetIp = {
  id: "bacnet-ip",
  label: "BACnet/IP",
  section: "bacnetIp",
  addressing: ADDRESSING.bacnetObject,
  probeMode: PROBE_MODE.discover,
  summary: "向指定 BACnet/IP 设备单播浏览对象目录，并只读标量 present-value；不做广播发现或写入。",
  pointLabel: "BACnet 对象",
  probeViewLabel: "指定设备对象目录",
  probeActionLabel: "浏览对象目录",
  defaultDataType: "auto",
  dataTypes: ["auto"],
  capabilities: {
    sourceTimestamp: false,
    sequencePath: false,
    parameterObjectPath: false,
    connectTimeout: true,
    reconnectDelay: true,
    perTopicMapping: false,
    registerByteOrder: false,
    bitAddressing: false,
  },
  constraints: [
    "面向 HVAC、照明、门禁等 BACnet/IP 设备，只读取对象目录中的标量 present-value。",
    "使用填写的 IP 主机、UDP 端口和 Device Instance 定向单播；不发送 Who-Is 广播或执行网络发现。",
    "仅读取，不写属性；当前不支持 MS/TP、BACnet/SC、数组值及非标量 present-value。",
  ],
  defaults: () => ({ host: "", port: 47808, deviceInstance: "", pollIntervalMs: 1000 }),
  fields: [
    { name: "host", label: "BACnet/IP 设备地址", type: "text", placeholder: "192.168.1.80",
      hint: "填写目标设备的单播 IPv4 地址；Edge 仍会检查私有网络或显式主机允许清单。" },
    { name: "port", label: "BACnet/IP UDP 端口", type: "number", min: 1, max: 65535,
      hint: "默认端口为 47808；设备配置了其他 UDP 端口时填写实际值。" },
    { name: "deviceInstance", label: "Device Instance", type: "number", min: 0, max: 4194302,
      hint: "填写目标 BACnet Device 对象实例编号（0-4194302），用于单播访问指定设备。" },
    { name: "pollIntervalMs", label: "轮询间隔（ms）", type: "number", min: 100,
      hint: "一次读取完成后等待多久再发起下一次；不是固定采样周期。" },
  ],
  validateConnection(connection) {
    const errors = {};
    if (!isUnicastIpv4(connection.host)) errors.host = "必须填写有效的单播 IPv4 地址，不能使用主机名、广播、组播或本机地址。";
    if (!(Number.isInteger(Number(connection.port)) && Number(connection.port) >= 1 && Number(connection.port) <= 65535))
      errors.port = "UDP 端口必须是 1-65535 之间的整数。";
    const deviceInstanceText = String(connection.deviceInstance ?? "").trim();
    const deviceInstance = Number(deviceInstanceText);
    if (!deviceInstanceText || !(Number.isInteger(deviceInstance) && deviceInstance >= 0 && deviceInstance <= 4194302))
      errors.deviceInstance = "Device Instance 必须是 0-4194302 之间的整数。";
    if (!(Number.isInteger(Number(connection.pollIntervalMs)) && Number(connection.pollIntervalMs) >= 100 && Number(connection.pollIntervalMs) <= 3600000))
      errors.pollIntervalMs = "轮询间隔必须是 100-3600000ms 之间的整数。";
    return errors;
  },
  validatePoint(row) {
    const selector = String(row.sourcePath || "").trim();
    const match = /^([a-z][a-z0-9-]*):(\d+)$/.exec(selector);
    if (!match) return { sourcePath: "对象选择器格式应为 object-type:instance，例如 analog-input:1。" };
    if (!BACNET_PRESENT_VALUE_OBJECT_TYPES.has(match[1]))
      return { sourcePath: "当前仅支持 analog、binary 和 multi-state input/output/value 对象。" };
    const instance = Number(match[2]);
    if (!Number.isSafeInteger(instance) || instance < 0 || instance > 4194302)
      return { sourcePath: "对象实例编号必须是 0-4194302 之间的整数。" };
    if (row.sourceDataType !== "auto") return { sourceDataType: "BACnet/IP 映射使用 auto 读取对象的 present-value 标量。" };
    return {};
  },
  probeReadiness(form) {
    const connection = form.bacnetIp || {};
    if (!(connection.host || "").trim()) return "请先填写 BACnet/IP 设备地址。";
    if (String(connection.deviceInstance ?? "").trim() === "")
      return "请填写 Device Instance，之后会单播浏览该设备的对象目录。";
    return Object.values(this.validateConnection(connection))[0] || "";
  },
};

const bacnetMstp = {
  id: "bacnet-mstp",
  label: "BACnet MS/TP",
  section: "bacnetMstp",
  addressing: ADDRESSING.bacnetObject,
  probeMode: PROBE_MODE.discover,
  summary: "通过配置的 RS-485 串口访问 BACnet MS/TP 总线，只查找指定 Device Instance 并只读标量 present-value。",
  pointLabel: "BACnet 对象",
  probeViewLabel: "指定设备对象目录",
  probeActionLabel: "浏览对象目录",
  defaultDataType: "auto",
  dataTypes: ["auto"],
  capabilities: {
    sourceTimestamp: false,
    sequencePath: false,
    parameterObjectPath: false,
    connectTimeout: true,
    reconnectDelay: true,
    perTopicMapping: false,
    registerByteOrder: false,
    bitAddressing: false,
  },
  constraints: [
    "面向 HVAC、照明、门禁等 BACnet MS/TP 设备；总线采用 8N1，波特率按现场设备配置。",
    "填写 Edge 已允许的物理串口、本机唯一 MS/TP 站地址和目标 Device Instance；只对该实例发送限定范围的 Who-Is。",
    "只读 Object_List 与已映射对象的标量 present-value；串口不可与 Modbus RTU/ASCII 或其他驱动同时占用，不写属性。",
  ],
  defaults: () => ({
    portName: "", baudRate: 38400, localStationAddress: "", maxMaster: 127,
    maxInfoFrames: 1, deviceInstance: "", pollIntervalMs: 1000,
  }),
  fields: [
    { group: "物理总线", name: "portName", label: "Edge 串口", type: "text", placeholder: "COM4 或 /dev/ttyUSB0",
      hint: "只能选择 Edge 运维允许清单中的 COMn 或 /dev/ 下设备路径。" },
    { group: "物理总线", name: "baudRate", label: "波特率", type: "select",
      options: [[9600, "9600"], [19200, "19200"], [38400, "38400"], [76800, "76800"], [115200, "115200"]] },
    { group: "物理总线", name: "localStationAddress", label: "本机 MS/TP 站地址", type: "number", min: 0, max: 127,
      hint: "填写总线上未被占用的主站地址（0-127）；同一 Edge 总线的任务应使用相同地址。" },
    { group: "高级总线参数", name: "maxMaster", label: "Max Master", type: "number", min: 0, max: 127,
      hint: "必须大于或等于本机站地址；按现场主站节点范围配置。" },
    { group: "高级总线参数", name: "maxInfoFrames", label: "Max Info Frames", type: "number", min: 1, max: 255,
      hint: "MS/TP 主站每次持有令牌时允许发送的信息帧上限。" },
    { group: "目标设备", name: "deviceInstance", label: "Device Instance", type: "number", min: 0, max: 4194302,
      hint: "填写要探查的 BACnet Device 实例；不会扫描其他实例。" },
    { group: "目标设备", name: "pollIntervalMs", label: "轮询间隔（ms）", type: "number", min: 100, max: 3600000,
      hint: "一次读取完成后等待多久再发起下一次读取。" },
  ],
  validateConnection(connection) {
    const errors = {};
    const portName = String(connection.portName || "").trim();
    const windowsPort = /^(?:COM[1-9][0-9]{0,3}|\\\\\.\\COM[1-9][0-9]{0,3})$/i.test(portName);
    const unixSegments = portName.split("/");
    const unixPort = unixSegments.length >= 3 && unixSegments[0] === "" && unixSegments[1] === "dev" &&
      unixSegments.slice(2).every(segment => segment.length > 0 && segment !== "." && segment !== "..");
    const hasControlCharacter = portName.split("").some(character => {
      const code = character.charCodeAt(0);
      return code <= 0x1f || code === 0x7f;
    });
    if (!portName || hasControlCharacter || (!windowsPort && !unixPort))
      errors.portName = "请填写 COMn、\\\\.\\COMn 或 /dev 下的设备串口路径。";
    const baudRate = Number(connection.baudRate);
    if (!Number.isInteger(baudRate) || ![9600, 19200, 38400, 76800, 115200].includes(baudRate))
      errors.baudRate = "波特率必须是 9600、19200、38400、76800 或 115200。";
    const localText = String(connection.localStationAddress ?? "").trim();
    const localAddress = Number(localText);
    if (!localText || !Number.isInteger(localAddress) || localAddress < 0 || localAddress > 127)
      errors.localStationAddress = "本机 MS/TP 站地址必须是 0-127 之间且在总线上唯一的整数。";
    const maxMaster = Number(connection.maxMaster);
    if (!Number.isInteger(maxMaster) || maxMaster < 0 || maxMaster > 127 || (localText && maxMaster < localAddress))
      errors.maxMaster = "Max Master 必须是 0-127 之间且大于或等于本机站地址。";
    const maxInfoFrames = Number(connection.maxInfoFrames);
    if (!Number.isInteger(maxInfoFrames) || maxInfoFrames < 1 || maxInfoFrames > 255)
      errors.maxInfoFrames = "Max Info Frames 必须是 1-255 之间的整数。";
    const deviceText = String(connection.deviceInstance ?? "").trim();
    const deviceInstance = Number(deviceText);
    if (!deviceText || !Number.isInteger(deviceInstance) || deviceInstance < 0 || deviceInstance > 4194302)
      errors.deviceInstance = "Device Instance 必须是 0-4194302 之间的整数。";
    const pollIntervalMs = Number(connection.pollIntervalMs);
    if (!Number.isInteger(pollIntervalMs) || pollIntervalMs < 100 || pollIntervalMs > 3600000)
      errors.pollIntervalMs = "轮询间隔必须是 100-3600000ms 之间的整数。";
    return errors;
  },
  validatePoint(row) {
    const selector = String(row.sourcePath || "").trim();
    const match = /^([a-z][a-z0-9-]*):(\d+)$/.exec(selector);
    if (!match) return { sourcePath: "对象选择器格式应为 object-type:instance，例如 analog-input:1。" };
    if (!BACNET_PRESENT_VALUE_OBJECT_TYPES.has(match[1]))
      return { sourcePath: "当前仅支持 analog、binary 和 multi-state input/output/value 对象。" };
    const instance = Number(match[2]);
    if (!Number.isSafeInteger(instance) || instance < 0 || instance > 4194302)
      return { sourcePath: "对象实例编号必须是 0-4194302 之间的整数。" };
    if (row.sourceDataType !== "auto") return { sourceDataType: "BACnet 映射使用 auto 读取对象的 present-value 标量。" };
    return {};
  },
  probeReadiness(form) {
    const connection = form.bacnetMstp || {};
    if (!(connection.portName || "").trim()) return "请先填写 Edge 串口。";
    if (String(connection.localStationAddress ?? "").trim() === "") return "请先填写本机 MS/TP 站地址。";
    if (String(connection.deviceInstance ?? "").trim() === "") return "请填写 Device Instance，之后只查找该实例。";
    return Object.values(this.validateConnection(connection))[0] || "";
  },
};

const snmp = {
  id: "snmp",
  label: "SNMPv2c/v3",
  section: "snmp",
  addressing: ADDRESSING.snmpOid,
  probeMode: PROBE_MODE.discover,
  summary: "按用户指定的数字 OID 子树进行有限只读浏览和 GET 采集；不扫描网段、不写入或订阅 Trap。",
  pointLabel: "数字 OID",
  probeViewLabel: "指定 OID 子树点位",
  probeActionLabel: "浏览 OID 子树",
  defaultDataType: "auto",
  dataTypes: ["auto", "int32", "uint32", "uint64", "boolean", "string"],
  capabilities: {
    sourceTimestamp: true,
    sequencePath: true,
    parameterObjectPath: false,
    connectTimeout: true,
    reconnectDelay: true,
    perTopicMapping: false,
    registerByteOrder: false,
    bitAddressing: false,
  },
  constraints: [
    "仅支持 SNMPv2c/v3；v3 默认 authPriv、SHA-256 与 AES-128，凭据使用 env: 密钥引用。",
    "只浏览显式配置的数字 OID 子树，最多 512 个对象；运行仅读取已映射 OID。",
    "不支持 MIB 符号名称、SET、Trap 或网段扫描；Counter 按累计值读取，不计算速率。",
  ],
  defaults: () => ({
    host: "", port: 161, version: "v3", browseRootOid: "1.3.6.1.2.1.1", contextName: "",
    communitySecretRef: "", userName: "", securityLevel: "authPriv",
    authenticationProtocol: "sha256", authenticationSecretRef: "",
    privacyProtocol: "aes128", privacySecretRef: "", pollIntervalMs: 5000,
  }),
  fields: [
    { group: "目标设备", name: "host", label: "SNMP 设备主机", type: "text", placeholder: "192.168.1.80",
      hint: "填写规范单播 IPv4 地址，例如 192.168.1.80；不能使用主机名、IPv6、回环、链路本地、组播或广播地址。目标仍须由 Edge 网络白名单允许。" },
    { group: "目标设备", name: "port", label: "UDP 端口", type: "number", min: 1, max: 65535,
      hint: "默认端口为 161。" },
    { group: "目标设备", name: "version", label: "SNMP 版本", type: "select", options: [["v2c", "SNMPv2c"], ["v3", "SNMPv3"]] },
    { group: "目标设备", name: "browseRootOid", label: "浏览根 OID", type: "text", placeholder: "1.3.6.1.2.1.1",
      hint: "只在此数字 OID 子树内有限浏览；可输入前导点，保存时会规范化。" },
    { group: "目标设备", name: "pollIntervalMs", label: "轮询间隔（ms）", type: "number", min: 100, max: 3600000,
      hint: "一次读取完成后等待多久再发起下一次读取。" },
    { group: "SNMPv2c 凭据", name: "communitySecretRef", label: "Community 密钥引用", type: "text", placeholder: "env:SNMP_COMMUNITY",
      hint: "仅填写 env:... 引用，不填写 community 明文。SNMPv2c 不加密报文。", when: connection => connection.version === "v2c" },
    { group: "SNMPv3 安全", name: "contextName", label: "Context Name", type: "text", placeholder: "",
      hint: "可选上下文名称；留空使用默认上下文。", when: connection => connection.version === "v3" },
    { group: "SNMPv3 安全", name: "userName", label: "用户名", type: "text", placeholder: "snmp-reader",
      when: connection => connection.version === "v3" },
    { group: "SNMPv3 安全", name: "securityLevel", label: "安全级别", type: "select",
      options: [["noAuthNoPriv", "无认证 / 无加密"], ["authNoPriv", "认证 / 无加密"], ["authPriv", "认证 / 加密"]],
      when: connection => connection.version === "v3" },
    { group: "SNMPv3 认证", name: "authenticationProtocol", label: "认证算法", type: "select",
      options: [["sha256", "SHA-256"], ["sha384", "SHA-384"], ["sha512", "SHA-512"]],
      when: connection => connection.version === "v3" && ["authNoPriv", "authPriv"].includes(connection.securityLevel) },
    { group: "SNMPv3 认证", name: "authenticationSecretRef", label: "认证密钥引用", type: "text", placeholder: "env:SNMP_AUTH_SECRET",
      hint: "仅填写 env:... 引用，不填写认证密钥明文。", when: connection => connection.version === "v3" && ["authNoPriv", "authPriv"].includes(connection.securityLevel) },
    { group: "SNMPv3 隐私", name: "privacyProtocol", label: "加密算法", type: "select",
      options: [["aes128", "AES-128"], ["aes192", "AES-192"], ["aes256", "AES-256"]],
      when: connection => connection.version === "v3" && connection.securityLevel === "authPriv" },
    { group: "SNMPv3 隐私", name: "privacySecretRef", label: "隐私密钥引用", type: "text", placeholder: "env:SNMP_PRIVACY_SECRET",
      hint: "仅填写 env:... 引用，不填写隐私密钥明文。", when: connection => connection.version === "v3" && connection.securityLevel === "authPriv" },
  ],
  validateConnection(connection) {
    const errors = {};
    if (!isUnicastIpv4(connection.host))
      errors.host = "必须填写规范的单播 IPv4 地址（四段十进制，每段 0-255），不能使用回环、未指定、链路本地、组播或广播地址。";
    if (!(Number.isInteger(Number(connection.port)) && Number(connection.port) >= 1 && Number(connection.port) <= 65535))
      errors.port = "UDP 端口必须是 1-65535 之间的整数。";
    if (!["v2c", "v3"].includes(connection.version)) errors.version = "请选择 SNMPv2c 或 SNMPv3。";
    if (!normalizeSnmpOid(connection.browseRootOid)) errors.browseRootOid = snmpOidError(connection.browseRootOid);
    if (!(Number.isInteger(Number(connection.pollIntervalMs)) && Number(connection.pollIntervalMs) >= 100 && Number(connection.pollIntervalMs) <= 3600000))
      errors.pollIntervalMs = "轮询间隔必须是 100-3600000ms 之间的整数。";
    if (connection.version === "v3" &&
        (String(connection.contextName ?? "").length > 128 || hasControlCharacters(connection.contextName)))
      errors.contextName = "Context Name 最多 128 个字符且不能包含控制字符。";

    if (connection.version === "v2c") {
      if (!isEnvironmentSecretReference(connection.communitySecretRef))
        errors.communitySecretRef = "Community 必须填写 env:NAME 格式的密钥引用，不得填写明文。";
    } else if (connection.version === "v3") {
      if (!String(connection.userName ?? "").trim() || String(connection.userName).length > 32 || hasControlCharacters(connection.userName))
        errors.userName = "SNMPv3 用户名不能为空且最多 32 个字符。";
      if (!["noAuthNoPriv", "authNoPriv", "authPriv"].includes(connection.securityLevel))
        errors.securityLevel = "请选择有效的 SNMPv3 安全级别。";
      if (["authNoPriv", "authPriv"].includes(connection.securityLevel)) {
        if (!["sha256", "sha384", "sha512"].includes(connection.authenticationProtocol))
          errors.authenticationProtocol = "认证算法仅支持 SHA-256、SHA-384 或 SHA-512。";
        if (!isEnvironmentSecretReference(connection.authenticationSecretRef))
          errors.authenticationSecretRef = "认证密钥必须填写 env:NAME 格式的密钥引用。";
      }
      if (connection.securityLevel === "authPriv") {
        if (!["aes128", "aes192", "aes256"].includes(connection.privacyProtocol))
          errors.privacyProtocol = "加密算法仅支持 AES-128、AES-192 或 AES-256。";
        if (!isEnvironmentSecretReference(connection.privacySecretRef))
          errors.privacySecretRef = "隐私密钥必须填写 env:NAME 格式的密钥引用。";
      }
    }
    return errors;
  },
  validatePoint(row) {
    const sourcePath = normalizeSnmpOid(row.sourcePath);
    if (!sourcePath) return { sourcePath: snmpOidError(row.sourcePath) };
    if (!this.dataTypes.includes(row.sourceDataType)) return { sourceDataType: "SNMP 映射仅支持 auto、int32、uint32、uint64、boolean 或 string。" };
    return {};
  },
  probeReadiness(form) {
    const connection = form.snmp || {};
    return Object.values(this.validateConnection(connection))[0] || "";
  },
};

function isUnicastIpv4(value) {
  const parts = String(value ?? "").trim().split(".");
  if (parts.length !== 4 || parts.some(part => !/^(0|[1-9]\d{0,2})$/.test(part) || Number(part) > 255)) return false;
  const [first, second, third, fourth] = parts.map(Number);
  return !(first === 0 || first === 127 || first >= 224 || (first === 169 && second === 254) ||
    (first === 255 && second === 255 && third === 255 && fourth === 255));
}

const DESCRIPTORS = [httpPolling, mtConnect, mqtt, opcUa, modbusTcp, modbusRtu, modbusAscii, melsecA1e, melsecMc3E, siemensS7, ethernetIp, omronFinsTcp, omronFinsUdp, bacnetIp, bacnetMstp, snmp];

export const protocolOptions = DESCRIPTORS.map(item => [item.id, item.label, item.summary]);

const PROTOCOL_OPTION_GROUPS = [
  {
    label: "网络 / IP 接入",
    protocols: [
      "http-polling", "mtconnect", "mqtt", "opc-ua", "modbus-tcp",
      "melsec-a1e", "melsec-mc-3e", "siemens-s7", "ethernet-ip",
      "omron-fins-tcp", "omron-fins-udp", "bacnet-ip", "snmp",
    ],
  },
  {
    label: "串口 / RS-485 接入",
    protocols: ["modbus-rtu", "modbus-ascii", "bacnet-mstp"],
  },
];

export function protocolOptionGroups(protocolIds) {
  const allowedProtocols = protocolIds ? new Set(protocolIds) : null;
  const optionsById = new Map(protocolOptions.map(option => [option[0], option]));
  return PROTOCOL_OPTION_GROUPS.map(group => ({
    label: group.label,
    options: group.protocols
      .filter(id => !allowedProtocols || allowedProtocols.has(id))
      .map(id => optionsById.get(id))
      .filter(Boolean),
  })).filter(group => group.options.length > 0);
}

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
