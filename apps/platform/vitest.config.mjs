// 配置平台真实渲染测试的 jsdom 环境、初始化脚本和文件范围。

import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom",
    setupFiles: ["./tests/vitest.setup.js"],
    include: ["tests/**/*.test.jsx"],
    // Tests stub the browser fetch boundary; run files serially so one file's
    // cleanup cannot race another file's in-flight request assertions.
    fileParallelism: false,
    restoreMocks: true,
  },
});
