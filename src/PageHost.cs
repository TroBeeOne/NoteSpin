using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace NoteSpin
{
    /// <summary>
    /// A WebView2 in one of NoteSpin's windows, showing a page compiled into the exe at https://notespin.local/.
    /// Nothing else can load in it; outside links open in the default browser. The main window and the note
    /// window each have one, sharing one browser environment.
    /// </summary>
    internal sealed class PageHost
    {
        public const string Origin = "https://notespin.local/";
        public static readonly bool DevTools = Environment.GetEnvironmentVariable("NOTESPIN_DEVTOOLS") == "1";

        // browser-only entries removed from the right-click menu (copy, paste, spelling etc. stay)
        static readonly HashSet<string> BlockedMenuItems = new HashSet<string>
        {
            "back", "forward", "reload", "saveAs", "print", "createQrCode", "share", "inspectElement", "viewPageSource",
            "openLinkInNewWindow", "saveLinkAs", "saveImageAs", "saveMediaAs", "openImageInNewWindow", "copyLinkToHighlight", "webCapture",
            "copyImage", "copyImageLocation",   // the cards have their own menu (Copy as picture); nothing else is a picture
        };

        // NOTESPIN_WEB=<the project's web folder> serves the page from disk instead of the exe, so a
        // reload picks up changes while working on it (with NOTESPIN_DEVTOOLS=1 for F5 and F12).
        static readonly string devWeb = Environment.GetEnvironmentVariable("NOTESPIN_WEB") is string w && Directory.Exists(w) ? Path.GetFullPath(w) : null;

        readonly Form form;
        readonly Color background;
        CoreWebView2Environment env;

        public CoreWebView2Controller Controller { get; private set; }
        public CoreWebView2 Core { get; private set; }

        /// <summary>Space left around the page (the note window's resize border), in pixels.</summary>
        public int Margin { get; set; }

        /// <summary>A file was dropped on the page (it tried to navigate to a file: address).</summary>
        public event Action<string> FileDropped;

        /// <summary>The browser process stopped: every page in the app is gone.</summary>
        public event Action BrowserExited;

        public PageHost(Form form, Color background)
        {
            this.form = form;
            this.background = background;
        }

        public async Task Start(CoreWebView2Environment environment, string page, object hostObject)
        {
            env = environment;
            Controller = await env.CreateCoreWebView2ControllerAsync(form.Handle);
            Controller.DefaultBackgroundColor = background;
            Fit();
            Controller.AcceleratorKeyPressed += OnAcceleratorKey;
            Controller.MoveFocusRequested += (s, a) => { a.Handled = true; Controller.MoveFocus(a.Reason); };   // Tab cycles inside the page

            Core = Controller.CoreWebView2;
            var st = Core.Settings;
            st.AreDevToolsEnabled = DevTools;
            st.IsStatusBarEnabled = false;
            st.IsPasswordAutosaveEnabled = false;
            st.IsGeneralAutofillEnabled = false;
            st.IsSwipeNavigationEnabled = false;
            st.IsZoomControlEnabled = false;      // Ctrl+wheel and Ctrl +/- would zoom the page; the wheel spins the cards
            st.IsPinchZoomEnabled = false;        // touch screens: pinching would zoom the whole page
            st.IsReputationCheckingRequired = false;   // no SmartScreen look-ups: the page comes from inside the exe

            Core.AddHostObjectToScript("ns", hostObject);
            Core.AddWebResourceRequestedFilter(Origin + "*", CoreWebView2WebResourceContext.All);
            Core.WebResourceRequested += OnWebResource;
            Core.NavigationStarting += OnNavigationStarting;
            Core.NewWindowRequested += OnNewWindow;
            Core.DownloadStarting += (s, a) => a.Cancel = true;
            Core.ContextMenuRequested += OnContextMenu;
            Core.ProcessFailed += OnProcessFailed;
            Core.PermissionRequested += (s, a) => a.State = CoreWebView2PermissionState.Deny;

            Core.Navigate(Origin + page);
        }

        /// <summary>Sizes the page to the window (less the margin); hidden while the window is minimized.</summary>
        public void Fit()
        {
            if (Controller == null) return;
            Controller.IsVisible = form.WindowState != FormWindowState.Minimized;
            if (!Controller.IsVisible) return;
            var r = form.ClientRectangle;
            r.Inflate(-Margin, -Margin);
            if (r.Width > 0 && r.Height > 0) Controller.Bounds = r;
        }

        public void Moved() => Controller?.NotifyParentWindowPositionChanged();

        public void Focus() => Controller?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);

        /// <summary>The page's pixels per CSS pixel.</summary>
        public double Scale => Controller?.RasterizationScale ?? form.DeviceDpi / 96.0;

        public void Post(object message)
        {
            try { Core?.PostWebMessageAsJson(HostApi.Json.Serialize(message)); } catch { }
        }

        /// <summary>Runs a line of script in the page: its result as JSON, or null when the page couldn't answer.</summary>
        public async Task<string> Eval(string script)
        {
            if (Core == null) return null;
            try { return await Core.ExecuteScriptAsync(script); }
            catch { return null; }
        }

        /// <summary>Gives the page up to 5 s to write its last change (window.__nsBusy() is true while a save is waiting or under way).</summary>
        public async Task WaitWhileBusy()
        {
            for (int i = 0; i < 100; i++)
            {
                string busy = await Eval("typeof window.__nsBusy === 'function' && window.__nsBusy() === true");
                if (busy != "true") return;
                await Task.Delay(50);
            }
        }

        public void Close()
        {
            try { Controller?.Close(); } catch { }
            Controller = null;
            Core = null;
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

        static byte[] FromDisk(string path)
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
            if (uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) FileDropped?.Invoke(uri);
            else OpenExternal(uri);
        }

        void OnNewWindow(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;   // never a browser window
            string uri = e.Uri ?? "";
            if (!uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) OpenExternal(uri);
        }

        internal static void OpenExternal(string uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri u)) return;
            // only web and mail links are handed to Windows, never other protocol handlers
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeMailto) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); } catch { }
        }

        void OnContextMenu(object sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            var items = e.MenuItems;
            for (int i = items.Count - 1; i >= 0; i--)
                if (BlockedMenuItems.Contains(items[i].Name) && !(DevTools && items[i].Name == "inspectElement")) items.RemoveAt(i);
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
            bool ctrl = (Control.ModifierKeys & Keys.Control) != 0, shift = (Control.ModifierKeys & Keys.Shift) != 0;
            bool keep = DevTools && (key == Keys.F12 || (ctrl && shift && key == Keys.I));
            if (!keep) e.IsBrowserAcceleratorKeyEnabled = false;
        }

        void OnProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited ||
                e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
            {
                Later(() => Core?.Reload());
            }
            else if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
            {
                Later(() => BrowserExited?.Invoke());
            }
        }

        void Later(Action a)
        {
            if (!form.IsDisposed && form.IsHandleCreated) form.BeginInvoke(a);
        }
    }
}
