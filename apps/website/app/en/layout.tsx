
import type { Metadata } from "next";
import "../globals.css";

// Defines canonical English metadata for the public product entry point.

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  title: "Ingot — Open-source Process Diagnosis & Optimization",
  description: "An open-source process diagnosis and optimization system that uses real production runs, quality outcomes, and diagnostic evidence to support the next process-specification revision.",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "process diagnosis", "recipe optimization", "process optimization", "process engineer decisions",
    "production runs", "next recipe", "process specification",
    "version lineage", "process data", "mechanism notes", "engineering decisions",
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
    title: "Ingot — From run evidence to the next recipe.",
    description: "Connect real production runs and revise the next process specification from quality and diagnostic evidence.",
    url: `${origin}/en/`,
    locale: "en_US",
    alternateLocale: ["zh_CN"],
    siteName: "Ingot",
    type: "website",
    images: [{ url: "/og.png", width: 1200, height: 630, alt: "Ingot — From run evidence to the next recipe." }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — Open-source Process Diagnosis & Optimization",
    description: "Use traceable real runs for process diagnosis and next process-specification revision.",
    images: ["/og.png"],
  },
};

export default function EnglishLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en"><body>{children}</body></html>;
}
