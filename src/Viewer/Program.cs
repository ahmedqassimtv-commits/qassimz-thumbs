using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Qassimz.Thumbs.Viewer
{
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // installer hooks:  --register [--raw]   |   --unregister
            if (args.Contains("--register"))
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                string dll = Path.Combine(dir, "shell", MainForm.AppVersion, "QassimzThumbs.Shell.dll");
                if (!File.Exists(dll)) dll = Path.Combine(dir, "QassimzThumbs.Shell.dll");
                Registration.Register(dll, args.Contains("--raw"));
                return 0;
            }
            if (args.Contains("--unregister")) { Registration.Unregister(); return 0; }

            string start = args.FirstOrDefault(a => !a.StartsWith("--"));
            if (start != null && File.Exists(start)) start = Path.GetDirectoryName(start); // opened with a file
            if (start != null && !Directory.Exists(start)) start = null;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(start));
            return 0;
        }
    }

    internal sealed class Settings
    {
        public string LastFolder;
        public List<string> Recent = new List<string>();
        public Rectangle Bounds;
        public bool Maximized;
        public bool AutoUpdate = true;
        public int ThumbSize = 180;
        public string Sort = "name";
        public string Filter = "all";

        static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QASSIMZ Thumbs", "settings.json");
        static readonly JavaScriptSerializer J = new JavaScriptSerializer();

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                var d = J.Deserialize<Dictionary<string, object>>(File.ReadAllText(FilePath));
                s.Apply(d);
                if (d.TryGetValue("lastFolder", out var lf)) s.LastFolder = lf as string;
                if (d.TryGetValue("recent", out var r) && r is ArrayList al) s.Recent = al.OfType<string>().ToList();
                if (d.TryGetValue("bounds", out var b) && b is ArrayList bl && bl.Count == 4)
                    s.Bounds = new Rectangle(Convert.ToInt32(bl[0]), Convert.ToInt32(bl[1]), Convert.ToInt32(bl[2]), Convert.ToInt32(bl[3]));
                if (d.TryGetValue("maximized", out var m)) s.Maximized = Convert.ToBoolean(m);
            }
            catch { }
            return s;
        }

        public void Apply(Dictionary<string, object> d)
        {
            if (d == null) return;
            if (d.TryGetValue("autoUpdate", out var a)) AutoUpdate = Convert.ToBoolean(a);
            if (d.TryGetValue("thumbSize", out var t)) ThumbSize = Convert.ToInt32(t);
            if (d.TryGetValue("sort", out var so)) Sort = so as string ?? Sort;
            if (d.TryGetValue("filter", out var f)) Filter = f as string ?? Filter;
        }

        public Dictionary<string, object> ToDict() => new Dictionary<string, object>
        {
            { "autoUpdate", AutoUpdate }, { "thumbSize", ThumbSize }, { "sort", Sort }, { "filter", Filter },
        };

        public void PushRecent(string p)
        {
            Recent.RemoveAll(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase));
            Recent.Insert(0, p);
            if (Recent.Count > 8) Recent.RemoveRange(8, Recent.Count - 8);
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var d = ToDict();
                d["lastFolder"] = LastFolder;
                d["recent"] = Recent;
                d["bounds"] = new[] { Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height };
                d["maximized"] = Maximized;
                File.WriteAllText(FilePath, J.Serialize(d));
            }
            catch { }
        }
    }

    /// <summary>Modern Windows folder picker (IFileOpenDialog with FOS_PICKFOLDERS).</summary>
    internal static class FolderPicker
    {
        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")] class FileOpenDialogRCW { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(); void SetFileTypeIndex(); void GetFileTypeIndex(); void Advise(); void Unadvise();
            void SetOptions(uint fos); void GetOptions(out uint fos);
            void SetDefaultFolder(IShellItem psi); void SetFolder(IShellItem psi);
            void GetFolder(); void GetCurrentSelection();
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name); void GetFileName();
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel(); void SetFileNameLabel();
            void GetResult(out IShellItem ppsi);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(); void GetParent();
            void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem item);

        public static string Pick(IntPtr owner, string initial)
        {
            var dlg = (IFileOpenDialog)new FileOpenDialogRCW();
            try
            {
                dlg.SetOptions(0x20 /*PICKFOLDERS*/ | 0x40 /*FORCEFILESYSTEM*/);
                dlg.SetTitle("اختار مجلد");
                if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial))
                {
                    try { SHCreateItemFromParsingName(initial, IntPtr.Zero, typeof(IShellItem).GUID, out var si); dlg.SetFolder(si); } catch { }
                }
                if (dlg.Show(owner) != 0) return null;
                dlg.GetResult(out var item);
                item.GetDisplayName(0x80058000 /*SIGDN_FILESYSPATH*/, out string path);
                return path;
            }
            finally { Marshal.ReleaseComObject(dlg); }
        }
    }
}
