
import type { MetadataRoute } from "next";

// Exposes install metadata for the public, read-only website surface.

export const dynamic = "force-static";

export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "Ingot",
    short_name: "Ingot",
    description: "组织研发项目、实验记录与运行证据，支持质量分析、工艺追因和配方优化。",
    start_url: "/",
    display: "standalone",
    background_color: "#10161c",
    theme_color: "#10161c",
    icons: [{ src: "/brand/ingot-mark-dark.svg", sizes: "any", type: "image/svg+xml" }],
  };
}
