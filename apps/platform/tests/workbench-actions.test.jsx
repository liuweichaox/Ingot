// 验证各岗位工作台的操作优先级及入口唯一性。
import React from "react";
import { cleanup, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";
import { WorkbenchPage } from "../src/pages/OperationsPages";

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

function stubWorkbench(executionTotal = 2) {
  vi.stubGlobal("fetch", vi.fn().mockImplementation(url => {
    const path = String(url);
    const payload = path.includes("/process-executions")
      ? { items: [], total: executionTotal }
      : path.includes("/inspection-tasks/summary")
        ? { pending: 2 }
        : path.includes("/edges")
          ? [{ edgeId: "edge-01", lastSeen: new Date().toISOString() }]
          : path.includes("/production-contexts")
            ? [{ contextId: "context-01" }]
            : [];
    return Promise.resolve(new Response(JSON.stringify(payload), {
      headers: { "Content-Type": "application/json" },
    }));
  }));
}

describe("工作台岗位操作", () => {
  it.each([
    ["检验员", ["quality.inspector"], "质量待办", ["/inspections", "/analysis", "/edges"]],
    ["质量复核员", ["quality.reviewer"], "质量待办", ["/inspections", "/analysis", "/edges"]],
    ["工程师", ["process.engineer"], "下一步", ["/analysis", "/inspections", "/edges"]],
    ["管理员", ["platform.admin"], "下一步", ["/analysis", "/edges", "/inspections"]],
    ["工程与质量兼岗", ["process.engineer", "quality.inspector"], "下一步", ["/analysis", "/inspections", "/edges"]],
    ["无特定岗位", [], "下一步", ["/analysis", "/inspections", "/edges"]],
  ])("为%s提供三个不同入口并保持优先级", async (_name, roles, heading, expectedPaths) => {
    stubWorkbench();
    render(<MemoryRouter><WorkbenchPage identity={{ roles }} /></MemoryRouter>);

    const card = (await screen.findByRole("heading", { name: heading, exact: true })).closest("section");
    const paths = within(card).getAllByRole("link").map(link => link.getAttribute("href"));
    expect(paths).toEqual(expectedPaths);
    expect(new Set(paths).size).toBe(3);
  });

  it("在运行不足时将工程师分析入口指向运行记录", async () => {
    stubWorkbench(1);
    render(<MemoryRouter><WorkbenchPage identity={{ roles: ["process.engineer"] }} /></MemoryRouter>);

    const card = (await screen.findByRole("heading", { name: "下一步", exact: true })).closest("section");
    expect(within(card).getAllByRole("link").map(link => link.getAttribute("href")))
      .toEqual(["/process-executions", "/inspections", "/edges"]);
  });
});
