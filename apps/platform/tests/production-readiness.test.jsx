// 验证登录错误呈现、角色导航与管理员路由的前端安全边界。
import React from "react";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";
import { RequireRole, sectionsForIdentity } from "../src/App";
import AuthGate from "../src/auth/AuthGate";

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  sessionStorage.clear();
});

function response(status, payload) {
  return new Response(payload == null ? "" : JSON.stringify(payload), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

describe("production-ready authentication and authorization", () => {
  it("treats an initial 401 as signed-out state without showing Unauthorized", async () => {
    vi.stubGlobal("fetch", vi.fn()
      .mockResolvedValueOnce(response(200, { mode: "local" }))
      .mockResolvedValueOnce(response(401, { title: "Unauthorized" })));
    render(<AuthGate>{() => <div>signed in</div>}</AuthGate>);
    expect(await screen.findByRole("heading", { name: "进入 Ingot" })).toBeInTheDocument();
    expect(screen.queryByText("Unauthorized")).toBeNull();
  });

  it("localizes an explicit bad-password response", async () => {
    vi.stubGlobal("fetch", vi.fn()
      .mockResolvedValueOnce(response(200, { mode: "local" }))
      .mockResolvedValueOnce(response(401, { title: "Unauthorized" }))
      .mockResolvedValueOnce(response(401, { title: "Unauthorized" })));
    render(<AuthGate>{() => <div>signed in</div>}</AuthGate>);
    fireEvent.change(await screen.findByLabelText("用户名"), { target: { value: "demo" } });
    fireEvent.change(screen.getByLabelText("口令"), { target: { value: "wrong" } });
    fireEvent.click(screen.getByRole("button", { name: "登录" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("用户名或口令错误。");
  });

  it("hides system administration from non-admin navigation and guards deep links", () => {
    expect(sectionsForIdentity({ roles: ["process.engineer"] }).some(section => section.id === "system")).toBe(false);
    expect(sectionsForIdentity({ roles: ["platform.admin"] }).some(section => section.id === "system")).toBe(true);
    render(<MemoryRouter><RequireRole identity={{ roles: ["process.engineer"] }} roles={["platform.admin"]}><div>admin content</div></RequireRole></MemoryRouter>);
    expect(screen.getByRole("alert")).toHaveTextContent("当前岗位不能访问此功能");
    expect(screen.queryByText("admin content")).toBeNull();
  });
});
