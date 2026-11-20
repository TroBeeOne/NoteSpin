using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static NoteSpin.GtkNative;

namespace NoteSpin
{
    /// <summary>
    /// The Linux GTK3 main window hosting web/index.html in a WebKit2GTK-4.1 view,
    /// plus managing the pool of pop-out NoteWindow instances and native file/folder dialogs.
    /// </summary>
    internal sealed class MainForm
    {
        static readonly GdkRGBA Bg = new GdkRGBA(17 / 255.0, 19 / 255.0, 24 / 255.0, 1.0);
        static readonly GdkRGBA Transparent = new GdkRGBA(0, 0, 0, 0);
        const int MinWidth = 560, MinHeight = 420, NarrowWidth = 340;

        readonly Settings settings;
        readonly Library lib;
        readonly PageHost page;
        readonly SignalStore signals = new SignalStore();
        readonly IntPtr webContext;
        readonly List<NoteWindow> windows = new List<NoteWindow>();
        readonly HashSet<string> opening = new HashSet<string>();
        readonly Dictionary<string, string> recentDropped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string startupFile;
        Task<NoteWindow> making;
        NoteWindow lastShown;
        int windowCount;
        bool narrow, seeThrough, rgbaAvailable;
        bool closeConfirmed, closing, notesRemembered, firstLoaded;

        public IntPtr Handle { get; }
        public string DataDir { get; }
        public bool IsDisposed { get; private set; }

        public MainForm(string dataDir, Settings settings, Library lib, string startupFile)
        {
            DataDir = dataDir;
            this.settings = settings;
            this.lib = lib;
            this.startupFile = startupFile;

            webContext = PageHost.EnsureSharedContext(dataDir);

            Handle = gtk_window_new(GTK_WINDOW_TOPLEVEL);
            gtk_window_set_title(Handle, "NoteSpin");

            SetWindowIcon(Handle, WebAssets.Favicon());

            // Configure RGBA visual BEFORE realizing so see-through background can toggle cleanly at runtime
            IntPtr screen = gtk_widget_get_screen(Handle);
            if (screen != IntPtr.Zero)
            {
                IntPtr visual = gdk_screen_get_rgba_visual(screen);
                if (visual != IntPtr.Zero)
                {
                    gtk_widget_set_visual(Handle, visual);
                    gtk_widget_set_app_paintable(Handle, 1);
                    rgbaAvailable = true;
                }
            }

            narrow = settings.Get("pref:hideBase") == "1";
            FitMinimumSize();
            RestorePlacement();

            page = new PageHost(Bg);
            page.FilesDropped += OnFilesDropped;
            page.DroppedPaths += OnDroppedPaths;
            page.TitleChanged += t => gtk_window_set_title(Handle, string.IsNullOrWhiteSpace(t) ? "NoteSpin" : t);
            page.LoadFinished += OnFirstPageLoaded;
            page.ProcessTerminated += () =>
            {
                ShowError("The web view stopped unexpectedly. Notes are saved as you type; reopen NoteSpin to carry on.");
                closeConfirmed = true;
                Close();
            };

            page.Start(webContext, "index.html", new HostApi(this, settings, lib, null), settings, Array.Empty<object>());
            gtk_container_add(Handle, page.Widget);

            signals.Connect(Handle, "draw", new SignalDraw(OnDraw));
            signals.Connect(Handle, "delete-event", new SignalEvent(OnDeleteEvent));
            signals.Connect(Handle, "destroy", new SignalVoid1(OnDestroy));
        }

        public void Show()
        {
            gtk_widget_show_all(Handle);
            page.Focus();
        }

        async void OnFirstPageLoaded()
        {
            if (firstLoaded) return;
            firstLoaded = true;
            await Task.Delay(1500);
            KeepSpare();
        }

        /* ---------- note windows ---------- */

        internal async Task<NoteWindow> TakeWindow()
        {
            if (closing || closeConfirmed || IsDisposed) throw new InvalidOperationException("NoteSpin is closing.");
            NoteWindow w = windows.FirstOrDefault(x => x.IsFree);
            if (w != null) w.InUse = true;
            else
            {
                var t = making ?? MakeWindow();
                making = null;
                w = await t;
            }
            SpareSoon();
            return w;
        }

