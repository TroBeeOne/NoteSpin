using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace NoteSpin
{
    /// <summary>
    /// The app window: a WebView2 that shows the page compiled into the exe at https://notespin.local/.
    /// Nothing else can load in it; outside links open in the default browser.
    /// </summary>
    internal sealed class MainForm : Form
    {
        public const string Origin = "https://notespin.local/";
        static readonly Color Bg = Color.FromArgb(17, 19, 24);
        // browser-only entries removed from the right-click menu (copy, paste, spelling etc. stay)
        static readonly HashSet<string> BlockedMenuItems = new HashSet<string>
        {
            "back", "forward", "reload", "saveAs", "print", "createQrCode", "share", "inspectElement", "viewPageSource",
            "openLinkInNewWindow", "saveLinkAs", "saveImageAs", "saveMediaAs", "openImageInNewWindow", "copyLinkToHighlight", "webCapture",
        };

        readonly Settings settings;
        readonly Library lib;
        string startupFile;
        CoreWebView2Environment env;
        CoreWebView2Controller ctl;
        CoreWebView2 core;
        bool closeConfirmed, closing;
        readonly bool devTools = Environment.GetEnvironmentVariable("NOTESPIN_DEVTOOLS") == "1";

        public string DataDir { get; }

        public MainForm(string dataDir, Settings settings, Library lib, string startupFile)
        {
            DataDir = dataDir;
            this.settings = settings;
            this.lib = lib;
            this.startupFile = startupFile;
            Text = "NoteSpin";
            BackColor = Bg;
            Icon = WebAssets.AppIcon() ?? Icon;
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(560, 420);
            RestorePlacement();
        }

        /* ---------- start-up ---------- */

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                await InitWebView();
            }
            catch (WebView2RuntimeNotFoundException)
            {
                if (MessageBox.Show(this, "NoteSpin needs the Microsoft Edge WebView2 Runtime, which comes with Windows 11 but isn't on this PC.\n\nOpen Microsoft's download page?",
                        "NoteSpin", MessageBoxButtons.YesNo, MessageBoxIcon.Error) == DialogResult.Yes)
                    OpenExternal("https://developer.microsoft.com/microsoft-edge/webview2/");
                closeConfirmed = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "NoteSpin couldn't start its web view.\n\n" + ex.Message, "NoteSpin", MessageBoxButtons.OK, MessageBoxIcon.Error);
                closeConfirmed = true;
                Close();
            }
        }

        async Task InitWebView()
        {
            env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(DataDir, "WebView2"), new CoreWebView2EnvironmentOptions());
            ctl = await env.CreateCoreWebView2ControllerAsync(Handle);
            ctl.DefaultBackgroundColor = Bg;
            ctl.Bounds = ClientRectangle;
            ctl.IsVisible = WindowState != FormWindowState.Minimized;
            ctl.AcceleratorKeyPressed += OnAcceleratorKey;
            ctl.MoveFocusRequested += (s, a) => { a.Handled = true; ctl.MoveFocus(a.Reason); };   // Tab cycles inside the page

            core = ctl.CoreWebView2;
            var st = core.Settings;
            st.AreDevToolsEnabled = devTools;
            st.IsStatusBarEnabled = false;
            st.IsPasswordAutosaveEnabled = false;
            st.IsGeneralAutofillEnabled = false;
            st.IsSwipeNavigationEnabled = false;
            st.IsZoomControlEnabled = false;      // Ctrl+wheel and Ctrl +/- would zoom the page; the wheel spins the cards
            st.IsPinchZoomEnabled = false;        // touch screens: pinching would zoom the whole page
            st.IsReputationCheckingRequired = false;   // no SmartScreen look-ups: the page comes from inside the exe

            core.AddHostObjectToScript("ns", new HostApi(this, settings, lib));
            core.AddWebResourceRequestedFilter(Origin + "*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnWebResource;
            core.NavigationStarting += OnNavigationStarting;
            core.NewWindowRequested += OnNewWindow;
            core.WebMessageReceived += OnWebMessage;
            core.DownloadStarting += (s, a) => a.Cancel = true;
            core.ContextMenuRequested += OnContextMenu;
            core.ProcessFailed += OnProcessFailed;
            core.PermissionRequested += (s, a) => a.State = CoreWebView2PermissionState.Deny;
            core.DocumentTitleChanged += (s, a) => Text = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "NoteSpin" : core.DocumentTitle;

            core.Navigate(Origin + "index.html");
            if (ContainsFocus) ctl.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
        }

        /* ---------- the page, served from the exe ---------- */

        void OnWebResource(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            string path;
            try { path = Uri.UnescapeDataString(new Uri(e.Request.Uri).AbsolutePath).TrimStart('/'); }
            catch { path = ""; }
            if (path.Length == 0) path = "index.html";
            byte[] data = path.Equals("favicon.ico", StringComparison.OrdinalIgnoreCase) ? WebAssets.Favicon() : devWeb != null ? FromDisk(path) : WebAssets.Get(path);
            e.Response = data == null
                ? env.CreateWebResourceResponse(null, 404, "Not Found", "Content-Type: text/plain")
                : env.CreateWebResourceResponse(new MemoryStream(data), 200, "OK", "Content-Type: " + WebAssets.MimeType(path) + "\r\nCache-Control: no-store");
        }

        // NOTESPIN_WEB=<the project's web folder> serves the page from disk instead of the exe, so a
        // reload picks up changes while working on it (with NOTESPIN_DEVTOOLS=1 for F5 and F12).
        readonly string devWeb = Environment.GetEnvironmentVariable("NOTESPIN_WEB") is string w && Directory.Exists(w) ? Path.GetFullPath(w) : null;

        byte[] FromDisk(string path)
        {
            string full = Path.GetFullPath(Path.Combine(devWeb, path.Replace('/', '\\')));
            if (!full.StartsWith(devWeb + "\\", StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return null;
            try { return File.ReadAllBytes(full); } catch { return null; }
        }

        void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            string uri = e.Uri ?? "";
            if (uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) return;
            e.Cancel = true;
            if (uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) OnDroppedFile(uri);
            else OpenExternal(uri);
        }

        // A .notespin file dropped on the window: the page offers to import it
        void OnDroppedFile(string fileUri)
        {
            try
            {
                string p = new Uri(fileUri).LocalPath;
                if (!File.Exists(p)) return;
                Later(() => Post(new Dictionary<string, object> { ["type"] = "importFile", ["path"] = p }));
            }
            catch { }
        }

        // The page asks for the real path of a dropped file: postMessageWithAdditionalObjects({type:'filePath', id}, [file])
        void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!(e.Source ?? "").StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) return;
            Dictionary<string, object> m;
            try { m = HostApi.Json.DeserializeObject(e.WebMessageAsJson) as Dictionary<string, object>; }
            catch { return; }
            if (m == null || !(m.TryGetValue("type", out object t) && (t as string) == "filePath") || !(m.TryGetValue("id", out object id) && id is string rid)) return;
            string path = null;
            try
            {
                var objs = e.AdditionalObjects;
                if (objs != null && objs.Count > 0 && objs[0] is CoreWebView2File f && !string.IsNullOrEmpty(f.Path)) path = f.Path;
            }
            catch { }
            Reply(rid, path);
        }

        void OnNewWindow(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;   // never a second browser window
            string uri = e.Uri ?? "";
            if (!uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) OpenExternal(uri);
        }

        internal static void OpenExternal(string uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri u)) return;
            // only web and mail links are handed to Windows, never other protocol handlers
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeMailto) return;
            try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); } catch { }
        }

        void OnContextMenu(object sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            var items = e.MenuItems;
            for (int i = items.Count - 1; i >= 0; i--)
                if (BlockedMenuItems.Contains(items[i].Name) && !(devTools && items[i].Name == "inspectElement")) items.RemoveAt(i);
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (items[i].Kind != CoreWebView2ContextMenuItemKind.Separator) continue;
                if (i == 0 || i == items.Count - 1 || items[i - 1].Kind == CoreWebView2ContextMenuItemKind.Separator) items.RemoveAt(i);
            }
            if (items.Count == 0) e.Handled = true;
        }

        // Keys still reach the page; this only stops the browser's own find bar, reload, print, back… so the
        // page can use Ctrl+F for its search. Editing keys (Ctrl+C/V/X/Z/A) aren't browser keys and keep working.
        void OnAcceleratorKey(object sender, CoreWebView2AcceleratorKeyPressedEventArgs e)
        {
            if (e.KeyEventKind != CoreWebView2KeyEventKind.KeyDown && e.KeyEventKind != CoreWebView2KeyEventKind.SystemKeyDown) return;
            var key = (Keys)(int)e.VirtualKey;
            bool ctrl = (ModifierKeys & Keys.Control) != 0, shift = (ModifierKeys & Keys.Shift) != 0;
            bool keep = devTools && (key == Keys.F12 || (ctrl && shift && key == Keys.I));
            if (!keep) e.IsBrowserAcceleratorKeyEnabled = false;
        }

        void OnProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited ||
                e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
            {
                Later(() => core?.Reload());
            }
            else if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
            {
                Later(() =>
                {
                    MessageBox.Show(this, "The web view stopped unexpectedly. Notes are saved as you type; reopen NoteSpin to carry on.",
                        "NoteSpin", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    closeConfirmed = true;
                    Close();
                });
            }
        }

        /* ---------- helpers for HostApi ---------- */

        internal void Later(Action a)
        {
            if (!IsDisposed && IsHandleCreated) BeginInvoke(a);
        }

        void Post(Dictionary<string, object> m)
        {
            try { core?.PostWebMessageAsJson(HostApi.Json.Serialize(m)); } catch { }
        }

        internal void Reply(string id, string path, string error = null)
        {
            var d = new Dictionary<string, object> { ["id"] = id, ["path"] = path };
            if (error != null) d["error"] = error;
            Post(d);
        }

        internal string TakeStartupFile()
        {
            string f = startupFile;
            startupFile = null;
            return f;
        }

        const string Filter = "NoteSpin notebook (*" + Notebook.Ext + ")|*" + Notebook.Ext + "|All files (*.*)|*.*";

        internal string PickOpen()
        {
            using (var d = new OpenFileDialog
            {
                Title = "Import a NoteSpin backup",
                Filter = Filter,
                CheckFileExists = true,
                RestoreDirectory = true,
            })
            {
                string initial = StartDir();
                if (initial != null) d.InitialDirectory = initial;
                if (d.ShowDialog(this) != DialogResult.OK) return null;
                settings.Set("app:backupDir", Path.GetDirectoryName(d.FileName));
                return d.FileName;
            }
        }

        internal string PickSave(string name)
        {
            using (var d = new SaveFileDialog
            {
                Title = "Export a backup",
                FileName = name ?? "",
                Filter = Filter,
                DefaultExt = Notebook.Ext.TrimStart('.'),
                AddExtension = true,
                OverwritePrompt = true,
                RestoreDirectory = true,
            })
            {
                string initial = StartDir();
                if (initial != null) d.InitialDirectory = initial;
                if (d.ShowDialog(this) != DialogResult.OK) return null;
                settings.Set("app:backupDir", Path.GetDirectoryName(d.FileName));
                return d.FileName;
            }
        }

        internal string PickFolder(string startIn)
        {
            try { return FolderPicker.Pick(Handle, "Choose a folder for your notebooks", startIn); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The folder picker couldn't open.\n\n" + ex.Message, "NoteSpin", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        string StartDir()
        {
            foreach (string c in new[] { settings.Get("app:backupDir"), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) })
                if (!string.IsNullOrEmpty(c) && Directory.Exists(c)) return c;
            return null;
        }

        internal void ShowLicenses()
        {
            string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LICENSES.txt");
            if (File.Exists(p))
            {
                try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); return; } catch { }
            }
            MessageBox.Show(this, "LICENSES.txt isn't in the NoteSpin folder.", "NoteSpin", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /* ---------- window ---------- */

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (ctl == null) return;
            ctl.IsVisible = WindowState != FormWindowState.Minimized;
            if (ctl.IsVisible) ctl.Bounds = ClientRectangle;
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            ctl?.NotifyParentWindowPositionChanged();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            ctl?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
        }

        // Notes save as you type; before closing, the page gets up to 5 s to write the last change
        // (window.__nsBusy() starts any save that's waiting and returns true while one is being written).
        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if (closeConfirmed || core == null || e.CloseReason == CloseReason.WindowsShutDown)
            {
                SavePlacement();
                base.OnFormClosing(e);
                return;
            }
            e.Cancel = true;
            if (closing) return;
            closing = true;
            try
            {
                for (int i = 0; i < 100; i++)
                {
                    string busy = await core.ExecuteScriptAsync("typeof window.__nsBusy === 'function' && window.__nsBusy() === true");
                    if (busy != "true") break;
                    await Task.Delay(50);
                }
            }
            catch { /* the page isn't answering: close anyway */ }
            closeConfirmed = true;
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { ctl?.Close(); } catch { }
            base.OnFormClosed(e);
        }

        void RestorePlacement()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            Bounds = new Rectangle(wa.X + wa.Width / 10, wa.Y + wa.Height / 14, wa.Width * 4 / 5, wa.Height * 6 / 7);
            // NOTESPIN_WINDOW=x,y,width,height places the window exactly (for repeatable test screenshots)
            string fixedPlace = Environment.GetEnvironmentVariable("NOTESPIN_WINDOW");
            string[] p = (string.IsNullOrEmpty(fixedPlace) ? settings.Get("app:window") ?? "" : fixedPlace + ",0").Split(',');
            if (p.Length < 5 || !int.TryParse(p[0], out int x) || !int.TryParse(p[1], out int y) ||
                !int.TryParse(p[2], out int w) || !int.TryParse(p[3], out int h)) return;
            var r = new Rectangle(x, y, w, h);
            if (w < 400 || h < 300 || !Screen.AllScreens.Any(sc => Rectangle.Intersect(sc.WorkingArea, r).Width >= 200 && Rectangle.Intersect(sc.WorkingArea, r).Height >= 100)) return;
            Bounds = r;
            if (p[4] == "1") WindowState = FormWindowState.Maximized;
        }

        void SavePlacement()
        {
            try
            {
                var r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                settings.Set("app:window", $"{r.X},{r.Y},{r.Width},{r.Height},{(WindowState == FormWindowState.Maximized ? 1 : 0)}");
            }
            catch { }
        }
    }
}
