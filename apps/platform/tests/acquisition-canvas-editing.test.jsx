// 验证采集画布的节点选择、点位输入和共享任务表单编辑。
import React, { useState } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router";
import { IngestionTaskTopologyCanvas } from "../src/acquisition/IngestionTaskTopologyCanvas";
import { IngestionTaskPage } from "../src/acquisition/IngestionTaskPage";
import { applyProtocolChange, createIngestionTaskForm } from "../src/acquisition/ingestionTaskForm";
import { protocolDescriptor } from "../src/acquisition/protocolRegistry";

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

function SingleTaskHarness({ readOnly = false }) {
  const [form, setForm] = useState(() => createIngestionTaskForm());
  const descriptor = protocolDescriptor(form.protocol);
  return <>
    <IngestionTaskTopologyCanvas
      form={form}
      descriptor={descriptor}
      edges={[{ edgeId: "edge-1", hostname: "现场节点一" }]}
      models={[{ modelId: "model-1", version: 1, name: "模型一", status: "published" }]}
      readOnly={readOnly}
      onUpdate={patch => setForm(current => ({ ...current, ...patch }))}
      onUpdateSection={(section, value) => setForm(current => ({ ...current, [section]: value }))}
      onProtocolChange={protocol => setForm(current => applyProtocolChange(current, protocol))}
    />
    <output data-testid="shared-form">{JSON.stringify(form)}</output>
  </>;
}

describe("采集拓扑画布编辑", () => {
  it("单任务画布的协议、连接和归属编辑写入同一表单状态", () => {
    render(<SingleTaskHarness />);
    fireEvent.change(screen.getByLabelText("画布 通信驱动"), { target: { value: "melsec-a1e" } });
    fireEvent.change(screen.getByLabelText("画布 PLC 地址"), { target: { value: "192.168.1.42" } });
    fireEvent.change(screen.getByLabelText("画布 通信数据码"), { target: { value: "ascii" } });
    let form = JSON.parse(screen.getByTestId("shared-form").textContent);
    expect(form.protocol).toBe("melsec-a1e");
    expect(form.melsecA1E).toMatchObject({ host: "192.168.1.42", dataCode: "ascii" });

    fireEvent.click(screen.getByRole("button", { name: /对象与设备单元/ }));
    fireEvent.change(screen.getByLabelText("画布 归属对象编号"), { target: { value: "MACHINE-1" } });
    fireEvent.change(screen.getByLabelText("画布 生产设备单元编号"), { target: { value: "CHAMBER-1" } });
    form = JSON.parse(screen.getByTestId("shared-form").textContent);
    expect(form).toMatchObject({ subjectId: "MACHINE-1", equipmentId: "CHAMBER-1" });
  });

  it("只读画布不允许修改连接、现场节点或归属", () => {
    render(<SingleTaskHarness readOnly />);
    expect(screen.getByLabelText("画布 通信驱动")).toBeDisabled();
    expect(screen.getByLabelText("画布 服务地址")).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: /现场节点/ }));
    expect(screen.getByLabelText("画布 现场节点")).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: /对象与设备单元/ }));
    expect(screen.getByLabelText("画布 归属对象编号")).toBeDisabled();
    expect(screen.getByLabelText("画布 生产设备单元编号")).toBeDisabled();
  });

  it("配置改变后迟到的旧探查结果不能启用发布", async () => {
    const task = {
      taskId: "editable-task", version: 1, name: "待验证设备", status: "draft", edgeId: "edge-1",
      protocol: "http-polling", dataModelId: "model-1", dataModelVersion: 1,
      subjectType: "equipment", subjectId: "MACHINE-1", equipmentId: "CHAMBER-1",
      httpPolling: { baseUrl: "http://plc.local", snapshotPath: "/snapshot", pollIntervalMs: 1000 },
      valueMappings: [{ dataItemCode: "temperature", sourcePath: "temperature", sourceDataType: "auto", sourceUnit: "°C" }],
    };
    let resolveProbe;
    const delayedProbe = new Promise(resolve => { resolveProbe = resolve; });
    const response = data => new Response(JSON.stringify(data), { status: 200, headers: { "Content-Type": "application/json" } });
    vi.stubGlobal("fetch", vi.fn((url, options = {}) => {
      const path = String(url);
      if (path.includes("/ingestion-tasks/probe") && options.method === "POST") return delayedProbe;
      if (path.includes("/ingestion-tasks?")) return Promise.resolve(response({ data: [task] }));
      if (path.includes("/process-data-models")) return Promise.resolve(response({ data: [{ modelId: "model-1", version: 1, status: "published", acquisition: { dataItems: [{ code: "temperature", dataType: "double", nullable: false, unit: "°C" }] }, controlParameters: [] }] }));
      if (path.includes("/api/edges")) return Promise.resolve(response({ data: [{ edgeId: "edge-1" }] }));
      return Promise.resolve(response({ protocols: [] }));
    }));
    render(<MemoryRouter initialEntries={["/configuration/ingestion-tasks/editable-task?version=1"]}>
      <Routes><Route path="/configuration/ingestion-tasks/:taskId" element={<IngestionTaskPage />} /></Routes>
    </MemoryRouter>);

    const probeButton = await screen.findByRole("button", { name: "验证连接" });
    expect(probeButton).toBeEnabled();
    fireEvent.click(probeButton);
    expect(screen.getByRole("button", { name: "读取中…" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("画布 服务地址"), { target: { value: "http://changed.local" } });
    await act(async () => resolveProbe(response({ success: true, mappingsValidated: true, message: "旧结果不能用于发布", points: [] })));
    await waitFor(() => expect(screen.getByRole("button", { name: "验证连接" })).toBeEnabled());
    expect(screen.queryByText("旧结果不能用于发布")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "发布到现场节点" })).toBeDisabled();
  });

  it("新建设备支持单任务和通用多设备画布", async () => {
    const response = data => new Response(JSON.stringify(data), { status: 200, headers: { "Content-Type": "application/json" } });
    vi.stubGlobal("fetch", vi.fn(url => {
      const path = String(url);
      if (path.includes("/api/edges")) return Promise.resolve(response({ data: [{ edgeId: "edge-1" }] }));
      if (path.includes("/process-data-models")) return Promise.resolve(response({ data: [] }));
      if (path.includes("/acquisition-protocols")) return Promise.resolve(response({ protocols: [] }));
      return Promise.resolve(response({ data: [] }));
    }));
    render(<MemoryRouter initialEntries={["/configuration/ingestion-tasks/new"]}>
      <Routes><Route path="/configuration/ingestion-tasks/:taskId" element={<IngestionTaskPage />} /></Routes>
    </MemoryRouter>);
    const setupOptions = within(screen.getByRole("group", { name: "采集配置方式" }));
    expect(setupOptions.getByRole("button", { name: "单数据源任务" })).toBeInTheDocument();
    expect(setupOptions.getAllByRole("button")).toHaveLength(2);
    fireEvent.click(screen.getByRole("button", { name: "多设备画布" }));
    expect(await screen.findByRole("region", { name: "多设备采集拓扑画布" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "添加采集设备" })).toBeInTheDocument();
  });
});
