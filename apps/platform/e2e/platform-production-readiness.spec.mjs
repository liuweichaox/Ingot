// 覆盖 Platform 核心工作流、状态矩阵、权限边界与响应式交互的浏览器回归。
import { expect, test } from "@playwright/test";

const demoApi = `http://127.0.0.1:${process.env.INGOT_E2E_API_PORT || "4010"}`;

async function setScenario(request, mode = "normal") {
  const response = await request.get(`${demoApi}/__demo/state?mode=${mode}`);
  expect(response.ok()).toBeTruthy();
}

async function login(page, username = "demo", password = "demo") {
  await page.goto("/");
  await page.getByLabel("用户名").fill(username);
  await page.getByLabel("口令").fill(password);
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await expect(page).toHaveURL(/\/workbench$/);
  await expect(page.getByRole("heading", { name: "工作台", exact: true })).toBeVisible();
}

test.beforeEach(async ({ request }) => {
  await setScenario(request);
});

test.afterEach(async ({ request }) => {
  await setScenario(request);
});

test("核心数据接口和主要页面使用完整模拟场景", async ({ page, request }) => {
  const loginResponse = await request.post(`${demoApi}/api/v1/auth/login`, {
    data: { username: "demo", password: "demo" },
  });
  const token = (await loginResponse.json()).token;
  for (const [path, minimumRows] of [
    ["/api/v1/process-data-models", 3],
    ["/api/v1/process-executions", 6],
    ["/api/v1/inspection-tasks?status=all", 3],
    ["/api/edges", 3],
  ]) {
    const response = await request.get(`${demoApi}${path}`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(response.ok(), path).toBeTruthy();
    const body = await response.json();
    expect(body.items?.length ?? body.data?.length ?? body.length, path).toBeGreaterThanOrEqual(minimumRows);
  }

  await login(page);
  await expect(page.getByText("禁止分析", { exact: true })).toBeVisible();
  await expect(page.getByRole("navigation", { name: "主导航" })).not.toContainText("系统管理");

  for (const [path, heading, evidence] of [
    ["/configuration/process-data-models", "工艺数据字典", "精密光学模压工艺"],
    ["/process-executions", "运行记录", "RUN-2026-0821-005"],
    ["/inspections", "检验任务", "LENS-006"],
    ["/edges", "现场节点", "上海一号压机节点"],
    ["/research-projects", "研发项目", "面形误差候选原因验证"],
  ]) {
    await page.goto(path);
    await expect(page.getByRole("heading", { name: heading, exact: true }).first()).toBeVisible();
    await expect(page.getByText(evidence, { exact: false }).first()).toBeVisible();
  }
});

test("权限边界和管理员导航一致", async ({ page }) => {
  await login(page);
  await page.goto("/identity/users");
  await expect(page.getByRole("heading", { name: "当前岗位不能访问此功能" })).toBeVisible();
  await expect(page.getByText("该页面仅向平台管理员开放。", { exact: false })).toBeVisible();

  await page.evaluate(() => sessionStorage.clear());
  await login(page, "admin", "admin12345");
  await expect(page.getByRole("navigation", { name: "主导航" })).toContainText("系统管理");
  await page.goto("/identity/users");
  await expect(page.getByRole("heading", { name: "用户与权限", exact: true }).first()).toBeVisible();
});

test("接口失败有可恢复路径", async ({ page, request }) => {
  await login(page);
  await setScenario(request, "error");
  await page.goto("/data-quality");
  await expect(page.getByText("数据暂时无法读取", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "重试" })).toBeVisible();

  await setScenario(request, "normal");
  await page.getByRole("button", { name: "重试" }).click();
  await expect(page.getByText("数据暂时无法读取", { exact: true })).toHaveCount(0);
  await expect(page.getByText("数据可信度确认后的下一步", { exact: true })).toBeVisible();
});

test("窄屏导航和宽表仍可操作", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(page);
  await expect(page.getByRole("button", { name: "打开主导航" })).toBeVisible();
  await page.getByRole("button", { name: "打开主导航" }).click();
  await expect(page.getByRole("navigation", { name: "主导航" })).toBeVisible();
  await page.getByRole("button", { name: "关闭模块导航" }).click();

  await page.goto("/process-executions");
  await expect(page.getByText("左右滑动查看全部字段；操作列固定在右侧。", { exact: true })).toBeVisible();
  await expect(page.getByText("RUN-2026-0821-005", { exact: true }).first()).toBeVisible();
});

