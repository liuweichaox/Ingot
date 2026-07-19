import type { Metadata } from "next";
import "../globals.css";

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  title: "Ingot — Production facts you can verify, question, and investigate",
  description: "Ingot brings important production records together. Engineers use Ingot Chat to ask questions, inspect evidence, and investigate complex problems.",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "production facts", "Ingot Chat", "inspection facts",
    "production history", "process investigation", "quality investigation", "evidence traceability",
  ],
  alternates: {
    canonical: "/en/",
    languages: { "zh-CN": "/", en: "/en/" },
  },
  icons: {
    icon: "/brand/ingot-mark-dark.svg",
    shortcut: "/brand/ingot-mark-dark.svg",
    apple: "/brand/ingot-mark-dark.svg",
  },
  openGraph: {
    title: "Ingot — Production facts you can verify, question, and investigate",
    description: "Bring production records together, then use Ingot Chat to ask questions, inspect evidence, and investigate when needed.",
    url: `${origin}/en/`,
    locale: "en_US",
    alternateLocale: ["zh_CN"],
    siteName: "Ingot",
    type: "website",
    images: [{ url: "/og.png", width: 1200, height: 630, alt: "Ingot — Production facts you can verify, question, and investigate" }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — Production facts you can verify, question, and investigate",
    description: "Ingot Chat helps engineers investigate problems with production records and evidence.",
    images: ["/og.png"],
  },
};

export default function EnglishLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en"><body>{children}</body></html>;
}
