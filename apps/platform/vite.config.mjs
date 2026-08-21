// 配置 Platform 前端构建，并让 demo 模式默认连接只读本地模拟 API。
import { defineConfig, loadEnv } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), "");
  const defaultApiTarget = mode === "demo" ? "http://127.0.0.1:4010" : "http://127.0.0.1:8000";
  const apiTarget = env.INGOT_PLATFORM_API_TARGET || process.env.INGOT_PLATFORM_API_TARGET || defaultApiTarget;
  return ({
  plugins: [react(), tailwindcss()],
  build: {
    // Plotly stays in a lazy chart-only chunk; the initial application chunks remain below the default threshold.
    chunkSizeWarningLimit: 1200,
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (/[\\/]node_modules[\\/](react|react-dom|react-router)[\\/]/.test(id)) {
            return "react-vendor";
          }
          if (/[\\/]node_modules[\\/]@(?:headlessui|heroicons)[\\/]react[\\/]/.test(id)) {
            return "ui-vendor";
          }
          return undefined;
        },
      },
    },
  },
  server: {
    port: 3000,
    proxy: {
      "/api": {
        target: apiTarget,
        changeOrigin: true,
      },
      "/metrics": {
        target: apiTarget,
        changeOrigin: true,
      },
      "/health": {
        target: apiTarget,
        changeOrigin: true,
      },
    },
  },
  });
});
