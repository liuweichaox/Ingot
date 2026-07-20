import type { Metadata } from "next";
import "../globals.css";

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  alternates: {
    canonical: "/",
    languages: { "zh-CN": "/", en: "/en/" },
  },
  title: "Ingot — 让生产数据可验证、可追问、可分析",
  description: "Ingot 汇集生产现场的重要记录；工程师通过 Ingot Chat 查问题、看证据，并在需要时深入调查。",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "生产数据", "Ingot Chat", "人工检测",
    "生产履历", "工艺调查", "质量调查", "证据回链",
  ],
  icons: {
    icon: "/brand/ingot-mark-dark.svg",
    shortcut: "/brand/ingot-mark-dark.svg",
    apple: "/brand/ingot-mark-dark.svg",
  },
  openGraph: {
    title: "Ingot — 让生产数据可验证、可追问、可分析",
    description: "汇集生产记录，在 Ingot Chat 中查询问题、查看证据，并在需要时深入调查。",
    url: origin,
    type: "website",
    locale: "zh_CN",
    siteName: "Ingot",
    images: [{ url: "/og.png", width: 1200, height: 630, alt: "Ingot — 让生产数据可验证、可追问、可分析" }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — 让生产数据可验证、可追问、可分析",
    description: "Ingot Chat 用生产记录和证据帮助工程师调查问题。",
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
