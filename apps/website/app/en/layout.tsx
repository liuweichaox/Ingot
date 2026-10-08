
import type { Metadata } from "next";
import "../globals.css";

// Defines canonical English metadata for the public product entry point.

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  title: "Ingot — Open-source Process R&D and Optimization System",
  description: "Organize R&D projects, experiment records, and run evidence to support quality analysis, process diagnosis, and recipe optimization.",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "process diagnosis", "recipe optimization", "process optimization", "process engineer decisions",
    "production runs", "next recipe", "recipe version",
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
    title: "Ingot — From process data to evidence-based R&D decisions.",
    description: "Organize R&D projects, experiment records, and run evidence to support quality analysis, process diagnosis, and recipe optimization.",
    url: `${origin}/en/`,
    locale: "en_US",
    alternateLocale: ["zh_CN"],
    siteName: "Ingot",
    type: "website",
    images: [{ url: "/og.png", width: 1200, height: 630, alt: "Ingot — From process data to evidence-based R&D decisions." }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — Open-source Process R&D and Optimization System",
    description: "Organize R&D projects, experiment records, and run evidence to support quality analysis, process diagnosis, and recipe optimization.",
    images: ["/og.png"],
  },
};

export default function EnglishLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en"><body>{children}</body></html>;
}
