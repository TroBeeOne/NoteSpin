using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace NoteSpin
{
    /// <summary>
    /// A note in a window of its own, for keeping on the desktop. Each note window holds one note, and a note
    /// is open in one place at a time (here, in another note window, or in the main window). Windows are made
    /// ahead, hidden, so one appears at once (even in the middle of a drag); see MainForm.TakeWindow.
    /// It has no frame: the note's top strip moves it, the thin edge around the note resizes it. The page in it
    /// (web\note.html) runs the same editor as the main window. It isn't owned by the main window, so it stays
    /// on screen when that's minimized; closing the main window closes it too.
    /// Each note remembers its own place, size and pin, so it comes back where it was.
    /// </summary>
    internal sealed class NoteWindow : Form
    {
        // "app:noteWindow": where a note window was last (where a note goes the first time it's out);
        // "app:noteWindow:<note id>" and "app:noteTop:<note id>": each note's own place and pin
        const string LastPlaceKey = "app:noteWindow", PlaceKeyPrefix = "app:noteWindow:", PinKeyPrefix = "app:noteTop:";
        static readonly Color DefaultPaper = Color.FromArgb(0xFF, 0xF2, 0xA6), DefaultBand = Color.FromArgb(0xE9, 0xCF, 0x5C);

        readonly MainForm main;
        readonly Settings settings;
        readonly PageHost page;
        readonly List<object> waiting = new List<object>();   // messages for the page before it's listening
        readonly Timer followTimer = new Timer { Interval = 10 };
        Point followOffset;
        bool followMouse, following, movingByStrip, ready, allowClose, showQuietly;
        int followStarted;

        /// <summary>Which window this is, for the main page ("w1", "w2", …).</summary>
        public string WinId { get; }

        /// <summary>Given a note (or about to be), so not free for another; false: hidden and waiting for one.</summary>
        public bool InUse { get; set; }

        /// <summary>The page is up, so a note can be sent to it.</summary>
        public bool Started { get; private set; }

        /// <summary>Free for the next note: made, hidden and not in use.</summary>
        public bool IsFree => Started && !InUse && !Visible && !IsDisposed && !allowClose;

        /// <summary>The note closed (or went back to the main window) and the window hid: it's free again.</summary>
        public event Action<NoteWindow> Freed;

        /// <summary>A note is open in the window (or about to be: the page reports back once it's shown).</summary>
        public bool HasNote { get; private set; }
        public string NoteId { get; private set; }
        public bool NoteLocked { get; private set; }

        public NoteWindow(MainForm main, Settings settings, string winId)
        {
            this.main = main;
            this.settings = settings;
            WinId = winId;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            Text = "Note — NoteSpin";
            Icon = WebAssets.AppIcon() ?? Icon;
            BackColor = DefaultBand;
            page = new PageHost(this, DefaultPaper);
            sizer = new EdgeSizer(this);
            sizer.Ended += SavePlacement;
            followTimer.Tick += (s, e) => Follow();
            FitToDpi();
        }

        /// <summary>The note id in a note's state (see Open), or null for a new note.</summary>
        public static string NoteIdOf(Dictionary<string, object> state) =>
            state != null && state.TryGetValue("note", out object n) && n is Dictionary<string, object> nd &&
            nd.TryGetValue("id", out object id) && id is string s && s.Length > 0 ? s : null;

        // a minimise box so a click on its taskbar button can minimise it; a system menu for Close there and Alt+Space
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= Native.WS_MINIMIZEBOX | Native.WS_SYSMENU;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => showQuietly;

        public async Task Start(CoreWebView2Environment env, HostApi api)
        {
            var _ = Handle;   // the page needs a window to live in; it stays hidden until a note opens
            await page.Start(env, "note.html", api);
            Started = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle);
        }

        /* ---------- size, edges and DPI ---------- */

        double DpiScale => DeviceDpi / 96.0;
        int Grip => Math.Max(4, (int)Math.Round(5 * DpiScale));   // the edge for resizing

        void FitToDpi()
        {
            MinimumSize = new Size((int)(250 * DpiScale), (int)(200 * DpiScale));
            page.Margin = Grip;
            page?.Fit();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            FitToDpi();
            sizer.Restart();   // resized onto another screen: go on from here
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            page?.Fit();
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            SavePlacement();
        }

        // The page covers everything but the edge, so only the edge reaches here: it resizes the window.
        // Windows is told which part of the edge the mouse is on, but the arrows and the resizing are done
        // here (EdgeSizer): Windows' own only work on windows with a sizing frame, which this hasn't.
        protected override void WndProc(ref Message m)
        {
            if (sizer.Handle(ref m)) return;
            switch (m.Msg)
            {
                case Native.WM_NCHITTEST:
                    base.WndProc(ref m);
                    if ((int)m.Result == Native.HTCLIENT) m.Result = (IntPtr)EdgeAt(PointToClient(EdgeSizer.LParamPoint(m.LParam)));
                    return;
                case Native.WM_SETCURSOR:   // the mouse is over the window: the arrows on the edge, before it's pressed
                    int over = (short)(m.LParam.ToInt64() & 0xFFFF);
                    if (sizer.Edge == 0 && over >= Native.HTLEFT && over <= Native.HTBOTTOMRIGHT && WindowState == FormWindowState.Normal)
                    {
                        Cursor.Current = EdgeSizer.CursorFor(over);
                        m.Result = (IntPtr)1;
                        return;
                    }
                    break;
                case Native.WM_NCLBUTTONDOWN:
                case Native.WM_NCLBUTTONDBLCLK:   // a quick second press starts a resize too (rather than Windows' double-click)
                    int edge = (int)m.WParam;
                    if (edge >= Native.HTLEFT && edge <= Native.HTBOTTOMRIGHT)
                    {
                        if (WindowState == FormWindowState.Normal) sizer.Start(edge, EdgeSizer.LParamPoint(m.LParam));   // screen pixels
                        return;
                    }
                    break;
            }
            base.WndProc(ref m);
        }

        /// <summary>Which part of the edge a point (client pixels) is on: HTLEFT … HTBOTTOMRIGHT, or HTCLIENT.</summary>
        int EdgeAt(Point p)
        {
            int g = Grip, corner = g * 4, w = ClientSize.Width, h = ClientSize.Height;
            bool left = p.X < g, right = p.X >= w - g, top = p.Y < g, bottom = p.Y >= h - g;
            // the corners reach a little way along each edge, so they're easy to catch
            if (top || bottom) { left |= p.X < corner; right |= p.X >= w - corner; }
            if (left || right) { top |= p.Y < corner; bottom |= p.Y >= h - corner; }
            return top && left ? Native.HTTOPLEFT : top && right ? Native.HTTOPRIGHT : bottom && left ? Native.HTBOTTOMLEFT
                : bottom && right ? Native.HTBOTTOMRIGHT : left ? Native.HTLEFT : right ? Native.HTRIGHT : top ? Native.HTTOP
                : bottom ? Native.HTBOTTOM : Native.HTCLIENT;
        }

        readonly EdgeSizer sizer;   // resizing from the edge

        /* ---------- where it goes ---------- */

        /// <summary>
        /// Where the note was the last time it was out. A note that hasn't been out before goes a step down and to
        /// the right of the note window shown last (so they don't stack up), else where a note window was last,
        /// else beside the main window. It takes the size note windows had last.
        /// </summary>
        Rectangle Placement()
        {
            var size = new Size((int)(360 * DpiScale), (int)(440 * DpiScale));
            bool hasLast = TryPlace(settings.Get(LastPlaceKey), out Rectangle last);
            if (NoteId != null && TryPlace(settings.Get(PlaceKeyPrefix + NoteId), out Rectangle own))
            {
                if (OnScreen(own)) return own;
                size = own.Size;
            }
            else if (hasLast) size = last.Size;
            var other = main.LastShownNoteWindow(this);
            if (other != null) return Cascade(other.Value.Location, size);
            if (hasLast && OnScreen(new Rectangle(last.Location, size))) return new Rectangle(last.Location, size);
            var m = main.WindowState == FormWindowState.Normal ? main.Bounds : main.RestoreBounds;
            var wa = Screen.FromRectangle(m).WorkingArea;
            int nx = m.Right + 12;
            if (nx + size.Width > wa.Right) nx = Math.Max(wa.Left, m.Right - size.Width - 48);
            int ny = Math.Max(wa.Top, Math.Min(m.Top + 64, wa.Bottom - size.Height));
            return new Rectangle(nx, ny, size.Width, size.Height);
        }

        // A step down and to the right of from, clear of the other note windows; back to the top of the screen
        // when it would run off the bottom or the right
        Rectangle Cascade(Point from, Size size)
        {
            int step = (int)Math.Round(28 * DpiScale);
            var wa = Screen.FromPoint(from).WorkingArea;
            var p = new Point(from.X + step, from.Y + step);
            for (int i = 0; i < 40; i++)
            {
                if (p.X + size.Width > wa.Right || p.Y + size.Height > wa.Bottom) p = new Point(wa.Left + step, wa.Top + step);
                if (!main.NoteWindowNear(p, this)) break;
                p.Offset(step, step);
            }
            return new Rectangle(p, size);
        }

        bool TryPlace(string saved, out Rectangle r)
        {
            r = Rectangle.Empty;
            string[] p = (saved ?? "").Split(',');
            if (p.Length < 4 || !int.TryParse(p[0], out int x) || !int.TryParse(p[1], out int y) ||
                !int.TryParse(p[2], out int w) || !int.TryParse(p[3], out int h) || w < MinimumSize.Width || h < MinimumSize.Height) return false;
            r = new Rectangle(x, y, w, h);
            return true;
        }

        static bool OnScreen(Rectangle r) =>
            Screen.AllScreens.Any(sc => { var i = Rectangle.Intersect(sc.WorkingArea, r); return i.Width >= 100 && i.Height >= 60; });

        void SavePlacement()
        {
            if (!Visible || WindowState != FormWindowState.Normal) return;
            var r = Bounds;
            string v = $"{r.X},{r.Y},{r.Width},{r.Height}";
            settings.Set(LastPlaceKey, v);
            if (NoteId != null) settings.Set(PlaceKeyPrefix + NoteId, v);
        }

        /// <summary>Kept on top of other windows; each note remembers its own.</summary>
        public bool Pinned
        {
            get => TopMost;
            set { TopMost = value; if (NoteId != null) settings.Set(PinKeyPrefix + NoteId, value ? "1" : ""); }
        }

        /* ---------- showing a note ---------- */

        /// <summary>Opens a note in the page (state: what the editor needs, from the page that had it).</summary>
        public void Open(Dictionary<string, object> state)
        {
            InUse = true;
            HasNote = true;
            NoteId = NoteIdOf(state);
            NoteLocked = state.TryGetValue("key", out object k) && k != null;
            TopMost = NoteId != null && settings.Get(PinKeyPrefix + NoteId) == "1";
            Send(new Dictionary<string, object> { ["type"] = "open", ["state"] = state, ["pinned"] = TopMost });
        }

        /// <summary>Shows the window where it was last; activate: take the focus (not when NoteSpin starts with it).</summary>
        public void ShowNote(bool activate)
        {
            if (!Visible)
            {
                Bounds = Placement();
                showQuietly = !activate;
                Show();
                showQuietly = false;
            }
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            if (activate) Activate();
            else Native.BringToFrontQuietly(Handle);
            main.NoteWindowShown(this);
        }

        /// <summary>
        /// Shows the window under the mouse, held at grab (CSS pixels from the note's top-left in the main window),
        /// and moves it with the mouse until the button is let go: the note was dragged out of the main window.
        /// </summary>
        public void ShowFollowing(double grabX, double grabY, double mainScale, bool mouse)
        {
            var size = Placement().Size;
            int g = Grip;
            int gx = (int)Math.Max(g + 12, Math.Min(grabX * mainScale, size.Width - 48));
            int gy = (int)Math.Max(g + 4, Math.Min(grabY * mainScale, g + 30 * DpiScale));
            var c = Cursor.Position;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Bounds = new Rectangle(c.X - gx, c.Y - gy, size.Width, size.Height);
            if (!Visible)
            {
                showQuietly = true;   // the main window keeps the mouse until the drag ends
                Show();
                showQuietly = false;
            }
            Native.BringToFrontQuietly(Handle);
            main.NoteWindowShown(this);
            followOffset = new Point(gx, gy);
            followMouse = mouse;
            following = true;
            followStarted = Environment.TickCount;
            followTimer.Start();
        }

        void Follow()
        {
            if (!following) { followTimer.Stop(); return; }
            // a mouse drag ends when the button comes up; a touch drag when the page says so (or after a while)
            if (followMouse ? !Native.PrimaryButtonDown() : Environment.TickCount - followStarted > 30000) { EndFollow(); return; }
            var c = Cursor.Position;
            var to = new Point(c.X - followOffset.X, c.Y - followOffset.Y);
            if (to != Location) Location = to;
            main.DockHover(this, c);
        }

        /// <summary>The drag that brought the note out has ended (the page saw the pointer come up).</summary>
        public void EndFollow()
        {
            if (!following) return;
            following = false;
            followTimer.Stop();
            SavePlacement();
            Activate();
            _ = FinishMove();
        }

        /// <summary>Moves the window with the mouse (or finger) from its top strip, the way a title bar does.</summary>
        public void MoveByStrip(bool mouse)
        {
            if (mouse && !Native.PrimaryButtonDown()) return;   // already let go: that was a click
            movingByStrip = true;
            try
            {
                Native.ReleaseCapture();
                Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);   // returns when it's let go
            }
            finally { movingByStrip = false; }
            SavePlacement();
            _ = FinishMove();
        }

        // Dropped on the main window's cards: the note goes back in there
        async Task FinishMove()
        {
            if (HasNote && await main.DockDrop(this, Cursor.Position)) Send(new Dictionary<string, object> { ["type"] = "dock" });
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            page?.Moved();
            if (movingByStrip) main.DockHover(this, Cursor.Position);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            page?.Focus();
        }

        /// <summary>The note was closed (or went back into the main window).</summary>
        public void HideNote()
        {
            following = false;
            followTimer.Stop();
            SavePlacement();
            HasNote = false;
            NoteId = null;
            NoteLocked = false;
            TopMost = false;
            if (Visible) Hide();
            if (InUse) { InUse = false; Freed?.Invoke(this); }
        }

        /* ---------- the page ---------- */

        public void Send(object message)
        {
            if (ready) page.Post(message);
            else waiting.Add(message);
        }

        public void PageReady()
        {
            ready = true;
            foreach (var m in waiting) page.Post(m);
            waiting.Clear();
        }

        /// <summary>What the page reports about the note it shows: for the taskbar, the colour of the edge, and the main window.</summary>
        public void SetState(Dictionary<string, object> s)
        {
            string before = NoteId;
            HasNote = s.TryGetValue("open", out object o) && o is bool b && b;
            NoteId = HasNote && s.TryGetValue("id", out object id) && id is string sid && sid.Length > 0 ? sid : null;
            // a new note gets its id when it's first saved: its place and pin are kept under it from then on
            if (before == null && NoteId != null) { SavePlacement(); if (TopMost) settings.Set(PinKeyPrefix + NoteId, "1"); }
            NoteLocked = HasNote && s.TryGetValue("locked", out object l) && l is bool lb && lb;
            string title = s.TryGetValue("title", out object t) ? (t as string ?? "").Trim() : "";
            if (title.Length > 80) title = title.Substring(0, 80) + "…";
            Text = (title.Length > 0 ? title : "New note") + " — NoteSpin";
            if (s.TryGetValue("band", out object bc) && TryColor(bc as string, out Color band)) BackColor = band;
            if (s.TryGetValue("paper", out object pc) && TryColor(pc as string, out Color paper) && page.Controller != null)
                page.Controller.DefaultBackgroundColor = paper;
        }

        static bool TryColor(string hex, out Color c)
        {
            c = Color.Empty;
            if (hex == null || hex.Length != 7 || hex[0] != '#') return false;
            try { c = ColorTranslator.FromHtml(hex); return true; } catch { return false; }
        }

        public Task WaitWhileBusy() => page.WaitWhileBusy();

        /* ---------- closing ---------- */

        // Closed from the taskbar, Alt+F4 or anything else but NoteSpin itself: the page closes the note
        // (saving it) and the window hides, free for another note (MainForm keeps one free window spare)
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!allowClose && e.CloseReason != CloseReason.WindowsShutDown)
            {
                e.Cancel = true;
                if (ready && HasNote) Send(new Dictionary<string, object> { ["type"] = "close" });
                else HideNote();
                return;
            }
            // Windows is shutting down and may close this before the main window: the notes that are out are noted now
            if (e.CloseReason == CloseReason.WindowsShutDown) main.RememberOpenNotes();
            base.OnFormClosing(e);
        }

        /// <summary>NoteSpin is closing.</summary>
        public void CloseForGood()
        {
            allowClose = true;
            followTimer.Stop();
            if (!IsDisposed) Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            page.Close();
            followTimer.Dispose();
            base.OnFormClosed(e);
        }
    }
}
