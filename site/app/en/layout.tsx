import type { Metadata } from "next";
import "../globals.css";

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  title: "Ingot — Trusted production facts, standard event ingestion, and Ingot Chat",
  description: "Ingest different sources through a standard event API, then use Ingot Chat in Central Web to query production facts and find data problems.",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "production facts", "Ingot Chat", "inspection facts",
    "normalized production events", "event ingestion API", "source adaptation", "evidence traceability",
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
    title: "Ingot — Trusted production facts, standard event ingestion, and Ingot Chat",
    description: "Ingest production facts through a standard event API, then use Ingot Chat in Central Web to investigate them with evidence.",
    url: `${origin}/en/`,
    locale: "en_US",
    alternateLocale: ["zh_CN"],
    siteName: "Ingot",
    type: "website",
    images: [{ url: "/og.png", width: 1200, height: 630, alt: "Ingot — Trusted production facts, standard event ingestion, and Ingot Chat" }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — Trusted production facts, standard event ingestion, and Ingot Chat",
    description: "Ingot Chat finds production-data problems and links evidence to standard production facts.",
    images: ["/og.png"],
  },
};

export default function EnglishLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en"><body>{children}</body></html>;
}
