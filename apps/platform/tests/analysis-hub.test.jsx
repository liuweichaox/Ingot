// 验证分析入口在站点选择、空状态和导航方面的交互边界。
import React from "react";
import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";
import { AnalysisHubPage } from "../src/pages/AnalysisHubPage";

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

function jsonResponse(payload) {
  return new Response(JSON.stringify(payload), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}

describe("追因总览请求范围", () => {
  it("只读取已完成运行", async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(jsonResponse([])));
    vi.stubGlobal("fetch", fetchMock);

    render(
      <MemoryRouter>
        <AnalysisHubPage />
      </MemoryRouter>,
    );

    expect(await screen.findByText("没有待分析运行")).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/process-executions?status=completed&limit=50");
    expect(screen.queryByText("进行中项目")).toBeNull();
  });
});
