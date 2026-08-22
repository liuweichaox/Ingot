// 运行 Platform 的真实浏览器生产就绪回归，并复用或启动本地 demo 服务。
import { defineConfig, devices } from "@playwright/test";

const demoPort = process.env.INGOT_E2E_API_PORT || "4010";
const platformPort = process.env.INGOT_E2E_WEB_PORT || "3001";
const demoUrl = `http://127.0.0.1:${demoPort}`;
const platformUrl = `http://127.0.0.1:${platformPort}`;

export default defineConfig({
  testDir: "./e2e",
  timeout: 30_000,
  expect: { timeout: 8_000 },
  fullyParallel: false,
  workers: 1,
  reporter: "line",
  outputDir: "/tmp/ingot-platform-playwright-results",
  use: {
    baseURL: platformUrl,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: [
    {
      command: `INGOT_DEMO_PORT=${demoPort} node ../../scripts/platform-demo.mjs`,
      url: `${demoUrl}/health`,
      reuseExistingServer: true,
      timeout: 20_000,
    },
    {
      command: `INGOT_PLATFORM_API_TARGET=${demoUrl} npm run demo -- --port ${platformPort}`,
      url: platformUrl,
      reuseExistingServer: true,
      timeout: 30_000,
    },
  ],
});
