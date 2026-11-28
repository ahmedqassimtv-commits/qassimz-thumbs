import { useEffect, useState } from "react";
import { AnimatePresence, motion } from "motion/react";
import { api } from "../lib/api";
import { getCopy, type Lang, type Theme } from "../lib/i18n";

declare global {
  interface Window {
    __qzSplashExit?: () => void;
  }
}

export function SplashApp() {
  const initialTheme: Theme =
    document.documentElement.dataset.theme === "light" ? "light" : "dark";
  const initialLang: Lang =
    document.documentElement.lang === "en" ? "en" : "ar";

  const [theme] = useState<Theme>(initialTheme);
  const [lang] = useState<Lang>(initialLang);
  const [stageIdx, setStageIdx] = useState(0);
  const [progress, setProgress] = useState(24);
  const [exiting, setExiting] = useState(false);

  const copy = getCopy(lang);
  const isDark = theme === "dark";

  useEffect(() => {
    window.__qzSplashExit = () => setExiting(true);

    const raf = requestAnimationFrame(() => {
      api.splashReady().catch(() => {});
    });

    const t1 = window.setTimeout(() => {
      setStageIdx(1);
      setProgress(64);
    }, 520);

    const t2 = window.setTimeout(() => {
      setStageIdx(2);
      setProgress(92);
    }, 1050);

    return () => {
      cancelAnimationFrame(raf);
      window.clearTimeout(t1);
      window.clearTimeout(t2);
    };
  }, []);

  const surfaceBg = isDark ? "#111215" : "#F7F6F2";
  const borderCol = isDark ? "rgba(255,255,255,0.08)" : "rgba(24,24,27,0.10)";
  const artBg = isDark ? "#0B0B0D" : "#ECEAE2";
  const cardBg = isDark ? "#17171C" : "#FFFFFF";
  const cardBorder = isDark ? "rgba(255,255,255,0.09)" : "rgba(24,24,27,0.10)";

  return (
    <motion.div
      dir="ltr"
      initial={{ opacity: 0, scale: 0.985 }}
      animate={{ opacity: exiting ? 0 : 1, scale: exiting ? 0.985 : 1 }}
      transition={{ duration: 0.2, ease: "easeOut" }}
      className="relative flex overflow-hidden select-none"
      style={{
        width: 660,
        height: 400,
        borderRadius: 20,
        background: surfaceBg,
        border: `1px solid ${borderCol}`,
      }}
    >
      {/* Left Info Half (50%) — padding 32px */}
      <div
        className="w-1/2 h-full flex flex-col justify-between"
        style={{ padding: 32 }}
      >
        {/* Top: Th 44x44 + 2-line Geist Sans Bold title */}
        <div>
          <img
            src="./logo.svg"
            alt="QASSIMZ Thumbs"
            width={44}
            height={44}
            className="rounded-[10px] mb-5"
            draggable={false}
          />
          <div
            style={{
              fontFamily: "var(--font-latin)",
              fontWeight: 700,
              fontSize: 30,
              lineHeight: 1.06,
              letterSpacing: "-0.025em",
              color: isDark ? "#F4F4F5" : "#18181B",
            }}
          >
            <div>QASSIMZ</div>
            <div style={{ color: "#F59E0B" }}>Thumbs</div>
          </div>
        </div>

        {/* Middle: Slide+Fade stage copy + 2px amber progress bar */}
        <div className="flex flex-col gap-3">
          <div
            className="h-5 overflow-hidden relative"
            dir={lang === "ar" ? "rtl" : "ltr"}
          >
            <AnimatePresence mode="wait">
              <motion.p
                key={stageIdx}
                initial={{ opacity: 0, y: 6 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -6 }}
                transition={{ duration: 0.22, ease: "easeOut" }}
                className="m-0 text-[12.5px] font-medium truncate"
                style={{
                  color: isDark ? "#A1A1AA" : "#52525B",
                  fontFamily:
                    lang === "ar" ? "var(--font-arabic)" : "var(--font-latin)",
                }}
              >
                {copy.splashStages[stageIdx]}
              </motion.p>
            </AnimatePresence>
          </div>

          <div
            className="w-full overflow-hidden rounded-full"
            style={{
              height: 2,
              background: isDark
                ? "rgba(255,255,255,0.08)"
                : "rgba(24,24,27,0.10)",
            }}
          >
            <motion.div
              className="h-full rounded-full"
              style={{ background: "#F59E0B" }}
              initial={{ width: "18%" }}
              animate={{ width: `${progress}%` }}
              transition={{ duration: 0.48, ease: "easeOut" }}
            />
          </div>
        </div>

        {/* Bottom: Copyright + Version + Qh Suite Signature */}
        <div className="flex flex-col gap-2.5">
          <p
            className="m-0 text-[10.5px] leading-tight"
            style={{
              color: isDark ? "#71717A" : "#71717A",
              fontFamily: "var(--font-latin)",
            }}
          >
            {copy.splashCopyright}
          </p>

          <div
            className="flex items-center gap-2 pt-2"
            dir={lang === "ar" ? "rtl" : "ltr"}
            style={{
              borderTop: `1px solid ${borderCol}`,
            }}
          >
            <img
              src="./suite-logo.svg"
              alt="QASSIMZ Hub"
              width={18}
              height={18}
              className="rounded-[4px] shrink-0"
              draggable={false}
            />
            <span
              className="text-[12px] font-medium"
              style={{
                color: isDark ? "#D4D4D8" : "#3F3F46",
                fontFamily:
                  lang === "ar" ? "var(--font-arabic)" : "var(--font-latin)",
              }}
            >
              {copy.splashSuite}
            </span>
          </div>
        </div>
      </div>

      {/* Right Art Half: 302x372px, 14px margin, 14px radius — Darkroom Contact Sheet */}
      <div
        className="relative overflow-hidden shrink-0 checkerboard"
        style={{
          width: 302,
          height: 372,
          margin: 14,
          borderRadius: 14,
          backgroundColor: artBg,
          border: `1px solid ${borderCol}`,
        }}
      >
        {/* Warm Darkroom Amber Glow (pure radial gradient, no CSS blur filter) */}
        <div
          className="absolute inset-0 pointer-events-none"
          style={{
            background: isDark
              ? "radial-gradient(circle at 65% 32%, rgba(245,158,11,0.22) 0%, rgba(245,158,11,0.05) 45%, rgba(11,11,13,0.82) 100%)"
              : "radial-gradient(circle at 65% 32%, rgba(245,158,11,0.24) 0%, rgba(245,158,11,0.06) 45%, rgba(236,234,226,0.68) 100%)",
          }}
        />

        {/* Subtle contact-sheet grid hairline */}
        <svg
          className="absolute inset-0 w-full h-full pointer-events-none"
          viewBox="0 0 302 372"
          fill="none"
        >
          <path
            d="M0 93H302M0 186H302M0 279H302M100 0V372M201 0V372"
            stroke={isDark ? "rgba(255,255,255,0.05)" : "rgba(24,24,27,0.06)"}
            strokeWidth="1"
          />
          <text
            x="16"
            y="24"
            fill={isDark ? "rgba(245,158,11,0.75)" : "rgba(180,83,9,0.85)"}
            fontSize="9.5"
            fontFamily="Geist Sans, sans-serif"
            fontWeight="600"
            letterSpacing="1.6"
          >
            CONTACT SHEET · 64-BIT
          </text>
          <text
            x="16"
            y="356"
            fill={isDark ? "rgba(255,255,255,0.42)" : "rgba(24,24,27,0.48)"}
            fontSize="9"
            fontFamily="Geist Sans, sans-serif"
            fontWeight="500"
            letterSpacing="1.2"
          >
            IThumbnailProvider · HKCU
          </text>
        </svg>

        {/* Overlapping Geometric Thumbnail Cards — GPU-only Ambient Float */}
        <ContactCard
          className="qz-float-a"
          style={{ top: 44, left: 18, width: 130, height: 108 }}
          tag="PSD"
          sub="3840 × 2160"
          accent="#38BDF8"
          cardBg={cardBg}
          cardBorder={cardBorder}
          isDark={isDark}
        />

        <ContactCard
          className="qz-float-b"
          style={{ top: 76, right: 16, width: 130, height: 108 }}
          tag="AI"
          sub="VECTOR · PDF"
          accent="#F59E0B"
          cardBg={cardBg}
          cardBorder={cardBorder}
          isDark={isDark}
        />

        <ContactCard
          className="qz-float-c"
          style={{ bottom: 68, left: 18, width: 132, height: 106 }}
          tag="MOGRT"
          sub="60 FPS · ALPHA"
          accent="#A78BFA"
          cardBg={cardBg}
          cardBorder={cardBorder}
          isDark={isDark}
        />

        <ContactCard
          className="qz-float-d"
          style={{ bottom: 38, right: 16, width: 128, height: 104 }}
          tag="RAW"
          sub="CR3 · DNG"
          accent="#10B981"
          cardBg={cardBg}
          cardBorder={cardBorder}
          isDark={isDark}
        />
      </div>
    </motion.div>
  );
}

