// Verify manual canvas placement can be restored without changing acquisition task data.

import React from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createIngestionTaskForm } from "../src/acquisition/ingestionTaskForm";
import { ingestionCanvasLayoutStorageKey } from "../src/acquisition/ingestionCanvasLayout";

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
    ReactFlow: ({ nodes = [], nodeTypes = {}, onNodesChange, onNodeDragStop, onNodeClick, onNodeContextMenu }) => ReactRuntime.createElement(ReactRuntime.Fragment, null,
      ReactRuntime.createElement("output", { "data-testid": "canvas-node-positions" }, JSON.stringify(
        nodes.map(node => ({ id: node.id, position: node.position })),
      )),
      ReactRuntime.createElement("output", { "data-testid": "canvas-selected-nodes" }, JSON.stringify(
        nodes.filter(node => node.selected).map(node => node.id),
      )),
      ...nodes.map(node => ReactRuntime.createElement("button", {
        key: node.id,
        type: "button",
        onClick: () => onNodeDragStop?.(null, { ...node, position: { x: 901, y: 902 } }),
      }, `模拟移动 ${node.id}`)),
      ...nodes.map(node => ReactRuntime.createElement("button", {
        key: `select:${node.id}`,
        type: "button",
        onClick: event => {
          onNodesChange?.([{ type: "select", id: node.id, selected: true }]);
          onNodeClick?.(event, node);
        },
      }, `选择 ${node.id}`)),
      ...nodes.map(node => ReactRuntime.createElement("button", {
        key: `context:${node.id}`,
        type: "button",
        onClick: () => onNodeContextMenu?.({
          preventDefault: () => {},
          stopPropagation: () => {},
          clientX: 10,
          clientY: 10,
        }, node),
      }, `打开菜单 ${node.id}`)),
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
      const onNodesChange = changes => setNodes(current => changes.reduce((result, change) => {
        if (change.type !== "select") return result;
        return result.map(node => ({
          ...node,
          selected: node.id === change.id ? change.selected : change.selected ? false : node.selected,
        }));
      }, current));
      return [nodes, setNodes, onNodesChange];
    },
    useReactFlow: () => ({ screenToFlowPosition: point => point, fitView: flowState.fitView }),
  };
});

import { IngestionDeviceGroupCanvas } from "../src/acquisition/IngestionDeviceGroupCanvas";

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

beforeEach(() => {
  localStorage.clear();
  flowState.fitView.mockReset();
  vi.stubGlobal("requestAnimationFrame", callback => setTimeout(callback, 0));
  vi.stubGlobal("cancelAnimationFrame", handle => clearTimeout(handle));
});

function stableTaskDevice() {
  const form = createIngestionTaskForm();
  return {
    key: "single-device",
    form: {
      ...form,
      taskId: "press-01",
      version: 1,
      name: "压力机一号",
      status: "draft",
      protocol: "http-polling",
      edgeId: "edge-1",
      dataModel: "model-1::1",
      subjectType: "equipment",
      subjectId: "PRESS-01",
      valueMappings: [{ ...form.valueMappings[0], dataItemCode: "", sourcePath: "/temperature" }],
    },
  };
}

function stableTaskCanvas(layoutStorageKey = "", layoutPersistenceStatus = "session") {
  return <IngestionDeviceGroupCanvas
    layoutStorageKey={layoutStorageKey}
    layoutPersistenceStatus={layoutPersistenceStatus}
    devices={[stableTaskDevice()]}
    edges={[{ edgeId: "edge-1", siteId: "SITE-001", hostname: "现场节点一" }]}
    models={[{ modelId: "model-1", version: 1, name: "工艺模型一", status: "published", acquisition: { dataItems: [] } }]}
    activeDeviceKey="single-device"
    inspectorPanels={{ device: null, connection: null, mappings: null, advanced: null }}
  />;
}

function renderStableTaskCanvas(layoutStorageKey, layoutPersistenceStatus) {
  return render(stableTaskCanvas(layoutStorageKey, layoutPersistenceStatus));
}

