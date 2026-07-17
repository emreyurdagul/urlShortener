"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useState } from "react";
import { clearSession, getSession, type Session } from "@/lib/api";

export function Nav() {
  const path = usePathname();
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
        Create
      </Link>
      <Link href="/dashboard" className={path === "/dashboard" ? "active" : ""}>
        Dashboard
      </Link>
      {session ? (
        <>
          <span className={`badge ${session.plan}`}>{session.plan}</span>
          <a
            href="#"
            onClick={(e) => {
              e.preventDefault();
              clearSession();
            }}
          >
            Sign out
          </a>
        </>
      ) : (
        <Link href="/login" className={path === "/login" ? "active" : ""}>
          Sign in
        </Link>
      )}
    </nav>
  );
}
