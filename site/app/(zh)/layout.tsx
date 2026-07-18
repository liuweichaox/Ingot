import type { Metadata } from "next";
import "../globals.css";

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  alternates: {
    canonical: "/",
    languages: { "zh-CN": "/", en: "/en/" },
  },
  title: "Ingot — 可信生产事实、Chat 与桌面连接器 Agent",
  description: "Central Web Chat 只读查询生产事实并查找数据问题；Ingot Agent 桌面端生成、构建、测试并打包连接器代码。",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "生产事实", "Chat", "Ingot Agent Desktop", "人工检测",
    "标准生产事件", "工业连接器", "industrial connectors", "证据回链", "连接器代码生成",
  ],
  icons: {
    icon: "/brand/ingot-mark-dark.svg",
    shortcut: "/brand/ingot-mark-dark.svg",
    apple: "/brand/ingot-mark-dark.svg",
  },
  openGraph: {
    title: "Ingot — 可信生产事实、Chat 与桌面连接器 Agent",
    description: "Central Web Chat 查询生产事实；Ingot Agent 桌面端生成连接器代码，经受控构建、测试和人工批准后打包。",
    url: origin,
    type: "website",
    locale: "zh_CN",
    siteName: "Ingot",
    images: [{ url: "/og.png", width: 1200, height: 630, alt: "Ingot — 可信生产事实、Chat 与桌面连接器 Agent" }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — 可信生产事实、Chat 与桌面连接器 Agent",
    description: "Chat 只读查找生产问题；桌面 Agent 生成、构建、测试并打包连接器代码。",
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
