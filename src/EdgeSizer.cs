using System;
using System.Drawing;
using System.Windows.Forms;

namespace NoteSpin
{
    /// <summary>
    /// Resizes a window that has no sizing frame from one of its edges, as Windows does for framed ones: the
    /// window holds the mouse until it's let go, the dragged sides follow it and the others stay put, never
    /// smaller than the window's smallest size. Windows' own resizing only works on windows with a sizing frame.
    /// Used by the note windows (their thin edge) and the main window with its frame hidden (the page's edges).
    /// </summary>
    internal sealed class EdgeSizer
    {
        readonly Form form;
        Point from;         // where the mouse was when it started (screen pixels)
        Rectangle start;    // and the window
        bool watchButton;   // stop when the button is found up (started from the page, which may miss the let-go)

        /// <summary>The part of the edge being dragged (HTLEFT … HTBOTTOMRIGHT), 0 when not resizing.</summary>
        public int Edge { get; private set; }

        /// <summary>The resizing ended (the mouse was let go, or something else took it).</summary>
        public event Action Ended;

        public EdgeSizer(Form form) => this.form = form;

        /// <summary>Starts resizing from an edge, the mouse at screen point at.</summary>
        public void Start(int edge, Point at, bool watchButton = false)
        {
            Edge = edge;
            from = at;
            start = form.Bounds;
            this.watchButton = watchButton;
            form.Capture = true;   // the mouse stays the window's until it's let go, even off it
            Cursor.Current = CursorFor(edge);
        }

        /// <summary>The window moved to a screen with another DPI while resizing: carries on from here.</summary>
        public void Restart()
        {
            if (Edge == 0) return;
            start = form.Bounds;
            from = Cursor.Position;
        }

        /// <summary>For the window's WndProc: true when the message was part of resizing and is dealt with.</summary>
        public bool Handle(ref Message m)
        {
            if (Edge == 0) return false;
            switch (m.Msg)
            {
                case Native.WM_MOUSEMOVE:   // client pixels
                    if (watchButton && !Native.PrimaryButtonDown()) { End(); return true; }
                    SizeTo(form.PointToScreen(LParamPoint(m.LParam)));
                    return true;
                case Native.WM_LBUTTONUP:
                    End();
                    return true;
                case Native.WM_CAPTURECHANGED:   // something else took the mouse
                    End();
                    return false;
            }
            return false;
        }

        void SizeTo(Point screen)
        {
            int dx = screen.X - from.X, dy = screen.Y - from.Y, e = Edge;
            var r = start;
            int left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom, minW = form.MinimumSize.Width, minH = form.MinimumSize.Height;
            if (e == Native.HTLEFT || e == Native.HTTOPLEFT || e == Native.HTBOTTOMLEFT) left = Math.Min(r.Left + dx, right - minW);
            if (e == Native.HTRIGHT || e == Native.HTTOPRIGHT || e == Native.HTBOTTOMRIGHT) right = Math.Max(r.Right + dx, left + minW);
            if (e == Native.HTTOP || e == Native.HTTOPLEFT || e == Native.HTTOPRIGHT) top = Math.Min(r.Top + dy, bottom - minH);
            if (e == Native.HTBOTTOM || e == Native.HTBOTTOMLEFT || e == Native.HTBOTTOMRIGHT) bottom = Math.Max(r.Bottom + dy, top + minH);
            var to = Rectangle.FromLTRB(left, top, right, bottom);
            if (to != form.Bounds) form.Bounds = to;
            Cursor.Current = CursorFor(e);
        }

        void End()
        {
            if (Edge == 0) return;
            Edge = 0;
            form.Capture = false;
            Ended?.Invoke();
        }

        /// <summary>The arrows for a part of the edge.</summary>
        public static Cursor CursorFor(int e) =>
            e == Native.HTLEFT || e == Native.HTRIGHT ? Cursors.SizeWE : e == Native.HTTOP || e == Native.HTBOTTOM ? Cursors.SizeNS
            : e == Native.HTTOPLEFT || e == Native.HTBOTTOMRIGHT ? Cursors.SizeNWSE : Cursors.SizeNESW;

        /// <summary>A part of the edge by name ("left", "topright", …), as the page says it; 0 for none.</summary>
        public static int Named(string name)
        {
            switch (name)
            {
                case "left": return Native.HTLEFT;
                case "right": return Native.HTRIGHT;
                case "top": return Native.HTTOP;
                case "bottom": return Native.HTBOTTOM;
                case "topleft": return Native.HTTOPLEFT;
                case "topright": return Native.HTTOPRIGHT;
                case "bottomleft": return Native.HTBOTTOMLEFT;
                case "bottomright": return Native.HTBOTTOMRIGHT;
                default: return 0;
            }
        }

        // the point in a mouse message
        public static Point LParamPoint(IntPtr lParam)
        {
            long lp = lParam.ToInt64();
            return new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
        }
    }
}
