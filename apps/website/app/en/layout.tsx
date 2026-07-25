import type { Metadata } from "next";
import "../globals.css";

const origin = "https://ingotstack.com";

export const metadata: Metadata = {
  metadataBase: new URL(origin),
  title: "Ingot — AI Process R&D for Manufacturing",
  description: "Fuse experimental data, real-time process data, physical mechanisms, and expert knowledge to design experiments, optimize parameters, validate process windows, and shorten development cycles.",
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
    title: "Ingot — AI Process R&D for Manufacturing",
    description: "Use fewer experiments to find and validate reliable processes faster.",
    url: `${origin}/en/`,
    locale: "en_US",
    alternateLocale: ["zh_CN"],
    siteName: "Ingot",
    type: "website",
  },
  twitter: {
    card: "summary",
    title: "Ingot — AI Process R&D for Manufacturing",
    description: "Fuse data, physical mechanisms, and expert knowledge to shorten process-development cycles.",
  },
};

export default function EnglishLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="en"><body>{children}</body></html>;
}
