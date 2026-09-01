// 覆盖项目创建和下一配方决定到实际结果的工作台呈现。
import React, { useState } from "react";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { RecipeRecommendationDecisionDrawer } from "../src/research/components/ResearchProjectDrawers";
import { CreateProjectDrawer } from "../src/research/components/CreateResearchProjectDrawer";
import { WorkspaceContent } from "../src/research/components/ResearchWorkspaceContent";
import { projectFormInitial } from "../src/research/researchProjectModel";

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

function jsonResponse(data) {
  return new Response(JSON.stringify({ data }), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}

function CreateProjectHarness() {
  const [form, setForm] = useState(projectFormInitial);
  return <CreateProjectDrawer open saving={false} form={form} setForm={setForm} onClose={() => {}} onSubmit={event => event.preventDefault()} />;
}

function RecipeDecisionHarness() {
  const [form, setForm] = useState({ decision: "accepted", usefulnessRating: "", factors: { temperature: 620 }, reason: "" });
  return <RecipeRecommendationDecisionDrawer
    target={{ item: { recommendationKey: "next-1", parameters: [{ variableCode: "temperature", value: 620, unit: "C" }] } }}
    form={form}
    setForm={setForm}
    saving={false}
    variables={[{ code: "temperature", name: "模具温度", unit: "C" }]}
    onClose={() => {}}
    onSubmit={event => event.preventDefault()}
  />;
}

function renderWorkspace(overrides = {}, props = {}) {
  const baseProject = {
    projectId: "project-1",
    status: "active",
    variables: [{ code: "temperature", name: "模具温度", unit: "C", role: "control" }],
    objectives: [{ code: "yield", name: "良率", unit: "%" }],
  };
  const workspace = {
    optimizationObservationSummary: { validObservationCount: 3, candidateRunCount: 3, observedExecutionKeys: ["RUN-001"] },
    recipeRecommendationFlows: [],
    audit: [],
    ...overrides,
    project: { ...baseProject, ...(overrides.project || {}) },
  };
  return render(<WorkspaceContent
    workspace={workspace}
    loading={false}
    historyLoading={false}
    onLoadOlderHistory={() => {}}
    onGenerateRecipeRecommendation={props.onGenerateRecipeRecommendation || (() => {})}
    onRecipeRecommendationDecision={props.onRecipeRecommendationDecision || (() => {})}
    onLinkRecipeRecommendationExecution={props.onLinkRecipeRecommendationExecution || (() => {})}
    onMaterializeRecipeRecommendationOutcome={props.onMaterializeRecipeRecommendationOutcome || (() => {})}
    onAskAi={() => {}}
  />);
}

describe("研发项目创建抽屉", () => {
  it("读取现有定义，并从工艺配置连续带入模型、目标与控制参数", async () => {
    const fetchMock = vi.fn(url => {
      if (String(url).startsWith("/api/v1/process-executions")) return Promise.resolve(jsonResponse([{
        executionId: "RUN-001", productCode: "LENS-A", equipmentId: "PRESS-01", toolingAssemblyId: "MOLD-A-01", processSpecificationId: "SPEC-A", processSpecificationVersion: 5, siteId: "SITE-001", materialLotRef: "GLASS-LOT-01",
      }]));
      if (url === "/api/v1/inspection-definitions") return Promise.resolve(jsonResponse([{
        code: "VISUAL", version: 1, name: "外观检验", characteristics: [{ code: "scratch-rate", name: "划伤率", inputType: "numeric", unit: "%", upperLimit: 1.5 }],
      }]));
      if (url === "/api/v1/process-data-models") return Promise.resolve(jsonResponse([{
        modelId: "molding", version: 2, name: "精密模压模型", status: "published", controlParameters: [{ code: "mold-temperature", displayName: "模具温度", unit: "℃" }],
      }]));
      if (url === "/api/v1/scenario-packages") return Promise.resolve(jsonResponse([{
        packageId: "lens-production", version: 3, name: "镜片量产配置", status: "published", dataModelId: "molding", dataModelVersion: 2,
      }]));
      return Promise.reject(new Error(`未处理的测试请求：${url}`));
    });
    vi.stubGlobal("fetch", fetchMock);
    render(<CreateProjectHarness />);
    const scenario = await screen.findByLabelText(/^工艺配置（推荐）/);
    fireEvent.change(screen.getByLabelText(/^参考运行/), { target: { value: "RUN-001" } });
    fireEvent.change(scenario, { target: { value: "lens-production:3" } });
    expect(screen.getByLabelText(/^工艺数据字典/)).toHaveValue("molding:2");
    fireEvent.change(screen.getByLabelText(/^质量目标/), { target: { value: "VISUAL:1:scratch-rate" } });
    expect(screen.getByLabelText("数据来源")).toHaveValue("inspection:scratch-rate");
    fireEvent.change(screen.getByLabelText(/^控制参数/), { target: { value: "mold-temperature" } });
    expect(screen.getByLabelText("实际数据来源")).toHaveValue("control-parameter:mold-temperature");
    expect(screen.getByLabelText(/^目标产品/)).toHaveValue("LENS-A");
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(4));
  });
});

