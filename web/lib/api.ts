"use client";

const TOKEN_KEY = "us_token";
const REFRESH_KEY = "us_refresh";
const PLAN_KEY = "us_plan";
const EMAIL_KEY = "us_email";

export type Session = { token: string; refreshToken: string; plan: string; email: string };

export function getSession(): Session | null {
  if (typeof window === "undefined") return null;
  const token = localStorage.getItem(TOKEN_KEY);
  if (!token) return null;
  return {
    token,
    refreshToken: localStorage.getItem(REFRESH_KEY) ?? "",
    plan: localStorage.getItem(PLAN_KEY) ?? "free",
    email: localStorage.getItem(EMAIL_KEY) ?? "",
  };
}

export function saveSession(s: Session) {
  localStorage.setItem(TOKEN_KEY, s.token);
  localStorage.setItem(REFRESH_KEY, s.refreshToken);
  localStorage.setItem(PLAN_KEY, s.plan);
  localStorage.setItem(EMAIL_KEY, s.email);
  window.dispatchEvent(new Event("session"));
}

export function clearSession() {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(REFRESH_KEY);
  localStorage.removeItem(PLAN_KEY);
  localStorage.removeItem(EMAIL_KEY);
  window.dispatchEvent(new Event("session"));
}

/** Revokes the refresh token server-side, then clears the local session. */
export async function logout() {
  const s = getSession();
  if (s?.refreshToken) {
    try {
      await fetch("/api/auth/logout", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ refreshToken: s.refreshToken }),
      });
    } catch {
      /* best-effort */
    }
  }
  clearSession();
}

export class ApiError extends Error {
  status: number;
  code?: string;
  body: Record<string, unknown>;
  constructor(status: number, body: Record<string, unknown>) {
    super(typeof body.error === "string" ? body.error : `Request failed (${status})`);
    this.status = status;
    this.code = typeof body.code === "string" ? body.code : undefined;
    this.body = body;
  }
}

// Exchange the refresh token for a fresh access token. Concurrent callers share
// one in-flight refresh. Returns the new access token, or null on failure (the
// session is cleared).
let refreshing: Promise<string | null> | null = null;
function tryRefresh(): Promise<string | null> {
  const s = getSession();
  if (!s?.refreshToken) return Promise.resolve(null);
  refreshing ??= (async () => {
    try {
      const res = await fetch("/api/auth/refresh", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ refreshToken: s.refreshToken }),
      });
      if (!res.ok) {
        clearSession();
        return null;
      }
      const b = await res.json();
      saveSession({ token: b.token, refreshToken: b.refreshToken, plan: b.plan, email: s.email });
      return b.token as string;
    } catch {
      clearSession();
      return null;
    } finally {
      refreshing = null;
    }
  })();
  return refreshing;
}

export async function api<T = unknown>(path: string, init: RequestInit = {}): Promise<T> {
  const send = (token?: string) => {
    const headers = new Headers(init.headers);
    headers.set("Content-Type", "application/json");
    if (token) headers.set("Authorization", `Bearer ${token}`);
    return fetch(path, { ...init, headers });
  };

  let res = await send(getSession()?.token);
  // Access token rejected while signed in? Refresh once and retry transparently.
  // If there's no refresh token (a stale pre-refresh session) or the refresh
  // fails, the session is dead — drop it so the UI reflects signed-out instead
  // of silently 401ing every request.
  if (res.status === 401 && getSession()) {
    const fresh = getSession()?.refreshToken ? await tryRefresh() : null;
    if (fresh) res = await send(fresh);
    else clearSession();
  }

  const text = await res.text();
  const body = text ? JSON.parse(text) : {};
  if (!res.ok) {
    throw new ApiError(res.status, body);
  }
  return body as T;
}
