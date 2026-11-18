import type { Metadata } from "next";
import { DM_Sans, Playfair_Display } from "next/font/google";
import "./globals.css";
import { buildMetadata } from "@/lib/seo";

// One immutable image serves dev and production; indexing policy is read at request time.
export const dynamic = "force-dynamic";
export function generateMetadata(): Metadata { return buildMetadata(); }

const playfairDisplay = Playfair_Display({
  subsets: ["latin"],
  weight: ["400", "600", "700"],
  style: ["normal", "italic"],
  variable: "--font-playfair-display",
  display: "swap",
});

const dmSans = DM_Sans({
  subsets: ["latin"],
  weight: ["300", "400", "500", "600"],
  variable: "--font-dm-sans",
  display: "swap",
});

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="pt-BR" className={`${playfairDisplay.variable} ${dmSans.variable}`}>
      <body>{children}</body>
    </html>
  );
}
