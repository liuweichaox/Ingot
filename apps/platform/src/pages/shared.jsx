
import { ArrowPathIcon } from "@heroicons/react/24/outline";
import { Card } from "../ui/components";

export const formatTime = value => value ? new Date(value).toLocaleString("zh-CN") : "—";
export const formatInteger = value => Number.isFinite(Number(value)) ? Number(value).toLocaleString("zh-CN") : "—";
export const formatMeasurementValue = value => {
  if (value == null || value === "") return "—";
  const numeric = Number(value);
  return Number.isFinite(numeric)
    ? numeric.toLocaleString("zh-CN", { maximumFractionDigits: 6 })
    : String(value);
};
export const formatBytes = value => {
  const bytes = Number(value);
  if (!Number.isFinite(bytes)) return "—";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 ** 2) return `${(bytes / 1024).toFixed(1)} KiB`;
  if (bytes < 1024 ** 3) return `${(bytes / 1024 ** 2).toFixed(1)} MiB`;
  return `${(bytes / 1024 ** 3).toFixed(1)} GiB`;
};
const metricSamples = (payload, name) => payload?.metrics?.[name]?.data || [];
export const metricTotal = (payload, name) => metricSamples(payload, name).reduce((sum, sample) => sum + Number(sample.value || 0), 0);
export const formatDuration = value => {
  const milliseconds = Number(value);
  if (!Number.isFinite(milliseconds)) return "—";
  if (milliseconds < 1000) return `${Math.round(milliseconds)} 毫秒`;
  const totalSeconds = Math.round(milliseconds / 1000);
  if (totalSeconds < 60) return `${totalSeconds} 秒`;
  const totalMinutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  if (totalMinutes < 60) return seconds ? `${totalMinutes} 分 ${seconds} 秒` : `${totalMinutes} 分钟`;
  const totalHours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  if (totalHours < 24) return minutes ? `${totalHours} 小时 ${minutes} 分` : `${totalHours} 小时`;
  const days = Math.floor(totalHours / 24);
  const hours = totalHours % 24;
  return hours ? `${days} 天 ${hours} 小时` : `${days} 天`;
};
export const edgeStatus = edge => {
  if (!edge?.lastSeen) return "unknown";
  if (edge.lastError || ["degraded", "failed"].includes(edge.acquisition?.state) || edge.delivery?.state === "degraded") return "degraded";
  return Date.now() - new Date(edge.lastSeen).getTime() <= 30000 ? "online" : "offline";
};

const inspectionInputTypeLabels = {
  numeric: "数值",
  text: "文本",
  select: "选项",
  boolean: "是/否",
};

export const acquisitionProtocolLabels = {
  "http-polling": "HTTP 接口",
  mqtt: "MQTT",
  "opc-ua": "OPC UA",
  "modbus-tcp": "Modbus TCP",
  "melsec-a1e": "三菱 MC 1E",
};

const objectTypeLabels = {
  equipment: "生产设备",
  workpiece: "工件",
};

export const objectTypeLabel = value => objectTypeLabels[value] || value || "未分类";

const eventTypeLabels = {
  "process.started": "生产开始",
  "process.completed": "生产完成",
  "process.sample": "过程采样",
  "process.execution.started": "运行开始",
  "process.execution.completed": "运行完成",
  "processSpecification.step_changed": "工艺步骤切换",
  "process.stage_changed": "工艺阶段切换",
  "quality.inspection.completed": "质检完成",
  "alarm.raised": "设备报警",
  "alarm.cleared": "报警解除",
};

export const eventTypeLabel = value => eventTypeLabels[value] || value?.split(".").join(" / ") || "生产事件";

const contextFieldLabels = {
  context_capture_status: "上下文捕获状态",
  equipment_id: "设备编号",
  execution_id: "运行编号",
  product_family_code: "产品系列",
  product_code: "产品编码",
  process_specification_id: "工艺规范",
  process_specification_version: "工艺规范版本",
  actual_process_specification_id: "实际工艺规范",
  actual_process_specification_version: "实际工艺规范版本",
  output_item_id: "产出物",
  production_context_id: "生产上下文",
  material_lot_ref: "材料批次",
  material_specification: "材料规格",
  external_order_ref: "外部工单",
  external_batch_ref: "外部批次",
  tooling_installation_id: "工装装机记录",
  tooling_assembly_id: "工装总成",
  assembly_revision: "工装版本",
  assembly_revision_id: "装配版本",
  tooling_usage_count: "工装使用次数",
  maintenance_status: "维护状态",
  calibration_status: "校准状态",
  calibration_ref: "校准记录",
  calibration_valid_until: "校准有效期",
};

export const contextFieldLabel = value => contextFieldLabels[value] || value || "未命名字段";

export function emptyInspectionCharacteristic() {
  return {
    code: "",
    name: "",
    inputType: "numeric",
    unit: "",
    lowerLimit: "",
    upperLimit: "",
    allowedValuesText: "",
    passingValuesText: "",
    required: true,
  };
}

