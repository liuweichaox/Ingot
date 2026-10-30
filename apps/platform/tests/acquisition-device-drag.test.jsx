// Verify relation-canvas resource search, unavailable references, and task creation.
import React from "react";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { IngestionDeviceGroupCanvas } from "../src/acquisition/IngestionDeviceGroupCanvas";
import { createIngestionTaskForm } from "../src/acquisition/ingestionTaskForm";

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

function task(key = "device-1", overrides = {}) {
  return {
    key,
    form: {
      ...createIngestionTaskForm(),
      taskId: "press-01",
      name: "压力机一号",
      protocol: "http-polling",
      edgeId: "edge-1",
      subjectId: "PRESS-01",
      deviceProfile: { kind: "plc" },
      httpPolling: { baseUrl: "http://plc.local" },
      valueMappings: [],
      ...overrides,
    },
  };
}

describe("采集关系画布", () => {
  it("按 Edge 和工艺模型筛选节点面板", () => {
    render(<IngestionDeviceGroupCanvas
      devices={[task()]}
      edges={edges}
      models={[
        { modelId: "model-a", version: 1, name: "冲压模型", status: "published" },
        { modelId: "model-b", version: 1, name: "热处理模型", status: "published" },
      ]}
      activeDeviceKey="device-1"
    />);

    fireEvent.change(screen.getByRole("textbox", { name: "筛选节点" }), { target: { value: "edge-2" } });
    expect(screen.getByRole("button", { name: /现场节点二/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /现场节点一/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /冲压模型/ })).not.toBeInTheDocument();
  });

  it("显示被任务引用但已从 Edge 清单移除的 Edge，并禁止将其作为部署目标", () => {
    render(<IngestionDeviceGroupCanvas
      devices={[task("device-1", { edgeId: "removed-edge" })]}
      edges={[]}
      activeDeviceKey="device-1"
    />);

    fireEvent.click(screen.getByRole("button", { name: "显示关联 Edge / 模型" }));
    expect(screen.getByRole("button", { name: /removed-edge · 未登记/ })).toBeDisabled();
  });

  it("从任务面板拖入画布时沿用当前任务的设备类别和 Edge", () => {
    const onAddDevice = vi.fn(() => "device-2");
    render(<IngestionDeviceGroupCanvas
      devices={[task()]}
      edges={edges}
      activeDeviceKey="device-1"
      canAddDevice
      onAddDevice={onAddDevice}
    />);

    const addTask = screen.getByRole("button", { name: /添加采集任务/ });
    const dataTransfer = createDataTransfer();
    fireEvent.dragStart(addTask, { dataTransfer });
    fireEvent.drop(screen.getByRole("main", { name: "Edge、采集任务、点位映射与工艺模型画布" }).querySelector(".acq-canvas__dropzone"), {
      dataTransfer,
      clientX: 300,
      clientY: 240,
    });

    expect(onAddDevice).toHaveBeenCalledWith("plc", "edge-1");
  });
});
