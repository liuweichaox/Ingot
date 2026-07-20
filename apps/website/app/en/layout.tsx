import type { Metadata } from "next";
import "../globals.css";

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  title: "Ingot — understand production, process settings, and quality",
  description: "Ingot brings equipment settings, production history, and inspection results together so engineers can investigate issues and compare runs.",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "production records", "Ingot Chat", "inspection records",
    "production history", "process analysis", "quality analysis", "parameter correlation",
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
    title: "Ingot — understand production, process settings, and quality",
    description: "Bring production records together, then use Ingot Chat to investigate issues, compare runs, and review possible causes.",
    url: `${origin}/en/`,
    locale: "en_US",
    alternateLocale: ["zh_CN"],
    siteName: "Ingot",
    type: "website",
    images: [{ url: "/og.png", width: 1200, height: 630, alt: "Ingot — understand production, process settings, and quality" }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — understand production, process settings, and quality",
    description: "Ingot Chat helps engineers investigate problems using production records and inspection results.",
    images: ["/og.png"],
  },
};

export default function EnglishLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en"><body>{children}</body></html>;
}
