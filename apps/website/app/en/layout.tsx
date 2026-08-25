
import type { Metadata } from "next";
import "../globals.css";

// Defines canonical English metadata for the public product entry point.

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  title: "Ingot — Open-source Process Diagnosis & Optimization",
  description: "An open-source process diagnosis and optimization system for run-evidence linkage, process diagnosis, experiment design, and constrained optimization.",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "process diagnosis", "process optimization", "process R&D", "process engineer decisions",
    "process executions", "experiment design", "constrained optimization",
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
    title: "Ingot — Understand this run. Choose the right next experiment.",
    description: "Connect real-run evidence, narrow candidate causes, design validation, and optimize the next experiment within safety boundaries.",
    url: `${origin}/en/`,
    locale: "en_US",
    alternateLocale: ["zh_CN"],
    siteName: "Ingot",
    type: "website",
    images: [{ url: "/og.png", width: 1200, height: 630, alt: "Ingot — Understand this run. Choose the right next experiment." }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — Open-source Process Diagnosis & Optimization",
    description: "Use traceable run evidence for process diagnosis, experiment design, and constrained optimization.",
    images: ["/og.png"],
  },
};

export default function EnglishLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en"><body>{children}</body></html>;
}
