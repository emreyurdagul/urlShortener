"use client";

const TOKEN_KEY = "us_token";
const PLAN_KEY = "us_plan";
const EMAIL_KEY = "us_email";

export type Session = { token: string; plan: string; email: string };

export function getSession(): Session | null {
  if (typeof window === "undefined") return null;
  const token = localStorage.getItem(TOKEN_KEY);
  if (!token) return null;
  return {
    token,
    plan: localStorage.getItem(PLAN_KEY) ?? "free",
    email: localStorage.getItem(EMAIL_KEY) ?? "",
  };
}

export function saveSession(s: Session) {
  localStorage.setItem(TOKEN_KEY, s.token);
  localStorage.setItem(PLAN_KEY, s.plan);
  localStorage.setItem(EMAIL_KEY, s.email);
  window.dispatchEvent(new Event("session"));
}

export function clearSession() {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(PLAN_KEY);
  localStorage.removeItem(EMAIL_KEY);
  window.dispatchEvent(new Event("session"));
}

// Carries the API's structured error: a machine-readable `code` the frontend
// localizes (see i18n `tErr`), the raw English `error` as a fallback, and the
// full `body` so localized messages can interpolate params (limit, plan, …).
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

export async function api<T = unknown>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set("Content-Type", "application/json");
  const token = getSession()?.token;
  if (token) headers.set("Authorization", `Bearer ${token}`);

  const res = await fetch(path, { ...init, headers });
  const text = await res.text();
  const body = text ? JSON.parse(text) : {};

  if (!res.ok) {
    throw new ApiError(res.status, body);
  }
  return body as T;
}
