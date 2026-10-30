// 验证数据源复用、逻辑对象绑定和绑定草稿的探查发布流程。
import React from "react";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router";
import { IngestionTaskPage } from "../src/acquisition/IngestionTaskPage";
import { ReusableBindingPanel } from "../src/acquisition/ReusableBindingPanel";
import { createIngestionTaskForm, toPayload, validateIngestionTask } from "../src/acquisition/ingestionTaskForm";

const source = {
  dataSourceId: "source-1", version: 1, name: "采集源 A", status: "published",
  edgeId: "edge-1", protocol: "http-polling", sourceKey: "equipment/source-1",
  subjectType: "equipment", subjectId: "EQUIP-01",
  httpPolling: { baseUrl: "http://device.local", snapshotPath: "/snapshot", pollIntervalMs: 1000 },
};
const template = {
  templateId: "device-template", version: 1, name: "设备模板", status: "published",
  protocol: "http-polling", dataModelId: "model-1", dataModelVersion: 1,
};
const legacyBinding = {
  taskId: "legacy", version: 1, name: "原有任务", status: "published",
  dataSourceId: "source-1", dataSourceVersion: 1,
  templateId: "device-template", templateVersion: 1,
};

function response(payload) {
  return new Response(JSON.stringify(payload), { status: 200, headers: { "Content-Type": "application/json" } });
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("复用数据源建立采集任务", () => {
  it("按同一端点各任务的采样周期估算串行快照负荷", () => {
    const firstSource = { ...source, protocol: "melsec-a1e", melsecA1E: { host: "DEVICE-01", port: 4104, pollIntervalMs: 500, dataCode: "ascii" } };
    const publishedBindings = [1, 2].map(index => ({
      ...legacyBinding,
      taskId: `task-${index + 1}`,
    }));
    render(<MemoryRouter><ReusableBindingPanel
      sources={[firstSource]}
      templates={[{ ...template, protocol: "melsec-a1e" }]}
      tasks={publishedBindings.map(binding => ({ taskId: binding.taskId, version: 1, status: "published" }))}
      bindings={publishedBindings}
      capabilities={[{ protocol: "melsec-a1e", sharesConnectionBetweenTasks: true, supportedWireEncodings: ["binary", "ascii"] }]}
      onChanged={vi.fn()}
    /></MemoryRouter>);

    expect(screen.getAllByText(/同端点已发布 2 个任务，按配置周期预计约 240 份快照\/分钟/)).toHaveLength(1);
  });

  it("显示旧绑定的默认对象和每个任务的生产设备单元", () => {
    render(<MemoryRouter><ReusableBindingPanel
      sources={[source]}
      templates={[template]}
      tasks={[{ taskId: "newer", version: 1, equipmentId: "EQUIP-01" }]}
      bindings={[legacyBinding, { ...legacyBinding, taskId: "newer", name: "设备 A", subjectType: "equipment", subjectId: "UNIT-A", equipmentId: "EQUIP-01" }]}
      onChanged={vi.fn()}
    /></MemoryRouter>);

    expect(screen.getByText(/对象 生产设备 · EQUIP-01（默认）/)).toBeInTheDocument();
    expect(screen.getByText(/对象 生产设备 · UNIT-A/)).toBeInTheDocument();
    expect(screen.getByText("设备单元 EQUIP-01")).toBeInTheDocument();
    expect(screen.getAllByRole("link", { name: "查看任务" })[0]).toHaveAttribute("href", "/configuration/ingestion-tasks/legacy?version=1");
  });

  it("使用相同物理数据源创建带逻辑对象和机器编号的草稿", async () => {
    const fetchMock = vi.fn().mockResolvedValue(response({ data: [], count: 1 }));
    vi.stubGlobal("fetch", fetchMock);
    const onChanged = vi.fn().mockResolvedValue(undefined);
    render(<MemoryRouter><ReusableBindingPanel
      sources={[source]}
      templates={[template, { ...template, templateId: "other", protocol: "mqtt" }]}
      tasks={[]}
      bindings={[legacyBinding]}
      onChanged={onChanged}
    /></MemoryRouter>);

    fireEvent.change(screen.getByLabelText("已发布数据源"), { target: { value: "source-1::1" } });
    expect(screen.getByText(/任务归属 生产设备 · EQUIP-01（数据源默认）/)).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: /other/ })).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText(/已发布任务模板/), { target: { value: "device-template::1" } });
    fireEvent.change(screen.getByLabelText("新任务编号"), { target: { value: "device-a" } });
    fireEvent.change(screen.getByLabelText("新任务名称"), { target: { value: "设备 A" } });
    fireEvent.change(screen.getByLabelText(/逻辑对象类型/), { target: { value: "equipment" } });
    expect(screen.getByRole("button", { name: "创建任务草稿" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/逻辑对象编号/), { target: { value: "UNIT-A" } });
    fireEvent.change(screen.getByLabelText(/生产设备单元编号/), { target: { value: "EQUIP-01" } });
    fireEvent.click(screen.getByRole("button", { name: "创建任务草稿" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    const [url, options] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/v1/ingestion-configuration/materialize");
    expect(JSON.parse(options.body).bindings[0]).toMatchObject({
      taskId: "device-a", status: "draft", dataSourceId: "source-1", dataSourceVersion: 1,
      templateId: "device-template", templateVersion: 1,
      subjectType: "equipment", subjectId: "UNIT-A", equipmentId: "EQUIP-01",
    });
    await waitFor(() => expect(onChanged).toHaveBeenCalled());
    expect(await screen.findByText(/已创建 device-a 草稿/)).toBeInTheDocument();
  });

  it("空逻辑对象沿用来源默认值并提示固定 MQTT 客户端编号", async () => {
    const fetchMock = vi.fn().mockResolvedValue(response({ data: [], count: 1 }));
    vi.stubGlobal("fetch", fetchMock);
    const mqttSource = { ...source, protocol: "mqtt", mqtt: { clientId: "fixed-client" } };
    render(<MemoryRouter><ReusableBindingPanel
      sources={[mqttSource]} templates={[{ ...template, protocol: "mqtt" }]}
      tasks={[]} bindings={[]} onChanged={vi.fn().mockResolvedValue(undefined)}
    /></MemoryRouter>);
    fireEvent.change(screen.getByLabelText("已发布数据源"), { target: { value: "source-1::1" } });
    expect(screen.getByText(/多个任务使用相同编号会互相断开/)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText(/已发布任务模板/), { target: { value: "device-template::1" } });
    fireEvent.change(screen.getByLabelText("新任务编号"), { target: { value: "device-b" } });
    fireEvent.change(screen.getByLabelText("新任务名称"), { target: { value: "设备 B" } });
    fireEvent.click(screen.getByRole("button", { name: "创建任务草稿" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    expect(JSON.parse(fetchMock.mock.calls[0][1].body).bindings[0]).toMatchObject({
      subjectType: null, subjectId: null, equipmentId: null,
    });
  });

  it("普通采集任务可保留可选生产设备单元编号", () => {
    const form = createIngestionTaskForm({ subjectId: "UNIT-A", equipmentId: "EQUIP-01" });
    expect(form.equipmentId).toBe("EQUIP-01");
    expect(toPayload(form).equipmentId).toBe("EQUIP-01");
    expect(toPayload(createIngestionTaskForm()).equipmentId).toBeNull();
  });

  it("只读身份仍可核对来源和绑定，但不能创建草稿", () => {
    render(<MemoryRouter><ReusableBindingPanel
      sources={[source]} templates={[template]} tasks={[]} bindings={[legacyBinding]} canWrite={false}
    /></MemoryRouter>);
    expect(screen.getByText(/对象 生产设备 · EQUIP-01（默认）/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "创建任务草稿" })).not.toBeInTheDocument();
  });

  it("按服务端驱动能力展示共享连接与编码，不套用到其他协议", () => {
    const a1eSource = { ...source, protocol: "melsec-a1e", melsecA1E: { dataCode: "ascii" } };
    const capabilities = [
      { protocol: "melsec-a1e", sharesConnectionBetweenTasks: true, supportedWireEncodings: ["binary", "ascii"] },
      { protocol: "mqtt", sharesConnectionBetweenTasks: false, supportedWireEncodings: [] },
    ];
    const view = render(<MemoryRouter><ReusableBindingPanel
      sources={[a1eSource]} templates={[{ ...template, protocol: "melsec-a1e" }]}
      tasks={[]} bindings={[]} capabilities={capabilities} onChanged={vi.fn()}
    /></MemoryRouter>);
    fireEvent.change(screen.getByLabelText("已发布数据源"), { target: { value: "source-1::1" } });
    expect(screen.getAllByText(/同一端点的兼容任务共享连接，读取与探查串行/).length).toBeGreaterThan(0);
    expect(screen.getByText(/驱动支持 二进制、ASCII 码/)).toBeInTheDocument();
    expect(screen.getByText(/当前来源通信数据码：ASCII 码/)).toBeInTheDocument();
    view.unmount();
    render(<MemoryRouter><ReusableBindingPanel
      sources={[{ ...source, protocol: "mqtt" }]} templates={[{ ...template, protocol: "mqtt" }]}
      tasks={[]} bindings={[]} capabilities={capabilities} onChanged={vi.fn()}
    /></MemoryRouter>);
    fireEvent.change(screen.getByLabelText("已发布数据源"), { target: { value: "source-1::1" } });
    expect(screen.getAllByText(/各任务独立采集，不共享采集会话/).length).toBeGreaterThan(0);
    expect(screen.queryByText(/驱动支持 二进制、ASCII 码/)).not.toBeInTheDocument();
  });
});

describe("绑定草稿现场验证", () => {
  it("只读绑定草稿可以探查，成功后可发布", async () => {
    const boundTask = {
      taskId: "device-a", version: 1, name: "设备 A", status: "draft",
      templateId: template.templateId, templateVersion: 1, dataSourceId: source.dataSourceId, dataSourceVersion: 1,
      edgeId: source.edgeId, protocol: source.protocol, dataModelId: "model-1", dataModelVersion: 1,
      source: source.sourceKey, subjectType: "equipment", subjectId: "UNIT-A", equipmentId: "EQUIP-01",
      httpPolling: source.httpPolling, timestampMode: "edge-received",
      valueMappings: [{ dataItemCode: "temperature", sourcePath: "temperature", sourceDataType: "auto", sourceUnit: "°C" }],
    };
    let probeAttempts = 0;
    const fetchMock = vi.fn((url, options = {}) => {
      const path = String(url);
      if (path.includes("/ingestion-tasks/probe") && options.method === "POST") {
        probeAttempts += 1;
        return Promise.resolve(response(probeAttempts === 1
          ? { success: false, mappingsValidated: false, message: "点位校验失败", points: [] }
          : { success: true, mappingsValidated: true, message: "连接和映射已验证", points: [] }));
      }
      if (path.includes("/bindings/device-a/1:publish") && options.method === "POST")
        return Promise.resolve(response({ data: boundTask }));
      if (path.includes("/ingestion-tasks?")) return Promise.resolve(response({ data: [boundTask] }));
      if (path.includes("/process-data-models")) return Promise.resolve(response({ data: [{ modelId: "model-1", version: 1, name: "工艺变量", acquisition: { dataItems: [{ code: "temperature", displayName: "温度", nullable: false, unit: "°C" }] }, controlParameters: [] }] }));
      if (path.includes("/api/edges")) return Promise.resolve(response({ data: [{ edgeId: "edge-1" }] }));
      return Promise.resolve(response({ protocols: [] }));
    });
    vi.stubGlobal("fetch", fetchMock);
    render(<MemoryRouter initialEntries={["/configuration/ingestion-tasks/device-a?version=1"]}>
      <Routes><Route path="/configuration/ingestion-tasks/:taskId" element={<IngestionTaskPage />} /></Routes>
    </MemoryRouter>);

    expect(await screen.findByText(/该任务由不可变模板与数据源版本生成/)).toBeInTheDocument();
    expect(screen.getByLabelText(/生产设备单元编号/)).toHaveValue("EQUIP-01");
    const publish = screen.getByRole("button", { name: "发布任务绑定" });
    expect(publish).toBeDisabled();
    fireEvent.click(screen.getByRole("tab", { name: "连接" }));
    fireEvent.click(screen.getByRole("button", { name: "验证连接" }));
    expect(await screen.findByText("点位校验失败")).toBeInTheDocument();
    expect(publish).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "验证连接" }));
    expect(await screen.findByText("连接和映射已验证")).toBeInTheDocument();
    await waitFor(() => expect(publish).toBeEnabled());
    fireEvent.click(publish);
    await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => String(url).includes("/bindings/device-a/1:publish"))).toBe(true));
  });
});

