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

export class ApiError extends Error {
  status: number;
  constructor(status: number, message: string) {
    super(message);
    this.status = status;
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
    throw new ApiError(res.status, body.error ?? `Request failed (${res.status})`);
  }
  return body as T;
}
