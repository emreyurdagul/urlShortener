"use client";

import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { ApiError } from "./api";

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

  "err.url_invalid": "Enter a valid http(s) URL.",
  "err.domain_invalid": "That domain isn't available.",
  "err.quota_exceeded": "Your {plan} plan allows {quota} links. Upgrade for more.",
  "err.vanity_forbidden": "Custom codes are a Pro feature.",
  "err.vanity_invalid": "Use 1-32 letters, digits, '-' or '_'.",
  "err.code_taken": "That code is already taken.",
  "err.code_length_range": "Length must be {min}-{max}.",
  "err.code_length_locked": "{plan} starts at {minLength} chars. Upgrade for shorter.",
  "err.code_alloc_failed": "Couldn't generate a code. Try again.",
  "err.email_invalid": "Enter a valid email.",
  "err.password_short": "Password needs at least {min} characters.",
  "err.email_taken": "That email is already registered.",
  "err.bad_credentials": "Wrong email or password.",
  "err.plan_invalid": "Pick Plus or Pro.",
  "err.unauthenticated": "Please sign in.",
  "err.user_not_found": "Account not found.",

  "dash.title": "Dashboard",
  "dash.subtitle": "Your links and live gateway telemetry.",
  "dash.telemetry": "Gateway telemetry",
  "dash.streaming": "streaming",
  "dash.reconnecting": "reconnecting…",
  "dash.reqPerSec": "Requests / sec",
  "dash.total": "{n} total",
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

  "nav.upgrade": "Upgrade",

  "create.hint.free": "Codes start at 5 chars on Free.",
  "create.hint.plus": "Plus: codes from 3. Pro unlocks 1-2 + custom.",
  "create.hint.pro": "Pro: codes down to 1 char, plus custom codes.",
  "create.upgradeCta": "Upgrade →",
  "create.customCode": "Custom code · Pro",
  "create.customPlaceholder": "my-link",
  "create.customHint": "Leave empty for a random code.",

  "tier.free.name": "Free",
  "tier.plus.name": "Plus",
  "tier.pro.name": "Pro",

  "upgrade.title": "Upgrade your plan",
  "upgrade.subtitle": "Shorter codes, unlimited links, custom URLs.",
  "upgrade.demo": "Demo — no real payment is taken.",
  "upgrade.free": "Free",
  "upgrade.perMonth": "/mo",
  "upgrade.current": "current",
  "upgrade.cta": "Upgrade",
  "upgrade.ctaCurrent": "Current plan",
  "upgrade.success": "You're on {plan} now.",
  "upgrade.signInFirst": "Sign in to change your plan.",
  "upgrade.feat.codes5": "Codes from 5 characters",
  "upgrade.feat.codes3": "Codes from 3 characters",
  "upgrade.feat.codes1": "Codes from 1 character",
  "upgrade.feat.links50": "Up to 50 links",
  "upgrade.feat.linksUnlim": "Unlimited links",
  "upgrade.feat.vanity": "Custom (vanity) codes",
  "upgrade.feat.analytics": "Click analytics + QR",

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

  "err.url_invalid": "Geçerli bir http(s) URL gir.",
  "err.domain_invalid": "Bu alan adı kullanılamıyor.",
  "err.quota_exceeded": "{plan} planında {quota} link hakkın var. Yükselt.",
  "err.vanity_forbidden": "Özel kod Pro özelliğidir.",
  "err.vanity_invalid": "1-32 harf, rakam, '-' veya '_' kullan.",
  "err.code_taken": "Bu kod zaten alınmış.",
  "err.code_length_range": "Uzunluk {min}-{max} olmalı.",
  "err.code_length_locked": "{plan} planı {minLength} karakterden başlar. Yükselt.",
  "err.code_alloc_failed": "Kod üretilemedi, tekrar dene.",
  "err.email_invalid": "Geçerli bir e-posta gir.",
  "err.password_short": "Parola en az {min} karakter olmalı.",
  "err.email_taken": "Bu e-posta zaten kayıtlı.",
  "err.bad_credentials": "E-posta veya parola yanlış.",
  "err.plan_invalid": "Plus ya da Pro seç.",
  "err.unauthenticated": "Lütfen giriş yap.",
  "err.user_not_found": "Hesap bulunamadı.",

  "dash.title": "Panel",
  "dash.subtitle": "Linklerin ve canlı gateway telemetrisi.",
  "dash.telemetry": "Gateway telemetrisi",
  "dash.streaming": "akıyor",
  "dash.reconnecting": "yeniden bağlanıyor…",
  "dash.reqPerSec": "İstek / sn",
  "dash.total": "{n} toplam",
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

  "nav.upgrade": "Yükselt",

  "create.hint.free": "Free'de kodlar 5 karakterden başlar.",
  "create.hint.plus": "Plus: 3'ten başlar. Pro 1-2 + özel kodu açar.",
  "create.hint.pro": "Pro: 1 karaktere kadar, üstelik özel kod.",
  "create.upgradeCta": "Yükselt →",
  "create.customCode": "Özel kod · Pro",
  "create.customPlaceholder": "benim-linkim",
  "create.customHint": "Boş bırakırsan rastgele kod üretilir.",

  "tier.free.name": "Free",
  "tier.plus.name": "Plus",
  "tier.pro.name": "Pro",

  "upgrade.title": "Planını yükselt",
  "upgrade.subtitle": "Daha kısa kodlar, limitsiz link, özel URL'ler.",
  "upgrade.demo": "Demo — gerçek ödeme alınmaz.",
  "upgrade.free": "Ücretsiz",
  "upgrade.perMonth": "/ay",
  "upgrade.current": "mevcut",
  "upgrade.cta": "Yükselt",
  "upgrade.ctaCurrent": "Mevcut plan",
  "upgrade.success": "Artık {plan} kullanıyorsun.",
  "upgrade.signInFirst": "Plan değiştirmek için giriş yap.",
  "upgrade.feat.codes5": "5 karakterden itibaren kod",
  "upgrade.feat.codes3": "3 karakterden itibaren kod",
  "upgrade.feat.codes1": "1 karakterden itibaren kod",
  "upgrade.feat.links50": "50 linke kadar",
  "upgrade.feat.linksUnlim": "Limitsiz link",
  "upgrade.feat.vanity": "Özel (vanity) kodlar",
  "upgrade.feat.analytics": "Tıklama analizi + QR",

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

  "err.url_invalid": "Gib eine gültige http(s)-URL ein.",
  "err.domain_invalid": "Diese Domain ist nicht verfügbar.",
  "err.quota_exceeded": "Dein {plan}-Tarif erlaubt {quota} Links. Upgrade für mehr.",
  "err.vanity_forbidden": "Eigene Codes sind ein Pro-Feature.",
  "err.vanity_invalid": "1-32 Buchstaben, Ziffern, '-' oder '_'.",
  "err.code_taken": "Dieser Code ist bereits vergeben.",
  "err.code_length_range": "Länge muss {min}-{max} sein.",
  "err.code_length_locked": "{plan} beginnt bei {minLength} Zeichen. Upgrade für kürzere.",
  "err.code_alloc_failed": "Code konnte nicht erzeugt werden. Versuch es erneut.",
  "err.email_invalid": "Gib eine gültige E-Mail ein.",
  "err.password_short": "Passwort braucht mindestens {min} Zeichen.",
  "err.email_taken": "Diese E-Mail ist bereits registriert.",
  "err.bad_credentials": "E-Mail oder Passwort falsch.",
  "err.plan_invalid": "Wähle Plus oder Pro.",
  "err.unauthenticated": "Bitte melde dich an.",
  "err.user_not_found": "Konto nicht gefunden.",

  "dash.title": "Dashboard",
  "dash.subtitle": "Deine Links und Live-Telemetrie des Gateways.",
  "dash.telemetry": "Gateway-Telemetrie",
  "dash.streaming": "streamt",
  "dash.reconnecting": "verbindet erneut…",
  "dash.reqPerSec": "Anfragen / Sek.",
  "dash.total": "{n} gesamt",
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

  "nav.upgrade": "Upgrade",

  "create.hint.free": "Codes ab 5 Zeichen im Free-Tarif.",
  "create.hint.plus": "Plus: ab 3 Zeichen. Pro schaltet 1-2 + eigene frei.",
  "create.hint.pro": "Pro: bis 1 Zeichen, plus eigene Codes.",
  "create.upgradeCta": "Upgrade →",
  "create.customCode": "Eigener Code · Pro",
  "create.customPlaceholder": "mein-link",
  "create.customHint": "Leer lassen für einen zufälligen Code.",

  "tier.free.name": "Free",
  "tier.plus.name": "Plus",
  "tier.pro.name": "Pro",

  "upgrade.title": "Tarif upgraden",
  "upgrade.subtitle": "Kürzere Codes, unbegrenzte Links, eigene URLs.",
  "upgrade.demo": "Demo — es wird keine echte Zahlung erhoben.",
  "upgrade.free": "Kostenlos",
  "upgrade.perMonth": "/Mon.",
  "upgrade.current": "aktuell",
  "upgrade.cta": "Upgraden",
  "upgrade.ctaCurrent": "Aktueller Tarif",
  "upgrade.success": "Du nutzt jetzt {plan}.",
  "upgrade.signInFirst": "Melde dich an, um deinen Tarif zu ändern.",
  "upgrade.feat.codes5": "Codes ab 5 Zeichen",
  "upgrade.feat.codes3": "Codes ab 3 Zeichen",
  "upgrade.feat.codes1": "Codes ab 1 Zeichen",
  "upgrade.feat.links50": "Bis zu 50 Links",
  "upgrade.feat.linksUnlim": "Unbegrenzte Links",
  "upgrade.feat.vanity": "Eigene (Vanity-)Codes",
  "upgrade.feat.analytics": "Klick-Analyse + QR",

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

  "err.url_invalid": "أدخل رابط http(s) صالحًا.",
  "err.domain_invalid": "هذا النطاق غير متاح.",
  "err.quota_exceeded": "خطة {plan} تسمح بـ {quota} رابطًا. رقِّ خطتك.",
  "err.vanity_forbidden": "الرموز المخصصة ميزة Pro.",
  "err.vanity_invalid": "استخدم 1-32 حرفًا أو رقمًا أو '-' أو '_'.",
  "err.code_taken": "هذا الرمز مأخوذ بالفعل.",
  "err.code_length_range": "يجب أن يكون الطول بين {min} و{max}.",
  "err.code_length_locked": "{plan} يبدأ من {minLength} أحرف. رقِّ للأقصر.",
  "err.code_alloc_failed": "تعذّر توليد رمز. حاول مجددًا.",
  "err.email_invalid": "أدخل بريدًا إلكترونيًا صالحًا.",
  "err.password_short": "كلمة المرور تحتاج {min} أحرف على الأقل.",
  "err.email_taken": "هذا البريد مسجَّل بالفعل.",
  "err.bad_credentials": "البريد الإلكتروني أو كلمة المرور غير صحيحة.",
  "err.plan_invalid": "اختر Plus أو Pro.",
  "err.unauthenticated": "الرجاء تسجيل الدخول.",
  "err.user_not_found": "الحساب غير موجود.",

  "dash.title": "لوحة التحكم",
  "dash.subtitle": "روابطك وقياسات البوابة الحيّة.",
  "dash.telemetry": "قياسات البوابة",
  "dash.streaming": "يبثّ",
  "dash.reconnecting": "إعادة الاتصال…",
  "dash.reqPerSec": "طلبات / ثانية",
  "dash.total": "{n} إجمالي",
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

  "nav.upgrade": "ترقية",

  "create.hint.free": "تبدأ الرموز من 5 أحرف في الخطة المجانية.",
  "create.hint.plus": "Plus: من 3 أحرف. Pro يفتح 1-2 ورموزًا مخصصة.",
  "create.hint.pro": "Pro: حتى حرف واحد، مع رموز مخصصة.",
  "create.upgradeCta": "ترقية ←",
  "create.customCode": "رمز مخصص · Pro",
  "create.customPlaceholder": "رابطي",
  "create.customHint": "اتركه فارغًا للحصول على رمز عشوائي.",

  "tier.free.name": "Free",
  "tier.plus.name": "Plus",
  "tier.pro.name": "Pro",

  "upgrade.title": "ترقية خطتك",
  "upgrade.subtitle": "رموز أقصر، روابط غير محدودة، عناوين مخصصة.",
  "upgrade.demo": "عرض تجريبي — لا يتم تحصيل أي دفع فعلي.",
  "upgrade.free": "مجاني",
  "upgrade.perMonth": "/شهر",
  "upgrade.current": "الحالية",
  "upgrade.cta": "ترقية",
  "upgrade.ctaCurrent": "خطتك الحالية",
  "upgrade.success": "أنت الآن على خطة {plan}.",
  "upgrade.signInFirst": "سجّل الدخول لتغيير خطتك.",
  "upgrade.feat.codes5": "رموز من 5 أحرف",
  "upgrade.feat.codes3": "رموز من 3 أحرف",
  "upgrade.feat.codes1": "رموز من حرف واحد",
  "upgrade.feat.links50": "حتى 50 رابطًا",
  "upgrade.feat.linksUnlim": "روابط غير محدودة",
  "upgrade.feat.vanity": "رموز مخصصة (vanity)",
  "upgrade.feat.analytics": "تحليلات النقر + QR",

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
  // Localizes an API error by its machine `code` (err.<code>), interpolating the
  // error body's params; falls back to the backend's English message.
  tErr: (err: unknown) => string;
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

  const tErr = (err: unknown): string => {
    if (err instanceof ApiError) {
      if (err.code) {
        const key = `err.${err.code}`;
        const s = t(key, err.body as Record<string, string | number>);
        if (s !== key) return s; // known code → localized
      }
      return err.message || t("common.error"); // fall back to the backend's message
    }
    return t("common.error");
  };

  const dir = RTL_LOCALES.includes(locale) ? "rtl" : "ltr";

  return <I18nContext.Provider value={{ locale, dir, setLocale, t, tErr }}>{children}</I18nContext.Provider>;
}

export function useI18n(): I18n {
  const ctx = useContext(I18nContext);
  if (!ctx) throw new Error("useI18n must be used within I18nProvider");
  return ctx;
}
