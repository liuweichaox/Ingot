
import type { Metadata } from "next";
import "../globals.css";

// Defines canonical Chinese metadata for the public product entry point.

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  alternates: {
    canonical: "/",
    languages: { "zh-CN": "/", en: "/en/" },
  },
  title: "Ingot — 开源工艺追因与优化系统",
  description: "面向工艺工程师的开源工艺追因与优化系统，支持运行证据关联、工艺追因、实验设计与受约束优化。",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "工艺追因", "工艺优化", "工艺研发", "工艺工程师决策",
    "生产运行", "实验设计", "受约束优化", "工艺操作域",
    "过程数据", "贝叶斯优化", "工艺知识",
  ],
  icons: {
    icon: "/brand/ingot-mark-dark.svg",
    shortcut: "/brand/ingot-mark-dark.svg",
    apple: "/brand/ingot-mark-dark.svg",
  },
  openGraph: {
    title: "Ingot — 看清这次运行，做对下一项实验",
    description: "连接真实运行证据，缩小候选原因，设计验证并在安全边界内优化下一项实验。",
    url: origin,
    type: "website",
    locale: "zh_CN",
    siteName: "Ingot",
    images: [{ url: "/og.zh.png", width: 1200, height: 630, alt: "Ingot — 看清这次运行，做对下一项实验。" }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — 开源工艺追因与优化系统",
    description: "用可追溯运行证据支持工艺追因、实验设计和受约束优化。",
    images: ["/og.zh.png"],
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
