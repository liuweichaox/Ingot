// 验证前端 research-project-model 的渲染、交互、错误和边界状态。

import assert from "node:assert/strict";
import test from "node:test";
import {
  createTaskForm,
  experimentScale,
  nextProjectAction,
  projectFormInitial,
  statusLabels,
} from "../src/research/researchProjectModel.js";

test("research project model preserves lifecycle and localized statuses", () => {
  assert.deepEqual(nextProjectAction("draft"), ["开始研发", "active"]);
  assert.deepEqual(nextProjectAction("active"), ["进入验证", "validating"]);
  assert.deepEqual(nextProjectAction("validating"), ["完成项目", "completed"]);
  assert.equal(nextProjectAction("completed"), null);
  assert.equal(statusLabels["awaiting-approval"], "等待批准");
  assert.equal(projectFormInitial.objectiveDirection, "minimize");
});

test("research project model derives experiment scale and task defaults", () => {
  const workspace = {
    project: {
      variables: [{ code: "temperature", role: "control", lowerLimit: 120, upperLimit: 180 }],
    },
    hypotheses: [{ hypothesisId: "hypothesis-1" }],
    operatingRegions: [{ operatingRegionId: "region-1", status: "validated", validationLevel: "laboratory" }],
    transferAssessments: [{ assessmentId: "transfer-1", status: "reviewed", outcome: "beneficial" }],
    transferSources: [{ operatingRegionId: "source-region-1" }],
    experimentResults: [{ resultId: "result-1" }, { resultId: "result-2" }],
  };

  const form = createTaskForm("experiment", workspace);
  assert.equal(form.variableCode, "temperature");
  assert.equal(form.low, 120);
  assert.equal(form.high, 180);
  assert.equal(form.hypothesisId, "hypothesis-1");
  assert.equal(form.operatingRegionId, "region-1");
  assert.equal(form.transferAssessmentId, "transfer-1");
  assert.deepEqual(experimentScale({
    runPlan: [
      { factors: [{ variableCode: "temperature", value: 120 }] },
      { factors: [{ variableCode: "temperature", value: 120 }] },
      { factors: [{ variableCode: "temperature", value: 180 }] },
      { factors: [{ variableCode: "temperature", value: 180 }] },
    ],
  }), { distinctConditions: 2, replicates: 2 });
});
