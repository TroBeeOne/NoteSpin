using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static NoteSpin.GtkNative;

namespace NoteSpin
{
    /// <summary>
    /// A WebKit2GTK-4.1 web view in one of NoteSpin's GTK windows, serving the embedded web/ assets
    /// at notespin://app/ (registered as secure + CORS-enabled so Web Crypto and ES modules work).
    /// Nothing else can load in it; outside links open in the default browser via xdg-open.
    /// </summary>
    internal sealed class PageHost
    {
        public const string Scheme = "notespin";
        public const string Origin = "notespin://app/";
        public static readonly bool DevTools = Environment.GetEnvironmentVariable("NOTESPIN_DEVTOOLS") == "1";

        static readonly HashSet<int> BlockedMenuActions = new HashSet<int>
        {
            WEBKIT_CONTEXT_MENU_ACTION_GO_BACK,
            WEBKIT_CONTEXT_MENU_ACTION_GO_FORWARD,
            WEBKIT_CONTEXT_MENU_ACTION_STOP,
            WEBKIT_CONTEXT_MENU_ACTION_RELOAD,
            WEBKIT_CONTEXT_MENU_ACTION_OPEN_LINK_IN_NEW_WINDOW,
            WEBKIT_CONTEXT_MENU_ACTION_DOWNLOAD_LINK_TO_DISK,
            WEBKIT_CONTEXT_MENU_ACTION_OPEN_IMAGE_IN_NEW_WINDOW,
            WEBKIT_CONTEXT_MENU_ACTION_DOWNLOAD_IMAGE_TO_DISK,
            WEBKIT_CONTEXT_MENU_ACTION_OPEN_FRAME_IN_NEW_WINDOW,
            WEBKIT_CONTEXT_MENU_ACTION_OPEN_VIDEO_IN_NEW_WINDOW,
            WEBKIT_CONTEXT_MENU_ACTION_OPEN_AUDIO_IN_NEW_WINDOW,
            WEBKIT_CONTEXT_MENU_ACTION_DOWNLOAD_VIDEO_TO_DISK,
            WEBKIT_CONTEXT_MENU_ACTION_DOWNLOAD_AUDIO_TO_DISK,
            WEBKIT_CONTEXT_MENU_ACTION_INSPECT_ELEMENT,
        };

        // NOTESPIN_WEB=<the project's web folder> serves the page from disk instead of the binary
        static readonly string devWeb = Environment.GetEnvironmentVariable("NOTESPIN_WEB") is string w && Directory.Exists(w) ? Path.GetFullPath(w) : null;

        // Shared WebKitWebContext across main window + all pop-out note windows
        static readonly SignalStore ContextSignals = new SignalStore();
        static IntPtr sharedContext;

        readonly SignalStore signals = new SignalStore();
        readonly GdkRGBA initialBg;
        HostApi hostApi;
        string[] lastDragPaths;
        bool dropPending;

        public IntPtr Widget { get; private set; }
        public double LastPressRootX { get; private set; }
        public double LastPressRootY { get; private set; }
        public uint LastPressTime { get; private set; }

        /// <summary>One or more local files were dropped on the page (real drop, not hover).</summary>
        public event Action<string[]> FilesDropped;

        /// <summary>Files seen during GTK drag-and-drop so host.js filePath() knows their real disk paths.</summary>
        public event Action<string[]> DroppedPaths;

        /// <summary>The WebKit web process terminated unexpectedly.</summary>
        public event Action ProcessTerminated;

        /// <summary>The document title changed.</summary>
        public event Action<string> TitleChanged;

        /// <summary>Initial navigation finished.</summary>
        public event Action LoadFinished;

        public PageHost(GdkRGBA background)
        {
            initialBg = background;
        }

