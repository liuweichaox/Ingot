import type { Metadata } from "next";
import "../globals.css";

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  alternates: {
    canonical: "/",
    languages: { "zh-CN": "/", en: "/en/" },
  },
  title: "Ingot — AI 闭环工艺优化系统",
  description: "让设备轨迹、实际配方和质量结果持续学习，用物理先验与贝叶斯优化决定下一次工艺实验。",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "AI 工艺研发", "实验设计", "工艺优化",
    "工艺窗口", "机理融合", "贝叶斯优化", "工艺知识",
  ],
  icons: {
    icon: "/brand/ingot-mark-dark.svg",
    shortcut: "/brand/ingot-mark-dark.svg",
    apple: "/brand/ingot-mark-dark.svg",
  },
  openGraph: {
    title: "Ingot — 让每一次试验都逼近最优工艺",
    description: "面向昂贵、小样本制造实验的闭环工艺优化系统。",
    url: origin,
    type: "website",
    locale: "zh_CN",
    siteName: "Ingot",
    images: [{ url: "/og.png", width: 1734, height: 909, alt: "Ingot — The next run, optimized." }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — AI 闭环工艺优化系统",
    description: "从真实过程证据到下一炉可验证的工艺参数。",
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
