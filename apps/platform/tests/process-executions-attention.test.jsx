// 验证运行记录的“需要处理”筛选只保留质量或数据异常的已完成运行。
import React from "react";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, useLocation } from "react-router";
import { ProcessExecutionsPage } from "../src/pages/OperationsPages";

function CurrentLocation() {
  const location = useLocation();
  return <output aria-label="当前位置">{`${location.pathname}${location.search}`}</output>;
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

function stubExecutions(items) {
  const fetchMock = vi.fn().mockImplementation(url => {
    const path = String(url);
    const payload = path.includes("/api/v1/auth/me")
      ? { siteIds: ["SITE-001"] }
      : path.includes("/api/edges")
      ? [{ edgeId: "edge-01", siteId: "SITE-001" }]
      : path.includes("/process-executions")
        ? { items, total: items.length }
        : [];
    return Promise.resolve(new Response(JSON.stringify(payload), { headers: { "Content-Type": "application/json" } }));
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

const execution = (executionId, overrides = {}) => ({
  executionId,
  siteId: "SITE-001",
  status: "completed",
  equipmentId: "press-01",
  qualityStatus: "pass",
  processDataQuality: { status: "complete" },
  ...overrides,
});

describe("运行记录需要处理筛选", () => {
  it("保留同一设备单元内不同逻辑对象的身份，并兼容旧运行", async () => {
    stubExecutions([
      execution("RUN-CAVITY-1", { subjectType: "cavity", subjectId: "M01C01" }),
      execution("RUN-CAVITY-2", { subjectType: "cavity", subjectId: "M01C02" }),
      execution("RUN-LEGACY"),
    ]);
    render(<MemoryRouter initialEntries={["/process-executions"]}><ProcessExecutionsPage /></MemoryRouter>);

    expect(await screen.findByText("逻辑对象 M01C01")).toBeInTheDocument();
    expect(screen.getByText("逻辑对象 M01C02")).toBeInTheDocument();
    expect(screen.getByText("RUN-LEGACY")).toBeInTheDocument();
    expect(screen.getAllByText("press-01")).toHaveLength(3);
  });

  it("读取已完成运行并只列出质量或数据异常的运行", async () => {
    const fetchMock = stubExecutions([
      execution("RUN-OK"),
      execution("RUN-FAIL", { qualityStatus: "fail" }),
      execution("RUN-DATA", { processDataQuality: { status: "degraded" } }),
    ]);
    render(<MemoryRouter initialEntries={["/process-executions?attention=1"]}><ProcessExecutionsPage /></MemoryRouter>);

    expect(await screen.findByText("RUN-FAIL")).toBeInTheDocument();
    expect(screen.getByText("RUN-DATA")).toBeInTheDocument();
    expect(screen.queryByText("RUN-OK")).toBeNull();
    expect(screen.getByRole("heading", { name: "需要处理的运行" })).toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes("status=completed") && String(url).includes("siteId=SITE-001"))).toBe(true);
  });

  it("没有异常运行时给出空状态", async () => {
    stubExecutions([execution("RUN-OK")]);
    render(<MemoryRouter initialEntries={["/process-executions?attention=1"]}><ProcessExecutionsPage /><CurrentLocation /></MemoryRouter>);

    expect(await screen.findByText("没有需要处理的运行")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "查看全部运行" }));
    expect(await screen.findByText("RUN-OK")).toBeInTheDocument();
    expect(screen.getByLabelText("当前位置")).toHaveTextContent(/^\/process-executions\?siteId=SITE-001$/);
  });

  it("默认不过滤运行", async () => {
    stubExecutions([execution("RUN-OK")]);
    render(<MemoryRouter initialEntries={["/process-executions"]}><ProcessExecutionsPage /></MemoryRouter>);

    expect(await screen.findByText("RUN-OK")).toBeInTheDocument();
  });
});