        async Task<NoteWindow> MakeWindow()
        {
            var w = new NoteWindow(this, settings, "w" + (++windowCount)) { InUse = true };
            windows.Add(w);
            w.Destroyed += () => { windows.Remove(w); if (lastShown == w) lastShown = null; PushNoteWinState(); };
            w.Freed += OnWindowFreed;
            try { await w.Start(webContext, new HostApi(this, settings, lib, w)); }
            catch
            {
                windows.Remove(w);
                try { w.CloseForGood(); } catch { }
                throw;
            }
            return w;
        }

        async void KeepSpare()
        {
            if (making != null || closing || closeConfirmed || IsDisposed || windows.Any(x => x.IsFree)) return;
            var t = making = MakeWindow();
            NoteWindow w;
            try { w = await t; }
            catch { if (making == t) making = null; return; }
            if (making == t) { making = null; w.InUse = false; }
        }

        void SpareSoon() => Later(async () => { await Task.Delay(1200); KeepSpare(); });

        void OnWindowFreed(NoteWindow w)
        {
            PushNoteWinState();
            Later(async () =>
            {
                await Task.Delay(3000);
                foreach (var extra in windows.Where(x => x.IsFree).Skip(1).ToList()) extra.CloseForGood();
            });
        }

        internal IEnumerable<NoteWindow> OpenNoteWindows => windows.Where(w => w.HasNote && !w.IsDisposed).ToList();

        internal NoteWindow WindowFor(string noteId) =>
            string.IsNullOrEmpty(noteId) ? null : windows.FirstOrDefault(w => w.HasNote && !w.IsDisposed && w.NoteId == noteId);

        internal NoteWindow WindowById(string winId) => windows.FirstOrDefault(w => w.WinId == winId && !w.IsDisposed);

        internal void SendToNotes(object message, NoteWindow except = null)
        {
            foreach (var w in windows.ToList()) if (w != except && !w.IsDisposed) w.Send(message);
        }

        internal void PushNoteWinState()
        {
            if (IsDisposed) return;
            var st = OpenNoteWindows.Select(w => (object)new Dictionary<string, object> { ["win"] = w.WinId, ["id"] = w.NoteId }).ToList();
            Post(new Dictionary<string, object> { ["type"] = "__noteWinState", ["state"] = st });
        }

        internal async Task OpenInWindow(Dictionary<string, object> state, bool activate)
        {
            string id = NoteWindow.NoteIdOf(state);
            var there = WindowFor(id);
            if (there != null) { there.ShowNote(true); return; }
            if (id != null && !opening.Add(id)) return;
            try
            {
                var w = await TakeWindow();
                w.Open(state);
                w.ShowNote(activate);
                PushNoteWinState();
            }
            finally { if (id != null) opening.Remove(id); }
        }

        internal async Task PopOut(Dictionary<string, object> state, bool drag, double grabX, double grabY, bool mouse)
        {
            var w = await TakeWindow();
            w.Open(state);
            w.ShowNote(true);
            PushNoteWinState();
        }

        internal void NoteWindowShown(NoteWindow w) => lastShown = w;

        internal (int X, int Y, int W, int H)? LastShownNoteWindow(NoteWindow except)
        {
            bool onScreen(NoteWindow x) => x != null && x != except && !x.IsDisposed && x.Visible;
            var w = onScreen(lastShown) ? lastShown : windows.LastOrDefault(onScreen);
            return w?.GetBounds();
        }

        internal (int X, int Y, int W, int H) GetBounds()
        {
            gtk_window_get_position(Handle, out int x, out int y);
            gtk_window_get_size(Handle, out int w, out int h);
            return (x, y, w, h);
        }

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

        internal void DockIn(object state)
        {
            gtk_window_deiconify(Handle);
            gtk_window_present(Handle);
            Post(new Dictionary<string, object> { ["type"] = "dockIn", ["state"] = state });
        }

        /* ---------- messages & drops ---------- */

        void OnFilesDropped(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;
            // Short beat so if WebKit's HTML5 drop event already handled it, app.js deduplicates cleanly
            Later(async () =>
            {
                await Task.Delay(60);
                Post(new Dictionary<string, object> { ["type"] = "droppedFiles", ["paths"] = paths });
            });
        }

        void OnDroppedPaths(string[] paths)
        {
            recentDropped.Clear();
            foreach (string p in paths) recentDropped[Path.GetFileName(p)] = p;
        }

        internal string TakeDroppedFilePath(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            if (recentDropped.TryGetValue(fileName, out string p))
            {
                recentDropped.Remove(fileName);
                return p;
            }
            return null;
        }

