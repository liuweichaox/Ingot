
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
