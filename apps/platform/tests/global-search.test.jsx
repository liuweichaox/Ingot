// 验证功能搜索的键盘导航、输入法边界与角色可见范围。
import React from "react";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
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

function CurrentUrl() {
  const location = useLocation();
  return <output aria-label="当前位置">{`${location.pathname}${location.search}`}</output>;
}

async function openSearch(roles = ["process.engineer"]) {
  vi.stubGlobal("fetch", vi.fn().mockImplementation(() => Promise.resolve(new Response("[]", {
    headers: { "Content-Type": "application/json" },
  }))));
  render(<MemoryRouter initialEntries={["/workbench"]}><App identity={{ roles }} logout={vi.fn()} /><CurrentPath /></MemoryRouter>);
  fireEvent.click(screen.getByRole("button", { name: "打开功能搜索" }));
  const input = await screen.findByPlaceholderText("例如：现场节点、配方版本、运行对比、检验任务");
  await waitFor(() => expect(input).toHaveFocus());
  return input;
}

describe("功能搜索", () => {
  it("按产品工作流组织导航，并保留管理员专属系统入口", () => {
    const sections = sectionsForIdentity({ roles: ["process.engineer"] });
    expect(sections.map(({ id, label, path }) => [id, label, path])).toEqual([
      ["overview", "工作台", "/workbench"],
      ["investigation", "AI 助手", "/chat"],
      ["evidence", "现场运行", "/process-executions"],
      ["insight", "工艺洞察", "/comparisons"],
      ["optimization", "配方优化", "/configuration/process-specifications"],
      ["configuration", "配置与接入", "/configuration/process-data-models"],
    ]);
    expect(sections.find(section => section.id === "evidence")?.groups).toEqual([
      { items: [["/process-executions", "运行记录"], ["/edges", "现场节点"], ["/production/changeover", "生产切换"], ["/production/tooling-installations", "工装装卸"], ["/inspections", "检验任务"], ["/quality-analysis", "质量偏差"], ["/data-quality", "数据质量"]] },
    ]);
    expect(sections.find(section => section.id === "insight")?.groups).toEqual([
      { items: [["/comparisons", "工艺分析"], ["/knowledge/process-materials", "工艺资料"], ["/knowledge/mechanism", "机理知识"]] },
    ]);
    expect(sections.find(section => section.id === "configuration")?.groups.map(group => [group.heading, group.items])).toEqual([
      ["工艺与质量", [["/configuration/process-data-models", "工艺变量"], ["/configuration/process-analysis-plans", "分析规则"], ["/configuration/inspection-definitions", "检测定义"], ["/configuration/quality-plans", "质量方案"]]],
      ["生产资源", [["/configuration/component-types", "组件分类"], ["/configuration/components", "组件台账"], ["/configuration/tooling-types", "工装结构"], ["/configuration/tooling-assemblies", "工装总成"]]],
    ]);
    expect(sections.flatMap(section => section.groups.flatMap(group => group.items.filter(([path]) => path === "/edges")))
      .map(([, label]) => label)).toEqual(["现场节点"]);
    expect(sections.some(section => section.id === "system")).toBe(false);
    expect(sectionsForIdentity({ roles: ["platform.admin"] }).at(-1)?.id).toBe("system");
  });

  it("把现场节点列入现场运行，并在功能搜索中保留当前站点", async () => {
    vi.stubGlobal("fetch", vi.fn(async input => {
      const url = new URL(String(input), "http://localhost");
      const value = url.pathname === "/api/v1/auth/me"
        ? { roles: ["process.engineer"], siteIds: ["SITE-A"] }
        : url.pathname === "/api/edges"
          ? [{ edgeId: "EDGE-A", siteId: "SITE-A", hostname: "节点 A" }]
          : [];
      return new Response(JSON.stringify(value), { headers: { "Content-Type": "application/json" } });
    }));
    render(<MemoryRouter initialEntries={["/workbench?siteId=SITE-A"]}>
      <App identity={{ roles: ["process.engineer"], siteIds: ["SITE-A"] }} logout={vi.fn()} />
      <CurrentUrl />
    </MemoryRouter>);

    fireEvent.click(screen.getByRole("button", { name: "打开功能搜索" }));
    const input = await screen.findByRole("combobox");
    fireEvent.change(input, { target: { value: "现场节点" } });
    const option = screen.getAllByRole("option").find(item => within(item).queryByText("现场节点", { exact: true }));
    expect(option).toBeDefined();
    expect(within(option).getByText("现场运行")).toBeInTheDocument();
    fireEvent.click(option);

    await waitFor(() => expect(screen.getByLabelText("当前位置")).toHaveTextContent("/edges?siteId=SITE-A"));
    expect(screen.getByRole("main", { name: "现场节点" })).toHaveFocus();
    expect(await screen.findByRole("heading", { name: "现场节点" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "现场节点" })).toHaveAttribute("aria-current", "page");

    fireEvent.click(await screen.findByRole("link", { name: "查看诊断" }));
    await waitFor(() => expect(screen.getByLabelText("当前位置")).toHaveTextContent("/edges/EDGE-A?siteId=SITE-A"));
    expect(screen.getByRole("link", { name: "现场节点" })).toHaveAttribute("aria-current", "page");
    expect(within(screen.getByRole("navigation", { name: "面包屑" })).getByRole("link", { name: "现场运行" }))
      .toHaveAttribute("href", "/process-executions?siteId=SITE-A");
  });

  it("收起侧栏后仍可用子菜单进入二级页面并看到当前项", async () => {
    const user = userEvent.setup();
    vi.stubGlobal("fetch", vi.fn(async input => {
      const url = new URL(String(input), "http://localhost");
      const value = url.pathname === "/api/v1/auth/me"
        ? { roles: ["process.engineer"], siteIds: ["SITE-A"] }
        : url.pathname === "/api/edges"
          ? [{ edgeId: "EDGE-A", siteId: "SITE-A", hostname: "节点 A" }]
          : [];
      return new Response(JSON.stringify(value), { headers: { "Content-Type": "application/json" } });
    }));
    render(<MemoryRouter initialEntries={["/workbench?siteId=SITE-A"]}>
      <App identity={{ roles: ["process.engineer"], siteIds: ["SITE-A"] }} logout={vi.fn()} />
      <CurrentUrl />
    </MemoryRouter>);

    await user.click(screen.getByRole("button", { name: "收起侧边栏" }));
    expect(screen.getByRole("link", { name: "AI 助手" })).toHaveAttribute(
      "href", "/chat?contextKind=site&contextId=SITE-A&contextSiteId=SITE-A",
    );
    const sectionButton = screen.getByRole("button", { name: "现场运行菜单" });
    expect(sectionButton).toHaveAttribute("aria-expanded", "false");
    sectionButton.focus();
    await user.keyboard("{Enter}");
    let submenu = await screen.findByRole("navigation", { name: "现场运行导航" });
    await user.tab();
    expect(within(submenu).getByRole("link", { name: "运行记录" })).toHaveFocus();
    await user.keyboard("{Escape}");
    await waitFor(() => expect(sectionButton).toHaveAttribute("aria-expanded", "false"));
    expect(sectionButton).toHaveFocus();

    await user.click(sectionButton);
    submenu = await screen.findByRole("navigation", { name: "现场运行导航" });
    const edgeLink = within(submenu).getByRole("link", { name: "现场节点" });
    expect(edgeLink).toHaveAttribute("href", "/edges?siteId=SITE-A");
    await user.click(edgeLink);

    await waitFor(() => expect(screen.getByLabelText("当前位置")).toHaveTextContent("/edges?siteId=SITE-A"));
    expect(screen.getByRole("main", { name: "现场节点" })).toHaveFocus();
    await waitFor(() => expect(sectionButton).toHaveAttribute("aria-expanded", "false"));
    await user.click(sectionButton);
    submenu = await screen.findByRole("navigation", { name: "现场运行导航" });
    expect(within(submenu).getByRole("link", { name: "现场节点" })).toHaveAttribute("aria-current", "page");
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
    await waitFor(() => expect(screen.getByLabelText("当前位置")).toHaveTextContent("/chat"));
  });

  it("筛选后重置选项，空结果不会跳转，重新打开清除查询", async () => {
    const input = await openSearch();
    fireEvent.keyDown(input, { key: "ArrowUp" });
    fireEvent.change(input, { target: { value: "工艺变量" } });
    const [firstOption] = screen.getAllByRole("option");
    expect(firstOption).toHaveTextContent("工艺变量");
    expect(firstOption).toHaveAttribute("aria-selected", "true");
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
    fireEvent.change(input, { target: { value: "工艺变量" } });
    fireEvent.keyDown(input, { key: "Enter", isComposing: true });
    fireEvent.keyDown(input, { key: "Enter", keyCode: 229 });
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByLabelText("当前位置")).toHaveTextContent("/workbench");
    fireEvent.keyDown(input, { key: "Enter" });
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() => expect(screen.getByLabelText("当前位置")).toHaveTextContent("/configuration/process-data-models"));
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

  it("按产品名称和证据核对搜索 AI 助手与配方优化入口", async () => {
    let input = await openSearch();
    fireEvent.change(input, { target: { value: "AI 助手" } });
    expect(screen.getByRole("option")).toHaveTextContent("AI 助手");
    fireEvent.keyDown(input, { key: "Enter" });
    await waitFor(() => expect(screen.getByLabelText("当前位置")).toHaveTextContent("/chat"));

    fireEvent.click(screen.getByRole("button", { name: "打开功能搜索" }));
    input = await screen.findByRole("combobox");
    fireEvent.change(input, { target: { value: "证据核对" } });
    expect(screen.getByRole("option")).toHaveTextContent("AI 助手");
    fireEvent.change(input, { target: { value: "配方优化" } });
    const recipeOptions = screen.getAllByRole("option").map(option => option.textContent);
    expect(recipeOptions.some(text => text.includes("配方版本"))).toBe(true);
    expect(recipeOptions.some(text => text.includes("调整与验证"))).toBe(true);
    fireEvent.keyDown(input, { key: "Enter" });
    await waitFor(() => expect(screen.getByLabelText("当前位置")).toHaveTextContent("/configuration/process-specifications"));
  });

  it("保留工装建模前置配置和数据质量的可发现入口", async () => {
    let input = await openSearch();
    fireEvent.change(input, { target: { value: "工装结构" } });
    const toolingTypeOption = screen.getAllByRole("option").find(option => option.textContent.includes("工装结构"));
    expect(toolingTypeOption).toBeDefined();
    fireEvent.click(toolingTypeOption);
    await waitFor(() => expect(screen.getByLabelText("当前位置")).toHaveTextContent("/configuration/tooling-types"));

    fireEvent.click(screen.getByRole("button", { name: "打开功能搜索" }));
    input = await screen.findByRole("combobox");
    fireEvent.change(input, { target: { value: "数据质量" } });
    const dataQualityOption = screen.getAllByRole("option").find(option => option.textContent.includes("数据质量"));
    expect(dataQualityOption).toBeDefined();
    fireEvent.click(dataQualityOption);
    await waitFor(() => expect(screen.getByLabelText("当前位置")).toHaveTextContent("/data-quality"));
  });
});
