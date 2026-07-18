import type { MetadataRoute } from "next";

export default function sitemap(): MetadataRoute.Sitemap {
  const languages = {
    "zh-CN": "https://ingotstack.com/",
    en: "https://ingotstack.com/en/",
  };
  return [
    {
      url: languages["zh-CN"],
      changeFrequency: "weekly",
      priority: 1,
      alternates: { languages },
    },
    {
      url: languages.en,
      changeFrequency: "weekly",
      priority: 0.9,
      alternates: { languages },
    },
  ];
}
