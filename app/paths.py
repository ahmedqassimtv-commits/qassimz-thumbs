"""Filesystem path resolution for QASSIMZ Thumbs."""
from __future__ import annotations

import os
import sys
from pathlib import Path

from . import APP_VERSION


def repo_root() -> Path:
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parent.parent


def ui_dir() -> Path:
    return Path(__file__).resolve().parent / "ui"


def data_dir() -> Path:
    if sys.platform == "win32":
        base = Path(os.environ.get("LOCALAPPDATA") or (Path.home() / "AppData" / "Local"))
    else:
        base = Path(os.environ.get("XDG_CONFIG_HOME") or (Path.home() / ".config"))
    path = base / "QASSIMZ Thumbs"
    path.mkdir(parents=True, exist_ok=True)
    return path


def settings_path() -> Path:
    return data_dir() / "settings.json"


def window_state_path() -> Path:
    return data_dir() / "window.json"


def log_path() -> Path:
    return data_dir() / "thumbs.log"


def find_shell_dll(extra_dirs: list[Path] | None = None) -> Path | None:
    """Locate the compiled 64-bit COM IThumbnailProvider DLL if present."""
    root = repo_root()
    candidates: list[Path] = []
    if extra_dirs:
        for d in extra_dirs:
            candidates.append(d / "QassimzThumbs.Shell.dll")
    candidates.extend([
        root / "shell" / APP_VERSION / "QassimzThumbs.Shell.dll",
        root / "shell" / "QassimzThumbs.Shell.dll",
        root / "QassimzThumbs.Shell.dll",
        root / "dist" / "app" / "shell" / APP_VERSION / "QassimzThumbs.Shell.dll",
        root / "src" / "Shell" / "bin" / "Release" / "net48" / "QassimzThumbs.Shell.dll",
        root / "src" / "Shell" / "bin" / "Debug" / "net48" / "QassimzThumbs.Shell.dll",
    ])
    for candidate in candidates:
        if candidate.is_file():
            return candidate.resolve()
    return None
