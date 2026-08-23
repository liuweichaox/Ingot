
import type { Metadata } from "next";
import "../globals.css";

// Defines canonical English metadata for the public product entry point.

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  title: "Ingot — Open-source Process Diagnosis & Optimization",
  description: "Turn real runs into comparable, testable engineering evidence, avoid unproductive experiments, and reach target process conditions faster.",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "data-supported process R&D", "process engineer decisions", "process diagnosis",
    "process executions", "experiment design", "process R&D",
    "process window", "process data", "Bayesian optimization", "process knowledge",
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
    title: "Ingot — Fewer wasted experiments, faster routes to target conditions",
    description: "Reconstruct real runs, compare important differences, design validation, and select the next experiment.",
    url: `${origin}/en/`,
    locale: "en_US",
    alternateLocale: ["zh_CN"],
    siteName: "Ingot",
    type: "website",
    images: [{ url: "/og.png", width: 1200, height: 630, alt: "Ingot — Turn real runs into testable process evidence." }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — Open-source Process Diagnosis & Optimization",
    description: "Use real run evidence for process comparison, causal validation, and the next experiment.",
    images: ["/og.png"],
  },
};

export default function EnglishLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en"><body>{children}</body></html>;
}
