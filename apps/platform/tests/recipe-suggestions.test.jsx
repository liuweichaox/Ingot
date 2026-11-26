// 验证已发布配方版本上的下一轮校正：生成条件来自该版本，采用不产生新版本，显著变更交给修订草稿。
import React from "react";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import { RecipeVersionProposal } from "../src/recipe/RecipeVersionProposal";
import { OutcomeReceipt, RecommendationEvidence } from "../src/recipe/RecommendationEvidence";
import { inferObjective, runScopes } from "../src/recipe/proposalScope";

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
    modelVersion: "recipe-test-model",
    observationCount: 3,
    observationReferences: [{ executionKey: "RUN-1", sourceContentHash: "a".repeat(64) }],
    brief: { name: "lens-press 第 2 版", context: { process_specification_version: "2" } },
  },
  item: {
    recommendationKey: "recipe-001",
    parameters: [{ variableCode: "temperature", value: 510, unit: "Cel" }],
    prediction: {
      rationale: "在已观察范围内靠近目标。",
      feasibilityProbability: 0.87,
      objectives: { "form-error": { mean: 0.4, lower95: 0.3, upper95: 0.5, unit: "um" } },
      constraints: { "crack-rate": { mean: 0.01, lower95: 0, upper95: 0.02, unit: "1" } },
    },
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
  render(<MemoryRouter><RecipeVersionProposal specification={specification} siteId="SITE-001" onPromote={onPromote} /></MemoryRouter>);
  return { fetchMock, onPromote };
}

