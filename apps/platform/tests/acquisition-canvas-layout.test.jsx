// Verify manual canvas placement can be restored without changing acquisition task data.

import React from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createIngestionTaskForm } from "../src/acquisition/ingestionTaskForm";

const flowState = vi.hoisted(() => ({ fitView: vi.fn() }));

vi.mock("@xyflow/react", async () => {
  const ReactRuntime = await import("react");
  const passthrough = ({ children }) => ReactRuntime.createElement("div", null, children);
  return {
    Background: passthrough,
    BackgroundVariant: { Dots: "dots" },
    Controls: passthrough,
    Handle: () => null,
    MarkerType: { ArrowClosed: "arrowclosed" },
    MiniMap: passthrough,
    Panel: passthrough,
    Position: { Left: "left", Right: "right", Top: "top", Bottom: "bottom" },
    ReactFlow: ({ nodes = [], nodeTypes = {}, onNodeDragStop }) => ReactRuntime.createElement(ReactRuntime.Fragment, null,
      ReactRuntime.createElement("output", { "data-testid": "canvas-node-positions" }, JSON.stringify(
        nodes.map(node => ({ id: node.id, position: node.position })),
      )),
      ...nodes.map(node => ReactRuntime.createElement("button", {
        key: node.id,
        type: "button",
        onClick: () => onNodeDragStop?.(null, { ...node, position: { x: 901, y: 902 } }),
      }, `模拟移动 ${node.id}`)),
      ...nodes.filter(node => node.type === "task").map(node => ReactRuntime.createElement(nodeTypes.task, {
        key: `task-content:${node.id}`,
        data: node.data,
        selected: node.selected,
      })),
    ),
    ReactFlowProvider: ({ children }) => children,
    useNodesInitialized: () => true,
    useNodesState: initialNodes => {
      const [nodes, setNodes] = ReactRuntime.useState(initialNodes);
      return [nodes, setNodes, () => {}];
    },
    useReactFlow: () => ({ screenToFlowPosition: point => point, fitView: flowState.fitView }),
  };
});

import { IngestionDeviceGroupCanvas } from "../src/acquisition/IngestionDeviceGroupCanvas";

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

beforeEach(() => {
  flowState.fitView.mockReset();
  vi.stubGlobal("requestAnimationFrame", callback => setTimeout(callback, 0));
  vi.stubGlobal("cancelAnimationFrame", handle => clearTimeout(handle));
});

