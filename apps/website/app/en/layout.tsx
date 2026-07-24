import type { Metadata } from "next";
import "../globals.css";

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  title: "Ingot — Manufacturing Production Data & Process Analysis",
  description: "Connect equipment processes, batches, workpieces, recipes, tooling, and inspections into production histories for yield, machine, and workpiece investigations.",
  applicationName: "Ingot",
  keywords: [
    "Ingot", "process investigation", "yield analysis", "Ingot Chat",
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
    title: "Ingot — Manufacturing Production Data & Process Analysis",
    description: "Turn every production run into traceable engineering evidence through connected plant data, production histories, and process investigation.",
    url: `${origin}/en/`,
    locale: "en_US",
    alternateLocale: ["zh_CN"],
    siteName: "Ingot",
    type: "website",
    images: [{ url: "/og.png", width: 1200, height: 630, alt: "Ingot — Manufacturing Production Data & Process Analysis" }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ingot — Manufacturing Production Data & Process Analysis",
    description: "Connected plant data, production histories, and process investigation.",
    images: ["/og.png"],
  },
};

export default function EnglishLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en"><body>{children}</body></html>;
}
