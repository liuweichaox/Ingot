import assert from "node:assert/strict";
import { test } from "node:test";
import { briefForRecipe, runScopes } from "../src/recipe/proposalScope.js";

const specification = { processSpecificationId: "recipe", version: 1 };
const model = { controlParameters: [{ code: "temperature", unit: "C", minimum: 10, maximum: 20 }] };
const characteristic = { code: "quality", unit: "mm", lowerLimit: 1, upperLimit: 2 };
const run = { processSpecificationId: "recipe", processSpecificationVersion: "1", productFamilyCode: "family", productCode: "product", equipmentId: "equipment" };

function brief(runs, selectedScope) {
  return briefForRecipe({ specification, model, characteristic, siteCode: "site", runs, selectedScope });
}

test("marks missing scope fields explicitly instead of widening the production cohort", () => {
  const incomplete = { ...run, productFamilyCode: null };
  const runs = [incomplete, run];
  const selectedScope = runScopes(runs).find(scope => scope.values[0] === "").key;
  const result = brief(runs, selectedScope);
  assert.equal(result.context.missing_scope_fields, "product_family_code");
  assert.equal(result.context.product_code, "product");
});

test("keeps every field of a complete selected scope", () => {
  const result = brief([run]);
  assert.equal(result.context.product_family_code, "family");
  assert.equal(result.context.product_code, "product");
  assert.equal(result.context.equipment_id, "equipment");
  assert.equal(result.context.missing_scope_fields, undefined);
});

test("records each missing scope dimension", () => {
  const incomplete = { ...run, productFamilyCode: "", productCode: null, equipmentId: "" };
  const result = brief([incomplete]);
  assert.equal(result.context.missing_scope_fields, "product_family_code,product_code,equipment_id");
});

test("rejects a stale selected scope and an empty run list", () => {
  assert.throws(() => brief([run], '["old","product","equipment"]'), /工况/);
  assert.throws(() => brief([]), /运行/);
});