describe("配方版本上的下一轮校正", () => {
  it("保留质量规格并按实际组合选择工况", () => {
    expect(inferObjective({ lowerLimit: 48, upperLimit: 52 })).toEqual({ direction: "range", target: 50, lowerLimit: 48, upperLimit: 52 });
    expect(inferObjective({ lowerLimit: null, upperLimit: 0 })).toEqual({ direction: "minimize", target: 0, upperLimit: 0 });
    expect(inferObjective({ lowerLimit: 2, upperLimit: null })).toEqual({ direction: "maximize", target: 2, lowerLimit: 2 });
    expect(inferObjective({ lowerLimit: null, upperLimit: null })).toBeNull();
    expect(inferObjective({ lowerLimit: 2, upperLimit: 1 })).toBeNull();
    const scopes = runScopes([{ productFamilyCode: "A", productCode: "1", equipmentId: "E" }, { productFamilyCode: "A", productCode: "2", equipmentId: "E" }, { productFamilyCode: "B", productCode: "3", equipmentId: "E" }, { productFamilyCode: "B", productCode: "3", equipmentId: "E" }]);
    expect(scopes[0].values).toEqual(["B", "3", "E"]);
  });
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
    expect(brief.objectives).toEqual([expect.objectContaining({ code: "form-error", direction: "range", lowerLimit: 0, upperLimit: 1 })]);
    expect(brief.variables).toEqual([expect.objectContaining({ code: "temperature", lowerLimit: 480, upperLimit: 550 })]);
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes("research-projects"))).toBe(false);
  });

  it("决定之后列出候选运行，由工程师确认后才关联", async () => {
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

    expect(await screen.findByText("RUN-NEXT")).toBeInTheDocument();
    expect(screen.queryByText("RUN-OLD")).not.toBeInTheDocument();
    expect(screen.queryByText("RUN-OTHER")).not.toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes("/execution-link"))).toBe(false);

    fireEvent.click(screen.getByRole("button", { name: "核对此运行" }));
    expect(screen.getByText(/实际参数将在冻结结果时核对/)).toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes("/execution-link"))).toBe(false);
    fireEvent.click(screen.getByRole("button", { name: "取消" }));
    expect(screen.queryByLabelText("确认关联运行")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "核对此运行" }));
    fireEvent.click(screen.getByRole("button", { name: "确认关联" }));
    await waitFor(() => expect(fetchMock.mock.calls.some(([url, options]) =>
      String(url).includes("/recipe-recommendations/decisions/44444444-4444-4444-8444-444444444444/execution-link") &&
      JSON.parse(options.body).actualExecutionKey === "RUN-NEXT")).toBe(true));
  });

  it("结果冻结前允许纠正已关联的运行", async () => {
    const pendingOutcome = {
      ...flow,
      state: "pending-outcome",
      decision: {
        decisionId: "44444444-4444-4444-8444-444444444444",
        decision: "accepted",
        decidedAt: "2026-10-09T00:00:00Z",
        actualExecutionKey: "RUN-WRONG",
      },
    };
    const { fetchMock } = renderProposal({
      "/recipe-recommendations/flows": { items: [pendingOutcome] },
      "/process-executions": { items: [{ ...completedRun, executionId: "RUN-RIGHT", startedAt: "2026-10-10T00:00:00Z" }] },
      "/execution-link": { decisionId: pendingOutcome.decision.decisionId },
    });
    expect(await screen.findByText(/当前关联运行：RUN-WRONG/)).toBeInTheDocument();
    fireEvent.click(await screen.findByRole("button", { name: "核对此运行" }));
    fireEvent.click(screen.getByRole("button", { name: "确认关联" }));
    await waitFor(() => expect(fetchMock.mock.calls.some(([url, options]) =>
      String(url).includes("/execution-link") && JSON.parse(options.body).actualExecutionKey === "RUN-RIGHT")).toBe(true));
  });

  it("展示预测区间与冻结结果的执行一致性回执", async () => {
    const frozen = {
      ...flow,
      state: "outcome-frozen",
      decision: {
        decisionId: "55555555-5555-4555-8555-555555555555",
        decision: "accepted",
        decidedAt: "2026-10-09T00:00:00Z",
        prediction: flow.item.prediction,
        outcome: {
          actualExecutionKey: "RUN-FROZEN",
          outcomes: { "form-error": 0.42 },
          constraintOutcomes: { "crack-rate": 0.01 },
          validForOptimization: true,
          executionAdherence: {
            status: "within-tolerance",
            policyVersion: "execution-adherence-v1",
            variables: [{
              variableCode: "temperature",
              expectedValue: 510,
              actualValue: 510.5,
              deviation: 0.5,
              tolerance: 1,
              unit: "Cel",
              withinTolerance: true,
            }],
            checkedAt: "2026-10-09T01:00:00Z",
          },
          capturedAt: "2026-10-09T01:00:00Z",
        },
      },
      allowedActions: [],
    };
    renderProposal({ "/recipe-recommendations/flows": { items: [frozen] } });

    expect(await screen.findByText("按决定执行")).toBeInTheDocument();
    expect(screen.getByText("RUN-FROZEN", { exact: false })).toBeInTheDocument();
    expect(screen.getAllByText(/95% 区间/).length).toBeGreaterThan(0);
    expect(screen.getByText(/0\.42/)).toBeInTheDocument();
    expect(screen.getByText("在容差内")).toBeInTheDocument();
  });

  it("支持输入不在最近运行列表中的运行号", async () => {
    const pending = { ...flow, state: "pending-execution", decision: { decisionId: "decision-1", decision: "accepted", decidedAt: "2026-10-09T00:00:00Z" } };
    const { fetchMock } = renderProposal({ "/recipe-recommendations/flows": { items: [pending] }, "/execution-link": { decisionId: "decision-1" } });
    const input = await screen.findByLabelText("输入运行号 recipe-001");
    fireEvent.change(input, { target: { value: "RUN-OLDER-THAN-FIRST-PAGE" } });
    fireEvent.click(screen.getByRole("button", { name: "核对输入的运行" }));
    expect(screen.getByText(/最近运行列表没有该运行的详细信息/)).toBeInTheDocument();
    expect(screen.getByText(/实际参数将在冻结结果时核对/)).toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes("/execution-link"))).toBe(false);
    fireEvent.click(screen.getByRole("button", { name: "确认关联" }));
    await waitFor(() => expect(fetchMock.mock.calls.some(([url, options]) => String(url).includes("/execution-link") && JSON.parse(options.body).actualExecutionKey === "RUN-OLDER-THAN-FIRST-PAGE")).toBe(true));
  });

  it("缺少可行概率时不显示零概率", () => {
    render(<RecommendationEvidence recommendation={{}} prediction={{ feasibilityProbability: null }} />);
    expect(screen.queryByText(/可行概率/)).not.toBeInTheDocument();
  });

  it.each([["modified", "within-tolerance"], ["accepted", "outside-tolerance"], ["accepted", null]])("%s 决定、%s 执行一致性不计算区间命中", (decisionType, status) => {
    const decision = { decision: decisionType, prediction: flow.item.prediction, outcome: { outcomes: { "form-error": 0.42 }, executionAdherence: status ? { status } : null } };
    render(<MemoryRouter><OutcomeReceipt decision={decision} siteId="SITE-001" /></MemoryRouter>);
    expect(screen.getByText("不适用")).toBeInTheDocument();
    expect(screen.queryByText("是", { exact: true })).not.toBeInTheDocument();
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
