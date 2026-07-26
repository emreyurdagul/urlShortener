"use client";

import { createContext, useContext, useEffect, useState, type ReactNode } from "react";

export const LOCALES = ["tr", "en", "de", "ar"] as const;
export type Locale = (typeof LOCALES)[number];

export const RTL_LOCALES: readonly Locale[] = ["ar"];
export const LOCALE_LABELS: Record<Locale, string> = { tr: "TR", en: "EN", de: "DE", ar: "AR" };

const LOCALE_KEY = "us_locale";

type Dict = Record<string, string>;

const en: Dict = {
  "nav.create": "Create",
  "nav.dashboard": "Dashboard",
  "nav.signin": "Sign in",
  "nav.signout": "Sign out",

  "create.title": "Shorten a link",
  "create.subtitle": "Pick a domain, choose how short the code should be, get an instant QR.",
  "create.url": "Destination URL",
  "create.urlPlaceholder": "https://example.com/some/long/path",
  "create.domain": "Domain",
  "create.codeLength": "Code length · {n} chars",
  "create.premiumHint": "4-char codes unlock with premium.",
  "create.submit": "Shorten",
  "create.submitBusy": "Shortening…",
  "create.resultMeta": "code {code} · on {domain}",
  "create.copy": "Copy link",

  "common.error": "Something went wrong",

  "dash.title": "Dashboard",
  "dash.subtitle": "Your links and live gateway telemetry.",
  "dash.telemetry": "Gateway telemetry",
  "dash.streaming": "streaming",
  "dash.reconnecting": "reconnecting…",
  "dash.reqPerSec": "Requests / sec",
  "dash.latencyP95": "Latency p95",
  "dash.errorRatio": "Error ratio",
  "dash.rateLimited": "Rate-limited / sec",
  "dash.percentiles": "p50 {p50} · p99 {p99} ms",
  "dash.yourLinks": "Your links",
  "dash.loading": "Loading…",
  "dash.empty": "No links yet. Create one to see it here.",
  "dash.colQr": "QR",
  "dash.colShort": "Short",
  "dash.colDest": "Destination",
  "dash.colClicks": "Clicks",
  "dash.colCreated": "Created",

  "auth.welcomeBack": "Welcome back",
  "auth.createAccount": "Create an account",
  "auth.subtitle": "Own your links, track clicks, unlock premium.",
  "auth.signin": "Sign in",
  "auth.register": "Register",
  "auth.email": "Email",
  "auth.password": "Password",
  "auth.passwordHint": "At least 8 characters.",
  "auth.createBtn": "Create account",
  "auth.errWrong": "Wrong email or password.",
  "auth.errExists": "That email is already registered.",

  "lang.switch": "Language",
};

const tr: Dict = {
  "nav.create": "Oluştur",
  "nav.dashboard": "Panel",
  "nav.signin": "Giriş",
  "nav.signout": "Çıkış",

  "create.title": "Link kısalt",
  "create.subtitle": "Bir alan adı seç, kod uzunluğunu ayarla, anında QR al.",
  "create.url": "Hedef URL",
  "create.urlPlaceholder": "https://ornek.com/cok/uzun/bir/adres",
  "create.domain": "Alan adı",
  "create.codeLength": "Kod uzunluğu · {n} karakter",
  "create.premiumHint": "4 karakterlik kodlar premium ile açılır.",
  "create.submit": "Kısalt",
  "create.submitBusy": "Kısaltılıyor…",
  "create.resultMeta": "kod {code} · {domain} üzerinde",
  "create.copy": "Linki kopyala",

  "common.error": "Bir şeyler ters gitti",

  "dash.title": "Panel",
  "dash.subtitle": "Linklerin ve canlı gateway telemetrisi.",
  "dash.telemetry": "Gateway telemetrisi",
  "dash.streaming": "akıyor",
  "dash.reconnecting": "yeniden bağlanıyor…",
  "dash.reqPerSec": "İstek / sn",
  "dash.latencyP95": "Gecikme p95",
  "dash.errorRatio": "Hata oranı",
  "dash.rateLimited": "Sınırlanan / sn",
  "dash.percentiles": "p50 {p50} · p99 {p99} ms",
  "dash.yourLinks": "Linklerin",
  "dash.loading": "Yükleniyor…",
  "dash.empty": "Henüz link yok. Bir tane oluştur, burada görünsün.",
  "dash.colQr": "QR",
  "dash.colShort": "Kısa",
  "dash.colDest": "Hedef",
  "dash.colClicks": "Tıklama",
  "dash.colCreated": "Tarih",

  "auth.welcomeBack": "Tekrar hoş geldin",
  "auth.createAccount": "Hesap oluştur",
  "auth.subtitle": "Linklerine sahip ol, tıklamaları izle, premium'u aç.",
  "auth.signin": "Giriş yap",
  "auth.register": "Kayıt ol",
  "auth.email": "E-posta",
  "auth.password": "Parola",
  "auth.passwordHint": "En az 8 karakter.",
  "auth.createBtn": "Hesap oluştur",
  "auth.errWrong": "E-posta veya parola yanlış.",
  "auth.errExists": "Bu e-posta zaten kayıtlı.",

  "lang.switch": "Dil",
};

