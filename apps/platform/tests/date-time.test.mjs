import assert from "node:assert/strict";
import test from "node:test";
import { formatLocalDateTime, localDateTimeToIso } from "../src/ui/dateTime.js";

test("datetime-local round trip preserves an ISO instant", () => {
  const instant = "2026-09-02T00:00:00.000Z";
  const value = formatLocalDateTime(instant);

  assert.equal(localDateTimeToIso(value), instant);
});

test("datetime-local formatter uses browser-local clock fields", () => {
  const instant = "2026-09-02T00:00:00.000Z";
  const date = new Date(instant);
  const pad = value => String(value).padStart(2, "0");
  const expected = `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;

  assert.equal(formatLocalDateTime(instant), expected);
});
