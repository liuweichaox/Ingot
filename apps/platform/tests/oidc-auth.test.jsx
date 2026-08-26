// 验证 OIDC Code+PKCE 回调、会话恢复和安全返回地址处理。
import React from "react";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import AuthGate from "../src/auth/AuthGate";
import { safeReturnUrl } from "../src/auth/oidc";

const oidcConfiguration = {
  mode: "oidc",
  authority: "https://identity.example.com/tenant",
  clientId: "ingot-platform-spa",
  scope: "openid profile ingot-platform-api",
  callbackPath: "/auth/callback",
  silentCallbackPath: "/auth/silent-callback",
  logoutCallbackPath: "/auth/logout-callback",
};

function response(status, payload) {
  return new Response(payload == null ? "" : JSON.stringify(payload), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function manager(overrides = {}) {
  return {
    events: {
      addUserLoaded: vi.fn(),
      addAccessTokenExpired: vi.fn(),
      addSilentRenewError: vi.fn(),
    },
    getUser: vi.fn().mockResolvedValue(null),
    signinRedirect: vi.fn().mockResolvedValue(undefined),
    signinRedirectCallback: vi.fn(),
    signinSilent: vi.fn().mockResolvedValue(null),
    signinSilentCallback: vi.fn().mockResolvedValue(undefined),
    signoutRedirect: vi.fn().mockResolvedValue(undefined),
    signoutRedirectCallback: vi.fn().mockResolvedValue(undefined),
    removeUser: vi.fn().mockResolvedValue(undefined),
    ...overrides,
  };
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  sessionStorage.clear();
  window.history.replaceState({}, "", "/");
});

describe("OIDC authentication", () => {
  it("selects enterprise login mode and starts a stateful redirect", async () => {
    const oidcManager = manager();
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response(200, oidcConfiguration)));

    render(<AuthGate oidcManagerFactory={() => oidcManager}>{() => <div>signed in</div>}</AuthGate>);

    expect(await screen.findByRole("button", { name: "使用企业身份登录" })).toBeInTheDocument();
    expect(screen.queryByLabelText("用户名")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "使用企业身份登录" }));
    await waitFor(() => expect(oidcManager.signinRedirect).toHaveBeenCalledWith({
      state: { returnUrl: "/" },
    }));
  });

  it("accepts a validated callback, stores the access token, and restores the route", async () => {
    window.history.replaceState({}, "", "/auth/callback?code=code&state=state");
    const user = {
      access_token: "oidc-access-token",
      expired: false,
      state: { returnUrl: "/analysis" },
    };
    const oidcManager = manager({ signinRedirectCallback: vi.fn().mockResolvedValue(user) });
    vi.stubGlobal("fetch", vi.fn()
      .mockResolvedValueOnce(response(200, oidcConfiguration))
      .mockResolvedValueOnce(response(200, {
        userId: "oidc-user",
        username: "oidc.user",
        roles: ["process.engineer"],
        siteIds: ["SITE-001"],
      })));

    render(<AuthGate oidcManagerFactory={() => oidcManager}>{({ identity }) => <div>signed in as {identity.username}</div>}</AuthGate>);

    expect(await screen.findByText("signed in as oidc.user")).toBeInTheDocument();
    expect(oidcManager.signinRedirectCallback).toHaveBeenCalledWith(expect.stringContaining("/auth/callback?code=code&state=state"));
    expect(sessionStorage.getItem("ingot.auth.token")).toBe("oidc-access-token");
    expect(window.location.pathname).toBe("/analysis");
  });

  it("fails closed when callback state validation fails", async () => {
    window.history.replaceState({}, "", "/auth/callback?code=code&state=untrusted");
    const oidcManager = manager({
      signinRedirectCallback: vi.fn().mockRejectedValue(new Error("No matching state found in storage")),
    });
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response(200, oidcConfiguration)));

    render(<AuthGate oidcManagerFactory={() => oidcManager}>{() => <div>signed in</div>}</AuthGate>);

    expect(await screen.findByRole("alert")).toHaveTextContent("企业身份回调校验失败");
    expect(sessionStorage.getItem("ingot.auth.token")).toBeNull();
    expect(screen.queryByText("signed in")).toBeNull();
  });

  it("renews an expired token before loading identity and redirects on logout", async () => {
    const oidcManager = manager({
      getUser: vi.fn().mockResolvedValue({ access_token: "expired", expired: true }),
      signinSilent: vi.fn().mockResolvedValue({ access_token: "renewed-token", expired: false }),
    });
    vi.stubGlobal("fetch", vi.fn()
      .mockResolvedValueOnce(response(200, oidcConfiguration))
      .mockResolvedValueOnce(response(200, {
        userId: "oidc-user",
        username: "oidc.user",
        roles: ["process.engineer"],
        siteIds: ["SITE-001"],
      })));

    render(<AuthGate oidcManagerFactory={() => oidcManager}>{({ logout }) => <button onClick={logout}>sign out</button>}</AuthGate>);

    fireEvent.click(await screen.findByRole("button", { name: "sign out" }));
    await waitFor(() => expect(oidcManager.signoutRedirect).toHaveBeenCalledOnce());
    expect(oidcManager.signinSilent).toHaveBeenCalledOnce();
    expect(sessionStorage.getItem("ingot.auth.token")).toBeNull();
  });

  it("rejects cross-origin and callback return paths", () => {
    expect(safeReturnUrl("//evil.example/path")).toBe("/");
    expect(safeReturnUrl("/auth/callback?next=/admin")).toBe("/");
    expect(safeReturnUrl("/workbench?site=SITE-001")).toBe("/workbench?site=SITE-001");
  });
});
