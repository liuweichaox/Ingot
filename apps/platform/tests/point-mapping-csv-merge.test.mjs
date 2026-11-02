// Verify linear-time CSV mapping merges preserve matching, partial-update, and ordering behavior.
import assert from "node:assert/strict";
import test from "node:test";
import { mergePointMappingRows } from "../src/acquisition/pointMappingCsvMerge.js";

test("merge matches target codes without case sensitivity and updates only provided columns", () => {
  const existing = [
    { dataItemCode: "temperature", sourcePath: "/old-temperature", sourceUnit: "C", required: true },
    { dataItemCode: "pressure", sourcePath: "/pressure", sourceUnit: "kPa", required: false },
  ];
  const imported = [
    { dataItemCode: "TEMPERATURE", sourcePath: "/new-temperature", sourceUnit: "", required: false },
    { dataItemCode: "flow", sourcePath: "/flow", sourceUnit: "L/min", required: true },
  ];

  const merged = mergePointMappingRows(existing, imported, [["sourcePath"], ["sourcePath", "sourceUnit"]]);

  assert.deepEqual(merged, [
    { dataItemCode: "TEMPERATURE", sourcePath: "/new-temperature", sourceUnit: "C", required: false },
    existing[1],
    imported[1],
  ]);
});

test("10,000 existing and imported mappings inspect each existing key at most twice", () => {
  let existingKeyReads = 0;
  const existing = Array.from({ length: 10_000 }, (_, index) => new Proxy({
    dataItemCode: `point-${index}`,
    sourcePath: `/old/${index}`,
  }, {
    get(target, property, receiver) {
      if (property === "dataItemCode") existingKeyReads += 1;
      return Reflect.get(target, property, receiver);
    },
  }));
  const imported = Array.from({ length: 10_000 }, (_, index) => ({
    dataItemCode: `POINT-${index + 5_000}`,
    sourcePath: `/new/${index}`,
    required: false,
  }));

  const merged = mergePointMappingRows(existing, imported, imported.map(() => ["sourcePath"]));
  const keyReadsAfterMerge = existingKeyReads;

  assert.equal(merged.length, 15_000);
  assert.equal(merged[5_000].sourcePath, "/new/0");
  assert.equal(merged[4_999].dataItemCode, "point-4999");
  assert.equal(merged[10_000].dataItemCode, "POINT-10000");
  assert.equal(keyReadsAfterMerge, 15_000);
});
