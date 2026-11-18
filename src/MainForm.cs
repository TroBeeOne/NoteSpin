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
    /// note windows, where notes can be kept outside it (see NoteWindow).
    /// </summary>
    internal sealed class MainForm : Form
    {
        static readonly Color Bg = Color.FromArgb(17, 19, 24);

        readonly Settings settings;
        readonly Library lib;
        readonly PageHost page;
        string startupFile;
        CoreWebView2Environment env;
        readonly List<NoteWindow> windows = new List<NoteWindow>();   // note windows: open ones, and the spare
        readonly HashSet<string> opening = new HashSet<string>();     // notes on their way to a window
        Task<NoteWindow> making;   // the next spare note window, being made
        NoteWindow lastShown;
        int windowCount;
        bool closeConfirmed, closing, dockShown, notesRemembered;
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

        // The first note window is made a moment after the cards are up, so it's ready when a note is dragged out
        async void OnFirstPageLoaded(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            page.Core.NavigationCompleted -= OnFirstPageLoaded;
            await Task.Delay(1500);
            KeepSpare();
        }

        /* ---------- note windows ----------
           Each note that's out has a window of its own. One more is kept made and hidden, so the next note
           (dragged out, say) has one at once; while another is being made, a note waits for it. A window whose
           note closes hides and becomes the spare, or goes if there's a spare already. */

        /// <summary>A window for a note: the spare if there is one, else a new one. It's in use from now on.</summary>
        internal async Task<NoteWindow> TakeWindow()
        {
            if (closing || closeConfirmed) throw new InvalidOperationException("NoteSpin is closing.");
            NoteWindow w = windows.FirstOrDefault(x => x.IsFree);
            if (w != null) w.InUse = true;
            else
            {
                var t = making ?? MakeWindow();
                making = null;   // this one's taken: KeepSpare makes another
                w = await t;
            }
            SpareSoon();
            return w;
        }

        // A new note window, in use (KeepSpare frees it if nothing took it while it was made)
        async Task<NoteWindow> MakeWindow()
        {
            if (env == null) throw new InvalidOperationException("NoteSpin is still starting.");
            var w = new NoteWindow(this, settings, "w" + (++windowCount)) { InUse = true };
            windows.Add(w);
            w.FormClosed += (s, e) => { windows.Remove(w); if (lastShown == w) lastShown = null; };
            w.Freed += OnWindowFreed;
            try { await w.Start(env, new HostApi(this, settings, lib, w)); }
            catch
            {
                windows.Remove(w);
                try { w.CloseForGood(); } catch { }
                throw;
            }
            return w;
        }

        // Makes the spare window, unless there is one (or one's on its way)
        async void KeepSpare()
        {
            if (making != null || closing || closeConfirmed || env == null || windows.Any(x => x.IsFree)) return;
            var t = making = MakeWindow();
            NoteWindow w;
            try { w = await t; }
            catch { if (making == t) making = null; return; }   // tried again when a note needs a window
            if (making == t) { making = null; w.InUse = false; }
        }

        // the next spare is made a little after a window is taken, so it doesn't slow that one down
        void SpareSoon() => Later(async () => { await Task.Delay(1200); KeepSpare(); });

        // A note closed and its window hid: one free window stays as the spare, the others go
        void OnWindowFreed(NoteWindow w) => Later(async () =>
        {
            await Task.Delay(3000);
            foreach (var extra in windows.Where(x => x.IsFree).Skip(1).ToList()) extra.CloseForGood();
        });

        /// <summary>The windows with a note in them.</summary>
        internal IEnumerable<NoteWindow> OpenNoteWindows => windows.Where(w => w.HasNote && !w.IsDisposed).ToList();

        /// <summary>The window a note is out in (null when it isn't out).</summary>
        internal NoteWindow WindowFor(string noteId) =>
            string.IsNullOrEmpty(noteId) ? null : windows.FirstOrDefault(w => w.HasNote && !w.IsDisposed && w.NoteId == noteId);

        internal NoteWindow WindowById(string winId) => windows.FirstOrDefault(w => w.WinId == winId && !w.IsDisposed);

        /// <summary>A message for the note pages (all of them but except).</summary>
        internal void SendToNotes(object message, NoteWindow except = null)
        {
            foreach (var w in windows.ToList()) if (w != except && !w.IsDisposed) w.Send(message);
        }

        /// <summary>
        /// Opens a note in a window of its own (state: what the editor needs). A note that's out already just
        /// comes to the front. activate: the window takes the focus (not when NoteSpin starts with it).
        /// </summary>
        internal async Task OpenInWindow(Dictionary<string, object> state, bool activate)
        {
            string id = NoteWindow.NoteIdOf(state);
            var there = WindowFor(id);
            if (there != null) { there.ShowNote(true); return; }
            if (id != null && !opening.Add(id)) return;   // on its way already (a double click)
            try
            {
                var w = await TakeWindow();
                w.Open(state);
                w.ShowNote(activate);
            }
            finally { if (id != null) opening.Remove(id); }
        }

        /// <summary>
        /// The note open in this window moves to a window of its own. drag: it's being dragged out, held at
        /// (grabX, grabY) CSS pixels from its top-left corner, so the window follows the mouse until it's let go.
        /// </summary>
        internal async Task PopOut(Dictionary<string, object> state, bool drag, double grabX, double grabY, bool mouse)
        {
            double scale = page.Scale;
            var w = await TakeWindow();
            w.Open(state);
            if (drag) w.ShowFollowing(grabX, grabY, scale, mouse);
            else w.ShowNote(true);
        }

        internal void NoteWindowShown(NoteWindow w) => lastShown = w;

        /// <summary>Where the note window shown last is (not counting except), for placing the next one beside it.</summary>
        internal Rectangle? LastShownNoteWindow(NoteWindow except)
        {
            bool onScreen(NoteWindow x) => x != null && x != except && !x.IsDisposed && x.Visible && x.WindowState == FormWindowState.Normal;
            var w = onScreen(lastShown) ? lastShown : windows.LastOrDefault(onScreen);
            return w?.Bounds;
        }

        /// <summary>Whether another note window has its top-left corner at (or within a few pixels of) p.</summary>
        internal bool NoteWindowNear(Point p, NoteWindow except) =>
            windows.Any(w => w != except && !w.IsDisposed && w.Visible && Math.Abs(w.Left - p.X) <= 4 && Math.Abs(w.Top - p.Y) <= 4);

        /// <summary>
        /// NoteSpin is closing: the notes that are out (but not locked ones, whose password isn't kept) open in
        /// their windows again next time. Only the first call counts, so windows closing one by one change nothing.
        /// </summary>
        internal void RememberOpenNotes()
        {
            if (notesRemembered) return;
            notesRemembered = true;
            try
            {
                var ids = windows.Where(w => w.HasNote && !w.NoteLocked && !string.IsNullOrEmpty(w.NoteId) && !w.IsDisposed)
                    .Select(w => w.NoteId).Distinct();
                settings.Set("app:noteOpen", string.Join(",", ids));
            }
            catch { }
        }

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

        /* ---------- putting a note back: a note window dropped on the cards ---------- */

        /// <summary>
        /// A note window is being moved with the mouse at p (on screen). Over this window (and with nothing
        /// else in between), the page shows where to drop it.
        /// </summary>
        internal void DockHover(NoteWindow w, Point p)
        {
            var at = DockPoint(w, p);
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

        /// <summary>A note window was let go with the mouse at p: true when the page takes the note back.</summary>
        internal async Task<bool> DockDrop(NoteWindow w, Point p)
        {
            var at = DockPoint(w, p);
            dockShown = false;
            if (at == null) { await page.Eval("window.__nsDockHover && __nsDockHover(null)"); return false; }
            return await page.Eval($"!!(window.__nsDockDrop && __nsDockDrop({at.Value.X},{at.Value.Y}))") == "true";
        }

        // p in the page's CSS pixels, or null when the mouse isn't over this window's page (just under note window w)
        Point? DockPoint(NoteWindow w, Point p)
        {
            if (!Visible || WindowState == FormWindowState.Minimized || page.Core == null || w == null || w.IsDisposed) return null;
            var c = PointToClient(p);
            if (!ClientRectangle.Contains(c) || !Native.IsWindowBelowAt(w.Handle, Handle, p)) return null;
            double s = page.Scale;
            return new Point((int)Math.Round(c.X / s), (int)Math.Round(c.Y / s));
        }

        /// <summary>A note from a note window comes back to this window (state: what the editor needs).</summary>
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
        // Notes left open in note windows open there again next time (unless they're locked).
        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if (closeConfirmed || page.Core == null || e.CloseReason == CloseReason.WindowsShutDown)
            {
                closeConfirmed = true;
                RememberOpenNotes();
                SavePlacement();
                foreach (var w in windows.ToList()) w.CloseForGood();
                base.OnFormClosing(e);
                return;
            }
            e.Cancel = true;
            if (closing) return;
            closing = true;
            try
            {
                await Task.WhenAll(new[] { page.WaitWhileBusy() }.Concat(windows.ToList().Select(w => w.WaitWhileBusy())));
            }
            catch { /* a page isn't answering: close anyway */ }
            closeConfirmed = true;
            Close();
        }

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
