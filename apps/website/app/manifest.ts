import type { MetadataRoute } from "next";

export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "Ingot",
    short_name: "Ingot",
    description: "让生产数据可验证、可追问、可分析",
    start_url: "/",
    display: "standalone",
    background_color: "#10161c",
    theme_color: "#10161c",
    icons: [{ src: "/brand/ingot-mark-dark.svg", sizes: "any", type: "image/svg+xml" }],
  };
}
