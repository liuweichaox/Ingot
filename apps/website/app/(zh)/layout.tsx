import type { Metadata } from "next";
import "../globals.css";

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  alternates: {
    canonical: "/",
    languages: { "zh-CN": "/", en: "/en/" },
  },
  title: "Ingot — 查清生产过程，分析参数与质量",
  description: "Ingot 汇集设备参数、生产过程和检测结果；工程师通过 Ingot Chat 查询异常、比较周期并分析可能原因。",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "生产数据", "Ingot Chat", "人工检测",
    "生产履历", "工艺分析", "质量分析", "参数相关性",
  ],
  icons: {
    icon: "/brand/ingot-mark-dark.svg",
    shortcut: "/brand/ingot-mark-dark.svg",
    apple: "/brand/ingot-mark-dark.svg",
  },
  openGraph: {
    title: "Ingot — 查清生产过程，分析参数与质量",
    description: "汇集生产记录，在 Ingot Chat 中查询异常、比较周期并分析可能原因。",
    url: origin,
    type: "website",
    locale: "zh_CN",
    siteName: "Ingot",
    images: [{ url: "/og.png", width: 1200, height: 630, alt: "Ingot — 查清生产过程，分析参数与质量" }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — 查清生产过程，分析参数与质量",
    description: "Ingot Chat 使用生产记录帮助工程师查询异常和比较工艺参数。",
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
