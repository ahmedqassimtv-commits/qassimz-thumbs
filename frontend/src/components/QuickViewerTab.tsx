import { useRef, useState } from "react";
import { Upload, Sparkles, Clock, Maximize2, FileImage, AlertTriangle, RotateCcw } from "lucide-react";
import type { Copy, Lang } from "../lib/i18n";
import { api, type PreviewResponse } from "../lib/api";

interface QuickViewerTabProps {
  lang: Lang;
  copy: Copy;
}

function formatBytes(bytes: number): string {
  if (!bytes || bytes <= 0) return "0 B";
  const units = ["B", "KB", "MB", "GB"];
  let value = bytes;
  let i = 0;
  while (value >= 1024 && i < units.length - 1) {
    value /= 1024;
    i++;
  }
  return `${value.toFixed(i === 0 ? 0 : 1)} ${units[i]}`;
}

function fileToBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const result = String(reader.result || "");
      const idx = result.indexOf(",");
      resolve(idx >= 0 ? result.slice(idx + 1) : result);
    };
    reader.onerror = () => reject(new Error("Could not read file"));
    reader.readAsDataURL(file);
  });
}

export function QuickViewerTab({ lang, copy }: QuickViewerTabProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);
  const [extracting, setExtracting] = useState(false);
  const [preview, setPreview] = useState<PreviewResponse | null>(null);
  const [errorText, setErrorText] = useState<string | null>(null);

  async function inspectFile(file: File) {
    setExtracting(true);
    setErrorText(null);
    try {
      const data_b64 = await fileToBase64(file);
      const res = await api.previewFile({
        filename: file.name,
        data_b64,
        file_size: file.size,
      });
      if (!res.ok) {
        setPreview(null);
        setErrorText(lang === "ar" ? res.message_ar || "تعذّر استخراج المعاينة." : res.message_en || "Failed to extract preview.");
      } else {
        setPreview(res);
      }
    } catch (err) {
      setPreview(null);
      setErrorText(err instanceof Error ? err.message : "Extraction failed");
    } finally {
      setExtracting(false);
    }
  }

  function handleDrop(e: React.DragEvent) {
    e.preventDefault();
    setDragging(false);
    const file = e.dataTransfer.files?.[0];
    if (file) {
      void inspectFile(file);
    }
  }

  function handlePick(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (file) {
      void inspectFile(file);
    }
    e.target.value = "";
  }

  return (
    <div className="h-full flex flex-col p-6 gap-4 max-w-[1040px] mx-auto w-full">
      <input
        ref={inputRef}
        type="file"
        className="hidden"
        data-testid="viewer-file-input"
        onChange={handlePick}
      />

      {!preview ? (
        <div
          onDragOver={(e) => {
            e.preventDefault();
            setDragging(true);
          }}
          onDragLeave={() => setDragging(false)}
          onDrop={handleDrop}
          onClick={() => inputRef.current?.click()}
          data-testid="viewer-dropzone"
          className="flex-1 rounded-2xl flex flex-col items-center justify-center p-10 text-center cursor-pointer transition-colors"
          style={{
            background: dragging ? "var(--accent-soft)" : "var(--surface)",
            border: `1.5px dashed ${dragging ? "var(--accent)" : "var(--hairline-strong)"}`,
          }}
        >
          <div
            className="w-14 h-14 rounded-2xl flex items-center justify-center mb-4"
            style={{
              background: "var(--surface-elevated)",
              border: "1px solid var(--hairline-strong)",
              color: "var(--accent)",
            }}
          >
            <Upload size={24} />
          </div>

          <h2
            className="text-[17px] font-bold"
            style={{ color: "var(--ink)" }}
          >
            {extracting ? copy.extracting : copy.dropTitle}
          </h2>

          <p
            className="text-[12.5px] font-medium mt-2 px-3 py-1 rounded-md"
            style={{
              background: "var(--surface-elevated)",
              color: "var(--ink-secondary)",
              fontFamily: "var(--font-latin)",
            }}
          >
            {copy.dropSubtitle}
          </p>

          <p
            className="text-[13px] mt-3 max-w-md leading-relaxed"
            style={{ color: "var(--ink-secondary)" }}
          >
            {copy.dropHint}
          </p>

          <button
            type="button"
            className="mt-5 h-9 px-4 rounded-lg text-[13px] font-semibold cursor-pointer"
            style={{
              background: "var(--accent)",
              color: "#0C0C0E",
            }}
          >
            {copy.dropBrowse}
          </button>

          {errorText && (
            <div
              className="mt-5 px-4 py-2.5 rounded-lg text-[13px] flex items-center gap-2"
              style={{
                background: "var(--danger-soft)",
                color: "var(--danger)",
                border: "1px solid rgba(239,68,68,0.28)",
              }}
            >
              <AlertTriangle size={15} />
              <span>{errorText}</span>
            </div>
          )}
        </div>
      ) : (
        <div
          className="flex-1 min-h-0 flex flex-col rounded-2xl overflow-hidden"
          style={{
            background: "var(--surface)",
            border: "1px solid var(--hairline)",
          }}
        >
          {/* Top file bar */}
          <div
            className="px-4 py-3 flex items-center justify-between gap-3 shrink-0"
            style={{ borderBottom: "1px solid var(--hairline)" }}
          >
            <div className="flex items-center gap-2.5 min-w-0">
              <FileImage size={16} style={{ color: "var(--accent)" }} />
              <span
                className="text-[13.5px] font-semibold truncate"
                style={{ color: "var(--ink)", fontFamily: "var(--font-latin)" }}
              >
                {preview.filename}
              </span>
              <span
                className="text-[12px] font-semibold px-2 py-0.5 rounded"
                style={{
                  background: "var(--accent-soft)",
                  color: "var(--accent)",
                  fontFamily: "var(--font-latin)",
                }}
              >
                {preview.format}
              </span>
            </div>

            <button
              type="button"
              onClick={() => {
                setPreview(null);
                setErrorText(null);
              }}
              data-testid="viewer-clear"
              className="h-8 px-3 rounded-lg text-[12.5px] font-medium flex items-center gap-1.5 cursor-pointer"
              style={{
                background: "var(--surface-elevated)",
                color: "var(--ink)",
                border: "1px solid var(--hairline-strong)",
              }}
            >
              <RotateCcw size={13} />
              <span>{copy.clearPreview}</span>
            </button>
          </div>

          {/* Checkerboard preview stage */}
          <div
            onDragOver={(e) => e.preventDefault()}
            onDrop={handleDrop}
            className="flex-1 min-h-0 checkerboard flex items-center justify-center p-6 overflow-hidden"
          >
            {preview.data_url && (
              <img
                src={preview.data_url}
                alt={preview.filename || "Preview"}
                data-testid="viewer-preview-img"
                className="max-w-full max-h-full object-contain rounded-lg"
                style={{
                  border: "1px solid rgba(255,255,255,0.08)",
                }}
              />
            )}
          </div>

          {/* Metadata telemetry bar */}
          <div
            data-testid="viewer-meta-bar"
            className="px-5 py-3 grid grid-cols-2 sm:grid-cols-5 gap-3 shrink-0"
            style={{
              background: "var(--surface)",
              borderTop: "1px solid var(--hairline)",
            }}
          >
            <MetaCell
              icon={<Maximize2 size={13} />}
              label={copy.metaDimensions}
              value={
                preview.width && preview.height
                  ? `${preview.width} × ${preview.height}`
                  : "—"
              }
              mono
            />
            <MetaCell
              icon={<FileImage size={13} />}
              label={copy.metaSize}
              value={formatBytes(preview.file_size || 0)}
              mono
            />
            <MetaCell
              icon={<Sparkles size={13} />}
              label={copy.metaFormat}
              value={preview.format || "—"}
              mono
            />
            <MetaCell
              icon={<Clock size={13} />}
              label={copy.metaTime}
              value={`${preview.extract_ms ?? 0} ms`}
              accent
              mono
            />
            <MetaCell
              icon={<Sparkles size={13} />}
              label={copy.metaAlpha}
              value={preview.has_alpha ? copy.alphaYes : copy.alphaNo}
            />
          </div>
        </div>
      )}
    </div>
  );
}

function MetaCell({
  icon,
  label,
  value,
  accent,
  mono,
}: {
  icon: React.ReactNode;
  label: string;
  value: string;
  accent?: boolean;
  mono?: boolean;
}) {
  return (
    <div className="flex flex-col gap-0.5 min-w-0">
      <span
        className="text-[12px] flex items-center gap-1"
        style={{ color: "var(--ink-muted)" }}
      >
        {icon}
        <span>{label}</span>
      </span>
      <span
        dir={mono ? "ltr" : undefined}
        className="text-[13px] font-semibold truncate w-fit"
        style={{
          color: accent ? "var(--accent)" : "var(--ink)",
          fontFamily: mono ? "var(--font-latin)" : undefined,
        }}
      >
        {value}
      </span>
    </div>
  );
}
