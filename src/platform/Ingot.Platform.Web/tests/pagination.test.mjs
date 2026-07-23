import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const read = (path) => readFile(new URL(path, import.meta.url), "utf8");
const pagination = await read("../src/components/TablePagination.vue");
const composable = await read("../src/composables/useClientPagination.js");
const views = await Promise.all([
  "CyclesView.vue",
  "EventsView.vue",
  "DataQualityView.vue",
  "QualityAnalysisView.vue",
  "InspectionsView.vue",
  "ObjectExplorerView.vue",
  "MetricsView.vue",
  "EdgesView.vue",
  "ProductionSetupView.vue",
  "ProcessDataModelsView.vue",
  "RecipeVersionsView.vue",
  "ProcessAnalysisPlansView.vue",
  "InspectionDefinitionsView.vue",
  "QualityPlansView.vue",
  "SubscriptionsView.vue",
].map((name) => read(`../src/views/${name}`)));

test("long-running and maintenance lists use the shared pagination contract", () => {
  assert.match(pagination, /layout="total, sizes, prev, pager, next"|:layout=/);
  assert.match(composable, /slice\(start, start \+ pageSize\.value\)/);
  assert.match(composable, /pageCount/);
  for (const view of views) {
    assert.match(view, /TablePagination/);
  }
  for (const [index, view] of views.entries()) {
    if (![0, 1, 4].includes(index)) assert.match(view, /useClientPagination/);
  }
});

test("event history uses one server-backed pagination model", () => {
  const eventsView = views[1];
  assert.doesNotMatch(eventsView, /加载更早记录|loadMore|useClientPagination/);
  assert.match(eventsView, /params\.set\("offset"/);
  assert.match(eventsView, /eventTotal\.value = Number\(result\.total/);
});

test("inspection tasks and records use true server-backed pagination", () => {
  const inspectionsView = views[4];
  assert.doesNotMatch(inspectionsView, /useClientPagination|limit=200|limit=100/);
  assert.match(inspectionsView, /offset = \(recordPage\.value - 1\)/);
  assert.match(inspectionsView, /offset = \(taskPage\.value - 1\)/);
  assert.match(inspectionsView, /recordTotal\.value = result\.total/);
  assert.match(inspectionsView, /taskTotal\.value = result\.total/);
});

test("full-history analytical pages do not silently stop at the first backend page", () => {
  assert.match(views[0], /result\.total/);
  assert.match(views[0], /offset: String\(\(cyclePage\.value - 1\)/);
  for (const view of [views[2], views[3], views[5]]) {
    assert.match(view, /offset/);
    assert.match(view, /page\.length < 1000/);
  }
});