const de: Dict = {
  "nav.create": "Erstellen",
  "nav.dashboard": "Dashboard",
  "nav.signin": "Anmelden",
  "nav.signout": "Abmelden",

  "create.title": "Link kürzen",
  "create.subtitle": "Wähle eine Domain, lege die Codelänge fest, erhalte sofort einen QR-Code.",
  "create.url": "Ziel-URL",
  "create.urlPlaceholder": "https://beispiel.de/ein/sehr/langer/pfad",
  "create.domain": "Domain",
  "create.codeLength": "Codelänge · {n} Zeichen",
  "create.premiumHint": "4-stellige Codes gibt es mit Premium.",
  "create.submit": "Kürzen",
  "create.submitBusy": "Wird gekürzt…",
  "create.resultMeta": "Code {code} · auf {domain}",
  "create.copy": "Link kopieren",

  "common.error": "Etwas ist schiefgelaufen",

  "dash.title": "Dashboard",
  "dash.subtitle": "Deine Links und Live-Telemetrie des Gateways.",
  "dash.telemetry": "Gateway-Telemetrie",
  "dash.streaming": "streamt",
  "dash.reconnecting": "verbindet erneut…",
  "dash.reqPerSec": "Anfragen / Sek.",
  "dash.latencyP95": "Latenz p95",
  "dash.errorRatio": "Fehlerquote",
  "dash.rateLimited": "Gedrosselt / Sek.",
  "dash.percentiles": "p50 {p50} · p99 {p99} ms",
  "dash.yourLinks": "Deine Links",
  "dash.loading": "Wird geladen…",
  "dash.empty": "Noch keine Links. Erstelle einen, dann erscheint er hier.",
  "dash.colQr": "QR",
  "dash.colShort": "Kurz",
  "dash.colDest": "Ziel",
  "dash.colClicks": "Klicks",
  "dash.colCreated": "Erstellt",

  "auth.welcomeBack": "Willkommen zurück",
  "auth.createAccount": "Konto erstellen",
  "auth.subtitle": "Besitze deine Links, verfolge Klicks, schalte Premium frei.",
  "auth.signin": "Anmelden",
  "auth.register": "Registrieren",
  "auth.email": "E-Mail",
  "auth.password": "Passwort",
  "auth.passwordHint": "Mindestens 8 Zeichen.",
  "auth.createBtn": "Konto erstellen",
  "auth.errWrong": "E-Mail oder Passwort falsch.",
  "auth.errExists": "Diese E-Mail ist bereits registriert.",

  "lang.switch": "Sprache",
};

