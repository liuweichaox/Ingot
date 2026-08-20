// 验证前端 security-and-readiness 的渲染、交互、错误和边界状态。

import React from "react";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AnalysisReadinessCard } from "../src/pages/AnalysisPages";
import { MemberManagementButton } from "../src/pages/ResearchProjectsPage";
import { getJson, patchJson } from "../src/api/http";

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  sessionStorage.clear();
});

describe("项目成员权限与请求契约", () => {
  it("只为 Owner 或管理员渲染成员管理动作", () => {
    const onClick = vi.fn();
    const { rerender } = render(<MemberManagementButton allowed={false} onClick={onClick} />);
    expect(screen.queryByRole("button", { name: "添加协作成员" })).toBeNull();

    rerender(<MemberManagementButton allowed onClick={onClick} />);
    fireEvent.click(screen.getByRole("button", { name: "添加协作成员" }));
    expect(onClick).toHaveBeenCalledOnce();
  });

  it("成员变更使用 PATCH 和 revision，而不是通用 PUT", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ revision: 4 }), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    }));
    vi.stubGlobal("fetch", fetchMock);

    await patchJson("/api/v1/research-projects/project/members", {
      revision: 3,
      memberUserIds: ["owner", "member"],
    });

    expect(fetchMock).toHaveBeenCalledWith("/api/v1/research-projects/project/members", expect.objectContaining({
      method: "PATCH",
      body: JSON.stringify({ revision: 3, memberUserIds: ["owner", "member"] }),
    }));
  });
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
