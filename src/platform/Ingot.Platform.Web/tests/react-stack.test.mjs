import assert from "node:assert/strict";
import { readdir, readFile } from "node:fs/promises";
import test from "node:test";

const packageJson = JSON.parse(await readFile(new URL("../package.json", import.meta.url), "utf8"));
const app = await readFile(new URL("../src/App.jsx", import.meta.url), "utf8");
const pages = await readFile(new URL("../src/pages/index.jsx", import.meta.url), "utf8");
const styles = await readFile(new URL("../src/styles/global.css", import.meta.url), "utf8");
const vite = await readFile(new URL("../vite.config.mjs", import.meta.url), "utf8");

async function sourceFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  return (await Promise.all(entries.map(async entry => {
    const url = new URL(`${entry.name}${entry.isDirectory() ? "/" : ""}`, directory);
    return entry.isDirectory() ? sourceFiles(url) : [url];
  }))).flat();
}

test("platform uses React, Tailwind, and Headless UI without Vue or Element Plus", async () => {
  assert.ok(packageJson.dependencies.react);
  assert.ok(packageJson.dependencies["react-dom"]);
  assert.ok(packageJson.dependencies["@headlessui/react"]);
  assert.ok(packageJson.devDependencies.tailwindcss);
  assert.ok(packageJson.devDependencies["@vitejs/plugin-react"]);
  assert.equal(packageJson.dependencies.vue, undefined);
  assert.equal(packageJson.dependencies["vue-router"], undefined);
  assert.equal(packageJson.dependencies["element-plus"], undefined);
  assert.match(vite, /@vitejs\/plugin-react/);
  assert.match(vite, /@tailwindcss\/vite/);
  assert.match(styles, /@import "tailwindcss"/);
  assert.match(app, /@headlessui\/react/);
  assert.match(pages, /TabGroup/);
  const files = await sourceFiles(new URL("../src/", import.meta.url));
  assert.equal(files.filter(file => file.pathname.endsWith(".vue")).length, 0);
});

test("all platform routes remain available after the React migration", () => {
  for (const route of [
    "/workbench", "/chat", "/explorer", "/cycles", "/events", "/production/changeover",
    "/production/tooling-installations", "/configuration/component-types", "/configuration/components",
    "/configuration/tooling-types", "/configuration/tooling-assemblies", "/inspections",
    "/quality-analysis", "/configuration/inspection-definitions", "/configuration/quality-plans",
    "/comparisons", "/data-quality", "/process-improvement",
    "/configuration/process-analysis-plans", "/configuration/process-data-models",
    "/configuration/recipe-versions", "/configuration/acquisition-profiles", "/edges",
    "/platform-metrics", "/subscriptions", "/logs",
  ]) {
    assert.match(app, new RegExp(route.replaceAll("/", "\\/")));
  }
  assert.match(app, /Navigate to="\/workbench"/);
  assert.match(app, /Navigate to="\/configuration\/process-data-models"/);
});

test("navigation and overlays are accessible Headless UI components", () => {
  assert.match(app, /DialogBackdrop/);
  assert.match(app, /DialogPanel/);
  assert.match(app, /MenuButton/);
  assert.match(app, /aria-label="全局导航"/);
  assert.match(app, /aria-label="打开模块导航"/);
});
