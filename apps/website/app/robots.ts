// 生成搜索引擎抓取策略，避免依赖手工维护的静态文件。

import type { MetadataRoute } from "next";

export const dynamic = "force-static";

export default function robots(): MetadataRoute.Robots {
  return {
    rules: { userAgent: "*", allow: "/" },
    sitemap: "https://ingotstack.com/sitemap.xml",
    host: "https://ingotstack.com",
  };
}
