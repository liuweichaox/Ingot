import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  clearScreen: false,
  server: {
    strictPort: true,
    watch: {
      ignored: ["**/src-tauri/**"]
    }
  },
  build: {
    target: ["es2022", "chrome105", "safari13"],
    sourcemap: true
  },
  test: {
    include: ["src/**/*.test.ts"],
    environment: "node"
  }
});
