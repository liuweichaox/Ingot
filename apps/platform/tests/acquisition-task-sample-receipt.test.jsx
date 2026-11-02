// Verify task-version event receipts stay separate from verified sample frames and Edge-wide shipment evidence.
import React from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { IngestionTaskPage } from "../src/acquisition/IngestionTaskPage";

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

describe("采集任务 Platform 版本引用事件证据", () => {
  it("按任务版本显示匹配事件并说明不代表有效采样帧", async () => {
    const publishedTask = {
      taskId: "task-1",
      version: 3,
      name: "压力机一号采集",
      status: "published",
      edgeId: "edge-1",
      protocol: "http-polling",
      dataModelId: "model-1",
      dataModelVersion: 1,
      subjectType: "equipment",
      subjectId: "PRESS-01",
      equipmentId: "PRESS-01",
      httpPolling: { baseUrl: "http://plc.local", snapshotPath: "/snapshot", pollIntervalMs: 1000 },
      valueMappings: [{ dataItemCode: "temperature", sourcePath: "temperature", sourceDataType: "auto" }],
    };
    const sampleReceipt = {
      eventId: "019A7E0B-361D-7A39-9D2B-E6D84B2D68A1",
      ingestedAt: "2026-10-31T09:20:00Z",
      evidenceKind: "typed-sample-frame",
    };
    const requests = [];
    let receiptRequestCount = 0;
    let resolveReceiptRefresh;
    const receiptRefreshRequest = new Promise(resolve => { resolveReceiptRefresh = resolve; });
    const response = data => new Response(JSON.stringify(data), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    });
    vi.stubGlobal("fetch", vi.fn(url => {
      const path = String(url);
      requests.push(path);
      if (path.includes("/api/v1/ingestion-tasks?taskId=task-1"))
        return Promise.resolve(response({ data: [publishedTask] }));
      if (path.includes("/api/v1/ingestion-tasks/task-1/3/sample-receipt?eventId=")) {
        receiptRequestCount += 1;
        return receiptRequestCount === 1 ? Promise.resolve(response(sampleReceipt)) : receiptRefreshRequest;
      }
      if (path.includes("/api/v1/process-data-models"))
        return Promise.resolve(response({ data: [{
          modelId: "model-1",
          version: 1,
          status: "published",
          acquisition: { dataItems: [{ code: "temperature", dataType: "double", nullable: false }] },
          controlParameters: [],
        }] }));
      if (path.includes("/api/edges/edge-1/acquisition/status"))
        return Promise.resolve(response({
          enabled: true,
          state: "running",
          reportedAt: "2026-10-31T09:20:05Z",
          platformObservedAt: "2026-10-31T09:20:05Z",
          platformReceivedAt: "2026-10-31T09:20:04Z",
          supportsSampleEventIdCorrelation: true,
          tasks: [{ configurationKey: "task-1@3", state: "running", lastValidSnapshotAt: "2026-10-31T09:20:00Z", lastValidSnapshotEventId: sampleReceipt.eventId }],
          deployments: [{
            taskId: "task-1",
            desiredVersion: 3,
            desiredConfigurationHash: "hash-3",
            appliedVersion: 3,
            appliedConfigurationHash: "hash-3",
            state: "applied",
          }],
        }));
      if (path.endsWith("/api/edges"))
        return Promise.resolve(response({ data: [{
          edgeId: "edge-1",
          connectionState: "online",
          lastSeen: "2026-10-31T09:20:04Z",
          delivery: { state: "healthy", lastSuccessfulShipmentAt: "2026-10-31T09:19:58Z" },
          backlogStatisticsFresh: true,
        }] }));
      if (path.includes("/api/v1/acquisition-protocols"))
        return Promise.resolve(response({ protocols: [] }));
      return Promise.resolve(response({ data: [] }));
    }));

    render(<MemoryRouter initialEntries={["/configuration/ingestion-tasks/task-1?version=3"]}>
      <Routes><Route path="/configuration/ingestion-tasks/:taskId" element={<IngestionTaskPage />} /></Routes>
    </MemoryRouter>);

    expect(await screen.findByText("同一帧已写入类型化样本")).toBeInTheDocument();
    expect(screen.getByText("任务版本采样落库证据")).toBeInTheDocument();
    expect(screen.getAllByText(new RegExp(sampleReceipt.eventId))).toHaveLength(2);
    expect(screen.getByText(/不表示采样质量为良好、任务版本当时已在 Edge 生效/)).toBeInTheDocument();
    expect(screen.getByText("同一帧已入库")).toBeInTheDocument();
    await waitFor(() => expect(requests).toContain(`/api/v1/ingestion-tasks/task-1/3/sample-receipt?eventId=${encodeURIComponent(sampleReceipt.eventId)}`));

    await waitFor(() => expect(screen.getByRole("button", { name: "刷新现场证据" })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: "刷新现场证据" }));
    expect(await screen.findByText("刷新中")).toBeInTheDocument();
    expect(screen.getByText(/正在重新查询；以下记录来自最近一次成功查询/)).toBeInTheDocument();
    await act(async () => resolveReceiptRefresh(new Response("查询失败", { status: 503 })));
    expect(await screen.findByText("结果待刷新")).toBeInTheDocument();
    expect(screen.getByText("最近一次已确认匹配记录；本次刷新失败")).toBeInTheDocument();
  });

  it("没有匹配的 Platform 事件时保留未知状态", async () => {
    const publishedTask = {
      taskId: "task-2",
      version: 1,
      status: "published",
      edgeId: "edge-2",
      protocol: "http-polling",
      sampleEventType: "process.sample",
      dataModelId: "model-2",
      dataModelVersion: 1,
      subjectType: "equipment",
      subjectId: "PRESS-02",
      httpPolling: { baseUrl: "http://plc.local", snapshotPath: "/snapshot", pollIntervalMs: 1000 },
      valueMappings: [],
    };
    const response = data => new Response(JSON.stringify(data), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    });
    vi.stubGlobal("fetch", vi.fn(url => {
      const path = String(url);
      if (path.includes("/api/v1/ingestion-tasks?taskId=task-2"))
        return Promise.resolve(response({ data: [publishedTask] }));
      if (path.includes("/api/v1/ingestion-tasks/task-2/1/sample-receipt?eventId=sample-not-uploaded"))
        return Promise.resolve(response({ eventId: null, ingestedAt: null }));
      if (path.includes("/api/v1/process-data-models")) return Promise.resolve(response({ data: [] }));
      if (path.endsWith("/api/edges")) return Promise.resolve(response({ data: [{ edgeId: "edge-2" }] }));
      if (path.includes("/api/edges/edge-2/acquisition/status")) return Promise.resolve(response({
        platformObservedAt: "2026-10-31T09:20:05Z",
        platformReceivedAt: "2026-10-31T09:20:04Z",
        supportsSampleEventIdCorrelation: true,
        tasks: [{ configurationKey: "task-2@1", lastValidSnapshotAt: "2026-10-31T09:20:00Z", lastValidSnapshotEventId: "sample-not-uploaded" }],
        deployments: [],
      }));
      return Promise.resolve(response({ protocols: [] }));
    }));

    render(<MemoryRouter initialEntries={["/configuration/ingestion-tasks/task-2?version=1"]}>
      <Routes><Route path="/configuration/ingestion-tasks/:taskId" element={<IngestionTaskPage />} /></Routes>
    </MemoryRouter>);

    expect(await screen.findByText("尚未找到此样本回执")).toBeInTheDocument();
    expect(screen.getByText("尚无回执")).toBeInTheDocument();
    expect(screen.getByText(/Platform 尚未找到此样本 Event ID/)).toBeInTheDocument();
    expect(screen.queryByText(/自定义事件仅证明/)).not.toBeInTheDocument();
  });

  it("区分尚未完成查询与没有匹配记录", async () => {
    const publishedTask = {
      taskId: "task-3",
      version: 1,
      status: "published",
      edgeId: "edge-3",
      protocol: "http-polling",
      sampleEventType: "custom.measurement",
      dataModelId: "model-3",
      dataModelVersion: 1,
      subjectType: "equipment",
      subjectId: "PRESS-03",
      httpPolling: { baseUrl: "http://plc.local", snapshotPath: "/snapshot", pollIntervalMs: 1000 },
      valueMappings: [],
    };
    let resolveReceipt;
    const receiptRequest = new Promise(resolve => { resolveReceipt = resolve; });
    const response = data => new Response(JSON.stringify(data), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    });
    vi.stubGlobal("fetch", vi.fn(url => {
      const path = String(url);
      if (path.includes("/api/v1/ingestion-tasks?taskId=task-3")) return Promise.resolve(response({ data: [publishedTask] }));
      if (path.includes("/api/v1/ingestion-tasks/task-3/1/sample-receipt")) return receiptRequest;
      if (path.includes("/api/v1/process-data-models")) return Promise.resolve(response({ data: [] }));
      if (path.endsWith("/api/edges")) return Promise.resolve(response({ data: [{ edgeId: "edge-3" }] }));
      if (path.includes("/api/edges/edge-3/acquisition/status")) return Promise.resolve(response({
        tasks: [{ configurationKey: "task-3@1", lastValidSnapshotAt: "2026-10-31T09:20:00Z" }],
        deployments: [],
      }));
      if (path.includes("/api/v1/acquisition-protocols")) return Promise.resolve(response({ protocols: [] }));
      return Promise.resolve(response({ data: [] }));
    }));

    render(<MemoryRouter initialEntries={["/configuration/ingestion-tasks/task-3?version=1"]}>
      <Routes><Route path="/configuration/ingestion-tasks/:taskId" element={<IngestionTaskPage />} /></Routes>
    </MemoryRouter>);

    expect(await screen.findByText("正在核对当前样本 Event ID…")).toBeInTheDocument();
    expect(screen.getByText("正在核对")).toBeInTheDocument();
    expect(screen.getByText(/查询尚未完成，暂不判断此样本是否已入库/)).toBeInTheDocument();
    expect(screen.queryByText(/自定义事件仅证明/)).not.toBeInTheDocument();

    await act(async () => resolveReceipt(new Response("查询失败", { status: 503 })));
    expect(await screen.findByText("采样证据查询失败")).toBeInTheDocument();
    expect(screen.getByText("查询失败")).toBeInTheDocument();
    expect(screen.getByText("本次请求失败不代表没有匹配记录；请刷新重试，或查看现场节点诊断。")).toBeInTheDocument();
    expect(screen.queryByText("尚无回执")).not.toBeInTheDocument();
  });

  it("Edge 明确报告没有样本 Event ID 时不查询旧回执或标记入库", async () => {
    const publishedTask = {
      taskId: "task-4",
      version: 1,
      status: "published",
      edgeId: "edge-4",
      protocol: "http-polling",
      sampleEventType: "process.sample",
      dataModelId: "model-4",
      dataModelVersion: 1,
      subjectType: "equipment",
      subjectId: "PRESS-04",
      httpPolling: { baseUrl: "http://plc.local", snapshotPath: "/snapshot", pollIntervalMs: 1000 },
      valueMappings: [],
    };
    const requests = [];
    const response = data => new Response(JSON.stringify(data), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    });
    vi.stubGlobal("fetch", vi.fn(url => {
      const path = String(url);
      requests.push(path);
      if (path.includes("/api/v1/ingestion-tasks?taskId=task-4")) return Promise.resolve(response({ data: [publishedTask] }));
      if (path.includes("/api/v1/process-data-models")) return Promise.resolve(response({ data: [] }));
      if (path.endsWith("/api/edges")) return Promise.resolve(response({ data: [{ edgeId: "edge-4" }] }));
      if (path.includes("/api/edges/edge-4/acquisition/status")) return Promise.resolve(response({
        platformObservedAt: "2026-10-31T09:20:05Z",
        platformReceivedAt: "2026-10-31T09:20:04Z",
        supportsSampleEventIdCorrelation: true,
        tasks: [{ configurationKey: "task-4@1", lastValidSnapshotAt: "2026-10-31T09:20:00Z", lastValidSnapshotEventId: null }],
        deployments: [],
      }));
      if (path.includes("/api/v1/ingestion-tasks/task-4/1/sample-receipt"))
        return Promise.resolve(response({ eventId: "older-sample", ingestedAt: "2026-10-30T09:20:00Z", evidenceKind: "typed-sample-frame" }));
      return Promise.resolve(response({ protocols: [] }));
    }));

    render(<MemoryRouter initialEntries={["/configuration/ingestion-tasks/task-4?version=1"]}>
      <Routes><Route path="/configuration/ingestion-tasks/:taskId" element={<IngestionTaskPage />} /></Routes>
    </MemoryRouter>);

    expect(await screen.findByText("当前快照没有样本事件")).toBeInTheDocument();
    expect(screen.getByText("无样本事件")).toBeInTheDocument();
    expect(screen.getByText(/不会把较早回执标记为当前样本已入库/)).toBeInTheDocument();
    expect(requests.some(path => path.includes("/sample-receipt"))).toBe(false);
    expect(screen.queryByText("older-sample")).not.toBeInTheDocument();
  });

  it("旧版 Edge 的历史回执显示为同帧未核验", async () => {
    const publishedTask = {
      taskId: "task-5",
      version: 1,
      status: "published",
      edgeId: "edge-5",
      protocol: "http-polling",
      sampleEventType: "custom.measurement",
      dataModelId: "model-5",
      dataModelVersion: 1,
      subjectType: "equipment",
      subjectId: "PRESS-05",
      httpPolling: { baseUrl: "http://plc.local", snapshotPath: "/snapshot", pollIntervalMs: 1000 },
      valueMappings: [],
    };
    const response = data => new Response(JSON.stringify(data), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    });
    vi.stubGlobal("fetch", vi.fn(url => {
      const path = String(url);
      if (path.includes("/api/v1/ingestion-tasks?taskId=task-5")) return Promise.resolve(response({ data: [publishedTask] }));
      if (path.includes("/api/v1/ingestion-tasks/task-5/1/sample-receipt"))
        return Promise.resolve(response({ eventId: "legacy-sample", ingestedAt: "2026-10-31T09:20:00Z", evidenceKind: "event-payload" }));
      if (path.includes("/api/v1/process-data-models")) return Promise.resolve(response({ data: [] }));
      if (path.endsWith("/api/edges")) return Promise.resolve(response({ data: [{ edgeId: "edge-5" }] }));
      if (path.includes("/api/edges/edge-5/acquisition/status")) return Promise.resolve(response({
        platformObservedAt: "2026-10-31T09:20:05Z",
        platformReceivedAt: "2026-10-31T09:20:04Z",
        supportsSampleEventIdCorrelation: false,
        tasks: [{ configurationKey: "task-5@1", lastValidSnapshotAt: "2026-10-31T09:20:00Z", lastValidSnapshotEventId: null }],
        deployments: [],
      }));
      return Promise.resolve(response({ protocols: [] }));
    }));

    render(<MemoryRouter initialEntries={["/configuration/ingestion-tasks/task-5?version=1"]}>
      <Routes><Route path="/configuration/ingestion-tasks/:taskId" element={<IngestionTaskPage />} /></Routes>
    </MemoryRouter>);

    expect(await screen.findByText("发现历史记录；同一帧未核验")).toBeInTheDocument();
    expect(screen.getByText("同帧未核验")).toBeInTheDocument();
    expect(screen.getByText(/旧版 Edge 未提供 Event ID，无法确认同一帧/)).toBeInTheDocument();
    expect(screen.queryByText("已入库")).not.toBeInTheDocument();
  });
});
