// Verify bounded mapping-table rendering, cross-page selection, and validation focus navigation.
import React, { useState } from "react";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { PointMappingPanel, blankRow } from "../src/acquisition/panels/PointMappingPanel";
import { createIngestionTaskForm } from "../src/acquisition/ingestionTaskForm";
import { pointMappingsToCsv } from "../src/acquisition/pointMappingCsv";
import { protocolDescriptor } from "../src/acquisition/protocolRegistry";

const descriptor = protocolDescriptor("http-polling");
const options = [{ code: "temperature", displayName: "温度", nullable: false, unit: "°C", dataType: "double" }];
let originalScrollIntoView;

function createRows(count, incompleteIndex = -1) {
  return Array.from({ length: count }, (_, index) => ({
    ...blankRow(descriptor),
    dataItemCode: "temperature",
    sourcePath: (Array.isArray(incompleteIndex) ? incompleteIndex.includes(index) : index === incompleteIndex)
      ? "" : `/temperature/${index + 1}`,
    sourceUnit: "°C",
    sourceDataType: "double",
  }));
}

function createUniqueRows(count, incompleteIndexes = []) {
  const incomplete = new Set(incompleteIndexes);
  return Array.from({ length: count }, (_, index) => ({
    ...blankRow(descriptor),
    dataItemCode: `point-${index + 1}`,
    sourcePath: incomplete.has(index) ? "" : `/temperature/${index + 1}`,
    sourceUnit: "°C",
    sourceDataType: blankRow(descriptor).sourceDataType,
  }));
}

function createUniqueOptions(count) {
  return Array.from({ length: count }, (_, index) => ({
    code: `point-${index + 1}`,
    displayName: `点位 ${index + 1}`,
    nullable: true,
    unit: "°C",
    dataType: "double",
  }));
}

function MappingPanelHarness({ initialRows, ...panelProps }) {
  const [rows, setRows] = useState(initialRows);
  const form = createIngestionTaskForm({
    taskId: "mapping-test",
    name: "映射分页测试",
    edgeId: "edge-1",
    dataModelId: "model-1",
    subjectId: "machine-1",
    protocol: descriptor.id,
    httpPolling: { baseUrl: "http://device.local", snapshotPath: "/snapshot", pollIntervalMs: 1000 },
    valueMappings: initialRows,
  });
  return <PointMappingPanel
    title="点位映射"
    descriptor={descriptor}
    rows={rows}
    options={options}
    errors={{}}
    errorPrefix="valueMappings"
    probe={null}
    form={form}
    readOnly={false}
    onChange={setRows}
    {...panelProps}
  />;
}

function installAnimationFrame() {
  vi.stubGlobal("requestAnimationFrame", callback => setTimeout(callback, 0));
  vi.stubGlobal("cancelAnimationFrame", handle => clearTimeout(handle));
  originalScrollIntoView = HTMLElement.prototype.scrollIntoView;
  Object.defineProperty(HTMLElement.prototype, "scrollIntoView", { configurable: true, value: vi.fn() });
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  if (originalScrollIntoView)
    Object.defineProperty(HTMLElement.prototype, "scrollIntoView", { configurable: true, value: originalScrollIntoView });
  else
    delete HTMLElement.prototype.scrollIntoView;
});

beforeEach(() => installAnimationFrame());

