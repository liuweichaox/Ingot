
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
  title: "Ingot — 开源工艺研发与优化系统",
  description: "组织配方版本、实验记录与运行证据，支持质量分析、工艺追因和配方优化。",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "工艺追因", "配方优化", "工艺优化", "工艺工程师决策",
    "生产运行", "下一份配方", "配方版本", "工艺版本",
    "过程数据", "机理依据", "工程师决策",
  ],
  icons: {
    icon: "/brand/ingot-mark-dark.svg",
    shortcut: "/brand/ingot-mark-dark.svg",
    apple: "/brand/ingot-mark-dark.svg",
  },
  openGraph: {
    title: "Ingot — 从工艺数据，到有依据的研发决策。",
    description: "组织配方版本、实验记录与运行证据，支持质量分析、工艺追因和配方优化。",
    url: origin,
    type: "website",
    locale: "zh_CN",
    siteName: "Ingot",
    images: [{ url: "/og.zh.png", width: 1200, height: 630, alt: "Ingot — 从工艺数据，到有依据的研发决策。" }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — 开源工艺研发与优化系统",
    description: "组织配方版本、实验记录与运行证据，支持质量分析、工艺追因和配方优化。",
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
