"use client";

import { useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { api, getSession } from "@/lib/api";
import { useI18n } from "@/lib/i18n";

type LinkItem = {
  code: string;
  domain: string;
  targetUrl: string;
  shortUrl: string;
  qrUrl: string;
  createdAt: string;
};

type Stats = { total: number; lastClick: string | null };

type Snapshot = {
  requestRate: number;
  totalRequests: number;
  errorRatio: number;
  rateLimitedRate: number;
  // null when there's no recent traffic — latency is meaningless then, so the
  // UI shows "—" instead of a frozen histogram artifact.
  p50Ms: number | null;
  p95Ms: number | null;
  p99Ms: number | null;
  backends: { backend: string; healthy: boolean }[];
};

const BLOCKS = "▁▂▃▄▅▆▇█";

// A terminal-native sparkline: one phosphor block per sample, tallest = live max.
function Sparkline({ values }: { values: number[] }) {
  const max = Math.max(...values, 1);
  const cells = values
    .map((v) => BLOCKS[Math.min(7, Math.max(0, Math.round((v / max) * 7)))])
    .join("");
  return (
    <div className="spark" aria-hidden="true">
      {cells || " ".repeat(8)}
    </div>
  );
}

export default function DashboardPage() {
  const { t } = useI18n();
  const router = useRouter();
  const [links, setLinks] = useState<LinkItem[]>([]);
  const [stats, setStats] = useState<Record<string, Stats>>({});
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
      .then((d) => {
        setLinks(d.links);
        // Per-link click totals from the analytics service. N calls (one per
        // link) is fine at this scale; a batch endpoint would be the next step.
        Promise.all(
          d.links.map((l) =>
            api<Stats & { code: string }>(
              `/api/analytics/${encodeURIComponent(l.code)}?domain=${encodeURIComponent(l.domain)}`,
            )
              .then((s) => [l.code, { total: s.total, lastClick: s.lastClick }] as const)
              .catch(() => [l.code, { total: 0, lastClick: null }] as const),
          ),
        ).then((entries) => setStats(Object.fromEntries(entries)));
      })
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
        <h1>{t("dash.title")}</h1>
        <p>{t("dash.subtitle")}</p>
      </div>

      <div className="card">
        <div className="section-title">
          {t("dash.telemetry")}
          <span className="live">
            <span className={`dot ${connected ? "up" : "down"}`} />
            {connected ? t("dash.streaming") : t("dash.reconnecting")}
          </span>
        </div>

        <div className="metrics-grid">
          <div className="stat">
            <div className="k">{t("dash.reqPerSec")}</div>
            <div className="v">{snap ? snap.requestRate.toFixed(1) : "—"}</div>
            <Sparkline values={history.current} />
            <div className="hint">{t("dash.total", { n: snap ? snap.totalRequests.toLocaleString() : "—" })}</div>
          </div>
          <div className="stat">
            <div className="k">{t("dash.latencyP95")}</div>
            <div className="v">
              {snap?.p95Ms != null ? snap.p95Ms.toFixed(1) : "—"} <small>ms</small>
            </div>
            <div className="hint">
              {t("dash.percentiles", {
                p50: snap?.p50Ms != null ? snap.p50Ms.toFixed(1) : "—",
                p99: snap?.p99Ms != null ? snap.p99Ms.toFixed(1) : "—",
              })}
            </div>
          </div>
          <div className="stat">
            <div className="k">{t("dash.errorRatio")}</div>
            <div className="v">{snap ? (snap.errorRatio * 100).toFixed(2) : "—"}<small>%</small></div>
          </div>
          <div className="stat">
            <div className="k">{t("dash.rateLimited")}</div>
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
        <div className="section-title">{t("dash.yourLinks")}</div>
        {loading ? (
          <div className="empty">{t("dash.loading")}</div>
        ) : links.length === 0 ? (
          <div className="empty">{t("dash.empty")}</div>
        ) : (
          <table className="links-table">
            <thead>
              <tr>
                <th>{t("dash.colQr")}</th>
                <th>{t("dash.colShort")}</th>
                <th>{t("dash.colDest")}</th>
                <th className="num-col">{t("dash.colClicks")}</th>
                <th>{t("dash.colCreated")}</th>
              </tr>
            </thead>
            <tbody>
              {links.map((l) => {
                const s = stats[l.code];
                return (
                  <tr key={l.code}>
                    <td data-label={t("dash.colQr")}>
                      <img src={l.qrUrl} alt="QR" />
                    </td>
                    <td className="code" data-label={t("dash.colShort")}>
                      <a href={l.shortUrl} target="_blank" rel="noreferrer">
                        {l.domain}/{l.code}
                      </a>
                    </td>
                    <td className="tgt" data-label={t("dash.colDest")}>{l.targetUrl}</td>
                    <td className="clicks" data-label={t("dash.colClicks")}>
                      <span className="clicks-n">{s ? s.total : "·"}</span>
                      {s?.lastClick && (
                        <span className="clicks-last">{new Date(s.lastClick).toLocaleDateString()}</span>
                      )}
                    </td>
                    <td className="tgt" data-label={t("dash.colCreated")}>{new Date(l.createdAt).toLocaleDateString()}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
