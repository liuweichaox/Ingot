
import React from "react";
import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AnalysisReadinessCard } from "../src/pages/AnalysisPages";
import { getJson } from "../src/api/http";

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  sessionStorage.clear();
});

describe("诊断透明降级", () => {
  it.each([
    ["descriptive-only", "仅描述性统计"],
    ["exploratory", "探索性候选"],
    ["candidate-ranking", "候选排序"],
  ])("渲染 %s 模式", (mode, label) => {
    render(<AnalysisReadinessCard diagnosis={{ readiness: { mode, blockingReasons: [] } }} />);
    expect(screen.getByText(label)).toBeInTheDocument();
    cleanup();
  });

  it("展示阻断原因和未测量混杂披露", () => {
    render(<AnalysisReadinessCard diagnosis={{
      readiness: { mode: "descriptive-only", blockingReasons: ["quality-outcomes-missing"] },
      knownUnmeasuredConfounders: [{ name: "操作员经验" }],
      sensitivityAssessment: { reason: "缺少可解释效应估计和置信区间" },
    }} />);
    expect(screen.getByText("质量结果尚未与运行关联")).toBeInTheDocument();
    expect(screen.getByText(/操作员经验/)).toBeInTheDocument();
    expect(screen.getByText("暂不可估")).toBeInTheDocument();
  });
});

describe("HTTP 鉴权反馈", () => {
  it("401 触发重新认证事件，403 显示服务端权限说明", async () => {
    const unauthorized = vi.fn();
    window.addEventListener("ingot:unauthorized", unauthorized, { once: true });
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response("", { status: 401 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ detail: "没有管理成员权限。" }), { status: 403 }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(getJson("/unauthorized")).rejects.toThrow(/状态 401/);
    expect(unauthorized).toHaveBeenCalledOnce();
    await expect(getJson("/forbidden")).rejects.toThrow("没有管理成员权限。");
  });
});
