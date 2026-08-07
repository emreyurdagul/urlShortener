"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useState } from "react";
import { clearSession, getSession, type Session } from "@/lib/api";
import { LOCALES, LOCALE_LABELS, useI18n, type Locale } from "@/lib/i18n";

// Legacy "premium" tokens read as the top tier.
function normPlan(plan: string): "free" | "plus" | "pro" {
  if (plan === "plus" || plan === "pro") return plan;
  if (plan === "premium") return "pro";
  return "free";
}

function LangSwitcher() {
  const { locale, setLocale, t } = useI18n();
  return (
    <select
      className="lang"
      value={locale}
      aria-label={t("lang.switch")}
      onChange={(e) => setLocale(e.target.value as Locale)}
    >
      {LOCALES.map((l) => (
        <option key={l} value={l}>
          {LOCALE_LABELS[l]}
        </option>
      ))}
    </select>
  );
}

export function Nav() {
  const path = usePathname();
  const { t } = useI18n();
  const [session, setSession] = useState<Session | null>(null);

  useEffect(() => {
    const sync = () => setSession(getSession());
    sync();
    window.addEventListener("session", sync);
    return () => window.removeEventListener("session", sync);
  }, []);

  return (
    <nav className="nav">
      <Link href="/" className="brand">
        short<span>link</span>
      </Link>
      <div className="spacer" />
      <Link href="/" className={path === "/" ? "active" : ""}>
        {t("nav.create")}
      </Link>
      <Link href="/dashboard" className={path === "/dashboard" ? "active" : ""}>
        {t("nav.dashboard")}
      </Link>
      {session ? (
        <>
          {normPlan(session.plan) !== "pro" && (
            <Link href="/upgrade" className={path === "/upgrade" ? "active" : ""}>
              {t("nav.upgrade")}
            </Link>
          )}
          <span className={`badge ${normPlan(session.plan)}`}>{normPlan(session.plan)}</span>
          <a
            href="#"
            onClick={(e) => {
              e.preventDefault();
              clearSession();
            }}
          >
            {t("nav.signout")}
          </a>
        </>
      ) : (
        <Link href="/login" className={path === "/login" ? "active" : ""}>
          {t("nav.signin")}
        </Link>
      )}
      <LangSwitcher />
    </nav>
  );
}
