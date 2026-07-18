import type { MetadataRoute } from "next";

export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "Ingot",
    short_name: "Ingot",
    description: "可信生产事实、标准事件接入与 Ingot Chat",
    start_url: "/",
    display: "standalone",
    background_color: "#10161c",
    theme_color: "#10161c",
    icons: [{ src: "/brand/ingot-mark-dark.svg", sizes: "any", type: "image/svg+xml" }],
  };
}
