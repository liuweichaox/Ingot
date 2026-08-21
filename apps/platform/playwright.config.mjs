// 运行 Platform 的真实浏览器生产就绪回归，并复用或启动本地 demo 服务。
import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  timeout: 30_000,
  expect: { timeout: 8_000 },
  fullyParallel: false,
  workers: 1,
  reporter: "line",
  outputDir: "/tmp/ingot-platform-playwright-results",
  use: {
    baseURL: "http://127.0.0.1:3001",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: [
    {
      command: "node ../../scripts/platform-demo.mjs",
      url: "http://127.0.0.1:4010/health",
      reuseExistingServer: true,
      timeout: 20_000,
    },
    {
      command: "npm run demo",
      url: "http://127.0.0.1:3001",
      reuseExistingServer: true,
      timeout: 30_000,
    },
  ],
});
