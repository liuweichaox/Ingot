import type { Metadata } from "next";
import "../globals.css";

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  alternates: {
    canonical: "/",
    languages: { "zh-CN": "/", en: "/en/" },
  },
  title: "Ingot — AI 工艺研发系统",
  description: "融合实验数据、实时过程数据、物理机理和专家知识，辅助工艺工程师设计实验、优化参数并验证工艺窗口，缩短工艺研发周期。",
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
    title: "Ingot — AI 工艺研发系统",
    description: "用更少的实验，更快找到并验证可靠工艺。",
    url: origin,
    type: "website",
    locale: "zh_CN",
    siteName: "Ingot",
  },
  twitter: {
    card: "summary",
    title: "Ingot — AI 工艺研发系统",
    description: "融合实验数据、实时过程数据、物理机理和专家知识，缩短工艺研发周期。",
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
