// 定义当前路由层级的页面框架、语言和元数据边界。

import type { Metadata } from "next";
import "../globals.css";

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  title: "Ingot — Open-source Process Diagnosis & Optimization",
  description: "Move process R&D from unsupported judgment to decisions grounded in real run evidence, helping process engineers choose what to do next.",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "data-supported process R&D", "process engineer decisions", "process diagnosis",
    "process executions", "experiment design", "process R&D",
    "process window", "process data", "Bayesian optimization", "process knowledge",
  ],
  alternates: {
    canonical: "/en/",
    languages: { "zh-CN": "/", en: "/en/" },
  },
  icons: {
    icon: "/brand/ingot-mark-dark.svg",
    shortcut: "/brand/ingot-mark-dark.svg",
    apple: "/brand/ingot-mark-dark.svg",
  },
  openGraph: {
    title: "Ingot — Help process engineers decide with real data",
    description: "Turn actual conditions, process trajectories, and quality outcomes into engineering evidence, then select methods that fit the question.",
    url: `${origin}/en/`,
    locale: "en_US",
    alternateLocale: ["zh_CN"],
    siteName: "Ingot",
    type: "website",
    images: [{ url: "/og.png", width: 1733, height: 908, alt: "Ingot — Help process engineers decide with real data." }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — Open-source Process Diagnosis & Optimization",
    description: "Use real run evidence for process comparison, causal validation, and the next experiment.",
    images: ["/og.png"],
  },
};

export default function EnglishLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en"><body>{children}</body></html>;
}