        internal void Later(Action a)
        {
            if (!IsDisposed) InvokeOnMainThread(() => { if (!IsDisposed) a(); });
        }

        internal void Post(object m) => page.Post(m);

        internal void Reply(string id, string path, string error = null)
        {
            var d = new Dictionary<string, object> { ["id"] = id, ["path"] = path };
            if (error != null) d["error"] = error;
            Post(d);
        }

        internal string TakeStartupFile()
        {
            string f = startupFile;
            startupFile = null;
            return f;
        }

        /* ---------- native file & folder dialogs (XDG Desktop Portal via GtkFileChooserNative) ---------- */

        internal string PickOpen()
        {
            IntPtr dlg = gtk_file_chooser_native_new("Import a NoteSpin backup", Handle, GTK_FILE_CHOOSER_ACTION_OPEN, "Open", "Cancel");
            try
            {
                AddNotebookFilters(dlg);
                string initial = StartDir();
                if (initial != null) gtk_file_chooser_set_current_folder(dlg, initial);
                if (gtk_native_dialog_run(dlg) != GTK_RESPONSE_ACCEPT) return null;
                string path = FromUtf8(gtk_file_chooser_get_filename(dlg), free: true);
                if (!string.IsNullOrEmpty(path)) settings.SetPath("app:backupDir", Path.GetDirectoryName(path));
                return path;
            }
            finally { g_object_unref(dlg); }
        }

        internal string PickSave(string name)
        {
            IntPtr dlg = gtk_file_chooser_native_new("Export a backup", Handle, GTK_FILE_CHOOSER_ACTION_SAVE, "Save", "Cancel");
            try
            {
                AddNotebookFilters(dlg);
                gtk_file_chooser_set_do_overwrite_confirmation(dlg, 1);
                if (!string.IsNullOrEmpty(name)) gtk_file_chooser_set_current_name(dlg, name);
                string initial = StartDir();
                if (initial != null) gtk_file_chooser_set_current_folder(dlg, initial);
                if (gtk_native_dialog_run(dlg) != GTK_RESPONSE_ACCEPT) return null;
                string path = FromUtf8(gtk_file_chooser_get_filename(dlg), free: true);
                if (string.IsNullOrEmpty(path)) return null;
                if (!path.EndsWith(Notebook.Ext, StringComparison.OrdinalIgnoreCase)) path += Notebook.Ext;
                settings.SetPath("app:backupDir", Path.GetDirectoryName(path));
                return path;
            }
            finally { g_object_unref(dlg); }
        }

        internal string PickFolder(string startIn)
        {
            IntPtr dlg = gtk_file_chooser_native_new("Choose a folder for your notebooks", Handle, GTK_FILE_CHOOSER_ACTION_SELECT_FOLDER, "Use this folder", "Cancel");
            try
            {
                if (!string.IsNullOrEmpty(startIn) && Directory.Exists(startIn))
                    gtk_file_chooser_set_current_folder(dlg, startIn);
                if (gtk_native_dialog_run(dlg) != GTK_RESPONSE_ACCEPT) return null;
                return FromUtf8(gtk_file_chooser_get_filename(dlg), free: true);
            }
            finally { g_object_unref(dlg); }
        }

        static void AddNotebookFilters(IntPtr dlg)
        {
            IntPtr f1 = gtk_file_filter_new();
            gtk_file_filter_set_name(f1, "NoteSpin notebook (*" + Notebook.Ext + ")");
            gtk_file_filter_add_pattern(f1, "*" + Notebook.Ext);
            gtk_file_chooser_add_filter(dlg, f1);

            IntPtr f2 = gtk_file_filter_new();
            gtk_file_filter_set_name(f2, "All files");
            gtk_file_filter_add_pattern(f2, "*");
            gtk_file_chooser_add_filter(dlg, f2);
        }

        string StartDir()
        {
            foreach (string c in new[] { settings.GetPath("app:backupDir"), Places.DocumentsDir() })
                if (!string.IsNullOrEmpty(c) && Directory.Exists(c)) return c;
            return null;
        }

        internal void ShowLicenses()
        {
            string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LICENSES.txt");
            if (File.Exists(p))
            {
                try
                {
                    var psi = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                    psi.ArgumentList.Add(p);
                    Process.Start(psi);
                    return;
                }
                catch { }
            }
            ShowMessage(Handle, "LICENSES.txt isn't in the NoteSpin folder.", error: false);
        }

        internal void ShowError(string message) => ShowMessage(Handle, message, error: true);

