// 验证前端 process-curves 的渲染、交互、错误和边界状态。

import assert from "node:assert/strict";
import test from "node:test";
import { loadProcessCurves } from "../src/hooks/useProcessCurves.js";

test("process curve loader requests only the selected signals and a bounded point count", async () => {
  let requestedUrl = "";
  const response = { totalFrameCount: 80000, returnedPointCount: 4000, downsampled: true, series: [] };
  const request = async url => {
    requestedUrl = url;
    return response;
  };

  const result = await loadProcessCurves(
    "execution/01",
    ["temperature", "pressure"],
    2000,
    undefined,
    request,
  );

  assert.equal(result, response);
  assert.match(requestedUrl, /process-executions\/execution%2F01\/curves/);
  assert.match(requestedUrl, /signalCodes=temperature%2Cpressure/);
  assert.match(requestedUrl, /maxPoints=2000/);
});
