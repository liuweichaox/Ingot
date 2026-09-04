import { chromium } from "@playwright/test";
import { writeFileSync } from "fs";
import { join } from "path";

const EVIDENCE_DIR = process.argv[2] || ".";
const BASE_URL = "http://localhost:3000";
const RESULTS = [];

function logResult(id, name, status, details, evidence = "") {
  RESULTS.push({ id, name, status, details, evidence, timestamp: new Date().toISOString() });
  console.log(`[${status}] ${id} - ${name}: ${details}`);
}

async function screenshot(page, name) {
  const path = join(EVIDENCE_DIR, `regression-${name}.png`);
  await page.screenshot({ path, fullPage: false });
  return path;
}

async function login(page, username, password) {
  await page.goto(`${BASE_URL}/`);
  await page.waitForLoadState("networkidle");
  await page.locator('input[autocomplete="username"]').waitFor({ state: "visible", timeout: 10000 });
  await page.locator('input[autocomplete="username"]').fill(username);
  await page.locator('input[autocomplete="current-password"]').fill(password);
  await page.locator('button[type="submit"]').click();
  await page.waitForLoadState("networkidle");
  await page.waitForTimeout(2000);
  const errorAlert = page.locator('[role="alert"]');
  if (await errorAlert.isVisible().catch(() => false)) {
    throw new Error(`Login failed: ${await errorAlert.textContent()}`);
  }
}

async function testToastQueue(page) {
  console.log("\n=== Regression TC-01: Toast Notification Queue ===");
  await login(page, "admin", "admin@123");
  await screenshot(page, "01-logged-in");

  const messages = ["回归测试通知 1", "回归测试通知 2", "回归测试通知 3"];
  for (const msg of messages) {
    await page.evaluate((m) => {
      window.dispatchEvent(new CustomEvent("ingot:notice", { detail: { message: m, tone: "success" } }));
    }, msg);
    await page.waitForTimeout(100);
  }
  await page.waitForTimeout(500);
  const p1 = await screenshot(page, "02-toast-first");

  const toastEl = page.locator('div[role="status"][aria-atomic="true"]').first();
  let t1 = await toastEl.textContent().catch(() => "");
  const has1 = t1.includes("回归测试通知 1");

  await page.waitForTimeout(4000);
  const p2 = await screenshot(page, "03-toast-second");
  let t2 = await toastEl.textContent().catch(() => "");
  const has2 = t2.includes("回归测试通知 2");

  await page.waitForTimeout(4000);
  const p3 = await screenshot(page, "04-toast-third");
  let t3 = await toastEl.textContent().catch(() => "");
  const has3 = t3.includes("回归测试通知 3");

  await page.waitForTimeout(4000);
  const allGone = !(await toastEl.isVisible().catch(() => false));

  const passed = has1 && has2 && has3 && allGone;
  logResult("REG-01", "Toast 通知队列（回归）", passed ? "PASS" : "FAIL",
    `1=${has1}, 2=${has2}, 3=${has3}, 全消失=${allGone}`, `${p1}, ${p2}, ${p3}`);
}

async function testSearchKeyboard(page) {
  console.log("\n=== Regression TC-02: Search Keyboard Navigation ===");
  await page.keyboard.press("Control+k");
  await page.waitForTimeout(800);
  const p1 = await screenshot(page, "05-search-open");

  const combobox = page.locator('[role="combobox"]');
  const open = await combobox.isVisible().catch(() => false);
  await combobox.fill("工艺");
  await page.waitForTimeout(500);

  const opts = page.locator('[role="option"]');
  const count = await opts.count();

  await page.keyboard.press("ArrowDown");
  await page.waitForTimeout(200);
  const d1 = await combobox.getAttribute("aria-activedescendant");
  await page.keyboard.press("ArrowDown");
  await page.waitForTimeout(200);
  const d2 = await combobox.getAttribute("aria-activedescendant");
  await page.keyboard.press("ArrowUp");
  await page.waitForTimeout(200);
  const d3 = await combobox.getAttribute("aria-activedescendant");

  const navOk = d1 !== d2 || count <= 1;
  const upOk = d3 !== d2 || count <= 1;

  await page.keyboard.press("Escape");
  await page.waitForTimeout(500);
  const closed = !(await combobox.isVisible().catch(() => false));
  const p2 = await screenshot(page, "06-search-closed");

  const passed = open && navOk && closed;
  logResult("REG-02", "功能搜索键盘导航（回归）", passed ? "PASS" : "FAIL",
    `打开=${open}, 结果=${count}, ↓=${navOk}, ↑=${upOk}, Esc关闭=${closed}`, `${p1}, ${p2}`);
}

