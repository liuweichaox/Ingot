import type {
  AgentRunSnapshot,
  ConnectorRequirement,
  EvidenceRef
} from "../types";

export const TERMINAL_RUN_STATUSES = new Set(["completed", "failed", "cancelled"]);

export const WORKSPACE_STATUS_LABELS: Record<string, string> = {
  ready: "源码已生成",
  building: "构建中",
  "build-failed": "构建失败",
  built: "构建通过",
  testing: "测试中",
  "test-failed": "测试失败",
  "awaiting-package-approval": "等待批准打包",
  "package-approved": "已批准打包",
  packaged: "制品已生成"
};

export function normalizeCentralUrl(value: string): string {
  return value.trim().replace(/\/+$/, "");
}

export function buildConnectorQuestion(requirement: ConnectorRequirement): string {
  const networkTargets = requirement.allowedNetworkTargets.trim() || "无（生成与测试阶段保持禁网）";
  const authentication = requirement.authentication.trim() || "none";
  const notes = requirement.notes.trim() || "无";

  return [
    "为以下生产数据源生成可构建、可测试的 Ingot 采集连接器代码。",
    "必须先形成完整连接器规格，再创建受控工作区、生成源码、执行固定构建和测试。",
    "测试通过后停止在等待操作者批准打包状态，不连接设备、不部署、不修改生产数据。",
    "",
    `连接器名称：${requirement.name.trim()}`,
    `来源编码：${requirement.sourceCode.trim()}`,
    `协议或 SDK：${requirement.protocol.trim()}`,
    `数据端点：${requirement.endpoint.trim()}`,
    `认证方式：${authentication}`,
    `输入数据契约：${requirement.dataContract.trim()}`,
    `采样策略：${requirement.samplingPolicy.trim()}`,
    `成功标准：${requirement.successCriteria.trim()}`,
    `允许的运行期网络目标：${networkTargets}`,
    `补充约束：${notes}`
  ].join("\n");
}

export function connectorRequirementIsComplete(requirement: ConnectorRequirement): boolean {
  return [
    requirement.name,
    requirement.sourceCode,
    requirement.protocol,
    requirement.endpoint,
    requirement.dataContract,
    requirement.samplingPolicy,
    requirement.successCriteria
  ].every((value) => value.trim().length > 0);
}

export function workspaceIdsFromRun(run?: AgentRunSnapshot): string[] {
  if (!run) return [];
  const evidence: EvidenceRef[] = [
    ...(run.answer?.evidence ?? []),
    ...run.toolInvocations.flatMap((invocation) => invocation.evidence ?? [])
  ];

  return [...new Set(evidence.filter((item) => item.kind === "connector-workspace").map((item) => item.id))];
}

export function formatBytes(value: number): string {
  if (!Number.isFinite(value) || value < 0) return "—";
  if (value < 1024) return `${value} B`;
  const units = ["KB", "MB", "GB"];
  let size = value / 1024;
  let unit = units[0];
  for (let index = 1; size >= 1024 && index < units.length; index += 1) {
    size /= 1024;
    unit = units[index];
  }
  return `${size.toFixed(size >= 10 ? 1 : 2)} ${unit}`;
}

export function packageFileName(relativePath: string, packageName: string, sha256: string): string {
  const candidate = relativePath.split("/").at(-1)?.trim();
  return candidate?.endsWith(".zip") ? candidate : `${packageName}-${sha256}.zip`;
}

