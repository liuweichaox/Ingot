import type { Metadata } from "next";
import "../globals.css";

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  title: "Ingot — Open-source Process Diagnosis & Optimization",
  description: "Link trajectories, actual recipes, and quality outcomes into traceable evidence: explain which variable made this run miss spec, then recommend the next experiment with physical priors and Bayesian optimization.",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "AI process R&D", "process diagnosis", "root cause analysis",
    "cycle diagnosis", "experiment design", "process optimization",
    "process window", "mechanism fusion", "Bayesian optimization", "process knowledge",
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
    title: "Ingot — Make every experiment converge on a better process",
    description: "Open-source process diagnosis and optimization for expensive, small-data manufacturing experiments.",
    url: `${origin}/en/`,
    locale: "en_US",
    alternateLocale: ["zh_CN"],
    siteName: "Ingot",
    type: "website",
    images: [{ url: "/og.png", width: 1734, height: 909, alt: "Ingot — Explain this run, optimize the next." }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — Open-source Process Diagnosis & Optimization",
    description: "From real process evidence to an explanation of the deviation and the next verifiable recipe.",
    images: ["/og.png"],
  },
};

export default function EnglishLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en"><body>{children}</body></html>;
}
