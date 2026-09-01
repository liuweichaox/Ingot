import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

// 静态守卫：产品 UI 只保留建议、工程师决策和真实生产结果的单一闭环。
const [page, workspace, drawers, model] = await Promise.all([
  readFile(new URL("../src/pages/ResearchProjectsPage.jsx", import.meta.url), "utf8"),
  readFile(new URL("../src/research/components/ResearchWorkspaceContent.jsx", import.meta.url), "utf8"),
  readFile(new URL("../src/research/components/ResearchProjectDrawers.jsx", import.meta.url), "utf8"),
  readFile(new URL("../src/research/researchProjectModel.js", import.meta.url), "utf8"),
]);
const researchProjects = [page, workspace, drawers, model].join("\n");

test("daily recipe recommendations form the only active project loop", () => {
  assert.match(researchProjects, /recipeRecommendationFlows/);
  assert.match(researchProjects, /生成下一配方建议/);
  assert.match(researchProjects, /登记下一配方决定/);
  assert.match(researchProjects, /关联实际生产运行/);
  assert.match(researchProjects, /冻结实际结果/);
  assert.match(researchProjects, /recipe-recommendation-decisions/);
  assert.match(researchProjects, /execution-link/);
  assert.match(researchProjects, /materialize-outcome/);
  assert.match(researchProjects, /工程师最终决定、实际生产运行和源数据结果，形成不可覆盖的闭环证据/);
});

test("project UI no longer reads or calls retired validation workflows", () => {
  for (const retiredTerm of ["experiments", "shadow-recommendations", "controlled-decision", "import-history", "experiment-results"]) {
    assert.doesNotMatch(researchProjects, new RegExp(retiredTerm, "i"));
  }
  assert.doesNotMatch(researchProjects, /设计受控验证/);
  assert.doesNotMatch(researchProjects, /影子建议/);
});