test("全部业务导航和关键详情深链可达", async ({ page }) => {
  const pageErrors = [];
  page.on("pageerror", error => pageErrors.push(error.message));
  await login(page);

  const routes = [
    "/workbench",
    "/configuration",
    "/configuration/process-data-models",
    "/configuration/process-specifications",
    "/configuration/process-analysis-plans",
    "/configuration/inspection-definitions",
    "/configuration/quality-plans",
    "/configuration/component-types",
    "/configuration/components",
    "/configuration/tooling-types",
    "/configuration/tooling-assemblies",
    "/configuration/scenario-packages",
    "/edges",
    "/configuration/ingestion-tasks",
    "/process-executions",
    "/events",
    "/explorer",
    "/production/changeover",
    "/production/tooling-installations",
    "/inspections",
    "/quality-analysis",
    "/analysis",
    "/comparisons",
    "/data-quality",
    "/chat",
    "/research-projects",
    "/research-assets",
    "/platform-metrics",
    "/process-executions/RUN-2026-0821-005?siteId=SITE-001",
    "/edges/edge-shanghai-01",
    "/configuration/ingestion-tasks/INGEST-PRESS-01",
    "/research-projects/research-active",
  ];

  for (const route of routes) {
    pageErrors.length = 0;
    await page.goto(route);
    await expect(page.locator("main h1, main h2").first(), route).toBeVisible();
    await expect(page.getByText("数据暂时无法读取", { exact: true }), route).toHaveCount(0);
    expect(pageErrors, route).toEqual([]);
    await expect(page.getByRole("navigation", { name: "面包屑" }), route).toBeVisible();
  }
});

test("全局搜索、面包屑和系统管理深链一致", async ({ page }) => {
  await login(page, "admin", "admin12345");
  await page.getByRole("button", { name: "打开功能搜索" }).click();
  await page.getByPlaceholder("例如：数据源配置、工艺规范、运行对比、检验任务").fill("运行对比");
  await page.getByText("运行对比", { exact: true }).last().click();
  await expect(page).toHaveURL(/\/comparisons$/);
  await expect(page.getByRole("navigation", { name: "面包屑" })).toContainText("运行对比");

  for (const [route, heading] of [
    ["/identity/users", "用户与权限"],
    ["/logs", "平台日志"],
    ["/golden-questions", "评测问题集"],
  ]) {
    await page.goto(route);
    await expect(page.getByRole("heading", { name: heading, exact: true }).first(), route).toBeVisible();
  }
});

test("模拟场景覆盖空态、加载态、权限态和生命周期状态", async ({ page, request }) => {
  await login(page);

  await page.goto("/configuration/process-data-models");
  for (const status of ["草稿", "已发布", "已停用"]) {
    await expect(page.getByText(status, { exact: true }).first()).toBeVisible();
  }
  await page.goto("/edges");
  for (const status of ["在线", "运行异常", "离线"]) {
    await expect(page.getByText(status, { exact: true }).first()).toBeVisible();
  }
  await page.goto("/research-projects");
  for (const status of ["草稿", "研发中", "验证中", "已完成", "已归档"]) {
    await expect(page.getByText(status, { exact: true }).first()).toBeVisible();
  }

  await setScenario(request, "empty");
  for (const [route, emptyText] of [
    ["/configuration/process-data-models", "还没有工艺数据字典"],
    ["/process-executions", "还没有形成生产运行"],
    ["/inspections", "暂无数据"],
    ["/edges", "暂无数据"],
  ]) {
    await page.goto(route);
    await expect(page.getByText(emptyText, { exact: true }).first(), route).toBeVisible();
  }

  await setScenario(request, "slow");
  await page.goto("/configuration/process-data-models");
  await expect(page.getByText("正在读取数据", { exact: true })).toBeVisible();
  await expect(page.getByText("精密光学模压工艺", { exact: true })).toBeVisible();

  await setScenario(request, "forbidden");
  await page.goto("/data-quality");
  await expect(page.getByText("当前岗位无权读取这些数据", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "重试" })).toBeVisible();
});

