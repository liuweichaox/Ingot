// Mounts the authenticated product on a data router so unsaved edits can guard every transition.
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { createBrowserRouter, RouterProvider } from "react-router";
import App from "./App";
import AuthGate from "./auth/AuthGate";
import { UnsavedNavigationGuard } from "./acquisition/UnsavedNavigationGuard";
import "./styles/global.css";

const router = createBrowserRouter([
  {
    path: "*",
    element: <UnsavedNavigationGuard><AuthGate>{auth => <App {...auth} />}</AuthGate></UnsavedNavigationGuard>,
  },
]);

createRoot(document.getElementById("root")).render(
  <StrictMode>
    <RouterProvider router={router} />
  </StrictMode>,
);
