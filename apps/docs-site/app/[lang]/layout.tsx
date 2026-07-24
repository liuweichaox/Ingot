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
      ? "了解 Ingot 如何建立生产履历、调查工艺问题并在制造现场落地"
      : "Learn how Ingot builds production histories, investigates process questions, and rolls out at manufacturing sites",
    robots: { index: true, follow: true },
  };
}

export default async function LanguageLayout({ children, params }: Props) {
  const { lang } = await params;
  if (lang !== "zh" && lang !== "en") notFound();
  return <html lang={lang === "zh" ? "zh-CN" : "en"}><body>{children}</body></html>;
}
