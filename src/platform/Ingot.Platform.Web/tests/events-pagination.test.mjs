import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const source = readFileSync(new URL("../src/views/EventsView.vue", import.meta.url), "utf8");

test("event query pages through history without rendering the entire event store", () => {
  assert.match(source, /const eventPageSize = ref\(50\)/);
  assert.match(source, /params\.set\("offset"/);
  assert.match(source, /eventTotal\.value = Number\(result\.total/);
  assert.doesNotMatch(source, /beforeIngestId|加载更早记录|loadMore/);
  assert.doesNotMatch(source, /while \(true\)/);
});

test("live event stream starts after the newest loaded record", () => {
  assert.match(source, /params\.set\("afterIngestId", String\(latestIngestId\.value\)\)/);
});
