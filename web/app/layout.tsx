import type { Metadata } from "next";
import "./globals.css";
import { Nav } from "./nav";
import { I18nProvider } from "@/lib/i18n";

export const metadata: Metadata = {
  title: "shortlink · gateway console",
  description: "URL shortener on a hand-written .NET API gateway",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>
        <I18nProvider>
          <div className="container">
            <Nav />
            {children}
          </div>
        </I18nProvider>
      </body>
    </html>
  );
}
