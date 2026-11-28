import { useCallback, useEffect, useState } from "react";
import { AnimatePresence, MotionConfig, motion } from "motion/react";
import { Titlebar, type TabId } from "./components/Titlebar";
import { ShellFormatsTab } from "./components/ShellFormatsTab";
import { QuickViewerTab } from "./components/QuickViewerTab";
import { api, type FormatsStatusResponse } from "./lib/api";
import { getCopy, type Lang, type Theme } from "./lib/i18n";

function readInitialTheme(): Theme {
  const t = document.documentElement.dataset.theme;
  return t === "light" ? "light" : "dark";
}

function readInitialLang(): Lang {
  return document.documentElement.lang === "en" ? "en" : "ar";
}

export function App() {
  const [activeTab, setActiveTab] = useState<TabId>("formats");
  const [theme, setTheme] = useState<Theme>(readInitialTheme);
  const [lang, setLang] = useState<Lang>(readInitialLang);
  const [status, setStatus] = useState<FormatsStatusResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [rebuilding, setRebuilding] = useState(false);
  const [notice, setNotice] = useState<{ kind: "ok" | "error"; text: string } | null>(null);

  const copy = getCopy(lang);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    document.documentElement.style.backgroundColor =
      theme === "light" ? "#F7F6F2" : "#0C0C0E";
    try {
      localStorage.setItem("qz_thumbs_theme", theme);
    } catch {
      // ignore
    }
  }, [theme]);

  useEffect(() => {
    document.documentElement.lang = lang;
    document.documentElement.dir = lang === "ar" ? "rtl" : "ltr";
    try {
      localStorage.setItem("qz_thumbs_lang", lang);
    } catch {
      // ignore
    }
  }, [lang]);

  const refreshStatus = useCallback(async () => {
    setLoading(true);
    try {
      const data = await api.getFormatsStatus();
      setStatus(data);
    } catch (err) {
      setNotice({
        kind: "error",
        text: err instanceof Error ? err.message : "Failed to read registry status",
      });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    let active = true;
    (async () => {
      try {
        const saved = await api.getSettings();
        if (active && saved.ok) {
          const urlParams = new URLSearchParams(window.location.search);
          if (!urlParams.get("theme")) setTheme(saved.theme);
          if (!urlParams.get("lang")) setLang(saved.lang);
        }
      } catch {
        // use defaults
      }
      await refreshStatus();
      if (active) {
        api.uiReady().catch(() => {});
      }
    })();
    return () => {
      active = false;
    };
  }, [refreshStatus]);

  async function handleToggleFormat(id: string, enabled: boolean) {
    setBusyId(id);
    setNotice(null);
    try {
      const res = await api.toggleFormat(id, enabled);
      if (!res.ok) {
        setNotice({
          kind: "error",
          text: lang === "ar" ? res.message_ar || "فشل تحديث الصيغة." : res.message_en || "Failed to update format.",
        });
      } else {
        setStatus(res);
      }
    } catch (err) {
      setNotice({
        kind: "error",
        text: err instanceof Error ? err.message : "Registry update error",
      });
    } finally {
      setBusyId(null);
    }
  }

  async function handleEnableAll(enabled: boolean) {
    setBusyId("__all__");
    setNotice(null);
    try {
      const res = await api.enableAllFormats(enabled);
      if (!res.ok) {
        setNotice({
          kind: "error",
          text: lang === "ar" ? res.message_ar || "فشل تحديث الصيغ." : res.message_en || "Failed to update formats.",
        });
      } else {
        setStatus(res);
      }
    } catch (err) {
      setNotice({
        kind: "error",
        text: err instanceof Error ? err.message : "Registry update error",
      });
    } finally {
      setBusyId(null);
    }
  }

  async function handleRebuildCache() {
    setRebuilding(true);
    setNotice(null);
    try {
      const res = await api.rebuildThumbnailCache();
      setNotice({
        kind: res.ok ? "ok" : "error",
        text: lang === "ar" ? res.message_ar : res.message_en,
      });
    } catch (err) {
      setNotice({
        kind: "error",
        text: err instanceof Error ? err.message : "Cache rebuild failed",
      });
    } finally {
      setRebuilding(false);
    }
  }

  function handleToggleLang() {
    const next: Lang = lang === "ar" ? "en" : "ar";
    setLang(next);
    api.saveSettings({ lang: next }).catch(() => {});
  }

  function handleToggleTheme() {
    const next: Theme = theme === "dark" ? "light" : "dark";
    setTheme(next);
    api.saveSettings({ theme: next }).catch(() => {});
  }

  return (
    <MotionConfig reducedMotion="user">
      <div
        className="h-screen w-screen flex flex-col overflow-hidden"
        style={{ background: "var(--bg)", color: "var(--ink)" }}
      >
        <Titlebar
          activeTab={activeTab}
          onSelectTab={setActiveTab}
          lang={lang}
          onToggleLang={handleToggleLang}
          theme={theme}
          onToggleTheme={handleToggleTheme}
          copy={copy}
        />

        <main className="flex-1 min-h-0 overflow-hidden relative">
          <AnimatePresence mode="wait">
            {activeTab === "formats" ? (
              <motion.div
                key="formats"
                initial={{ opacity: 0, y: 8 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -6 }}
                transition={{ duration: 0.2, ease: [0.22, 1, 0.36, 1] }}
                className="h-full"
              >
                <ShellFormatsTab
                  status={status}
                  loading={loading}
                  busyId={busyId}
                  rebuilding={rebuilding}
                  notice={notice}
                  onToggleFormat={handleToggleFormat}
                  onEnableAll={handleEnableAll}
                  onRebuildCache={handleRebuildCache}
                  lang={lang}
                  copy={copy}
                />
              </motion.div>
            ) : (
              <motion.div
                key="viewer"
                initial={{ opacity: 0, y: 8 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -6 }}
                transition={{ duration: 0.2, ease: [0.22, 1, 0.36, 1] }}
                className="h-full"
              >
                <QuickViewerTab lang={lang} copy={copy} />
              </motion.div>
            )}
          </AnimatePresence>
        </main>
      </div>
    </MotionConfig>
  );
}
