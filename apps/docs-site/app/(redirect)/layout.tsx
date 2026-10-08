
// Defines the documentation entry-point metadata and locale redirect shell.
import type { Metadata } from "next";
import "../globals.css";

export const metadata: Metadata = {
  metadataBase: new URL("https://docs.ingotstack.com"),
  title: { default: "Ingot Docs", template: "%s · Ingot Docs" },
  description: "组织研发项目、实验记录与运行证据，支持质量分析、工艺追因和配方优化。",
  robots: { index: true, follow: true },
};

export default function RedirectLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="zh-CN"><body>{children}</body></html>;
}