        public static IntPtr EnsureSharedContext(string dataDir)
        {
            if (sharedContext != IntPtr.Zero) return sharedContext;
            string webData = Path.Combine(dataDir, "WebKit", "data");
            string webCache = Path.Combine(dataDir, "WebKit", "cache");
            Directory.CreateDirectory(webData);
            Directory.CreateDirectory(webCache);

            IntPtr mgr = webkit_website_data_manager_new(
                "base-data-directory", webData,
                "base-cache-directory", webCache,
                IntPtr.Zero);
            sharedContext = webkit_web_context_new_with_website_data_manager(mgr);
            g_object_unref(mgr);

            // Register notespin:// as secure (for crypto.subtle) and CORS-enabled (for ES modules + importmap)
            IntPtr secMgr = webkit_web_context_get_security_manager(sharedContext);
            webkit_security_manager_register_uri_scheme_as_secure(secMgr, Scheme);
            webkit_security_manager_register_uri_scheme_as_cors_enabled(secMgr, Scheme);

            WebKitURISchemeRequestCallback schemeCb = OnUriSchemeRequest;
            ContextSignals.Pin(schemeCb);
            webkit_web_context_register_uri_scheme(
                sharedContext,
                Scheme,
                Marshal.GetFunctionPointerForDelegate(schemeCb),
                IntPtr.Zero,
                IntPtr.Zero);

            return sharedContext;
        }

        public void Start(IntPtr context, string page, HostApi api, Settings settings, object initialNoteWinState = null)
        {
            hostApi = api;
            Widget = webkit_web_view_new_with_context(context);

            var bg = initialBg;
            webkit_web_view_set_background_color(Widget, ref bg);

            IntPtr st = webkit_web_view_get_settings(Widget);
            webkit_settings_set_enable_developer_extras(st, DevTools ? 1 : 0);
            webkit_settings_set_enable_webgl(st, 1);
            if (DevTools) webkit_settings_set_enable_write_console_messages_to_stdout(st, 1);

            IntPtr ucm = webkit_web_view_get_user_content_manager(Widget);

            // Preload pref:* values synchronously at document start so NS.prefGet() needs no blocking IPC
            var initPrefs = settings != null ? settings.GetAllPrefs() : new Dictionary<string, string>();
            string bootstrap = "window.__nsInitPrefs = " + JsonCodec.Serialize(initPrefs) + ";" +
                (initialNoteWinState != null ? "window.__nsNoteWinState = " + JsonCodec.Serialize(initialNoteWinState) + ";" : "");
            IntPtr userScript = webkit_user_script_new(
                bootstrap,
                WEBKIT_USER_CONTENT_INJECT_TOP_FRAME,
                WEBKIT_USER_SCRIPT_INJECT_AT_DOCUMENT_START,
                IntPtr.Zero,
                IntPtr.Zero);
            webkit_user_content_manager_add_script(ucm, userScript);
            webkit_user_script_unref(userScript);

            webkit_user_content_manager_register_script_message_handler(ucm, "ns");
            signals.Connect(ucm, "script-message-received::ns", new SignalVoid2(OnScriptMessage));
            signals.Connect(Widget, "decide-policy", new SignalDecidePolicy(OnDecidePolicy));
            signals.Connect(Widget, "context-menu", new SignalContextMenu(OnContextMenu));
            signals.Connect(Widget, "key-press-event", new SignalEvent(OnKeyPress));
            signals.Connect(Widget, "button-press-event", new SignalEvent(OnButtonPress));
            signals.Connect(Widget, "drag-drop", new SignalDragDrop(OnDragDrop));
            signals.Connect(Widget, "drag-data-received", new SignalDragDataReceived(OnDragDataReceived));
            signals.Connect(Widget, "notify::title", new SignalVoid2(OnTitleNotify));
            signals.Connect(Widget, "load-changed", new SignalLoadChanged(OnLoadChanged));
            signals.Connect(Widget, "web-process-terminated", new SignalWebProcessTerminated(OnWebProcessTerminated));

            webkit_web_view_load_uri(Widget, Origin + page);
        }

        public void SetBackgroundColor(GdkRGBA rgba)
        {
            if (Widget == IntPtr.Zero) return;
            webkit_web_view_set_background_color(Widget, ref rgba);
        }

        public void Focus()
        {
            if (Widget != IntPtr.Zero) gtk_widget_grab_focus(Widget);
        }

        public double Scale => Widget != IntPtr.Zero ? Math.Max(1.0, gtk_widget_get_scale_factor(Widget)) : 1.0;

        public void Post(object message)
        {
            if (Widget == IntPtr.Zero) return;
            string json = JsonCodec.Serialize(message);
            // Escape U+2028 / U+2029 just in case, and pass as JS object literal into __nsReceive
            json = json.Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029");
            EvalFireAndForget("window.__nsReceive && window.__nsReceive(" + json + ");");
        }

