"""Hardened local HTTP/1.1 server for QASSIMZ Thumbs.

Binds strictly to 127.0.0.1, requires per-session X-QZ-Token on POST /api/*,
enforces an explicit ALLOWED endpoint allowlist, and serves static assets
from app/ui/ with ETag caching and strict CSP.
"""
from __future__ import annotations

import base64
import hashlib
import json
import mimetypes
import secrets
import threading
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any
from urllib.parse import urlparse

from . import APP_NAME, APP_VERSION
from .config import load_settings, save_settings
from .paths import ui_dir
from .preview_extractor import extract_preview_bytes, extract_preview_file
from .shell_registry import ShellRegistryManager

ALLOWED: frozenset[str] = frozenset({
    "get_formats_status",
    "toggle_format",
    "enable_all_formats",
    "rebuild_thumbnail_cache",
    "preview_file",
    "splash_ready",
    "ui_ready",
    "boot_status",
    "get_settings",
    "save_settings",
    "win_minimize",
    "win_toggle_maximize",
    "win_close",
    "win_quit",
})

MAX_BODY_BYTES = 24 * 1024 * 1024  # allow drag-and-drop base64 preview payloads up to 24 MB

CSP_HEADER = (
    "default-src 'self'; "
    "script-src 'self'; "
    "style-src 'self' 'unsafe-inline'; "
    "img-src 'self' data: blob:; "
    "font-src 'self' data:; "
    "connect-src 'self'; "
    "frame-ancestors 'none'; "
    "base-uri 'self'"
)

mimetypes.add_type("font/woff2", ".woff2")
mimetypes.add_type("image/svg+xml", ".svg")


class ThumbsApi:
    """Backend dispatcher backing POST /api/<name>."""

    def __init__(
        self,
        registry: ShellRegistryManager | None = None,
        settings_file: Path | None = None,
        boot_bridge: Any = None,
        window_controls: Any = None,
    ) -> None:
        self.registry = registry if registry is not None else ShellRegistryManager()
        self.settings_file = settings_file
        self.boot_bridge = boot_bridge
        self.window_controls = window_controls
        self._boot_stage = "preparing"
        self._ui_ready_flag = False

    def dispatch(self, name: str, payload: dict[str, Any]) -> dict[str, Any]:
        if name not in ALLOWED:
            return {"ok": False, "error": "endpoint_not_allowed", "endpoint": name}
        handler = getattr(self, f"_op_{name}", None)
        if handler is None:
            return {"ok": False, "error": "not_implemented", "endpoint": name}
        return handler(payload)

    def _op_get_formats_status(self, _payload: dict[str, Any]) -> dict[str, Any]:
        self._boot_stage = "ready"
        return self.registry.get_formats_status()

    def _op_toggle_format(self, payload: dict[str, Any]) -> dict[str, Any]:
        format_id = str(payload.get("format_id") or payload.get("id") or "")
        enabled = bool(payload.get("enabled", True))
        return self.registry.toggle_format(format_id, enabled)

    def _op_enable_all_formats(self, payload: dict[str, Any]) -> dict[str, Any]:
        enabled = bool(payload.get("enabled", True))
        return self.registry.enable_all_formats(enabled=enabled)

    def _op_rebuild_thumbnail_cache(self, _payload: dict[str, Any]) -> dict[str, Any]:
        return self.registry.rebuild_thumbnail_cache()

    def _op_preview_file(self, payload: dict[str, Any]) -> dict[str, Any]:
        file_path = payload.get("path")
        if isinstance(file_path, str) and file_path.strip():
            return extract_preview_file(file_path.strip())

        filename = str(payload.get("filename") or "preview.bin")
        data_b64 = payload.get("data_b64")
        if isinstance(data_b64, str) and data_b64:
            try:
                raw = base64.b64decode(data_b64, validate=False)
            except Exception:
                return {
                    "ok": False,
                    "error": "invalid_base64",
                    "message_en": "Invalid file payload.",
                    "message_ar": "بيانات الملف غير صالحة.",
                }
            file_size = payload.get("file_size")
            return extract_preview_bytes(
                filename,
                raw,
                file_size=int(file_size) if isinstance(file_size, int) else len(raw),
            )

        return {
            "ok": False,
            "error": "missing_file",
            "message_en": "Provide a file path or dropped file payload.",
            "message_ar": "يرجى اختيار ملف أو سحبه وإفلاته للمعاينة.",
        }

    def _op_splash_ready(self, _payload: dict[str, Any]) -> dict[str, Any]:
        self._boot_stage = "checking"
        if self.boot_bridge is not None:
            return self.boot_bridge.splash_ready()
        return {"ok": True}

    def _op_ui_ready(self, _payload: dict[str, Any]) -> dict[str, Any]:
        self._ui_ready_flag = True
        self._boot_stage = "ready"
        if self.boot_bridge is not None:
            return self.boot_bridge.ui_ready()
        return {"ok": True}

    def _op_boot_status(self, _payload: dict[str, Any]) -> dict[str, Any]:
        settings = load_settings(self.settings_file)
        return {
            "ok": True,
            "app": APP_NAME,
            "version": APP_VERSION,
            "stage": self._boot_stage,
            "ui_ready": self._ui_ready_flag,
            "theme": settings["theme"],
            "lang": settings["lang"],
        }

    def _op_get_settings(self, _payload: dict[str, Any]) -> dict[str, Any]:
        settings = load_settings(self.settings_file)
        return {"ok": True, **settings, "version": APP_VERSION}

    def _op_save_settings(self, payload: dict[str, Any]) -> dict[str, Any]:
        updated = save_settings(payload, self.settings_file)
        return {"ok": True, **updated}

    def _op_win_minimize(self, _payload: dict[str, Any]) -> dict[str, Any]:
        if self.window_controls is not None:
            self.window_controls.minimize()
        return {"ok": True}

    def _op_win_toggle_maximize(self, _payload: dict[str, Any]) -> dict[str, Any]:
        maximized = False
        if self.window_controls is not None:
            maximized = bool(self.window_controls.toggle_maximize())
        return {"ok": True, "maximized": maximized}

    def _op_win_close(self, _payload: dict[str, Any]) -> dict[str, Any]:
        if self.window_controls is not None:
            self.window_controls.close()
        return {"ok": True}

    def _op_win_quit(self, _payload: dict[str, Any]) -> dict[str, Any]:
        if self.window_controls is not None:
            self.window_controls.quit()
        return {"ok": True}


