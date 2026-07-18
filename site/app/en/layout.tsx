import type { Metadata } from "next";
import "../globals.css";

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  title: "Ingot — Trusted production facts, Chat, and desktop connector Agent",
  description: "Central Web Chat queries production facts and finds data problems. Ingot Agent Desktop generates, builds, tests, and packages connector code.",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "production facts", "Chat", "Ingot Agent Desktop", "inspection facts",
    "normalized production events", "industrial connectors", "evidence traceability", "connector code generation",
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
    title: "Ingot — Trusted production facts, Chat, and desktop connector Agent",
    description: "Central Web Chat queries facts. Ingot Agent Desktop generates connector code with governed build, test, and operator-approved packaging.",
    url: `${origin}/en/`,
    locale: "en_US",
    alternateLocale: ["zh_CN"],
    siteName: "Ingot",
    type: "website",
    images: [{ url: "/og.png", width: 1200, height: 630, alt: "Ingot — Trusted production facts, Chat, and desktop connector Agent" }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — Trusted production facts, Chat, and desktop connector Agent",
    description: "Chat finds production-data problems; the desktop Agent generates, builds, tests, and packages connector code.",
    images: ["/og.png"],
  },
};

export default function EnglishLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en"><body>{children}</body></html>;
}
