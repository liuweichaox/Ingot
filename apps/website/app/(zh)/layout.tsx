
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
  description: "开源工艺追因与优化系统，用真实生产运行、质量结果和工艺追因证据支持工程师修订下一版工艺规范。",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "工艺追因", "配方优化", "工艺优化", "工艺工程师决策",
    "生产运行", "下一版配方", "工艺规范", "工艺版本",
    "过程数据", "机理依据", "工程师决策",
  ],
  icons: {
    icon: "/brand/ingot-mark-dark.svg",
    shortcut: "/brand/ingot-mark-dark.svg",
    apple: "/brand/ingot-mark-dark.svg",
  },
  openGraph: {
    title: "Ingot — 从运行证据，到下一份配方",
    description: "连接真实生产运行，基于质量与追因证据修订下一版工艺规范。",
    url: origin,
    type: "website",
    locale: "zh_CN",
    siteName: "Ingot",
    images: [{ url: "/og.zh.png", width: 1200, height: 630, alt: "Ingot — 从运行证据，到下一份配方。" }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — 开源工艺追因与优化系统",
    description: "用可追溯真实运行支持工艺追因与下一版工艺规范修订。",
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
