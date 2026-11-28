import type { Lang, Theme } from "./i18n";

export interface FormatItem {
  id: string;
  group: "design" | "editing" | "raw_modern";
  label: string;
  extensions: string[];
  enabled: boolean;
  engine_hint_ar: string;
  engine_hint_en: string;
}

export interface FormatsStatusResponse {
  ok: boolean;
  error?: string;
  message_en?: string;
  message_ar?: string;
  platform_supported: boolean;
  dll_found: boolean;
  dll_path: string | null;
  com_registered: boolean;
  clsid: string;
  iid: string;
  registry_root: string;
  active_count: number;
  total_count: number;
  system_linked: boolean;
  formats: FormatItem[];
}

export interface RebuildCacheResponse {
  ok: boolean;
  notified: boolean;
  removed_files: number;
  locked_files: number;
  message_en: string;
  message_ar: string;
}

export interface PreviewResponse {
  ok: boolean;
  error?: string;
  message_en?: string;
  message_ar?: string;
  filename?: string;
  format?: string;
  width?: number;
  height?: number;
  file_size?: number;
  extract_ms?: number;
  has_alpha?: boolean;
  method?: string;
  data_url?: string;
}

export interface BootStatusResponse {
  ok: boolean;
  app: string;
  version: string;
  stage: string;
  ui_ready: boolean;
  theme: Theme;
  lang: Lang;
}

export interface SettingsResponse {
  ok: boolean;
  theme: Theme;
  lang: Lang;
  version?: string;
}

function getToken(): string {
  const params = new URLSearchParams(window.location.search);
  const urlToken = params.get("token");
  if (urlToken) {
    try {
      sessionStorage.setItem("qz_thumbs_token", urlToken);
    } catch {
      // ignore
    }
    return urlToken;
  }
  try {
    return sessionStorage.getItem("qz_thumbs_token") || "";
  } catch {
    return "";
  }
}

async function postApi<T>(endpoint: string, payload: Record<string, unknown> = {}): Promise<T> {
  const res = await fetch(`/api/${endpoint}`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "X-QZ-Token": getToken(),
    },
    body: JSON.stringify(payload),
  });
  if (!res.ok) {
    throw new Error(`API ${endpoint} failed with HTTP ${res.status}`);
  }
  return (await res.json()) as T;
}

export const api = {
  getFormatsStatus: () => postApi<FormatsStatusResponse>("get_formats_status"),
  toggleFormat: (format_id: string, enabled: boolean) =>
    postApi<FormatsStatusResponse>("toggle_format", { format_id, enabled }),
  enableAllFormats: (enabled = true) =>
    postApi<FormatsStatusResponse>("enable_all_formats", { enabled }),
  rebuildThumbnailCache: () => postApi<RebuildCacheResponse>("rebuild_thumbnail_cache"),
  previewFile: (payload: { path?: string; filename?: string; data_b64?: string; file_size?: number }) =>
    postApi<PreviewResponse>("preview_file", payload),
  splashReady: () => postApi<{ ok: boolean }>("splash_ready"),
  uiReady: () => postApi<{ ok: boolean }>("ui_ready"),
  bootStatus: () => postApi<BootStatusResponse>("boot_status"),
  getSettings: () => postApi<SettingsResponse>("get_settings"),
  saveSettings: (patch: Partial<{ theme: Theme; lang: Lang }>) =>
    postApi<SettingsResponse>("save_settings", patch),
  winMinimize: () => postApi<{ ok: boolean }>("win_minimize"),
  winToggleMaximize: () => postApi<{ ok: boolean; maximized: boolean }>("win_toggle_maximize"),
  winClose: () => postApi<{ ok: boolean }>("win_close"),
  winQuit: () => postApi<{ ok: boolean }>("win_quit"),
};
