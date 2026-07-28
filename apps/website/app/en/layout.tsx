import type { Metadata } from "next";
import "../globals.css";

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  title: "Ingot — Closed-loop AI Process Optimization",
  description: "Continuously learn from trajectories, actual recipes, and quality outcomes to choose the next manufacturing experiment.",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "AI process R&D", "experiment design", "process optimization",
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
    description: "Closed-loop process optimization for expensive, small-data manufacturing experiments.",
    url: `${origin}/en/`,
    locale: "en_US",
    alternateLocale: ["zh_CN"],
    siteName: "Ingot",
    type: "website",
    images: [{ url: "/og.png", width: 1734, height: 909, alt: "Ingot — The next run, optimized." }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — Closed-loop AI Process Optimization",
    description: "Turn real process evidence into the next verifiable recipe.",
    images: ["/og.png"],
  },
};

export default function EnglishLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en"><body>{children}</body></html>;
}
