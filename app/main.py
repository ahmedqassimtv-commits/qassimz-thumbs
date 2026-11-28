"""QASSIMZ Thumbs desktop entry point with flicker-free Splash -> Main handoff."""
from __future__ import annotations

import argparse
import sys
import threading
import time
from typing import Any

from . import APP_NAME
from .config import load_settings
from .server import ThumbsApi, create_server, server_url, start_server
from .shell_registry import ShellRegistryManager

MIN_SPLASH_S = 1.3
MAX_SPLASH_S = 5.0
SPLASH_FADE_S = 0.20
SPLASH_SIZE = (660, 400)
MAIN_SIZE = (1180, 760)
MIN_SIZE = (920, 620)


def reveal_delay(elapsed: float) -> float:
    if elapsed < 0:
        return MIN_SPLASH_S
    return max(0.0, MIN_SPLASH_S - elapsed)


class BootBridge:
    """Coordinates flicker-free Splash -> Main window handoff."""

    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._splash: Any = None
        self._main: Any = None
        self._splash_shown_at: float | None = None
        self._ui_ready = False
        self._revealed = False
        self._safety: threading.Timer | None = None

    def attach(self, splash: Any, main: Any) -> None:
        with self._lock:
            self._splash = splash
            self._main = main

    def splash_ready(self) -> dict[str, Any]:
        with self._lock:
            if self._revealed:
                return {"ok": False, "error": "already_revealed"}
            self._splash_shown_at = time.monotonic()
            splash = self._splash
            self._safety = threading.Timer(MAX_SPLASH_S, self._force_reveal)
            self._safety.daemon = True
            self._safety.start()
        try:
            if splash is not None:
                splash.show()
        except Exception:
            pass
        return {"ok": True}

    def ui_ready(self) -> dict[str, Any]:
        with self._lock:
            if self._ui_ready or self._revealed:
                return {"ok": False, "error": "already_ready"}
            self._ui_ready = True
            shown = self._splash_shown_at
        elapsed = 0.0 if shown is None else time.monotonic() - shown
        delay = reveal_delay(elapsed)
        worker = threading.Timer(delay, self._reveal)
        worker.daemon = True
        worker.start()
        return {"ok": True, "delay": round(delay, 3)}

    def _force_reveal(self) -> None:
        self._reveal()

    def _reveal(self) -> None:
        with self._lock:
            if self._revealed:
                return
            self._revealed = True
            splash = self._splash
            main = self._main
            if self._safety is not None:
                self._safety.cancel()
        try:
            if splash is not None:
                splash.evaluate_js("window.__qzSplashExit && window.__qzSplashExit()")
        except Exception:
            pass
        time.sleep(SPLASH_FADE_S)
        try:
            if main is not None:
                main.show()
                enable_win11_window_animations(main)
        except Exception:
            pass
        try:
            if splash is not None:
                splash.destroy()
        except Exception:
            pass


def _find_window_hwnd(window: Any) -> int | None:
    if sys.platform != "win32" or window is None:
        return None
    try:
        import ctypes
        user32 = ctypes.windll.user32
        hwnd = user32.FindWindowW(None, getattr(window, "title", APP_NAME))
        if not hwnd:
            hwnd = user32.GetForegroundWindow()
        return int(hwnd) if hwnd else None
    except Exception:
        return None


def enable_win11_window_animations(window: Any, dark: bool = True) -> int | None:
    """Restore native Windows 11 minimize/maximize/close DWM animations on a frameless window."""
    if sys.platform != "win32":
        return None
    hwnd = _find_window_hwnd(window)
    if not hwnd:
        return None
    try:
        import ctypes

        user32 = ctypes.windll.user32
        dwmapi = ctypes.windll.dwmapi

        GWL_STYLE = -16
        WS_MINIMIZEBOX = 0x00020000
        WS_MAXIMIZEBOX = 0x00010000
        WS_SYSMENU = 0x00080000
        style = user32.GetWindowLongW(hwnd, GWL_STYLE)
        new_style = style | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU
        if new_style != style:
            user32.SetWindowLongW(hwnd, GWL_STYLE, new_style)

        # DWMWA_TRANSITIONS_FORCEDISABLED (3) -> 0 enables native Win11 minimize/maximize/close animations
        disable_transitions = ctypes.c_int(0)
        dwmapi.DwmSetWindowAttribute(hwnd, 3, ctypes.byref(disable_transitions), ctypes.sizeof(disable_transitions))
        # DWMWA_USE_IMMERSIVE_DARK_MODE (20)
        dark_val = ctypes.c_int(1 if dark else 0)
        dwmapi.DwmSetWindowAttribute(hwnd, 20, ctypes.byref(dark_val), ctypes.sizeof(dark_val))
        # DWMWA_WINDOW_CORNER_PREFERENCE (33) -> DWMWCP_ROUND (2)
        corner_pref = ctypes.c_int(2)
        dwmapi.DwmSetWindowAttribute(hwnd, 33, ctypes.byref(corner_pref), ctypes.sizeof(corner_pref))
        # DWMWA_SYSTEMBACKDROP_TYPE (38) -> DWMSBT_MAINWINDOW (2 = Mica)
        mica = ctypes.c_int(2)
        dwmapi.DwmSetWindowAttribute(hwnd, 38, ctypes.byref(mica), ctypes.sizeof(mica))

        class MARGINS(ctypes.Structure):
            _fields_ = [
                ("cxLeftWidth", ctypes.c_int),
                ("cxRightWidth", ctypes.c_int),
                ("cyTopHeight", ctypes.c_int),
                ("cyBottomHeight", ctypes.c_int),
            ]

        margins = MARGINS(0, 0, 0, 1)
        dwmapi.DwmExtendFrameIntoClientArea(hwnd, ctypes.byref(margins))

        SWP_FRAMECHANGED = 0x0020
        SWP_NOMOVE = 0x0002
        SWP_NOSIZE = 0x0001
        SWP_NOZORDER = 0x0004
        SWP_NOACTIVATE = 0x0010
        user32.SetWindowPos(hwnd, 0, 0, 0, 0, 0, SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE)
    except Exception:
        pass
    return hwnd


