"""Comprehensive unit tests for QASSIMZ Thumbs backend, HKCU registry, preview extractor & API parity."""
from __future__ import annotations

import base64
import io
import json
import re
import struct
import tempfile
import unittest
import urllib.request
import zipfile
from pathlib import Path

from app.main import BootBridge, MIN_SPLASH_S, reveal_delay
from app.preview_extractor import extract_preview_bytes
from app.server import ALLOWED, ThumbsApi, create_server, start_server
from app.shell_registry import (
    FORMAT_BY_ID,
    PROVIDER_CLSID,
    THUMBNAIL_PROVIDER_IID,
    MemoryRegistryBackend,
    ShellRegistryManager,
)

REPO_ROOT = Path(__file__).resolve().parent.parent

# Minimal valid 1x1 transparent PNG
MINIMAL_RGBA_PNG = base64.b64decode(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="
)

# Minimal valid JPEG (SOI + SOF0 2x2 + EOI)
MINIMAL_JPEG = (
    b"\xff\xd8\xff\xe0\x00\x10JFIF\x00\x01\x01\x00\x00\x01\x00\x01\x00\x00"
    b"\xff\xc0\x00\x0b\x08\x00\x02\x00\x02\x01\x01\x11\x00"
    b"\xff\xd9"
)


def _build_synthetic_psd_with_8bim_thumb() -> bytes:
    """Construct a valid PSD header containing an 8BIM 1036 JPEG thumbnail."""
    header = b"8BPS" + struct.pack(">H", 1) + (b"\x00" * 6)
    header += struct.pack(">HIIHH", 3, 1080, 1920, 8, 3)  # 1920x1080 8-bit RGB
    color_mode = struct.pack(">I", 0)
    thumb_block_body = (b"\x00" * 28) + MINIMAL_JPEG
    pad = b"\x00" if len(thumb_block_body) % 2 else b""
    res_block = b"8BIM" + struct.pack(">H", 1036) + b"\x00\x00" + struct.pack(">I", len(thumb_block_body)) + thumb_block_body + pad
    resources = struct.pack(">I", len(res_block)) + res_block
    return header + color_mode + resources


def _build_synthetic_mogrt() -> bytes:
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as zf:
        zf.writestr("definition.json", '{"name":"Lower Third"}')
        zf.writestr("thumb.png", MINIMAL_RGBA_PNG)
    return buf.getvalue()


class TestApiParity(unittest.TestCase):
    def test_frontend_api_endpoints_match_server_allowed(self) -> None:
        api_ts = (REPO_ROOT / "frontend" / "src" / "lib" / "api.ts").read_text(encoding="utf-8")
        called = set(re.findall(r'postApi(?:<[^>]+>)?\(\s*"([a-z0-9_]+)"', api_ts))
        self.assertTrue(called, "Expected postApi(...) calls in frontend/src/lib/api.ts")
        missing = called - set(ALLOWED)
        self.assertEqual(missing, set(), f"Frontend calls endpoints not in server ALLOWED: {missing}")

        required_core = {
            "get_formats_status",
            "toggle_format",
            "enable_all_formats",
            "rebuild_thumbnail_cache",
            "preview_file",
            "splash_ready",
            "ui_ready",
            "boot_status",
        }
        self.assertTrue(required_core.issubset(set(ALLOWED)))