test("危险操作有确认，证据边界和业务下一步可见", async ({ page }) => {
  await login(page);
  await page.goto("/configuration/process-data-models");
  await page.getByRole("button", { name: "删除草稿", exact: true }).click();
  const confirmation = page.getByRole("dialog");
  await expect(confirmation).toContainText("草稿删除后无法恢复");
  await expect(confirmation.getByRole("button", { name: "确认删除" })).toBeVisible();
  await confirmation.getByRole("button", { name: "取消" }).click();

  await page.goto("/inspections");
  await expect(page.getByText("完成检验后的下一步", { exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "检查数据可信度" })).toBeVisible();
  await page.goto("/data-quality");
  await expect(page.getByText("数据可信度确认后的下一步", { exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "进入运行对比" })).toBeVisible();

  await page.goto("/comparisons");
  await page.getByRole("button", { name: "生成对比结论", exact: true }).click();
  await expect(page.getByText("观察结果只形成待验证候选", { exact: false })).toBeVisible();
  await expect(page.getByText("因果关系仍需后续受控实验验证", { exact: false })).toBeVisible();

  await page.goto("/research-projects?create=1&executionId=RUN-2026-0821-005&comparisonExecutionIds=RUN-2026-0821-005,RUN-2026-0821-004");
  const projectDialog = page.getByRole("dialog");
  await expect(projectDialog.getByText("创建工艺研发项目", { exact: true })).toBeVisible();
  await expect(projectDialog.getByRole("combobox").first()).toHaveValue("RUN-2026-0821-005");
});

test("核心模块接口失败后均可原地重试恢复", async ({ page, request }) => {
  await login(page);
  for (const [route, recoveredEvidence] of [
    ["/configuration/process-data-models", "精密光学模压工艺"],
    ["/edges", "上海一号压机节点"],
    ["/process-executions", "RUN-2026-0821-005"],
    ["/inspections", "LENS-006"],
    ["/research-projects", "面形误差候选原因验证"],
    ["/research-assets", "光学模压标准作业指导书"],
  ]) {
    await setScenario(request, "error");
    await page.goto(route);
    const retry = page.getByRole("button", { name: "重试" }).first();
    await expect(retry, route).toBeVisible();
    await setScenario(request, "normal");
    await retry.click();
    await expect(page.getByText(recoveredEvidence, { exact: false }).first(), route).toBeVisible();
  }
});

test("机理知识从业务选择映射到可追溯实验建议", async ({ page }) => {
  await login(page);
  await page.goto("/research-assets");
  await page.getByLabel("当前研发项目").selectOption({ label: "面形误差候选原因验证" });

  await expect(page.getByText("已激活的模压安全窗口", { exact: true })).toBeVisible();
  await expect(page.getByText("待审核的冷却速率观察", { exact: true })).toBeVisible();
  await expect(page.getByText("不适用于当前设备的声明", { exact: true })).toBeVisible();
  await expect(page.getByText("已反证的升温收益声明", { exact: true })).toBeVisible();
  await expect(page.getByText("已停用的旧材料经验", { exact: true })).toBeVisible();
  await expect(page.getByText("相同产品范围内对温度影响方向的判断相反", { exact: false })).toBeVisible();

  await page.getByLabel("项目变量").selectOption("holding.temperature");
  await expect(page.getByLabel("作用变量单位")).toHaveValue("°C");
  await expect(page.getByLabel("作用变量单位")).toHaveAttribute("readonly", "");

  await page.getByLabel("适用维度").selectOption("equipment");
  await expect(page.getByLabel("适用对象")).toHaveValue("PRESS-01");
  await expect(page.getByLabel("适用对象").locator("option")).toHaveCount(1);

  await page.goto("/research-projects/research-active");
  const knowledgeSummary = page.getByText("本次采用的机理知识 · 1 条", { exact: true });
  await expect(knowledgeSummary).toBeVisible();
  await knowledgeSummary.click();
  await expect(page.getByText("硬边界", { exact: true })).toBeVisible();
  await expect(page.getByText("候选偏好", { exact: true })).toBeVisible();
  await expect(page.getByText("高温高压联合禁区", { exact: true })).toBeVisible();
});
