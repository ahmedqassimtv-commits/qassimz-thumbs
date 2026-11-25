// Thumbnail service for the viewer: our decoders for design/RAW files, GDI+ for common images,
// and the Windows shell (IShellItemImageFactory) for everything else (videos, WebP, HEIC, PDF, apps…).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Qassimz.Thumbs.Viewer
{
    internal static class Thumbs
    {
        static readonly HashSet<string> Gdi = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".gif", ".tif", ".tiff", ".ico" };

        public static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QASSIMZ Thumbs", "cache");

        // ---------------------------------------------------------------- STA worker pool
        static readonly BlockingCollection<Action> Queue = new BlockingCollection<Action>();

        static Thumbs()
        {
            Directory.CreateDirectory(CacheDir);
            int n = Math.Max(2, Math.Min(6, Environment.ProcessorCount - 1));
            for (int i = 0; i < n; i++)
            {
                var t = new Thread(() => { foreach (var a in Queue.GetConsumingEnumerable()) { try { a(); } catch { } } })
                { IsBackground = true, Name = "thumb-" + i };
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
            }
        }

        public static Task<byte[]> GetPngAsync(string path, int size)
        {
            var tcs = new TaskCompletionSource<byte[]>();
            Queue.Add(() =>
            {
                try { tcs.SetResult(GetPng(path, size)); }
                catch (Exception e) { tcs.SetException(e); }
            });
            return tcs.Task;
        }

        public static byte[] GetPng(string path, int size)
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return null;
            string key = Hash(path.ToLowerInvariant() + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks + "|" + size);
            string cached = Path.Combine(CacheDir, key + ".png");
            if (size <= 640 && File.Exists(cached)) return File.ReadAllBytes(cached);

            using (var bmp = Render(path, size))
            {
                if (bmp == null) return null;
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    var bytes = ms.ToArray();
                    if (size <= 640) { try { File.WriteAllBytes(cached, bytes); } catch { } }
                    return bytes;
                }
            }
        }

        public static Bitmap Render(string path, int size)
        {
            string ext = Path.GetExtension(path);
            Bitmap b = null;
            if (Extractor.Handles(ext))
            {
                try { b = Extractor.FromFile(path, size); } catch { b = null; }
            }
            else if (Gdi.Contains(ext))
            {
                try { b = LoadGdi(path, size); } catch { b = null; }
            }
            return b ?? Shell(path, size);
        }

        static Bitmap LoadGdi(string path, int size)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var img = Image.FromStream(fs, false, false))
            {
                // EXIF orientation
                const int OrientationId = 0x0112;
                if (Array.IndexOf(img.PropertyIdList, OrientationId) >= 0)
                {
                    var rot = img.GetPropertyItem(OrientationId).Value[0];
                    var rf = rot == 3 ? RotateFlipType.Rotate180FlipNone : rot == 6 ? RotateFlipType.Rotate90FlipNone
                           : rot == 8 ? RotateFlipType.Rotate270FlipNone : RotateFlipType.RotateNoneFlipNone;
                    if (rf != RotateFlipType.RotateNoneFlipNone) img.RotateFlip(rf);
                }
                return Extractor.Fit(new Bitmap(img), size);
            }
        }

        static string Hash(string s)
        {
            using (var sha = SHA1.Create())
            {
                var h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder();
                foreach (var x in h) sb.Append(x.ToString("x2"));
                return sb.ToString();
            }
        }

        public static long CacheSize()
        {
            long t = 0;
            try { foreach (var f in new DirectoryInfo(CacheDir).EnumerateFiles()) t += f.Length; } catch { }
            return t;
        }

        public static void ClearCache()
        {
            try { foreach (var f in new DirectoryInfo(CacheDir).EnumerateFiles()) { try { f.Delete(); } catch { } } } catch { }
        }

        // ---------------------------------------------------------------- Windows shell thumbnails
        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int cx, cy; }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public short bmPlanes, bmBitsPixel; public IntPtr bmBits; }

        [StructLayout(LayoutKind.Sequential)]
        struct DIBSECTION
        {
            public BITMAP dsBm;
            public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
            public int dsBitfields0, dsBitfields1, dsBitfields2; public IntPtr dshSection; public int dsOffset;
        }

        [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int c, out DIBSECTION ds);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

        public static Bitmap Shell(string path, int size)
        {
            IShellItemImageFactory f = null;
            IntPtr hbm = IntPtr.Zero;
            try
            {
                SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out f);
                // 0 = RESIZETOFIT (thumbnail if any, otherwise the file's icon)
                if (f.GetImage(new SIZE { cx = size, cy = size }, 0, out hbm) != 0 || hbm == IntPtr.Zero) return null;
                return FromHBitmap(hbm);
            }
            catch { return null; }
            finally
            {
                if (hbm != IntPtr.Zero) DeleteObject(hbm);
                if (f != null) Marshal.ReleaseComObject(f);
            }
        }

        static Bitmap FromHBitmap(IntPtr hbm)
        {
            if (GetObject(hbm, Marshal.SizeOf(typeof(DIBSECTION)), out DIBSECTION ds) == 0 || ds.dsBm.bmBits == IntPtr.Zero || ds.dsBm.bmBitsPixel != 32)
            {
                using (var img = Image.FromHbitmap(hbm)) return new Bitmap(img);
            }
            int w = ds.dsBm.bmWidth, h = ds.dsBm.bmHeight, stride = ds.dsBm.bmWidthBytes;
            bool bottomUp = ds.biHeight > 0;
            var dst = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            var bd = dst.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            byte[] row = new byte[w * 4];
            bool anyAlpha = false;
            for (int y = 0; y < h; y++)
            {
                int sy = bottomUp ? h - 1 - y : y;
                Marshal.Copy(ds.dsBm.bmBits + sy * stride, row, 0, row.Length);
                for (int i = 3; i < row.Length && !anyAlpha; i += 4) if (row[i] != 0) anyAlpha = true;
                Marshal.Copy(row, 0, bd.Scan0 + y * bd.Stride, row.Length);
            }
            if (!anyAlpha) // opaque bitmap without alpha channel → force alpha = 255
            {
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(bd.Scan0 + y * bd.Stride, row, 0, row.Length);
                    for (int i = 3; i < row.Length; i += 4) row[i] = 255;
                    Marshal.Copy(row, 0, bd.Scan0 + y * bd.Stride, row.Length);
                }
            }
            dst.UnlockBits(bd);
            var argb = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(argb)) g.DrawImageUnscaled(dst, 0, 0);
            dst.Dispose();
            return argb;
        }

        // ---------------------------------------------------------------- details
        public static Dictionary<string, object> Details(string path)
        {
            var d = new Dictionary<string, object>();
            var fi = new FileInfo(path);
            d["name"] = fi.Name;
            d["path"] = fi.FullName;
            d["size"] = fi.Length;
            d["modified"] = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
            d["created"] = fi.CreationTime.ToString("yyyy-MM-dd HH:mm");
            d["ext"] = fi.Extension.TrimStart('.').ToUpperInvariant();
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    byte[] h = new byte[26];
                    if (Extractor.ReadFully(fs, h, 0, 26) == 26 && h[0] == '8' && h[1] == 'B' && h[2] == 'P' && h[3] == 'S')
                    {
                        int ch = h[12] << 8 | h[13];
                        int height = h[14] << 24 | h[15] << 16 | h[16] << 8 | h[17];
                        int width = h[18] << 24 | h[19] << 16 | h[20] << 8 | h[21];
                        int depth = h[22] << 8 | h[23], mode = h[24] << 8 | h[25];
                        d["width"] = width; d["height"] = height; d["depth"] = depth;
                        d["mode"] = mode == 3 ? "RGB" : mode == 4 ? "CMYK" : mode == 1 ? "Grayscale" : mode == 2 ? "Indexed" : mode == 9 ? "Lab" : "—";
                        d["channels"] = ch;
                    }
                    else if (Gdi.Contains(fi.Extension))
                    {
                        fs.Position = 0;
                        using (var img = Image.FromStream(fs, false, false)) { d["width"] = img.Width; d["height"] = img.Height; }
                    }
                }
            }
            catch { }
            return d;
        }
    }
}
