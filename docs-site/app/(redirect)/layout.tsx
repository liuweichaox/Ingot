import type { Metadata } from "next";
import "../globals.css";

export const metadata: Metadata = {
  metadataBase: new URL("https://docs.ingotstack.com"),
  title: { default: "Ingot Docs", template: "%s · Ingot Docs" },
  description: "Ingot 可信生产事实、标准事件接入与 Ingot Chat 文档",
  robots: { index: true, follow: true },
};

export default function RedirectLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="zh-CN"><body>{children}</body></html>;
}
