using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using static NoteSpin.GtkNative;

namespace NoteSpin
{
    /// <summary>
    /// A pop-out note in its own borderless GTK3 window on Linux.
    /// The note's top strip drags it via gtk_window_begin_move_drag, and the 5 px coloured border resizes it
    /// natively via gtk_window_begin_resize_drag (working on both Wayland and X11).
    /// </summary>
    internal sealed class NoteWindow
    {
        const string LastPlaceKey = "app:noteWindow", PlaceKeyPrefix = "app:noteWindow:", PinKeyPrefix = "app:noteTop:";
        const int Grip = 5;
        const int MinW = 250, MinH = 200;

        static readonly GdkRGBA DefaultPaper = new GdkRGBA(0xFF / 255.0, 0xF2 / 255.0, 0xA6 / 255.0, 1.0);
        static readonly GdkRGBA DefaultBand = new GdkRGBA(0xE9 / 255.0, 0xCF / 255.0, 0x5C / 255.0, 1.0);

        readonly MainForm main;
        readonly Settings settings;
        readonly PageHost page;
        readonly SignalStore signals = new SignalStore();
        readonly List<object> waiting = new List<object>();

        GdkRGBA bandColor = DefaultBand;
        bool ready, allowClose, pinned;

        public IntPtr Handle { get; }
        public string WinId { get; }
        public bool InUse { get; set; }
        public bool Started { get; private set; }
        public bool Visible { get; private set; }
        public bool IsDisposed { get; private set; }
        public bool IsFree => Started && !InUse && !Visible && !IsDisposed && !allowClose;

        public bool HasNote { get; private set; }
        public string NoteId { get; private set; }
        public bool NoteLocked { get; private set; }

        public event Action<NoteWindow> Freed;
        public event Action Destroyed;

        public NoteWindow(MainForm main, Settings settings, string winId)
        {
            this.main = main;
            this.settings = settings;
            WinId = winId;

            Handle = gtk_window_new(GTK_WINDOW_TOPLEVEL);
            gtk_window_set_title(Handle, "Note — NoteSpin");
            gtk_window_set_decorated(Handle, 0);
            gtk_window_set_resizable(Handle, 1);
            gtk_widget_set_size_request(Handle, MinW, MinH);
            gtk_container_set_border_width(Handle, Grip);
            gtk_widget_set_app_paintable(Handle, 1);
            gtk_widget_add_events(Handle, GDK_POINTER_MOTION_MASK | GDK_BUTTON_PRESS_MASK | GDK_LEAVE_NOTIFY_MASK);

            SetWindowIcon(Handle, WebAssets.Favicon());

            page = new PageHost(DefaultPaper);

            signals.Connect(Handle, "draw", new SignalDraw(OnDraw));
            signals.Connect(Handle, "motion-notify-event", new SignalEvent(OnMotion));
            signals.Connect(Handle, "leave-notify-event", new SignalEvent(OnLeave));
            signals.Connect(Handle, "button-press-event", new SignalEvent(OnBorderPress));
            signals.Connect(Handle, "configure-event", new SignalEvent(OnConfigure));
            signals.Connect(Handle, "delete-event", new SignalEvent(OnDeleteEvent));
            signals.Connect(Handle, "destroy", new SignalVoid1(OnDestroy));
        }

        public static string NoteIdOf(Dictionary<string, object> state) =>
            state != null && state.TryGetValue("note", out object n) && n is Dictionary<string, object> nd &&
            nd.TryGetValue("id", out object id) && id is string s && s.Length > 0 ? s : null;

        public Task Start(IntPtr webContext, HostApi api)
        {
            page.Start(webContext, "note.html", api, settings);
            gtk_container_add(Handle, page.Widget);
            gtk_widget_show(page.Widget); // child visible, top-level window stays hidden until ShowNote()
            Started = true;
            return Task.CompletedTask;
        }

        /* ---------- border paint & edge resizing ---------- */

        int OnDraw(IntPtr widget, IntPtr cr, IntPtr userData)
        {
            cairo_set_operator(cr, CAIRO_OPERATOR_SOURCE);
            cairo_set_source_rgba(cr, bandColor.Red, bandColor.Green, bandColor.Blue, 1.0);
            cairo_paint(cr);
            return 0;
        }

