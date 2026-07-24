import type { NextConfig } from "next";
import { dirname } from "node:path";
import { fileURLToPath } from "node:url";

const siteRoot = dirname(fileURLToPath(import.meta.url));

const nextConfig: NextConfig = {
  output: "export",
  trailingSlash: true,
  outputFileTracingRoot: siteRoot,
};

export default nextConfig;
