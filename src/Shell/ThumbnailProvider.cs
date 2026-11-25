// QASSIMZ Thumbs — Windows Explorer thumbnail provider (IThumbnailProvider + IInitializeWithStream).
// Explorer runs it inside its isolated thumbnail process; registration is per-user (HKCU), no admin needed.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32;

namespace Qassimz.Thumbs
{
    public enum WTS_ALPHATYPE { WTSAT_UNKNOWN = 0, WTSAT_RGB = 1, WTSAT_ARGB = 2 }

    [ComImport, Guid("e357fccd-a995-4576-b01f-234630154e96"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IThumbnailProvider
    {
        void GetThumbnail(uint cx, out IntPtr phbmp, out WTS_ALPHATYPE pdwAlpha);
    }

    [ComImport, Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithStream
    {
        void Initialize(IStream pstream, uint grfMode);
    }

    [ComVisible(true)]
    [Guid(ThumbnailProvider.Clsid)]
    [ClassInterface(ClassInterfaceType.None)]
    [ProgId("Qassimz.ThumbnailProvider")]
    public sealed class ThumbnailProvider : IThumbnailProvider, IInitializeWithStream
    {
        public const string Clsid = "949B93CF-8658-43F7-9720-21A186C8A55A";
        const long MaxBytes = 1536L * 1024 * 1024; // don't read files bigger than 1.5 GB fully
        MemoryStream _data;

        public void Initialize(IStream pstream, uint grfMode)
        {
            try
            {
                pstream.Stat(out System.Runtime.InteropServices.ComTypes.STATSTG st, 1 /* STATFLAG_NONAME */);
                long len = Math.Min(st.cbSize, MaxBytes);
                var ms = new MemoryStream((int)Math.Min(len, int.MaxValue - 64));
                byte[] buf = new byte[1 << 20];
                IntPtr pRead = Marshal.AllocCoTaskMem(4);
                try
                {
                    long left = len;
                    while (left > 0)
                    {
                        int want = (int)Math.Min(buf.Length, left);
                        pstream.Read(buf, want, pRead);
                        int got = Marshal.ReadInt32(pRead);
                        if (got <= 0) break;
                        ms.Write(buf, 0, got);
                        left -= got;
                    }
                }
                finally { Marshal.FreeCoTaskMem(pRead); }
                ms.Position = 0;
                _data = ms;
            }
            finally
            {
                Marshal.ReleaseComObject(pstream);
            }
        }

        public void GetThumbnail(uint cx, out IntPtr phbmp, out WTS_ALPHATYPE pdwAlpha)
        {
            phbmp = IntPtr.Zero;
            pdwAlpha = WTS_ALPHATYPE.WTSAT_ARGB;
            if (_data == null) throw new COMException("not initialized", unchecked((int)0x80004005));
            Bitmap bmp = null;
            try
            {
                bmp = Extractor.FromStream(_data, (int)Math.Max(16, cx));
                if (bmp == null) throw new COMException("no preview", unchecked((int)0x80004005));
                phbmp = ToHBitmap(bmp);
            }
            finally
            {
                bmp?.Dispose();
                _data.Dispose();
                _data = null;
            }
        }

        // ---- 32bpp top-down DIB with straight alpha (what WTSAT_ARGB expects)
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage, out IntPtr bits, IntPtr hSection, uint offset);

        internal static IntPtr ToHBitmap(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            var bi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            IntPtr hbmp = CreateDIBSection(IntPtr.Zero, ref bi, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (hbmp == IntPtr.Zero) throw new COMException("CreateDIBSection failed");
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] row = new byte[w * 4];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(bd.Scan0 + y * bd.Stride, row, 0, row.Length);
                    Marshal.Copy(row, 0, bits + y * w * 4, row.Length);
                }
            }
            finally { bmp.UnlockBits(bd); }
            return hbmp;
        }
    }

    /// <summary>Per-user registration (HKCU) — used by the installer and the viewer's settings toggle.</summary>
    public static class Registration
    {
        const string ThumbHandlerKey = "{e357fccd-a995-4576-b01f-234630154e96}";
        static string ClsidKey => "{" + ThumbnailProvider.Clsid + "}";

        public static void Register(string dllPath, bool includeRaw)
        {
            var asm = typeof(ThumbnailProvider).Assembly;
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\CLSID\" + ClsidKey))
            {
                k.SetValue("", "QASSIMZ Thumbs");
                k.SetValue("DisableProcessIsolation", 0, RegistryValueKind.DWord);
                using (var ip = k.CreateSubKey("InprocServer32"))
                {
                    ip.SetValue("", "mscoree.dll");
                    ip.SetValue("ThreadingModel", "Both");
                    ip.SetValue("Class", typeof(ThumbnailProvider).FullName);
                    ip.SetValue("Assembly", asm.FullName);
                    ip.SetValue("RuntimeVersion", "v4.0.30319");
                    ip.SetValue("CodeBase", new Uri(dllPath).AbsoluteUri);
                }
                using (var cat = k.CreateSubKey(@"Implemented Categories\{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}")) { }
            }
            foreach (var ext in Extractor.DesignExtensions) SetExt(ext, true);
            foreach (var ext in Extractor.RawExtensions) SetExt(ext, includeRaw);
            Notify();
        }

        public static void Unregister()
        {
            foreach (var ext in Extractor.DesignExtensions) SetExt(ext, false);
            foreach (var ext in Extractor.RawExtensions) SetExt(ext, false);
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\CLSID\" + ClsidKey, false); } catch { }
            Notify();
        }

        public static bool IsRegistered()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.psd\ShellEx\" + ThumbHandlerKey))
                return k != null && string.Equals(k.GetValue("") as string, ClsidKey, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsRawRegistered()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.cr2\ShellEx\" + ThumbHandlerKey))
                return k != null;
        }

        static void SetExt(string ext, bool on)
        {
            string path = @"Software\Classes\" + ext + @"\ShellEx\" + ThumbHandlerKey;
            if (on)
            {
                using (var k = Registry.CurrentUser.CreateSubKey(path)) k.SetValue("", ClsidKey);
                // also on the SystemFileAssociations branch (wins over some app ProgIDs)
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\SystemFileAssociations\" + ext + @"\ShellEx\" + ThumbHandlerKey)) k.SetValue("", ClsidKey);
            }
            else
            {
                try { Registry.CurrentUser.DeleteSubKey(path, false); } catch { }
                try { Registry.CurrentUser.DeleteSubKey(@"Software\Classes\SystemFileAssociations\" + ext + @"\ShellEx\" + ThumbHandlerKey, false); } catch { }
            }
        }

        [DllImport("shell32.dll")]
        static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        public static void Notify() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
    }
}