        public void EvalFireAndForget(string script)
        {
            if (Widget == IntPtr.Zero || string.IsNullOrEmpty(script)) return;
            byte[] bytes = Encoding.UTF8.GetBytes(script);
            webkit_web_view_evaluate_javascript(Widget, bytes, (IntPtr)bytes.Length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>Runs JS in the page and resolves with its JSON-serialised result (or null on failure).</summary>
        public Task<string> Eval(string script)
        {
            if (Widget == IntPtr.Zero || string.IsNullOrEmpty(script)) return Task.FromResult<string>(null);
            var tcs = new TaskCompletionSource<string>();
            byte[] bytes = Encoding.UTF8.GetBytes(script);

            GAsyncReadyCallback cb = null;
            cb = (source, res, _) =>
            {
                UnpinOneShot(cb);
                try
                {
                    IntPtr val = webkit_web_view_evaluate_javascript_finish(source, res, out IntPtr err);
                    if (err != IntPtr.Zero)
                    {
                        g_error_free(err);
                        tcs.TrySetResult(null);
                        return;
                    }
                    if (val == IntPtr.Zero) { tcs.TrySetResult(null); return; }
                    try
                    {
                        if (jsc_value_is_boolean(val) != 0)
                            tcs.TrySetResult(jsc_value_to_boolean(val) != 0 ? "true" : "false");
                        else
                            tcs.TrySetResult(FromUtf8(jsc_value_to_json(val, 0), free: true));
                    }
                    finally { g_object_unref(val); }
                }
                catch { tcs.TrySetResult(null); }
            };

            IntPtr fn = PinOneShot(cb);
            webkit_web_view_evaluate_javascript(Widget, bytes, (IntPtr)bytes.Length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, fn, IntPtr.Zero);
            return tcs.Task;
        }

        /// <summary>Gives the page up to 5 s to finish saving before closing.</summary>
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
            signals.Dispose();
            Widget = IntPtr.Zero;
        }

        /* ---------- JS -> C# bridge ---------- */

        void OnScriptMessage(IntPtr ucm, IntPtr jsResult, IntPtr userData)
        {
            if (hostApi == null || jsResult == IntPtr.Zero) return;
            IntPtr jscVal = webkit_javascript_result_get_js_value(jsResult);
            string payload = FromUtf8(jsc_value_to_string(jscVal), free: true);
            if (string.IsNullOrEmpty(payload)) return;

            Dictionary<string, object> msg;
            try { msg = JsonCodec.Deserialize(payload) as Dictionary<string, object>; }
            catch { return; }
            if (msg == null) return;

            string callId = msg.TryGetValue("callId", out object cid) ? cid as string : null;
            string method = msg.TryGetValue("method", out object m) ? m as string : null;
            var args = msg.TryGetValue("args", out object a) && a is IList<object> list ? list : Array.Empty<object>();
            if (string.IsNullOrEmpty(method)) return;

            if (hostApi.TryDispatch(method, args, out string resultJson) && callId != null)
            {
                Post(new Dictionary<string, object> { ["__callId"] = callId, ["raw"] = resultJson });
            }
        }

        /* ---------- embedded web asset serving ---------- */

        static void OnUriSchemeRequest(IntPtr request, IntPtr userData)
        {
            string uriStr = FromUtf8(webkit_uri_scheme_request_get_uri(request)) ?? "";
            string path = "";
            try { path = Uri.UnescapeDataString(new Uri(uriStr).AbsolutePath).TrimStart('/'); }
            catch { }
            if (path.Length == 0) path = "index.html";

            byte[] data = path.Equals("favicon.ico", StringComparison.OrdinalIgnoreCase)
                ? WebAssets.Favicon()
                : devWeb != null ? FromDisk(path) : WebAssets.Get(path);

            if (data == null)
            {
                byte[] empty = Encoding.UTF8.GetBytes("Not Found");
                FinishResponse(request, empty, 404, "Not Found", "text/plain; charset=utf-8");
            }
            else
            {
                FinishResponse(request, data, 200, "OK", WebAssets.MimeType(path));
            }
        }

        static void FinishResponse(IntPtr request, byte[] data, uint status, string reason, string contentType)
        {
            IntPtr nativeBuf = g_malloc((UIntPtr)Math.Max(1, data.Length));
            if (data.Length > 0) Marshal.Copy(data, 0, nativeBuf, data.Length);
            IntPtr stream = g_memory_input_stream_new_from_data(nativeBuf, (IntPtr)data.Length, GFreePtr);
            IntPtr resp = webkit_uri_scheme_response_new(stream, data.Length);
            webkit_uri_scheme_response_set_status(resp, status, reason);
            webkit_uri_scheme_response_set_content_type(resp, contentType);
            IntPtr headers = soup_message_headers_new(SOUP_MESSAGE_HEADERS_RESPONSE);
            soup_message_headers_append(headers, "Cache-Control", "no-store");
            webkit_uri_scheme_response_set_http_headers(resp, headers); // takes ownership of headers
            webkit_uri_scheme_request_finish_with_response(request, resp);
            g_object_unref(resp);
            g_object_unref(stream);
        }

        static byte[] FromDisk(string path)
        {
            string full = Path.GetFullPath(Path.Combine(devWeb, path.Replace('\\', '/')));
            string prefix = Places.NoEndSlash(devWeb) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, Places.PathComparison) || !File.Exists(full)) return null;
            try { return File.ReadAllBytes(full); } catch { return null; }
        }

