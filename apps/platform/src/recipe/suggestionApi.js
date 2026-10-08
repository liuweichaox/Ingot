// 配方建议页面使用的服务端入口。浏览器地址不暴露这组路径。
import { getJson, postJson } from "../api/http";

const scopes = "/api/v1/research-projects";

export function listScopes() {
  return getJson(`${scopes}?limit=100`);
}

export function createScope(body) {
  return postJson(scopes, body);
}

export function activateScope(projectId, revision) {
  return postJson(`${scopes}/${projectId}/status`, { targetStatus: "active", revision });
}

export function readReadiness(projectId) {
  return getJson(`${scopes}/${projectId}/optimization-readiness`);
}

export function listSuggestionFlows(projectId) {
  return getJson(`${scopes}/${projectId}/recipe-recommendation-flows?limit=100`);
}

export function createSuggestion(projectId) {
  return postJson(`${scopes}/${projectId}/recipe-recommendations`, { seed: 1 });
}

export function recordDecision(recommendationId, recommendationKey, body) {
  return postJson(
    `${scopes}/recipe-recommendations/${recommendationId}/items/${encodeURIComponent(recommendationKey)}/decision`,
    body,
  );
}

export function linkRun(decisionId, actualExecutionKey) {
  return postJson(`${scopes}/recipe-recommendation-decisions/${decisionId}/execution-link`, { actualExecutionKey });
}

export function freezeOutcome(decisionId) {
  return postJson(`${scopes}/recipe-recommendation-decisions/${decisionId}/materialize-outcome`, {});
}