describe("下一配方闭环", () => {
  it("拒绝建议时隐藏配方参数，只保留必填原因", () => {
    render(<RecipeDecisionHarness />);
    fireEvent.change(screen.getByLabelText("工程师决定"), { target: { value: "rejected" } });
    expect(screen.queryByLabelText("模具温度")).not.toBeInTheDocument();
    expect(screen.getByLabelText("修改或拒绝原因")).toBeRequired();
  });

  it("呈现拒绝的终态、理由、快照和审计", () => {
    renderWorkspace({
      recipeRecommendationFlows: [{
        allowedActions: [],
        recommendation: { recommendationId: "recommendation-1", generatedAt: "2026-08-30T08:00:00Z", projectSnapshotHash: "abcdef1234567890" },
        item: { recommendationKey: "next-1", parameters: [{ variableCode: "temperature", value: 620, unit: "C" }], prediction: { objectives: { yield: 98 }, rationale: "现场窗口内收益最高且满足安全边界" } },
        decision: { decisionId: "decision-1", decision: "rejected", reason: "当前材料批次不适用", usefulnessRating: "partly-useful", decidedBy: "engineer-b" },
      }],
      audit: [{ entryId: "audit-1", resourceType: "recipe-recommendation-decision", resourceId: "decision-1", action: "decided", userId: "engineer-b", createdAt: "2026-08-30T09:00:00Z" }],
    });
    expect(screen.getByText(/依据：现场窗口内收益最高且满足安全边界/)).toBeInTheDocument();
    expect(screen.getByText("有用性：部分有用")).toBeInTheDocument();
    expect(screen.getByText("终态：无需关联运行或回收结果")).toBeInTheDocument();
    expect(screen.getByText("已拒绝，流程结束")).toBeInTheDocument();
    expect(screen.getByTitle("abcdef1234567890")).toHaveTextContent("abcdef123456");
    expect(screen.queryByRole("button", { name: "关联实际运行" })).not.toBeInTheDocument();
  });

  it("按决定、实际运行和结果顺序开放动作", () => {
    const onDecision = vi.fn();
    const onLink = vi.fn();
    const onOutcome = vi.fn();
    const base = {
      recommendation: { recommendationId: "recommendation-2", generatedAt: "2026-08-30T08:00:00Z" },
      item: { recommendationKey: "next-2", parameters: [], prediction: { objectives: {} } },
    };
    const { rerender } = renderWorkspace({ recipeRecommendationFlows: [{ ...base, allowedActions: ["decide"] }] }, {
      onRecipeRecommendationDecision: onDecision,
      onLinkRecipeRecommendationExecution: onLink,
      onMaterializeRecipeRecommendationOutcome: onOutcome,
    });
    fireEvent.click(screen.getByRole("button", { name: "接受 / 修改 / 拒绝" }));
    expect(onDecision).toHaveBeenCalled();

    rerender(<WorkspaceContent workspace={{ project: { projectId: "project-1", status: "active", variables: [], objectives: [] }, optimizationObservationSummary: { validObservationCount: 3 }, recipeRecommendationFlows: [{ ...base, allowedActions: ["link-execution"], decision: { decisionId: "decision-2", decision: "accepted" } }] }} loading={false} historyLoading={false} onLoadOlderHistory={() => {}} onGenerateRecipeRecommendation={() => {}} onRecipeRecommendationDecision={onDecision} onLinkRecipeRecommendationExecution={onLink} onMaterializeRecipeRecommendationOutcome={onOutcome} onAskAi={() => {}} />);
    fireEvent.click(screen.getByRole("button", { name: "关联实际运行" }));
    expect(onLink).toHaveBeenCalledWith(expect.objectContaining({ decisionId: "decision-2" }));

    rerender(<WorkspaceContent workspace={{ project: { projectId: "project-1", status: "active", variables: [], objectives: [] }, optimizationObservationSummary: { validObservationCount: 3 }, recipeRecommendationFlows: [{ ...base, allowedActions: ["materialize-outcome"], actualExecutionKey: "RUN-002", decision: { decisionId: "decision-2", decision: "accepted" } }] }} loading={false} historyLoading={false} onLoadOlderHistory={() => {}} onGenerateRecipeRecommendation={() => {}} onRecipeRecommendationDecision={onDecision} onLinkRecipeRecommendationExecution={onLink} onMaterializeRecipeRecommendationOutcome={onOutcome} onAskAi={() => {}} />);
    fireEvent.click(screen.getByRole("button", { name: "冻结实际结果" }));
    expect(onOutcome).toHaveBeenCalledWith(expect.objectContaining({ decisionId: "decision-2" }));
  });
});
