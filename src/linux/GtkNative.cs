using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace NoteSpin
{
    /// <summary>
    /// Minimal P/Invoke layer over system GTK3, GDK3, WebKit2GTK-4.1, JavaScriptCore, GLib/GIO, Cairo and LibSoup.
    /// Zero NuGet packages required: binds directly to the shared libraries installed on modern Linux desktops.
    /// </summary>
    internal static class GtkNative
    {
        public const string GtkLib = "libgtk-3.so.0";
        public const string GdkLib = "libgdk-3.so.0";
        public const string WebKitLib = "libwebkit2gtk-4.1.so.0";
        public const string JscLib = "libjavascriptcoregtk-4.1.so.0";
        public const string GObjectLib = "libgobject-2.0.so.0";
        public const string GLibLib = "libglib-2.0.so.0";
        public const string GioLib = "libgio-2.0.so.0";
        public const string CairoLib = "libcairo.so.2";
        public const string PixbufLib = "libgdk_pixbuf-2.0.so.0";
        public const string SoupLib = "libsoup-3.0.so.0";

        public static readonly IntPtr GFreePtr = NativeLibrary.GetExport(NativeLibrary.Load(GLibLib), "g_free");

        /* ---------- structs ---------- */

        [StructLayout(LayoutKind.Sequential)]
        public struct GdkRGBA
        {
            public double Red, Green, Blue, Alpha;
            public GdkRGBA(double r, double g, double b, double a) { Red = r; Green = g; Blue = b; Alpha = a; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GdkRectangle
        {
            public int X, Y, Width, Height;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CairoRectangleInt
        {
            public int X, Y, Width, Height;
            public CairoRectangleInt(int x, int y, int w, int h) { X = x; Y = y; Width = w; Height = h; }
        }

        /* ---------- constants ---------- */

        public const int GTK_WINDOW_TOPLEVEL = 0;

        public const int GTK_FILE_CHOOSER_ACTION_OPEN = 0;
        public const int GTK_FILE_CHOOSER_ACTION_SAVE = 1;
        public const int GTK_FILE_CHOOSER_ACTION_SELECT_FOLDER = 2;
        public const int GTK_RESPONSE_ACCEPT = -3;

        public const int GTK_DIALOG_MODAL = 1 << 0;
        public const int GTK_DIALOG_DESTROY_WITH_PARENT = 1 << 1;
        public const int GTK_MESSAGE_INFO = 0;
        public const int GTK_MESSAGE_ERROR = 3;
        public const int GTK_BUTTONS_OK = 1;

        public const int GDK_POINTER_MOTION_MASK = 1 << 2;
        public const int GDK_BUTTON_PRESS_MASK = 1 << 8;
        public const int GDK_BUTTON_RELEASE_MASK = 1 << 9;
        public const int GDK_ENTER_NOTIFY_MASK = 1 << 12;
        public const int GDK_LEAVE_NOTIFY_MASK = 1 << 13;

        public const int GDK_WINDOW_EDGE_NORTH_WEST = 0;
        public const int GDK_WINDOW_EDGE_NORTH = 1;
        public const int GDK_WINDOW_EDGE_NORTH_EAST = 2;
        public const int GDK_WINDOW_EDGE_WEST = 3;
        public const int GDK_WINDOW_EDGE_EAST = 4;
        public const int GDK_WINDOW_EDGE_SOUTH_WEST = 5;
        public const int GDK_WINDOW_EDGE_SOUTH = 6;
        public const int GDK_WINDOW_EDGE_SOUTH_EAST = 7;

        public const int GDK_WINDOW_STATE_ICONIFIED = 1 << 1;

        public const uint GDK_SHIFT_MASK = 1 << 0;
        public const uint GDK_CONTROL_MASK = 1 << 2;

        public const uint GDK_KEY_F5 = 0xffc2;
        public const uint GDK_KEY_F12 = 0xffc9;
        public const uint GDK_KEY_i = 0x0069;
        public const uint GDK_KEY_I = 0x0049;
        public const uint GDK_KEY_p = 0x0070;
        public const uint GDK_KEY_P = 0x0050;
        public const uint GDK_KEY_r = 0x0072;
        public const uint GDK_KEY_R = 0x0052;
        public const uint GDK_KEY_plus = 0x002b;
        public const uint GDK_KEY_minus = 0x002d;
        public const uint GDK_KEY_equal = 0x003d;
        public const uint GDK_KEY_0 = 0x0030;

        public const int CAIRO_OPERATOR_SOURCE = 1;

        public const int WEBKIT_POLICY_DECISION_TYPE_NAVIGATION_ACTION = 0;
        public const int WEBKIT_POLICY_DECISION_TYPE_NEW_WINDOW_ACTION = 1;

        public const int WEBKIT_LOAD_FINISHED = 3;

        public const int WEBKIT_USER_CONTENT_INJECT_TOP_FRAME = 1;
        public const int WEBKIT_USER_SCRIPT_INJECT_AT_DOCUMENT_START = 0;

        public const int WEBKIT_CONTEXT_MENU_ACTION_NO_ACTION = 0;
        public const int WEBKIT_CONTEXT_MENU_ACTION_OPEN_LINK_IN_NEW_WINDOW = 2;
        public const int WEBKIT_CONTEXT_MENU_ACTION_DOWNLOAD_LINK_TO_DISK = 3;
        public const int WEBKIT_CONTEXT_MENU_ACTION_OPEN_IMAGE_IN_NEW_WINDOW = 5;
        public const int WEBKIT_CONTEXT_MENU_ACTION_DOWNLOAD_IMAGE_TO_DISK = 6;
        public const int WEBKIT_CONTEXT_MENU_ACTION_OPEN_FRAME_IN_NEW_WINDOW = 8;
        public const int WEBKIT_CONTEXT_MENU_ACTION_GO_BACK = 10;
        public const int WEBKIT_CONTEXT_MENU_ACTION_GO_FORWARD = 11;
        public const int WEBKIT_CONTEXT_MENU_ACTION_STOP = 12;
        public const int WEBKIT_CONTEXT_MENU_ACTION_RELOAD = 13;
        public const int WEBKIT_CONTEXT_MENU_ACTION_INSPECT_ELEMENT = 30;
        public const int WEBKIT_CONTEXT_MENU_ACTION_OPEN_VIDEO_IN_NEW_WINDOW = 35;
        public const int WEBKIT_CONTEXT_MENU_ACTION_OPEN_AUDIO_IN_NEW_WINDOW = 36;
        public const int WEBKIT_CONTEXT_MENU_ACTION_DOWNLOAD_VIDEO_TO_DISK = 41;
        public const int WEBKIT_CONTEXT_MENU_ACTION_DOWNLOAD_AUDIO_TO_DISK = 42;

        public const int SOUP_MESSAGE_HEADERS_RESPONSE = 1;

        /* ---------- delegates ---------- */

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int GSourceFunc(IntPtr data);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void GAsyncReadyCallback(IntPtr sourceObject, IntPtr res, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void WebKitURISchemeRequestCallback(IntPtr request, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void SignalVoid1(IntPtr obj, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void SignalVoid2(IntPtr obj, IntPtr arg1, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void SignalLoadChanged(IntPtr webView, int loadEvent, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int SignalEvent(IntPtr widget, IntPtr ev, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int SignalDraw(IntPtr widget, IntPtr cr, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int SignalDecidePolicy(IntPtr webView, IntPtr decision, int decisionType, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int SignalContextMenu(IntPtr webView, IntPtr menu, IntPtr ev, IntPtr hitTest, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int SignalWebProcessTerminated(IntPtr webView, int reason, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void SignalDragDataReceived(IntPtr widget, IntPtr context, int x, int y, IntPtr selectionData, uint info, uint time, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int SignalDragDrop(IntPtr widget, IntPtr context, int x, int y, uint time, IntPtr userData);

        /* ---------- GLib / GObject / GIO ---------- */

        [DllImport(GLibLib)] public static extern IntPtr g_malloc(UIntPtr nBytes);
        [DllImport(GLibLib)] public static extern void g_free(IntPtr mem);
        [DllImport(GLibLib)] public static extern void g_strfreev(IntPtr strArray);
        [DllImport(GLibLib)] public static extern void g_error_free(IntPtr err);
        [DllImport(GLibLib)] public static extern uint g_idle_add(IntPtr func, IntPtr data);
        [DllImport(GLibLib)] public static extern uint g_timeout_add(uint intervalMs, IntPtr func, IntPtr data);
        [DllImport(GLibLib)] public static extern void g_set_prgname([MarshalAs(UnmanagedType.LPUTF8Str)] string prgname);

        [DllImport(GObjectLib)] public static extern IntPtr g_object_ref(IntPtr obj);
        [DllImport(GObjectLib)] public static extern void g_object_unref(IntPtr obj);
        [DllImport(GObjectLib)] public static extern ulong g_signal_connect_data(
            IntPtr instance,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string detailedSignal,
            IntPtr cHandler,
            IntPtr data,
            IntPtr destroyData,
            int connectFlags);

        [DllImport(GioLib)] public static extern IntPtr g_memory_input_stream_new_from_data(IntPtr data, IntPtr len, IntPtr destroy);

        /* ---------- GTK3 ---------- */

        [DllImport(GtkLib)] public static extern void gtk_init(ref int argc, ref IntPtr argv);
        [DllImport(GtkLib)] public static extern void gtk_main();
        [DllImport(GtkLib)] public static extern void gtk_main_quit();

        [DllImport(GtkLib)] public static extern IntPtr gtk_window_new(int type);
        [DllImport(GtkLib)] public static extern void gtk_window_set_title(IntPtr win, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);
        [DllImport(GtkLib)] public static extern void gtk_window_set_default_size(IntPtr win, int width, int height);
        [DllImport(GtkLib)] public static extern void gtk_window_resize(IntPtr win, int width, int height);
        [DllImport(GtkLib)] public static extern void gtk_window_move(IntPtr win, int x, int y);
        [DllImport(GtkLib)] public static extern void gtk_window_get_size(IntPtr win, out int width, out int height);
        [DllImport(GtkLib)] public static extern void gtk_window_get_position(IntPtr win, out int x, out int y);
        [DllImport(GtkLib)] public static extern void gtk_window_maximize(IntPtr win);
        [DllImport(GtkLib)] public static extern void gtk_window_unmaximize(IntPtr win);
        [DllImport(GtkLib)] public static extern void gtk_window_iconify(IntPtr win);
        [DllImport(GtkLib)] public static extern void gtk_window_deiconify(IntPtr win);
        [DllImport(GtkLib)] public static extern void gtk_window_present(IntPtr win);
        [DllImport(GtkLib)] public static extern void gtk_window_set_decorated(IntPtr win, int setting);
        [DllImport(GtkLib)] public static extern void gtk_window_set_resizable(IntPtr win, int setting);
        [DllImport(GtkLib)] public static extern void gtk_window_set_keep_above(IntPtr win, int setting);
        [DllImport(GtkLib)] public static extern void gtk_window_set_icon(IntPtr win, IntPtr pixbuf);
        [DllImport(GtkLib)] public static extern void gtk_window_begin_move_drag(IntPtr win, int button, int rootX, int rootY, uint timestamp);
        [DllImport(GtkLib)] public static extern void gtk_window_begin_resize_drag(IntPtr win, int edge, int button, int rootX, int rootY, uint timestamp);
        [DllImport(GtkLib)] public static extern int gtk_window_is_maximized(IntPtr win);

        [DllImport(GtkLib)] public static extern void gtk_widget_show_all(IntPtr widget);
        [DllImport(GtkLib)] public static extern void gtk_widget_show(IntPtr widget);
        [DllImport(GtkLib)] public static extern void gtk_widget_hide(IntPtr widget);
        [DllImport(GtkLib)] public static extern void gtk_widget_destroy(IntPtr widget);
        [DllImport(GtkLib)] public static extern void gtk_widget_grab_focus(IntPtr widget);
        [DllImport(GtkLib)] public static extern void gtk_widget_set_size_request(IntPtr widget, int width, int height);
        [DllImport(GtkLib)] public static extern void gtk_widget_set_app_paintable(IntPtr widget, int appPaintable);
        [DllImport(GtkLib)] public static extern void gtk_widget_set_visual(IntPtr widget, IntPtr visual);
        [DllImport(GtkLib)] public static extern IntPtr gtk_widget_get_screen(IntPtr widget);
        [DllImport(GtkLib)] public static extern IntPtr gtk_widget_get_window(IntPtr widget);
        [DllImport(GtkLib)] public static extern void gtk_widget_queue_draw(IntPtr widget);
        [DllImport(GtkLib)] public static extern int gtk_widget_get_scale_factor(IntPtr widget);
        [DllImport(GtkLib)] public static extern int gtk_widget_get_allocated_width(IntPtr widget);
        [DllImport(GtkLib)] public static extern int gtk_widget_get_allocated_height(IntPtr widget);
        [DllImport(GtkLib)] public static extern void gtk_widget_add_events(IntPtr widget, int events);
        [DllImport(GtkLib)] public static extern void gtk_widget_realize(IntPtr widget);
        [DllImport(GtkLib)] public static extern void gtk_widget_input_shape_combine_region(IntPtr widget, IntPtr region);

        [DllImport(GtkLib)] public static extern void gtk_container_add(IntPtr container, IntPtr widget);
        [DllImport(GtkLib)] public static extern void gtk_container_set_border_width(IntPtr container, uint borderWidth);

        [DllImport(GtkLib)] public static extern IntPtr gtk_file_chooser_native_new(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string title,
            IntPtr parent,
            int action,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string acceptLabel,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string cancelLabel);
        [DllImport(GtkLib)] public static extern int gtk_native_dialog_run(IntPtr dialog);
        [DllImport(GtkLib)] public static extern IntPtr gtk_file_chooser_get_filename(IntPtr chooser);
        [DllImport(GtkLib)] public static extern int gtk_file_chooser_set_current_folder(IntPtr chooser, [MarshalAs(UnmanagedType.LPUTF8Str)] string folder);
        [DllImport(GtkLib)] public static extern void gtk_file_chooser_set_current_name(IntPtr chooser, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport(GtkLib)] public static extern void gtk_file_chooser_set_do_overwrite_confirmation(IntPtr chooser, int doConfirm);
        [DllImport(GtkLib)] public static extern void gtk_file_chooser_add_filter(IntPtr chooser, IntPtr filter);
        [DllImport(GtkLib)] public static extern IntPtr gtk_file_filter_new();
        [DllImport(GtkLib)] public static extern void gtk_file_filter_set_name(IntPtr filter, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport(GtkLib)] public static extern void gtk_file_filter_add_pattern(IntPtr filter, [MarshalAs(UnmanagedType.LPUTF8Str)] string pattern);

        [DllImport(GtkLib)] public static extern IntPtr gtk_message_dialog_new(
            IntPtr parent, int flags, int type, int buttons,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string format,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string message);
        [DllImport(GtkLib)] public static extern int gtk_dialog_run(IntPtr dialog);
        [DllImport(GtkLib)] public static extern IntPtr gtk_selection_data_get_uris(IntPtr selectionData);

        /* ---------- GDK3 ---------- */

        [DllImport(GdkLib)] public static extern IntPtr gdk_screen_get_rgba_visual(IntPtr screen);
        [DllImport(GdkLib)] public static extern int gdk_screen_is_composited(IntPtr screen);
        [DllImport(GdkLib)] public static extern IntPtr gdk_display_get_default();
        [DllImport(GdkLib)] public static extern IntPtr gdk_display_get_primary_monitor(IntPtr display);
        [DllImport(GdkLib)] public static extern IntPtr gdk_display_get_monitor(IntPtr display, int monitorNum);
        [DllImport(GdkLib)] public static extern void gdk_monitor_get_workarea(IntPtr monitor, out GdkRectangle rect);
        [DllImport(GdkLib)] public static extern void gdk_window_set_cursor(IntPtr gdkWin, IntPtr cursor);
        [DllImport(GdkLib)] public static extern IntPtr gdk_cursor_new_from_name(IntPtr display, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport(GdkLib)] public static extern int gdk_window_get_state(IntPtr gdkWin);
        [DllImport(GdkLib)] public static extern void gdk_set_program_class([MarshalAs(UnmanagedType.LPUTF8Str)] string programClass);

        [DllImport(GdkLib)] public static extern int gdk_event_get_coords(IntPtr ev, out double x, out double y);
        [DllImport(GdkLib)] public static extern int gdk_event_get_root_coords(IntPtr ev, out double x, out double y);
        [DllImport(GdkLib)] public static extern int gdk_event_get_button(IntPtr ev, out uint button);
        [DllImport(GdkLib)] public static extern uint gdk_event_get_time(IntPtr ev);
        [DllImport(GdkLib)] public static extern int gdk_event_get_keyval(IntPtr ev, out uint keyval);
        [DllImport(GdkLib)] public static extern int gdk_event_get_state(IntPtr ev, out uint state);

        /* ---------- Cairo & Pixbuf ---------- */

        [DllImport(CairoLib)] public static extern void cairo_set_operator(IntPtr cr, int op);
        [DllImport(CairoLib)] public static extern void cairo_set_source_rgba(IntPtr cr, double r, double g, double b, double a);
        [DllImport(CairoLib)] public static extern void cairo_paint(IntPtr cr);
        [DllImport(CairoLib)] public static extern IntPtr cairo_region_create_rectangle(ref CairoRectangleInt rect);
        [DllImport(CairoLib)] public static extern void cairo_region_destroy(IntPtr region);

        [DllImport(PixbufLib)] public static extern IntPtr gdk_pixbuf_loader_new();
        [DllImport(PixbufLib)] public static extern int gdk_pixbuf_loader_write(IntPtr loader, byte[] buf, UIntPtr count, out IntPtr err);
        [DllImport(PixbufLib)] public static extern int gdk_pixbuf_loader_close(IntPtr loader, out IntPtr err);
        [DllImport(PixbufLib)] public static extern IntPtr gdk_pixbuf_loader_get_pixbuf(IntPtr loader);

        /* ---------- WebKit2GTK-4.1 / JSC / Soup ---------- */

        [DllImport(WebKitLib)] public static extern IntPtr webkit_website_data_manager_new(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string prop1,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string val1,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string prop2,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string val2,
            IntPtr endNull);
        [DllImport(WebKitLib)] public static extern IntPtr webkit_web_context_new_with_website_data_manager(IntPtr manager);
        [DllImport(WebKitLib)] public static extern void webkit_web_context_register_uri_scheme(
            IntPtr context,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string scheme,
            IntPtr callback,
            IntPtr userData,
            IntPtr destroyNotify);
        [DllImport(WebKitLib)] public static extern IntPtr webkit_web_context_get_security_manager(IntPtr context);
        [DllImport(WebKitLib)] public static extern void webkit_security_manager_register_uri_scheme_as_secure(
            IntPtr secMgr, [MarshalAs(UnmanagedType.LPUTF8Str)] string scheme);
        [DllImport(WebKitLib)] public static extern void webkit_security_manager_register_uri_scheme_as_cors_enabled(
            IntPtr secMgr, [MarshalAs(UnmanagedType.LPUTF8Str)] string scheme);

        [DllImport(WebKitLib)] public static extern IntPtr webkit_web_view_new_with_context(IntPtr context);
        [DllImport(WebKitLib)] public static extern IntPtr webkit_web_view_get_user_content_manager(IntPtr webView);
        [DllImport(WebKitLib)] public static extern IntPtr webkit_web_view_get_settings(IntPtr webView);
        [DllImport(WebKitLib)] public static extern void webkit_web_view_load_uri(IntPtr webView, [MarshalAs(UnmanagedType.LPUTF8Str)] string uri);
        [DllImport(WebKitLib)] public static extern void webkit_web_view_reload(IntPtr webView);
        [DllImport(WebKitLib)] public static extern void webkit_web_view_set_background_color(IntPtr webView, ref GdkRGBA rgba);
        [DllImport(WebKitLib)] public static extern IntPtr webkit_web_view_get_title(IntPtr webView);
        [DllImport(WebKitLib)] public static extern IntPtr webkit_web_view_get_inspector(IntPtr webView);
        [DllImport(WebKitLib)] public static extern void webkit_web_inspector_show(IntPtr inspector);
        [DllImport(WebKitLib)] public static extern void webkit_web_view_evaluate_javascript(
            IntPtr webView,
            byte[] script,
            IntPtr length,
            IntPtr worldName,
            IntPtr sourceUri,
            IntPtr cancellable,
            IntPtr callback,
            IntPtr userData);
        [DllImport(WebKitLib)] public static extern IntPtr webkit_web_view_evaluate_javascript_finish(
            IntPtr webView, IntPtr result, out IntPtr error);

        [DllImport(WebKitLib)] public static extern void webkit_settings_set_enable_developer_extras(IntPtr settings, int enabled);
        [DllImport(WebKitLib)] public static extern void webkit_settings_set_enable_webgl(IntPtr settings, int enabled);
        [DllImport(WebKitLib)] public static extern void webkit_settings_set_enable_write_console_messages_to_stdout(IntPtr settings, int enabled);

        [DllImport(WebKitLib)] public static extern int webkit_user_content_manager_register_script_message_handler(
            IntPtr manager, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport(WebKitLib)] public static extern void webkit_user_content_manager_add_script(IntPtr manager, IntPtr script);
        [DllImport(WebKitLib)] public static extern IntPtr webkit_user_script_new(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string source,
            int injectedFrames,
            int injectionTime,
            IntPtr allowList,
            IntPtr blockList);
        [DllImport(WebKitLib)] public static extern void webkit_user_script_unref(IntPtr script);

        [DllImport(WebKitLib)] public static extern IntPtr webkit_uri_scheme_request_get_uri(IntPtr request);
        [DllImport(WebKitLib)] public static extern IntPtr webkit_uri_scheme_response_new(IntPtr stream, long contentLength);
        [DllImport(WebKitLib)] public static extern void webkit_uri_scheme_response_set_status(
            IntPtr response, uint statusCode, [MarshalAs(UnmanagedType.LPUTF8Str)] string reasonPhrase);
        [DllImport(WebKitLib)] public static extern void webkit_uri_scheme_response_set_content_type(
            IntPtr response, [MarshalAs(UnmanagedType.LPUTF8Str)] string contentType);
        [DllImport(WebKitLib)] public static extern void webkit_uri_scheme_response_set_http_headers(IntPtr response, IntPtr headers);
        [DllImport(WebKitLib)] public static extern void webkit_uri_scheme_request_finish_with_response(IntPtr request, IntPtr response);

        [DllImport(WebKitLib)] public static extern IntPtr webkit_navigation_policy_decision_get_navigation_action(IntPtr decision);
        [DllImport(WebKitLib)] public static extern IntPtr webkit_navigation_action_get_request(IntPtr action);
        [DllImport(WebKitLib)] public static extern IntPtr webkit_uri_request_get_uri(IntPtr request);
        [DllImport(WebKitLib)] public static extern void webkit_policy_decision_ignore(IntPtr decision);

        [DllImport(WebKitLib)] public static extern uint webkit_context_menu_get_n_items(IntPtr menu);
        [DllImport(WebKitLib)] public static extern IntPtr webkit_context_menu_get_item_at_position(IntPtr menu, uint position);
        [DllImport(WebKitLib)] public static extern void webkit_context_menu_remove(IntPtr menu, IntPtr item);
        [DllImport(WebKitLib)] public static extern int webkit_context_menu_item_get_stock_action(IntPtr item);

        [DllImport(WebKitLib)] public static extern IntPtr webkit_javascript_result_get_js_value(IntPtr jsResult);

        [DllImport(JscLib)] public static extern IntPtr jsc_value_to_string(IntPtr val);
        [DllImport(JscLib)] public static extern IntPtr jsc_value_to_json(IntPtr val, uint indent);
        [DllImport(JscLib)] public static extern int jsc_value_is_boolean(IntPtr val);
        [DllImport(JscLib)] public static extern int jsc_value_to_boolean(IntPtr val);

        [DllImport(SoupLib)] public static extern IntPtr soup_message_headers_new(int type);
        [DllImport(SoupLib)] public static extern void soup_message_headers_append(
            IntPtr headers,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        /* ---------- helpers ---------- */

        public static string FromUtf8(IntPtr p, bool free = false)
        {
            if (p == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUTF8(p); }
            finally { if (free) g_free(p); }
        }

        public static string[] ReadNullTerminatedStrings(IntPtr strv)
        {
            if (strv == IntPtr.Zero) return Array.Empty<string>();
            var list = new List<string>();
            for (int i = 0; ; i++)
            {
                IntPtr p = Marshal.ReadIntPtr(strv, i * IntPtr.Size);
                if (p == IntPtr.Zero) break;
                string s = Marshal.PtrToStringUTF8(p);
                if (s != null) list.Add(s);
            }
            return list.ToArray();
        }

        /// <summary>Extracts the embedded 256×256 PNG frame from a multi-size .ico file, if present.</summary>
        public static byte[] ExtractPngFromIco(byte[] ico)
        {
            if (ico == null || ico.Length < 22) return null;
            int count = ico[4] | (ico[5] << 8);
            byte[] best = null;
            for (int i = 0; i < count && 6 + (i + 1) * 16 <= ico.Length; i++)
            {
                int e = 6 + i * 16;
                int size = BitConverter.ToInt32(ico, e + 8);
                int off = BitConverter.ToInt32(ico, e + 12);
                if (size < 8 || off < 0 || off + size > ico.Length) continue;
                // PNG magic: 89 50 4E 47 0D 0A 1A 0A
                if (ico[off] == 0x89 && ico[off + 1] == 0x50 && ico[off + 2] == 0x4E && ico[off + 3] == 0x47)
                {
                    if (best == null || size > best.Length)
                    {
                        best = new byte[size];
                        Buffer.BlockCopy(ico, off, best, 0, size);
                    }
                }
            }
            return best;
        }

        public static void SetWindowIcon(IntPtr win, byte[] icoBytes)
        {
            if (win == IntPtr.Zero || icoBytes == null || icoBytes.Length == 0) return;
            byte[] img = ExtractPngFromIco(icoBytes) ?? icoBytes;
            IntPtr loader = gdk_pixbuf_loader_new();
            if (loader == IntPtr.Zero) return;
            try
            {
                if (gdk_pixbuf_loader_write(loader, img, (UIntPtr)img.Length, out IntPtr err) == 0)
                {
                    if (err != IntPtr.Zero) g_error_free(err);
                    return;
                }
                if (gdk_pixbuf_loader_close(loader, out err) == 0 && err != IntPtr.Zero) g_error_free(err);
                IntPtr pixbuf = gdk_pixbuf_loader_get_pixbuf(loader);
                if (pixbuf != IntPtr.Zero) gtk_window_set_icon(win, pixbuf);
            }
            catch { }
            finally { g_object_unref(loader); }
        }

        public static void ShowMessage(IntPtr parent, string message, bool error = true)
        {
            IntPtr dlg = gtk_message_dialog_new(
                parent,
                GTK_DIALOG_MODAL | GTK_DIALOG_DESTROY_WITH_PARENT,
                error ? GTK_MESSAGE_ERROR : GTK_MESSAGE_INFO,
                GTK_BUTTONS_OK,
                "%s",
                message ?? "");
            gtk_window_set_title(dlg, "NoteSpin");
            gtk_dialog_run(dlg);
            gtk_widget_destroy(dlg);
        }

        /// <summary>Keeps native signal delegates rooted until Dispose().</summary>
        public sealed class SignalStore : IDisposable
        {
            readonly List<Delegate> pinned = new List<Delegate>();

            public ulong Connect(IntPtr obj, string signal, Delegate handler)
            {
                pinned.Add(handler);
                return g_signal_connect_data(obj, signal, Marshal.GetFunctionPointerForDelegate(handler), IntPtr.Zero, IntPtr.Zero, 0);
            }

            public void Pin(Delegate handler) => pinned.Add(handler);

            public void Dispose() => pinned.Clear();
        }

        static readonly object OneShotLock = new object();
        static readonly HashSet<Delegate> OneShotPinned = new HashSet<Delegate>();

        public static void InvokeOnMainThread(Action action)
        {
            GSourceFunc cb = null;
            cb = _ =>
            {
                lock (OneShotLock) OneShotPinned.Remove(cb);
                action();
                return 0; // G_SOURCE_REMOVE
            };
            lock (OneShotLock) OneShotPinned.Add(cb);
            g_idle_add(Marshal.GetFunctionPointerForDelegate(cb), IntPtr.Zero);
        }

        public static IntPtr PinOneShot(Delegate d)
        {
            lock (OneShotLock) OneShotPinned.Add(d);
            return Marshal.GetFunctionPointerForDelegate(d);
        }

        public static void UnpinOneShot(Delegate d)
        {
            lock (OneShotLock) OneShotPinned.Remove(d);
        }

        /// <summary>Ensures async/await continuations resume safely on the GTK main thread.</summary>
        public sealed class GtkSyncContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object state) =>
                InvokeOnMainThread(() => d(state));

            public override void Send(SendOrPostCallback d, object state) =>
                Post(d, state);
        }
    }
}
