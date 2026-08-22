// 验证机理知识录入使用业务名称、自动单位和受控项目上下文。
import React from "react";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MechanismKnowledgeWorkbench } from "../src/components/MechanismKnowledgeWorkbench";

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

describe("机理知识工作台", () => {
  it("按业务名称选择变量并自动带入单位和项目适用范围", async () => {
    const fetchMock = vi.fn(url => {
      const path = String(url);
      if (path.endsWith("/mechanism-claims/conflicts")) return Promise.resolve(response({ data: [] }));
      if (path.endsWith("/mechanism-claims")) return Promise.resolve(response({ data: [] }));
      if (path.endsWith("/api/v1/research-projects/research-active")) {
        return Promise.resolve(response({
          project: {
            projectId: "research-active",
            code: "OPTICAL-SURFACE",
            processName: "精密光学模压工艺",
            productName: "LENS-A",
            materialName: "GLASS-LOT-2408",
            siteCode: "SITE-001",
            context: {
              equipment: "PRESS-01",
              tooling: "MOLD-A-01",
              "process-specification": "SPEC-OPTICAL-A@5",
            },
            variables: [
              { code: "holding.temperature", name: "保压温度", role: "control", unit: "°C" },
              { code: "holding.pressure", name: "保压压力", role: "control", unit: "kN" },
            ],
          },
          hypotheses: [],
          experiments: [],
          experimentResults: [],
        }));
      }
      return Promise.reject(new Error(`未处理的测试请求：${path}`));
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<MechanismKnowledgeWorkbench
      projectId="research-active"
      sources={[{
        sourceId: "knowledge-sop",
        title: "模压作业指导书",
        extractionStatus: "completed",
        sha256: "a".repeat(64),
      }]}
      reloadAssets={() => {}}
    />);

    const variable = await screen.findByLabelText("项目变量");
    expect(variable).toHaveTextContent("保压温度（°C）");
    expect(screen.queryByText("变量代码", { exact: true })).not.toBeInTheDocument();

    fireEvent.change(variable, { target: { value: "holding.temperature" } });
    expect(screen.getByLabelText("作用变量单位")).toHaveValue("°C");
    expect(screen.getByLabelText("作用变量单位")).toHaveAttribute("readonly");

    await waitFor(() => expect(screen.getByLabelText("适用维度")).toHaveValue("product"));
    expect(screen.getByLabelText("适用对象")).toHaveValue("LENS-A");

    fireEvent.change(screen.getByLabelText("适用维度"), { target: { value: "equipment" } });
    expect(screen.getByLabelText("适用对象")).toHaveValue("PRESS-01");
    expect(screen.getByLabelText("适用对象")).toHaveTextContent("PRESS-01");
  });
});
