// 验证功能搜索的键盘导航、输入法边界与角色可见范围。
import React from "react";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, useLocation } from "react-router";
import App, { sectionsForIdentity } from "../src/App";

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  localStorage.clear();
});

function CurrentPath() {
  return <output aria-label="当前位置">{useLocation().pathname}</output>;
}

async function openSearch(roles = ["process.engineer"]) {
  vi.stubGlobal("fetch", vi.fn().mockImplementation(() => Promise.resolve(new Response("[]", {
    headers: { "Content-Type": "application/json" },
  }))));
  render(<MemoryRouter initialEntries={["/workbench"]}><App identity={{ roles }} logout={vi.fn()} /><CurrentPath /></MemoryRouter>);
  fireEvent.click(screen.getByRole("button", { name: "打开功能搜索" }));
  const input = await screen.findByPlaceholderText("例如：采集配置、配方版本、运行对比、检验任务");
  await waitFor(() => expect(input).toHaveFocus());
  return input;
}

describe("功能搜索", () => {
  it("参数配置统一使用配方版本入口，不展示重复的研发变量入口", () => {
    for (const roles of [["quality.inspector"], ["process.engineer"], ["platform.admin"]]) {
      const sections = sectionsForIdentity({ roles });
      expect(sections.some(section => section.id === "research")).toBe(false);
      expect(sections.flatMap(section => section.groups.flatMap(group => group.items))
        .some(([path]) => path === "/configuration/process-specifications")).toBe(true);
    }
  });

  it("旧的研发项目地址不再进入产品页面", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response("[]", { headers: { "Content-Type": "application/json" } })));
    render(<MemoryRouter initialEntries={["/research-projects"]}><App identity={{ roles: ["process.engineer"] }} logout={vi.fn()} /><CurrentPath /></MemoryRouter>);
    expect(await screen.findByRole("heading", { name: "页面不存在" })).toBeInTheDocument();
    expect(screen.getByLabelText("当前位置")).toHaveTextContent("/research-projects");
    expect(fetch.mock.calls.some(([url]) => String(url).includes("/api/v1/research-projects"))).toBe(false);
  });

  it.each([
    [["process.engineer"], "/workbench"],
    [["quality.inspector"], "/workbench"],
  ])("首页按岗位进入可访问的工作区", async (roles, expectedPath) => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response("[]", {
      headers: { "Content-Type": "application/json" },
    })));
    render(<MemoryRouter initialEntries={["/"]}><App identity={{ roles }} logout={vi.fn()} /><CurrentPath /></MemoryRouter>);
    await waitFor(() => expect(screen.getByLabelText("当前位置")).toHaveTextContent(expectedPath));
  });

  it("上下键循环选择可见结果，Enter 打开选中的功能", async () => {
    const input = await openSearch();
    const dialog = screen.getByRole("dialog");
    const options = within(dialog).getAllByRole("option");
    expect(input).toHaveAttribute("role", "combobox");
    expect(options[0]).toHaveAttribute("aria-selected", "true");
    fireEvent.keyDown(input, { key: "ArrowUp" });
    expect(options.at(-1)).toHaveAttribute("aria-selected", "true");
    fireEvent.keyDown(input, { key: "ArrowDown" });
    fireEvent.keyDown(input, { key: "ArrowDown" });
    expect(options[1]).toHaveAttribute("aria-selected", "true");
    expect(input).toHaveAttribute("aria-activedescendant", options[1].id);
    expect(input).toHaveFocus();
    fireEvent.keyDown(input, { key: "Enter" });
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() => expect(screen.getByLabelText("当前位置")).toHaveTextContent("/edges"));
  });

  it("筛选后重置选项，空结果不会跳转，重新打开清除查询", async () => {
    const input = await openSearch();
    fireEvent.keyDown(input, { key: "ArrowUp" });
    fireEvent.change(input, { target: { value: "采集配置" } });
    expect(screen.getByRole("option")).toHaveAttribute("aria-selected", "true");
    fireEvent.change(input, { target: { value: "不存在的功能xyz" } });
    expect(screen.queryByRole("option")).toBeNull();
    expect(input).not.toHaveAttribute("aria-activedescendant");
    fireEvent.keyDown(input, { key: "ArrowDown" });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(screen.getByLabelText("当前位置")).toHaveTextContent("/workbench");
    fireEvent.keyDown(input, { key: "Escape" });
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    fireEvent.click(screen.getByRole("button", { name: "打开功能搜索" }));
    expect(await screen.findByRole("combobox")).toHaveValue("");
  });

  it("中文组合输入期间 Enter 不跳转，确认完成后可正常打开", async () => {
    const input = await openSearch();
    fireEvent.change(input, { target: { value: "采集配置" } });
    fireEvent.keyDown(input, { key: "Enter", isComposing: true });
    fireEvent.keyDown(input, { key: "Enter", keyCode: 229 });
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByLabelText("当前位置")).toHaveTextContent("/workbench");
    fireEvent.keyDown(input, { key: "Enter" });
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() => expect(screen.getByLabelText("当前位置")).toHaveTextContent("/configuration/ingestion-tasks"));
  });

  it("普通工程师不能搜索管理员入口，管理员可点击打开", async () => {
    let input = await openSearch();
    fireEvent.change(input, { target: { value: "用户权限" } });
    expect(screen.queryByRole("option")).toBeNull();
    cleanup();
    input = await openSearch(["platform.admin"]);
    fireEvent.change(input, { target: { value: "用户权限" } });
    fireEvent.click(screen.getByRole("option"));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() => expect(screen.getByLabelText("当前位置")).toHaveTextContent("/identity/users"));
  });
});
