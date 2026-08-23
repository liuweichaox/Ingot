
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
  description: "把真实运行变成可比较、可验证的工程证据，减少无效实验，更快找到达标工艺。",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "数据驱动工艺研发", "工艺工程师决策", "工艺追因",
    "生产周期", "实验设计", "工艺研发", "工艺窗口",
    "过程数据", "贝叶斯优化", "工艺知识",
  ],
  icons: {
    icon: "/brand/ingot-mark-dark.svg",
    shortcut: "/brand/ingot-mark-dark.svg",
    apple: "/brand/ingot-mark-dark.svg",
  },
  openGraph: {
    title: "Ingot — 少做无效实验，更快找到达标工艺",
    description: "还原真实运行，比较关键差异，设计验证并选择下一项实验。",
    url: origin,
    type: "website",
    locale: "zh_CN",
    siteName: "Ingot",
    images: [{ url: "/og.zh.png", width: 1200, height: 630, alt: "Ingot — 让真实运行成为可验证的工艺证据。" }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — 开源工艺追因与优化系统",
    description: "让真实运行证据支持工艺比较、原因验证和下一步实验。",
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
