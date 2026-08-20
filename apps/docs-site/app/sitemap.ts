// 从公开路由生成站点地图，保持中英文页面可发现。

import type { MetadataRoute } from "next";
import { docs, routeFor } from "@/lib/docs";

export const dynamic = "force-static";

export default function sitemap(): MetadataRoute.Sitemap {
  return docs.map((doc) => ({ url: `https://docs.ingotstack.com${routeFor(doc.lang, doc.slug)}`, changeFrequency: "weekly" }));
}