class QZHTTPServer(ThreadingHTTPServer):
    allow_reuse_address = True

    def __init__(
        self,
        server_address: tuple[str, int],
        api: ThumbsApi,
        static_root: Path,
        token: str,
    ) -> None:
        self.api = api
        self.static_root = static_root.resolve()
        self.token = token
        super().__init__(server_address, _Handler)


class _Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    server: QZHTTPServer  # type: ignore[assignment]

    def log_message(self, format: str, *args: Any) -> None:  # noqa: A002
        return

    def _send_json(self, status: int, body: dict[str, Any]) -> None:
        raw = json.dumps(body, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(raw)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.end_headers()
        self.wfile.write(raw)

    def do_POST(self) -> None:
        parsed = urlparse(self.path)
        if not parsed.path.startswith("/api/"):
            self._send_json(HTTPStatus.NOT_FOUND, {"ok": False, "error": "not_found"})
            return

        token = self.headers.get("X-QZ-Token", "")
        if not secrets.compare_digest(token, self.server.token):
            self._send_json(HTTPStatus.FORBIDDEN, {"ok": False, "error": "invalid_token"})
            return

        name = parsed.path[len("/api/") :].strip("/")
        if name not in ALLOWED:
            self._send_json(HTTPStatus.NOT_FOUND, {"ok": False, "error": "endpoint_not_allowed"})
            return

        try:
            length = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            length = 0
        if length < 0 or length > MAX_BODY_BYTES:
            self._send_json(HTTPStatus.REQUEST_ENTITY_TOO_LARGE, {"ok": False, "error": "payload_too_large"})
            return

        raw = self.rfile.read(length) if length > 0 else b"{}"
        try:
            payload = json.loads(raw.decode("utf-8")) if raw else {}
            if not isinstance(payload, dict):
                payload = {}
        except Exception:
            self._send_json(HTTPStatus.BAD_REQUEST, {"ok": False, "error": "invalid_json"})
            return

        result = self.server.api.dispatch(name, payload)
        self._send_json(HTTPStatus.OK, result)

    def do_GET(self) -> None:
        parsed = urlparse(self.path)
        rel = parsed.path.lstrip("/") or "index.html"
        target = (self.server.static_root / rel).resolve()
        try:
            target.relative_to(self.server.static_root)
        except ValueError:
            self.send_error(HTTPStatus.FORBIDDEN)
            return

        if not target.is_file():
            self.send_error(HTTPStatus.NOT_FOUND)
            return

        data = target.read_bytes()
        ctype = mimetypes.guess_type(str(target))[0] or "application/octet-stream"
        etag = '"' + hashlib.sha256(data).hexdigest()[:16] + '"'

        if self.headers.get("If-None-Match") == etag:
            self.send_response(HTTPStatus.NOT_MODIFIED)
            self.send_header("ETag", etag)
            self.send_header("Content-Length", "0")
            self.end_headers()
            return

        self.send_response(HTTPStatus.OK)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.send_header("ETag", etag)
        self.send_header("Content-Security-Policy", CSP_HEADER)
        self.send_header("X-Content-Type-Options", "nosniff")
        self.end_headers()
        self.wfile.write(data)


def create_server(
    api: ThumbsApi | None = None,
    static_root: Path | None = None,
    token: str | None = None,
    port: int = 0,
) -> QZHTTPServer:
    return QZHTTPServer(
        ("127.0.0.1", port),
        api=api if api is not None else ThumbsApi(),
        static_root=static_root if static_root is not None else ui_dir(),
        token=token if token is not None else secrets.token_urlsafe(24),
    )


def start_server(srv: QZHTTPServer) -> threading.Thread:
    thread = threading.Thread(target=srv.serve_forever, daemon=True)
    thread.start()
    return thread


def server_url(srv: QZHTTPServer, path: str = "index.html", theme: str | None = None, lang: str | None = None) -> str:
    host, port = srv.server_address[:2]
    qs = f"token={srv.token}"
    if theme in ("dark", "light"):
        qs += f"&theme={theme}"
    if lang in ("ar", "en"):
        qs += f"&lang={lang}"
    return f"http://{host}:{port}/{path}?{qs}"
