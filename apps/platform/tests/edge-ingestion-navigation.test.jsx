// Verify Edge diagnostics carry the selected site and node into acquisition task navigation.

import React from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import { IngestionTaskPage, IngestionTasksPage } from "../src/acquisition/IngestionTaskPage";
import { EdgeDetailPage } from "../src/pages/EdgePages";

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

function jsonResponse(payload) {
  return new Response(JSON.stringify(payload), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}

function LocationOutput() {
  const location = useLocation();
  return <output data-testid="current-location">{`${location.pathname}${location.search}`}</output>;
}

function stubEdgeDetailApi(role) {
  const requests = [];
  vi.stubGlobal("fetch", vi.fn(url => {
    const requestUrl = new URL(String(url), "http://localhost");
    requests.push(requestUrl);
    if (requestUrl.pathname === "/api/v1/auth/me")
      return Promise.resolve(jsonResponse({ roles: [role], siteIds: ["SITE-A"] }));
    if (requestUrl.pathname === "/api/edges")
      return Promise.resolve(jsonResponse([{ edgeId: "EDGE-A", siteId: "SITE-A", hostname: "节点 A" }]));
    if (requestUrl.pathname.endsWith("/acquisition/status"))
      return Promise.resolve(jsonResponse({ tasks: [], deployments: [] }));
    return Promise.resolve(jsonResponse({ data: [], total: 0 }));
  }));
  return requests;
}

function renderEdgeDetailPage() {
  render(<MemoryRouter initialEntries={["/edges/EDGE-A?siteId=SITE-A"]}>
    <Routes><Route path="/edges/:edgeId" element={<EdgeDetailPage />} /></Routes>
  </MemoryRouter>);
}

describe("Edge 诊断到采集配置导航", () => {
  it("业务角色只请求操作日志，不能打开系统日志", async () => {
    const requests = stubEdgeDetailApi("process.engineer");
    renderEdgeDetailPage();

    expect(await screen.findByRole("heading", { name: "节点操作日志" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "展开系统日志" })).not.toBeInTheDocument();
    await waitFor(() => expect(requests.some(request =>
      request.pathname === "/api/edges/EDGE-A/logs" && request.searchParams.get("audience") === "operator",
    )).toBe(true));
  });

  it("平台管理员可以打开系统日志", async () => {
    const requests = stubEdgeDetailApi("platform.admin");
    renderEdgeDetailPage();

    fireEvent.click(await screen.findByRole("button", { name: "展开系统日志" }));
    expect(await screen.findByRole("heading", { name: "节点系统日志" })).toBeInTheDocument();
    await waitFor(() => expect(requests.some(request =>
      request.pathname === "/api/edges/EDGE-A/logs" && !request.searchParams.has("audience"),
    )).toBe(true));
  });

  it.each(["EDGE-A", "EDGE-B"])("从 %s 保留站点和节点筛选", async edgeId => {
    vi.stubGlobal("fetch", vi.fn(url => {
      const requestUrl = new URL(String(url), "http://localhost");
      if (requestUrl.pathname === "/api/v1/auth/me")
        return Promise.resolve(jsonResponse({ roles: ["process.engineer"], siteIds: ["SITE-A", "SITE-B"] }));
      if (requestUrl.pathname === "/api/edges" && requestUrl.searchParams.get("siteId") === "SITE-A")
        return Promise.resolve(jsonResponse([
          { edgeId: "EDGE-A", siteId: "SITE-A", hostname: "节点 A" },
          { edgeId: "EDGE-B", siteId: "SITE-A", hostname: "节点 B" },
        ]));
      if (requestUrl.pathname === "/api/edges") return Promise.resolve(jsonResponse([]));
      if (requestUrl.pathname.endsWith("/acquisition/status"))
        return Promise.resolve(jsonResponse({ tasks: [], deployments: [] }));
      return Promise.resolve(jsonResponse({ data: [] }));
    }));

    render(<MemoryRouter initialEntries={[`/edges/${edgeId}?siteId=SITE-A`]}>
      <Routes><Route path="/edges/:edgeId" element={<EdgeDetailPage />} /></Routes>
    </MemoryRouter>);

    expect(await screen.findByRole("link", { name: "配置采集" }))
      .toHaveAttribute("href", `/configuration/ingestion-tasks?siteId=SITE-A&edgeId=${edgeId}`);
    expect(screen.getByRole("link", { name: "查看采集配置" }))
      .toHaveAttribute("href", `/configuration/ingestion-tasks?siteId=SITE-A&edgeId=${edgeId}`);
  });

  it("从 Edge 诊断进入筛选列表、新建配置并返回时保留节点", async () => {
    const tasks = [
      { taskId: "task-a", version: 1, name: "任务 A", status: "draft", edgeId: "EDGE-A", protocol: "http-polling", subjectId: "PRESS-A", valueMappings: [] },
      { taskId: "task-b", version: 1, name: "任务 B", status: "draft", edgeId: "EDGE-B", protocol: "http-polling", subjectId: "PRESS-B", valueMappings: [] },
      { taskId: "task-c", version: 1, name: "任务 C", status: "draft", edgeId: "EDGE-C", protocol: "http-polling", subjectId: "PRESS-C", valueMappings: [] },
    ];
    vi.stubGlobal("fetch", vi.fn(url => {
      const requestUrl = new URL(String(url), "http://localhost");
      if (requestUrl.pathname === "/api/v1/auth/me")
        return Promise.resolve(jsonResponse({ roles: ["process.engineer"], siteIds: ["SITE-A", "SITE-B"] }));
      if (requestUrl.pathname === "/api/edges" && requestUrl.searchParams.get("siteId") === "SITE-A")
        return Promise.resolve(jsonResponse([
          { edgeId: "EDGE-A", siteId: "SITE-A", hostname: "节点 A" },
          { edgeId: "EDGE-B", siteId: "SITE-A", hostname: "节点 B" },
        ]));
      if (requestUrl.pathname === "/api/edges")
        return Promise.resolve(jsonResponse([
          { edgeId: "EDGE-A", siteId: "SITE-A", hostname: "节点 A" },
          { edgeId: "EDGE-B", siteId: "SITE-A", hostname: "节点 B" },
          { edgeId: "EDGE-C", siteId: "SITE-B", hostname: "节点 C" },
        ]));
      if (requestUrl.pathname === "/api/v1/ingestion-tasks") return Promise.resolve(jsonResponse(tasks));
      if (requestUrl.pathname === "/api/v1/acquisition-protocols") return Promise.resolve(jsonResponse({ protocols: [] }));
      if (requestUrl.pathname.endsWith("/acquisition/status")) return Promise.resolve(jsonResponse({ tasks: [], deployments: [] }));
      return Promise.resolve(jsonResponse({ data: [] }));
    }));

    render(<MemoryRouter initialEntries={["/edges/EDGE-A?siteId=SITE-A"]}>
      <LocationOutput />
      <Routes>
        <Route path="/edges/:edgeId" element={<EdgeDetailPage />} />
        <Route path="/configuration/ingestion-tasks" element={<IngestionTasksPage />} />
        <Route path="/configuration/ingestion-tasks/new" element={<IngestionTaskPage />} />
        <Route path="/configuration/ingestion-tasks/:taskId" element={<IngestionTaskPage />} />
      </Routes>
    </MemoryRouter>);

    fireEvent.click(await screen.findByRole("link", { name: "配置采集" }));
    expect(await screen.findByText("已筛选现场节点 EDGE-A")).toBeInTheDocument();
    const taskTable = screen.getByRole("table");
    expect(within(taskTable).getByText("任务 A")).toBeInTheDocument();
    expect(within(taskTable).queryByText("任务 B")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("link", { name: "新建采集配置" }));
    await waitFor(() => expect(screen.getByTestId("current-location")).toHaveTextContent(
      "/configuration/ingestion-tasks/new?siteId=SITE-A&edgeId=EDGE-A&returnTo=%2Fconfiguration%2Fingestion-tasks%3FsiteId%3DSITE-A%26edgeId%3DEDGE-A",
    ));
    expect(await screen.findByRole("region", { name: "采集任务节点工作区" })).toBeInTheDocument();
    expect(screen.getByText("运行于 Edge").parentElement).toHaveTextContent("节点 A");

    fireEvent.click(screen.getByRole("link", { name: "返回列表" }));
    await waitFor(() => expect(screen.getByTestId("current-location")).toHaveTextContent(
      "/configuration/ingestion-tasks?siteId=SITE-A&edgeId=EDGE-A",
    ));
    expect(await screen.findByText("已筛选现场节点 EDGE-A")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "返回节点诊断" })).toHaveAttribute(
      "href", "/edges/EDGE-A?siteId=SITE-A",
    );

    fireEvent.click(screen.getByRole("button", { name: "查看" }));
    await waitFor(() => expect(screen.getByTestId("current-location")).toHaveTextContent(
      "/configuration/ingestion-tasks/task-a?version=1&siteId=SITE-A&edgeId=EDGE-A&returnTo=%2Fconfiguration%2Fingestion-tasks%3FsiteId%3DSITE-A%26edgeId%3DEDGE-A",
    ));
    fireEvent.click(await screen.findByRole("link", { name: "返回列表" }));
    await waitFor(() => expect(screen.getByTestId("current-location")).toHaveTextContent(
      "/configuration/ingestion-tasks?siteId=SITE-A&edgeId=EDGE-A",
    ));

    fireEvent.click(screen.getByRole("button", { name: "新版本" }));
    await waitFor(() => expect(screen.getByTestId("current-location")).toHaveTextContent(
      "/configuration/ingestion-tasks/task-a?version=1&mode=version&siteId=SITE-A&edgeId=EDGE-A&returnTo=%2Fconfiguration%2Fingestion-tasks%3FsiteId%3DSITE-A%26edgeId%3DEDGE-A",
    ));
    fireEvent.click(await screen.findByRole("link", { name: "返回列表" }));
    await waitFor(() => expect(screen.getByTestId("current-location")).toHaveTextContent(
      "/configuration/ingestion-tasks?siteId=SITE-A&edgeId=EDGE-A",
    ));

    fireEvent.click(screen.getByRole("button", { name: "显示本站全部节点" }));
    expect(await screen.findByText("已筛选站点 SITE-A")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "查看本站节点" })).toHaveAttribute("href", "/edges?siteId=SITE-A");
    await waitFor(() => expect(screen.getByTestId("current-location")).toHaveTextContent(
      "/configuration/ingestion-tasks?siteId=SITE-A",
    ));
    expect(await within(screen.getByRole("table")).findByText("任务 B")).toBeInTheDocument();
    expect(within(screen.getByRole("table")).queryByText("任务 C")).not.toBeInTheDocument();
    const taskBRow = within(screen.getByRole("table")).getByText("任务 B").closest("tr");
    fireEvent.click(within(taskBRow).getByRole("button", { name: "查看" }));
    await waitFor(() => expect(screen.getByTestId("current-location")).toHaveTextContent(
      "/configuration/ingestion-tasks/task-b?version=1&siteId=SITE-A&returnTo=%2Fconfiguration%2Fingestion-tasks%3FsiteId%3DSITE-A",
    ));
    fireEvent.click(await screen.findByRole("link", { name: "返回列表" }));
    await waitFor(() => expect(screen.getByTestId("current-location")).toHaveTextContent(
      "/configuration/ingestion-tasks?siteId=SITE-A",
    ));

    fireEvent.click(screen.getByRole("button", { name: "显示全部站点" }));
    await waitFor(() => expect(screen.getByTestId("current-location")).toHaveTextContent("/configuration/ingestion-tasks"));
    expect(screen.getByRole("link", { name: "查看现场节点" })).toHaveAttribute("href", "/edges");
    expect(await within(screen.getByRole("table")).findByText("任务 C")).toBeInTheDocument();
    const taskCRow = within(screen.getByRole("table")).getByText("任务 C").closest("tr");
    fireEvent.click(within(taskCRow).getByRole("button", { name: "查看" }));
    await waitFor(() => expect(screen.getByTestId("current-location")).toHaveTextContent(
      "/configuration/ingestion-tasks/task-c?version=1&returnTo=%2Fconfiguration%2Fingestion-tasks",
    ));
    fireEvent.click(await screen.findByRole("link", { name: "返回列表" }));
    await waitFor(() => expect(screen.getByTestId("current-location")).toHaveTextContent("/configuration/ingestion-tasks"));
  });

  it("新建时改选其他站点的 Edge 后，返回筛选与当前选择一致", async () => {
    const confirm = vi.fn(() => true);
    vi.stubGlobal("confirm", confirm);
    vi.stubGlobal("fetch", vi.fn(url => {
      const requestUrl = new URL(String(url), "http://localhost");
      if (requestUrl.pathname === "/api/v1/auth/me")
        return Promise.resolve(jsonResponse({ roles: ["process.engineer"], siteIds: ["SITE-A", "SITE-B"] }));
      if (requestUrl.pathname === "/api/edges") return Promise.resolve(jsonResponse([
        { edgeId: "EDGE-A", siteId: "SITE-A", hostname: "节点 A" },
        { edgeId: "EDGE-C", siteId: "SITE-B", hostname: "节点 C" },
      ].filter(edge => !requestUrl.searchParams.get("siteId") || edge.siteId === requestUrl.searchParams.get("siteId"))));
      if (requestUrl.pathname === "/api/v1/process-data-models")
        return Promise.resolve(jsonResponse({ data: [{
          modelId: "MODEL-1",
          version: 1,
          name: "测试工艺变量",
          status: "published",
          acquisition: { dataItems: [{ code: "temperature", dataType: "double", nullable: false }] },
          controlParameters: [],
        }] }));
      if (requestUrl.pathname === "/api/v1/acquisition-protocols")
        return Promise.resolve(jsonResponse({ protocols: [] }));
      if (requestUrl.pathname === "/api/v1/ingestion-tasks")
        return Promise.resolve(jsonResponse({ data: [] }));
      return Promise.resolve(jsonResponse({ data: [] }));
    }));

    render(<MemoryRouter initialEntries={["/configuration/ingestion-tasks/new?siteId=SITE-A&edgeId=EDGE-A"]}>
      <LocationOutput />
      <Routes>
        <Route path="/configuration/ingestion-tasks/new" element={<IngestionTaskPage />} />
      </Routes>
    </MemoryRouter>);

    const workbench = await screen.findByRole("region", { name: "采集任务节点工作区" });
    const siteSelect = await screen.findByLabelText("可选现场节点所属站点");
    await waitFor(() => expect(siteSelect).toBeEnabled());
    fireEvent.change(siteSelect, { target: { value: "SITE-B" } });
    const edgePalette = within(workbench).getByRole("region", { name: "现场 Edge" });
    fireEvent.click(await within(edgePalette).findByRole("button", { name: /节点 C/ }));
    fireEvent.click(within(workbench).getByRole("button", { name: /分配给当前任务/ }));
    const backLink = screen.getByRole("link", { name: "返回列表" });
    expect(backLink).toHaveAttribute("href", "/configuration/ingestion-tasks?siteId=SITE-B&edgeId=EDGE-C");
    fireEvent.click(backLink);
    expect(confirm).toHaveBeenCalledTimes(1);

    await waitFor(() => expect(screen.getByTestId("current-location")).toHaveTextContent(
      "/configuration/ingestion-tasks?siteId=SITE-B&edgeId=EDGE-C",
    ));
  });

  it("多站点用户打开未限定的新建页时先选站点，再按站点读取 Edge", async () => {
    const requests = [];
    vi.stubGlobal("fetch", vi.fn(url => {
      const requestUrl = new URL(String(url), "http://localhost");
      requests.push(`${requestUrl.pathname}${requestUrl.search}`);
      if (requestUrl.pathname === "/api/v1/auth/me")
        return Promise.resolve(jsonResponse({ roles: ["process.engineer"], siteIds: ["SITE-A", "SITE-B"] }));
      if (requestUrl.pathname === "/api/edges") {
        if (!requestUrl.searchParams.has("siteId"))
          return Promise.resolve(new Response("必须指定 siteId", { status: 400 }));
        return Promise.resolve(jsonResponse([
          { edgeId: "EDGE-A", siteId: "SITE-A", hostname: "节点 A" },
          { edgeId: "EDGE-B", siteId: "SITE-B", hostname: "节点 B" },
        ].filter(edge => edge.siteId === requestUrl.searchParams.get("siteId"))));
      }
      return Promise.resolve(jsonResponse({ data: [] }));
    }));

    render(<MemoryRouter initialEntries={["/configuration/ingestion-tasks/new"]}>
      <Routes><Route path="/configuration/ingestion-tasks/new" element={<IngestionTaskPage />} /></Routes>
    </MemoryRouter>);

    const siteSelect = await screen.findByLabelText("可选现场节点所属站点");
    expect(screen.getByText("当前账号可访问多个站点。请选择现场节点所属站点后，才能加载可分配的 Edge。"))
      .toBeInTheDocument();
    expect(requests).not.toContain("/api/edges");

    fireEvent.change(siteSelect, { target: { value: "SITE-B" } });
    await waitFor(() => expect(requests).toContain("/api/edges?siteId=SITE-B"));
    expect(await screen.findByRole("button", { name: /节点 B/ })).toBeInTheDocument();
    expect(requests).not.toContain("/api/edges");
  });
});
