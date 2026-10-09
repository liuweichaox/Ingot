// 已发布配方版本上的校正使用的服务端入口。浏览器地址不暴露这组路径。
import { getJson, postJson } from "../api/http";

const base = "/api/v1/recipe-recommendations";

export function readReadiness(brief) {
  return postJson(`${base}/readiness`, brief);
}

export function createSuggestion(brief) {
  return postJson(base, { brief, seed: 1 });
}

export function listSuggestionFlows({ siteId, processSpecificationId } = {}) {
  const query = new URLSearchParams({ limit: "100" });
  if (siteId) query.set("siteId", siteId);
  if (processSpecificationId) query.set("processSpecificationId", processSpecificationId);
  return getJson(`${base}/flows?${query}`);
}

export function recordDecision(recommendationId, recommendationKey, body) {
  return postJson(`${base}/${recommendationId}/items/${encodeURIComponent(recommendationKey)}/decision`, body);
}

export function linkRun(decisionId, actualExecutionKey) {
  return postJson(`${base}/decisions/${decisionId}/execution-link`, { actualExecutionKey });
}

export function freezeOutcome(decisionId) {
  return postJson(`${base}/decisions/${decisionId}/materialize-outcome`, {});
}
