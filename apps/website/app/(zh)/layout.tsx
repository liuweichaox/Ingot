import type { Metadata } from "next";
import "../globals.css";

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  alternates: {
    canonical: "/",
    languages: { "zh-CN": "/", en: "/en/" },
  },
  title: "Ingot — 开源工艺追因与优化系统",
  description: "把真实周期、过程轨迹和检验结果连成可追溯证据：解释这次运行的偏差来自哪个变量、哪段轨迹，再用物理先验与贝叶斯优化推荐下一次运行参数。",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "AI 工艺研发", "工艺追因", "根因分析",
    "周期诊断", "实验设计", "工艺优化", "工艺窗口",
    "机理融合", "贝叶斯优化", "工艺知识",
  ],
  icons: {
    icon: "/brand/ingot-mark-dark.svg",
    shortcut: "/brand/ingot-mark-dark.svg",
    apple: "/brand/ingot-mark-dark.svg",
  },
  openGraph: {
    title: "Ingot — 看清这次运行，优化下一次运行",
    description: "面向高成本、小样本制造实验的开源工艺追因与优化系统。",
    url: origin,
    type: "website",
    locale: "zh_CN",
    siteName: "Ingot",
    images: [{ url: "/og.png", width: 1734, height: 909, alt: "Ingot — 看清这次运行，优化下一次运行。" }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — 开源工艺追因与优化系统",
    description: "从真实过程证据，到偏差的解释和下一次运行可验证的工艺参数。",
    images: ["/og.png"],
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="zh-CN">
      <body>{children}</body>
    </html>
  );
}