        int EdgeAt(double x, double y)
        {
            int w = gtk_widget_get_allocated_width(Handle);
            int h = gtk_widget_get_allocated_height(Handle);
            int corner = Grip * 4;
            bool left = x < Grip, right = x >= w - Grip, top = y < Grip, bottom = y >= h - Grip;
            if (top || bottom) { left |= x < corner; right |= x >= w - corner; }
            if (left || right) { top |= y < corner; bottom |= y >= h - corner; }
            if (top && left) return GDK_WINDOW_EDGE_NORTH_WEST;
            if (top && right) return GDK_WINDOW_EDGE_NORTH_EAST;
            if (bottom && left) return GDK_WINDOW_EDGE_SOUTH_WEST;
            if (bottom && right) return GDK_WINDOW_EDGE_SOUTH_EAST;
            if (left) return GDK_WINDOW_EDGE_WEST;
            if (right) return GDK_WINDOW_EDGE_EAST;
            if (top) return GDK_WINDOW_EDGE_NORTH;
            if (bottom) return GDK_WINDOW_EDGE_SOUTH;
            return -1;
        }

        static string CursorForEdge(int edge) => edge switch
        {
            GDK_WINDOW_EDGE_WEST or GDK_WINDOW_EDGE_EAST => "ew-resize",
            GDK_WINDOW_EDGE_NORTH or GDK_WINDOW_EDGE_SOUTH => "ns-resize",
            GDK_WINDOW_EDGE_NORTH_WEST or GDK_WINDOW_EDGE_SOUTH_EAST => "nwse-resize",
            GDK_WINDOW_EDGE_NORTH_EAST or GDK_WINDOW_EDGE_SOUTH_WEST => "nesw-resize",
            _ => "default",
        };

        void SetCursor(string name)
        {
            IntPtr gdkWin = gtk_widget_get_window(Handle);
            IntPtr disp = gdk_display_get_default();
            if (gdkWin == IntPtr.Zero || disp == IntPtr.Zero) return;
            IntPtr cursor = string.IsNullOrEmpty(name) ? IntPtr.Zero : gdk_cursor_new_from_name(disp, name);
            gdk_window_set_cursor(gdkWin, cursor);
            if (cursor != IntPtr.Zero) g_object_unref(cursor);
        }

        int OnMotion(IntPtr widget, IntPtr ev, IntPtr userData)
        {
            if (gdk_event_get_coords(ev, out double x, out double y) != 0)
                SetCursor(CursorForEdge(EdgeAt(x, y)));
            return 0;
        }

        int OnLeave(IntPtr widget, IntPtr ev, IntPtr userData)
        {
            SetCursor(null);
            return 0;
        }

        int OnBorderPress(IntPtr widget, IntPtr ev, IntPtr userData)
        {
            if (gdk_event_get_button(ev, out uint btn) == 0 || btn != 1) return 0;
            if (gdk_event_get_coords(ev, out double x, out double y) == 0) return 0;
            int edge = EdgeAt(x, y);
            if (edge < 0) return 0;
            gdk_event_get_root_coords(ev, out double rx, out double ry);
            gtk_window_begin_resize_drag(Handle, edge, 1, (int)Math.Round(rx), (int)Math.Round(ry), gdk_event_get_time(ev));
            return 1;
        }

        int OnConfigure(IntPtr widget, IntPtr ev, IntPtr userData)
        {
            if (Visible && HasNote) SavePlacement();
            return 0;
        }

        /* ---------- placement, pin & strip drag ---------- */

        public (int X, int Y, int W, int H) GetBounds()
        {
            gtk_window_get_position(Handle, out int x, out int y);
            gtk_window_get_size(Handle, out int w, out int h);
            return (x, y, w, h);
        }

        (int X, int Y, int W, int H, bool HasPos) Placement()
        {
            int w = 360, h = 440;
            bool hasLast = TryPlace(settings.Get(LastPlaceKey), out var last);
            if (NoteId != null && TryPlace(settings.Get(PlaceKeyPrefix + NoteId), out var own))
                return (own.X, own.Y, own.W, own.H, true);
            if (hasLast) { w = last.W; h = last.H; }
            var other = main.LastShownNoteWindow(this);
            if (other != null) return (other.Value.X + 28, other.Value.Y + 28, w, h, true);
            if (hasLast) return (last.X, last.Y, w, h, true);
            var m = main.GetBounds();
            return (m.X + m.W + 12, m.Y + 64, w, h, true);
        }

        static bool TryPlace(string saved, out (int X, int Y, int W, int H) r)
        {
            r = default;
            string[] p = (saved ?? "").Split(',');
            if (p.Length < 4 || !int.TryParse(p[0], out int x) || !int.TryParse(p[1], out int y) ||
                !int.TryParse(p[2], out int w) || !int.TryParse(p[3], out int h) || w < MinW || h < MinH) return false;
            r = (x, y, w, h);
            return true;
        }

        void SavePlacement()
        {
            if (!Visible || IsDisposed) return;
            var b = GetBounds();
            if (b.W < MinW || b.H < MinH) return;
            string v = $"{b.X},{b.Y},{b.W},{b.H}";
            settings.Set(LastPlaceKey, v);
            if (NoteId != null) settings.Set(PlaceKeyPrefix + NoteId, v);
        }

