"""Real Windows HKCU Shell ThumbnailProvider registration and cache rebuild.

All COM CLSID and per-extension ShellEx handlers are written under
HKCU\\Software\\Classes (never HKLM) so toggles work immediately with zero UAC.
No mock state is ever fabricated: status is always read directly from the
registry backend, and missing-DLL or write errors are returned explicitly.
"""
from __future__ import annotations

import ctypes
import glob
import os
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Protocol
from urllib.parse import quote

from .paths import find_shell_dll

THUMBNAIL_PROVIDER_IID = "{e357fccd-a995-4576-b01f-234630154e96}"
PROVIDER_CLSID = "{949B93CF-8658-43F7-9720-21A186C8A55A}"
DOTNET_CATEGORY = "{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}"
APPROVED_KEY = r"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved"
ASSEMBLY_FULL_NAME = "QassimzThumbs.Shell, Version=1.4.0.0, Culture=neutral, PublicKeyToken=null"
PROVIDER_CLASS = "Qassimz.Thumbs.ThumbnailProvider"


@dataclass(frozen=True)
class FormatSpec:
    id: str
    group: str  # "design" | "editing" | "raw_modern"
    label: str
    extensions: tuple[str, ...]
    engine_hint_ar: str
    engine_hint_en: str


FORMAT_SPECS: tuple[FormatSpec, ...] = (
    FormatSpec(
        id="psd",
        group="design",
        label="PSD / PSB",
        extensions=(".psd", ".psb", ".psdt", ".pdd"),
        engine_hint_ar="جميع صيغ فوتوشوب (PSD, PSB, PSDT, PDD) — معاينة مدمجة فورية من الترويسة",
        engine_hint_en="All Photoshop formats (PSD, PSB, PSDT, PDD) — composite header preview",
    ),
    FormatSpec(
        id="ai",
        group="design",
        label="AI",
        extensions=(".ai", ".ait"),
        engine_hint_ar="استخراج طبقة معاينة PDF / XMP المدمجة",
        engine_hint_en="Embedded PDF / XMP preview layer",
    ),
    FormatSpec(
        id="eps",
        group="design",
        label="EPS",
        extensions=(".eps", ".epsf"),
        engine_hint_ar="استخراج معاينة TIFF / XMP المدمجة",
        engine_hint_en="Embedded DOS-EPS TIFF / XMP preview",
    ),
    FormatSpec(
        id="svg",
        group="design",
        label="SVG",
        extensions=(".svg",),
        engine_hint_ar="رندر سريع مباشر بدقة المصغرة المطلوبة",
        engine_hint_en="Direct fast vector thumbnail render",
    ),
    FormatSpec(
        id="mogrt",
        group="editing",
        label="MOGRT",
        extensions=(".mogrt", ".aegraphic"),
        engine_hint_ar="فك سريع للأرشيف وقراءة thumb.png مباشرة",
        engine_hint_en="Fast archive read of thumb.png / poster frame",
    ),
    FormatSpec(
        id="raw",
        group="raw_modern",
        label="RAW / DNG",
        extensions=(".cr3", ".cr2", ".arw", ".nef", ".dng", ".raf", ".orf", ".rw2"),
        engine_hint_ar="استخراج Embedded JPEG Preview فقط دون فك الخام",
        engine_hint_en="Embedded JPEG preview only (zero sensor demosaic)",
    ),
    FormatSpec(
        id="heic",
        group="raw_modern",
        label="HEIC",
        extensions=(".heic", ".heif"),
        engine_hint_ar="رندر ومعاينة فورية لصور Apple HEIF",
        engine_hint_en="Fast direct thumbnail render for HEIC/HEIF",
    ),
    FormatSpec(
        id="webp",
        group="raw_modern",
        label="WEBP",
        extensions=(".webp",),
        engine_hint_ar="رندر سريع مباشر مع دعم الشفافية",
        engine_hint_en="Direct fast WebP thumbnail with alpha",
    ),
)

