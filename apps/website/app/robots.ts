import type { MetadataRoute } from "next";

export default function robots(): MetadataRoute.Robots {
  return {
    rules: { userAgent: "*", allow: "/" },
    sitemap: "https://ingotstack.com/sitemap.xml",
    host: "https://ingotstack.com",
  };
}
