// 验证拆分后的研发项目组件仍覆盖配置复用、空态和失败关闭边界。
import React, { useState } from "react";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { CreateProjectDrawer } from "../src/research/components/CreateResearchProjectDrawer";
import {
  HistoricalReplayCard,
  OnlineAdmissionCard,
} from "../src/research/components/ResearchEvidenceCards";
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
  return (
    <CreateProjectDrawer
      open
      saving={false}
      form={form}
      setForm={setForm}
      onClose={() => {}}
      onSubmit={event => event.preventDefault()}
    />
  );
}

describe("研发项目创建抽屉", () => {
  it("读取现有定义，并从工艺配置连续带入模型、目标与控制参数", async () => {
    const fetchMock = vi.fn(url => {
      if (String(url).startsWith("/api/v1/process-executions")) {
        return Promise.resolve(jsonResponse([{ executionId: "RUN-001", productCode: "LENS-A", equipmentId: "PRESS-01" }]));
      }
      if (url === "/api/v1/inspection-definitions") {
        return Promise.resolve(jsonResponse([{
          code: "VISUAL",
          version: 1,
          name: "外观检验",
          characteristics: [{ code: "scratch-rate", name: "划伤率", inputType: "numeric", unit: "%", upperLimit: 1.5 }],
        }]));
      }
      if (url === "/api/v1/process-data-models") {
        return Promise.resolve(jsonResponse([{
          modelId: "molding",
          version: 2,
          name: "精密模压模型",
          status: "published",
          controlParameters: [{ code: "mold-temperature", displayName: "模具温度", unit: "℃" }],
        }]));
      }
      if (url === "/api/v1/scenario-packages") {
        return Promise.resolve(jsonResponse([{
          packageId: "lens-production",
          version: 3,
          name: "镜片量产配置",
          status: "published",
          dataModelId: "molding",
          dataModelVersion: 2,
        }]));
      }
      return Promise.reject(new Error(`未处理的测试请求：${url}`));
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<CreateProjectHarness />);

    const scenario = await screen.findByLabelText(/^工艺配置（推荐）/);
    fireEvent.change(scenario, { target: { value: "lens-production:3" } });
    expect(screen.getByLabelText(/^工艺数据字典/)).toHaveValue("molding:2");

    fireEvent.change(screen.getByLabelText(/^质量目标/), { target: { value: "VISUAL:1:scratch-rate" } });
    expect(screen.getByLabelText("数据来源")).toHaveValue("inspection:scratch-rate");
    expect(screen.getByLabelText("指标单位")).toHaveValue("%");

    fireEvent.change(screen.getByLabelText(/^控制参数/), { target: { value: "mold-temperature" } });
    expect(screen.getByLabelText("实际数据来源")).toHaveValue("control-parameter:mold-temperature");
    expect(screen.getByLabelText("变量单位")).toHaveValue("℃");

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(4));
  });
});

describe("研发证据状态", () => {
  it("没有历史回放时显示可行动的证据要求", () => {
    render(<HistoricalReplayCard reports={[]} currentUserId="engineer-1" onReview={() => {}} />);
    expect(screen.getByText("尚未生成历史回放报告")).toBeInTheDocument();
    expect(screen.getByText(/至少积累 3 种不同的完整实际工艺规范条件/)).toBeInTheDocument();
  });

  it("在线准入失败时明确禁止进入，并同时展示失败与运行前确认", () => {
    render(<OnlineAdmissionCard evidence={{
      eligible: false,
      validShadowOutcomeCount: 2,
      shadowRecommendationCount: 5,
      failures: ["独立历史回放尚未通过"],
      warnings: ["现场工程师必须确认回退目标"],
    }} />);

    expect(screen.getByText("禁止进入在线")).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent("独立历史回放尚未通过");
    expect(screen.getByText("运行前必须确认")).toBeInTheDocument();
    expect(screen.getByText("现场工程师必须确认回退目标")).toBeInTheDocument();
  });
});
