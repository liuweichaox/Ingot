import assert from "node:assert/strict";
import { readdir, readFile } from "node:fs/promises";
import test from "node:test";

async function vueSources(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const nested = await Promise.all(entries.map(async entry => {
    const url = new URL(`${entry.name}${entry.isDirectory() ? "/" : ""}`, directory);
    if (entry.isDirectory()) return vueSources(url);
    return entry.name.endsWith(".vue") ? [url] : [];
  }));
  return nested.flat();
}

test("pages do not expose manual refresh buttons", async () => {
  const sources = await vueSources(new URL("../src/", import.meta.url));
  const refreshLabel = /(?:刷新|重新加载|重新获取)/;
  const refreshIcon = /:icon\s*=\s*["'](?:Refresh|RefreshLeft|RefreshRight|Reload)["']/;
  const labelledButton = /<(?:el-button|button)\b[^>]*(?:aria-label|title)\s*=\s*["'][^"']*(?:刷新|重新加载|重新获取)[^"']*["'][^>]*>/i;

  for (const source of sources) {
    const content = await readFile(source, "utf8");
    const template = content.match(/<template>([\s\S]*?)<\/template>/)?.[1] || "";
    const buttons = template.match(/<(?:el-button|button)\b[^>]*>[\s\S]*?<\/(?:el-button|button)>/gi) || [];

    assert.doesNotMatch(template, refreshIcon, `${source.pathname} uses a refresh icon`);
    assert.doesNotMatch(template, labelledButton, `${source.pathname} labels a button as refresh`);
    for (const button of buttons) {
      assert.doesNotMatch(button, refreshLabel, `${source.pathname} contains a visible refresh button`);
    }
  }
});