export function inspectionDefinitionForm(value = {}, version) {
  const characteristics = Array.isArray(value.characteristics) && value.characteristics.length > 0
    ? value.characteristics.map(characteristic => ({
      code: characteristic.code || "",
      name: characteristic.name || "",
      inputType: characteristic.inputType || "numeric",
      unit: characteristic.unit || "",
      lowerLimit: characteristic.lowerLimit ?? "",
      upperLimit: characteristic.upperLimit ?? "",
      allowedValuesText: (characteristic.allowedValues || []).join("\n"),
      passingValuesText: (characteristic.passingValues || []).join("\n"),
      required: characteristic.required !== false,
    }))
    : [emptyInspectionCharacteristic()];

  return {
    code: value.code || "",
    version: version ?? value.version ?? 1,
    name: value.name || "",
    description: value.description || "",
    characteristics,
  };
}

export function inspectionDefinitionPayload(form) {
  return {
    code: form.code.trim(),
    version: Number(form.version),
    name: form.name.trim(),
    description: form.description.trim() || null,
    characteristics: form.characteristics.map(characteristic => ({
      code: characteristic.code.trim(),
      name: characteristic.name.trim(),
      inputType: characteristic.inputType,
      unit: characteristic.inputType === "numeric" ? characteristic.unit.trim() || null : null,
      lowerLimit: characteristic.inputType === "numeric" && characteristic.lowerLimit !== ""
        ? Number(characteristic.lowerLimit)
        : null,
      upperLimit: characteristic.inputType === "numeric" && characteristic.upperLimit !== ""
        ? Number(characteristic.upperLimit)
        : null,
      allowedValues: characteristic.inputType === "select"
        ? characteristic.allowedValuesText.split(/\r?\n|,/).map(value => value.trim()).filter(Boolean)
        : [],
      passingValues: characteristic.inputType !== "numeric"
        ? characteristic.passingValuesText.split(/\r?\n|,/).map(value => value.trim()).filter(Boolean)
        : [],
      required: characteristic.required,
    })),
  };
}

export function inspectionDefinitionValidation(form) {
  const codePattern = /^[a-z][a-z0-9_-]*(?:\.[a-z0-9][a-z0-9_-]*)*$/;
  if (!codePattern.test(form.code.trim())) return "定义代码需使用小写点分格式，例如 hardness.final。";
  if (!Number.isInteger(Number(form.version)) || Number(form.version) < 1) return "版本必须是大于 0 的整数。";
  if (!form.name.trim()) return "请填写定义名称。";
  if (form.characteristics.length === 0) return "请至少添加一个检测特性。";

  const codes = new Set();
  for (const [index, characteristic] of form.characteristics.entries()) {
    const position = `第 ${index + 1} 个检测特性`;
    const code = characteristic.code.trim();
    if (!codePattern.test(code)) return `${position}的代码需使用小写点分格式。`;
    if (codes.has(code)) return `检测特性代码“${code}”重复。`;
    codes.add(code);
    if (!characteristic.name.trim()) return `${position}缺少名称。`;
    if (characteristic.inputType === "select" &&
        !characteristic.allowedValuesText.split(/\r?\n|,/).some(value => value.trim())) {
      return `${position}是选项类型，请至少填写一个可选值。`;
    }
    if (["select", "boolean"].includes(characteristic.inputType) &&
        !characteristic.passingValuesText.split(/\r?\n|,/).some(value => value.trim())) {
      return `${position}必须明确填写合格值，不能由提交端自行判定。`;
    }
    if (characteristic.inputType === "numeric") {
      const lower = characteristic.lowerLimit === "" ? null : Number(characteristic.lowerLimit);
      const upper = characteristic.upperLimit === "" ? null : Number(characteristic.upperLimit);
      if ((lower !== null && !Number.isFinite(lower)) || (upper !== null && !Number.isFinite(upper))) {
        return `${position}的上下限必须是有效数字。`;
      }
      if (lower !== null && upper !== null && lower > upper) return `${position}的下限不能大于上限。`;
    }
  }
  return "";
}

export function inspectionInputTypes(characteristics) {
  if (!Array.isArray(characteristics) || characteristics.length === 0) return "—";
  return [...new Set(characteristics.map(item => inspectionInputTypeLabels[item.inputType] || item.inputType))].join("、");
}

export function LoadingCard() {
  return (
    <Card>
      <div className="grid min-h-44 place-items-center text-sm text-slate-500">
        <span className="inline-flex items-center gap-2"><ArrowPathIcon className="size-5 animate-spin" />正在读取数据</span>
      </div>
    </Card>
  );
}

export function uuidv7() {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  const timestamp = BigInt(Date.now());
  bytes[0] = Number((timestamp >> 40n) & 0xffn);
  bytes[1] = Number((timestamp >> 32n) & 0xffn);
  bytes[2] = Number((timestamp >> 24n) & 0xffn);
  bytes[3] = Number((timestamp >> 16n) & 0xffn);
  bytes[4] = Number((timestamp >> 8n) & 0xffn);
  bytes[5] = Number(timestamp & 0xffn);
  bytes[6] = (bytes[6] & 0x0f) | 0x70;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = [...bytes].map(value => value.toString(16).padStart(2, "0")).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
