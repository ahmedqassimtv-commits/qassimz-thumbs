// QASSIMZ Thumbs — format decoders.
// Turns design / RAW files into a Bitmap by sniffing their content:
//   PSD / PSB  → merged composite (8/16-bit, RGB / Gray / CMYK / Indexed), fallback: embedded JPEG thumbnail
//   AI / INDD  → XMP embedded JPEG thumbnail
//   EPS        → DOS-EPS TIFF preview, fallback: XMP
//   ZIP based  → XD, Sketch, Krita, OpenRaster, Procreate, MOGRT, CorelDRAW … (preview.png / thumb.png / mergedimage.png)
//   anything   → largest embedded JPEG (RAW photos: CR2/CR3/NEF/ARW/DNG/RAF/ORF/RW2) or embedded PNG (Affinity …)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Qassimz.Thumbs
{
    public static class Extractor
    {
        /// <summary>Extensions handled by this extractor (also registered for Explorer thumbnails).</summary>
        public static readonly string[] DesignExtensions =
        {
            ".psd", ".psb", ".psdt", ".ai", ".ait", ".eps", ".epsf", ".indd", ".indt", ".idml",
            ".xd", ".sketch", ".kra", ".ora", ".procreate", ".mogrt", ".aegraphic",
            ".afdesign", ".afphoto", ".afpub", ".cdr", ".clip"
        };

        public static readonly string[] RawExtensions =
        {
            ".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".dng", ".raf", ".orf", ".rw2", ".pef", ".srw", ".x3f", ".3fr", ".iiq", ".rwl"
        };

        public static bool Handles(string ext)
        {
            ext = (ext ?? "").ToLowerInvariant();
            return Array.IndexOf(DesignExtensions, ext) >= 0 || Array.IndexOf(RawExtensions, ext) >= 0;
        }

        // ------------------------------------------------------------------ entry points
        public static Bitmap FromFile(string path, int size)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
            {
                return FromStream(fs, size);
            }
        }

        public static Bitmap FromStream(Stream s, int size)
        {
            byte[] head = new byte[16];
            int n = ReadFully(s, head, 0, head.Length);
            s.Position = 0;
            if (n < 4) return null;

            Bitmap result = null;
            try
            {
                if (head[0] == '8' && head[1] == 'B' && head[2] == 'P' && head[3] == 'S')
                    result = Psd.Decode(s, size);
                else if (head[0] == 'P' && head[1] == 'K' && head[2] == 3 && head[3] == 4)
                    result = FromZip(s);
                else if (head[0] == 0xC5 && head[1] == 0xD0 && head[2] == 0xD3 && head[3] == 0xC6)
                    result = FromDosEps(s);
            }
            catch { result = null; }

            if (result == null)
            {
                // generic: XMP thumbnail → embedded JPEG (largest) → embedded PNG
                byte[] all = ReadAll(s, 400L * 1024 * 1024);
                result = FromXmp(all) ?? LargestEmbeddedJpeg(all) ?? FirstEmbeddedPng(all);
            }
            return result == null ? null : Fit(result, size);
        }

        // ------------------------------------------------------------------ helpers
        internal static int ReadFully(Stream s, byte[] buf, int off, int count)
        {
            int total = 0;
            while (total < count)
            {
                int r = s.Read(buf, off + total, count - total);
                if (r <= 0) break;
                total += r;
            }
            return total;
        }

        internal static byte[] ReadAll(Stream s, long cap)
        {
            s.Position = 0;
            if (s is MemoryStream ms0 && ms0.Length <= cap) return ms0.ToArray();
            long len = s.CanSeek ? Math.Min(s.Length, cap) : cap;
            using (var ms = new MemoryStream((int)Math.Min(len, int.MaxValue / 2)))
            {
                byte[] buf = new byte[1 << 20];
                long left = len;
                int r;
                while (left > 0 && (r = s.Read(buf, 0, (int)Math.Min(buf.Length, left))) > 0)
                {
                    ms.Write(buf, 0, r);
                    left -= r;
                }
                return ms.ToArray();
            }
        }

        internal static int IndexOf(byte[] hay, byte[] needle, int start, int end = -1)
        {
            if (end < 0 || end > hay.Length) end = hay.Length;
            int last = end - needle.Length;
            byte f = needle[0];
            for (int i = Math.Max(0, start); i <= last; i++)
            {
                if (hay[i] != f) continue;
                int j = 1;
                while (j < needle.Length && hay[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }

        static Bitmap TryDecode(byte[] data, int off, int len)
        {
            try
            {
                using (var ms = new MemoryStream(data, off, len, false))
                using (var img = Image.FromStream(ms, false, false))
                {
                    return new Bitmap(img);
                }
            }
            catch { return null; }
        }

        static Bitmap TryDecode(byte[] data) => data == null ? null : TryDecode(data, 0, data.Length);

        /// <summary>Scale down (never up) to fit in size×size, high quality, 32bpp ARGB.</summary>
        public static Bitmap Fit(Bitmap src, int size)
        {
            if (src == null) return null;
            if (size <= 0) size = 256;
            int w = src.Width, h = src.Height;
            double k = Math.Min(1.0, Math.Min((double)size / w, (double)size / h));
            int nw = Math.Max(1, (int)Math.Round(w * k)), nh = Math.Max(1, (int)Math.Round(h * k));
            if (nw == w && nh == h && src.PixelFormat == PixelFormat.Format32bppArgb) return src;
            var dst = new Bitmap(nw, nh, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                using (var ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(src, new Rectangle(0, 0, nw, nh), 0, 0, w, h, GraphicsUnit.Pixel, ia);
                }
            }
            src.Dispose();
            return dst;
        }

        // ------------------------------------------------------------------ XMP (AI, INDD, EPS, …)
        static readonly byte[] XmpTag1 = Encoding.ASCII.GetBytes("xmpGImg:image>");
        static readonly byte[] XmpTag2 = Encoding.ASCII.GetBytes("xapGImg:image>");

        public static Bitmap FromXmp(byte[] all)
        {
            Bitmap best = null;
            foreach (var tag in new[] { XmpTag1, XmpTag2 })
            {
                int pos = 0;
                for (int guard = 0; guard < 6; guard++)
                {
                    int i = IndexOf(all, tag, pos);
                    if (i < 0) break;
                    int start = i + tag.Length;
                    // closing tag starts with '<'
                    int end = start;
                    while (end < all.Length && all[end] != (byte)'<') end++;
                    pos = end;
                    if (end - start < 100) continue; // this was the closing tag
                    string b64 = Encoding.ASCII.GetString(all, start, end - start)
                        .Replace("&#xA;", "").Replace("&#xa;", "").Replace("&#10;", "")
                        .Replace("\r", "").Replace("\n", "").Replace(" ", "").Replace("\t", "");
                    try
                    {
                        var bmp = TryDecode(Convert.FromBase64String(b64));
                        if (bmp != null && (best == null || bmp.Width * bmp.Height > best.Width * best.Height))
                        {
                            best?.Dispose();
                            best = bmp;
                        }
                        else bmp?.Dispose();
                    }
                    catch { }
                }
            }
            return best;
        }

        // ------------------------------------------------------------------ DOS EPS
        static Bitmap FromDosEps(Stream s)
        {
            byte[] h = new byte[30];
            if (ReadFully(s, h, 0, 30) < 30) return null;
            uint wmfOff = BitConverter.ToUInt32(h, 12), wmfLen = BitConverter.ToUInt32(h, 16);
            uint tifOff = BitConverter.ToUInt32(h, 20), tifLen = BitConverter.ToUInt32(h, 24);
            foreach (var (off, len) in new[] { (tifOff, tifLen), (wmfOff, wmfLen) })
            {
                if (off == 0 || len == 0 || off + len > s.Length) continue;
                byte[] buf = new byte[len];
                s.Position = off;
                ReadFully(s, buf, 0, (int)len);
                var bmp = TryDecode(buf);
                if (bmp != null) return bmp;
            }
            return null;
        }

        // ------------------------------------------------------------------ ZIP containers
        static readonly string[] ZipPreferred =
        {
            "mergedimage.png",                 // Krita, OpenRaster
            "preview.png", "previews/preview.png",   // Sketch, XD
            "quicklook/thumbnail.png", "quicklook/preview.png", // Procreate, Pages…
            "thumbnails/thumbnail.png",        // OpenRaster
            "thumb.png", "thumbnail.png", "thumbnail.jpg", "preview.jpg",
            "metadata/thumbnails/thumbnail.png", "metadata/thumbnails/thumbnail.bmp", // CorelDRAW
            "docProps/thumbnail.jpeg", "docProps/thumbnail.jpg"
        };

        static Bitmap FromZip(Stream s)
        {
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read, true))
            {
                var entries = zip.Entries.Where(e => e.Length > 0 && e.Length < 200L * 1024 * 1024).ToList();
                var byName = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in entries) byName[e.FullName.Replace('\\', '/')] = e;

                IEnumerable<ZipArchiveEntry> candidates = ZipPreferred.Where(byName.ContainsKey).Select(k => byName[k]);
                // then any image whose name says thumb / preview / poster, largest first
                candidates = candidates.Concat(entries
                    .Where(e => IsImageName(e.FullName) && (e.FullName.IndexOf("thumb", StringComparison.OrdinalIgnoreCase) >= 0
                                                          || e.FullName.IndexOf("preview", StringComparison.OrdinalIgnoreCase) >= 0
                                                          || e.FullName.IndexOf("poster", StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderByDescending(e => e.Length));
                // last resort: biggest image inside
                candidates = candidates.Concat(entries.Where(e => IsImageName(e.FullName)).OrderByDescending(e => e.Length).Take(3));

                foreach (var e in candidates.Take(12))
                {
                    try
                    {
                        using (var es = e.Open())
                        using (var ms = new MemoryStream())
                        {
                            es.CopyTo(ms);
                            var bmp = TryDecode(ms.ToArray());
                            if (bmp != null) return bmp;
                        }
                    }
                    catch { }
                }
            }
            return null;
        }

        static bool IsImageName(string n)
        {
            n = n.ToLowerInvariant();
            return n.EndsWith(".png") || n.EndsWith(".jpg") || n.EndsWith(".jpeg") || n.EndsWith(".bmp") || n.EndsWith(".gif");
        }

        // ------------------------------------------------------------------ embedded JPEG / PNG
        static readonly byte[] JpegSoi = { 0xFF, 0xD8, 0xFF };
        static readonly byte[] PngSig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public static Bitmap LargestEmbeddedJpeg(byte[] all)
        {
            Bitmap best = null;
            int pos = 0, tries = 0;
            while (tries < 24)
            {
                int i = IndexOf(all, JpegSoi, pos);
                if (i < 0) break;
                pos = i + 3;
                byte m = all.Length > i + 3 ? all[i + 3] : (byte)0;
                if (m < 0xC0 || m == 0xFF) continue; // must be a real marker
                tries++;
                // quick size probe (SOF) to avoid decoding tiny thumbnails twice
                var bmp = TryDecode(all, i, all.Length - i);
                if (bmp == null) continue;
                if (best == null || bmp.Width * bmp.Height > best.Width * best.Height)
                {
                    best?.Dispose();
                    best = bmp;
                    if (best.Width >= 1600) break; // good enough (full preview)
                }
                else bmp.Dispose();
            }
            return best;
        }

        public static Bitmap FirstEmbeddedPng(byte[] all)
        {
            int pos = 0;
            for (int t = 0; t < 8; t++)
            {
                int i = IndexOf(all, PngSig, pos);
                if (i < 0) break;
                pos = i + 8;
                var bmp = TryDecode(all, i, all.Length - i);
                if (bmp != null && bmp.Width >= 32) return bmp;
                bmp?.Dispose();
            }
            return null;
        }
    }

    // ======================================================================== PSD / PSB
    internal static class Psd
    {
        sealed class BE
        {
            readonly Stream s;
            readonly byte[] b = new byte[8];
            public BE(Stream s) { this.s = s; }
            public Stream S => s;
            public long Pos { get => s.Position; set => s.Position = value; }
            public long Length => s.Length;
            void Fill(int n) { if (Extractor.ReadFully(s, b, 0, n) < n) throw new EndOfStreamException(); }
            public ushort U16() { Fill(2); return (ushort)(b[0] << 8 | b[1]); }
            public short S16() { return (short)U16(); }
            public uint U32() { Fill(4); return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]); }
            public ulong U64() { ulong hi = U32(); return hi << 32 | U32(); }
            public byte U8() { Fill(1); return b[0]; }
            public byte[] Bytes(int n) { var r = new byte[n]; if (Extractor.ReadFully(s, r, 0, n) < n) throw new EndOfStreamException(); return r; }
            public void Skip(long n) { s.Position += n; }
        }

        public static Bitmap Decode(Stream stream, int size)
        {
            var r = new BE(stream);
            r.Skip(4);
            int version = r.U16();
            bool psb = version == 2;
            r.Skip(6);
            int channels = r.U16();
            int height = (int)r.U32();
            int width = (int)r.U32();
            int depth = r.U16();
            int mode = r.U16();

            // color mode data (palette for indexed)
            uint cmLen = r.U32();
            byte[] palette = null;
            if (mode == 2 && cmLen >= 768) { palette = r.Bytes(768); r.Skip(cmLen - 768); }
            else r.Skip(cmLen);

            // image resources → embedded JPEG thumbnail (1036 / 1033)
            uint resLen = r.U32();
            long resEnd = r.Pos + resLen;
            byte[] thumbJpeg = null;
            bool thumbBgr = false;
            while (r.Pos + 12 <= resEnd)
            {
                if (r.U32() != 0x3842494D) break; // '8BIM'
                int id = r.U16();
                int nameLen = r.U8();
                r.Skip(nameLen + ((nameLen + 1) % 2));
                uint sz = r.U32();
                long next = r.Pos + sz + (sz % 2);
                if ((id == 1036 || id == 1033) && sz > 28)
                {
                    r.Skip(28);
                    thumbJpeg = r.Bytes((int)sz - 28);
                    thumbBgr = id == 1033;
                }
                r.Pos = next;
            }
            r.Pos = resEnd;

            // layer & mask info: negative layer count ⇒ merged result has transparency
            long lmLen = psb ? (long)r.U64() : r.U32();
            long lmEnd = r.Pos + lmLen;
            bool mergedAlpha = false;
            if (lmLen > 4)
            {
                long liLen = psb ? (long)r.U64() : r.U32();
                if (liLen > 2) mergedAlpha = r.S16() < 0;
            }
            r.Pos = lmEnd;

            Bitmap composite = null;
            try
            {
                if (width > 0 && height > 0 && (depth == 8 || depth == 16) && r.Pos < r.Length - 2)
                    composite = DecodeComposite(r, psb, width, height, channels, depth, mode, palette, mergedAlpha, size);
            }
            catch { composite = null; }

            if (composite != null && !LooksBlank(composite)) return composite;

            if (thumbJpeg != null)
            {
                try
                {
                    using (var ms = new MemoryStream(thumbJpeg))
                    using (var img = Image.FromStream(ms))
                    {
                        var bmp = new Bitmap(img);
                        if (thumbBgr) SwapRB(bmp);
                        composite?.Dispose();
                        return bmp;
                    }
                }
                catch { }
            }
            return composite;
        }

        static Bitmap DecodeComposite(BE r, bool psb, int width, int height, int channels, int depth, int mode,
                                      byte[] palette, bool mergedAlpha, int size)
        {
            int compression = r.U16();
            if (compression > 1) return null; // zip-compressed composite: rare → use thumbnail

            int bpc = depth / 8;
            int rowBytes = width * bpc;

            // which channels do we need?
            int colorCh = mode == 4 ? 4 : (mode == 3 ? 3 : 1);
            if (channels < colorCh) return null;
            bool alpha = mergedAlpha && channels > colorCh;
            int need = colorCh + (alpha ? 1 : 0);

            // sampling step so big canvases decode fast
            int target = Math.Max(64, size) * 2;
            int step = Math.Max(1, Math.Max(width, height) / target);
            int ow = (width + step - 1) / step, oh = (height + step - 1) / step;

            long dataStart = r.Pos;
            long[] rowOff = null; // file offset of each (channel,row) for RLE
            int[] rowLen = null;
            if (compression == 1)
            {
                int rows = channels * height;
                rowOff = new long[rows];
                rowLen = new int[rows];
                for (int i = 0; i < rows; i++) rowLen[i] = psb ? (int)r.U32() : r.U16();
                long p = r.Pos;
                for (int i = 0; i < rows; i++) { rowOff[i] = p; p += rowLen[i]; }
            }

            // plane[c][y*ow + x]
            var planes = new byte[need][];
            for (int c = 0; c < need; c++) planes[c] = new byte[ow * oh];

            byte[] row = new byte[rowBytes];
            byte[] packed = new byte[Math.Max(16, rowBytes * 2 + 16)];
            for (int c = 0; c < need; c++)
            {
                for (int oy = 0; oy < oh; oy++)
                {
                    int y = oy * step;
                    int idx = c * height + y;
                    if (compression == 0)
                    {
                        r.Pos = dataStart + (long)idx * rowBytes;
                        if (Extractor.ReadFully(GetStream(r), row, 0, rowBytes) < rowBytes) return null;
                    }
                    else
                    {
                        int len = rowLen[idx];
                        if (len > packed.Length) packed = new byte[len];
                        r.Pos = rowOff[idx];
                        if (Extractor.ReadFully(GetStream(r), packed, 0, len) < len) return null;
                        UnpackBits(packed, len, row, rowBytes);
                    }
                    byte[] plane = planes[c];
                    int o = oy * ow;
                    if (bpc == 1)
                        for (int ox = 0, x = 0; ox < ow; ox++, x += step) plane[o + ox] = row[x];
                    else
                        for (int ox = 0, x = 0; ox < ow; ox++, x += step) plane[o + ox] = row[x * 2]; // high byte of BE u16
                }
            }

            // compose ARGB
            var bmp = new Bitmap(ow, oh, PixelFormat.Format32bppArgb);
            var bd = bmp.LockBits(new Rectangle(0, 0, ow, oh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            byte[] px = new byte[ow * 4];
            for (int y = 0; y < oh; y++)
            {
                int o = y * ow;
                for (int x = 0; x < ow; x++)
                {
                    byte R, G, B, A = 255;
                    int i = o + x;
                    switch (mode)
                    {
                        case 3: R = planes[0][i]; G = planes[1][i]; B = planes[2][i]; break;
                        case 4:
                            {
                                // PSD stores CMYK inverted (0 = full ink)
                                int C = 255 - planes[0][i], M = 255 - planes[1][i], Y = 255 - planes[2][i], K = 255 - planes[3][i];
                                R = (byte)((255 - C) * (255 - K) / 255);
                                G = (byte)((255 - M) * (255 - K) / 255);
                                B = (byte)((255 - Y) * (255 - K) / 255);
                                break;
                            }
                        case 2:
                            {
                                int v = planes[0][i];
                                if (palette != null) { R = palette[v]; G = palette[256 + v]; B = palette[512 + v]; }
                                else R = G = B = (byte)v;
                                break;
                            }
                        default: R = G = B = planes[0][i]; break; // grayscale, duotone
                    }
                    if (alpha) A = planes[colorCh][i];
                    px[x * 4] = B; px[x * 4 + 1] = G; px[x * 4 + 2] = R; px[x * 4 + 3] = A;
                }
                Marshal.Copy(px, 0, bd.Scan0 + y * bd.Stride, ow * 4);
            }
            bmp.UnlockBits(bd);
            return bmp;
        }

        static Stream GetStream(BE r) => r.S;

        static void UnpackBits(byte[] src, int srcLen, byte[] dst, int dstLen)
        {
            int i = 0, o = 0;
            while (i < srcLen && o < dstLen)
            {
                int n = (sbyte)src[i++];
                if (n >= 0)
                {
                    int cnt = Math.Min(n + 1, Math.Min(srcLen - i, dstLen - o));
                    Buffer.BlockCopy(src, i, dst, o, cnt);
                    i += n + 1; o += cnt;
                }
                else if (n != -128)
                {
                    int cnt = Math.Min(1 - n, dstLen - o);
                    if (i >= srcLen) break;
                    byte v = src[i++];
                    for (int k = 0; k < cnt; k++) dst[o++] = v;
                }
            }
            while (o < dstLen) dst[o++] = 0;
        }

        static bool LooksBlank(Bitmap b)
        {
            // "Maximize compatibility" off ⇒ Photoshop writes an all-white composite
            int sx = Math.Max(1, b.Width / 16), sy = Math.Max(1, b.Height / 16);
            Color first = b.GetPixel(0, 0);
            for (int y = 0; y < b.Height; y += sy)
                for (int x = 0; x < b.Width; x += sx)
                    if (b.GetPixel(x, y) != first) return false;
            return first.A == 0 || (first.R == 255 && first.G == 255 && first.B == 255);
        }

        static void SwapRB(Bitmap b)
        {
            var bd = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            int bytes = bd.Stride * b.Height;
            byte[] px = new byte[bytes];
            Marshal.Copy(bd.Scan0, px, 0, bytes);
            for (int i = 0; i + 3 < bytes; i += 4) { byte t = px[i]; px[i] = px[i + 2]; px[i + 2] = t; }
            Marshal.Copy(px, 0, bd.Scan0, bytes);
            b.UnlockBits(bd);
        }
    }
}
