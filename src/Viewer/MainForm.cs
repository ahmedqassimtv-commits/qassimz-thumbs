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
        public const string AppVersion = "1.2.0";
        public const string Repo = "ahmedqassimtv-commits/qassimz-thumbs";

        const int WM_NCHITTEST = 0x0084;
        const int WM_NCLBUTTONDOWN = 0x00A1;
        const int HTCAPTION = 0x0002;
        const int HTLEFT = 10;
        const int HTRIGHT = 11;
        const int HTTOP = 12;
        const int HTTOPLEFT = 13;
        const int HTTOPRIGHT = 14;
        const int HTBOTTOM = 15;
        const int HTBOTTOMLEFT = 16;
        const int HTBOTTOMRIGHT = 17;
        const int ResizeBorder = 5;

        readonly WebView2 web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(4, 10, 20) };
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        readonly object settingsLock = new object();
        readonly string startPath;
        CoreWebView2Environment env;
        Settings settings = Settings.Load();
        Dictionary<string, object> update;

        static readonly string AppDir = Path.GetDirectoryName(Application.ExecutablePath);

        public MainForm(string startPath)
        {
            this.startPath = startPath;
            Text = "QASSIMZ Thumbs";
            FormBorderStyle = FormBorderStyle.None;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(4, 10, 20);
            MinimumSize = new Size(980, 640);
            StartPosition = FormStartPosition.CenterScreen;

            Rectangle savedBounds;
            bool savedMax;
            lock (settingsLock)
            {
                savedBounds = settings.Bounds;
                savedMax = settings.Maximized;
            }
            if (savedBounds.Width >= 980 && savedBounds.Height >= 640 && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(savedBounds)))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = savedBounds;
            }
            else Size = new Size(1280, 800);
            Padding = savedMax ? Padding.Empty : new Padding(ResizeBorder);
            if (savedMax) WindowState = FormWindowState.Maximized;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Controls.Add(web);
            Load += async (s, e) => await InitAsync();
            Resize += (s, e) =>
            {
                Padding = WindowState == FormWindowState.Maximized ? Padding.Empty : new Padding(ResizeBorder);
                UpdateMaximizedBounds();
            };
            FormClosing += (s, e) =>
            {
                lock (settingsLock)
                {
                    settings.Maximized = WindowState == FormWindowState.Maximized;
                    if (WindowState == FormWindowState.Normal) settings.Bounds = Bounds;
                    settings.Save();
                }
            };
        }

        // ---------------------------------------------------------------- frameless window + dark DWM chrome
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        void UpdateMaximizedBounds()
        {
            if (!IsHandleCreated) return;
            var scr = Screen.FromHandle(Handle);
            var wa = scr.WorkingArea;
            var sb = scr.Bounds;
            MaximizedBounds = new Rectangle(wa.X - sb.X, wa.Y - sb.Y, wa.Width, wa.Height);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateMaximizedBounds();
            int on = 1; DwmSetWindowAttribute(Handle, 20, ref on, 4);              // immersive dark mode
            int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4);        // DWMWCP_ROUND (Windows 11)
            int caption = 0x00_38_1A_08; DwmSetWindowAttribute(Handle, 35, ref caption, 4); // COLORREF 0x00BBGGRR → #081A38
            int text = 0x00_F8_F4_F4; DwmSetWindowAttribute(Handle, 36, ref text, 4);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                long lp = m.LParam.ToInt64();
                int x = (short)(lp & 0xFFFF);
                int y = (short)((lp >> 16) & 0xFFFF);
                var pt = PointToClient(new Point(x, y));
                bool left = pt.X <= ResizeBorder;
                bool right = pt.X >= ClientSize.Width - ResizeBorder;
                bool top = pt.Y <= ResizeBorder;
                bool bottom = pt.Y >= ClientSize.Height - ResizeBorder;

                if (top && left) { m.Result = (IntPtr)HTTOPLEFT; return; }
                if (top && right) { m.Result = (IntPtr)HTTOPRIGHT; return; }
                if (bottom && left) { m.Result = (IntPtr)HTBOTTOMLEFT; return; }
                if (bottom && right) { m.Result = (IntPtr)HTBOTTOMRIGHT; return; }
                if (left) { m.Result = (IntPtr)HTLEFT; return; }
                if (right) { m.Result = (IntPtr)HTRIGHT; return; }
                if (top) { m.Result = (IntPtr)HTTOP; return; }
                if (bottom) { m.Result = (IntPtr)HTBOTTOM; return; }
                return;
            }
            base.WndProc(ref m);
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
                if (IsDisposed || !IsHandleCreated)
                {
                    try { deferral.Complete(); } catch { }
                    return;
                }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        MemoryStream ms = null;
                        try
                        {
                            if (png != null && png.Length > 0)
                            {
                                byte[] copy = new byte[png.Length];
                                Buffer.BlockCopy(png, 0, copy, 0, png.Length);
                                ms = new MemoryStream(copy, 0, copy.Length, writable: false, publiclyVisible: false);
                                e.Response = env.CreateWebResourceResponse(ms, 200, "OK", "Content-Type: image/png\r\nCache-Control: max-age=86400");
                                ms = null; // ownership transferred to WebView2 IStream wrapper
                            }
                            else
                            {
                                e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", "");
                            }
                        }
                        catch
                        {
                            ms?.Dispose();
                        }
                        finally
                        {
                            try { deferral.Complete(); } catch { }
                        }
                    }));
                }
                catch
                {
                    try { deferral.Complete(); } catch { }
                }
            });
        }

        void Send(string type, object data)
        {
            if (IsDisposed || web.CoreWebView2 == null) return;
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
                    case "win_drag":
                        ReleaseCapture();
                        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                        break;
                    case "win_min":
                        WindowState = FormWindowState.Minimized;
                        Reply(true);
                        break;
                    case "win_max":
                        UpdateMaximizedBounds();
                        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                        Reply(WindowState == FormWindowState.Maximized);
                        break;
                    case "win_close":
                        Reply(true);
                        Close();
                        break;
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
                    case "openWith": Process.Start("rundll32.exe", "shell32.dll,OpenAs_RunDLL \"" + path + "\""); Reply(true); break;
                    case "copy":
                        Task.Run(() =>
                        {
                            Bitmap bmp = null;
                            try
                            {
                                bmp = Thumbs.Render(path, 4096);
                                if (bmp == null) { Reply(false); return; }
                                if (IsDisposed || !IsHandleCreated) { bmp.Dispose(); return; }
                                BeginInvoke((Action)(() =>
                                {
                                    try { Clipboard.SetImage(bmp); Reply(true); }
                                    catch (Exception ex) { Send("error", ex.Message); Reply(false); }
                                    finally { bmp.Dispose(); }
                                }));
                            }
                            catch (Exception ex)
                            {
                                bmp?.Dispose();
                                Send("error", ex.Message);
                                Reply(false);
                            }
                        });
                        break;
                    case "export":
                        {
                            string targetFile = null;
                            var format = System.Drawing.Imaging.ImageFormat.Png;
                            using (var dlg = new SaveFileDialog { Filter = "PNG|*.png|JPEG|*.jpg", FileName = Path.GetFileNameWithoutExtension(path) + ".png" })
                            {
                                if (dlg.ShowDialog(this) == DialogResult.OK)
                                {
                                    targetFile = dlg.FileName;
                                    format = dlg.FilterIndex == 2 ? System.Drawing.Imaging.ImageFormat.Jpeg : System.Drawing.Imaging.ImageFormat.Png;
                                }
                            }
                            if (string.IsNullOrEmpty(targetFile)) { Reply(null); break; }
                            Task.Run(() =>
                            {
                                try
                                {
                                    using (var bmp = Thumbs.Render(path, 8192))
                                    {
                                        if (bmp == null) { Reply(null); return; }
                                        bmp.Save(targetFile, format);
                                        Reply(targetFile);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Send("error", ex.Message);
                                    Reply(null);
                                }
                            });
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
                        Dictionary<string, object> snapshot;
                        lock (settingsLock)
                        {
                            if (m.TryGetValue("value", out var v) && v is Dictionary<string, object> sv) { settings.Apply(sv); settings.Save(); }
                            snapshot = settings.ToDict();
                        }
                        Reply(snapshot);
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
            string lastFolder;
            Dictionary<string, object> settingsSnapshot;
            lock (settingsLock)
            {
                lastFolder = settings.LastFolder;
                settingsSnapshot = settings.ToDict();
            }
            return new Dictionary<string, object>
            {
                { "version", AppVersion }, { "github", "https://github.com/" + Repo },
                { "support", "https://github.com/" + Repo + "/issues" }, { "site", "https://qassimz.com" },
                { "places", places }, { "drives", drives },
                { "start", startPath ?? lastFolder ?? Path.Combine(home, "Pictures") },
                { "explorer", Registration.IsRegistered() }, { "raw", Registration.IsRawRegistered() },
                { "cache", Thumbs.CacheSize() }, { "settings", settingsSnapshot }, { "update", update },
                { "designExt", Extractor.DesignExtensions }, { "rawExt", Extractor.RawExtensions },
            };
        }

        static Dictionary<string, object> Place(string name, string path, string icon) =>
            new Dictionary<string, object> { { "name", name }, { "path", path }, { "icon", icon } };

        object List(string path)
        {
            var di = new DirectoryInfo(path);
            if (!di.Exists) return new Dictionary<string, object> { { "error", "المجلد مش موجود" } };
            List<string> recentCopy;
            lock (settingsLock)
            {
                settings.LastFolder = di.FullName;
                settings.PushRecent(di.FullName);
                settings.Save();
                recentCopy = new List<string>(settings.Recent);
            }
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
                { "items", items }, { "recent", recentCopy },
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
            bool autoUpdate;
            lock (settingsLock) autoUpdate = settings.AutoUpdate;
            if (!manual && !autoUpdate) return;
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