        /* ---------- navigation, links, drops, context menu & keys ---------- */

        int OnDecidePolicy(IntPtr webView, IntPtr decision, int decisionType, IntPtr userData)
        {
            if (decisionType != WEBKIT_POLICY_DECISION_TYPE_NAVIGATION_ACTION &&
                decisionType != WEBKIT_POLICY_DECISION_TYPE_NEW_WINDOW_ACTION)
                return 0;

            IntPtr action = webkit_navigation_policy_decision_get_navigation_action(decision);
            IntPtr req = action != IntPtr.Zero ? webkit_navigation_action_get_request(action) : IntPtr.Zero;
            string uri = req != IntPtr.Zero ? (FromUtf8(webkit_uri_request_get_uri(req)) ?? "") : "";

            if (decisionType == WEBKIT_POLICY_DECISION_TYPE_NAVIGATION_ACTION &&
                uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase))
                return 0; // allow internal navigation

            webkit_policy_decision_ignore(decision);
            if (uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string local = new Uri(uri).LocalPath;
                    if (File.Exists(local)) FilesDropped?.Invoke(new[] { local });
                }
                catch { }
            }
            else OpenExternal(uri);
            return 1;
        }

        internal static void OpenExternal(string uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri u)) return;
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeMailto) return;
            try
            {
                var psi = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                psi.ArgumentList.Add(u.AbsoluteUri);
                Process.Start(psi);
            }
            catch { }
        }

        int OnContextMenu(IntPtr webView, IntPtr menu, IntPtr ev, IntPtr hitTest, IntPtr userData)
        {
            uint n = webkit_context_menu_get_n_items(menu);
            for (int i = (int)n - 1; i >= 0; i--)
            {
                IntPtr item = webkit_context_menu_get_item_at_position(menu, (uint)i);
                int action = webkit_context_menu_item_get_stock_action(item);
                if (BlockedMenuActions.Contains(action) && !(DevTools && action == WEBKIT_CONTEXT_MENU_ACTION_INSPECT_ELEMENT))
                    webkit_context_menu_remove(menu, item);
            }
            // Strip leading, trailing, or duplicate separators (action == 0)
            n = webkit_context_menu_get_n_items(menu);
            for (int i = (int)n - 1; i >= 0; i--)
            {
                IntPtr item = webkit_context_menu_get_item_at_position(menu, (uint)i);
                if (webkit_context_menu_item_get_stock_action(item) != WEBKIT_CONTEXT_MENU_ACTION_NO_ACTION) continue;
                bool dupPrev = i > 0 && webkit_context_menu_item_get_stock_action(webkit_context_menu_get_item_at_position(menu, (uint)(i - 1))) == WEBKIT_CONTEXT_MENU_ACTION_NO_ACTION;
                if (i == 0 || i == (int)n - 1 || dupPrev)
                {
                    webkit_context_menu_remove(menu, item);
                    n--;
                }
            }
            return webkit_context_menu_get_n_items(menu) == 0 ? 1 : 0;
        }

        int OnKeyPress(IntPtr widget, IntPtr ev, IntPtr userData)
        {
            if (gdk_event_get_keyval(ev, out uint key) == 0) return 0;
            gdk_event_get_state(ev, out uint state);
            bool ctrl = (state & GDK_CONTROL_MASK) != 0;
            bool shift = (state & GDK_SHIFT_MASK) != 0;

            if (DevTools)
            {
                if (key == GDK_KEY_F12 || (ctrl && shift && (key == GDK_KEY_i || key == GDK_KEY_I)))
                {
                    IntPtr insp = webkit_web_view_get_inspector(Widget);
                    if (insp != IntPtr.Zero) webkit_web_inspector_show(insp);
                    return 1;
                }
                if (key == GDK_KEY_F5 || (ctrl && (key == GDK_KEY_r || key == GDK_KEY_R)))
                {
                    webkit_web_view_reload(Widget);
                    return 1;
                }
            }

            // Block browser reload/print/zoom shortcuts so the page stays app-like
            if (key == GDK_KEY_F5 ||
                (ctrl && (key == GDK_KEY_r || key == GDK_KEY_R || key == GDK_KEY_p || key == GDK_KEY_P ||
                          key == GDK_KEY_plus || key == GDK_KEY_minus || key == GDK_KEY_equal || key == GDK_KEY_0)))
                return 1;

            return 0;
        }

        int OnButtonPress(IntPtr widget, IntPtr ev, IntPtr userData)
        {
            if (gdk_event_get_root_coords(ev, out double rx, out double ry) != 0)
            {
                LastPressRootX = rx;
                LastPressRootY = ry;
            }
            LastPressTime = gdk_event_get_time(ev);
            return 0; // let WebKit handle the click normally
        }

        int OnDragDrop(IntPtr widget, IntPtr context, int x, int y, uint time, IntPtr userData)
        {
            dropPending = true;
            // If WebKit already fetched the URI list during drag-motion and doesn't re-emit drag-data-received
            // after drag-drop, fire FilesDropped after a short beat using the captured paths.
            _ = FlushPendingDrop();
            return 0;
        }

        async Task FlushPendingDrop()
        {
            await Task.Delay(80);
            if (!dropPending) return;
            dropPending = false;
            var paths = lastDragPaths;
            lastDragPaths = null;
            if (paths != null && paths.Length > 0) FilesDropped?.Invoke(paths);
        }

        void OnDragDataReceived(IntPtr widget, IntPtr context, int x, int y, IntPtr selectionData, uint info, uint time, IntPtr userData)
        {
            if (selectionData == IntPtr.Zero) return;
            IntPtr urisPtr = gtk_selection_data_get_uris(selectionData);
            if (urisPtr == IntPtr.Zero) return;
            try
            {
                string[] uris = ReadNullTerminatedStrings(urisPtr);
                var paths = new List<string>();
                foreach (string u in uris)
                {
                    try
                    {
                        if (Uri.TryCreate(u, UriKind.Absolute, out Uri parsed) && parsed.IsFile && File.Exists(parsed.LocalPath))
                            paths.Add(parsed.LocalPath);
                    }
                    catch { }
                }
                if (paths.Count == 0) return;
                var arr = paths.ToArray();
                lastDragPaths = arr;
                DroppedPaths?.Invoke(arr);
                if (dropPending)
                {
                    dropPending = false;
                    lastDragPaths = null;
                    FilesDropped?.Invoke(arr);
                }
            }
            finally { g_strfreev(urisPtr); }
        }

        void OnTitleNotify(IntPtr webView, IntPtr pspec, IntPtr userData)
        {
            string t = FromUtf8(webkit_web_view_get_title(webView));
            TitleChanged?.Invoke(t);
        }

        void OnLoadChanged(IntPtr webView, int loadEvent, IntPtr userData)
        {
            if (loadEvent == WEBKIT_LOAD_FINISHED) LoadFinished?.Invoke();
        }

        int OnWebProcessTerminated(IntPtr webView, int reason, IntPtr userData)
        {
            ProcessTerminated?.Invoke();
            return 0;
        }
    }
}
