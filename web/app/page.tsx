"use client";

import { useEffect, useState } from "react";
import { api, ApiError, getSession } from "@/lib/api";
import { useI18n } from "@/lib/i18n";

type Domains = { domains: string[]; default: string };
type Created = { code: string; domain: string; shortUrl: string };

// Shortest code length each tier may request (mirrors the backend Plans).
const MIN_BY_PLAN: Record<string, number> = { free: 5, plus: 3, pro: 1, premium: 1 };

export default function CreatePage() {
  const { t } = useI18n();
  const [url, setUrl] = useState("");
  const [domain, setDomain] = useState("");
  const [codeLength, setCodeLength] = useState(7);
  const [customCode, setCustomCode] = useState("");
  const [domains, setDomains] = useState<string[]>([]);
  const [plan, setPlan] = useState("free");
  const [result, setResult] = useState<Created | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api<Domains>("/api/domains")
      .then((d) => {
        setDomains(d.domains);
        setDomain(d.default);
      })
      .catch(() => {});
  }, []);

  useEffect(() => {
    const sync = () => setPlan(getSession()?.plan ?? "free");
    sync();
    window.addEventListener("session", sync);
    return () => window.removeEventListener("session", sync);
  }, []);

  const norm = plan === "premium" ? "pro" : plan;
  const minLen = MIN_BY_PLAN[plan] ?? 5;
  const canVanity = norm === "pro";

  // Keep the slider value at/above the tier floor when the plan changes.
  useEffect(() => {
    setCodeLength((c) => Math.max(c, minLen));
  }, [minLen]);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError("");
    setResult(null);
    setBusy(true);
    try {
      const vanity = canVanity ? customCode.trim() : "";
      const created = await api<Created>("/api/links", {
        method: "POST",
        body: JSON.stringify({ url, domain, codeLength, ...(vanity ? { code: vanity } : {}) }),
      });
      setResult(created);
      setUrl("");
      setCustomCode("");
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("common.error");
      setError(msg);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      <div className="hero">
        <h1>{t("create.title")}</h1>
        <p>{t("create.subtitle")}</p>
      </div>

      <div className="card">
        <form onSubmit={submit}>
          <div className="field">
            <label>{t("create.url")}</label>
            <input
              type="url"
              required
              placeholder={t("create.urlPlaceholder")}
              value={url}
              onChange={(e) => setUrl(e.target.value)}
            />
          </div>

          <div className="row">
            <div className="field">
              <label>{t("create.domain")}</label>
              <select value={domain} onChange={(e) => setDomain(e.target.value)}>
                {domains.map((d) => (
                  <option key={d} value={d}>
                    {d}
                  </option>
                ))}
              </select>
            </div>
            <div className="field">
              <label>{t("create.codeLength", { n: codeLength })}</label>
              <input
                type="range"
                min={minLen}
                max={8}
                value={codeLength}
                onChange={(e) => setCodeLength(Number(e.target.value))}
              />
              <div className="hint">
                {norm === "pro" ? (
                  t("create.hint.pro")
                ) : (
                  <>
                    {t(norm === "plus" ? "create.hint.plus" : "create.hint.free")}{" "}
                    <a href="/upgrade">{t("create.upgradeCta")}</a>
                  </>
                )}
              </div>
            </div>
          </div>

          {canVanity && (
            <div className="field">
              <label>{t("create.customCode")}</label>
              <input
                value={customCode}
                onChange={(e) => setCustomCode(e.target.value)}
                placeholder={t("create.customPlaceholder")}
                maxLength={32}
              />
              <div className="hint">{t("create.customHint")}</div>
            </div>
          )}

          <button type="submit" disabled={busy}>
            {busy ? t("create.submitBusy") : t("create.submit")}
          </button>
          {error && <div className="error">{error}</div>}
        </form>

        {result && (
          <div className="result">
            <img src={`/api/links/${result.code}/qr`} alt="QR code" />
            <div>
              <div className="short">
                <a href={result.shortUrl} target="_blank" rel="noreferrer">
                  {result.shortUrl}
                </a>
              </div>
              <div className="target">{t("create.resultMeta", { code: result.code, domain: result.domain })}</div>
              <div style={{ marginTop: 10 }}>
                <button
                  className="ghost"
                  onClick={() => navigator.clipboard?.writeText(result.shortUrl)}
                >
                  {t("create.copy")}
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
