// 定义当前路由层级的页面框架、语言和元数据边界。

import type { Metadata } from "next";
import { notFound } from "next/navigation";
import "../globals.css";

type Props = Readonly<{
  children: React.ReactNode;
  params: Promise<{ lang: string }>;
}>;

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { lang } = await params;
  if (lang !== "zh" && lang !== "en") return {};
  return {
    metadataBase: new URL("https://docs.ingotstack.com"),
    title: {
      default: lang === "zh" ? "Ingot 文档" : "Ingot Documentation",
      template: lang === "zh" ? "%s · Ingot 文档" : "%s · Ingot Documentation",
    },
    description: lang === "zh"
      ? "了解 Ingot 如何让真实生产数据支持工艺工程师比较运行、验证原因并选择下一步实验"
      : "Learn how Ingot uses real production evidence to help process engineers compare runs, validate causes, and select the next experiment",
    robots: { index: true, follow: true },
  };
}

export default async function LanguageLayout({ children, params }: Props) {
  const { lang } = await params;
  if (lang !== "zh" && lang !== "en") notFound();
  return <html lang={lang === "zh" ? "zh-CN" : "en"}><body>{children}</body></html>;
}
