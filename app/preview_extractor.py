"""Fast embedded-preview thumbnail extractor for Quick Viewer.

Extracts high-resolution embedded previews without full file decoding:
- PSD / PSB / PSDT / PDD: reads 8BIM resource 1036/1033 JPEG thumbnail directly from header
- AI / EPS: extracts XMP xmpGImg:image / xapGImg:image base64 preview or DOS-EPS preview
- MOGRT / AEGRAPHIC: reads thumb.png / thumbnail.png / preview.png from ZIP container
- RAW (CR3, CR2, ARW, NEF, DNG, ...): extracts largest embedded JPEG preview (FF D8 FF ... FF D9)
- SVG: renders clean vector data URL with parsed dimensions & alpha flag
- WEBP / HEIC / PNG / JPG / GIF / BMP: direct fast preview with dimensions & alpha detection
"""
from __future__ import annotations

import base64
import io
import re
import struct
import time
import zipfile
from pathlib import Path
from typing import Any

try:
    from PIL import Image  # type: ignore[import-not-found]
except ImportError:
    Image = None  # type: ignore[assignment]

MAX_READ_BYTES = 64 * 1024 * 1024  # 64 MB cap for fast embedded preview scan

_ZIP_THUMB_CANDIDATES = (
    "thumb.png",
    "thumbnail.png",
    "preview.png",
    "previews/preview.png",
    "mergedimage.png",
    "quicklook/thumbnail.png",
    "thumbnails/thumbnail.png",
    "thumb.jpg",
    "thumbnail.jpg",
    "preview.jpg",
)


def _mime_for_bytes(data: bytes) -> str:
    if data.startswith(b"\x89PNG\r\n\x1a\n"):
        return "image/png"
    if data.startswith(b"\xff\xd8\xff"):
        return "image/jpeg"
    if data[:4] == b"RIFF" and data[8:12] == b"WEBP":
        return "image/webp"
    if data.startswith((b"GIF87a", b"GIF89a")):
        return "image/gif"
    if data.startswith(b"BM"):
        return "image/bmp"
    return "application/octet-stream"


def _probe_image_meta(data: bytes) -> tuple[int, int, bool]:
    """Return (width, height, has_alpha) for PNG/JPEG/WEBP/GIF bytes."""
    if Image is not None:
        try:
            with Image.open(io.BytesIO(data)) as im:
                w, h = im.size
                has_alpha = im.mode in ("RGBA", "LA", "PA") or (
                    im.mode == "P" and "transparency" in im.info
                )
                return int(w), int(h), bool(has_alpha)
        except Exception:
            pass

    # Pure-Python fast header probes (zero dependency fallback)
    if data.startswith(b"\x89PNG\r\n\x1a\n") and len(data) >= 26:
        w, h = struct.unpack(">II", data[16:24])
        color_type = data[25]
        has_alpha = color_type in (4, 6) or (b"tRNS" in data[:1024])
        return w, h, has_alpha

    if data.startswith(b"\xff\xd8\xff"):
        i = 2
        n = len(data)
        while i + 9 < n:
            if data[i] != 0xFF:
                i += 1
                continue
            marker = data[i + 1]
            if marker in (0xC0, 0xC1, 0xC2):
                h, w = struct.unpack(">HH", data[i + 5 : i + 9])
                return w, h, False
            if marker in (0xD8, 0xD9) or (0xD0 <= marker <= 0xD7):
                i += 2
            else:
                if i + 4 > n:
                    break
                seg_len = struct.unpack(">H", data[i + 2 : i + 4])[0]
                if seg_len < 2:
                    break
                i += 2 + seg_len
        return 0, 0, False

    return 0, 0, False


def _extract_psd_header_thumb(data: bytes) -> tuple[bytes | None, int, int, str]:
    """Read Photoshop PSD/PSB/PSDT/PDD header & 8BIM 1036/1033 JPEG thumbnail."""
    if len(data) < 26 or data[:4] != b"8BPS":
        return None, 0, 0, "psd_invalid"
    version = struct.unpack(">H", data[4:6])[0]
    if version not in (1, 2):
        return None, 0, 0, "psd_version"
    height, width = struct.unpack(">II", data[14:22])
    pos = 26
    if pos + 4 > len(data):
        return None, width, height, "psd_eof"
    cm_len = struct.unpack(">I", data[pos : pos + 4])[0]
    pos += 4 + cm_len
    if pos + 4 > len(data):
        return None, width, height, "psd_eof"
    res_len = struct.unpack(">I", data[pos : pos + 4])[0]
    pos += 4
    res_end = min(len(data), pos + res_len)
    while pos + 12 <= res_end:
        if data[pos : pos + 4] != b"8BIM":
            break
        res_id = struct.unpack(">H", data[pos + 4 : pos + 6])[0]
        name_len = data[pos + 6]
        name_padded = name_len + 1 + ((name_len + 1) % 2)
        size_off = pos + 6 + name_padded
        if size_off + 4 > res_end:
            break
        sz = struct.unpack(">I", data[size_off : size_off + 4])[0]
        body_start = size_off + 4
        body_end = body_start + sz
        if res_id in (1036, 1033) and sz > 28 and body_end <= len(data):
            jpeg = data[body_start + 28 : body_end]
            if jpeg.startswith(b"\xff\xd8\xff"):
                return jpeg, width, height, "psd_8bim_header"
        pos = body_end + (sz % 2)
    return None, width, height, "psd_no_8bim"


