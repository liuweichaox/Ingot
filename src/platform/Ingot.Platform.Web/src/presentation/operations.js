export const EDGE_ONLINE_WINDOW_MS = 90_000;

const runtimeProblemStates = new Set(["degraded", "error", "failed", "faulted", "unreachable"]);

export function isEdgeOnline(edge, now = Date.now()) {
  if (!edge?.lastSeen) return false;
  const lastSeenAt = new Date(edge.lastSeen).getTime();
  return Number.isFinite(lastSeenAt) && now - lastSeenAt < EDGE_ONLINE_WINDOW_MS;
}

export function edgeHealth(edge, runtime, now = Date.now()) {
  if (!isEdgeOnline(edge, now) || runtime?.reachable === false) return "offline";
  const taskHasProblem = (runtime?.tasks || []).some(task => runtimeProblemStates.has(String(task.state).toLowerCase()));
  if (edge?.lastError || runtimeProblemStates.has(String(runtime?.state || "").toLowerCase()) || taskHasProblem)
    return "degraded";
  return "online";
}

export function summarizeRuntime(runtime) {
  const tasks = Array.isArray(runtime?.tasks) ? runtime.tasks : [];
  return {
    totalTasks: tasks.length,
    runningTasks: tasks.filter(task => String(task.state).toLowerCase() === "running").length,
    samplesCollected: Number(runtime?.samplesCollected) || tasks.reduce(
      (total, task) => total + (Number(task.samplesCollected) || 0),
      0,
    ),
  };
}

export function latestMetricValue(metrics, names) {
  for (const name of names) {
    const points = metrics?.[name]?.data;
    if (!Array.isArray(points)) continue;
    const point = [...points].reverse().find(item => !item?.labels?.le);
    const value = Number(point?.value);
    if (Number.isFinite(value)) return value;
  }
  return null;
}

export function metricScope(name) {
  if (/^(ingot_|event_)/.test(name)) return "ingot";
  if (/^(process_|system_runtime_|dotnet_)/.test(name)) return "runtime";
  if (/^(http_|microsoft_aspnetcore_)/.test(name)) return "http";
  return "other";
}

export function metricScopeLabel(scope) {
  return ({
    all: "全部范围",
    ingot: "Ingot 数据链路",
    runtime: ".NET 运行时",
    http: "HTTP 服务",
    other: "其他",
  })[scope] || scope;
}