        public bool Pinned
        {
            get => pinned;
            set
            {
                pinned = value;
                if (!IsDisposed) gtk_window_set_keep_above(Handle, value ? 1 : 0);
                if (NoteId != null) settings.Set(PinKeyPrefix + NoteId, value ? "1" : "");
            }
        }

        public void MoveByStrip(bool mouse)
        {
            if (IsDisposed || !Visible) return;
            gtk_window_begin_move_drag(Handle, 1, (int)Math.Round(page.LastPressRootX), (int)Math.Round(page.LastPressRootY), page.LastPressTime);
        }

        public void EndFollow() { }

        /* ---------- showing & hiding ---------- */

        public void Open(Dictionary<string, object> state)
        {
            InUse = true;
            HasNote = true;
            NoteId = NoteIdOf(state);
            NoteLocked = state.TryGetValue("key", out object k) && k != null;
            pinned = NoteId != null && settings.Get(PinKeyPrefix + NoteId) == "1";
            gtk_window_set_keep_above(Handle, pinned ? 1 : 0);
            Send(new Dictionary<string, object> { ["type"] = "open", ["state"] = state, ["pinned"] = pinned });
        }

        public void ShowNote(bool activate)
        {
            if (IsDisposed) return;
            if (!Visible)
            {
                var p = Placement();
                gtk_window_resize(Handle, p.W, p.H);
                if (p.HasPos) gtk_window_move(Handle, p.X, p.Y);
                gtk_widget_show_all(Handle);
                Visible = true;
            }
            gtk_window_deiconify(Handle);
            if (activate)
            {
                gtk_window_present(Handle);
                page.Focus();
            }
            main.NoteWindowShown(this);
        }

        public void HideNote()
        {
            if (IsDisposed) return;
            SavePlacement();
            HasNote = false;
            NoteId = null;
            NoteLocked = false;
            pinned = false;
            gtk_window_set_keep_above(Handle, 0);
            if (Visible)
            {
                gtk_widget_hide(Handle);
                Visible = false;
            }
            if (InUse)
            {
                InUse = false;
                Freed?.Invoke(this);
            }
        }

        /* ---------- page messaging ---------- */

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

        public void SetState(Dictionary<string, object> s)
        {
            string before = NoteId;
            HasNote = s.TryGetValue("open", out object o) && o is bool b && b;
            NoteId = HasNote && s.TryGetValue("id", out object id) && id is string sid && sid.Length > 0 ? sid : null;
            if (before == null && NoteId != null)
            {
                SavePlacement();
                if (pinned) settings.Set(PinKeyPrefix + NoteId, "1");
            }
            NoteLocked = HasNote && s.TryGetValue("locked", out object l) && l is bool lb && lb;
            string title = s.TryGetValue("title", out object t) ? (t as string ?? "").Trim() : "";
            if (title.Length > 80) title = title.Substring(0, 80) + "…";
            gtk_window_set_title(Handle, (title.Length > 0 ? title : "New note") + " — NoteSpin");
            if (s.TryGetValue("band", out object bc) && TryHex(bc as string, out GdkRGBA band))
            {
                bandColor = band;
                gtk_widget_queue_draw(Handle);
            }
            if (s.TryGetValue("paper", out object pc) && TryHex(pc as string, out GdkRGBA paper))
                page.SetBackgroundColor(paper);
            main.PushNoteWinState();
        }

        static bool TryHex(string hex, out GdkRGBA c)
        {
            c = default;
            if (hex == null || hex.Length != 7 || hex[0] != '#') return false;
            if (!int.TryParse(hex.Substring(1, 2), NumberStyles.HexNumber, null, out int r)) return false;
            if (!int.TryParse(hex.Substring(3, 2), NumberStyles.HexNumber, null, out int g)) return false;
            if (!int.TryParse(hex.Substring(5, 2), NumberStyles.HexNumber, null, out int b)) return false;
            c = new GdkRGBA(r / 255.0, g / 255.0, b / 255.0, 1.0);
            return true;
        }

        public Task WaitWhileBusy() => page.WaitWhileBusy();

        /* ---------- closing ---------- */

        int OnDeleteEvent(IntPtr widget, IntPtr ev, IntPtr userData)
        {
            if (!allowClose)
            {
                if (ready && HasNote) Send(new Dictionary<string, object> { ["type"] = "close" });
                else HideNote();
                return 1; // keep hidden as spare
            }
            return 0;
        }

        public void CloseForGood()
        {
            allowClose = true;
            if (!IsDisposed) gtk_widget_destroy(Handle);
        }

        void OnDestroy(IntPtr widget, IntPtr userData)
        {
            IsDisposed = true;
            Visible = false;
            page.Close();
            signals.Dispose();
            Destroyed?.Invoke();
        }
    }
}
