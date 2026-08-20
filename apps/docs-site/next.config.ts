// 固化站点构建、静态导出和安全响应头配置。

import type { NextConfig } from "next";
import path from "node:path";

const nextConfig: NextConfig = {
  output: "export",
  trailingSlash: true,
  images: { unoptimized: true },
  outputFileTracingRoot: path.resolve(process.cwd(), "../.."),
};

export default nextConfig;
