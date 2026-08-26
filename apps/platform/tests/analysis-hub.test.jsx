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
  it("质量岗位只读取已完成运行，并隐藏无权访问的研发项目", async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(jsonResponse([])));
    vi.stubGlobal("fetch", fetchMock);

    render(
      <MemoryRouter>
        <AnalysisHubPage identity={{ roles: ["quality.inspector"] }} />
      </MemoryRouter>,
    );

    expect(await screen.findByText("没有待分析运行")).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/process-executions?status=completed&limit=50");
    expect(screen.queryByText("待验证项目")).toBeNull();
    expect(screen.queryByText("验证中项目")).toBeNull();
  });

  it.each(["process.engineer", "platform.admin"])("%s 可以读取并查看研发项目", async role => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(jsonResponse([])));
    vi.stubGlobal("fetch", fetchMock);

    render(
      <MemoryRouter>
        <AnalysisHubPage identity={{ roles: [role] }} />
      </MemoryRouter>,
    );

    expect(await screen.findByText("没有待验证项目")).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual(expect.arrayContaining([
      "/api/v1/process-executions?status=completed&limit=50",
      "/api/v1/research-projects?limit=100",
    ]));
  });
});
