using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Qassimz.Thumbs.Viewer
{
    public sealed class MainForm : Form
    {
        public const string AppVersion = "1.0.0";
        public const string Repo = "ahmedqassimtv-commits/qassimz-thumbs";

        readonly WebView2 web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(4, 10, 20) };
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        readonly string startPath;
        CoreWebView2Environment env;
        Settings settings = Settings.Load();
        Dictionary<string, object> update;

        static readonly string AppDir = Path.GetDirectoryName(Application.ExecutablePath);

        public MainForm(string startPath)
        {
            this.startPath = startPath;
            Text = "QASSIMZ Thumbs";
            BackColor = Color.FromArgb(4, 10, 20);
            MinimumSize = new Size(980, 640);
            StartPosition = FormStartPosition.CenterScreen;
            var b = settings.Bounds;
            if (b.Width >= 980 && b.Height >= 640 && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(b)))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = b;
            }
            else Size = new Size(1280, 800);
            if (settings.Maximized) WindowState = FormWindowState.Maximized;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Controls.Add(web);
            Load += async (s, e) => await InitAsync();
            FormClosing += (s, e) =>
            {
                settings.Maximized = WindowState == FormWindowState.Maximized;
                if (WindowState == FormWindowState.Normal) settings.Bounds = Bounds;
                settings.Save();
            };
        }

        // ---------------------------------------------------------------- dark, brand-colored title bar
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int on = 1; DwmSetWindowAttribute(Handle, 20, ref on, 4);              // immersive dark mode
            int caption = 0x00_38_1A_08; DwmSetWindowAttribute(Handle, 35, ref caption, 4); // COLORREF 0x00BBGGRR → #081A38
            int text = 0x00_F8_F4_F4; DwmSetWindowAttribute(Handle, 36, ref text, 4);
        }

        // ---------------------------------------------------------------- WebView2
        async Task InitAsync()
        {
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QASSIMZ Thumbs", "WebView2");
            try
            {
                env = await CoreWebView2Environment.CreateAsync(null, data);
                await web.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                MessageBox.Show("محتاج Microsoft Edge WebView2 Runtime.\nهيتفتح رابط التحميل.\n\n" + ex.Message, "QASSIMZ Thumbs");
                Process.Start("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
                Close();
                return;
            }
            var cw = web.CoreWebView2;
            cw.Settings.AreDevToolsEnabled = false;
            cw.Settings.AreDefaultContextMenusEnabled = false;
            cw.Settings.IsStatusBarEnabled = false;
            cw.Settings.IsZoomControlEnabled = false;
            cw.SetVirtualHostNameToFolderMapping("app.qassimz", Path.Combine(AppDir, "ui"), CoreWebView2HostResourceAccessKind.Allow);
            cw.AddWebResourceRequestedFilter("https://thumb.qassimz/*", CoreWebView2WebResourceContext.All);
            cw.WebResourceRequested += OnThumbRequest;
            cw.WebMessageReceived += OnMessage;
            cw.NewWindowRequested += (s, e) => { e.Handled = true; OpenUrl(e.Uri); };
            cw.Navigate("https://app.qassimz/index.html");
            _ = CheckUpdateAsync(false);
        }

        void OnThumbRequest(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            var uri = new Uri(e.Request.Uri);
            var q = HttpUtility.ParseQueryString(uri.Query);
            string path = q["p"];
            int.TryParse(q["s"], out int size);
            size = Math.Max(48, Math.Min(4096, size == 0 ? 256 : size));
            var deferral = e.GetDeferral();
            Thumbs.GetPngAsync(path, size).ContinueWith(t =>
            {
                byte[] png = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
                BeginInvoke((Action)(() =>
                {
                    try
                    {
                        e.Response = png != null
                            ? env.CreateWebResourceResponse(new MemoryStream(png), 200, "OK", "Content-Type: image/png\r\nCache-Control: max-age=86400")
                            : env.CreateWebResourceResponse(null, 404, "Not Found", "");
                    }
                    catch { }
                    deferral.Complete();
                }));
            });
        }

        void Send(string type, object data)
        {
            if (web.CoreWebView2 == null) return;
            if (InvokeRequired) { BeginInvoke((Action)(() => Send(type, data))); return; }
            web.CoreWebView2.PostWebMessageAsJson(json.Serialize(new Dictionary<string, object> { { "type", type }, { "data", data } }));
        }

        // ---------------------------------------------------------------- commands from the UI
        void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            Dictionary<string, object> m;
            try { m = json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson); } catch { return; }
            string cmd = m.TryGetValue("cmd", out var c) ? c as string : null;
            string path = m.TryGetValue("path", out var p) ? p as string : null;
            int id = m.TryGetValue("id", out var i) && i is int ii ? ii : 0;
            object Reply(object data) { Send("reply", new Dictionary<string, object> { { "id", id }, { "data", data } }); return null; }

            try
            {
                switch (cmd)
                {
                    case "init": Reply(Info()); break;
                    case "drop":
                        {
                            var f = e.AdditionalObjects?.OfType<CoreWebView2File>().FirstOrDefault();
                            if (f != null)
                            {
                                string target = Directory.Exists(f.Path) ? f.Path : Path.GetDirectoryName(f.Path);
                                Send("navigate", target);
                            }
                            break;
                        }
                    case "list": Task.Run(() => Reply(List(path))); break;
                    case "details": Task.Run(() => Reply(Thumbs.Details(path))); break;
                    case "pick":
                        Reply(FolderPicker.Pick(Handle, path));
                        break;
                    case "open": Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); Reply(true); break;
                    case "reveal": Process.Start("explorer.exe", "/select,\"" + path + "\""); Reply(true); break;
                    case "openWith": Process.Start("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + path); Reply(true); break;
                    case "copy":
                        using (var bmp = Thumbs.Render(path, 4096)) { if (bmp != null) Clipboard.SetImage(bmp); Reply(bmp != null); }
                        break;
                    case "export":
                        {
                            var dlg = new SaveFileDialog { Filter = "PNG|*.png|JPEG|*.jpg", FileName = Path.GetFileNameWithoutExtension(path) + ".png" };
                            if (dlg.ShowDialog(this) == DialogResult.OK)
                                using (var bmp = Thumbs.Render(path, 8192))
                                {
                                    bmp?.Save(dlg.FileName, dlg.FilterIndex == 2 ? System.Drawing.Imaging.ImageFormat.Jpeg : System.Drawing.Imaging.ImageFormat.Png);
                                    Reply(bmp != null ? dlg.FileName : null);
                                }
                            else Reply(null);
                            break;
                        }
                    case "explorer":
                        {
                            bool on = Convert.ToBoolean(m["on"]), raw = m.ContainsKey("raw") && Convert.ToBoolean(m["raw"]);
                            if (on) Registration.Register(ShellDll(), raw); else Registration.Unregister();
                            Reply(new Dictionary<string, object> { { "on", Registration.IsRegistered() }, { "raw", Registration.IsRawRegistered() } });
                            break;
                        }
                    case "refreshExplorer": RefreshExplorerCache(); Reply(true); break;
                    case "clearCache": Thumbs.ClearCache(); Reply(Thumbs.CacheSize()); break;
                    case "url": OpenUrl(m["url"] as string); Reply(true); break;
                    case "settings":
                        if (m.TryGetValue("value", out var v) && v is Dictionary<string, object> sv) { settings.Apply(sv); settings.Save(); }
                        Reply(settings.ToDict());
                        break;
                    case "checkUpdate": _ = CheckUpdateAsync(true); Reply(true); break;
                    case "installUpdate": _ = InstallUpdateAsync(); Reply(true); break;
                    default: Reply(null); break;
                }
            }
            catch (Exception ex) { Send("error", ex.Message); Reply(null); }
        }

        Dictionary<string, object> Info()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var places = new List<object>
            {
                Place("سطح المكتب", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "desktop"),
                Place("التنزيلات", Path.Combine(home, "Downloads"), "downloads"),
                Place("الصور", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "pictures"),
                Place("المستندات", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "documents"),
                Place("الفيديوهات", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "videos"),
            };
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                .Select(d => Place(string.IsNullOrEmpty(d.VolumeLabel) ? d.Name.TrimEnd('\\') : d.VolumeLabel + " (" + d.Name.TrimEnd('\\') + ")", d.RootDirectory.FullName, "drive")).ToList<object>();
            return new Dictionary<string, object>
            {
                { "version", AppVersion }, { "github", "https://github.com/" + Repo },
                { "support", "https://github.com/" + Repo + "/issues" }, { "site", "https://qassimz.com" },
                { "places", places }, { "drives", drives },
                { "start", startPath ?? settings.LastFolder ?? Path.Combine(home, "Pictures") },
                { "explorer", Registration.IsRegistered() }, { "raw", Registration.IsRawRegistered() },
                { "cache", Thumbs.CacheSize() }, { "settings", settings.ToDict() }, { "update", update },
                { "designExt", Extractor.DesignExtensions }, { "rawExt", Extractor.RawExtensions },
            };
        }

        static Dictionary<string, object> Place(string name, string path, string icon) =>
            new Dictionary<string, object> { { "name", name }, { "path", path }, { "icon", icon } };

        object List(string path)
        {
            var di = new DirectoryInfo(path);
            if (!di.Exists) return new Dictionary<string, object> { { "error", "المجلد مش موجود" } };
            settings.LastFolder = di.FullName;
            settings.PushRecent(di.FullName);
            settings.Save();
            var items = new List<object>();
            try
            {
                foreach (var d in di.EnumerateDirectories())
                {
                    if ((d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    items.Add(new Dictionary<string, object> { { "n", d.Name }, { "p", d.FullName }, { "d", true }, { "m", d.LastWriteTime.Ticks / 10000 } });
                }
                foreach (var f in di.EnumerateFiles())
                {
                    if ((f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    items.Add(new Dictionary<string, object> { { "n", f.Name }, { "p", f.FullName }, { "s", f.Length }, { "m", f.LastWriteTime.Ticks / 10000 }, { "e", f.Extension.ToLowerInvariant() } });
                }
            }
            catch (UnauthorizedAccessException) { return new Dictionary<string, object> { { "error", "مفيش صلاحية لفتح المجلد ده" } }; }
            return new Dictionary<string, object>
            {
                { "path", di.FullName }, { "name", di.Name }, { "parent", di.Parent?.FullName },
                { "items", items }, { "recent", settings.Recent },
            };
        }

        string ShellDll()
        {
            // versioned folder so updates never collide with a DLL Explorer still has loaded
            string versioned = Path.Combine(AppDir, "shell", AppVersion, "QassimzThumbs.Shell.dll");
            return File.Exists(versioned) ? versioned : Path.Combine(AppDir, "QassimzThumbs.Shell.dll");
        }

        static void RefreshExplorerCache()
        {
            // Windows caches "no thumbnail" results: wipe thumbcache and restart Explorer
            foreach (var p in Process.GetProcessesByName("explorer")) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Explorer");
            try { foreach (var f in Directory.GetFiles(dir, "thumbcache_*.db")) { try { File.Delete(f); } catch { } } } catch { }
            Process.Start("explorer.exe");
            Registration.Notify();
        }

        static void OpenUrl(string url)
        {
            if (!string.IsNullOrEmpty(url) && (url.StartsWith("https://") || url.StartsWith("http://")))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        // ---------------------------------------------------------------- self-update from GitHub Releases
        async Task CheckUpdateAsync(bool manual)
        {
            if (!manual && !settings.AutoUpdate) return;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "QASSIMZ-Thumbs");
                    wc.Encoding = System.Text.Encoding.UTF8;
                    string s = await wc.DownloadStringTaskAsync("https://api.github.com/repos/" + Repo + "/releases/latest");
                    var rel = json.Deserialize<Dictionary<string, object>>(s);
                    string tag = (rel["tag_name"] as string ?? "").TrimStart('v', 'V');
                    var asset = (rel["assets"] as System.Collections.ArrayList)?.Cast<Dictionary<string, object>>()
                        .FirstOrDefault(a => ((string)a["name"]).ToLowerInvariant().Contains("setup") && ((string)a["name"]).EndsWith(".exe"));
                    if (asset != null && Version.TryParse(tag, out var latest) && latest > Version.Parse(AppVersion))
                    {
                        update = new Dictionary<string, object> { { "version", tag }, { "url", asset["browser_download_url"] }, { "notes", rel["body"] } };
                        Send("update", update);
                    }
                    else if (manual) Send("latest", true);
                }
            }
            catch { if (manual) Send("latest", null); }
        }

        async Task InstallUpdateAsync()
        {
            if (update == null) return;
            try
            {
                string dst = Path.Combine(Path.GetTempPath(), "QASSIMZ-Thumbs-Setup-" + update["version"] + ".exe");
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "QASSIMZ-Thumbs");
                    wc.DownloadProgressChanged += (s, e) => Send("updateProgress", e.ProgressPercentage);
                    await wc.DownloadFileTaskAsync(new Uri((string)update["url"]), dst);
                }
                Process.Start(dst, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS");
                Application.Exit();
            }
            catch (Exception ex) { Send("error", "التحديث فشل: " + ex.Message); }
        }
    }
}
