import Link from "next/link";

export default function Home() {
  return (
    <main className="redirect">
      <meta httpEquiv="refresh" content="0; url=/zh/" />
      <p>正在进入 <Link href="/zh/">Ingot 中文文档</Link>…</p>
    </main>
  );
}
