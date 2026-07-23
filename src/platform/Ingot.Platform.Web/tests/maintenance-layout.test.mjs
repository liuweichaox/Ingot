import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readView = (name) => readFile(new URL(`../src/views/${name}`, import.meta.url), "utf8");

const [definitions, qualityPlans, subscriptions, inspections] = await Promise.all([
  readView("InspectionDefinitionsView.vue"),
  readView("QualityPlansView.vue"),
  readView("SubscriptionsView.vue"),
  readView("InspectionsView.vue"),
]);

test("maintenance and entry pages keep lists primary and open forms in drawers", () => {
  for (const view of [definitions, qualityPlans, subscriptions, inspections]) {
    assert.match(view, /<el-table/);
    assert.match(view, /<el-drawer/);
  }

  assert.doesNotMatch(definitions, /<el-row :gutter="18">/);
  assert.doesNotMatch(qualityPlans, /<el-row :gutter="18">/);
  assert.doesNotMatch(subscriptions, /<el-dialog/);
});
