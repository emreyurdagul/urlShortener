"use client";

import { useEffect, useState } from "react";
import { api, ApiError } from "@/lib/api";

type Domains = { domains: string[]; default: string };
type Created = { code: string; domain: string; shortUrl: string };

export default function CreatePage() {
  const [url, setUrl] = useState("");
  const [domain, setDomain] = useState("");
  const [codeLength, setCodeLength] = useState(7);
  const [domains, setDomains] = useState<string[]>([]);
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

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError("");
    setResult(null);
    setBusy(true);
    try {
      const created = await api<Created>("/api/links", {
        method: "POST",
        body: JSON.stringify({ url, domain, codeLength }),
      });
      setResult(created);
      setUrl("");
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Something went wrong";
      setError(msg);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      <div className="hero">
        <h1>Shorten a link</h1>
        <p>Pick a domain, choose how short the code should be, get an instant QR.</p>
      </div>

      <div className="card">
        <form onSubmit={submit}>
          <div className="field">
            <label>Destination URL</label>
            <input
              type="url"
              required
              placeholder="https://example.com/some/long/path"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
            />
          </div>

          <div className="row">
            <div className="field">
              <label>Domain</label>
              <select value={domain} onChange={(e) => setDomain(e.target.value)}>
                {domains.map((d) => (
                  <option key={d} value={d}>
                    {d}
                  </option>
                ))}
              </select>
            </div>
            <div className="field">
              <label>Code length · {codeLength} chars</label>
              <input
                type="range"
                min={5}
                max={8}
                value={codeLength}
                onChange={(e) => setCodeLength(Number(e.target.value))}
              />
              <div className="hint">4-char codes unlock with premium.</div>
            </div>
          </div>

          <button type="submit" disabled={busy}>
            {busy ? "Shortening…" : "Shorten"}
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
              <div className="target">code {result.code} · on {result.domain}</div>
              <div style={{ marginTop: 10 }}>
                <button
                  className="ghost"
                  onClick={() => navigator.clipboard?.writeText(result.shortUrl)}
                >
                  Copy link
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
