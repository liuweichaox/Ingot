import assert from "node:assert/strict";
import test from "node:test";
import { protocolDescriptor } from "../src/acquisition/protocolRegistry.js";

const validateHttpConnection = snapshotPath => protocolDescriptor("http-polling").validateConnection({
  baseUrl: "http://192.168.1.10",
  snapshotPath,
  pollIntervalMs: 1000,
});

test("HTTP polling accepts a relative snapshot path", () => {
  assert.equal(validateHttpConnection("/api/v1/snapshot").snapshotPath, undefined);
});

test("HTTP polling rejects absolute, protocol-relative, and multiline snapshot paths", () => {
  for (const snapshotPath of ["https://other.example/snapshot", "//other.example/snapshot", "/snapshot\nheader:value"]) {
    assert.match(validateHttpConnection(snapshotPath).snapshotPath, /安全路径/);
  }
});