FORMAT_BY_ID: dict[str, FormatSpec] = {spec.id: spec for spec in FORMAT_SPECS}


class RegistryBackend(Protocol):
    @property
    def supported(self) -> bool: ...
    def read_value(self, subkey: str, name: str = "") -> Any | None: ...
    def write_value(self, subkey: str, name: str, value: Any, kind: str = "sz") -> None: ...
    def delete_subkey(self, subkey: str) -> None: ...


class WinRegBackend:
    """Real HKCU registry backend on Windows (zero UAC required)."""

    def __init__(self) -> None:
        self._winreg: Any = None
        if sys.platform == "win32":
            try:
                import winreg  # type: ignore[import-not-found]
                self._winreg = winreg
            except ImportError:
                self._winreg = None

    @property
    def supported(self) -> bool:
        return self._winreg is not None

    def read_value(self, subkey: str, name: str = "") -> Any | None:
        if not self._winreg:
            return None
        try:
            with self._winreg.OpenKey(self._winreg.HKEY_CURRENT_USER, subkey, 0, self._winreg.KEY_READ) as key:
                val, _ = self._winreg.QueryValueEx(key, name)
                return val
        except OSError:
            return None

    def write_value(self, subkey: str, name: str, value: Any, kind: str = "sz") -> None:
        if not self._winreg:
            raise OSError("Windows registry (HKCU) is unavailable on this platform.")
        reg_type = self._winreg.REG_DWORD if kind == "dword" else self._winreg.REG_SZ
        with self._winreg.CreateKeyEx(self._winreg.HKEY_CURRENT_USER, subkey, 0, self._winreg.KEY_WRITE) as key:
            self._winreg.SetValueEx(key, name, 0, reg_type, value)

    def delete_subkey(self, subkey: str) -> None:
        if not self._winreg:
            return
        try:
            self._winreg.DeleteKey(self._winreg.HKEY_CURRENT_USER, subkey)
        except OSError:
            pass


class MemoryRegistryBackend:
    """In-memory HKCU registry store for deterministic unit testing."""

    def __init__(self) -> None:
        self.store: dict[str, dict[str, Any]] = {}

    @property
    def supported(self) -> bool:
        return True

    def read_value(self, subkey: str, name: str = "") -> Any | None:
        return self.store.get(subkey.lower(), {}).get(name.lower())

    def write_value(self, subkey: str, name: str, value: Any, kind: str = "sz") -> None:
        bucket = self.store.setdefault(subkey.lower(), {})
        bucket[name.lower()] = value

    def delete_subkey(self, subkey: str) -> None:
        self.store.pop(subkey.lower(), None)


def _file_uri(path: Path) -> str:
    resolved = str(path.resolve()).replace("\\", "/")
    if not resolved.startswith("/"):
        resolved = "/" + resolved
    return "file://" + quote(resolved, safe="/:")