describe("采集画布布局恢复", () => {
  it("重置自动布局恢复派生坐标、适配全图并保留任务配置", async () => {
    const devices = [
      {
        key: "device-1",
        form: {
          ...createIngestionTaskForm(),
          taskId: "press-01",
          version: 1,
          name: "压力机一号",
          status: "draft",
          protocol: "http-polling",
          edgeId: "edge-1",
          dataModel: "",
          subjectType: "equipment",
          subjectId: "PRESS-01",
          equipmentId: "PRESS-01",
          deviceProfile: { kind: "plc" },
          httpPolling: { baseUrl: "http://plc.local", snapshotPath: "/snapshot", pollIntervalMs: 1000 },
          valueMappings: [],
        },
      },
      {
        key: "device-2",
        form: {
          ...createIngestionTaskForm(),
          taskId: "press-02",
          version: 1,
          name: "压力机二号",
          status: "draft",
          protocol: "http-polling",
          edgeId: "edge-1",
          dataModel: "",
          subjectType: "equipment",
          subjectId: "PRESS-02",
          equipmentId: "PRESS-02",
          deviceProfile: { kind: "plc" },
          httpPolling: { baseUrl: "http://plc.local", snapshotPath: "/snapshot", pollIntervalMs: 1000 },
          valueMappings: [],
        },
      },
    ];
    const onAssignEdge = vi.fn();
    const onAssignModel = vi.fn();
    render(<IngestionDeviceGroupCanvas
      devices={devices}
      edges={[]}
      models={[]}
      activeDeviceKey="device-1"
      inspectorPanels={{ device: null, connection: null, mappings: null, advanced: null }}
      onAssignEdge={onAssignEdge}
      onAssignModel={onAssignModel}
    />);

    const positions = screen.getByTestId("canvas-node-positions");
    await waitFor(() => expect(JSON.parse(positions.textContent)).toHaveLength(1));
    fireEvent.change(screen.getByLabelText("点位映射支路筛选").querySelector("select"), { target: { value: "all" } });
    await waitFor(() => expect(JSON.parse(positions.textContent)).toHaveLength(2));
    const autoPositions = JSON.parse(positions.textContent).map(node => ({ id: node.id, position: node.position }));
    fireEvent.click(screen.getByRole("button", { name: "模拟移动 task:device-1" }));
    await waitFor(() => expect(JSON.parse(positions.textContent).find(node => node.id === "task:device-1").position).toEqual({ x: 901, y: 902 }));

    flowState.fitView.mockClear();
    fireEvent.click(screen.getByRole("button", { name: "重置自动布局" }));

    await waitFor(() => expect(JSON.parse(positions.textContent)).toEqual(autoPositions));
    await waitFor(() => expect(flowState.fitView).toHaveBeenCalledWith(expect.objectContaining({
      nodes: [{ id: "task:device-1" }, { id: "task:device-2" }],
      padding: 0.08,
    })));
    expect(devices[0].form).toMatchObject({ taskId: "press-01", subjectId: "PRESS-01", edgeId: "edge-1" });
    expect(devices[1].form).toMatchObject({ taskId: "press-02", subjectId: "PRESS-02", edgeId: "edge-1" });
    expect(onAssignEdge).not.toHaveBeenCalled();
    expect(onAssignModel).not.toHaveBeenCalled();
  });

  it("为映射和探查分页提供可区分的屏幕阅读器名称", async () => {
    const devices = [{
      key: "device-1",
      form: {
        ...createIngestionTaskForm(),
        taskId: "press-01",
        name: "压力机一号",
        protocol: "http-polling",
        valueMappings: Array.from({ length: 9 }, (_, index) => ({
          sourcePath: `/points/${index + 1}`,
          dataItemCode: "",
        })),
      },
    }];
    const probe = {
      catalogCurrent: true,
      points: Array.from({ length: 9 }, (_, index) => ({
        name: `探查点 ${index + 1}`,
        path: `/discovered/${index + 1}`,
      })),
    };

    render(<IngestionDeviceGroupCanvas
      devices={devices}
      edges={[]}
      models={[]}
      activeDeviceKey="device-1"
      inspectorPanels={{ device: null, connection: null, mappings: null, advanced: null }}
      probe={probe}
    />);

    const mappingPagination = screen.getByRole("navigation", { name: "点位映射分页" });
    const probePagination = screen.getByRole("navigation", { name: "未映射探查点位分页" });
    expect(within(mappingPagination).getByRole("button", { name: "点位映射：上一批" })).toBeDisabled();
    expect(within(probePagination).getByRole("button", { name: "未映射探查点位：上一批" })).toBeDisabled();

    fireEvent.click(within(mappingPagination).getByRole("button", { name: "点位映射：下一批" }));
    fireEvent.click(within(probePagination).getByRole("button", { name: "未映射探查点位：下一批" }));
    await waitFor(() => {
      expect(within(mappingPagination).getByText("2/2")).toBeInTheDocument();
      expect(within(probePagination).getByText("2/2")).toBeInTheDocument();
    });
  });

  it("在设备未选中时仍显示该设备本会话的探查验证状态", async () => {
    const devices = [
      {
        key: "device-1",
        probeVerified: true,
        form: { ...createIngestionTaskForm(), taskId: "press-01", name: "压力机一号", valueMappings: [] },
      },
      {
        key: "device-2",
        probeVerified: false,
        form: { ...createIngestionTaskForm(), taskId: "press-02", name: "压力机二号", valueMappings: [] },
      },
    ];

    render(<IngestionDeviceGroupCanvas
      devices={devices}
      edges={[]}
      models={[]}
      activeDeviceKey="device-2"
      inspectorPanels={{ device: null, connection: null, mappings: null, advanced: null }}
    />);
    fireEvent.change(screen.getByLabelText("点位映射支路筛选").querySelector("select"), { target: { value: "all" } });

    const verifiedNode = [...document.querySelectorAll(".acq-flow-node--task")]
      .find(node => node.querySelector(".acq-flow-node__title")?.textContent === "压力机一号");
    expect(verifiedNode).toBeTruthy();
    expect(within(verifiedNode).getByText("本会话探查通过")).toBeInTheDocument();
  });
});
