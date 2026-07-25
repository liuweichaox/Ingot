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
      ? "了解 Ingot 如何融合实验数据、实时过程数据、物理机理和专家知识，辅助工艺工程师缩短工艺研发周期"
      : "Learn how Ingot fuses experimental data, real-time process data, physical mechanisms, and expert knowledge to shorten process R&D cycles",
    robots: { index: true, follow: true },
  };
}

export default async function LanguageLayout({ children, params }: Props) {
  const { lang } = await params;
  if (lang !== "zh" && lang !== "en") notFound();
  return <html lang={lang === "zh" ? "zh-CN" : "en"}><body>{children}</body></html>;
}
