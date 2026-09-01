// 验证生产运维页面的授权、分页、错误和业务状态呈现。
import React from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";
import { SystemStatusIndicator } from "../src/App";
import { mergeRunIssues } from "../src/pages/OperationsPages";
import { PlatformUptimeMetric } from "../src/pages/AdministrationPages";
import { ConfigurationHubPage, ProcessSpecificationsPage } from "../src/pages/RegistryPages";
import { DataTable, EmptyState, Field, Input } from "../src/ui/components";

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

function jsonResponse(payload) {
  return new Response(JSON.stringify(payload), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}

describe("生产界面状态反馈", () => {
  it("每秒刷新平台运行时间而不等待指标轮询", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(10_000));
    render(<PlatformUptimeMetric startedAtSeconds={5} />);

    expect(screen.getByText("00:00:05")).toBeInTheDocument();
    act(() => vi.advanceTimersByTime(1000));
    expect(screen.getByText("00:00:06")).toBeInTheDocument();
  });

  it("使用 TanStack Table 对业务列排序并保持操作列不可排序", () => {
    render(
      <DataTable
        rows={[
          { id: "run-b", name: "批次 B", score: 2 },
          { id: "run-a", name: "批次 A", score: 1 },
          { id: "run-c", name: "批次 C", score: 3 },
        ]}
        columns={[
          { key: "name", label: "批次" },
          { key: "score", label: "评分", align: "right" },
          { key: "action", label: "操作" },
        ]}
      />,
    );

    expect(within(screen.getAllByRole("row")[1]).getByText("批次 B")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /操作/ })).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "批次：未排序，点击切换排序" }));
    expect(screen.getByRole("columnheader", { name: /批次/ })).toHaveAttribute("aria-sort", "ascending");
    expect(within(screen.getAllByRole("row")[1]).getByText("批次 A")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "批次：升序，点击切换排序" }));
    expect(screen.getByRole("columnheader", { name: /批次/ })).toHaveAttribute("aria-sort", "descending");
    expect(within(screen.getAllByRole("row")[1]).getByText("批次 C")).toBeInTheDocument();
  });

  it("按文案去重数据问题时保留最高严重度", () => {
    const issues = mergeRunIssues(
      [{ code: "process_data.unavailable", message: "过程数据不可用。", severity: "error" }],
      ["过程数据不可用。"],
    );

    expect(issues).toEqual([
      { code: "process_data.unavailable", message: "过程数据不可用。", severity: "error" },
    ]);

    expect(mergeRunIssues([
      { code: "sample_gap.warning", message: "采样存在断点。", severity: "warning" },
      { code: "sample_gap.error", message: "采样存在断点。", severity: "error" },
    ])).toEqual([
      { code: "sample_gap.error", message: "采样存在断点。", severity: "error" },
    ]);
  });

  it("不在表单下方显示常规辅助说明，但保留校验错误", () => {
    render(
      <Field label="工艺规范" hint="选择当前生产使用的已发布版本。" error="请选择工艺规范">
        <Input />
      </Field>,
    );

    expect(screen.getByText("选择当前生产使用的已发布版本。")).toHaveClass("sr-only");
    expect(screen.getByRole("alert")).toHaveTextContent("请选择工艺规范");
    expect(screen.getByRole("alert")).not.toHaveClass("sr-only");
  });

  it("在全局导航中展示平台与现场节点状态", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([
      { edgeId: "edge-01", lastSeen: new Date().toISOString() },
    ])));

    render(<MemoryRouter><SystemStatusIndicator /></MemoryRouter>);

    expect(await screen.findByRole("link", { name: "系统状态：平台正常，现场节点 1/1 在线" })).toHaveAttribute("href", "/platform-metrics");
  });

  it("让空状态承载诊断信息和下一步操作", () => {
    render(
      <EmptyState
        title="还没有形成生产运行"
        description="请先完成现场接入。"
        details={<span>现场节点 0/1 在线</span>}
        actions={<button type="button">查看现场节点</button>}
      />,
    );

    expect(screen.getByText("现场节点 0/1 在线")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "查看现场节点" })).toBeInTheDocument();
  });

  it("为未完成配置提供进度和可直达的操作", async () => {
    vi.stubGlobal("fetch", vi.fn().mockImplementation(() => Promise.resolve(jsonResponse([]))));

    render(<MemoryRouter><ConfigurationHubPage /></MemoryRouter>);

    await waitFor(() => expect(screen.getByRole("progressbar", { name: "配置准备进度" })).toHaveAttribute("aria-valuenow", "0"));
    expect(screen.getByText("还需完成 5 项")).toBeInTheDocument();
    expect(screen.getAllByText("待完成")).toHaveLength(5);
    screen.getAllByText("待完成").forEach(badge => expect(badge).toHaveClass("bg-amber-50"));
    expect(screen.getByRole("link", { name: /配置数据来源/ })).toHaveAttribute("href", "/configuration/ingestion-tasks");
    expect(screen.getByRole("link", { name: /发布配置/ })).toHaveAttribute("href", "/configuration/scenario-packages");
  });

  it("用规范状态值呈现准备度检查中的黄色徽标", () => {
    vi.stubGlobal("fetch", vi.fn().mockImplementation(() => new Promise(() => {})));

    render(<MemoryRouter><ConfigurationHubPage /></MemoryRouter>);

    expect(screen.getAllByText("检查中")).toHaveLength(5);
    screen.getAllByText("检查中").forEach(badge => expect(badge).toHaveClass("bg-amber-50"));
  });

  it("用规范状态值呈现无法检查的红色徽标", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("读取失败", { status: 503 })));

    render(<MemoryRouter><ConfigurationHubPage /></MemoryRouter>);

    const badges = await screen.findAllByText("无法检查");
    expect(badges).toHaveLength(5);
    badges.forEach(badge => expect(badge).toHaveClass("bg-rose-50"));
  });

  it("用规范状态值呈现已准备的绿色徽标", async () => {
    vi.stubGlobal("fetch", vi.fn().mockImplementation(url => Promise.resolve(jsonResponse(
      String(url).includes("/inspection-definitions") ? [{ definitionId: "definition-01" }] : [{ status: "published" }],
    ))));

    render(<MemoryRouter><ConfigurationHubPage /></MemoryRouter>);

    await waitFor(() => expect(screen.getByRole("progressbar", { name: "配置准备进度" })).toHaveAttribute("aria-valuenow", "5"));
    expect(screen.getAllByText("已准备")).toHaveLength(5);
    screen.getAllByText("已准备").forEach(badge => expect(badge).toHaveClass("bg-emerald-50"));
  });

  it("从已发布工艺规范的运行依据创建下一版草稿", async () => {
    const specification = {
      processSpecificationId: "spec-lens-a",
      version: 5,
      name: "镜片模压标准配方",
      basedOnVersion: 4,
      dataModelId: "model-lens",
      dataModelVersion: 2,
      status: "published",
      contextSelector: { product_family_code: "LENS" },
      values: [
        { code: "holding.temperature", value: 520 },
        { code: "holding.pressure", value: 18.5 },
      ],
      updatedAt: "2026-08-31T08:00:00Z",
    };
    let createdPayload;
    vi.stubGlobal("fetch", vi.fn().mockImplementation((url, options = {}) => {
      if (options.method === "POST") {
        createdPayload = JSON.parse(options.body);
        return Promise.resolve(jsonResponse(createdPayload));
      }
      if (String(url).includes("/process-specifications")) return Promise.resolve(jsonResponse([specification]));
      if (String(url).includes("/process-data-models")) return Promise.resolve(jsonResponse([{
        modelId: "model-lens",
        version: 2,
        controlParameters: [
          { code: "holding.temperature", displayName: "保压温度", dataType: "double", unit: "°C" },
          { code: "holding.pressure", displayName: "保压压力", dataType: "double", unit: "kN" },
        ],
      }]));
      if (String(url).includes("/process-executions")) return Promise.resolve(jsonResponse([
        { executionId: "RUN-005", processSpecificationId: "spec-lens-a", processSpecificationVersion: 5, qualityStatus: "PASS" },
        { executionId: "RUN-004", processSpecificationId: "spec-lens-a", processSpecificationVersion: 4, qualityStatus: "FAIL" },
        { executionId: "RUN-OTHER", processSpecificationId: "spec-other", processSpecificationVersion: 5, qualityStatus: "PASS" },
      ]));
      return Promise.resolve(jsonResponse([]));
    }));

    render(<MemoryRouter><ProcessSpecificationsPage /></MemoryRouter>);

    fireEvent.click(await screen.findByRole("button", { name: "下一版配方" }));
    expect(await screen.findByText("RUN-005")).toBeInTheDocument();
    expect(screen.queryByText("RUN-004")).toBeNull();
    expect(screen.queryByText("RUN-OTHER")).toBeNull();
    fireEvent.change(screen.getByLabelText("下一版 保压温度"), { target: { value: "525" } });
    fireEvent.click(screen.getByRole("button", { name: "创建 V6 草稿" }));

    await waitFor(() => expect(createdPayload).toEqual(expect.objectContaining({
      processSpecificationId: "spec-lens-a",
      version: 6,
      name: "镜片模压标准配方",
      status: "draft",
      basedOnVersion: 5,
      dataModelId: "model-lens",
      dataModelVersion: 2,
      contextSelector: { product_family_code: "LENS" },
      values: [
        { code: "holding.temperature", value: 525 },
        { code: "holding.pressure", value: 18.5 },
      ],
    })));
  });
});