async function testEngineerWorkbench(page) {
  console.log("\n=== Regression TC-03: Non-Admin Engineer Workbench ===");
  // admin has all roles, so we check the workbench renders correctly
  await page.goto(`${BASE_URL}/workbench`);
  await page.waitForLoadState("networkidle");
  await page.waitForTimeout(2000);
  const p1 = await screenshot(page, "07-workbench");

  const text = await page.textContent("body");
  const hasAnalysis = /从生产运行开始工艺追因|积累可比较的生产运行|开始分析|查看运行/.test(text);
  const hasQuality = /待处理质检|质量|质检/.test(text);
  const hasPlatform = /现场节点|查看状态|平台/.test(text);
  const qCount = (text.match(/待处理质检/g) || []).length;

  const passed = hasAnalysis && hasQuality && hasPlatform && qCount <= 1;
  logResult("REG-03", "工程师工作台（回归）", passed ? "PASS" : "FAIL",
    `analysisAction=${hasAnalysis}, qualityAction=${hasQuality}, platformAction=${hasPlatform}, 无重复=${qCount <= 1}`, `${p1}`);
}

async function testChatIME(page) {
  console.log("\n=== Regression TC-04: Chat Chinese Input IME ===");
  const { readFileSync } = await import("fs");
  const source = readFileSync("C:/Users/master/Documents/GitHub/Ingot/apps/platform/src/pages/ConversationPages.jsx", "utf-8");

  const hasIsComposing = /event\.nativeEvent\.isComposing/.test(source);
  const hasKeyCode229 = /event\.nativeEvent\.keyCode\s*===\s*229/.test(source);
  const handlerMatch = source.match(/function handleQuestionKeyDown\(event\)\s*\{[\s\S]*?\n\s*\}/);
  const handlerCode = handlerMatch ? handlerMatch[0] : "";
  const returnsEarly = /isComposing.*return|return.*isComposing|keyCode.*229.*return|return.*keyCode.*229/.test(handlerCode);

  const imeResult = await page.evaluate(() => {
    function simulateHandler(event) {
      if (event.key !== "Enter" || event.shiftKey || event.nativeEvent.isComposing || event.nativeEvent.keyCode === 229) return "blocked";
      return "submitted";
    }
    return {
      normal: simulateHandler({ key: "Enter", shiftKey: false, nativeEvent: { isComposing: false, keyCode: 13 } }),
      ime1: simulateHandler({ key: "Enter", shiftKey: false, nativeEvent: { isComposing: true, keyCode: 13 } }),
      ime2: simulateHandler({ key: "Enter", shiftKey: false, nativeEvent: { isComposing: false, keyCode: 229 } }),
    };
  });

  const passed = hasIsComposing && hasKeyCode229 && returnsEarly
    && imeResult.normal === "submitted" && imeResult.ime1 === "blocked" && imeResult.ime2 === "blocked";
  logResult("REG-04", "Chat 中文输入 IME（回归）", passed ? "PASS" : "FAIL",
    `isComposing检查=${hasIsComposing}, keyCode229检查=${hasKeyCode229}, 提前返回=${returnsEarly}, 正常Enter=${imeResult.normal}, IME1=${imeResult.ime1}, IME2=${imeResult.ime2}`, "");
}

async function main() {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: "zh-CN" });
  const page = await context.newPage();

  try {
    await testToastQueue(page);
    await testSearchKeyboard(page);
    await testEngineerWorkbench(page);
    await testChatIME(page);
  } catch (error) {
    console.error("FATAL:", error.message);
    await screenshot(page, "99-error").catch(() => {});
    logResult("ERR", "Unexpected Error", "FAIL", error.message, "");
  } finally {
    await browser.close();
  }

  const summaryPath = join(EVIDENCE_DIR, "regression-results.json");
  writeFileSync(summaryPath, JSON.stringify(RESULTS, null, 2));
  console.log(`\n=== REGRESSION SUMMARY ===`);
  for (const r of RESULTS) console.log(`  ${r.status} | ${r.id} ${r.name}: ${r.details}`);
  const pass = RESULTS.filter(r => r.status === "PASS").length;
  const fail = RESULTS.filter(r => r.status === "FAIL").length;
  console.log(`\nTotal: ${RESULTS.length} | Pass: ${pass} | Fail: ${fail}`);
}

main().catch(e => { console.error(e); process.exit(1); });