def _extract_xmp_thumb(data: bytes) -> bytes | None:
    for tag in (b"xmpGImg:image>", b"xapGImg:image>"):
        idx = data.find(tag)
        if idx < 0:
            continue
        start = idx + len(tag)
        end = data.find(b"<", start)
        if end < 0 or end - start < 64:
            continue
        raw_b64 = (
            data[start:end]
            .replace(b"&#xA;", b"")
            .replace(b"&#xa;", b"")
            .replace(b"&#10;", b"")
            .replace(b"\r", b"")
            .replace(b"\n", b"")
            .replace(b" ", b"")
            .replace(b"\t", b"")
        )
        try:
            decoded = base64.b64decode(raw_b64, validate=False)
            if decoded.startswith((b"\xff\xd8\xff", b"\x89PNG\r\n\x1a\n")):
                return decoded
        except Exception:
            continue
    return None


def _extract_zip_thumb(data: bytes) -> bytes | None:
    try:
        with zipfile.ZipFile(io.BytesIO(data)) as zf:
            names_map = {n.replace("\\", "/").lower(): n for n in zf.namelist()}
            for cand in _ZIP_THUMB_CANDIDATES:
                if cand in names_map:
                    payload = zf.read(names_map[cand])
                    if len(payload) > 32:
                        return payload
            # Fallback: largest thumb/preview/poster image inside archive
            imgs = [
                info
                for info in zf.infolist()
                if info.file_size > 32
                and info.filename.lower().endswith((".png", ".jpg", ".jpeg", ".webp"))
            ]
            imgs.sort(
                key=lambda x: (
                    any(k in x.filename.lower() for k in ("thumb", "preview", "poster")),
                    x.file_size,
                ),
                reverse=True,
            )
            if imgs:
                return zf.read(imgs[0].filename)
    except Exception:
        return None
    return None


def _extract_largest_jpeg(data: bytes) -> bytes | None:
    best: bytes | None = None
    pos = 0
    tries = 0
    n = len(data)
    while tries < 24:
        i = data.find(b"\xff\xd8\xff", pos)
        if i < 0 or i + 4 >= n:
            break
        pos = i + 3
        marker = data[i + 3]
        if marker < 0xC0 or marker == 0xFF:
            continue
        tries += 1
        eoi = data.find(b"\xff\xd9", i + 4)
        chunk = data[i : eoi + 2] if eoi > i else data[i : min(n, i + 4 * 1024 * 1024)]
        if len(chunk) > 256 and (best is None or len(chunk) > len(best)):
            best = chunk
    return best


def _parse_svg_dims(text: str) -> tuple[int, int]:
    vb = re.search(r'viewBox\s*=\s*["\']\s*[\d.-]+\s+[\d.-]+\s+([\d.]+)\s+([\d.]+)\s*["\']', text, re.I)
    if vb:
        return int(float(vb.group(1))), int(float(vb.group(2)))
    wm = re.search(r'\bwidth\s*=\s*["\']([\d.]+)', text, re.I)
    hm = re.search(r'\bheight\s*=\s*["\']([\d.]+)', text, re.I)
    if wm and hm:
        return int(float(wm.group(1))), int(float(hm.group(1)))
    return 512, 512


