import type { Metadata } from "next";
import "../globals.css";

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  title: "Ingot — Process Root-Cause Engine · verifiable answers, no hallucinated numbers",
  description: "Ask why this batch differs from the last, in plain language. Every number traces to real data and opens the original record. Read-only, never touches equipment, never invents a figure.",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "process root-cause", "yield attribution", "Ingot Chat",
    "production history", "process analysis", "quality analysis", "verifiable analytics",
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
    title: "Ingot — Process Root-Cause Engine",
    description: "Why is this batch different from the last? Ask in plain language — every number opens the original record. Read-only, never invents.",
    url: `${origin}/en/`,
    locale: "en_US",
    alternateLocale: ["zh_CN"],
    siteName: "Ingot",
    type: "website",
    images: [{ url: "/og.png", width: 1200, height: 630, alt: "Ingot — Process Root-Cause Engine" }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — Process Root-Cause Engine",
    description: "Verifiable, no-hallucination production analysis: ask why this batch differs from the last.",
    images: ["/og.png"],
  },
};

export default function EnglishLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en"><body>{children}</body></html>;
}
