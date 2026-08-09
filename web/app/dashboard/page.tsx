"use client";

import { Fragment, useEffect, useRef, useState } from "react";
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

type Stats = {
  total: number;
  lastClick: string | null;
  daily?: { day: string; count: number }[];
  topReferrers?: { referer: string; count: number }[];
};

type Snapshot = {
  requestRate: number;
  totalRequests: number;
  errorRatio: number;
  rateLimitedRate: number;
  p50Ms: number | null;
  p95Ms: number | null;
  p99Ms: number | null;
  backends: { backend: string; healthy: boolean }[];
};

const BLOCKS = "▁▂▃▄▅▆▇█";

function Sparkline({ values }: { values: number[] }) {
  const max = Math.max(...values, 1);
  const cells = values
    .map((v) => BLOCKS[Math.min(7, Math.max(0, Math.round((v / max) * 7)))])
    .join("");
  return (
    <div className="spark" aria-hidden="true">
      {cells || " ".repeat(8)}
    </div>
  );
}

// Fill the last 7 days (UTC) so the sparkline is a fixed 7-slot shape.
function last7(daily?: { day: string; count: number }[]): number[] {
  const map = new Map((daily ?? []).map((d) => [d.day, d.count]));
  const out: number[] = [];
  const now = new Date();
  for (let i = 6; i >= 0; i--) {
    const d = new Date(now);
    d.setUTCDate(now.getUTCDate() - i);
    out.push(map.get(d.toISOString().slice(0, 10)) ?? 0);
  }
  return out;
}

export default function DashboardPage() {
  const { t, tErr } = useI18n();
  const router = useRouter();
  const [links, setLinks] = useState<LinkItem[]>([]);
  const [stats, setStats] = useState<Record<string, Stats>>({});
  const [loading, setLoading] = useState(true);
  const [snap, setSnap] = useState<Snapshot | null>(null);
  const [connected, setConnected] = useState(false);
  const [expanded, setExpanded] = useState<string | null>(null);
  const [error, setError] = useState("");
  const [page, setPage] = useState(0);
  const [hasMore, setHasMore] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const history = useRef<number[]>([]);

  async function loadPage(next: number) {
    const d = await api<{ links: LinkItem[]; hasMore: boolean }>(`/api/links?page=${next}&pageSize=20`);
    setLinks((prev) => (next === 1 ? d.links : [...prev, ...d.links]));
    setPage(next);
    setHasMore(d.hasMore);
    const entries = await Promise.all(
      d.links.map((l) =>
        api<Stats & { code: string }>(
          `/api/analytics/${encodeURIComponent(l.code)}?domain=${encodeURIComponent(l.domain)}`,
        )
          .then((s) => [l.code, s] as const)
          .catch(() => [l.code, { total: 0, lastClick: null }] as const),
      ),
    );
    setStats((prev) => ({ ...prev, ...Object.fromEntries(entries) }));
  }

  useEffect(() => {
    if (!getSession()) {
      router.push("/login");
      return;
    }
    loadPage(1)
      .catch(() => {})
      .finally(() => setLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
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

  async function remove(l: LinkItem) {
    if (!window.confirm(t("dash.confirmDelete", { code: l.code }))) return;
    setError("");
    try {
      await api(`/api/links/${encodeURIComponent(l.code)}?domain=${encodeURIComponent(l.domain)}`, {
        method: "DELETE",
      });
      setLinks((ls) => ls.filter((x) => x.code !== l.code || x.domain !== l.domain));
    } catch (err) {
      setError(tErr(err));
    }
  }

  async function edit(l: LinkItem) {
    const url = window.prompt(t("dash.editPrompt"), l.targetUrl);
    if (!url || url === l.targetUrl) return;
    setError("");
    try {
      const res = await api<{ targetUrl: string }>(
        `/api/links/${encodeURIComponent(l.code)}?domain=${encodeURIComponent(l.domain)}`,
        { method: "PUT", body: JSON.stringify({ url }) },
      );
      setLinks((ls) => ls.map((x) => (x.code === l.code && x.domain === l.domain ? { ...x, targetUrl: res.targetUrl } : x)));
    } catch (err) {
      setError(tErr(err));
    }
  }

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
        {error && <div className="error">{error}</div>}
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
                <th className="act-col">{t("dash.colActions")}</th>
              </tr>
            </thead>
            <tbody>
              {links.map((l) => {
                const s = stats[l.code];
                const open = expanded === l.code;
                return (
                  <Fragment key={l.code}>
                    <tr>
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
                        <button
                          className="clicks-btn"
                          onClick={() => setExpanded(open ? null : l.code)}
                          title={t("dash.detailToggle")}
                        >
                          <span className="clicks-n">{s ? s.total : "·"}</span>
                          {s?.lastClick && (
                            <span className="clicks-last">{new Date(s.lastClick).toLocaleDateString()}</span>
                          )}
                        </button>
                      </td>
                      <td className="tgt" data-label={t("dash.colCreated")}>{new Date(l.createdAt).toLocaleDateString()}</td>
                      <td className="actions" data-label={t("dash.colActions")}>
                        <button className="icon-btn" onClick={() => edit(l)} title={t("dash.edit")}>✎</button>
                        <button className="icon-btn danger" onClick={() => remove(l)} title={t("dash.delete")}>✕</button>
                      </td>
                    </tr>
                    {open && (
                      <tr className="detail-row">
                        <td colSpan={6}>
                          <div className="detail">
                            <div className="detail-block">
                              <div className="detail-k">{t("dash.detail7d")}</div>
                              {s && s.total > 0 ? (
                                <Sparkline values={last7(s.daily)} />
                              ) : (
                                <div className="hint">{t("dash.detailNone")}</div>
                              )}
                            </div>
                            <div className="detail-block">
                              <div className="detail-k">{t("dash.detailReferrers")}</div>
                              {s?.topReferrers?.length ? (
                                <ul className="ref-list">
                                  {s.topReferrers.map((r) => (
                                    <li key={r.referer}>
                                      <span className="ref-n">{r.count}</span>
                                      <span className="ref-src">{r.referer === "direct" ? t("dash.direct") : r.referer}</span>
                                    </li>
                                  ))}
                                </ul>
                              ) : (
                                <div className="hint">{t("dash.detailNone")}</div>
                              )}
                            </div>
                          </div>
                        </td>
                      </tr>
                    )}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        )}
        {hasMore && (
          <div style={{ textAlign: "center", marginTop: 18 }}>
            <button
              className="ghost"
              disabled={loadingMore}
              onClick={async () => {
                setLoadingMore(true);
                try {
                  await loadPage(page + 1);
                } finally {
                  setLoadingMore(false);
                }
              }}
            >
              {loadingMore ? "…" : t("dash.loadMore")}
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
