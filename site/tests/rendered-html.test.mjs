import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import test, { after } from "node:test";
import ts from "typescript";

const siteRoot = fileURLToPath(new URL("..", import.meta.url));
const testPort = 3127;
let serverProcess;
let serverOutput = "";

async function render(pathname = "/") {
  if (!serverProcess) {
    const nextBin = fileURLToPath(
      new URL("../node_modules/next/dist/bin/next", import.meta.url),
    );
    serverProcess = spawn(
      process.execPath,
      [nextBin, "start", "--hostname", "127.0.0.1", "--port", String(testPort)],
      {
        cwd: siteRoot,
        env: { ...process.env, NODE_ENV: "production" },
        stdio: ["ignore", "pipe", "pipe"],
      },
    );
    serverProcess.stdout.on("data", (chunk) => {
      serverOutput += chunk;
    });
    serverProcess.stderr.on("data", (chunk) => {
      serverOutput += chunk;
    });
  }

  const deadline = Date.now() + 20_000;
  while (Date.now() < deadline) {
    if (serverProcess.exitCode !== null) {
      throw new Error(`Next.js exited before the test request:\n${serverOutput}`);
    }
    try {
      return await fetch(`http://127.0.0.1:${testPort}${pathname}`, {
        headers: {
          accept: "text/html",
          "x-forwarded-host": "untrusted.invalid",
          "x-forwarded-proto": "https",
        },
      });
    } catch {
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
  }

  throw new Error(`Next.js did not become ready:\n${serverOutput}`);
}

after(() => serverProcess?.kill("SIGTERM"));

async function loadFactoryModel() {
  const sourceUrl = new URL("../app/factoryStages.ts", import.meta.url);
  const source = await readFile(sourceUrl, "utf8");
  const javascript = ts.transpileModule(source, {
    compilerOptions: {
      module: ts.ModuleKind.ESNext,
      target: ts.ScriptTarget.ES2022,
    },
    fileName: sourceUrl.pathname,
  }).outputText;

  return import(`data:text/javascript;base64,${Buffer.from(javascript).toString("base64")}`);
}

function eventDataValue(data, key) {
  if (data && typeof data === "object") {
    return data[key];
  }

  const match = String(data ?? "").match(new RegExp(`(?:^|\\s*[·,]\\s*)${key}=([^·,]+)`));
  return match?.[1]?.trim();
}

function collectEventTypes(value, types = []) {
  if (!value || typeof value !== "object") return types;

  if (typeof value.eventType === "string") types.push(value.eventType);
  for (const child of Object.values(value)) collectEventTypes(child, types);
  return types;
}

test("renders the Ingot product site and social metadata", async () => {
  const response = await render();
  assert.equal(response.status, 200);
  assert.match(response.headers.get("content-type") ?? "", /^text\/html\b/i);
  assert.equal(response.headers.get("x-content-type-options"), "nosniff");
  assert.equal(response.headers.get("x-frame-options"), "DENY");
  assert.equal(response.headers.get("referrer-policy"), "strict-origin-when-cross-origin");
  assert.equal(response.headers.get("permissions-policy"), "camera=(), microphone=(), geolocation=()");

  const html = await response.text();
  assert.match(html, /<title>Ingot — 可信生产事实、Chat 与桌面连接器 Agent<\/title>/i);
  assert.match(html, /TRUSTED FACTS \/ CHAT \/ DESKTOP AGENT/);
  assert.match(html, /用 Chat 查找问题/);
  assert.match(html, /用桌面 Agent 编写连接器/);
  assert.match(html, /https:\/\/docs\.ingotstack\.com/);
  assert.match(html, /https:\/\/github\.com\/liuweichaox\/Ingot\/releases\/latest/);
  assert.match(html, /标准事件有界上报/);
  assert.match(html, /diagnostic\.backlog_dropped/);
  assert.match(html, /连接器事件与检测事实，使用统一标识回链/);
  assert.match(html, /桌面 Agent 完成连接器工程/);
  assert.match(html, /awaiting-package-approval/);
  assert.match(html, /SHA-256 ZIP/);
  assert.match(html, /Tauri 2/);
  assert.match(html, /浏览器没有 Agent 代码生成入口/);
  assert.match(html, /示例检测记录/);
  assert.match(html, /检测事实进入 Central/);
  assert.match(html, /本地事件入口/);
  assert.match(html, /不提供高频时序趋势或自动质量关联/);
  assert.match(html, /不负责排产、库存、物流、质量处置或设备控制/);
  assert.match(html, /程序\/刀具 · 主轴\/进给 · 周期\/报警/);
  assert.match(html, /示例事实：工件 ID · 粗糙度 Ra · 仪器 · 检测员/);
  assert.match(html, /查看工程字段/);
  assert.match(html, /ingot-lockup-dark\.svg/i);
  assert.match(html, />EN</i);
  assert.match(html, /cycle\.completed/);
  assert.match(html, /cycle\.started/);
  assert.doesNotMatch(html, /cycle\.aborted|inspection\.completed|x\.quality\.|\bNG\b/);
  assert.match(html, /CONNECTOR EVENTS/i);
  assert.match(html, /CENTRAL FACTS/i);
  assert.match(html, /https:\/\/ingotstack\.com\/og\.png/i);
  assert.doesNotMatch(html, /untrusted\.invalid/i);
  assert.doesNotMatch(html, /codex-preview|Your site is taking shape/i);
  assert.doesNotMatch(html, /Ingot 直接连接|自动整理工件生产履历|高频设备数据保留/);
  assert.doesNotMatch(html, /AI 分析 Agent|多 Agent|AI Agent 原生/);
  assert.doesNotMatch(html, /test_http_connector|ConnectorTest|AllowedNetworkTargets/i);

  const pageSource = await readFile(new URL("../app/IngotSite.tsx", import.meta.url), "utf8");
  assert.match(pageSource, /示例事实/);
  assert.match(pageSource, /check_data_quality/);
  assert.match(pageSource, /get_cycle_trace/);
  assert.match(pageSource, /Chat 只调用白名单中的只读事实工具/);
  assert.match(pageSource, /Chat 位于 Central Web/);
  assert.match(pageSource, /桌面端通过 Rust 原生边界连接 Central/);
  assert.match(pageSource, /授权 Actor 审查后批准打包/);
  assert.match(pageSource, /does not deploy connectors or control equipment/i);
  assert.match(pageSource, /SHA-256 ZIP/);
  assert.match(pageSource, /DESKTOP AGENT CONNECTOR ENGINEERING/i);
  assert.match(pageSource, /Sample workflow: machine-side inspection/i);
  assert.match(pageSource, /周期事实与数据质量/);
  assert.match(pageSource, /Cycle facts and data quality/i);
  assert.match(pageSource, /platform\.analysis\.chat/);
  assert.match(pageSource, /const \[platformView, setPlatformView\]/);
  assert.match(pageSource, /setPlatformView\("analysis"\)/);
  assert.doesNotMatch(pageSource, /setEvidencePart|analysisDimension/);
  assert.doesNotMatch(pageSource, /AI Analysis Agent|Bounded multi-Agent|AI 分析 Agent|深度多 Agent/);
  assert.doesNotMatch(pageSource, /test_http_connector|ConnectorTest|AllowedNetworkTargets/i);
  assert.doesNotMatch(pageSource, /RELEASE REQUESTED|first_article_attested/i);
});

test("renders the stable English route and localized metadata", async () => {
  const response = await render("/en/");
  assert.equal(response.status, 200);
  const html = await response.text();
  assert.match(html, /<title>Ingot — Trusted production facts, Chat, and desktop connector Agent<\/title>/i);
  assert.match(html, /Use Chat to find problems/i);
  assert.match(html, /Use the desktop Agent to write connectors/i);
  assert.match(html, /DESKTOP AGENT CONNECTOR ENGINEERING/);
  assert.match(html, /Tauri 2/);
  assert.match(html, /awaiting-package-approval/);
  assert.match(html, /SHA-256 ZIP/);
  assert.match(html, /github\.com\/liuweichaox\/Ingot\/releases\/latest/);
  assert.doesNotMatch(html, /AI Analysis Agent|multi-Agent|Agent-native process analytics/i);
  assert.match(html, /<html lang="en">/);
  assert.match(html, /rel="canonical" href="https:\/\/ingotstack\.com\/en\/"/i);
  assert.match(html, /hreflang="zh-CN"/i);
});

test("drives the hero data card, station camera, animation, and data route from one stage index", async () => {
  const [pageSource, sceneSource, stageSource] = await Promise.all([
    readFile(new URL("../app/IngotSite.tsx", import.meta.url), "utf8"),
    readFile(new URL("../app/FactoryScene3D.tsx", import.meta.url), "utf8"),
    readFile(new URL("../app/factoryStages.ts", import.meta.url), "utf8"),
  ]);

  const views = [...stageSource.matchAll(/view: "([^"]+)"/g)].map((match) => match[1]);
  assert.deepEqual(views, ["feeding", "machining", "transfer", "inspection", "manual"]);

  const stageDefinitions = stageSource.slice(stageSource.indexOf("export const FACTORY_STAGES"));
  const captures = [...stageDefinitions.matchAll(/capture: "([^"]+)"/g)].map((match) => match[1]);
  assert.deepEqual(captures, ["automatic", "automatic", "automatic", "automatic", "hybrid"]);
  assert.match(stageSource, /recordType: "manual_inspection_record"/);
  assert.doesNotMatch(stageSource, /PACK-01|packaging_record|view: "packing"/);

  assert.match(pageSource, /const stageMeta = FACTORY_STAGES/);
  assert.match(pageSource, /FACTORY_STAGE_DATA/);
  assert.match(pageSource, /const factoryView = factoryViews\[liveIndex\]/);
  assert.match(pageSource, /activeStage=\{liveIndex\}/);
  assert.doesNotMatch(pageSource, /cncCompleted|CNC_COMPLETION_EVENT_AT|CNC_CYCLE_EVENTS/);
  assert.match(pageSource, /stageRevision=\{stageRevision\}/);
  assert.match(pageSource, /onClick=\{\(\) => selectLiveStage\(index\)\}/);
  assert.match(pageSource, /setFactoryPlaying\(false\)/);
  assert.match(pageSource, /paused=\{!factoryPlaying\}/);
  assert.match(pageSource, /DESKTOP AGENT CONNECTOR ENGINEERING/);
  assert.match(pageSource, /INSPECTION FACTS IN CENTRAL/);
  assert.match(pageSource, /const physicalActive = active < 4 \? active : -1/);
  assert.match(pageSource, /const manualActive = active === 4/);
  assert.match(pageSource, /drawManualQa\(qaX, qaY/);
  assert.doesNotMatch(pageSource, /setFactoryView|9000/);

  assert.match(sceneSource, /useProcessClock\(activeStage, stageRevision, paused, reducedMotion\)/);
  assert.match(sceneSource, /FACTORY_STAGE_MS \/ 1000/);
  assert.match(sceneSource, /DataFabric activeStage=\{activeStage\}/);
  assert.match(sceneSource, /IndustrialEdgeCabinet activeStage=\{activeStage\}/);
  assert.match(sceneSource, /active=\{index === activeStage\}/);
  assert.match(sceneSource, /ManualInspectionHmiDisplay width=\{0\.9\}/);
  assert.match(sceneSource, /CellTechnician active=\{activeStage === 4\}/);
  assert.match(sceneSource, /SURFACE ROUGHNESS/);
  assert.match(sceneSource, /IDENTIFY INSPECTOR/);
  assert.match(sceneSource, /OPERATE TESTER/);
  assert.match(sceneSource, /solveInspectionArmPose/);
  assert.match(sceneSource, /BADGE_READER_ARM_POSE/);
  assert.match(sceneSource, /SAVE_BUTTON_ARM_POSE/);
  assert.match(sceneSource, /InspectionExitBuffer/);
  assert.doesNotMatch(sceneSource, /PackingCell|CartonShell|activeStage === 5/);
  assert.match(sceneSource, /const ROBOT_CNC_FIXTURE = new THREE\.Vector3\(-3\.55, 0\.77, -1\.28\)/);
  assert.match(sceneSource, /const ROBOT_CNC_APPROACH = new THREE\.Vector3\(-3\.55, 0\.77, -0\.42\)/);
  assert.match(sceneSource, /stage === 0[\s\S]*ROBOT_RAW_PICK[\s\S]*ROBOT_CNC_FIXTURE/);
  assert.match(sceneSource, /stage !== 2[\s\S]*ROBOT_CNC_APPROACH[\s\S]*ROBOT_OUTPUT_PLACE/);
  assert.match(sceneSource, /robotTarget\(0, progress, nextPosition\)/);
  assert.match(sceneSource, /nextPosition\.copy\(ROBOT_CNC_FIXTURE\)/);
  assert.match(sceneSource, /Fixed internal bed: no conveyor or shuttle passes beneath the machine/);
  assert.match(sceneSource, /Only the downstream line begins at the robot's output nest/);
  assert.match(sceneSource, /CncMachine activeStage=\{activeStage\}/);
  assert.match(sceneSource, /RobotCell activeStage=\{activeStage\}/);
  assert.doesNotMatch(sceneSource, /tableSlide|ConveyorSegment start=\{-8\.9\}|CNC_INSIDE|ROBOT_CNC_LOAD/);
  assert.match(sceneSource, /new THREE\.Vector3\(-3, 1\.46, 1\.94\)/);
  assert.match(sceneSource, /stage === 4[\s\S]*nextPosition\.x = 5/);
  assert.match(sceneSource, /delayed=\{index === 4\}/);
});

test("reserves lifecycle events for core CNC machining and models every other station as data", async () => {
  const [pageSource, sceneSource, stageSource] = await Promise.all([
    readFile(new URL("../app/IngotSite.tsx", import.meta.url), "utf8"),
    readFile(new URL("../app/FactoryScene3D.tsx", import.meta.url), "utf8"),
    readFile(new URL("../app/factoryStages.ts", import.meta.url), "utf8"),
  ]);
  const { FACTORY_STAGES, FACTORY_STAGE_DATA } = await loadFactoryModel();

  assert.equal(FACTORY_STAGES.length, 5);
  assert.equal(FACTORY_STAGE_DATA.length, 5);
  assert.deepEqual(
    FACTORY_STAGES.map(({ code }) => code),
    ["ROBOT-02 · LOAD", "CNC-07", "ROBOT-02 · UNLOAD", "VISION-03", "QA-HMI-01"],
  );

  const machining = FACTORY_STAGE_DATA[1];
  assert.equal(machining.kind, "machining");
  assert.equal(machining.started.eventType, "cycle.started");
  assert.equal(machining.completed.eventType, "cycle.completed");
  assert.equal(eventDataValue(machining.started.data, "operation"), "machining");
  assert.equal(eventDataValue(machining.completed.data, "operation"), "machining");
  assert.equal(machining.started.correlationId, machining.completed.correlationId);

  for (const index of [0, 2, 3, 4]) {
    assert.equal(FACTORY_STAGE_DATA[index].kind, "record", `${FACTORY_STAGES[index].code} must not be a lifecycle event`);
    assert.deepEqual(collectEventTypes(FACTORY_STAGE_DATA[index]), []);
  }
  assert.deepEqual(collectEventTypes(FACTORY_STAGE_DATA), ["cycle.started", "cycle.completed"]);

  const visionResult = FACTORY_STAGE_DATA[3].settled;
  assert.equal(visionResult.recordType, "inspection_result");
  assert.equal(eventDataValue(visionResult.data, "result"), "PASS");

  const manualInspection = FACTORY_STAGE_DATA[4].settled;
  assert.equal(manualInspection.recordType, "manual_inspection_record");
  assert.equal(eventDataValue(manualInspection.data, "inspection_status"), "RECORDED");
  assert.equal(eventDataValue(manualInspection.data, "result"), "PASS");
  assert.equal(eventDataValue(manualInspection.data, "surface_roughness_ra_um"), "0.82");
  assert.equal(eventDataValue(manualInspection.data, "instrument"), "ROUGHNESS-01");
  assert.equal(eventDataValue(manualInspection.data, "calibration_status"), "VALID");

  assert.equal((stageSource.match(/view: "machining"/g) ?? []).length, 1);
  assert.doesNotMatch(stageSource, /cycle\.aborted|inspection\.completed|x\.quality\.|quality\.first_article_attested/);

  assert.match(pageSource, /FACTORY_STAGE_DATA\[liveIndex\]/);
  assert.match(pageSource, /setLiveIndex\(\(liveIndex \+ 1\) % stageMeta\.length\)/);
  assert.match(pageSource, /if \(!factoryPlaying/);
  assert.match(pageSource, /factory-event-pair/);
  assert.match(pageSource, /stageSettled \? liveStageData\.completed\.time : "—"/);
  assert.match(pageSource, /String\(liveDatum\.sequence\)/);
  assert.doesNotMatch(pageSource, /inspection\.completed|x\.quality\.|cycle\.aborted|\bNG\b/);
  assert.match(sceneSource, /MACHINING ENDED/);
  assert.match(sceneSource, /frameloop=\{reducedMotion \|\| paused \? "demand" : "always"\}/);
  assert.match(sceneSource, /INSPECTION RESULT SAVED/);
  assert.doesNotMatch(sceneSource, /APPROVED|RELEASE REQUESTED|QUALITY ATTESTATION/i);
  assert.match(sceneSource, /function VisionStation\(\{ active, stageSettled, process \}/);
  assert.match(sceneSource, /RESULT PENDING/);
  assert.doesNotMatch(sceneSource, /CYCLE VIEW ×12|INS-182343|\bNG\b/);
  assert.doesNotMatch(sceneSource, /cncEventCompleted|CNC_COMPLETION_EVENT_AT|CNC_CYCLE_EVENTS/);
});
