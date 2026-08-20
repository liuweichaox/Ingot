// 挂载平台 React 应用，并集中启用路由与全局运行时依赖。

import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router";
import App from "./App";
import AuthGate from "./auth/AuthGate";
import "./styles/global.css";

createRoot(document.getElementById("root")).render(
  <StrictMode>
    <BrowserRouter>
      <AuthGate>{auth => <App {...auth} />}</AuthGate>
    </BrowserRouter>
  </StrictMode>,
);
