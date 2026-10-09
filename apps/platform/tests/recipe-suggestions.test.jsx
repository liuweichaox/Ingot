// 验证已发布配方版本上的下一轮校正：生成条件来自该版本，采用不产生新版本，显著变更交给修订草稿。
import React from "react";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { RecipeVersionProposal } from "../src/recipe/RecipeVersionProposal";

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

function response(payload) {
  return new Response(JSON.stringify(payload), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}

const specification = {
  processSpecificationId: "lens-press",
  version: 2,
  status: "published",
  dataModelId: "lens-model",
  dataModelVersion: 1,
  values: [{ code: "temperature", value: 500 }],
};

const model = {
  modelId: "lens-model",
  version: 1,
  controlParameters: [{ code: "temperature", displayName: "温度", unit: "Cel", dataType: "double", minimum: 480, maximum: 550 }],
};

const definition = {
  code: "lens-inspection",
  version: 1,
  name: "镜片检测",
  characteristics: [{ code: "form-error", name: "面形误差", unit: "um", inputType: "numeric", lowerLimit: 0, upperLimit: 1 }],
};

const qualityPlan = {
  planId: "lens-form",
  version: 1,
  status: "published",
  scope: { processSpecificationId: "lens-press" },
  items: [{ definitionCode: "lens-inspection", definitionVersion: 1 }],
};

const completedRun = {
  executionId: "RUN-1",
  siteId: "SITE-001",
  processSpecificationId: "lens-press",
  processSpecificationVersion: "2",
  productFamilyCode: "lens",
  productCode: "asph-01",
  equipmentId: "press-01",
  startedAt: "2026-10-01T00:00:00Z",
  qualityStatus: "COMPLETE",
};

const flow = {
  state: "pending-decision",
  recommendation: {
    recommendationId: "22222222-2222-4222-8222-222222222222",
    siteCode: "SITE-001",
    processSpecificationId: "lens-press",
    brief: { name: "lens-press 第 2 版", context: { process_specification_version: "2" } },
  },
  item: {
    recommendationKey: "recipe-001",
    parameters: [{ variableCode: "temperature", value: 510, unit: "Cel" }],
    prediction: { rationale: "在已观察范围内靠近目标。", objectives: { "form-error": { mean: 0.4, unit: "um" } } },
  },
  decision: null,
  allowedActions: ["decide"],
};

function routes(overrides = {}) {
  return vi.fn(async (url, options = {}) => {
    const path = String(url);
    for (const [fragment, payload] of Object.entries(overrides)) {
      if (path.includes(fragment)) return response(typeof payload === "function" ? payload(options) : payload);
    }
    if (path.includes("process-data-models")) return response({ data: [model] });
    if (path.includes("inspection-definitions")) return response({ data: [definition] });
    if (path.includes("inspection-plans")) return response({ data: [qualityPlan] });
    if (path.includes("/api/edges")) return response({ data: [{ edgeId: "EDGE-1", siteId: "SITE-001" }] });
    if (path.includes("process-executions")) return response({ data: [completedRun] });
    return response({ data: [] });
  });
}

function renderProposal(overrides, onPromote = vi.fn()) {
  const fetchMock = routes(overrides);
  vi.stubGlobal("fetch", fetchMock);
  render(<RecipeVersionProposal specification={specification} onPromote={onPromote} />);
  return { fetchMock, onPromote };
}

describe("配方版本上的下一轮校正", () => {
  it("按这一版配方和已完成运行提交冻结的生成条件", async () => {
    const { fetchMock } = renderProposal({
      "/recipe-recommendations/flows": { items: [] },
      "/recipe-recommendations/readiness": { candidateRunCount: 4, validObservationCount: 3, excludedObservationCount: 1 },
    });
    expect(await screen.findByText(/asph-01/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "检查数据" }));
    await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => String(url).includes("/readiness"))).toBe(true));
    const [, options] = fetchMock.mock.calls.find(([url]) => String(url).includes("/readiness"));
    const brief = JSON.parse(options.body);
    expect(brief.siteCode).toBe("SITE-001");
    expect(brief.processSpecificationId).toBe("lens-press");
    expect(brief.context.process_specification_version).toBe("2");
    expect(brief.context.product_code).toBe("asph-01");
    expect(brief.objectives).toEqual([expect.objectContaining({ code: "form-error", direction: "minimize", target: 0 })]);
    expect(brief.variables).toEqual([expect.objectContaining({ code: "temperature", lowerLimit: 480, upperLimit: 550 })]);
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes("research-projects"))).toBe(false);
  });

  it("决定之后的同配方运行会自动接上", async () => {
    const pendingExecution = {
      ...flow,
      state: "pending-execution",
      decision: {
        decisionId: "44444444-4444-4444-8444-444444444444",
        decision: "accepted",
        decidedAt: "2026-10-09T00:00:00Z",
      },
      allowedActions: ["link-execution"],
    };
    const { fetchMock } = renderProposal({
      "/recipe-recommendations/flows": { items: [pendingExecution] },
      "/process-executions": {
        items: [
          { executionId: "RUN-OLD", siteId: "SITE-001", processSpecificationId: "lens-press", processSpecificationVersion: "2", startedAt: "2026-10-08T00:00:00Z" },
          { executionId: "RUN-OTHER", siteId: "SITE-001", processSpecificationId: "other-press", processSpecificationVersion: "1", startedAt: "2026-10-09T00:00:30Z" },
          { executionId: "RUN-NEXT", siteId: "SITE-001", processSpecificationId: "lens-press", processSpecificationVersion: "2", startedAt: "2026-10-09T00:01:00Z" },
        ],
      },
      "/execution-link": { decisionId: pendingExecution.decision.decisionId },
    });
    expect(await screen.findByText("已发布版本不变。决定之后，下一轮使用这一版配方并完成的运行会自动接上。")).toBeInTheDocument();
    await waitFor(() => expect(fetchMock.mock.calls.some(([url, options]) =>
      String(url).includes("/recipe-recommendations/decisions/44444444-4444-4444-8444-444444444444/execution-link") &&
      JSON.parse(options.body).actualExecutionKey === "RUN-NEXT")).toBe(true));
  });

  it("采用为下一轮校正时不创建新版本", async () => {
    const { fetchMock } = renderProposal({
      "/recipe-recommendations/flows": { items: [flow] },
      "/decision": { decisionId: "33333333-3333-4333-8333-333333333333", decision: "accepted" },
    });
    expect(await screen.findByRole("heading", { name: "待决定" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "采用为下一轮校正" }));
    await waitFor(() => expect(fetchMock.mock.calls.some(([url, options]) =>
      String(url).includes("/recipe-recommendations/22222222-2222-4222-8222-222222222222/items/recipe-001/decision") &&
      JSON.parse(options.body).decision === "accepted")).toBe(true));
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes("/drafts"))).toBe(false);
  });

  it("作为显著变更时把建议参数交给修订草稿，不登记校正决定", async () => {
    const { fetchMock, onPromote } = renderProposal({
      "/recipe-recommendations/flows": { items: [flow] },
    });
    expect(await screen.findByText(/asph-01/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "作为显著变更" }));
    expect(onPromote).toHaveBeenCalledWith(expect.objectContaining({
      parameterOverrides: [expect.objectContaining({ code: "temperature", value: "510" })],
      evidenceReferences: [{ kind: "process-execution", referenceId: "RUN-1" }],
    }));
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes("/decision"))).toBe(false);
  });
});