describe("过程执行激活条件", () => {
  it("旧配置默认按指定值判断，非零条件往返无需固定取值", () => {
    const existing = {
      taskId: "serial-a", name: "生产序号", edgeId: "edge-1", dataModelId: "model-1", dataModelVersion: 1,
      subjectId: "UNIT-A", httpPolling: source.httpPolling, timestampMode: "edge-received",
      valueMappings: [{ dataItemCode: "temperature", sourcePath: "temperature" }],
      contextMappings: [{ contextKey: "run_serial", sourcePath: "serial", required: true }],
      lifecycle: { mode: "discrete", activeContextKey: "run_serial", activeValue: "1" },
    };
    const oldForm = createIngestionTaskForm(existing);
    expect(oldForm.lifecycle.activeCondition).toBe("equals");
    expect(toPayload(oldForm).lifecycle).toMatchObject({ activeCondition: "equals", activeValue: "1" });

    const nonzero = createIngestionTaskForm({
      ...existing, lifecycle: { ...existing.lifecycle, activeCondition: "nonzero", activeValue: "" },
    });
    expect(validateIngestionTask(nonzero).errors["lifecycle.activeValue"]).toBeUndefined();
    expect(toPayload(nonzero).lifecycle).toMatchObject({ activeContextKey: "run_serial", activeCondition: "nonzero" });
    expect(toPayload(nonzero).lifecycle).not.toHaveProperty("activeValue");
    expect(createIngestionTaskForm({ lifecycle: toPayload(nonzero).lifecycle }).lifecycle.activeCondition).toBe("nonzero");

    const unknown = { ...nonzero, lifecycle: { ...nonzero.lifecycle, activeCondition: "unknown" } };
    expect(validateIngestionTask(unknown).errors["lifecycle.activeCondition"]).toBeTruthy();
    const missingValue = { ...oldForm, lifecycle: { ...oldForm.lifecycle, activeValue: "" } };
    expect(validateIngestionTask(missingValue).errors["lifecycle.activeValue"]).toBeTruthy();
  });

  it("在普通采集配置中选择非零并保存，保留生产状态上下文键", async () => {
    const task = {
      taskId: "serial-a", version: 1, name: "生产序号", status: "draft", edgeId: "edge-1",
      protocol: "http-polling", dataModelId: "model-1", dataModelVersion: 1,
      source: "equipment/source-1", subjectType: "equipment", subjectId: "UNIT-A", httpPolling: source.httpPolling,
      timestampMode: "edge-received", valueMappings: [{ dataItemCode: "temperature", sourcePath: "temperature", sourceUnit: "°C" }],
      contextMappings: [{ contextKey: "run_serial", sourcePath: "serial", required: true }],
      lifecycle: { mode: "discrete", activeContextKey: "run_serial", activeValue: "1" },
    };
    const fetchMock = vi.fn((url, options = {}) => {
      const path = String(url);
      if (path === "/api/v1/ingestion-tasks" && options.method === "POST") return Promise.resolve(response(task));
      if (path.includes("/ingestion-tasks?")) return Promise.resolve(response({ data: [task] }));
      if (path.includes("/process-data-models")) return Promise.resolve(response({ data: [{ modelId: "model-1", version: 1, name: "工艺变量", acquisition: { dataItems: [{ code: "temperature", unit: "°C", nullable: false }] }, controlParameters: [] }] }));
      if (path.includes("/api/edges")) return Promise.resolve(response({ data: [{ edgeId: "edge-1" }] }));
      return Promise.resolve(response({ protocols: [] }));
    });
    vi.stubGlobal("fetch", fetchMock);
    render(<MemoryRouter initialEntries={["/configuration/ingestion-tasks/serial-a?version=1"]}>
      <Routes><Route path="/configuration/ingestion-tasks/:taskId" element={<IngestionTaskPage />} /></Routes>
    </MemoryRouter>);

    expect(await screen.findByLabelText("配置名称")).toHaveValue("生产序号");
    fireEvent.click(screen.getByRole("tab", { name: "高级" }));
    const condition = screen.getByLabelText(/运行激活条件/);
    expect(condition).toHaveValue("equals");
    expect(screen.getByLabelText(/生产中的取值/)).toHaveValue("1");
    fireEvent.change(condition, { target: { value: "nonzero" } });
    expect(screen.queryByLabelText(/生产中的取值/)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "保存草稿" }));
    await waitFor(() => expect(fetchMock.mock.calls.some(([url, options]) =>
      String(url) === "/api/v1/ingestion-tasks" && options?.method === "POST")).toBe(true));
    const [, request] = fetchMock.mock.calls.find(([url, options]) =>
      String(url) === "/api/v1/ingestion-tasks" && options?.method === "POST");
    expect(JSON.parse(request.body).lifecycle).toMatchObject({ activeContextKey: "run_serial", activeCondition: "nonzero" });
    expect(JSON.parse(request.body).lifecycle).not.toHaveProperty("activeValue");
  });
});
