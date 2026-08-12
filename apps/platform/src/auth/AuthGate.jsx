import { useCallback, useEffect, useState } from "react";
import { getJson, postJson, setAuthToken } from "../api/http";
import { Alert, Button, Field, Input } from "../ui/components";

export default function AuthGate({ children }) {
  const [identity, setIdentity] = useState(null);
  const [checking, setChecking] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [credentials, setCredentials] = useState({ username: "", password: "" });

  const verifyIdentity = useCallback(async () => {
    setChecking(true);
    try {
      setIdentity(await getJson("/api/v1/auth/me"));
      setError("");
    } catch (requestError) {
      setIdentity(null);
      if (!requestError.message.includes("401")) setError(requestError.message);
    } finally {
      setChecking(false);
    }
  }, []);

  useEffect(() => {
    verifyIdentity();
    const handleUnauthorized = () => {
      setAuthToken(null);
      setIdentity(null);
      setChecking(false);
    };
    window.addEventListener("ingot:unauthorized", handleUnauthorized);
    return () => window.removeEventListener("ingot:unauthorized", handleUnauthorized);
  }, [verifyIdentity]);

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
      setError(requestError.message);
    } finally {
      setBusy(false);
    }
  }

  async function logout() {
    try {
      await postJson("/api/v1/auth/logout", {});
    } catch {
      // Local state must still be cleared when the server session has already expired.
    } finally {
      setAuthToken(null);
      setIdentity(null);
    }
  }

  if (checking) {
    return (
      <div className="grid min-h-screen place-items-center bg-slate-50 px-4">
        <div className="text-center">
          <img src="/ingot-mark.svg" alt="" className="mx-auto size-12" />
          <p className="mt-4 text-sm text-slate-500">正在确认平台身份…</p>
        </div>
      </div>
    );
  }

  if (!identity) {
    return (
      <main className="grid min-h-screen bg-slate-50 lg:grid-cols-[minmax(0,1fr)_32rem]">
        <section className="hidden border-r border-slate-200 bg-slate-950 px-12 py-16 text-white lg:flex lg:flex-col lg:justify-between">
          <div className="flex items-center gap-3">
            <span className="grid size-11 place-items-center rounded-xl bg-amber-50"><img src="/ingot-mark.svg" alt="" className="size-8" /></span>
            <div><strong className="text-lg">Ingot</strong><p className="text-sm text-slate-400">工业数据与工艺决策平台</p></div>
          </div>
          <div className="max-w-xl">
            <p className="text-sm font-semibold text-blue-300">从现场证据走向可验证决策</p>
            <h1 className="mt-4 text-4xl font-semibold leading-tight">统一连接生产运行、质量结果、工艺分析与优化验证。</h1>
            <p className="mt-5 text-base leading-7 text-slate-300">登录后继续访问本工厂的数据、配置和工程工作区。</p>
          </div>
          <p className="text-xs text-slate-500">单工厂部署 · 本地账户认证</p>
        </section>
        <section className="flex items-center justify-center px-5 py-12 sm:px-10">
          <div className="w-full max-w-sm">
            <div className="mb-8 flex items-center gap-3 lg:hidden">
              <span className="grid size-10 place-items-center rounded-xl bg-amber-50 ring-1 ring-amber-200"><img src="/ingot-mark.svg" alt="" className="size-7" /></span>
              <div><strong>Ingot</strong><p className="text-xs text-slate-500">工业数据与工艺决策平台</p></div>
            </div>
            <p className="text-sm font-semibold text-blue-700">平台登录</p>
            <h1 className="mt-2 text-3xl font-semibold tracking-tight text-slate-950">继续进入工作台</h1>
            <p className="mt-3 text-sm leading-6 text-slate-500">使用管理员分配的本地账户。登录会话仅保存在当前浏览器标签页。</p>
            <form className="mt-8 space-y-5" onSubmit={login}>
              <Field label="用户名">
                <Input autoComplete="username" value={credentials.username} onChange={event => setCredentials({ ...credentials, username: event.target.value })} required autoFocus />
              </Field>
              <Field label="口令">
                <Input type="password" autoComplete="current-password" value={credentials.password} onChange={event => setCredentials({ ...credentials, password: event.target.value })} required />
              </Field>
              {error && <Alert tone="danger">{error}</Alert>}
              <Button type="submit" variant="primary" className="min-h-11 w-full justify-center" disabled={busy}>
                {busy ? "正在登录…" : "登录"}
              </Button>
            </form>
            <p className="mt-6 text-xs leading-5 text-slate-400">无法登录时，请联系平台管理员确认账户状态和岗位权限。</p>
          </div>
        </section>
      </main>
    );
  }

  return children({ identity, logout });
}
