// 验证配方建议页能列出范围、展示待决定建议，并提交采用。
import React from "react";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";
import { RecipeSuggestionsPage } from "../src/pages/RecipeSuggestionsPage";

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

const scope = {
  projectId: "11111111-1111-4111-8111-111111111111",
  name: "镜片配方",
  processName: "模压",
  status: "active",
  revision: 2,
  siteCode: "SITE-001",
};

const flow = {
  state: "pending-decision",
  recommendation: { recommendationId: "22222222-2222-4222-8222-222222222222" },
  item: {
    recommendationKey: "recipe-001",
    parameters: [{ variableCode: "temperature", value: 500, unit: "Cel" }],
    prediction: { rationale: "在已观察范围内靠近目标。", objectives: { "form-error": { mean: 0.4, unit: "um" } } },
  },
  decision: null,
  allowedActions: ["decide"],
};

describe("配方建议链路", () => {
  it("没有范围时引导先发布配方", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => response({ data: [] })));
    render(<MemoryRouter><RecipeSuggestionsPage /></MemoryRouter>);
    expect(await screen.findByRole("heading", { name: "划定建议范围" })).toBeInTheDocument();
    expect(screen.getByText("还没有已发布配方")).toBeInTheDocument();
  });

  it("待决定建议可以提交采用", async () => {
    const fetchMock = vi.fn(async (url, options = {}) => {
      const path = String(url);
      if (path.includes("optimization-readiness")) {
        return response({ candidateRunCount: 4, validObservationCount: 3, excludedObservationCount: 1 });
      }
      if (path.includes("recipe-recommendation-flows")) return response({ items: [flow] });
      if (path.includes("/decision")) return response({ decisionId: "33333333-3333-4333-8333-333333333333", decision: "accepted" });
      if (path.includes("research-projects")) return response({ data: [scope] });
      return response({ data: [] });
    });
    vi.stubGlobal("fetch", fetchMock);
    render(<MemoryRouter><RecipeSuggestionsPage /></MemoryRouter>);
    expect(await screen.findByRole("heading", { name: "待决定" })).toBeInTheDocument();
    expect(screen.getByText(/4 条运行/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "采用" }));
    await waitFor(() => expect(fetchMock.mock.calls.some(([url, options]) =>
      String(url).includes("/items/recipe-001/decision") && JSON.parse(options.body).decision === "accepted")).toBe(true));
  });
});