function ContactCard({
  className,
  style,
  tag,
  sub,
  accent,
  cardBg,
  cardBorder,
  isDark,
}: {
  className: string;
  style: React.CSSProperties;
  tag: string;
  sub: string;
  accent: string;
  cardBg: string;
  cardBorder: string;
  isDark: boolean;
}) {
  return (
    <div
      className={`absolute rounded-xl p-2.5 flex flex-col justify-between ${className}`}
      style={{
        ...style,
        background: cardBg,
        border: `1px solid ${cardBorder}`,
      }}
    >
      <div
        className="w-full flex-1 rounded-lg mb-2 flex items-center justify-center relative overflow-hidden"
        style={{
          background: isDark
            ? `linear-gradient(135deg, ${accent}24 0%, rgba(255,255,255,0.03) 100%)`
            : `linear-gradient(135deg, ${accent}22 0%, rgba(24,24,27,0.03) 100%)`,
          border: `1px solid ${accent}38`,
        }}
      >
        <span
          style={{
            fontFamily: "var(--font-latin)",
            fontWeight: 700,
            fontSize: 16,
            color: accent,
            letterSpacing: "0.04em",
          }}
        >
          {tag}
        </span>
      </div>
      <div className="flex items-center justify-between">
        <span
          style={{
            fontFamily: "var(--font-latin)",
            fontSize: 9.5,
            fontWeight: 600,
            color: isDark ? "#A1A1AA" : "#52525B",
          }}
        >
          {sub}
        </span>
        <span
          className="w-1.5 h-1.5 rounded-full"
          style={{ background: accent }}
        />
      </div>
    </div>
  );
}
