"use client";

import { useEffect, useState } from "react";
import { api, getSession, saveSession, type Session } from "@/lib/api";
import { useI18n } from "@/lib/i18n";

type TierId = "free" | "plus" | "pro";
type Tier = { id: TierId; price: number; features: string[] };

const TIERS: Tier[] = [
  { id: "free", price: 0, features: ["upgrade.feat.codes5", "upgrade.feat.links50", "upgrade.feat.analytics"] },
  { id: "plus", price: 3, features: ["upgrade.feat.codes3", "upgrade.feat.linksUnlim", "upgrade.feat.analytics"] },
  { id: "pro", price: 9, features: ["upgrade.feat.codes1", "upgrade.feat.linksUnlim", "upgrade.feat.vanity", "upgrade.feat.analytics"] },
];

// Legacy "premium" tokens read as the top tier.
function normPlan(plan: string | undefined): TierId {
  if (plan === "plus" || plan === "pro") return plan;
  if (plan === "premium") return "pro";
  return "free";
}

export default function UpgradePage() {
  const { t, tErr } = useI18n();
  const [session, setSession] = useState<Session | null>(null);
  const [ready, setReady] = useState(false);
  const [busy, setBusy] = useState<TierId | null>(null);
  const [error, setError] = useState("");
  const [done, setDone] = useState("");

  useEffect(() => {
    const sync = () => setSession(getSession());
    sync();
    setReady(true);
    window.addEventListener("session", sync);
    return () => window.removeEventListener("session", sync);
  }, []);

  const current = session ? normPlan(session.plan) : null;

  async function choose(plan: Exclude<TierId, "free">) {
    setError("");
    setDone("");
    setBusy(plan);
    try {
      const res = await api<{ token: string; plan: string }>("/api/auth/upgrade", {
        method: "POST",
        body: JSON.stringify({ plan }),
      });
      saveSession({
        token: res.token,
        refreshToken: session?.refreshToken ?? "",
        plan: res.plan,
        email: session?.email ?? "",
      });
      setDone(t("upgrade.success", { plan: t(`tier.${normPlan(res.plan)}.name`) }));
    } catch (err) {
      setError(tErr(err));
    } finally {
      setBusy(null);
    }
  }

  return (
    <div>
      <div className="hero">
        <h1>{t("upgrade.title")}</h1>
        <p>{t("upgrade.subtitle")}</p>
      </div>

      <div className="demo-banner">{t("upgrade.demo")}</div>

      {ready && !session ? (
        <div className="card">
          <div className="empty">
            {t("upgrade.signInFirst")} <a href="/login">{t("nav.signin")}</a>
          </div>
        </div>
      ) : (
        <div className="tiers">
          {TIERS.map((tier) => {
            const isCurrent = current === tier.id;
            return (
              <div className={`tier ${tier.id}${isCurrent ? " current" : ""}`} key={tier.id}>
                <div className="tier-head">
                  <span className="tier-name">{t(`tier.${tier.id}.name`)}</span>
                  {isCurrent && <span className="tier-cur">{t("upgrade.current")}</span>}
                </div>
                <div className="tier-price">
                  {tier.price === 0 ? (
                    t("upgrade.free")
                  ) : (
                    <>
                      ${tier.price}
                      <small>{t("upgrade.perMonth")}</small>
                    </>
                  )}
                </div>
                <ul className="feats">
                  {tier.features.map((f) => (
                    <li key={f}>{t(f)}</li>
                  ))}
                </ul>
                {tier.id === "free" ? (
                  <div className="tier-foot">{isCurrent ? t("upgrade.ctaCurrent") : ""}</div>
                ) : isCurrent ? (
                  <button disabled>{t("upgrade.ctaCurrent")}</button>
                ) : (
                  <button onClick={() => choose(tier.id as "plus" | "pro")} disabled={busy !== null}>
                    {busy === tier.id ? "…" : t("upgrade.cta")}
                  </button>
                )}
              </div>
            );
          })}
        </div>
      )}

      {done && <div className="notice">{done}</div>}
      {error && <div className="error">{error}</div>}
    </div>
  );
}
