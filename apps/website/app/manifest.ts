import type { MetadataRoute } from "next";

export const dynamic = "force-static";

export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "Ingot",
    short_name: "Ingot",
    description: "汇集生产记录，查询异常，比较周期并分析可能原因",
    start_url: "/",
    display: "standalone",
    background_color: "#10161c",
    theme_color: "#10161c",
    icons: [{ src: "/brand/ingot-mark-dark.svg", sizes: "any", type: "image/svg+xml" }],
  };
}
