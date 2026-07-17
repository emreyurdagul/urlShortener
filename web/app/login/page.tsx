"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { api, ApiError, saveSession } from "@/lib/api";

type AuthResponse = { token: string; plan: string };

export default function LoginPage() {
  const router = useRouter();
  const [mode, setMode] = useState<"login" | "register">("login");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError("");
    setBusy(true);
    try {
      const res = await api<AuthResponse>(`/api/auth/${mode}`, {
        method: "POST",
        body: JSON.stringify({ email, password }),
      });
      saveSession({ token: res.token, plan: res.plan, email });
      router.push("/dashboard");
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) setError("Wrong email or password.");
      else if (err instanceof ApiError && err.status === 409) setError("That email is already registered.");
      else setError(err instanceof ApiError ? err.message : "Something went wrong");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div style={{ maxWidth: 400, margin: "0 auto" }}>
      <div className="hero" style={{ textAlign: "center" }}>
        <h1>{mode === "login" ? "Welcome back" : "Create an account"}</h1>
        <p>Own your links, track clicks, unlock premium.</p>
      </div>

      <div className="card">
        <div className="tabs">
          <button className={mode === "login" ? "active" : ""} onClick={() => setMode("login")} type="button">
            Sign in
          </button>
          <button className={mode === "register" ? "active" : ""} onClick={() => setMode("register")} type="button">
            Register
          </button>
        </div>

        <form onSubmit={submit}>
          <div className="field">
            <label>Email</label>
            <input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
          </div>
          <div className="field">
            <label>Password</label>
            <input
              type="password"
              required
              minLength={8}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
            {mode === "register" && <div className="hint">At least 8 characters.</div>}
          </div>
          <button type="submit" disabled={busy} style={{ width: "100%" }}>
            {busy ? "…" : mode === "login" ? "Sign in" : "Create account"}
          </button>
          {error && <div className="error">{error}</div>}
        </form>
      </div>
    </div>
  );
}
