import type { Metadata } from "next";
import "../globals.css";

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  alternates: {
    canonical: "/",
    languages: { "zh-CN": "/", en: "/en/" },
  },
  title: "Ingot — 工艺追因引擎 · 可核对、不编造数字的生产分析",
  description: "用日常语言查清生产过程：为什么这批和上批不一样。每个数字都来自真实数据、可点开原始记录；只读，不触碰设备，绝不编造数字。",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "工艺追因", "良率归因", "Ingot Chat",
    "生产履历", "工艺分析", "质量分析", "可核对分析",
  ],
  icons: {
    icon: "/brand/ingot-mark-dark.svg",
    shortcut: "/brand/ingot-mark-dark.svg",
    apple: "/brand/ingot-mark-dark.svg",
  },
  openGraph: {
    title: "Ingot — 工艺追因引擎",
    description: "为什么这批和上批不一样？问一句就知道 —— 每个数字都能点开原始记录。只读，不编造。",
    url: origin,
    type: "website",
    locale: "zh_CN",
    siteName: "Ingot",
    images: [{ url: "/og.png", width: 1200, height: 630, alt: "Ingot — 工艺追因引擎" }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — 工艺追因引擎",
    description: "可核对、不编造数字的生产分析：为什么这批和上批不一样，问一句就知道。",
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
