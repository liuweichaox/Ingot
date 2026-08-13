import type { Metadata } from "next";
import "../globals.css";

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  alternates: {
    canonical: "/",
    languages: { "zh-CN": "/", en: "/en/" },
  },
  title: "Ingot — 开源工艺追因与优化系统",
  description: "让工艺研发从没有数据支撑走向有数据支撑，让计算机基于真实运行证据帮助工艺工程师抉择。",
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
    title: "Ingot — 让真实数据帮助工艺工程师抉择",
    description: "从真实生产条件、过程轨迹和质量结果形成工程证据，再按问题选择有效分析与实验方法。",
    url: origin,
    type: "website",
    locale: "zh_CN",
    siteName: "Ingot",
    images: [{ url: "/og.png", width: 1733, height: 908, alt: "Ingot — 让真实数据帮助工艺工程师抉择。" }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — 开源工艺追因与优化系统",
    description: "让真实运行证据支持工艺比较、原因验证和下一步实验。",
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
