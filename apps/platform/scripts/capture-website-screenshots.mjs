// Captures the public website's product screenshots from the local demo flow.
import { chromium } from "@playwright/test";
import { mkdir } from "node:fs/promises";
import { resolve } from "node:path";

const baseUrl = process.env.INGOT_PLATFORM_URL || "http://127.0.0.1:3001";
const outputDirectory = resolve(import.meta.dirname, "../../website/public/screenshots");

async function login(page) {
  await page.goto(baseUrl);
  await page.getByLabel("用户名").fill("demo");
  await page.getByLabel("口令").fill("demo");
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await page.getByRole("heading", { name: "工作台", exact: true }).waitFor();
}

await mkdir(outputDirectory, { recursive: true });
const browser = await chromium.launch({ headless: true });
const page = await browser.newPage({ viewport: { width: 1600, height: 1000 }, deviceScaleFactor: 1 });

try {
  await login(page);
  await page.goto(`${baseUrl}/process-executions/RUN-2026-0821-005?siteId=SITE-001`);
  await page.waitForTimeout(1000);
  await page.screenshot({ path: resolve(outputDirectory, "production-run.png") });

  await page.goto(`${baseUrl}/analysis`);
  await page.getByRole("heading", { name: "追因总览", exact: true }).waitFor();
  await page.screenshot({ path: resolve(outputDirectory, "diagnosis.png") });

  await page.goto(`${baseUrl}/configuration/process-specifications`);
  await page.getByRole("heading", { name: "工艺规范", exact: true }).waitFor();
  await page.getByRole("button", { name: "修订规范", exact: true }).click();
  await page.waitForTimeout(800);
  await page.getByLabel("修订理由").fill("针对保压阶段的质量偏差创建规范修订草稿。");
  await page.screenshot({ path: resolve(outputDirectory, "next-recipe.png") });
} finally {
  await browser.close();
}
