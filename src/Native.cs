using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NoteSpin
{
    /// <summary>The few Windows calls the note window needs that Windows Forms doesn't offer.</summary>
    internal static class Native
    {
        public const int WM_NCHITTEST = 0x0084, WM_NCLBUTTONDOWN = 0x00A1, WM_NCLBUTTONDBLCLK = 0x00A3,
            WM_MOUSEMOVE = 0x0200, WM_LBUTTONUP = 0x0202, WM_CAPTURECHANGED = 0x0215;
        public const int HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14,
            HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
        public const int WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000;
        const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
        const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02;
        const uint GW_HWNDNEXT = 2;
        const int DWMWA_CLOAKED = 14, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2;

        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        /// <summary>Whether the main mouse button is down right now (the physical one, so a left-handed mouse counts too).</summary>
        public static bool PrimaryButtonDown() =>
            (GetAsyncKeyState(SystemInformation.MouseButtonsSwapped ? VK_RBUTTON : VK_LBUTTON) & 0x8000) != 0;

        /// <summary>Puts a window in front of the others without taking the focus.</summary>
        public static void BringToFrontQuietly(IntPtr hWnd) =>
            SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        /// <summary>Rounded corners on Windows 11 (Windows 10 keeps square ones).</summary>
        public static void RoundCorners(IntPtr hWnd)
        {
            int v = DWMWCP_ROUND;
            try { DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref v, sizeof(int)); } catch { }
        }

        /// <summary>
        /// Whether `target` is the window seen at point p just below `above` (the note window being moved):
        /// the first visible window under it in front-to-back order that covers p.
        /// </summary>
        public static bool IsWindowBelowAt(IntPtr above, IntPtr target, Point p)
        {
            for (IntPtr h = GetWindow(above, GW_HWNDNEXT); h != IntPtr.Zero; h = GetWindow(h, GW_HWNDNEXT))
            {
                if (!IsWindowVisible(h) || IsIconic(h)) continue;
                if (!GetWindowRect(h, out RECT r) || p.X < r.Left || p.X >= r.Right || p.Y < r.Top || p.Y >= r.Bottom) continue;
                // windows that are "visible" but not on screen (hidden Store apps, for one) don't count
                if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) continue;
                return h == target;
            }
            return false;
        }
    }
}