class TestShellRegistry(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = Path(tempfile.mkdtemp(prefix="qz_thumbs_test_"))
        self.fake_dll = self.tmp / "QassimzThumbs.Shell.dll"
        self.fake_dll.write_bytes(b"MZ_TEST_DLL")
        self.backend = MemoryRegistryBackend()
        self.notified = 0

        def _notify() -> None:
            self.notified += 1

        self.mgr = ShellRegistryManager(
            backend=self.backend,
            dll_path=self.fake_dll,
            notify_fn=_notify,
            thumbcache_dir=self.tmp,
        )

    def test_photoshop_spec_includes_all_formats(self) -> None:
        exts = set(FORMAT_BY_ID["psd"].extensions)
        self.assertTrue({".psd", ".psb", ".psdt", ".pdd"}.issubset(exts))

    def test_initial_status_reads_real_registry_zero_mock(self) -> None:
        st = self.mgr.get_formats_status()
        self.assertTrue(st["ok"])
        self.assertEqual(st["active_count"], 0)
        self.assertFalse(st["system_linked"])
        self.assertTrue(st["dll_found"])

    def test_toggle_psd_writes_hkcu_clsid_and_all_photoshop_exts(self) -> None:
        res = self.mgr.toggle_format("psd", True)
        self.assertTrue(res["ok"])
        self.assertTrue(res["system_linked"])
        self.assertEqual(res["active_count"], 1)
        self.assertEqual(self.notified, 1)

        for ext in (".psd", ".psb", ".psdt", ".pdd"):
            val = self.backend.read_value(rf"Software\Classes\{ext}\ShellEx\{THUMBNAIL_PROVIDER_IID}", "")
            self.assertEqual(val, PROVIDER_CLSID)

        off = self.mgr.toggle_format("psd", False)
        self.assertTrue(off["ok"])
        self.assertEqual(off["active_count"], 0)
        self.assertFalse(off["system_linked"])

    def test_missing_dll_returns_explicit_error_not_silent_success(self) -> None:
        mgr_no_dll = ShellRegistryManager(
            backend=self.backend,
            dll_path=self.tmp / "missing.dll",
            notify_fn=lambda: None,
        )
        res = mgr_no_dll.toggle_format("psd", True)
        self.assertFalse(res["ok"])
        self.assertEqual(res["error"], "dll_missing")
        self.assertTrue(res["message_ar"])
        self.assertTrue(res["message_en"])

    def test_enable_all_and_rebuild_cache(self) -> None:
        res = self.mgr.enable_all_formats(True)
        self.assertTrue(res["ok"])
        self.assertEqual(res["active_count"], res["total_count"])

        # Plant stale thumbcache files and verify cleanup
        (self.tmp / "thumbcache_256.db").write_bytes(b"stale")
        (self.tmp / "thumbcache_1024.db").write_bytes(b"stale")
        rebuilt = self.mgr.rebuild_thumbnail_cache()
        self.assertTrue(rebuilt["ok"])
        self.assertTrue(rebuilt["notified"])
        self.assertEqual(rebuilt["removed_files"], 2)


class TestPreviewExtractor(unittest.TestCase):
    def test_psd_8bim_fast_header_extraction(self) -> None:
        raw = _build_synthetic_psd_with_8bim_thumb()
        res = extract_preview_bytes("poster.psd", raw)
        self.assertTrue(res["ok"])
        self.assertEqual(res["format"], "PSD")
        self.assertEqual(res["width"], 1920)
        self.assertEqual(res["height"], 1080)
        self.assertEqual(res["method"], "psd_8bim_header")
        self.assertTrue(res["data_url"].startswith("data:image/jpeg;base64,"))

    def test_mogrt_zip_thumb_extraction(self) -> None:
        raw = _build_synthetic_mogrt()
        res = extract_preview_bytes("title.mogrt", raw)
        self.assertTrue(res["ok"])
        self.assertEqual(res["format"], "MOGRT")
        self.assertTrue(res["has_alpha"])
        self.assertEqual(res["method"], "zip_embedded_thumb")

    def test_svg_direct_vector_preview(self) -> None:
        svg = b'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512"><rect width="512" height="512"/></svg>'
        res = extract_preview_bytes("icon.svg", svg)
        self.assertTrue(res["ok"])
        self.assertEqual(res["width"], 512)
        self.assertEqual(res["height"], 512)
        self.assertTrue(res["has_alpha"])


class TestHttpServerAndBootBridge(unittest.TestCase):
    def test_http_endpoints_and_token_guard(self) -> None:
        tmp = Path(tempfile.mkdtemp(prefix="qz_srv_test_"))
        dll = tmp / "QassimzThumbs.Shell.dll"
        dll.write_bytes(b"MZ")
        mgr = ShellRegistryManager(backend=MemoryRegistryBackend(), dll_path=dll, notify_fn=lambda: None)
        api = ThumbsApi(registry=mgr, settings_file=tmp / "settings.json")
        srv = create_server(api=api, static_root=tmp, token="secret-tok", port=0)
        start_server(srv)
        try:
            host, port = srv.server_address[:2]
            req = urllib.request.Request(
                f"http://{host}:{port}/api/get_formats_status",
                data=b"{}",
                headers={"Content-Type": "application/json", "X-QZ-Token": "secret-tok"},
                method="POST",
            )
            with urllib.request.urlopen(req, timeout=5) as resp:
                body = json.loads(resp.read().decode("utf-8"))
            self.assertTrue(body["ok"])
            self.assertEqual(body["total_count"], 8)
        finally:
            srv.shutdown()
            srv.server_close()

    def test_boot_bridge_reveal_delay(self) -> None:
        self.assertAlmostEqual(reveal_delay(0.0), MIN_SPLASH_S)
        self.assertAlmostEqual(reveal_delay(2.0), 0.0)
        bridge = BootBridge()
        self.assertTrue(bridge.splash_ready()["ok"])


if __name__ == "__main__":
    unittest.main()
