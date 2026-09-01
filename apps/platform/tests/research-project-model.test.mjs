// 验证下一配方决定载荷和项目生命周期辅助函数。
import assert from "node:assert/strict";
import test from "node:test";
import {
  buildRecipeRecommendationDecisionPayload,
  canArchiveProject,
  nextProjectAction,
  projectFormInitial,
  statusLabels,
} from "../src/research/researchProjectModel.js";

test("rejected daily recommendation decisions omit parameter selections", () => {
  const payload = buildRecipeRecommendationDecisionPayload({
    parameters: [{ variableCode: "temperature", value: 620, unit: "C" }],
  }, {
    decision: "rejected",
    factors: { temperature: "640" },
    reason: "现场边界不允许",
    usefulnessRating: "partly-useful",
  });

  assert.deepEqual(payload.engineerSelectedParameters, []);
  assert.equal(payload.reason, "现场边界不允许");
  assert.equal(payload.usefulnessRating, "partly-useful");
});

test("project lifecycle and localized statuses support the production-evidence workflow", () => {
  assert.deepEqual(nextProjectAction("draft"), ["开始研发", "active"]);
  assert.deepEqual(nextProjectAction("active"), ["完成项目", "completed"]);
  assert.equal(nextProjectAction("completed"), null);
  assert.equal(canArchiveProject("draft"), true);
  assert.equal(canArchiveProject("completed"), true);
  assert.equal(canArchiveProject("active"), false);
  assert.equal(statusLabels.production, "生产运行");
  assert.equal(projectFormInitial.objectiveDirection, "minimize");
});
