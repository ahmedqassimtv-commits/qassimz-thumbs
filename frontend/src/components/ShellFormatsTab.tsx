import {
  CheckCircle2,
  AlertTriangle,
  RefreshCw,
  CheckCheck,
  ShieldCheck,
  HardDrive,
} from "lucide-react";
import { motion } from "motion/react";
import type { Copy, Lang } from "../lib/i18n";
import type { FormatItem, FormatsStatusResponse } from "../lib/api";

interface ShellFormatsTabProps {
  status: FormatsStatusResponse | null;
  loading: boolean;
  busyId: string | null;
  rebuilding: boolean;
  notice: { kind: "ok" | "error"; text: string } | null;
  onToggleFormat: (id: string, enabled: boolean) => void;
  onEnableAll: (enabled: boolean) => void;
  onRebuildCache: () => void;
  lang: Lang;
  copy: Copy;
}

const GROUPS: { key: FormatItem["group"]; titleKey: "groupDesign" | "groupEditing" | "groupRawModern" }[] = [
  { key: "design", titleKey: "groupDesign" },
  { key: "editing", titleKey: "groupEditing" },
  { key: "raw_modern", titleKey: "groupRawModern" },
];

export function ShellFormatsTab({
  status,
  loading,
  busyId,
  rebuilding,
  notice,
  onToggleFormat,
  onEnableAll,
  onRebuildCache,
  lang,
  copy,
}: ShellFormatsTabProps) {
  const formats = status?.formats ?? [];
  const linked = Boolean(status?.system_linked);
  const allEnabled =
    formats.length > 0 && formats.every((item) => item.enabled);

  return (
    <div className="h-full overflow-y-auto qz-scroll px-6 py-5">
      <div className="max-w-[980px] mx-auto flex flex-col gap-5">
        {/* Top Status Hero Card */}
        <motion.section
          initial={{ opacity: 0, y: 8 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.24, ease: [0.22, 1, 0.36, 1] }}
          className="rounded-xl p-5 flex flex-col gap-4"
          style={{
            background: "var(--surface)",
            border: "1px solid var(--hairline)",
          }}
        >
          <div className="flex flex-wrap items-center justify-between gap-4">
            <div className="flex items-start gap-3.5 min-w-0">
              <div
                className="w-10 h-10 rounded-lg flex items-center justify-center shrink-0"
                style={{
                  background: linked ? "var(--ok-soft)" : "var(--accent-soft)",
                  color: linked ? "var(--ok)" : "var(--accent)",
                  border: `1px solid ${linked ? "rgba(16,185,129,0.28)" : "var(--accent-border)"}`,
                }}
              >
                {linked ? <CheckCircle2 size={20} /> : <AlertTriangle size={20} />}
              </div>
              <div className="min-w-0">
                <div className="flex items-center gap-2.5 flex-wrap">
                  <h1
                    className="text-[16px] font-bold leading-snug"
                    style={{ color: "var(--ink)" }}
                  >
                    {linked ? copy.statusLinked : copy.statusNeedsActivation}
                  </h1>
                  <span
                    dir="ltr"
                    className="text-[12px] font-medium px-2 py-0.5 rounded-md"
                    style={{
                      background: "var(--surface-elevated)",
                      color: "var(--ink-secondary)",
                      border: "1px solid var(--hairline)",
                      fontFamily: "var(--font-mono)",
                    }}
                  >
                    {status ? `${status.active_count}/${status.total_count}` : "…"}
                  </span>
                </div>
                <p
                  className="text-[13px] mt-1 leading-relaxed"
                  style={{ color: "var(--ink-secondary)" }}
                >
                  {linked && status
                    ? copy.statusSubtitleLinked(status.active_count, status.total_count)
                    : copy.statusSubtitleNeeds}
                </p>
              </div>
            </div>

            {/* Action buttons */}
            <div className="flex items-center gap-2.5 shrink-0">
              <motion.button
                whileHover={{ y: -1 }}
                whileTap={{ scale: 0.97 }}
                type="button"
                disabled={loading || busyId === "__all__"}
                onClick={() => onEnableAll(!allEnabled)}
                data-testid="btn-enable-all"
                className="h-9 px-4 rounded-lg text-[13px] font-semibold flex items-center gap-2 transition-opacity cursor-pointer disabled:opacity-50"
                style={{
                  background: allEnabled ? "var(--surface-elevated)" : "var(--accent)",
                  color: allEnabled ? "var(--ink)" : "#0C0C0E",
                  border: allEnabled ? "1px solid var(--hairline-strong)" : "1px solid transparent",
                }}
              >
                <CheckCheck size={15} />
                <span>{allEnabled ? copy.disableAll : copy.enableAll}</span>
              </motion.button>

              <motion.button
                whileHover={{ y: -1 }}
                whileTap={{ scale: 0.97 }}
                type="button"
                disabled={rebuilding}
                onClick={onRebuildCache}
                data-testid="btn-rebuild-cache"
                className="h-9 px-3.5 rounded-lg text-[13px] font-medium flex items-center gap-2 transition-colors cursor-pointer disabled:opacity-50"
                style={{
                  background: "var(--surface-elevated)",
                  color: "var(--ink)",
                  border: "1px solid var(--hairline-strong)",
                }}
              >
                <RefreshCw size={14} className={rebuilding ? "animate-spin" : ""} />
                <span>{rebuilding ? copy.rebuilding : copy.rebuildCache}</span>
              </motion.button>
            </div>
          </div>

          {/* Real system telemetry strip */}
          <div
            className="pt-3 flex flex-wrap items-center gap-4 text-[12.5px]"
            style={{
              borderTop: "1px solid var(--hairline)",
              color: "var(--ink-secondary)",
            }}
          >
            <span className="flex items-center gap-1.5">
              <ShieldCheck size={14} style={{ color: "var(--accent)" }} />
              <span>{copy.registryRootLabel}:</span>
              <code
                dir="ltr"
                className="text-[12px] px-1.5 py-0.5 rounded"
                style={{
                  background: "var(--surface-elevated)",
                  color: "var(--ink)",
                  fontFamily: "var(--font-mono)",
                }}
              >
                {status?.registry_root ?? "HKCU\\Software\\Classes"}
              </code>
            </span>

            <span className="flex items-center gap-1.5">
              <HardDrive
                size={14}
                style={{ color: status?.dll_found ? "var(--ok)" : "var(--accent)" }}
              />
              <span>
                {status?.dll_found ? copy.dllFoundLabel : copy.dllMissingLabel}
              </span>
            </span>
          </div>
        </motion.section>

        {/* Clear Error or Success Feedback Banner (No Silent Failure) */}
        {notice && (
          <motion.div
            initial={{ opacity: 0, y: -4 }}
            animate={{ opacity: 1, y: 0 }}
            role="status"
            data-testid="formats-notice"
            className="rounded-lg px-4 py-3 text-[13px] font-medium flex items-center gap-2.5"
            style={{
              background: notice.kind === "ok" ? "var(--ok-soft)" : "var(--danger-soft)",
              color: notice.kind === "ok" ? "var(--ok)" : "var(--danger)",
              border: `1px solid ${notice.kind === "ok" ? "rgba(16,185,129,0.28)" : "rgba(239,68,68,0.28)"}`,
            }}
          >
            {notice.kind === "ok" ? <CheckCircle2 size={16} /> : <AlertTriangle size={16} />}
            <span>{notice.text}</span>
          </motion.div>
        )}

        {/* Grouped Format Cards */}
        {GROUPS.map((group) => {
          const items = formats.filter((f) => f.group === group.key);
          if (items.length === 0) return null;

          return (
            <section key={group.key} className="flex flex-col gap-2.5">
              <div className="flex items-center justify-between px-1">
                <h2
                  className="text-[13px] font-semibold"
                  style={{ color: "var(--ink-secondary)" }}
                >
                  {copy[group.titleKey]}
                </h2>
                <span
                  dir="ltr"
                  className="text-[12px]"
                  style={{ color: "var(--ink-muted)", fontFamily: "var(--font-mono)" }}
                >
                  {items.filter((i) => i.enabled).length}/{items.length}
                </span>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                {items.map((item, idx) => {
                  const isBusy = busyId === item.id || busyId === "__all__";
                  return (
                    <motion.div
                      key={item.id}
                      initial={{ opacity: 0, y: 8 }}
                      animate={{ opacity: 1, y: 0 }}
                      transition={{ duration: 0.22, delay: idx * 0.03, ease: [0.22, 1, 0.36, 1] }}
                      whileHover={{ y: -1.5 }}
                      data-testid={`format-card-${item.id}`}
                      className="rounded-xl p-4 flex items-center justify-between gap-4 transition-colors"
                      style={{
                        background: "var(--surface)",
                        border: `1px solid ${item.enabled ? "var(--accent-border)" : "var(--hairline)"}`,
                      }}
                    >
                      <div className="min-w-0 flex-1">
                        <div className="flex items-center gap-2.5 flex-wrap">
                          <span
                            className="text-[14.5px] font-bold leading-none"
                            style={{ color: "var(--ink)", fontFamily: "var(--font-latin)" }}
                          >
                            {item.label}
                          </span>
                          <span
                            className="text-[12px] font-medium px-2 py-0.5 rounded-full"
                            style={{
                              background: item.enabled ? "var(--ok-soft)" : "var(--surface-elevated)",
                              color: item.enabled ? "var(--ok)" : "var(--ink-muted)",
                            }}
                          >
                            {item.enabled ? copy.badgeActive : copy.badgeOff}
                          </span>
                        </div>

                        <p
                          className="text-[12.5px] mt-1.5 leading-snug"
                          style={{ color: "var(--ink-secondary)" }}
                        >
                          {lang === "ar" ? item.engine_hint_ar : item.engine_hint_en}
                        </p>

                        <div className="flex flex-wrap gap-1.5 mt-2.5">
                          {item.extensions.map((ext) => (
                            <span
                              key={ext}
                              dir="ltr"
                              className="text-[11.5px] px-1.5 py-0.5 rounded font-medium"
                              style={{
                                background: "var(--surface-elevated)",
                                color: "var(--ink-secondary)",
                                border: "1px solid var(--hairline)",
                                fontFamily: "var(--font-latin)",
                              }}
                            >
                              {ext}
                            </span>
                          ))}
                        </div>
                      </div>

                      {/* Real HKCU Toggle Switch */}
                      <button
                        type="button"
                        role="switch"
                        aria-checked={item.enabled}
                        aria-label={`Toggle ${item.label}`}
                        disabled={isBusy}
                        data-testid={`toggle-${item.id}`}
                        onClick={() => onToggleFormat(item.id, !item.enabled)}
                        className="relative w-11 h-6 rounded-full shrink-0 transition-colors cursor-pointer disabled:opacity-50"
                        style={{
                          background: item.enabled ? "var(--accent)" : "var(--surface-elevated)",
                          border: `1px solid ${item.enabled ? "transparent" : "var(--hairline-strong)"}`,
                        }}
                      >
                        <motion.span
                          layout
                          transition={{ type: "spring", stiffness: 520, damping: 32 }}
                          className="block w-4 h-4 rounded-full"
                          style={{
                            background: item.enabled ? "#0C0C0E" : "var(--ink-secondary)",
                            transform:
                              lang === "ar"
                                ? item.enabled
                                  ? "translateX(-22px)"
                                  : "translateX(-4px)"
                                : item.enabled
                                  ? "translateX(22px)"
                                  : "translateX(4px)",
                          }}
                        />
                      </button>
                    </motion.div>
                  );
                })}
              </div>
            </section>
          );
        })}
      </div>
    </div>
  );
}
