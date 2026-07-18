import type { MetadataRoute } from "next";
import { docs, routeFor } from "@/lib/docs";

export const dynamic = "force-static";

export default function sitemap(): MetadataRoute.Sitemap {
  return docs.map((doc) => ({ url: `https://docs.ingotstack.com${routeFor(doc.lang, doc.slug)}`, changeFrequency: "weekly" }));
}
