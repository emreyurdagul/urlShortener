"use client";

import { useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { api, getSession } from "@/lib/api";

type LinkItem = {
  code: string;
  domain: string;
  targetUrl: string;
  shortUrl: string;
  qrUrl: string;
  createdAt: string;
};

type Snapshot = {
  requestRate: number;
  errorRatio: number;
  rateLimitedRate: number;
  p50Ms: number;
  p95Ms: number;
  p99Ms: number;
  backends: { backend: string; healthy: boolean }[];
};

function Sparkline({ values }: { values: number[] }) {
  if (values.length < 2) return <svg width="100%" height="40" />;
  const max = Math.max(...values, 1);
  const w = 100;
  const pts = values
    .map((v, i) => `${(i / (values.length - 1)) * w},${40 - (v / max) * 36 - 2}`)
    .join(" ");
  return (
    <svg width="100%" height="40" viewBox={`0 0 ${w} 40`} preserveAspectRatio="none">
      <polyline points={pts} fill="none" stroke="url(#g)" strokeWidth="1.5" vectorEffect="non-scaling-stroke" />
      <defs>
        <linearGradient id="g" x1="0" x2="1">
          <stop offset="0" stopColor="#6d8bff" />
          <stop offset="1" stopColor="#9d7bff" />
        </linearGradient>
      </defs>
    </svg>
  );
}

export default function DashboardPage() {
  const router = useRouter();
  const [links, setLinks] = useState<LinkItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [snap, setSnap] = useState<Snapshot | null>(null);
  const [connected, setConnected] = useState(false);
  const history = useRef<number[]>([]);

  useEffect(() => {
    if (!getSession()) {
      router.push("/login");
      return;
    }
    api<{ links: LinkItem[] }>("/api/links")
      .then((d) => setLinks(d.links))
      .catch(() => {})
      .finally(() => setLoading(false));
  }, [router]);

  useEffect(() => {
    const host = typeof window !== "undefined" ? window.location.hostname : "localhost";
    const url = process.env.NEXT_PUBLIC_GATEWAY_WS || `ws://${host}:8080/ws/metrics`;
    let ws: WebSocket | null = null;
    let stopped = false;

    function connect() {
      ws = new WebSocket(url);
      ws.onopen = () => setConnected(true);
      ws.onclose = () => {
        setConnected(false);
        if (!stopped) setTimeout(connect, 2000);
      };
      ws.onmessage = (e) => {
        const s: Snapshot = JSON.parse(e.data);
        setSnap(s);
        history.current = [...history.current, s.requestRate].slice(-40);
      };
    }
    connect();
    return () => {
      stopped = true;
      ws?.close();
    };
  }, []);

  return (
    <div>
      <div className="hero">
        <h1>Dashboard</h1>
        <p>Your links and live gateway telemetry.</p>
      </div>

      <div className="card">
        <div className="section-title">
          Gateway — live
          <span className="live">
            <span className={`dot ${connected ? "up" : "down"}`} />
            {connected ? "streaming" : "reconnecting…"}
          </span>
        </div>

        <div className="metrics-grid">
          <div className="stat">
            <div className="k">Requests / sec</div>
            <div className="v">{snap ? snap.requestRate.toFixed(1) : "—"}</div>
            <Sparkline values={history.current} />
          </div>
          <div className="stat">
            <div className="k">Latency p95</div>
            <div className="v">
              {snap ? snap.p95Ms.toFixed(1) : "—"} <small>ms</small>
            </div>
            <div className="hint">p50 {snap ? snap.p50Ms.toFixed(1) : "—"} · p99 {snap ? snap.p99Ms.toFixed(1) : "—"} ms</div>
          </div>
          <div className="stat">
            <div className="k">Error ratio</div>
            <div className="v">{snap ? (snap.errorRatio * 100).toFixed(2) : "—"}<small>%</small></div>
          </div>
          <div className="stat">
            <div className="k">Rate-limited / sec</div>
            <div className="v">{snap ? snap.rateLimitedRate.toFixed(1) : "—"}</div>
          </div>
        </div>

        {snap && snap.backends.length > 0 && (
          <div className="chips">
            {snap.backends.map((b) => (
              <div className="chip" key={b.backend}>
                <span className={`dot ${b.healthy ? "up" : "down"}`} />
                {b.backend.replace(/^https?:\/\//, "").split(":")[0]}
              </div>
            ))}
          </div>
        )}
      </div>

      <div className="card">
        <div className="section-title">Your links</div>
        {loading ? (
          <div className="empty">Loading…</div>
        ) : links.length === 0 ? (
          <div className="empty">No links yet — create one to see it here.</div>
        ) : (
          <table className="links-table">
            <thead>
              <tr>
                <th>QR</th>
                <th>Short</th>
                <th>Destination</th>
                <th>Created</th>
              </tr>
            </thead>
            <tbody>
              {links.map((l) => (
                <tr key={l.code}>
                  <td>
                    <img src={l.qrUrl} alt="QR" />
                  </td>
                  <td className="code">
                    <a href={l.shortUrl} target="_blank" rel="noreferrer">
                      {l.domain}/{l.code}
                    </a>
                  </td>
                  <td className="tgt">{l.targetUrl}</td>
                  <td className="tgt">{new Date(l.createdAt).toLocaleDateString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
