using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
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
            Icon = WebAssets.AppIcon() ?? Icon;
            StartPosition = FormStartPosition.Manual;
            narrow = settings.Get("pref:hideBase") == "1";
            // with the frame hidden, clear from the start (the page says so again once it's up)
            frameless = seeThrough = settings.Get("pref:hideFrame") == "1";
            BackColor = seeThrough ? Color.Black : Bg;   // black is clear where the window is see-through
            FitMinimumSize();
            sizer = new EdgeSizer(this);
            page = new PageHost(this, frameless ? Color.Transparent : Bg);
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
                // the page is told about a hidden frame in its address, so its first paint is clear too
                await page.Start(env, frameless ? "index.html?frameless" : "index.html", new HostApi(this, settings, lib, null));
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
            if (!(frameless ? ShownPart : ClientRectangle).Contains(c) || !Native.IsWindowBelowAt(w.Handle, Handle, p)) return null;
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
                settings.SetPath("app:backupDir", Path.GetDirectoryName(d.FileName));
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
                settings.SetPath("app:backupDir", Path.GetDirectoryName(d.FileName));
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
            foreach (string c in new[] { settings.GetPath("app:backupDir"), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) })
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

        bool seeThrough;

        /// <summary>
        /// The see-through background (Settings): the desktop shows through wherever the page is transparent,
        /// the space around the cards. The page sets how much of its dark background stays there.
        /// </summary>
        internal bool SetSeeThrough(bool on)
        {
            seeThrough = on && Native.SeeThrough(Handle, true);
            if (!seeThrough) Native.SeeThrough(Handle, false);
            if (page.Controller != null) page.Controller.DefaultBackgroundColor = seeThrough ? Color.Transparent : Bg;
            BackColor = seeThrough ? Color.Black : Bg;   // black is clear where the window is see-through
            if (seeThrough) Native.ClearUnderChildren(Handle);
            Invalidate();
            return seeThrough;
        }

        // After the frame comes or goes: see-through set again, as for a new window. Windows keeps the frame the
        // window had when it was turned on (a frame put back after it was hidden came out see-through too).
        void RefreshSeeThrough()
        {
            if (!seeThrough) return;
            Native.SeeThrough(Handle, false);
            Native.SeeThrough(Handle, true);
            Native.ClearUnderChildren(Handle);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (seeThrough) Native.SeeThrough(Handle, true);   // a new handle starts solid
            ShowOnlyShown();
        }

        // With the spinner base hidden (Settings) only the cards show, so the window can be narrower:
        // down to NarrowWidth DIPs, though never wider than the usual minimum. With the frame hidden, the
        // cards' box has a smallest size of its own, and the window is that plus the toolbar's room.
        const int MinWidth = 560, MinHeight = 420, NarrowWidth = 340;
        const int MinCardsWidth = 360, MinCardsNarrowWidth = 240, MinCardsHeight = 280;
        bool narrow;

        internal void SetNarrow(bool on)
        {
            narrow = on;
            FitMinimumSize();
        }

        int SmallestCards => Px(narrow ? MinCardsNarrowWidth : MinCardsWidth);

        void FitMinimumSize() =>
            MinimumSize = frameless ? new Size(Px(cardsLeft) + SmallestCards, Px(cardsTop + MinCardsHeight))
                : new Size(narrow ? Math.Min(MinWidth, Px(NarrowWidth)) : MinWidth, MinHeight);

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            FitMinimumSize();
            sizer?.Restart();   // resized onto another screen: go on from here
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            page?.Fit();
            if (frameless && WindowState == FormWindowState.Normal) ShowOnlyShown();
            if (seeThrough && IsHandleCreated && WindowState != FormWindowState.Minimized) Native.ClearUnderChildren(Handle);
        }

        protected override void WndProc(ref Message m)
        {
            if (sizer != null && sizer.Handle(ref m)) return;
            base.WndProc(ref m);
        }

        /* ---------- the window frame hidden (Settings) ----------
           No title bar or border, and the space around the cards is clear, so they seem to stand on the desktop.
           The page keeps room to the left of the cards and above them for its panel and a title strip (the
           toolbar). The window takes in that room too, but only the part the page shows is kept (a window
           region): the cards' box with the toolbar hidden, toolbar and cards with it out. So the toolbar comes
           and goes without the window changing size, which would make the page jump: a web view resized from its
           left or top edge shows its old picture in the wrong place for a moment. */

        bool frameless;
        // DIPs (the page's CSS pixels): where the cards' box starts in the window, and where the part shown starts
        double cardsLeft = 264, cardsTop = 34, shownLeft = 264, shownTop = 34;
        // moved onto the screen to make room for the toolbar: how far, and where the window was put
        Point nudge;
        Rectangle nudged;
        readonly EdgeSizer sizer;   // resizing from the page's edges

        internal bool Frameless => frameless;

        int Px(double dips) => (int)Math.Round(dips * DeviceDpi / 96.0);

        // the part of the window shown (pixels from its top-left corner), and the cards' box on screen
        Rectangle ShownPart => Rectangle.FromLTRB(Px(shownLeft), Px(shownTop), Width, Height);
        Rectangle Cards => Rectangle.FromLTRB(Left + Px(cardsLeft), Top + Px(cardsTop), Right, Bottom);

        void ShowOnlyShown()
        {
            if (frameless && IsHandleCreated) Native.ShowOnly(Handle, ShownPart);
        }

        // No title bar or border; still a minimise box (for the taskbar button and Win+D) and the system menu (Alt+Space).
        // UpdateStyles applies these to the window as it is.
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                if (frameless)
                {
                    cp.Style = (cp.Style & ~(Native.WS_CAPTION | Native.WS_THICKFRAME | Native.WS_MAXIMIZEBOX)) | Native.WS_SYSMENU | Native.WS_MINIMIZEBOX;
                    cp.ExStyle &= ~(Native.WS_EX_DLGMODALFRAME | Native.WS_EX_WINDOWEDGE | Native.WS_EX_CLIENTEDGE);
                }
                return cp;
            }
        }

        /// <summary>
        /// The frame hidden or shown, and where the page has its parts (DIPs): the cards' box from (cardsLeft,
        /// cardsTop) to the bottom-right corner, the part shown from (shownLeft, shownTop), the toolbar being left
        /// of the cards and above them. The cards keep their place on screen. fitWidth: the cards' box is made
        /// this narrow (DIPs, keeping its middle) when it's wider; 0 leaves it. Whether the frame is hidden now.
        /// </summary>
        internal bool SetFrame(bool hidden, double cardsLeft, double cardsTop, double shownLeft, double shownTop, double fitWidth)
        {
            if (!hidden)
            {
                if (frameless) ShowFrame(shownLeft, cardsTop);
                return false;
            }
            Rectangle cards;
            bool coming = !frameless;
            if (coming)
            {
                if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
                // framed, the page starts with the panel: the cards' box starts where it ends
                var client = RectangleToScreen(ClientRectangle);
                cards = Rectangle.FromLTRB(client.Left + Px(cardsLeft - shownLeft), client.Top, client.Right, client.Bottom);
                frameless = true;
                BackColor = Color.Black;
                UpdateStyles();
            }
            else
            {
                cards = Cards;
                if (Bounds == nudged) cards.Offset(-nudge.X, -nudge.Y);   // moved for the toolbar and not since: back
            }
            this.cardsLeft = cardsLeft;
            this.cardsTop = cardsTop;
            this.shownLeft = shownLeft;
            this.shownTop = shownTop;
            FitMinimumSize();
            if (WindowState == FormWindowState.Normal)
            {
                int w = Math.Max(Px(fitWidth), SmallestCards);
                if (fitWidth > 0 && w < cards.Width) cards = new Rectangle(cards.X + (cards.Width - w) / 2, cards.Y, w, cards.Height);
                PlaceAround(cards);
            }
            ShowOnlyShown();
            if (coming) RefreshSeeThrough();
            return true;
        }

        // The window around the cards' box, with the toolbar's room to the left and above. If the part shown
        // would start off the screen, the window moves onto it (by no more than the toolbar's own size).
        void PlaceAround(Rectangle cards)
        {
            var r = Rectangle.FromLTRB(cards.Left - Px(cardsLeft), cards.Top - Px(cardsTop), cards.Right, cards.Bottom);
            var wa = Screen.FromRectangle(cards).WorkingArea;
            int dx = Math.Max(0, Math.Min(wa.Left - (r.Left + Px(shownLeft)), Px(cardsLeft - shownLeft)));
            int dy = Math.Max(0, Math.Min(wa.Top - (r.Top + Px(shownTop)), Px(cardsTop - shownTop)));
            r.Offset(dx, dy);
            nudge = new Point(dx, dy);
            Bounds = r;
            nudged = Bounds;
        }

        // The frame back: the page below the title strip (panel and cards) keeps its place, inside the frame
        void ShowFrame(double panelLeft, double stripHeight)
        {
            var keep = Rectangle.FromLTRB(Left + Px(panelLeft), Top + Px(stripHeight), Right, Bottom);
            frameless = false;
            Native.ShowOnly(Handle, null);
            BackColor = seeThrough ? Color.Black : Bg;
            UpdateStyles();
            FitMinimumSize();
            Rectangle b = Bounds, c = RectangleToScreen(ClientRectangle);
            var r = new Rectangle(keep.X - (c.X - b.X), keep.Y - (c.Y - b.Y), keep.Width + b.Width - c.Width, keep.Height + b.Height - c.Height);
            var wa = Screen.FromRectangle(keep).WorkingArea;
            if (r.Top < wa.Top) r.Offset(0, wa.Top - r.Top);   // the title bar on the screen
            Bounds = r;
            RefreshSeeThrough();   // still see-through: the page says next whether it stays so
            Invalidate();
        }

        /// <summary>Moves the window with the mouse (or a finger) from the page, as a title bar does. Whether it moved.</summary>
        internal bool DragWindow(bool mouse)
        {
            if (mouse && !Native.PrimaryButtonDown()) return false;   // already let go: that was a click
            var before = Location;
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);   // returns when it's let go
            return Location != before;
        }

        /// <summary>Resizes the window from one of the page's edges ("left", "topright", …) until the mouse is let go.</summary>
        internal void SizeWindow(string edge, bool mouse)
        {
            int e = EdgeSizer.Named(edge);
            if (!frameless || e == 0 || WindowState != FormWindowState.Normal || (mouse && !Native.PrimaryButtonDown())) return;
            Native.ReleaseCapture();   // from the page, which had the mouse
            sizer.Start(e, Cursor.Position, mouse);
        }

        /// <summary>The title strip's buttons: "minimize" or "close".</summary>
        internal void WindowCommand(string what)
        {
            if (what == "minimize") Later(() => WindowState = FormWindowState.Minimized);
            else if (what == "close") Later(Close);
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
            if (frameless && string.IsNullOrEmpty(fixedPlace) && RestoreFloat()) return;
            string[] p = (string.IsNullOrEmpty(fixedPlace) ? settings.Get("app:window") ?? "" : fixedPlace + ",0").Split(',');
            if (p.Length < 5 || !int.TryParse(p[0], out int x) || !int.TryParse(p[1], out int y) ||
                !int.TryParse(p[2], out int w) || !int.TryParse(p[3], out int h)) return;
            var r = new Rectangle(x, y, w, h);
            if (w < Math.Min(400, MinimumSize.Width) || h < 300 || !Screen.AllScreens.Any(sc => Rectangle.Intersect(sc.WorkingArea, r).Width >= 200 && Rectangle.Intersect(sc.WorkingArea, r).Height >= 100)) return;
            Bounds = r;
            if (p[4] == "1" && !frameless) WindowState = FormWindowState.Maximized;
        }

        // "app:float": with the frame hidden, the window (pixels) and where its parts start (DIPs, see SetFrame):
        // x,y,width,height,cardsLeft,cardsTop,shownLeft,shownTop. "app:window" keeps the framed window's place.
        const string FloatKey = "app:float";

        bool RestoreFloat()
        {
            string[] p = (settings.Get(FloatKey) ?? "").Split(',');
            var d = new double[4];
            if (p.Length < 8 || !int.TryParse(p[0], out int x) || !int.TryParse(p[1], out int y) || !int.TryParse(p[2], out int w) ||
                !int.TryParse(p[3], out int h)) return false;
            for (int i = 0; i < 4; i++)
                if (!double.TryParse(p[4 + i], NumberStyles.Float, CultureInfo.InvariantCulture, out d[i]) || d[i] < 0 || d[i] > 1000) return false;
            var shown = Rectangle.FromLTRB(x + Px(d[2]), y + Px(d[3]), x + w, y + h);
            if (shown.Width < 100 || shown.Height < 100 ||
                !Screen.AllScreens.Any(sc => { var i = Rectangle.Intersect(sc.WorkingArea, shown); return i.Width >= 100 && i.Height >= 60; })) return false;
            cardsLeft = d[0];
            cardsTop = d[1];
            shownLeft = d[2];
            shownTop = d[3];
            FitMinimumSize();
            Bounds = new Rectangle(x, y, w, h);
            return true;
        }

        void SavePlacement()
        {
            try
            {
                var r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                if (frameless)
                    settings.Set(FloatKey, string.Join(",", r.X, r.Y, r.Width, r.Height) + "," +
                        string.Join(",", new[] { cardsLeft, cardsTop, shownLeft, shownTop }.Select(v => v.ToString("0.##", CultureInfo.InvariantCulture))));
                else
                    settings.Set("app:window", $"{r.X},{r.Y},{r.Width},{r.Height},{(WindowState == FormWindowState.Maximized ? 1 : 0)}");
            }
            catch { }
        }
    }
}
