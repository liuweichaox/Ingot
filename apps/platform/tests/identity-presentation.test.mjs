// 验证前端 identity-presentation 的渲染、交互、错误和边界状态。

import assert from "node:assert/strict";
import test from "node:test";
import { formatRoleSummary, formatSiteScope } from "../src/auth/identityPresentation.js";

test("administrator empty site list means effective access to all sites", () => {
  assert.equal(formatSiteScope([], ["platform.admin"]), "全部站点（管理员）");
});

test("non-administrator empty site list is shown as no production-site authorization", () => {
  assert.equal(formatSiteScope([], ["process.engineer"]), "未授权生产站点");
});

test("identity presentation uses labels and explicit site ids", () => {
  assert.equal(formatRoleSummary(["quality.inspector", "process.engineer"]), "质量检验员、工艺工程师");
  assert.equal(formatSiteScope(["SITE-001", "SITE-002"], ["process.engineer"]), "SITE-001、SITE-002");
});
