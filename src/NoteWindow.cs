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
    /// A note in a window of its own, for keeping on the desktop: one note at a time, outside the main window.
    /// The window is made once, hidden, and used again, so it appears at once (even in the middle of a drag).
    /// It has no frame: the note's top strip moves it, the thin edge around the note resizes it. The page in it
    /// (web\note.html) runs the same editor as the main window. It isn't owned by the main window, so it stays
    /// on screen when that's minimized; closing the main window closes it too.
    /// </summary>
    internal sealed class NoteWindow : Form
    {
        const string PlaceKey = "app:noteWindow", PinKey = "app:noteTop";
        static readonly Color DefaultPaper = Color.FromArgb(0xFF, 0xF2, 0xA6), DefaultBand = Color.FromArgb(0xE9, 0xCF, 0x5C);

        readonly MainForm main;
        readonly Settings settings;
        readonly PageHost page;
        readonly List<object> waiting = new List<object>();   // messages for the page before it's listening
        readonly Timer followTimer = new Timer { Interval = 10 };
        Point followOffset;
        bool followMouse, following, movingByStrip, ready, allowClose, showQuietly;
        int followStarted;

        /// <summary>A note is open in the window (or about to be: the page reports back once it's shown).</summary>
        public bool HasNote { get; private set; }
        public string NoteId { get; private set; }
        public bool NoteLocked { get; private set; }

        public NoteWindow(MainForm main, Settings settings)
        {
            this.main = main;
            this.settings = settings;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            Text = "Note — NoteSpin";
            Icon = WebAssets.AppIcon() ?? Icon;
            BackColor = DefaultBand;
            TopMost = settings.Get(PinKey) == "1";
            page = new PageHost(this, DefaultPaper);
            followTimer.Tick += (s, e) => Follow();
            FitToDpi();
        }

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

        // The page covers everything but the edge, so only the edge reaches here: it resizes the window
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result != Native.HTCLIENT) return;
                long lp = m.LParam.ToInt64();
                var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                int g = Grip, corner = g * 4, w = ClientSize.Width, h = ClientSize.Height;
                bool left = p.X < g, right = p.X >= w - g, top = p.Y < g, bottom = p.Y >= h - g;
                // the corners reach a little way along each edge, so they're easy to catch
                if (top || bottom) { left |= p.X < corner; right |= p.X >= w - corner; }
                if (left || right) { top |= p.Y < corner; bottom |= p.Y >= h - corner; }
                int hit = top && left ? Native.HTTOPLEFT : top && right ? Native.HTTOPRIGHT : bottom && left ? Native.HTBOTTOMLEFT
                    : bottom && right ? Native.HTBOTTOMRIGHT : left ? Native.HTLEFT : right ? Native.HTRIGHT : top ? Native.HTTOP
                    : bottom ? Native.HTBOTTOM : Native.HTCLIENT;
                m.Result = (IntPtr)hit;
                return;
            }
            base.WndProc(ref m);
        }

        /* ---------- where it goes ---------- */

        /// <summary>Where it was last, else beside the main window.</summary>
        Rectangle Placement()
        {
            var size = new Size((int)(360 * DpiScale), (int)(440 * DpiScale));
            string[] p = (settings.Get(PlaceKey) ?? "").Split(',');
            if (p.Length >= 4 && int.TryParse(p[0], out int x) && int.TryParse(p[1], out int y) &&
                int.TryParse(p[2], out int w) && int.TryParse(p[3], out int h) && w >= MinimumSize.Width && h >= MinimumSize.Height)
            {
                var r = new Rectangle(x, y, w, h);
                if (OnScreen(r)) return r;
                size = r.Size;
            }
            var m = main.WindowState == FormWindowState.Normal ? main.Bounds : main.RestoreBounds;
            var wa = Screen.FromRectangle(m).WorkingArea;
            int nx = m.Right + 12;
            if (nx + size.Width > wa.Right) nx = Math.Max(wa.Left, m.Right - size.Width - 48);
            int ny = Math.Max(wa.Top, Math.Min(m.Top + 64, wa.Bottom - size.Height));
            return new Rectangle(nx, ny, size.Width, size.Height);
        }

        static bool OnScreen(Rectangle r) =>
            Screen.AllScreens.Any(sc => { var i = Rectangle.Intersect(sc.WorkingArea, r); return i.Width >= 100 && i.Height >= 60; });

        void SavePlacement()
        {
            if (!Visible || WindowState != FormWindowState.Normal) return;
            var r = Bounds;
            settings.Set(PlaceKey, $"{r.X},{r.Y},{r.Width},{r.Height}");
        }

        public bool Pinned
        {
            get => TopMost;
            set { TopMost = value; settings.Set(PinKey, value ? "1" : "0"); }
        }

        /* ---------- showing a note ---------- */

        /// <summary>Opens a note in the page (state: what the editor needs, from the page that had it).</summary>
        public void Open(Dictionary<string, object> state)
        {
            HasNote = true;
            NoteId = state.TryGetValue("note", out object n) && n is Dictionary<string, object> nd && nd.TryGetValue("id", out object id) ? id as string : null;
            NoteLocked = state.TryGetValue("key", out object k) && k != null;
            Send(new Dictionary<string, object> { ["type"] = "open", ["state"] = state });
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
            main.DockHover(c);
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
            if (HasNote && await main.DockDrop(Cursor.Position)) Send(new Dictionary<string, object> { ["type"] = "dock" });
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            page?.Moved();
            if (movingByStrip) main.DockHover(Cursor.Position);
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
            if (Visible) Hide();
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
            HasNote = s.TryGetValue("open", out object o) && o is bool b && b;
            NoteId = HasNote && s.TryGetValue("id", out object id) ? id as string : null;
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
        // (saving it) and the window hides, ready for the next note
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!allowClose && e.CloseReason != CloseReason.WindowsShutDown)
            {
                e.Cancel = true;
                if (ready && HasNote) Send(new Dictionary<string, object> { ["type"] = "close" });
                else HideNote();
                return;
            }
            if (e.CloseReason == CloseReason.WindowsShutDown) Remember();
            base.OnFormClosing(e);
        }

        /// <summary>NoteSpin is closing: the note open here (unless it's locked) opens here again next time.</summary>
        public void Remember()
        {
            try { settings.Set("app:noteOpen", HasNote && !NoteLocked && !string.IsNullOrEmpty(NoteId) ? NoteId : ""); }
            catch { }
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