class _WindowControls:
    def __init__(self) -> None:
        self.window: Any = None
        self._hwnd: int | None = None
        self._maximized = False

    def _resolve_hwnd(self) -> int | None:
        if not self._hwnd:
            self._hwnd = _find_window_hwnd(self.window)
        return self._hwnd

    def minimize(self) -> None:
        hwnd = self._resolve_hwnd()
        if sys.platform == "win32" and hwnd:
            try:
                import ctypes
                ctypes.windll.user32.ShowWindow(int(hwnd), 6)  # SW_MINIMIZE (native animation)
                return
            except Exception:
                pass
        if self.window is not None:
            self.window.minimize()

    def toggle_maximize(self) -> bool:
        hwnd = self._resolve_hwnd()
        if sys.platform == "win32" and hwnd:
            try:
                import ctypes
                SW_MAXIMIZE = 3
                SW_RESTORE = 9
                if self._maximized:
                    ctypes.windll.user32.ShowWindow(int(hwnd), SW_RESTORE)
                    self._maximized = False
                else:
                    ctypes.windll.user32.ShowWindow(int(hwnd), SW_MAXIMIZE)
                    self._maximized = True
                return self._maximized
            except Exception:
                pass
        if self.window is not None:
            if self._maximized:
                self.window.restore()
                self._maximized = False
            else:
                self.window.maximize()
                self._maximized = True
        return self._maximized

    def close(self) -> None:
        hwnd = self._resolve_hwnd()
        if sys.platform == "win32" and hwnd:
            try:
                import ctypes
                WM_CLOSE = 0x0010
                ctypes.windll.user32.PostMessageW(int(hwnd), WM_CLOSE, 0, 0)
                return
            except Exception:
                pass
        if self.window is not None:
            self.window.destroy()

    def quit(self) -> None:
        self.close()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="qassimz-thumbs")
    parser.add_argument("--register", action="store_true", help="Register all HKCU shell thumbnail formats")
    parser.add_argument("--unregister", action="store_true", help="Unregister all HKCU shell thumbnail formats")
    parser.add_argument("--rebuild-cache", action="store_true", help="Rebuild Windows Explorer thumbnail cache")
    args = parser.parse_args(argv)

    registry = ShellRegistryManager()
    if args.register:
        res = registry.enable_all_formats(True)
        return 0 if res.get("ok") else 1
    if args.unregister:
        res = registry.enable_all_formats(False)
        return 0 if res.get("ok") else 1
    if args.rebuild_cache:
        registry.rebuild_thumbnail_cache()
        return 0

    import webview  # type: ignore[import-not-found]

    settings = load_settings()
    theme = settings["theme"]
    lang = settings["lang"]
    bg_hex = "#111215" if theme == "dark" else "#F7F6F2"

    bridge = BootBridge()
    controls = _WindowControls()
    api = ThumbsApi(registry=registry, boot_bridge=bridge, window_controls=controls)
    srv = create_server(api=api)
    start_server(srv)

    splash_win = webview.create_window(
        f"{APP_NAME} — Splash",
        server_url(srv, "splash.html", theme=theme, lang=lang),
        width=SPLASH_SIZE[0],
        height=SPLASH_SIZE[1],
        frameless=True,
        resizable=False,
        hidden=True,
        on_top=True,
        background_color=bg_hex,
    )
    main_win = webview.create_window(
        APP_NAME,
        server_url(srv, "index.html", theme=theme, lang=lang),
        width=MAIN_SIZE[0],
        height=MAIN_SIZE[1],
        min_size=MIN_SIZE,
        frameless=True,
        easy_drag=False,
        hidden=True,
        background_color="#0C0C0E" if theme == "dark" else "#F7F6F2",
    )
    controls.window = main_win
    bridge.attach(splash_win, main_win)

    try:
        webview.start(gui="edgechromium" if sys.platform == "win32" else None)
    finally:
        srv.shutdown()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
