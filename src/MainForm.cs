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
    /// The app window: the page compiled into the exe (web\index.html) in a WebView2. It also looks after the
    /// note window, where a note can be kept outside it (see NoteWindow).
    /// </summary>
    internal sealed class MainForm : Form
    {
        static readonly Color Bg = Color.FromArgb(17, 19, 24);

        readonly Settings settings;
        readonly Library lib;
        readonly PageHost page;
        string startupFile;
        CoreWebView2Environment env;
        NoteWindow notes;
        Task<NoteWindow> notesReady;
        bool closeConfirmed, closing, dockShown;
        int dockSent;

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
            page = new PageHost(this, Bg);
            page.FileDropped += OnDroppedFile;
            page.BrowserExited += () =>
            {
                MessageBox.Show(this, "The web view stopped unexpectedly. Notes are saved as you type; reopen NoteSpin to carry on.",
                    "NoteSpin", MessageBoxButtons.OK, MessageBoxIcon.Error);
                closeConfirmed = true;
                Close();
            };
            RestorePlacement();
        }

        /* ---------- start-up ---------- */

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(DataDir, "WebView2"), new CoreWebView2EnvironmentOptions());
                await page.Start(env, "index.html", new HostApi(this, settings, lib, null));
                page.Core.WebMessageReceived += OnWebMessage;
                page.Core.DocumentTitleChanged += (s, a) => Text = string.IsNullOrWhiteSpace(page.Core.DocumentTitle) ? "NoteSpin" : page.Core.DocumentTitle;
                page.Core.NavigationCompleted += OnFirstPageLoaded;
                if (ContainsFocus) page.Focus();
            }
            catch (WebView2RuntimeNotFoundException)
            {
                if (MessageBox.Show(this, "NoteSpin needs the Microsoft Edge WebView2 Runtime, which comes with Windows 11 but isn't on this PC.\n\nOpen Microsoft's download page?",
                        "NoteSpin", MessageBoxButtons.YesNo, MessageBoxIcon.Error) == DialogResult.Yes)
                    PageHost.OpenExternal("https://developer.microsoft.com/microsoft-edge/webview2/");
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

        // The note window is made a moment after the cards are up, so it's ready when a note is dragged out
        async void OnFirstPageLoaded(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            page.Core.NavigationCompleted -= OnFirstPageLoaded;
            await Task.Delay(1500);
            try { await Notes(); } catch { /* tried again when a note is opened in it */ }
        }

        /// <summary>The note window, made on first use.</summary>
        internal Task<NoteWindow> Notes()
        {
            if (notesReady == null || notesReady.IsFaulted) notesReady = MakeNotes();
            return notesReady;
        }

        async Task<NoteWindow> MakeNotes()
        {
            if (env == null) throw new InvalidOperationException("NoteSpin is still starting.");
            var w = notes ?? new NoteWindow(this, settings);
            notes = w;
            w.FormClosed += (s, e) => { if (notes == w) { notes = null; notesReady = null; } };
            await w.Start(env, new HostApi(this, settings, lib, w));
            return w;
        }

        /// <summary>The note window if it's been made (else null).</summary>
        internal NoteWindow NoteWin => notes;

        /* ---------- messages to and from the page ---------- */

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
            if (!(e.Source ?? "").StartsWith(PageHost.Origin, StringComparison.OrdinalIgnoreCase)) return;
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

        internal void Later(Action a)
        {
            if (!IsDisposed && IsHandleCreated) BeginInvoke(a);
        }

        internal void Post(object m) => page.Post(m);

        internal void Reply(string id, string path, string error = null)
        {
            var d = new Dictionary<string, object> { ["id"] = id, ["path"] = path };
            if (error != null) d["error"] = error;
            Post(d);
        }

        /// <summary>The main page's pixels per CSS pixel.</summary>
        internal double PageScale => page.Scale;

        /* ---------- putting a note back: the note window dropped on the cards ---------- */

        /// <summary>
        /// The note window is being moved with the mouse at p (on screen). Over this window (and with nothing
        /// else in between), the page shows where to drop it.
        /// </summary>
        internal void DockHover(Point p)
        {
            var at = DockPoint(p);
            if (at == null)
            {
                if (dockShown) { dockShown = false; _ = page.Eval("window.__nsDockHover && __nsDockHover(null)"); }
                return;
            }
            int now = Environment.TickCount;
            if (dockShown && now - dockSent < 30) return;
            dockShown = true;
            dockSent = now;
            _ = page.Eval($"window.__nsDockHover && __nsDockHover({at.Value.X},{at.Value.Y})");
        }

        /// <summary>The note window was let go with the mouse at p: true when the page takes the note back.</summary>
        internal async Task<bool> DockDrop(Point p)
        {
            var at = DockPoint(p);
            dockShown = false;
            if (at == null) { await page.Eval("window.__nsDockHover && __nsDockHover(null)"); return false; }
            return await page.Eval($"!!(window.__nsDockDrop && __nsDockDrop({at.Value.X},{at.Value.Y}))") == "true";
        }

        // p in the page's CSS pixels, or null when the mouse isn't over this window's page
        Point? DockPoint(Point p)
        {
            if (!Visible || WindowState == FormWindowState.Minimized || page.Core == null || notes == null) return null;
            var c = PointToClient(p);
            if (!ClientRectangle.Contains(c) || !Native.IsWindowBelowAt(notes.Handle, Handle, p)) return null;
            double s = page.Scale;
            return new Point((int)Math.Round(c.X / s), (int)Math.Round(c.Y / s));
        }

        /// <summary>The note in the note window comes back to this window (state: what the editor needs).</summary>
        internal void DockIn(object state)
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            Post(new Dictionary<string, object> { ["type"] = "dockIn", ["state"] = state });
        }

        /* ---------- helpers for HostApi ---------- */

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
            page?.Fit();
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            page?.Moved();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            page?.Focus();
        }

        // Notes save as you type; before closing, each page gets up to 5 s to write the last change.
        // A note left open in the note window opens there again next time (unless it's locked).
        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if (closeConfirmed || page.Core == null || e.CloseReason == CloseReason.WindowsShutDown)
            {
                RememberNoteWindow();
                SavePlacement();
                notes?.CloseForGood();
                base.OnFormClosing(e);
                return;
            }
            e.Cancel = true;
            if (closing) return;
            closing = true;
            try
            {
                await page.WaitWhileBusy();
                if (notes != null) await notes.WaitWhileBusy();
            }
            catch { /* a page isn't answering: close anyway */ }
            closeConfirmed = true;
            Close();
        }

        // (if Windows closed the note window first, it remembered for itself)
        void RememberNoteWindow() => notes?.Remember();

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            page.Close();
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