describe("采集画布布局恢复", () => {
  it("只为已确认站点下的现有维护版本生成隔离的布局键", () => {
    const base = {
      taskId: "press-01", version: 2, mode: "maintain", siteId: "SITE-001",
      siteResolved: true, siteError: false, edgeScopeMismatch: false, isNew: false,
    };
    const original = ingestionCanvasLayoutStorageKey(base);
    expect(original).toBe("ingot:acquisition-canvas-layout:v1:SITE-001:press-01:v2");
    expect(ingestionCanvasLayoutStorageKey({ ...base, siteId: "SITE-002" })).not.toBe(original);
    expect(ingestionCanvasLayoutStorageKey({ ...base, taskId: "press-02" })).not.toBe(original);
    expect(ingestionCanvasLayoutStorageKey({ ...base, version: 3 })).not.toBe(original);
    expect(ingestionCanvasLayoutStorageKey({ ...base, isNew: true })).toBe("");
    expect(ingestionCanvasLayoutStorageKey({ ...base, mode: "version" })).toBe("");
    expect(ingestionCanvasLayoutStorageKey({ ...base, siteResolved: false })).toBe("");
    expect(ingestionCanvasLayoutStorageKey({ ...base, siteError: true })).toBe("");
    expect(ingestionCanvasLayoutStorageKey({ ...base, edgeScopeMismatch: true })).toBe("");
  });

  it("跨重挂载恢复稳定任务关系节点，点位支路维持自动位置，重置会清除持久布局", async () => {
    const layoutStorageKey = ingestionCanvasLayoutStorageKey({
      taskId: "press-01", version: 1, mode: "maintain", siteId: "SITE-001",
      siteResolved: true, siteError: false, edgeScopeMismatch: false, isNew: false,
    });
    const renderWorkspace = () => renderStableTaskCanvas(layoutStorageKey, "persistent");
    renderWorkspace();
    fireEvent.click(screen.getByRole("button", { name: "显示关联 Edge / 模型" }));

    let positions = screen.getByTestId("canvas-node-positions");
    await waitFor(() => expect(JSON.parse(positions.textContent)).toHaveLength(4));
    const automaticPositions = JSON.parse(positions.textContent);
    const automaticMappingPosition = automaticPositions.find(node => node.id === "mapping:single-device:0").position;
    for (const nodeId of ["edge:edge-1", "task:single-device", "model:model-1%3A%3A1"]) {
      fireEvent.click(screen.getByRole("button", { name: `模拟移动 ${nodeId}` }));
    }
    fireEvent.click(screen.getByRole("button", { name: "模拟移动 mapping:single-device:0" }));
    await waitFor(() => expect(JSON.parse(positions.textContent).every(node =>
      node.id === "mapping:single-device:0" || (node.position.x === 901 && node.position.y === 902))).toBe(true));

    const stored = JSON.parse(localStorage.getItem(layoutStorageKey));
    expect(stored.version).toBe(1);
    expect(Object.keys(stored.positions)).toEqual(["edge:edge-1", "task:single-device", "model:model-1%3A%3A1"]);
    cleanup();

    renderWorkspace();
    fireEvent.click(screen.getByRole("button", { name: "显示关联 Edge / 模型" }));
    positions = screen.getByTestId("canvas-node-positions");
    await waitFor(() => expect(JSON.parse(positions.textContent).find(node => node.id === "task:single-device").position)
      .toEqual({ x: 901, y: 902 }));
    expect(JSON.parse(positions.textContent).find(node => node.id === "mapping:single-device:0").position)
      .toEqual(automaticMappingPosition);
    expect(screen.getByText(/任务、Edge 和模型位置保存在此浏览器/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "重置自动布局" }));
    await waitFor(() => expect(localStorage.getItem(layoutStorageKey)).toBeNull());
    await waitFor(() => expect(JSON.parse(positions.textContent)).toEqual(automaticPositions));
    cleanup();

    renderWorkspace();
    fireEvent.click(screen.getByRole("button", { name: "显示关联 Edge / 模型" }));
    positions = screen.getByTestId("canvas-node-positions");
    await waitFor(() => expect(JSON.parse(positions.textContent)).toEqual(automaticPositions));
  });

  it("忽略损坏或越界的位置数据，并在浏览器存储不可用时继续当前会话操作", async () => {
    const layoutStorageKey = "ingot:acquisition-canvas-layout:v1:SITE-001:press-01:v1";
    localStorage.setItem(layoutStorageKey, "{invalid-json");
    renderStableTaskCanvas(layoutStorageKey, "persistent");
    let positions = screen.getByTestId("canvas-node-positions");
    await waitFor(() => expect(JSON.parse(positions.textContent)).toHaveLength(2));
    expect(JSON.parse(positions.textContent).find(node => node.id === "task:single-device").position)
      .toEqual({ x: 390, y: 64 });
    cleanup();

    localStorage.setItem(layoutStorageKey, JSON.stringify({ version: 1, positions: {
      "task:single-device": { x: 100001, y: 64 },
      "mapping:single-device:0": { x: 901, y: 902 },
    } }));
    renderStableTaskCanvas(layoutStorageKey, "persistent");
    positions = screen.getByTestId("canvas-node-positions");
    await waitFor(() => expect(JSON.parse(positions.textContent).find(node => node.id === "task:single-device").position)
      .toEqual({ x: 390, y: 64 }));
    cleanup();

    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => { throw new Error("storage unavailable"); });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("storage unavailable"); });
    vi.spyOn(Storage.prototype, "removeItem").mockImplementation(() => { throw new Error("storage unavailable"); });
    renderStableTaskCanvas(layoutStorageKey, "persistent");
    positions = screen.getByTestId("canvas-node-positions");
    expect(screen.getByText(/浏览器存储不可用/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "模拟移动 task:single-device" }));
    await waitFor(() => expect(JSON.parse(positions.textContent).find(node => node.id === "task:single-device").position)
      .toEqual({ x: 901, y: 902 }));
  });

  it("清理持久布局失败时仍恢复自动坐标并提示仅保留当前会话", async () => {
    const layoutStorageKey = "ingot:acquisition-canvas-layout:v1:SITE-001:press-01:v1";
    localStorage.setItem(layoutStorageKey, JSON.stringify({ version: 1, positions: {
      "task:single-device": { x: 901, y: 902 },
    } }));
    renderStableTaskCanvas(layoutStorageKey, "persistent");

    const positions = screen.getByTestId("canvas-node-positions");
    await waitFor(() => expect(JSON.parse(positions.textContent).find(node => node.id === "task:single-device").position)
      .toEqual({ x: 901, y: 902 }));
    vi.spyOn(Storage.prototype, "removeItem").mockImplementation(() => { throw new Error("storage unavailable"); });

    fireEvent.click(screen.getByRole("button", { name: "重置自动布局" }));

    await waitFor(() => expect(JSON.parse(positions.textContent).find(node => node.id === "task:single-device").position)
      .toEqual({ x: 390, y: 64 }));
    expect(screen.getByText(/浏览器存储不可用；节点布局仅保留在当前会话/)).toBeInTheDocument();
  });

  it("不同站点的布局键不会互相恢复位置", async () => {
    const keyFor = siteId => ingestionCanvasLayoutStorageKey({
      taskId: "press-01", version: 1, mode: "maintain", siteId,
      siteResolved: true, siteError: false, edgeScopeMismatch: false, isNew: false,
    });
    const siteAKey = keyFor("SITE-A");
    localStorage.setItem(siteAKey, JSON.stringify({ version: 1, positions: { "task:single-device": { x: 901, y: 902 } } }));
    renderStableTaskCanvas(keyFor("SITE-B"), "persistent");

    const positions = screen.getByTestId("canvas-node-positions");
    await waitFor(() => expect(JSON.parse(positions.textContent).find(node => node.id === "task:single-device").position)
      .toEqual({ x: 390, y: 64 }));
  });

  it("站点身份确认前锁定布局重置，异步载入已存位置后重新适配视口", async () => {
    const layoutStorageKey = "ingot:acquisition-canvas-layout:v1:SITE-001:press-01:v1";
    localStorage.setItem(layoutStorageKey, JSON.stringify({ version: 1, positions: {
      "task:single-device": { x: 901, y: 902 },
    } }));
    const view = renderStableTaskCanvas("", "resolving");
    const positions = screen.getByTestId("canvas-node-positions");
    await waitFor(() => expect(JSON.parse(positions.textContent)).toHaveLength(2));
    expect(screen.getByRole("button", { name: "重置自动布局" })).toBeDisabled();
    expect(screen.getByText(/正在确认任务站点范围/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "模拟移动 task:single-device" }));
    expect(JSON.parse(positions.textContent).find(node => node.id === "task:single-device").position)
      .toEqual({ x: 390, y: 64 });

    flowState.fitView.mockClear();
    view.rerender(stableTaskCanvas(layoutStorageKey, "persistent"));
    await waitFor(() => expect(JSON.parse(positions.textContent).find(node => node.id === "task:single-device").position)
      .toEqual({ x: 901, y: 902 }));
    await waitFor(() => expect(flowState.fitView).toHaveBeenCalledWith(expect.objectContaining({
      nodes: [{ id: "task:single-device" }, { id: "mapping:single-device:0" }],
      padding: 0.08,
    })));
  });

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
    expect(localStorage.length).toBe(0);

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
    const rejectUnnecessaryTaskSelection = vi.fn(() => false);

    render(<IngestionDeviceGroupCanvas
      devices={devices}
      edges={[]}
      models={[]}
      activeDeviceKey="device-1"
      inspectorPanels={{ device: null, connection: null, mappings: null, advanced: null }}
      probe={probe}
      onSelect={rejectUnnecessaryTaskSelection}
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

    fireEvent.click(screen.getByRole("button", { name: /查看待映射点位清单/ }));
    await waitFor(() => expect(within(probePagination).getByText("1/2")).toBeInTheDocument());
    const probeList = screen.getByRole("region", { name: "当前任务未映射探查点列表" });
    await waitFor(() => expect(probeList).toHaveFocus());
    expect(rejectUnnecessaryTaskSelection).not.toHaveBeenCalled();
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

  it("为触屏和键盘用户提供对象菜单入口与键盘导航", async () => {
    const devices = [{
      key: "device-1",
      form: {
        ...createIngestionTaskForm(),
        taskId: "press-01",
        version: 1,
        name: "压力机一号",
        status: "draft",
        protocol: "http-polling",
        edgeId: "edge-1",
        subjectType: "equipment",
        subjectId: "PRESS-01",
        httpPolling: { baseUrl: "http://plc.local", snapshotPath: "/snapshot", pollIntervalMs: 1000 },
        valueMappings: [],
      },
    }];
    render(<IngestionDeviceGroupCanvas
      devices={devices}
      edges={[]}
      models={[]}
      activeDeviceKey="device-1"
      inspectorPanels={{ device: null, connection: null, mappings: null, advanced: null }}
    />);

    fireEvent.click(screen.getByRole("button", { name: "选择 task:device-1" }));
    const trigger = screen.getByRole("button", { name: "打开所选对象的更多操作" });
    expect(trigger).toBeEnabled();
    fireEvent.click(trigger);

    const firstAction = await screen.findByRole("menuitem", { name: "设备信息" });
    const secondAction = screen.getByRole("menuitem", { name: "通信驱动与连接" });
    expect(firstAction).toHaveFocus();
    fireEvent.keyDown(firstAction, { key: "ArrowDown" });
    expect(secondAction).toHaveFocus();
    fireEvent.keyDown(secondAction, { key: "Escape" });

    await waitFor(() => expect(screen.queryByRole("menu", { name: "画布对象操作" })).not.toBeInTheDocument());
    await waitFor(() => expect(trigger).toHaveFocus());

    fireEvent.click(trigger);
    fireEvent.click(await screen.findByRole("menuitem", { name: "设备信息" }));
    const deviceTab = screen.getByRole("tab", { name: "设备" });
    await waitFor(() => expect(screen.queryByRole("menu", { name: "画布对象操作" })).not.toBeInTheDocument());
    await waitFor(() => expect(deviceTab).toHaveFocus());
  });

  it("页面拒绝切换任务时保留画布和检查器的当前选择", async () => {
    const devices = [
      { key: "device-1", form: { ...createIngestionTaskForm(), taskId: "press-01", name: "压力机一号" } },
      { key: "device-2", form: {
        ...createIngestionTaskForm(),
        taskId: "press-02",
        name: "压力机二号",
        valueMappings: [{ sourcePath: "/points/temperature", dataItemCode: "" }],
      } },
    ];
    const onSelect = vi.fn(key => key === "device-2" ? false : true);
    const onFocusMappingRow = vi.fn(() => false);
    render(<IngestionDeviceGroupCanvas
      devices={devices}
      edges={[]}
      models={[]}
      activeDeviceKey="device-1"
      inspectorPanels={{ device: <p>压力机一号表单</p>, connection: null, mappings: null, advanced: null }}
      onSelect={onSelect}
      onFocusMappingRow={onFocusMappingRow}
    />);

    fireEvent.change(screen.getByLabelText("点位映射支路筛选").querySelector("select"), { target: { value: "all" } });
    await waitFor(() => expect(JSON.parse(screen.getByTestId("canvas-node-positions").textContent)).toHaveLength(3));
    fireEvent.click(screen.getByRole("button", { name: "选择 task:device-1" }));
    expect(screen.getByText("压力机一号表单")).toBeInTheDocument();
    const taskFilter = screen.getByLabelText("点位映射支路筛选").querySelector("select");
    fireEvent.change(taskFilter, { target: { value: "device-2" } });
    await waitFor(() => expect(taskFilter.value).toBe("device-1"));
    await waitFor(() => expect(JSON.parse(screen.getByTestId("canvas-node-positions").textContent)).toHaveLength(1));
    expect(document.querySelector(".acq-flow-node--task.is-selected")?.querySelector(".acq-flow-node__title")?.textContent)
      .toBe("压力机一号");
    expect(screen.getByText("压力机一号表单")).toBeInTheDocument();
    fireEvent.change(taskFilter, { target: { value: "all" } });
    await waitFor(() => expect(JSON.parse(screen.getByTestId("canvas-node-positions").textContent)).toHaveLength(3));
    fireEvent.click(screen.getByRole("button", { name: "选择 task:device-2" }));

    const firstNode = document.querySelector(".acq-flow-node--task.is-selected");
    expect(firstNode?.querySelector(".acq-flow-node__title")?.textContent).toBe("压力机一号");
    expect(JSON.parse(screen.getByTestId("canvas-selected-nodes").textContent)).toEqual(["task:device-1"]);
    expect(screen.getByText("压力机一号表单")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "选择 mapping:device-2:0" }));
    expect(document.querySelector(".acq-flow-node--task.is-selected")?.querySelector(".acq-flow-node__title")?.textContent)
      .toBe("压力机一号");
    expect(onFocusMappingRow).toHaveBeenCalledWith("device-2", 0);

    fireEvent.click(screen.getByRole("button", { name: "打开菜单 task:device-2" }));
    expect(document.querySelector(".acq-flow-node--task.is-selected")?.querySelector(".acq-flow-node__title")?.textContent)
      .toBe("压力机一号");
    fireEvent.click(await screen.findByRole("menuitem", { name: "设备信息" }));

    expect(document.querySelector(".acq-flow-node--task.is-selected")?.querySelector(".acq-flow-node__title")?.textContent)
      .toBe("压力机一号");
    expect(screen.getByText("压力机一号表单")).toBeInTheDocument();
    expect(onSelect).toHaveBeenCalledWith("device-2");

    fireEvent.click(screen.getByRole("button", { name: "打开菜单 mapping:device-2:0" }));
    fireEvent.click(await screen.findByRole("menuitem", { name: "编辑此点位映射" }));
    expect(document.querySelector(".acq-flow-node--task.is-selected")?.querySelector(".acq-flow-node__title")?.textContent)
      .toBe("压力机一号");
    expect(screen.getByText("压力机一号表单")).toBeInTheDocument();
  });

  it("页面接受任务筛选后同步选中节点与检查器", async () => {
    const devices = [
      { key: "device-1", form: { ...createIngestionTaskForm(), taskId: "press-01", name: "压力机一号" } },
      { key: "device-2", form: { ...createIngestionTaskForm(), taskId: "press-02", name: "压力机二号" } },
    ];
    function SelectionHarness() {
      const [activeDeviceKey, setActiveDeviceKey] = React.useState("device-1");
      const activeDevice = devices.find(device => device.key === activeDeviceKey);
      return <IngestionDeviceGroupCanvas
        devices={devices}
        edges={[]}
        models={[]}
        activeDeviceKey={activeDeviceKey}
        inspectorPanels={{ device: <p>{activeDevice.form.name}表单</p>, connection: null, mappings: null, advanced: null }}
        onSelect={key => { setActiveDeviceKey(key); return true; }}
      />;
    }
    render(<SelectionHarness />);

    fireEvent.click(screen.getByRole("button", { name: "选择 task:device-1" }));
    const taskFilter = screen.getByLabelText("点位映射支路筛选").querySelector("select");
    fireEvent.change(taskFilter, { target: { value: "device-2" } });

    await waitFor(() => expect(JSON.parse(screen.getByTestId("canvas-node-positions").textContent)).toHaveLength(1));
    expect(document.querySelector(".acq-flow-node--task.is-selected")?.querySelector(".acq-flow-node__title")?.textContent)
      .toBe("压力机二号");
    expect(screen.getByText("压力机二号表单")).toBeInTheDocument();
    expect(JSON.parse(screen.getByTestId("canvas-selected-nodes").textContent)).toEqual(["task:device-2"]);
  });
});
