import type { Metadata } from "next";
import "../globals.css";

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  alternates: {
    canonical: "/",
    languages: { "zh-CN": "/", en: "/en/" },
  },
  title: "Ingot — 可信生产事实、标准事件接入与 Ingot Chat",
  description: "使用标准事件 API 接入不同数据源，在 Central Web 中使用 Ingot Chat 查询生产事实并查找数据问题。",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "生产事实", "Ingot Chat", "人工检测",
    "标准生产事件", "事件接入 API", "数据源适配", "证据回链",
  ],
  icons: {
    icon: "/brand/ingot-mark-dark.svg",
    shortcut: "/brand/ingot-mark-dark.svg",
    apple: "/brand/ingot-mark-dark.svg",
  },
  openGraph: {
    title: "Ingot — 可信生产事实、标准事件接入与 Ingot Chat",
    description: "使用标准事件 API 接入生产事实；在 Central Web 中使用 Ingot Chat 查询问题和证据。",
    url: origin,
    type: "website",
    locale: "zh_CN",
    siteName: "Ingot",
    images: [{ url: "/og.png", width: 1200, height: 630, alt: "Ingot — 可信生产事实、标准事件接入与 Ingot Chat" }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — 可信生产事实、标准事件接入与 Ingot Chat",
    description: "Ingot Chat 只读查找生产问题，并通过证据回链到标准生产事实。",
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
