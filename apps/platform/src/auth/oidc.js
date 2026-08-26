// 封装 OIDC Code+PKCE 客户端、回调和安全返回地址处理。
import { UserManager, WebStorageStateStore } from "oidc-client-ts";

const defaultPaths = {
  callbackPath: "/auth/callback",
  silentCallbackPath: "/auth/silent-callback",
  logoutCallbackPath: "/auth/logout-callback",
};

function sameOriginUrl(path) {
  const url = new URL(path, window.location.origin);
  if (url.origin !== window.location.origin) throw new Error("OIDC callback must use the platform origin.");
  return url.toString();
}

export async function loadAuthConfiguration() {
  const response = await fetch("/api/v1/auth/config", {
    cache: "no-store",
    headers: { Accept: "application/json" },
  });
  if (!response.ok) throw new Error(`Authentication configuration request failed (${response.status}).`);
  const configuration = await response.json();
  const mode = String(configuration?.mode || "").toLowerCase();
  if (!mode) throw new Error("Authentication configuration did not include a mode.");
  if (mode === "oidc" && (!configuration.authority || !configuration.clientId)) {
    throw new Error("OIDC authentication configuration is incomplete.");
  }
  return { ...defaultPaths, ...configuration, mode };
}

export function createOidcManager(configuration) {
  const stateStore = new WebStorageStateStore({ store: window.sessionStorage });
  return new UserManager({
    authority: configuration.authority,
    client_id: configuration.clientId,
    redirect_uri: sameOriginUrl(configuration.callbackPath),
    silent_redirect_uri: sameOriginUrl(configuration.silentCallbackPath),
    post_logout_redirect_uri: sameOriginUrl(configuration.logoutCallbackPath),
    response_type: "code",
    scope: configuration.scope || "openid profile",
    automaticSilentRenew: true,
    includeIdTokenInSilentRenew: true,
    loadUserInfo: false,
    monitorSession: false,
    redirectMethod: "replace",
    stateStore,
    userStore: stateStore,
  });
}

export function oidcCallbackKind(configuration, pathname = window.location.pathname) {
  if (pathname === configuration.callbackPath) return "signin";
  if (pathname === configuration.silentCallbackPath) return "silent";
  if (pathname === configuration.logoutCallbackPath) return "signout";
  return null;
}

export function safeReturnUrl(value) {
  if (typeof value !== "string" || !value.startsWith("/") || value.startsWith("//") || value.startsWith("/auth/")) return "/";
  return value;
}

export function currentReturnUrl() {
  return safeReturnUrl(`${window.location.pathname}${window.location.search}${window.location.hash}`);
}

export function replaceBrowserPath(path) {
  window.history.replaceState({}, document.title, safeReturnUrl(path));
  window.dispatchEvent(new PopStateEvent("popstate"));
}