describe("点位映射表分页", () => {
  it("10,000 行只渲染当前 50 行，并保持跨页选择", async () => {
    render(<MappingPanelHarness initialRows={createRows(10_000)} />);

    expect(screen.getAllByRole("combobox", { name: /第 \d+ 行平台变量/ })).toHaveLength(50);
    expect(screen.getByText("每页 50 条 · 第 1 / 200 页 · 显示 1–50 / 10000 条筛选结果")).toBeInTheDocument();
    expect(screen.queryByLabelText("选择第 51 行")).not.toBeInTheDocument();

    fireEvent.click(screen.getByLabelText("选择第 1 行"));
    const pagination = screen.getByRole("navigation", { name: "点位映射表分页" });
    fireEvent.click(within(pagination).getByRole("button", { name: "点位映射：下一页" }));

    expect(await screen.findByLabelText("选择第 51 行")).not.toBeChecked();
    fireEvent.click(screen.getByLabelText("选择第 51 行"));
    expect(screen.getByText(/已选 2 行（可跨页）/)).toBeInTheDocument();
    expect(within(pagination).getByText("每页 50 条 · 第 2 / 200 页 · 显示 51–100 / 10000 条筛选结果")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "选择全部筛选结果（10000 行）" }));
    expect(screen.getByRole("button", { name: "删除所选 10000" })).toBeInTheDocument();
    expect(screen.getByLabelText("选择第 51 行")).toBeChecked();
    fireEvent.click(within(pagination).getByRole("button", { name: "点位映射：上一页" }));
    expect(await screen.findByLabelText("选择第 1 行")).toBeChecked();
  });

  it("定位指定映射前先切换到目标页，再把焦点放到变量选择器", async () => {
    const onFocusRowComplete = vi.fn();
    render(<MappingPanelHarness
      initialRows={createRows(130)}
      focusRowRequest={{ index: 123, revision: 42 }}
      onFocusRowComplete={onFocusRowComplete}
    />);

    const target = await screen.findByRole("combobox", { name: "第 124 行平台变量" });
    await waitFor(() => expect(target).toHaveFocus());
    expect(screen.getByText("每页 50 条 · 第 3 / 3 页 · 显示 101–130 / 130 条筛选结果")).toBeInTheDocument();
    expect(onFocusRowComplete).toHaveBeenCalledWith(42);
  });

  it("聚焦未完成项时先筛选，再定位第一条未完成映射", async () => {
    const rows = createUniqueRows(130, [87]);
    render(<MappingPanelHarness initialRows={rows} options={createUniqueOptions(130)} focusIncompleteRevision={7} />);

    const target = await screen.findByRole("combobox", { name: "第 88 行平台变量" });
    await waitFor(() => expect(target).toHaveFocus());
    expect(screen.getByText("每页 50 条 · 第 1 / 1 页 · 显示 1–1 / 1 条筛选结果")).toBeInTheDocument();
    expect(screen.getByText("已筛选 1/130 行 · 已选 0 行（可跨页）")).toBeInTheDocument();
  });

  it("删除末页最后一行后自动回到仍有数据的页", async () => {
    render(<MappingPanelHarness initialRows={createRows(51)} />);
    const pagination = screen.getByRole("navigation", { name: "点位映射表分页" });
    fireEvent.click(within(pagination).getByRole("button", { name: "点位映射：下一页" }));
    fireEvent.click(await screen.findByLabelText("选择第 51 行"));
    fireEvent.click(screen.getByRole("button", { name: "删除所选 1" }));

    await waitFor(() => expect(within(pagination).getByText("每页 50 条 · 第 1 / 1 页 · 显示 1–50 / 50 条筛选结果")).toBeInTheDocument());
    expect(within(pagination).getByRole("button", { name: "点位映射：下一页" })).toBeDisabled();
  });

  it("添加点位时清除筛选并切换到新行所在页", async () => {
    render(<MappingPanelHarness initialRows={createRows(50)} />);
    fireEvent.change(screen.getByLabelText("搜索映射"), { target: { value: "not-there" } });
    fireEvent.change(screen.getByLabelText("状态"), { target: { value: "needs-attention" } });

    fireEvent.click(screen.getByRole("button", { name: "添加点位" }));

    expect(await screen.findByLabelText("选择第 51 行")).toBeInTheDocument();
    expect(screen.getByLabelText("搜索映射")).toHaveValue("");
    expect(screen.getByLabelText("状态")).toHaveValue("all");
    expect(screen.getByText("每页 50 条 · 第 2 / 2 页 · 显示 51–51 / 51 条筛选结果")).toBeInTheDocument();
  });

  it("添加缺失必需变量时定位到新行所在的筛选页", async () => {
    const rows = createUniqueRows(120, Array.from({ length: 60 }, (_, index) => index));
    const modelOptions = [...createUniqueOptions(120), {
      code: "pressure", displayName: "压力", nullable: false, unit: "kPa", dataType: "double",
    }];
    render(<MappingPanelHarness initialRows={rows} options={modelOptions} />);

    fireEvent.click(screen.getByRole("button", { name: "添加缺失的 1 个必需变量" }));

    const target = await screen.findByRole("combobox", { name: "第 121 行平台变量" });
    await waitFor(() => expect(target).toHaveFocus());
    expect(screen.getByText("每页 50 条 · 第 2 / 2 页 · 显示 51–61 / 61 条筛选结果")).toBeInTheDocument();
  });

  it("应用 CSV 后清除筛选并回到第一页", async () => {
    const rows = createUniqueRows(51);
    const modelOptions = createUniqueOptions(51);
    const { container } = render(<MappingPanelHarness initialRows={rows} options={modelOptions} />);
    const pagination = screen.getByRole("navigation", { name: "点位映射表分页" });
    fireEvent.click(within(pagination).getByRole("button", { name: "点位映射：下一页" }));
    fireEvent.change(screen.getByLabelText("状态"), { target: { value: "mapped" } });
    fireEvent.change(screen.getByLabelText("搜索映射"), { target: { value: "temperature" } });
    fireEvent.click(within(pagination).getByRole("button", { name: "点位映射：下一页" }));
    expect(within(pagination).getByText("每页 50 条 · 第 2 / 2 页 · 显示 51–51 / 51 条筛选结果")).toBeInTheDocument();

    const csv = pointMappingsToCsv([rows[0]], descriptor);
    const file = new File([csv], "mapping.csv", { type: "text/csv" });
    Object.defineProperty(file, "arrayBuffer", {
      value: async () => new TextEncoder().encode(csv).buffer,
    });
    fireEvent.change(container.querySelector('input[type="file"]'), { target: { files: [file] } });
    await screen.findByText(/mapping\.csv · 导入预览/);
    fireEvent.click(screen.getByRole("button", { name: "替换全部映射" }));
    vi.spyOn(window, "confirm").mockReturnValue(true);
    fireEvent.click(screen.getByRole("button", { name: "应用 1 个映射" }));

    expect(await screen.findByText("每页 50 条 · 第 1 / 1 页 · 显示 1–1 / 1 条筛选结果")).toBeInTheDocument();
    expect(screen.getByLabelText("搜索映射")).toHaveValue("");
    expect(screen.getByLabelText("状态")).toHaveValue("all");
  });
});
