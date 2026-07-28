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
      ? "了解 Ingot 如何把 PLC 周期、真实过程轨迹和检验结果转化为下一组可验证的工艺参数"
      : "Learn how Ingot turns PLC cycles, realized trajectories, and inspections into the next verifiable process experiment",
    robots: { index: true, follow: true },
  };
}

export default async function LanguageLayout({ children, params }: Props) {
  const { lang } = await params;
  if (lang !== "zh" && lang !== "en") notFound();
  return <html lang={lang === "zh" ? "zh-CN" : "en"}><body>{children}</body></html>;
}
