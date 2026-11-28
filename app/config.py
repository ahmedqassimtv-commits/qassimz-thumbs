"""Persistent user preferences (theme, language) with atomic writes."""
from __future__ import annotations

import json
import os
import tempfile
from pathlib import Path
from typing import Any

from .paths import settings_path

DEFAULTS: dict[str, Any] = {
    "theme": "dark",
    "lang": "ar",
}


def atomic_write_json(path: Path, data: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(dir=str(path.parent), prefix=".tmp_", suffix=".json")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as fh:
            json.dump(data, fh, ensure_ascii=False, indent=2)
        os.replace(tmp, path)
    except Exception:
        try:
            os.unlink(tmp)
        except OSError:
            pass
        raise


def load_settings(path: Path | None = None) -> dict[str, Any]:
    target = path or settings_path()
    result = dict(DEFAULTS)
    try:
        raw = json.loads(target.read_text(encoding="utf-8"))
        if isinstance(raw, dict):
            if raw.get("theme") in ("dark", "light"):
                result["theme"] = raw["theme"]
            if raw.get("lang") in ("ar", "en"):
                result["lang"] = raw["lang"]
    except Exception:
        pass
    return result


def save_settings(patch: dict[str, Any], path: Path | None = None) -> dict[str, Any]:
    target = path or settings_path()
    current = load_settings(target)
    if patch.get("theme") in ("dark", "light"):
        current["theme"] = patch["theme"]
    if patch.get("lang") in ("ar", "en"):
        current["lang"] = patch["lang"]
    atomic_write_json(target, current)
    return current
