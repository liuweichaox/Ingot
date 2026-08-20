// 配置平台真实渲染测试的 jsdom 环境、初始化脚本和文件范围。

import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom",
    setupFiles: ["./tests/vitest.setup.js"],
    include: ["tests/**/*.test.jsx"],
    restoreMocks: true,
  },
});