def extract_preview_bytes(filename: str, data: bytes, file_size: int | None = None) -> dict[str, Any]:
    """Extract an embedded preview from raw bytes and return telemetry + data URL."""
    t0 = time.perf_counter()
    ext = Path(filename).suffix.lower() or ""
    fmt = ext.lstrip(".").upper() or "FILE"
    total_size = file_size if file_size is not None else len(data)

    if not data:
        return {
            "ok": False,
            "error": "empty_file",
            "message_en": "The selected file is empty.",
            "message_ar": "الملف المحدد فارغ.",
        }

    # 1. SVG direct vector preview
    if ext == ".svg" or data.lstrip()[:64].lower().startswith((b"<svg", b"<?xml")):
        try:
            text = data.decode("utf-8", errors="ignore")
            if "<svg" in text.lower():
                w, h = _parse_svg_dims(text)
                b64 = base64.b64encode(data).decode("ascii")
                elapsed = round((time.perf_counter() - t0) * 1000.0, 2)
                return {
                    "ok": True,
                    "filename": Path(filename).name,
                    "format": "SVG",
                    "width": w,
                    "height": h,
                    "file_size": total_size,
                    "extract_ms": elapsed,
                    "has_alpha": True,
                    "method": "svg_vector",
                    "data_url": f"data:image/svg+xml;base64,{b64}",
                }
        except Exception:
            pass

    preview: bytes | None = None
    width = 0
    height = 0
    has_alpha = False
    method = "embedded_preview"

    # 2. Photoshop PSD / PSB / PSDT / PDD
    if data[:4] == b"8BPS" or ext in (".psd", ".psb", ".psdt", ".pdd"):
        thumb, pw, ph, method = _extract_psd_header_thumb(data)
        if thumb:
            preview = thumb
            tw, th, has_alpha = _probe_image_meta(thumb)
            width = pw or tw
            height = ph or th
        elif Image is not None:
            try:
                with Image.open(io.BytesIO(data)) as im:
                    width, height = im.size
                    has_alpha = im.mode in ("RGBA", "LA")
                    im.thumbnail((1024, 1024))
                    buf = io.BytesIO()
                    im.save(buf, format="PNG")
                    preview = buf.getvalue()
                    method = "psd_composite"
            except Exception:
                pass

    # 3. MOGRT / ZIP-based containers
    elif data[:4] == b"PK\x03\x04" or ext in (".mogrt", ".aegraphic", ".xd", ".sketch", ".kra", ".ora"):
        preview = _extract_zip_thumb(data)
        if preview:
            method = "zip_embedded_thumb"

    # 4. Standard raster images (PNG, JPEG, WEBP, GIF, BMP)
    elif _mime_for_bytes(data) != "application/octet-stream":
        preview = data
        method = "direct_raster"

    # 5. AI / EPS / RAW / HEIC embedded XMP or JPEG preview
    if preview is None:
        xmp = _extract_xmp_thumb(data)
        if xmp:
            preview = xmp
            method = "xmp_embedded"
        else:
            jpg = _extract_largest_jpeg(data)
            if jpg:
                preview = jpg
                method = "raw_embedded_jpeg"

    if preview is None and Image is not None:
        try:
            with Image.open(io.BytesIO(data)) as im:
                width, height = im.size
                has_alpha = im.mode in ("RGBA", "LA")
                im.thumbnail((1024, 1024))
                buf = io.BytesIO()
                im.save(buf, format="PNG")
                preview = buf.getvalue()
                method = "pillow_decode"
        except Exception:
            pass

    if preview is None:
        return {
            "ok": False,
            "error": "no_preview_found",
            "message_en": f"No embedded preview could be extracted from '{Path(filename).name}'.",
            "message_ar": f"لم يتم العثور على معاينة مدمجة داخل الملف '{Path(filename).name}'.",
        }

    pw2, ph2, alpha2 = _probe_image_meta(preview)
    if width == 0:
        width = pw2
    if height == 0:
        height = ph2
    has_alpha = has_alpha or alpha2
    mime = _mime_for_bytes(preview)
    if mime == "application/octet-stream":
        mime = "image/png"
    b64 = base64.b64encode(preview).decode("ascii")
    elapsed = round((time.perf_counter() - t0) * 1000.0, 2)

    return {
        "ok": True,
        "filename": Path(filename).name,
        "format": fmt,
        "width": width,
        "height": height,
        "file_size": total_size,
        "extract_ms": elapsed,
        "has_alpha": has_alpha,
        "method": method,
        "data_url": f"data:{mime};base64,{b64}",
    }


def extract_preview_file(path: str | Path) -> dict[str, Any]:
    target = Path(path)
    if not target.is_file():
        return {
            "ok": False,
            "error": "file_not_found",
            "message_en": f"File not found: {target}",
            "message_ar": f"الملف غير موجود: {target}",
        }
    total_size = target.stat().st_size
    with target.open("rb") as fh:
        data = fh.read(MAX_READ_BYTES)
    return extract_preview_bytes(target.name, data, file_size=total_size)