class ShellRegistryManager:
    """Reads and mutates HKCU\\Software\\Classes thumbnail provider entries."""

    def __init__(
        self,
        backend: RegistryBackend | None = None,
        dll_path: Path | None = None,
        notify_fn: Any = None,
        thumbcache_dir: Path | None = None,
    ) -> None:
        self._backend = backend if backend is not None else WinRegBackend()
        self._explicit_dll = dll_path
        self._notify_fn = notify_fn
        self._thumbcache_dir = thumbcache_dir

    def resolve_dll(self) -> Path | None:
        if self._explicit_dll is not None:
            return self._explicit_dll if self._explicit_dll.is_file() else None
        return find_shell_dll()

    def _clsid_key(self) -> str:
        return rf"Software\Classes\CLSID\{PROVIDER_CLSID}"

    def _ext_keys(self, ext: str) -> tuple[str, str]:
        return (
            rf"Software\Classes\{ext}\ShellEx\{THUMBNAIL_PROVIDER_IID}",
            rf"Software\Classes\SystemFileAssociations\{ext}\ShellEx\{THUMBNAIL_PROVIDER_IID}",
        )

    def _is_com_server_registered(self) -> bool:
        codebase = self._backend.read_value(self._clsid_key() + r"\InprocServer32", "CodeBase")
        return bool(codebase)

    def _is_extension_enabled(self, ext: str) -> bool:
        primary, _ = self._ext_keys(ext)
        val = self._backend.read_value(primary, "")
        return isinstance(val, str) and val.strip().lower() == PROVIDER_CLSID.lower()

    def _ensure_com_server(self, dll: Path) -> None:
        base = self._clsid_key()
        self._backend.write_value(base, "", "QASSIMZ Thumbs Thumbnail Provider")
        self._backend.write_value(base, "DisableProcessIsolation", 0, kind="dword")
        inproc = base + r"\InprocServer32"
        self._backend.write_value(inproc, "", "mscoree.dll")
        self._backend.write_value(inproc, "ThreadingModel", "Both")
        self._backend.write_value(inproc, "Class", PROVIDER_CLASS)
        self._backend.write_value(inproc, "Assembly", ASSEMBLY_FULL_NAME)
        self._backend.write_value(inproc, "RuntimeVersion", "v4.0.30319")
        self._backend.write_value(inproc, "CodeBase", _file_uri(dll))
        self._backend.write_value(base + rf"\Implemented Categories\{DOTNET_CATEGORY}", "", "")
        self._backend.write_value(APPROVED_KEY, PROVIDER_CLSID, "QASSIMZ Thumbs Thumbnail Provider")

    def notify_shell(self) -> bool:
        if self._notify_fn is not None:
            self._notify_fn()
            return True
        if sys.platform == "win32":
            try:
                # SHCNE_ASSOCCHANGED = 0x08000000, SHCNF_IDLIST = 0x0000
                ctypes.windll.shell32.SHChangeNotify(0x08000000, 0x0000, None, None)
                return True
            except Exception:
                return False
        return False

    def get_formats_status(self) -> dict[str, Any]:
        dll = self.resolve_dll()
        com_registered = self._is_com_server_registered() if self._backend.supported else False
        formats: list[dict[str, Any]] = []
        enabled_count = 0

        for spec in FORMAT_SPECS:
            ext_states = [self._is_extension_enabled(ext) for ext in spec.extensions] if self._backend.supported else [False]
            is_enabled = bool(com_registered and all(ext_states))
            if is_enabled:
                enabled_count += 1
            formats.append({
                "id": spec.id,
                "group": spec.group,
                "label": spec.label,
                "extensions": list(spec.extensions),
                "enabled": is_enabled,
                "engine_hint_ar": spec.engine_hint_ar,
                "engine_hint_en": spec.engine_hint_en,
            })

        return {
            "ok": True,
            "platform_supported": self._backend.supported,
            "dll_found": dll is not None,
            "dll_path": str(dll) if dll else None,
            "com_registered": com_registered,
            "clsid": PROVIDER_CLSID,
            "iid": THUMBNAIL_PROVIDER_IID,
            "registry_root": r"HKCU\Software\Classes",
            "active_count": enabled_count,
            "total_count": len(FORMAT_SPECS),
            "system_linked": bool(com_registered and enabled_count > 0),
            "formats": formats,
        }

    def toggle_format(self, format_id: str, enabled: bool) -> dict[str, Any]:
        spec = FORMAT_BY_ID.get((format_id or "").strip().lower())
        if spec is None:
            return {
                "ok": False,
                "error": "unknown_format",
                "message_en": f"Unknown format '{format_id}'.",
                "message_ar": f"صيغة غير معروفة: {format_id}.",
            }
        if not self._backend.supported:
            return {
                "ok": False,
                "error": "unsupported_platform",
                "message_en": "Windows Explorer HKCU registry is only available on Windows.",
                "message_ar": "تسجيل مصغرات مستكشف ويندوز (HKCU) يعمل على نظام Windows فقط.",
            }
        dll = self.resolve_dll()
        if enabled and dll is None:
            return {
                "ok": False,
                "error": "dll_missing",
                "message_en": "COM Shell DLL (QassimzThumbs.Shell.dll) was not found. Build or reinstall the package first.",
                "message_ar": "ملف مزوّد المصغرات (QassimzThumbs.Shell.dll) غير موجود. يرجى بناء الحزمة أو إعادة التثبيت أولاً.",
            }
        try:
            if enabled and dll is not None:
                self._ensure_com_server(dll)
                for ext in spec.extensions:
                    for key in self._ext_keys(ext):
                        self._backend.write_value(key, "", PROVIDER_CLSID)
            else:
                for ext in spec.extensions:
                    for key in self._ext_keys(ext):
                        self._backend.delete_subkey(key)
            self.notify_shell()
        except OSError as exc:
            return {
                "ok": False,
                "error": "registry_write_failed",
                "message_en": f"Failed to update HKCU registry for {spec.label}: {exc}",
                "message_ar": f"فشل تحديث الريجستري لصيغة {spec.label}: {exc}",
            }

        status = self.get_formats_status()
        status["toggled"] = {"id": spec.id, "enabled": enabled}
        return status

    def enable_all_formats(self, enabled: bool = True) -> dict[str, Any]:
        if not self._backend.supported:
            return {
                "ok": False,
                "error": "unsupported_platform",
                "message_en": "Windows Explorer HKCU registry is only available on Windows.",
                "message_ar": "تسجيل مصغرات مستكشف ويندوز (HKCU) يعمل على نظام Windows فقط.",
            }
        dll = self.resolve_dll()
        if enabled and dll is None:
            return {
                "ok": False,
                "error": "dll_missing",
                "message_en": "COM Shell DLL (QassimzThumbs.Shell.dll) was not found. Build or reinstall the package first.",
                "message_ar": "ملف مزوّد المصغرات (QassimzThumbs.Shell.dll) غير موجود. يرجى بناء الحزمة أو إعادة التثبيت أولاً.",
            }
        try:
            if enabled and dll is not None:
                self._ensure_com_server(dll)
                for spec in FORMAT_SPECS:
                    for ext in spec.extensions:
                        for key in self._ext_keys(ext):
                            self._backend.write_value(key, "", PROVIDER_CLSID)
            else:
                for spec in FORMAT_SPECS:
                    for ext in spec.extensions:
                        for key in self._ext_keys(ext):
                            self._backend.delete_subkey(key)
            self.notify_shell()
        except OSError as exc:
            return {
                "ok": False,
                "error": "registry_write_failed",
                "message_en": f"Failed to update HKCU registry: {exc}",
                "message_ar": f"فشل تحديث الريجستري: {exc}",
            }
        return self.get_formats_status()

    def rebuild_thumbnail_cache(self) -> dict[str, Any]:
        """Notify Windows Shell and safely clear stale thumbcache_*.db files."""
        notified = self.notify_shell()
        removed = 0
        locked = 0

        cache_dir = self._thumbcache_dir
        if cache_dir is None and sys.platform == "win32":
            local_app = os.environ.get("LOCALAPPDATA")
            if local_app:
                cache_dir = Path(local_app) / "Microsoft" / "Windows" / "Explorer"

        if cache_dir is not None and cache_dir.is_dir():
            for match in glob.glob(str(cache_dir / "thumbcache_*.db")):
                try:
                    os.unlink(match)
                    removed += 1
                except OSError:
                    locked += 1

        return {
            "ok": True,
            "notified": notified,
            "removed_files": removed,
            "locked_files": locked,
            "message_en": f"Windows shell notified. Cleared {removed} cache file(s).",
            "message_ar": f"تم إشعار مستكشف ويندوز وتنظيف {removed} ملف كاش قديم.",
        }