        /* ---------- window appearance, placement & lifecycle ---------- */

        internal bool SetSeeThrough(bool on)
        {
            IntPtr screen = gtk_widget_get_screen(Handle);
            bool canCompose = rgbaAvailable && screen != IntPtr.Zero && gdk_screen_is_composited(screen) != 0;
            seeThrough = on && canCompose;
            page.SetBackgroundColor(seeThrough ? Transparent : Bg);
            gtk_widget_queue_draw(Handle);
            return seeThrough;
        }

        int OnDraw(IntPtr widget, IntPtr cr, IntPtr userData)
        {
            cairo_set_operator(cr, CAIRO_OPERATOR_SOURCE);
            if (seeThrough) cairo_set_source_rgba(cr, 0, 0, 0, 0);
            else cairo_set_source_rgba(cr, Bg.Red, Bg.Green, Bg.Blue, 1.0);
            cairo_paint(cr);
            return 0; // let WebView draw on top
        }

        internal void SetNarrow(bool on)
        {
            narrow = on;
            FitMinimumSize();
        }

        void FitMinimumSize() => gtk_widget_set_size_request(Handle, narrow ? NarrowWidth : MinWidth, MinHeight);

        void RestorePlacement()
        {
            int defW = 1100, defH = 740;
            try
            {
                IntPtr disp = gdk_display_get_default();
                IntPtr mon = disp != IntPtr.Zero ? gdk_display_get_primary_monitor(disp) : IntPtr.Zero;
                if (mon == IntPtr.Zero && disp != IntPtr.Zero) mon = gdk_display_get_monitor(disp, 0);
                if (mon != IntPtr.Zero)
                {
                    gdk_monitor_get_workarea(mon, out GdkRectangle wa);
                    if (wa.Width >= 800 && wa.Height >= 600)
                    {
                        defW = wa.Width * 4 / 5;
                        defH = wa.Height * 6 / 7;
                    }
                }
            }
            catch { }

            string fixedPlace = Environment.GetEnvironmentVariable("NOTESPIN_WINDOW");
            string[] p = (string.IsNullOrEmpty(fixedPlace) ? settings.Get("app:window") ?? "" : fixedPlace + ",0").Split(',');
            if (p.Length >= 5 && int.TryParse(p[0], out int x) && int.TryParse(p[1], out int y) &&
                int.TryParse(p[2], out int w) && int.TryParse(p[3], out int h) &&
                w >= Math.Min(400, narrow ? NarrowWidth : MinWidth) && h >= 300)
            {
                gtk_window_set_default_size(Handle, w, h);
                gtk_window_move(Handle, x, y); // honored on X11/XWayland, safely ignored on Wayland
                if (p[4] == "1") gtk_window_maximize(Handle);
                return;
            }
            gtk_window_set_default_size(Handle, defW, defH);
        }

        void SavePlacement()
        {
            try
            {
                bool max = gtk_window_is_maximized(Handle) != 0;
                gtk_window_get_position(Handle, out int x, out int y);
                gtk_window_get_size(Handle, out int w, out int h);
                if (w >= 200 && h >= 200)
                    settings.Set("app:window", $"{x},{y},{w},{h},{(max ? 1 : 0)}");
            }
            catch { }
        }

        public void Close()
        {
            if (IsDisposed) return;
            if (closeConfirmed)
            {
                RememberOpenNotes();
                SavePlacement();
                foreach (var w in windows.ToList()) w.CloseForGood();
                gtk_widget_destroy(Handle);
                return;
            }
            BeginGracefulClose();
        }

        int OnDeleteEvent(IntPtr widget, IntPtr ev, IntPtr userData)
        {
            if (closeConfirmed)
            {
                RememberOpenNotes();
                SavePlacement();
                foreach (var w in windows.ToList()) w.CloseForGood();
                return 0; // allow GTK destroy
            }
            BeginGracefulClose();
            return 1; // wait for pages to finish saving
        }

        async void BeginGracefulClose()
        {
            if (closing) return;
            closing = true;
            try
            {
                await Task.WhenAll(new[] { page.WaitWhileBusy() }.Concat(windows.ToList().Select(w => w.WaitWhileBusy())));
            }
            catch { }
            closeConfirmed = true;
            Close();
        }

        void OnDestroy(IntPtr widget, IntPtr userData)
        {
            IsDisposed = true;
            page.Close();
            signals.Dispose();
            gtk_main_quit();
        }
    }
}
