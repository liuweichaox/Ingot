import React from "react";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";
import { SystemStatusIndicator } from "../src/App";
import { ConfigurationHubPage } from "../src/pages/RegistryPages";
import { EmptyState } from "../src/ui/components";

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

describe("生产界面状态反馈", () => {
  it("在全局导航中展示平台与现场节点状态", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([
      { edgeId: "edge-01", lastSeen: new Date().toISOString() },
    ])));

    render(<MemoryRouter><SystemStatusIndicator /></MemoryRouter>);

    expect(await screen.findByRole("link", { name: "系统状态：平台正常，现场节点 1/1 在线" })).toHaveAttribute("href", "/platform-metrics");
  });

  it("让空状态承载诊断信息和下一步操作", () => {
    render(
      <EmptyState
        title="还没有形成生产运行"
        description="请先完成现场接入。"
        details={<span>现场节点 0/1 在线</span>}
        actions={<button type="button">查看现场节点</button>}
      />,
    );

    expect(screen.getByText("现场节点 0/1 在线")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "查看现场节点" })).toBeInTheDocument();
  });

  it("为未完成配置提供进度和可直达的操作", async () => {
    vi.stubGlobal("fetch", vi.fn().mockImplementation(() => Promise.resolve(jsonResponse([]))));

    render(<MemoryRouter><ConfigurationHubPage /></MemoryRouter>);

    await waitFor(() => expect(screen.getByRole("progressbar", { name: "配置准备进度" })).toHaveAttribute("aria-valuenow", "0"));
    expect(screen.getByText("还需完成 5 项")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /配置现场接入/ })).toHaveAttribute("href", "/configuration/ingestion-tasks");
    expect(screen.getByRole("link", { name: /发布配置方案/ })).toHaveAttribute("href", "/configuration/scenario-packages");
  });
});
