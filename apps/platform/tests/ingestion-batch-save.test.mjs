import assert from "node:assert/strict";
import test from "node:test";
import { classifyIngestionDraftWriteOutcome } from "../src/acquisition/ingestionBatchSave.js";

// Keep retries safe when a batch draft write succeeds but its response is lost.

test("a lost write response is resolved as saved when the server has the same draft", () => {
  assert.deepEqual(classifyIngestionDraftWriteOutcome({
    expectedFingerprint: "draft-a",
    storedFingerprint: "draft-a",
    saveError: new Error("connection closed"),
  }), { kind: "saved", error: "", verificationRequired: false });
});

test("a different server draft is a conflict and must not be retried", () => {
  const result = classifyIngestionDraftWriteOutcome({
    expectedFingerprint: "draft-a",
    storedFingerprint: "draft-b",
  });
  assert.equal(result.kind, "conflict");
  assert.equal(result.verificationRequired, false);
  assert.match(result.error, /更换任务编号/);
});

test("a verified 404 leaves the task safe to retry", () => {
  assert.deepEqual(classifyIngestionDraftWriteOutcome({
    expectedFingerprint: "draft-a",
    saveError: new Error("平台暂不可用"),
    verificationError: Object.assign(new Error("not found"), { status: 404 }),
  }), { kind: "request", error: "平台暂不可用", verificationRequired: false });
});

test("server validation and conflict responses don't expose a blind retry", () => {
  const validation = classifyIngestionDraftWriteOutcome({
    expectedFingerprint: "draft-a",
    saveError: Object.assign(new Error("点位映射无效"), { status: 400 }),
  });
  assert.equal(validation.kind, "validation");
  assert.equal(validation.verificationRequired, false);

  const conflict = classifyIngestionDraftWriteOutcome({
    expectedFingerprint: "draft-a",
    saveError: Object.assign(new Error("版本状态已变化"), { status: 409 }),
  });
  assert.equal(conflict.kind, "conflict");
  assert.equal(conflict.verificationRequired, false);
});

test("a server rejection is surfaced for review rather than repeated", () => {
  const result = classifyIngestionDraftWriteOutcome({
    expectedFingerprint: "draft-a",
    saveError: Object.assign(new Error("当前身份无权修改"), { status: 403 }),
  });
  assert.equal(result.kind, "rejected");
  assert.equal(result.verificationRequired, false);
  assert.match(result.error, /无权修改/);
});

test("an unavailable verification keeps the result unresolved", () => {
  const result = classifyIngestionDraftWriteOutcome({
    expectedFingerprint: "draft-a",
    saveError: new Error("连接中断"),
    verificationError: new Error("服务仍不可用"),
  });
  assert.equal(result.kind, "unknown");
  assert.equal(result.verificationRequired, true);
  assert.match(result.error, /连接中断；服务仍不可用/);
});
