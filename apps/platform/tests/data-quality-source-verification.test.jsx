// Verify that reported acquisition references are shown with bounded, non-causal evidence language.
import React from "react";
import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";

const mocks = vi.hoisted(() => ({
  useApi: vi.fn(),
  useSiteScope: vi.fn(),
}));

vi.mock("../src/hooks/useApi", () => ({
  useApi: mocks.useApi,
  extractRows: payload => Array.isArray(payload) ? payload : Array.isArray(payload?.data) ? payload.data : [],
}));

vi.mock("../src/hooks/useSiteScope", () => ({ useSiteScope: mocks.useSiteScope }));

import { DataQualityPage } from "../src/pages/AnalysisPages";

const siteId = "site-source-test";

function makeBaseline(sampleConfigurationEvidence) {
  return {
    generatedAt: "2026-10-30T00:00:00.000Z",
    siteId,
    from: null,
    to: null,
    edgeId: null,
    equipmentId: null,
    subjectType: null,
    subjectId: null,
    matchingCompletedRunCount: 1,
    matchingObservedRunCount: 1,
    analyzedRunCount: 1,
    missingExecutionRowCount: 0,
    truncated: false,
    rates: [],
    contextFields: [],
    contextFactors: [],
    contextFactorOverlaps: [],
    unidentifiableConfoundingCount: 0,
    exclusions: [],
    runImpacts: [{
      executionId: "run-source-test",
      equipmentId: "equipment-source-test",
      edgeIds: ["edge-source-test"],
      hasStarted: true,
      hasCompleted: true,
      startedAt: "2026-10-30T00:00:00.000Z",
      completedAt: "2026-10-30T00:01:00.000Z",
      processDataStatus: "degraded",
      reasonCodes: ["process_data_incomplete"],
      sampleConfigurationEvidence,
    }],
    duplicateTimestampCount: 0,
    outOfOrderCount: 0,
    sequenceGapCount: 0,
    maximumSampleGapMs: null,
    maximumAbsoluteSourceClockOffsetMs: null,
    worstRunP95PlatformIngestLatencyMs: null,
    maximumPlatformIngestLatencyMs: null,
    negativePlatformIngestLatencyCount: 0,
  };
}

function renderPage(sampleConfigurationEvidence) {
  const baseline = makeBaseline(sampleConfigurationEvidence);
  mocks.useApi.mockImplementation(url => String(url).startsWith("/api/v1/data-reliability/baseline")
    ? { data: baseline, loading: false, error: "", reload: vi.fn() }
    : { data: { data: [], total: 0 }, loading: false, error: "", reload: vi.fn() });
  return render(<MemoryRouter initialEntries={["/data-quality"]}><DataQualityPage /></MemoryRouter>);
}

beforeEach(() => {
  mocks.useSiteScope.mockReturnValue({
    siteId,
    siteChoices: [siteId],
    selectSite: vi.fn(),
    invalidRequestedSite: false,
    loading: false,
    error: "",
    edgeResponse: { data: [], loading: false, error: "", reload: vi.fn() },
    reload: vi.fn(),
  });
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe("数据质量页采样来源核对", () => {
  it("呈现四类平台核对状态，并保留部署和因果边界", () => {
    const evidence = [
      {
        appliedConfigurationKind: "ingestion-task",
        appliedConfigurationId: "task-matched",
        appliedConfigurationVersion: 3,
        sourceEdgeId: "edge-source-test",
        referenceVerificationStatus: "matched",
        configurationStatus: "published",
        sampleFrameCount: 4,
        observedValueCount: 8,
        goodValueCount: 7,
        badValueCount: 1,
        connectionUrl: "http://private-device.invalid",
        headersText: "Authorization: private-header",
        secretRef: "private-secret-ref",
      },
      {
        appliedConfigurationKind: "ingestion-task",
        appliedConfigurationId: "task-missing",
        appliedConfigurationVersion: 2,
        sourceEdgeId: "edge-source-test",
        referenceVerificationStatus: "version-not-found",
        sampleFrameCount: 2,
        observedValueCount: 2,
      },
      {
        appliedConfigurationKind: "ingestion-task",
        appliedConfigurationId: "task-edge-mismatch",
        appliedConfigurationVersion: 7,
        sourceEdgeId: "edge-source-test",
        referenceVerificationStatus: "edge-mismatch",
        sampleFrameCount: 1,
        observedValueCount: 1,
      },
      {
        summarizedConfigurationGroupCount: 2,
        referenceVerificationStatus: "unverifiable",
        sampleFrameCount: 5,
        observedValueCount: 5,
      },
      {
        appliedConfigurationKind: "ingestion-task",
        appliedConfigurationId: "task-legacy-frame",
        appliedConfigurationVersion: 1,
        referenceVerificationStatus: "matched",
        sampleFrameCount: 1,
        observedValueCount: 1,
      },
      {
        appliedConfigurationKind: "other-configuration",
        appliedConfigurationId: "other-source",
        appliedConfigurationVersion: 1,
        sourceEdgeId: "edge-source-test",
        referenceVerificationStatus: "matched",
        sampleFrameCount: 1,
        observedValueCount: 1,
      },
    ];
    const { container } = renderPage(evidence);

    expect(screen.getAllByText("帧报告来源/版本核对")).toHaveLength(6);
    expect(screen.getByText("平台任务版本与 Edge 一致")).toBeInTheDocument();
    expect(screen.getByText("站点内未找到该任务版本")).toBeInTheDocument();
    expect(screen.getByText("平台记录与帧来源 Edge 不一致")).toBeInTheDocument();
    expect(screen.getAllByText("无法逐版本核对")).toHaveLength(3);
    expect(screen.getByText("当前平台记录状态：已发布")).toBeInTheDocument();
    expect(screen.getByText(/当前平台记录状态不代表采样时状态/)).toBeInTheDocument();
    expect(screen.getByText(/不证明 Edge 当时或当前已部署或应用该版本/)).toBeInTheDocument();
    expect(screen.getByText(/不证明该版本导致运行、分析或配方建议受影响/)).toBeInTheDocument();
    expect(container.textContent).not.toContain("private-device.invalid");
    expect(container.textContent).not.toContain("private-header");
    expect(container.textContent).not.toContain("private-secret-ref");
  });
});
