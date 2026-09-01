// 在本地认证与 OIDC Code+PKCE 模式间建立统一会话门禁。
import { useCallback, useEffect, useRef, useState } from "react";
import { getJson, postJson, setAuthToken } from "../api/http";
import { Alert, Button, Field, Input } from "../ui/components";
import {
  createOidcManager,
  currentReturnUrl,
  loadAuthConfiguration,
  oidcCallbackKind,
  replaceBrowserPath,
  safeReturnUrl,
} from "./oidc";

export default function AuthGate({ children, oidcManagerFactory = createOidcManager }) {
  const [identity, setIdentity] = useState(null);
  const [authMode, setAuthMode] = useState(null);
  const [checking, setChecking] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [credentials, setCredentials] = useState({ username: "", password: "" });
  const authModeRef = useRef(null);
  const oidcManagerRef = useRef(null);
  const initializationRef = useRef(null);
  const renewalRef = useRef(null);
  const unauthorizedRenewalAttemptedRef = useRef(false);

  const verifyIdentity = useCallback(async () => {
    setChecking(true);
    try {
      setIdentity(await getJson("/api/v1/auth/me"));
      unauthorizedRenewalAttemptedRef.current = false;
      setError("");
    } catch (requestError) {
      setIdentity(null);
      if (requestError.status !== 401) setError(requestError.message);
    } finally {
      setChecking(false);
    }
  }, []);

  const acceptOidcUser = useCallback(user => {
    if (!user?.access_token || user.expired) throw new Error("OIDC did not return a usable access token.");
    setAuthToken(user.access_token);
    return user;
  }, []);

  const renewOidcSession = useCallback(() => {
    const manager = oidcManagerRef.current;
    if (!manager) return Promise.resolve(null);
    if (!renewalRef.current) {
      renewalRef.current = manager.signinSilent()
        .then(user => user ? acceptOidcUser(user) : null)
        .finally(() => { renewalRef.current = null; });
    }
    return renewalRef.current;
  }, [acceptOidcUser]);

  useEffect(() => {
    const handleUnauthorized = () => {
      setIdentity(null);
      if (authModeRef.current !== "oidc") {
        setAuthToken(null);
        setChecking(false);
        return;
      }
      if (unauthorizedRenewalAttemptedRef.current) {
        setAuthToken(null);
        setError("企业身份令牌未通过平台校验，请重新登录。");
        setChecking(false);
        return;
      }
      unauthorizedRenewalAttemptedRef.current = true;
      setChecking(true);
      renewOidcSession()
        .then(user => {
          if (user) return verifyIdentity();
          setAuthToken(null);
          setChecking(false);
          return null;
        })
        .catch(() => {
          setAuthToken(null);
          setError("企业身份会话已失效，请重新登录。");
          setChecking(false);
        });
    };
    window.addEventListener("ingot:unauthorized", handleUnauthorized);
    return () => window.removeEventListener("ingot:unauthorized", handleUnauthorized);
  }, [renewOidcSession, verifyIdentity]);

  const initializeAuthentication = useCallback(async () => {
    let phase = "configuration";
    setChecking(true);
    setError("");
    try {
      const configuration = await loadAuthConfiguration();
      setAuthMode(configuration.mode);
      authModeRef.current = configuration.mode;
      if (configuration.mode !== "oidc") {
        await verifyIdentity();
        return;
      }

      const manager = oidcManagerFactory(configuration);
      oidcManagerRef.current = manager;
      manager.events.addUserLoaded(user => {
        if (user?.access_token && !user.expired) {
          unauthorizedRenewalAttemptedRef.current = false;
          setAuthToken(user.access_token);
        }
      });
      manager.events.addAccessTokenExpired(() => {
        setAuthToken(null);
        setIdentity(null);
        setChecking(false);
      });
      manager.events.addSilentRenewError(() => {
        setError("企业身份会话续期失败，请重新登录。");
      });

      const callback = oidcCallbackKind(configuration);
      phase = callback || "session";
      if (callback === "silent") {
        await manager.signinSilentCallback(window.location.href);
        setChecking(false);
        return;
      }
      if (callback === "signout") {
        await manager.signoutRedirectCallback(window.location.href);
        setAuthToken(null);
        setIdentity(null);
        replaceBrowserPath("/");
        setChecking(false);
        return;
      }

      let user;
      if (callback === "signin") {
        user = await manager.signinRedirectCallback(window.location.href);
      } else {
        user = await manager.getUser();
        if (user?.expired) user = await renewOidcSession();
      }
      if (!user) {
        setAuthToken(null);
        setIdentity(null);
        setChecking(false);
        return;
      }
      acceptOidcUser(user);
      if (callback === "signin") replaceBrowserPath(safeReturnUrl(user.state?.returnUrl));
      await verifyIdentity();
    } catch {
      setAuthToken(null);
      setIdentity(null);
      setError(phase === "signin"
        ? "企业身份回调校验失败，请重新发起登录。"
        : phase === "signout"
          ? "企业身份退出回调校验失败，请刷新后重试。"
          : phase === "configuration"
            ? "无法读取平台认证配置，请联系管理员。"
            : "企业身份会话无效，请重新登录。");
      setChecking(false);
    }
  }, [acceptOidcUser, oidcManagerFactory, renewOidcSession, verifyIdentity]);

  useEffect(() => {
    if (!initializationRef.current) initializationRef.current = initializeAuthentication();
  }, [initializeAuthentication]);

  async function login(event) {
    event.preventDefault();
    setBusy(true);
    setError("");
    try {
      const response = await postJson("/api/v1/auth/login", credentials);
      setAuthToken(response.token);
      setIdentity(response);
      setCredentials(current => ({ ...current, password: "" }));
    } catch (requestError) {
      setError(requestError.status === 401 ? "用户名或口令错误。" : requestError.message);
    } finally {
      setBusy(false);
    }
  }

  async function loginWithOidc() {
    const manager = oidcManagerRef.current;
    if (!manager) return;
    setBusy(true);
    setError("");
    try {
      await manager.signinRedirect({ state: { returnUrl: currentReturnUrl() } });
    } catch {
      setError("无法跳转到企业身份提供方，请稍后重试。");
    } finally {
      setBusy(false);
    }
  }

  async function logout() {
    if (authMode === "oidc") {
      setAuthToken(null);
      setIdentity(null);
      setBusy(true);
      try {
        await oidcManagerRef.current?.signoutRedirect();
      } catch {
        await oidcManagerRef.current?.removeUser().catch(() => null);
        setError("企业身份退出失败，本地会话已清除。");
        setChecking(false);
      } finally {
        setBusy(false);
      }
      return;
    }
    await postJson("/api/v1/auth/logout", {}).catch(() => null);
    setAuthToken(null);
    setIdentity(null);
  }

  if (checking) {
    return (
      <div className="app-canvas grid min-h-screen place-items-center px-4">
        <div className="text-center">
          <span className="mx-auto grid size-16 place-items-center rounded-2xl bg-coal-950 shadow-xl"><img src="/ingot-mark.svg" alt="" className="size-11" /></span>
          <p className="mt-5 text-sm font-medium text-slate-600">正在确认平台身份…</p>
        </div>
      </div>
    );
  }

  if (!identity) {
    return (
      <main className="grid min-h-screen bg-coal-950 lg:grid-cols-[minmax(0,1.08fr)_minmax(28rem,.92fr)]">
        <section className="product-panel-dark relative hidden min-h-screen overflow-hidden p-10 lg:flex lg:flex-col lg:justify-between xl:p-14" aria-label="产品介绍">
          <div className="absolute inset-0 opacity-35" aria-hidden="true" style={{ backgroundImage: "linear-gradient(rgba(95,212,200,.09) 1px, transparent 1px), linear-gradient(90deg, rgba(95,212,200,.09) 1px, transparent 1px)", backgroundSize: "64px 64px", maskImage: "linear-gradient(to bottom, black, transparent 85%)" }} />
          <img src="/brand/ingot-lockup-dark.svg" alt="Ingot" className="relative h-11 w-auto" />

          <div className="relative max-w-2xl py-14">
            <p className="data-label text-evidence-400">PROCESS DIAGNOSIS · SPECIFICATION REVISION</p>
            <h1 className="mt-5 text-5xl font-semibold leading-[1.06] tracking-normal text-white xl:text-6xl">从真实运行，<br /><span className="text-evidence-400">到下一版工艺规范。</span></h1>
            <p className="mt-7 max-w-xl text-base leading-8 text-slate-300">开源工艺追因与优化系统。把设备、生产和检验数据关联成可信证据，支持工程师修订下一版工艺规范。</p>

            <div className="mt-10 overflow-hidden rounded-lg border border-white/12 bg-black/15 backdrop-blur-sm">
              <div className="flex items-center justify-between border-b border-white/8 px-5 py-4">
                <div><p className="data-label text-slate-400">ENGINEERING DECISION · EVIDENCE</p><p className="mt-1 text-sm font-semibold text-white">一次运行的工程证据</p></div>
                <span className="rounded-full bg-trajectory-500/12 px-3 py-1 text-xs font-semibold text-trajectory-100 ring-1 ring-inset ring-trajectory-500/20">下一版草稿待确认</span>
              </div>
              <p className="px-5 pt-4 text-[11px] text-slate-500">SPECIFICATION REVISION · RUN-042</p>
              <div className="grid grid-cols-3 divide-x divide-white/8 px-2 py-5">
                {[["实际控制变量", "42.0"], ["阶段轨迹偏差", "+1.8σ"], ["工装版本", "TOOLING-A"]].map(([label, value]) => (
                  <div key={label} className="px-4"><p className="data-label text-slate-500">{label}</p><p className="data-value mt-2 text-xl font-semibold text-white">{value}</p></div>
                ))}
              </div>
              <div className="mx-5 divide-y divide-white/8 border border-white/8 bg-black/10 text-xs">
                {[["关键差异", "保压阶段"], ["有效运行", "12 条"], ["下一版规范", "待修订"]].map(([label, value]) => (
                  <div key={label} className="flex items-center justify-between gap-4 px-3 py-2.5"><span className="text-slate-400">{label}</span><strong className="font-mono text-slate-200">{value}</strong></div>
                ))}
              </div>
              <p className="px-5 py-4 text-[11px] leading-5 text-slate-500">产品界面示意 · 同时呈现事实、差异、不确定性和可执行下一步</p>
            </div>
          </div>

          <div className="relative flex gap-6 text-xs text-slate-400">
            {['证据可追溯', '原因可验证', '建议可审核', '结论可复用'].map(item => <span key={item} className="flex items-center gap-2"><i className="size-1.5 rounded-full bg-trajectory-500 shadow-[0_0_10px_rgba(95,212,200,.7)]" />{item}</span>)}
          </div>
        </section>

        <section className="app-canvas grid min-h-screen place-items-center px-5 py-10">
          <div className="w-full max-w-md">
            <header className="mb-10 flex items-center justify-between lg:hidden">
              <div className="flex items-center gap-3"><span className="grid size-11 place-items-center rounded-xl bg-coal-950"><img src="/ingot-mark.svg" alt="" className="size-8" /></span><div><strong className="text-base text-slate-950">Ingot</strong><p className="text-xs text-slate-500">工艺证据工作台</p></div></div>
              <span className="rounded-md border border-slate-200 bg-white px-2.5 py-1 text-xs font-medium text-slate-600">{import.meta.env.MODE === "demo" ? "演示环境" : "平台环境"}</span>
            </header>
            <div className="mb-8">
              <div className="hidden items-center justify-between lg:flex">
                <p className="data-label text-trajectory-700">Secure workspace</p>
                <span className="rounded-md border border-slate-200 bg-white px-2.5 py-1 text-xs font-medium text-slate-600">{import.meta.env.MODE === "demo" ? "演示环境" : "平台环境"}</span>
              </div>
              <h1 className="mt-3 text-3xl font-semibold tracking-normal text-slate-950">进入 Ingot</h1>
              <p className="mt-2 text-sm leading-6 text-slate-500">登录后继续查看真实运行、质量结果、工艺追因与工艺规范版本。</p>
            </div>
            {authMode === "oidc" ? (
              <div className="space-y-4">
                <p className="text-sm leading-6 text-slate-600">使用组织的统一身份提供方完成登录，平台不会收集企业口令。</p>
                {error && <Alert tone="danger">{error}</Alert>}
                <Button type="button" variant="primary" className="min-h-12 w-full justify-center" disabled={busy} onClick={loginWithOidc}>
                  {busy ? "正在跳转…" : "使用企业身份登录"}
                </Button>
              </div>
            ) : authMode === "local" || authMode === "disabled" ? (
              <form className="space-y-5" onSubmit={login}>
                <Field label="用户名">
                  <Input className="h-12 bg-white" autoComplete="username" value={credentials.username} onChange={event => setCredentials({ ...credentials, username: event.target.value })} required autoFocus />
                </Field>
                <Field label="口令">
                  <Input className="h-12 bg-white" type="password" autoComplete="current-password" value={credentials.password} onChange={event => setCredentials({ ...credentials, password: event.target.value })} required />
                </Field>
                {error && <Alert tone="danger">{error}</Alert>}
                <Button type="submit" variant="primary" className="min-h-12 w-full justify-center" disabled={busy}>
                  {busy ? "正在登录…" : "登录"}
                </Button>
              </form>
            ) : (
              <div className="mt-6 space-y-4">
                {error && <Alert tone="danger">{error}</Alert>}
                <Button type="button" className="min-h-11 w-full justify-center" onClick={() => window.location.reload()}>重新读取认证配置</Button>
              </div>
            )}
            <p className="mt-6 border-t border-slate-200 pt-5 text-xs leading-5 text-slate-500">无法登录时，请联系平台管理员确认账户状态和岗位权限。</p>
          </div>
        </section>
      </main>
    );
  }

  return children({ identity, logout });
}
