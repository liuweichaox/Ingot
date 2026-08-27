
import type { Metadata } from "next";
import "../globals.css";

// Defines canonical English metadata for the public product entry point.

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  title: "Ingot — Open-source Process Diagnosis & Optimization",
  description: "An open-source process diagnosis and optimization system that turns real recipe runs into observations and recommends the next recipe within safety boundaries and observed coverage.",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "process diagnosis", "recipe optimization", "process optimization", "process engineer decisions",
    "production runs", "next recipe", "constrained optimization",
    "operating region", "process data", "Bayesian optimization", "process knowledge",
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
    title: "Ingot — Understand this run. Improve the next recipe.",
    description: "Connect real recipe runs and continuously recommend the next recipe within safety boundaries and observed coverage.",
    url: `${origin}/en/`,
    locale: "en_US",
    alternateLocale: ["zh_CN"],
    siteName: "Ingot",
    type: "website",
    images: [{ url: "/og.png", width: 1200, height: 630, alt: "Ingot — Understand this run. Improve the next recipe." }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — Open-source Process Diagnosis & Optimization",
    description: "Use traceable real runs for process diagnosis, next-recipe recommendations, and constrained optimization.",
    images: ["/og.png"],
  },
};

export default function EnglishLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en"><body>{children}</body></html>;
}
