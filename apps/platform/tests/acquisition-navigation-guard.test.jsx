// Verifies unsaved multi-device acquisition drafts block every router transition.
import React, { useEffect, useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter, Route, Routes, RouterProvider, useLocation, useNavigate } from "react-router";
import { IngestionTaskPage } from "../src/acquisition/IngestionTaskPage";
import { UnsavedNavigationGuard, useUnsavedNavigationRegistration } from "../src/acquisition/UnsavedNavigationGuard";

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

function jsonResponse(payload) {
  return new Response(JSON.stringify(payload), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}

function taskEditorFetch(task, { saveStatus = 200, savePayload = task } = {}) {
  return vi.fn((url, options = {}) => {
    const requestUrl = new URL(String(url), "http://localhost");
    if (requestUrl.pathname === "/api/v1/auth/me")
      return Promise.resolve(jsonResponse({ roles: ["process.engineer"], siteIds: ["SITE-001"] }));
    if (requestUrl.pathname === "/api/edges")
      return Promise.resolve(jsonResponse({ data: [{ edgeId: "EDGE-1", siteId: "SITE-001", hostname: "现场节点一" }] }));
    if (requestUrl.pathname === "/api/v1/acquisition-protocols")
      return Promise.resolve(jsonResponse({ protocols: [] }));
    if (requestUrl.pathname === "/api/v1/process-data-models")
      return Promise.resolve(jsonResponse({ data: [{
        modelId: "MODEL-1", version: 1, name: "工艺变量",
        status: "published", acquisition: { dataItems: [{ code: "temperature", unit: "°C", nullable: false }] },
        controlParameters: [],
      }] }));
    if (requestUrl.pathname === "/api/v1/ingestion-tasks" && options.method === "POST")
      return Promise.resolve(new Response(JSON.stringify(savePayload), {
        status: saveStatus,
        headers: { "Content-Type": "application/json" },
      }));
    if (requestUrl.pathname === "/api/v1/ingestion-tasks")
      return Promise.resolve(jsonResponse({ data: task ? [task] : [] }));
    return Promise.resolve(jsonResponse({ data: [] }));
  });
}

function CurrentLocation() {
  const location = useLocation();
  return <output data-testid="current-location">{`${location.pathname}${location.search}`}</output>;
}

function DirtyPage() {
  const setDirty = useUnsavedNavigationRegistration();
  const navigate = useNavigate();
  const [registered, setRegistered] = useState(false);
  useEffect(() => {
    setDirty(true);
    setRegistered(true);
    return () => setDirty(false);
  }, [setDirty]);
  return <main>
    <p>{registered ? "更改已登记" : "正在登记"}</p>
    <button onClick={() => navigate("/outside")}>程序导航离开</button>
    <button onClick={() => { setDirty(false); navigate("/outside"); }}>保存后返回</button>
  </main>;
}

function GuardedTestRoutes({ editor }) {
  return <UnsavedNavigationGuard>
    <CurrentLocation />
    <Routes>
      <Route path="/editor" element={editor || <DirtyPage />} />
      <Route path="/outside" element={<h1>已离开编辑器</h1>} />
    </Routes>
  </UnsavedNavigationGuard>;
}

function renderGuardedRoutes(initialEntries, initialIndex, editor) {
  const router = createMemoryRouter([
    { path: "*", element: <GuardedTestRoutes editor={editor} /> },
  ], { initialEntries, initialIndex });
  render(<RouterProvider router={router} />);
  return router;
}

function renderTaskEditor(initialEntries, initialIndex = initialEntries.length - 1, canWrite = true) {
  const router = createMemoryRouter([
    {
      path: "*",
      element: <UnsavedNavigationGuard>
        <CurrentLocation />
        <Routes>
          <Route path="/configuration/ingestion-tasks/:taskId" element={<IngestionTaskPage canWrite={canWrite} />} />
          <Route path="/configuration/ingestion-tasks" element={<h1>采集任务列表</h1>} />
          <Route path="/outside" element={<h1>已离开编辑器</h1>} />
        </Routes>
      </UnsavedNavigationGuard>,
    },
  ], { initialEntries, initialIndex });
  render(<RouterProvider router={router} />);
  return router;
}

function editableTask() {
  return {
    taskId: "task-1", version: 1, name: "采集任务一", status: "draft", edgeId: "EDGE-1",
    protocol: "http-polling", dataModelId: "MODEL-1", dataModelVersion: 1,
    source: "equipment/source-1", subjectType: "equipment", subjectId: "PRESS-01",
    httpPolling: {
      baseUrl: "http://device.local", snapshotPath: "/snapshot", pollIntervalMs: 1000,
      method: "get", contentType: "application/json",
    },
    timestampMode: "edge-received",
    valueMappings: [{ dataItemCode: "temperature", sourcePath: "temperature", sourceUnit: "°C" }],
  };
}

describe("采集草稿离页保护", () => {
  it("后退时取消会留在当前页面，确认后才离开", async () => {
    const confirm = vi.fn(() => false);
    vi.stubGlobal("confirm", confirm);
    const router = renderGuardedRoutes(["/outside", "/editor"], 1);
    expect(await screen.findByText("更改已登记")).toBeInTheDocument();

    await act(async () => { await router.navigate(-1); });
    expect(confirm).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId("current-location")).toHaveTextContent("/editor");

    confirm.mockReturnValue(true);
    await act(async () => { await router.navigate(-1); });
    expect(confirm).toHaveBeenCalledTimes(2);
    expect(await screen.findByText("已离开编辑器")).toBeInTheDocument();
  });

  it("前进和按钮触发的程序导航也会经过同一确认", async () => {
    const confirm = vi.fn(() => false);
    vi.stubGlobal("confirm", confirm);
    const router = renderGuardedRoutes(["/editor", "/outside"], 0);
    expect(await screen.findByText("更改已登记")).toBeInTheDocument();

    await act(async () => { await router.navigate(1); });
    expect(confirm).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId("current-location")).toHaveTextContent("/editor");

    fireEvent.click(screen.getByRole("button", { name: "程序导航离开" }));
    expect(confirm).toHaveBeenCalledTimes(2);
    expect(screen.getByTestId("current-location")).toHaveTextContent("/editor");

    confirm.mockReturnValue(true);
    fireEvent.click(screen.getByRole("button", { name: "程序导航离开" }));
    expect(await screen.findByText("已离开编辑器")).toBeInTheDocument();
  });

  it("保存成功后同步清除脏状态再导航不会再次提示", async () => {
    const confirm = vi.fn(() => false);
    vi.stubGlobal("confirm", confirm);
    renderGuardedRoutes(["/editor"]);
    expect(await screen.findByText("更改已登记")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "保存后返回" }));
    expect(confirm).not.toHaveBeenCalled();
    expect(await screen.findByText("已离开编辑器")).toBeInTheDocument();
  });

  it("新建页尚未启用多设备画布时允许后退且不提示", async () => {
    const confirm = vi.fn(() => false);
    vi.stubGlobal("confirm", confirm);
    vi.stubGlobal("fetch", vi.fn(url => {
      const requestUrl = new URL(String(url), "http://localhost");
      if (requestUrl.pathname === "/api/v1/auth/me")
        return Promise.resolve(jsonResponse({ roles: ["process.engineer"], siteIds: ["SITE-001"] }));
      if (requestUrl.pathname === "/api/edges")
        return Promise.resolve(jsonResponse([{ edgeId: "EDGE-1", siteId: "SITE-001", hostname: "现场节点一" }]));
      if (requestUrl.pathname === "/api/v1/acquisition-protocols")
        return Promise.resolve(jsonResponse({ protocols: [] }));
      return Promise.resolve(jsonResponse({ data: [] }));
    }));
    const router = createMemoryRouter([
      {
        path: "*",
        element: <UnsavedNavigationGuard>
          <CurrentLocation />
          <Routes>
            <Route path="/configuration/ingestion-tasks/new" element={<IngestionTaskPage />} />
            <Route path="/outside" element={<h1>已离开编辑器</h1>} />
          </Routes>
        </UnsavedNavigationGuard>,
      },
    ], { initialEntries: ["/outside", "/configuration/ingestion-tasks/new"], initialIndex: 1 });
    render(<RouterProvider router={router} />);
    await screen.findByRole("region", { name: "采集任务节点工作区" });

    await act(async () => { await router.navigate(-1); });
    expect(confirm).not.toHaveBeenCalled();
    expect(await screen.findByText("已离开编辑器")).toBeInTheDocument();
  });

  it("单任务修改后取消离页保留内容，确认后才离开", async () => {
    const confirm = vi.fn(() => false);
    vi.stubGlobal("confirm", confirm);
    vi.stubGlobal("fetch", taskEditorFetch(editableTask()));
    const router = renderTaskEditor(["/outside", "/configuration/ingestion-tasks/task-1?version=1"], 1);

    const name = await screen.findByLabelText("配置名称");
    expect(name).toHaveValue("采集任务一");
    fireEvent.change(name, { target: { value: "采集任务已修改" } });
    const beforeUnload = new Event("beforeunload", { cancelable: true });
    window.dispatchEvent(beforeUnload);
    expect(beforeUnload.defaultPrevented).toBe(true);

    await act(async () => { await router.navigate(-1); });
    expect(confirm).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId("current-location")).toHaveTextContent("/configuration/ingestion-tasks/task-1");
    expect(screen.getByLabelText("配置名称")).toHaveValue("采集任务已修改");

    confirm.mockReturnValue(true);
    await act(async () => { await router.navigate(-1); });
    expect(await screen.findByText("已离开编辑器")).toBeInTheDocument();
  });

  it("单任务加载后未编辑时可离开，且只读配置不会登记脏状态", async () => {
    const confirm = vi.fn(() => false);
    vi.stubGlobal("confirm", confirm);
    vi.stubGlobal("fetch", taskEditorFetch(editableTask()));
    const router = renderTaskEditor(["/outside", "/configuration/ingestion-tasks/task-1?version=1"], 1, false);

    expect(await screen.findByLabelText("配置名称")).toHaveValue("采集任务一");
    const beforeUnload = new Event("beforeunload", { cancelable: true });
    window.dispatchEvent(beforeUnload);
    expect(beforeUnload.defaultPrevented).toBe(false);
    await act(async () => { await router.navigate(-1); });
    expect(confirm).not.toHaveBeenCalled();
    expect(await screen.findByText("已离开编辑器")).toBeInTheDocument();
  });

  it("单任务保存成功后清除离页保护，保存失败仍保护修改", async () => {
    const confirm = vi.fn(() => false);
    vi.stubGlobal("confirm", confirm);
    const task = editableTask();
    const fetchMock = taskEditorFetch(task);
    vi.stubGlobal("fetch", fetchMock);
    renderTaskEditor(["/configuration/ingestion-tasks/task-1?version=1"]);

    fireEvent.change(await screen.findByLabelText("配置名称"), { target: { value: "已保存的任务" } });
    fireEvent.click(screen.getByRole("button", { name: "保存草稿" }));
    expect(await screen.findByText("采集任务列表")).toBeInTheDocument();
    expect(confirm).not.toHaveBeenCalled();
    expect(fetchMock.mock.calls.some(([url, options]) =>
      String(url) === "/api/v1/ingestion-tasks" && options?.method === "POST")).toBe(true);

    cleanup();
    const failingConfirm = vi.fn(() => false);
    vi.stubGlobal("confirm", failingConfirm);
    const failingFetchMock = taskEditorFetch(task, { saveStatus: 500, savePayload: { detail: "模拟保存失败" } });
    vi.stubGlobal("fetch", failingFetchMock);
    const router = renderTaskEditor(["/outside", "/configuration/ingestion-tasks/task-1?version=1"], 1);
    fireEvent.change(await screen.findByLabelText("配置名称"), { target: { value: "尚未保存的任务" } });
    fireEvent.click(screen.getByRole("button", { name: "保存草稿" }));
    await waitFor(() => expect(failingFetchMock.mock.calls.some(([url, options]) =>
      String(url) === "/api/v1/ingestion-tasks" && options?.method === "POST")).toBe(true));
    await screen.findByText("模拟保存失败");
    await act(async () => { await router.navigate(-1); });
    expect(failingConfirm).toHaveBeenCalledTimes(1);
    expect(screen.getByLabelText("配置名称")).toHaveValue("尚未保存的任务");
  });

  it("单任务保存请求未完成时禁用输入，避免未提交修改随成功导航丢失", async () => {
    const user = userEvent.setup();
    const confirm = vi.fn(() => false);
    vi.stubGlobal("confirm", confirm);
    const task = editableTask();
    let resolveSave;
    const pendingSave = new Promise(resolve => { resolveSave = resolve; });
    const ordinaryFetch = taskEditorFetch(task);
    const fetchMock = vi.fn((url, options = {}) => {
      const requestUrl = new URL(String(url), "http://localhost");
      if (requestUrl.pathname === "/api/v1/ingestion-tasks" && options.method === "POST") return pendingSave;
      return ordinaryFetch(url, options);
    });
    vi.stubGlobal("fetch", fetchMock);
    const router = renderTaskEditor(["/outside", "/configuration/ingestion-tasks/task-1?version=1"], 1);
    const name = await screen.findByLabelText("配置名称");
    fireEvent.change(name, { target: { value: "提交中的任务" } });
    fireEvent.click(screen.getByRole("button", { name: "保存草稿" }));
    await waitFor(() => expect(fetchMock.mock.calls.some(([url, options]) =>
      String(url) === "/api/v1/ingestion-tasks" && options?.method === "POST")).toBe(true));

    await act(async () => { await router.navigate(-1); });
    expect(confirm).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId("current-location")).toHaveTextContent("/configuration/ingestion-tasks/task-1");
    const beforeUnload = new Event("beforeunload", { cancelable: true });
    window.dispatchEvent(beforeUnload);
    expect(beforeUnload.defaultPrevented).toBe(true);
    expect(screen.getByLabelText("配置名称")).toBeDisabled();
    await user.type(screen.getByLabelText("配置名称"), "请求期间的修改");
    expect(screen.getByLabelText("配置名称")).toHaveValue("提交中的任务");

    resolveSave(jsonResponse(task));
    expect(await screen.findByText("采集任务列表")).toBeInTheDocument();
    expect(screen.getByTestId("current-location")).toHaveTextContent("/configuration/ingestion-tasks");
    expect(router.state.location.pathname).toBe("/configuration/ingestion-tasks");
  });

  it("新增画布任务取消离页时保留任务，确认后离开", async () => {
    const confirm = vi.fn(() => false);
    vi.stubGlobal("confirm", confirm);
    vi.stubGlobal("fetch", vi.fn(url => {
      const requestUrl = new URL(String(url), "http://localhost");
      if (requestUrl.pathname === "/api/v1/auth/me")
        return Promise.resolve(jsonResponse({ roles: ["process.engineer"], siteIds: ["SITE-001"] }));
      if (requestUrl.pathname === "/api/edges")
        return Promise.resolve(jsonResponse([{ edgeId: "EDGE-1", siteId: "SITE-001", hostname: "现场节点一" }]));
      if (requestUrl.pathname === "/api/v1/acquisition-protocols")
        return Promise.resolve(jsonResponse({ protocols: [] }));
      return Promise.resolve(jsonResponse({ data: [] }));
    }));
    const router = createMemoryRouter([
      {
        path: "*",
        element: <UnsavedNavigationGuard>
          <CurrentLocation />
          <Routes>
            <Route path="/configuration/ingestion-tasks/new" element={<IngestionTaskPage />} />
            <Route path="/outside" element={<h1>已离开编辑器</h1>} />
          </Routes>
        </UnsavedNavigationGuard>,
      },
    ], { initialEntries: ["/outside", "/configuration/ingestion-tasks/new"], initialIndex: 1 });
    render(<RouterProvider router={router} />);

    fireEvent.click(await screen.findByRole("button", { name: /添加采集任务/ }));
    expect(await screen.findByText("2 个采集任务")).toBeInTheDocument();

    await act(async () => { await router.navigate(-1); });
    expect(confirm).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId("current-location")).toHaveTextContent("/configuration/ingestion-tasks/new");
    expect(screen.getByText("2 个采集任务")).toBeInTheDocument();

    confirm.mockReturnValue(true);
    await act(async () => { await router.navigate(-1); });
    expect(await screen.findByText("已离开编辑器")).toBeInTheDocument();
  });
});
