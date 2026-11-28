import { Layers, Eye, Sun, Moon, Minus, Square, X } from "lucide-react";
import { motion } from "motion/react";
import type { Copy, Lang, Theme } from "../lib/i18n";
import { api } from "../lib/api";
import { QassimzWordmark } from "./QassimzWordmark";

export type TabId = "formats" | "viewer";

interface TitlebarProps {
  activeTab: TabId;
  onSelectTab: (tab: TabId) => void;
  lang: Lang;
  onToggleLang: () => void;
  theme: Theme;
  onToggleTheme: () => void;
  copy: Copy;
}

export function Titlebar({
  activeTab,
  onSelectTab,
  lang,
  onToggleLang,
  theme,
  onToggleTheme,
  copy,
}: TitlebarProps) {
  const tabs: { id: TabId; label: string; icon: typeof Layers }[] = [
    { id: "formats", label: copy.tabFormats, icon: Layers },
    { id: "viewer", label: copy.tabViewer, icon: Eye },
  ];

  return (
    <header
      className="pywebview-drag-region flex items-center justify-between px-4 select-none shrink-0"
      style={{
        height: 48,
        background: "var(--surface)",
        borderBottom: "1px solid var(--hairline)",
      }}
    >
      {/* Brand lockup: Th 26x26 + official QASSIMZ vector wordmark */}
      <div className="flex items-center gap-2.5 min-w-0">
        <img
          src="./logo.svg"
          alt="QASSIMZ Thumbs"
          width={26}
          height={26}
          className="shrink-0 rounded-[6px]"
          draggable={false}
        />
        <span
          className="h-3.5 w-px shrink-0"
          style={{ background: "var(--hairline-strong)" }}
        />
        {lang === "ar" ? (
          <div className="flex items-center gap-2 min-w-0">
            <QassimzWordmark height={22} className="shrink-0" />
            <span
              className="text-[12px] font-semibold leading-none px-2 py-1 rounded-md whitespace-nowrap"
              style={{
                background: "var(--accent-soft)",
                color: "var(--accent)",
                border: "1px solid var(--accent-border)",
              }}
            >
              {copy.appSubAr}
            </span>
          </div>
        ) : (
          <div className="flex items-center gap-2 min-w-0">
            <span
              className="text-[14px] font-bold tracking-tight leading-none whitespace-nowrap"
              style={{ color: "var(--ink)", fontFamily: "var(--font-latin)" }}
            >
              {copy.appBrandEn}
            </span>
            <span
              className="text-[12.5px] font-semibold leading-none px-2 py-1 rounded-md whitespace-nowrap"
              style={{
                background: "var(--accent-soft)",
                color: "var(--accent)",
                border: "1px solid var(--accent-border)",
                fontFamily: "var(--font-latin)",
              }}
            >
              {copy.appSubEn}
            </span>
          </div>
        )}
      </div>

      {/* Centered 2-tab switcher with animated amber underline */}
      <nav
        className="no-drag flex items-center gap-1 h-full"
        aria-label="Primary sections"
      >
        {tabs.map((tab) => {
          const Icon = tab.icon;
          const active = activeTab === tab.id;
          return (
            <button
              key={tab.id}
              type="button"
              onClick={() => onSelectTab(tab.id)}
              data-testid={`tab-${tab.id}`}
              className="relative flex items-center gap-2 px-4 h-full text-[13px] font-medium transition-colors cursor-pointer"
              style={{
                color: active ? "var(--ink)" : "var(--ink-secondary)",
              }}
            >
              <Icon
                size={15}
                strokeWidth={active ? 2.1 : 1.75}
                style={{ color: active ? "var(--accent)" : "var(--ink-muted)" }}
              />
              <span>{tab.label}</span>
              {active && (
                <motion.span
                  layoutId="qz-active-tab-underline"
                  className="absolute bottom-0 inset-x-3 h-[2px] rounded-full"
                  style={{ background: "var(--accent)" }}
                  transition={{ type: "spring", stiffness: 460, damping: 36 }}
                />
              )}
            </button>
          );
        })}
      </nav>

      {/* Right utilities: Language, Theme, Window controls */}
      <div className="no-drag flex items-center gap-1">
        <button
          type="button"
          onClick={onToggleLang}
          data-testid="toggle-lang"
          title={lang === "ar" ? "Switch to English" : "التبديل إلى العربية"}
          className="h-7 px-2.5 rounded-md text-[12px] font-semibold transition-colors cursor-pointer"
          style={{
            color: "var(--ink-secondary)",
            border: "1px solid var(--hairline)",
            background: "transparent",
            fontFamily: "var(--font-latin)",
          }}
        >
          {lang === "ar" ? "EN" : "AR"}
        </button>

        <button
          type="button"
          onClick={onToggleTheme}
          data-testid="toggle-theme"
          title={theme === "dark" ? "Light mode" : "Dark mode"}
          className="h-7 w-7 rounded-md flex items-center justify-center transition-colors cursor-pointer"
          style={{
            color: "var(--ink-secondary)",
            border: "1px solid var(--hairline)",
          }}
        >
          {theme === "dark" ? <Sun size={14} /> : <Moon size={14} />}
        </button>

        <span
          className="mx-1.5 h-3.5 w-px"
          style={{ background: "var(--hairline-strong)" }}
        />

        <button
          type="button"
          onClick={() => api.winMinimize().catch(() => {})}
          aria-label="Minimize"
          className="h-7 w-7 rounded-md flex items-center justify-center transition-colors cursor-pointer hover:opacity-80"
          style={{ color: "var(--ink-secondary)" }}
        >
          <Minus size={14} />
        </button>
        <button
          type="button"
          onClick={() => api.winToggleMaximize().catch(() => {})}
          aria-label="Maximize"
          className="h-7 w-7 rounded-md flex items-center justify-center transition-colors cursor-pointer hover:opacity-80"
          style={{ color: "var(--ink-secondary)" }}
        >
          <Square size={11.5} />
        </button>
        <button
          type="button"
          onClick={() => api.winClose().catch(() => {})}
          aria-label="Close"
          className="h-7 w-7 rounded-md flex items-center justify-center transition-colors cursor-pointer hover:bg-red-500/20 hover:text-red-400"
          style={{ color: "var(--ink-secondary)" }}
        >
          <X size={14} />
        </button>
      </div>
    </header>
  );
}
