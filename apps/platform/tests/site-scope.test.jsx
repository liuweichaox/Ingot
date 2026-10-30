// Verify site-scoped queries fail closed and reject stale responses after a site switch.

import React from "react";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";
import { useApi } from "../src/hooks/useApi";
import { useSiteScope } from "../src/hooks/useSiteScope";

function jsonResponse(payload, status = 200) {
  return new Response(JSON.stringify(payload), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function deferred() {
  let resolve;
  const promise = new Promise(accept => { resolve = accept; });
  return { promise, resolve, started: false };
}

function SiteScopeHarness() {
  const scope = useSiteScope();
  const runs = useApi(scope.siteId
    ? `/api/v1/process-executions?siteId=${encodeURIComponent(scope.siteId)}`
    : "", { enabled: Boolean(scope.siteId) });

  return (
    <section>
      <output aria-label="当前站点">{scope.siteId}</output>
      <output aria-label="运行数据站点">{runs.data?.siteId || ""}</output>
      <output aria-label="站点参数状态">{scope.invalidRequestedSite ? "越权站点" : "有效"}</output>
      <button type="button" onClick={() => scope.selectSite("SITE-B")}>切换到站点 B</button>
    </section>
  );
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("站点范围与请求隔离", () => {
  it("切换站点后隐藏旧数据，并忽略迟到的旧站点响应", async () => {
    const runA = deferred();
    const runB = deferred();
    const fetchMock = vi.fn(async url => {
      const requestUrl = String(url);
      if (requestUrl === "/api/v1/auth/me") return jsonResponse({
        roles: ["process.engineer"],
        siteIds: ["SITE-A", "SITE-B"],
      });
      if (requestUrl.startsWith("/api/edges")) {
        const siteId = new URL(requestUrl, "http://localhost").searchParams.get("siteId");
        return jsonResponse([{ edgeId: `EDGE-${siteId}`, siteId }]);
      }
      if (requestUrl.includes("siteId=SITE-A")) {
        runA.started = true;
        return runA.promise;
      }
      if (requestUrl.includes("siteId=SITE-B")) {
        runB.started = true;
        return runB.promise;
      }
      throw new Error(`Unexpected request: ${requestUrl}`);
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<MemoryRouter initialEntries={["/process-executions?siteId=SITE-A"]}><SiteScopeHarness /></MemoryRouter>);

    await waitFor(() => expect(runA.started).toBe(true));
    expect(screen.getByLabelText("运行数据站点")).toBeEmptyDOMElement();
    fireEvent.click(screen.getByRole("button", { name: "切换到站点 B" }));

    await waitFor(() => expect(runB.started).toBe(true));
    expect(screen.getByLabelText("当前站点")).toHaveTextContent("SITE-B");
    expect(screen.getByLabelText("运行数据站点")).toBeEmptyDOMElement();

    await act(async () => runB.resolve(jsonResponse({ siteId: "SITE-B" })));
    await waitFor(() => expect(screen.getByLabelText("运行数据站点")).toHaveTextContent("SITE-B"));

    await act(async () => runA.resolve(jsonResponse({ siteId: "SITE-A" })));
    expect(screen.getByLabelText("运行数据站点")).toHaveTextContent("SITE-B");
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes("siteId=SITE-A") && String(url).includes("process-executions"))).toBe(true);
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes("siteId=SITE-B") && String(url).includes("process-executions"))).toBe(true);
  });

  it("URL 指向未授权站点时不发起站点业务查询", async () => {
    const fetchMock = vi.fn(async url => {
      const requestUrl = String(url);
      if (requestUrl === "/api/v1/auth/me") return jsonResponse({
        roles: ["process.engineer"],
        siteIds: ["SITE-A"],
      });
      if (requestUrl === "/api/edges?siteId=SITE-GHOST")
        return jsonResponse({ title: "Forbidden" }, 403);
      throw new Error(`Unexpected request: ${requestUrl}`);
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<MemoryRouter initialEntries={["/process-executions?siteId=SITE-GHOST"]}><SiteScopeHarness /></MemoryRouter>);

    await waitFor(() => expect(screen.getByLabelText("站点参数状态")).toHaveTextContent("越权站点"));
    expect(screen.getByLabelText("当前站点")).toBeEmptyDOMElement();
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes("process-executions"))).toBe(false);
  });
});
