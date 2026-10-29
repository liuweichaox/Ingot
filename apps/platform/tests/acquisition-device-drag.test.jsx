// Verify device-profile drag, Edge assignment, and accessible non-drag alternatives.
import React from "react";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router";
import { IngestionDeviceGroupCanvas } from "../src/acquisition/IngestionDeviceGroupCanvas";
import { IngestionTaskPage } from "../src/acquisition/IngestionTaskPage";

afterEach(() => cleanup());

function createDataTransfer() {
  const payload = new Map();
  return {
    effectAllowed: "none",
    dropEffect: "none",
    setData(type, value) { payload.set(type, value); },
    getData(type) { return payload.get(type) || ""; },
    get types() { return [...payload.keys()]; },
  };
}

const edges = [
  { edgeId: "edge-1", hostname: "现场节点一" },
  { edgeId: "edge-2", displayName: "现场节点二" },
];

describe("多设备画布拖拽", () => {
  it("设备类型入口支持搜索，便于在完整设备目录中定位", () => {
    render(<IngestionDeviceGroupCanvas edges={edges} devices={[]} onAddDeviceProfile={vi.fn()} />);
    fireEvent.change(screen.getByLabelText("搜索设备类型"), { target: { value: "机器人" } });

    expect(screen.getByRole("button", { name: /添加工业机器人到当前 Edge/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /添加PLC \/ 可编程控制器到当前 Edge/ })).not.toBeInTheDocument();
  });

  it("失效 Edge 分区不能添加新任务", () => {
    const onAddDeviceProfile = vi.fn();
    render(<IngestionDeviceGroupCanvas
      devices={[{
        key: "device-1",
        form: {
          taskId: "legacy", name: "旧任务", protocol: "http-polling", edgeId: "removed-edge",
          deviceProfile: { kind: "plc" }, httpPolling: { baseUrl: "http://plc.local" }, valueMappings: [],
        },
      }]}
      edges={[edges[0]]}
      onAddDeviceProfile={onAddDeviceProfile}
    />);
    const stalePartition = screen.getByRole("region", { name: "removed-edge设备分区" });

    expect(within(stalePartition).getByRole("combobox", { name: "选择要添加到removed-edge的设备类型" })).toBeDisabled();
    expect(within(stalePartition).getByRole("button", { name: "添加PLC / 可编程控制器到removed-edge" })).toBeDisabled();
    expect(onAddDeviceProfile).not.toHaveBeenCalled();
  });

  it("将设备类型拖到 Edge 分区后按类型和 Edge 创建新配置", () => {
    const onAddDeviceProfile = vi.fn();
    render(<IngestionDeviceGroupCanvas edges={edges} devices={[]} onAddDeviceProfile={onAddDeviceProfile} />);

    const plcType = screen.getByRole("button", { name: /添加PLC \/ 可编程控制器到当前 Edge/ });
    const dataTransfer = createDataTransfer();
    fireEvent.dragStart(plcType, { dataTransfer });
    fireEvent.dragOver(screen.getByRole("region", { name: "现场节点一设备分区" }), { dataTransfer });
    fireEvent.drop(screen.getByRole("region", { name: "现场节点一设备分区" }), { dataTransfer });

    expect(onAddDeviceProfile).toHaveBeenCalledWith("plc", "edge-1");
  });

  it("键盘可在指定 Edge 添加选择的设备类型并重新分配现有设备", () => {
    const onAddDeviceProfile = vi.fn();
    const onAssignEdge = vi.fn();
    const devices = [{
      key: "device-1",
      form: {
        taskId: "press-01", name: "压力机一号", protocol: "http-polling", edgeId: "edge-1",
        deviceProfile: { kind: "plc", manufacturer: "Mitsubishi", model: "FX3U" },
        httpPolling: { baseUrl: "http://plc.local" },
        valueMappings: [], subjectId: "PRESS-01",
      },
    }];
    render(<IngestionDeviceGroupCanvas
      devices={devices}
      edges={edges}
      activeDeviceKey="device-1"
      activeEdgeId="edge-1"
      onAddDeviceProfile={onAddDeviceProfile}
      onAssignEdge={onAssignEdge}
    />);

    const edgeTwo = screen.getByRole("region", { name: "现场节点二设备分区" });
    fireEvent.change(within(edgeTwo).getByRole("combobox", { name: "选择要添加到现场节点二的设备类型" }), {
      target: { value: "cnc" },
    });
    fireEvent.click(within(edgeTwo).getByRole("button", { name: /添加CNC \/ 数控机床到现场节点二/ }));
    expect(onAddDeviceProfile).toHaveBeenCalledWith("cnc", "edge-2");

    fireEvent.change(screen.getByRole("combobox", { name: "为 压力机一号 指定 Edge" }), {
      target: { value: "edge-2" },
    });
    expect(onAssignEdge).toHaveBeenCalledWith("device-1", "edge-2");
  });

  it("将现有设备卡片拖到另一个 Edge 时只更新其 Edge 归属", () => {
    const onAssignEdge = vi.fn();
    const devices = [{
      key: "device-1",
      form: {
        taskId: "press-01", name: "压力机一号", protocol: "http-polling", edgeId: "edge-1",
        deviceProfile: { kind: "plc" },
        httpPolling: { baseUrl: "http://plc.local" },
        valueMappings: [{ dataItemCode: "temperature", sourcePath: "temperature" }],
        subjectId: "PRESS-01",
      },
    }];
    render(<IngestionDeviceGroupCanvas devices={devices} edges={edges} onAssignEdge={onAssignEdge} />);

    const deviceCard = screen.getByRole("button", { name: /选择设备 压力机一号/ });
    const dataTransfer = createDataTransfer();
    fireEvent.dragStart(deviceCard, { dataTransfer });
    fireEvent.drop(screen.getByRole("region", { name: "现场节点二设备分区" }), { dataTransfer });

    expect(onAssignEdge).toHaveBeenCalledWith("device-1", "edge-2");
  });

  it("新建页把拖入设备与目标 Edge 写入真实任务表单", async () => {
    const response = data => new Response(JSON.stringify(data), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    });
    vi.stubGlobal("fetch", vi.fn(url => {
      const path = String(url);
      if (path.includes("/api/edges")) return Promise.resolve(response({ data: edges }));
      if (path.includes("/process-data-models")) return Promise.resolve(response({ data: [] }));
      if (path.includes("/acquisition-protocols")) return Promise.resolve(response({ protocols: [] }));
      return Promise.resolve(response({ data: [] }));
    }));
    render(<MemoryRouter initialEntries={["/configuration/ingestion-tasks/new"]}>
      <Routes><Route path="/configuration/ingestion-tasks/:taskId" element={<IngestionTaskPage />} /></Routes>
    </MemoryRouter>);

    fireEvent.click(await screen.findByRole("button", { name: "多设备画布" }));
    const edgeTwo = await screen.findByRole("region", { name: "现场节点二设备分区" });
    const plcType = screen.getByRole("button", { name: /添加PLC \/ 可编程控制器到当前 Edge/ });
    const dataTransfer = createDataTransfer();
    fireEvent.dragStart(plcType, { dataTransfer });
    fireEvent.drop(edgeTwo, { dataTransfer });

    expect(screen.getByLabelText("画布 设备类型")).toHaveValue("plc");
    const newDeviceEdge = screen.getByRole("combobox", {
      name: "为 PLC / 可编程控制器采集 2 指定 Edge",
    });
    expect(newDeviceEdge).toHaveValue("edge-2");

    const deviceCard = screen.getByRole("button", { name: /选择设备 PLC \/ 可编程控制器采集 2/ });
    const moveTransfer = createDataTransfer();
    fireEvent.dragStart(deviceCard, { dataTransfer: moveTransfer });
    fireEvent.drop(screen.getByRole("region", { name: "现场节点一设备分区" }), { dataTransfer: moveTransfer });
    expect(screen.getByRole("combobox", {
      name: "为 PLC / 可编程控制器采集 2 指定 Edge",
    })).toHaveValue("edge-1");
  });
});