const ar: Dict = {
  "nav.create": "إنشاء",
  "nav.dashboard": "لوحة التحكم",
  "nav.signin": "تسجيل الدخول",
  "nav.signout": "تسجيل الخروج",

  "create.title": "اختصر رابطًا",
  "create.subtitle": "اختر نطاقًا، وحدّد طول الرمز، واحصل على رمز QR فورًا.",
  "create.url": "الرابط الهدف",
  "create.urlPlaceholder": "https://example.com/path/طويل/جدا",
  "create.domain": "النطاق",
  "create.codeLength": "طول الرمز · {n} أحرف",
  "create.premiumHint": "رموز من 4 أحرف تُفتح مع الخطة المميزة.",
  "create.submit": "اختصر",
  "create.submitBusy": "جارٍ الاختصار…",
  "create.resultMeta": "الرمز {code} · على {domain}",
  "create.copy": "انسخ الرابط",

  "common.error": "حدث خطأ ما",

  "dash.title": "لوحة التحكم",
  "dash.subtitle": "روابطك وقياسات البوابة الحيّة.",
  "dash.telemetry": "قياسات البوابة",
  "dash.streaming": "يبثّ",
  "dash.reconnecting": "إعادة الاتصال…",
  "dash.reqPerSec": "طلبات / ثانية",
  "dash.latencyP95": "زمن الاستجابة p95",
  "dash.errorRatio": "نسبة الأخطاء",
  "dash.rateLimited": "محدود / ثانية",
  "dash.percentiles": "p50 {p50} · p99 {p99} ms",
  "dash.yourLinks": "روابطك",
  "dash.loading": "جارٍ التحميل…",
  "dash.empty": "لا روابط بعد. أنشئ رابطًا ليظهر هنا.",
  "dash.colQr": "QR",
  "dash.colShort": "المختصر",
  "dash.colDest": "الوجهة",
  "dash.colClicks": "النقرات",
  "dash.colCreated": "التاريخ",

  "auth.welcomeBack": "مرحبًا بعودتك",
  "auth.createAccount": "إنشاء حساب",
  "auth.subtitle": "امتلك روابطك، وتتبّع النقرات، وافتح الخطة المميزة.",
  "auth.signin": "تسجيل الدخول",
  "auth.register": "إنشاء حساب",
  "auth.email": "البريد الإلكتروني",
  "auth.password": "كلمة المرور",
  "auth.passwordHint": "8 أحرف على الأقل.",
  "auth.createBtn": "إنشاء حساب",
  "auth.errWrong": "البريد الإلكتروني أو كلمة المرور غير صحيحة.",
  "auth.errExists": "هذا البريد الإلكتروني مسجَّل بالفعل.",

  "lang.switch": "اللغة",
};

const DICTS: Record<Locale, Dict> = { en, tr, de, ar };

function detectLocale(): Locale {
  if (typeof navigator !== "undefined") {
    const n = navigator.language.slice(0, 2).toLowerCase();
    if ((LOCALES as readonly string[]).includes(n)) return n as Locale;
  }
  return "en";
}

type I18n = {
  locale: Locale;
  dir: "ltr" | "rtl";
  setLocale: (l: Locale) => void;
  t: (key: string, params?: Record<string, string | number>) => string;
};

const I18nContext = createContext<I18n | null>(null);

export function I18nProvider({ children }: { children: ReactNode }) {
  // SSR and first client render agree on "en" (no hydration mismatch); the
  // stored/detected locale is applied on mount, matching the app's other
  // client-only state (session, theme).
  const [locale, setLocaleState] = useState<Locale>("en");

  useEffect(() => {
    const stored = localStorage.getItem(LOCALE_KEY) as Locale | null;
    setLocaleState(stored && (LOCALES as readonly string[]).includes(stored) ? stored : detectLocale());
  }, []);

  useEffect(() => {
    document.documentElement.lang = locale;
    document.documentElement.dir = RTL_LOCALES.includes(locale) ? "rtl" : "ltr";
  }, [locale]);

  const setLocale = (l: Locale) => {
    localStorage.setItem(LOCALE_KEY, l);
    setLocaleState(l);
  };

  const t = (key: string, params?: Record<string, string | number>) => {
    let s = DICTS[locale][key] ?? en[key] ?? key;
    if (params) {
      for (const [k, v] of Object.entries(params)) s = s.replaceAll(`{${k}}`, String(v));
    }
    return s;
  };

  const dir = RTL_LOCALES.includes(locale) ? "rtl" : "ltr";

  return <I18nContext.Provider value={{ locale, dir, setLocale, t }}>{children}</I18nContext.Provider>;
}

export function useI18n(): I18n {
  const ctx = useContext(I18nContext);
  if (!ctx) throw new Error("useI18n must be used within I18nProvider");
  return ctx;
}
