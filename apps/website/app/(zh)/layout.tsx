import type { Metadata } from "next";
import "../globals.css";

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  alternates: {
    canonical: "/",
    languages: { "zh-CN": "/", en: "/en/" },
  },
  title: "Ingot — 制造生产数据与工艺分析系统",
  description: "连接设备过程、批次、工件、配方、工装和检测结果，建立连续生产履历，帮助工程师调查良率变化、设备差异与异常工件。",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "工艺调查", "良率分析", "Ingot Chat",
    "生产履历", "工艺分析", "质量分析", "可核对分析",
  ],
  icons: {
    icon: "/brand/ingot-mark-dark.svg",
    shortcut: "/brand/ingot-mark-dark.svg",
    apple: "/brand/ingot-mark-dark.svg",
  },
  openGraph: {
    title: "Ingot — 制造生产数据与工艺分析系统",
    description: "把每次生产过程变成可追溯的工程依据：连接现场数据、建立生产履历、完成工艺调查。",
    url: origin,
    type: "website",
    locale: "zh_CN",
    siteName: "Ingot",
    images: [{ url: "/og.png", width: 1200, height: 630, alt: "Ingot — 制造生产数据与工艺分析系统" }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — 制造生产数据与工艺分析系统",
    description: "连接现场数据、建立生产履历、完成工艺调查。",
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
