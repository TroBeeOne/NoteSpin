using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
#if !LINUX
using System.Windows.Forms;
#endif

namespace NoteSpin
{
    /// <summary>
    /// The object the page reaches via window.NS (see web/js/host.js).
    /// Every method returns JSON; a failure comes back as {"__error": message} and host.js turns it into
    /// a rejected promise. Only the app's own pages can load in its windows (PageHost blocks every other
    /// address), so only they can call these. Each page has its own: the main page's has no note window
    /// (noteWin null), a note page's is given the window it's in.
    /// </summary>
#if !LINUX
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDual)]
#endif
    public class HostApi
    {
        internal sealed class JsonCompat
        {
            public string Serialize(object value) => JsonCodec.Serialize(value);
            public object DeserializeObject(string json) => JsonCodec.Deserialize(json);
        }

        readonly MainForm form;
        readonly Settings settings;
        readonly Library lib;
        readonly NoteWindow noteWin;
        internal static readonly JsonCompat Json = new JsonCompat();

        internal HostApi(MainForm form, Settings settings, Library lib, NoteWindow noteWin)
        {
            this.form = form;
            this.settings = settings;
            this.lib = lib;
            this.noteWin = noteWin;
        }

        static string J(object o) => Json.Serialize(o);

        static string Do(Func<object> f)
        {
            try { return J(f()); }
            catch (Exception ex) { return J(new Dictionary<string, object> { ["__error"] = ex.Message }); }
        }

        static string Do(Action a) => Do(() => { a(); return true; });

        static Dictionary<string, object> Obj(string json) =>
            Json.DeserializeObject(json) as Dictionary<string, object> ?? throw new ArgumentException("Expected an object.");

        /* ---------- app ---------- */

        public string AppInfo() => Do(() => new Dictionary<string, object>
        {
            ["version"] = typeof(HostApi).Assembly.GetName().Version.ToString(3),
#if LINUX
            ["platform"] = "linux",
            ["wayland"] = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")),
#else
            ["platform"] = "windows",
            ["wayland"] = false,
#endif
            ["dataDir"] = form.DataDir,
            ["notebooksDir"] = lib.NotebooksDir,
            ["defaultDir"] = lib.DefaultDir,
            ["safetyDir"] = lib.SafetyDir,
            ["sqlite"] = Sqlite.Version,
            ["fileAssoc"] = FileAssoc.IsSet(),
            ["portable"] = Places.Portable,
            ["portableDir"] = Places.PortableDir,
            ["pcNotebooksDir"] = Places.PcNotebooksDir,
            ["ansi"] = System.Text.Encoding.Default.WebName,   // the PC's "ANSI" code page, for older text files
        });

        /// <summary>
        /// Turns portable on or off (see PortableSwitch): NoteSpin closes and starts again, switching on the way.
        /// bring: the notebooks (and, going back to this PC, the settings) come along. False: nothing to switch.
        /// </summary>
        public string PortableSet(bool on, bool bring) => Do(() =>
        {
            MainPageOnly();
            if (on == Places.Portable) return false;
            if (on) PortableSwitch.CheckCanBePortable();
            PortableSwitch.Start(on, bring);
            form.Later(() => form.Close());
            return true;
        });

        // settings: the page calls these synchronously while it starts up
        public string PrefGet(string key) => Do(() => settings.Get("pref:" + key));
        public string PrefSet(string key, string value) => Do(() => settings.Set("pref:" + key, value));

        public string TakeStartupFile() => Do(() => form.TakeStartupFile());

        public string FileAssocSet(bool on) => Do(() => { FileAssoc.Set(on); return FileAssoc.IsSet(); });

        public string ShowLicenses() => Do(() => form.ShowLicenses());

        /// <summary>The main window's see-through background on or off; whether it's on (false if Windows wouldn't).</summary>
        public string SeeThroughSet(bool on) => Do(() =>
        {
            if (noteWin != null) throw new InvalidOperationException("Only the main window can do that.");
            return form.SetSeeThrough(on);
        });

        /// <summary>The spinner base hidden or shown (Settings); hidden, the main window can be made narrower.</summary>
        public string BaseHiddenSet(bool hidden) => Do(() =>
        {
            MainPageOnly();
            form.SetNarrow(hidden);
        });

#if !LINUX
        /* ---------- the window frame hidden (Settings; see MainForm) ----------
           The Linux build doesn't have these yet: there, FrameInfo answers null and the page leaves the setting out. */

        /// <summary>For the page as it starts (synchronously): {hidden}, whether the window started without its frame.</summary>
        public string FrameInfo() => Do(() =>
        {
            MainPageOnly();
            return new Dictionary<string, object> { ["hidden"] = form.Frameless };
        });

        /// <summary>
        /// The frame hidden or shown, with where the page's parts are (CSS pixels): the cards' box starts at
        /// (cardsLeft, cardsTop), the part shown at (shownLeft, shownTop). fitWidth: narrow the cards' box to this
        /// (0: leave it). Whether the frame is hidden now.
        /// </summary>
        public string FrameSet(bool hidden, double cardsLeft, double cardsTop, double shownLeft, double shownTop, double fitWidth) => Do(() =>
        {
            MainPageOnly();
            return form.SetFrame(hidden, cardsLeft, cardsTop, shownLeft, shownTop, fitWidth);
        });

        /// <summary>Moves the window with the pointer that's down on the page (mouse: a mouse, not a finger). Whether it moved.</summary>
        public string FrameDrag(bool mouse) => Do(() =>
        {
            MainPageOnly();
            return form.DragWindow(mouse);
        });

        /// <summary>Resizes the window from one of the page's edges ("left", "topright", …) while the pointer is down.</summary>
        public string FrameSize(string edge, bool mouse) => Do(() =>
        {
            MainPageOnly();
            form.SizeWindow(edge, mouse);
        });

        /// <summary>"minimize" or "close", from the title strip.</summary>
        public string FrameCommand(string what) => Do(() =>
        {
            MainPageOnly();
            form.WindowCommand(what);
        });
#endif

        public string ShowFolder(string which) => Do(() =>
        {
            string dir = which == "safety" ? lib.SafetyDir : lib.NotebooksDir;
            Directory.CreateDirectory(dir);
#if LINUX
            var psi = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            psi.ArgumentList.Add(dir);
            Process.Start(psi);
#else
            Process.Start("explorer.exe", "\"" + dir + "\"");
#endif
        });

        /* ---------- notebooks ---------- */

        public string NbState() => Do(() => lib.State());
        public string NbOpen(string path) => Do(() => { lib.Open(path); return lib.State(); });
        public string NbCreate(string name) => Do(() => { lib.Create(name); return lib.State(); });
        public string NbRename(string name) => Do(() => { lib.Rename(name); return lib.State(); });
        public string NbDelete(string path) => Do(() => { lib.Delete(path); return lib.State(); });

        /// <summary>A file about to be imported: what's in it, and whether it's already one of the notebooks.</summary>
        public string NbInspect(string path) => Do(() =>
        {
            var info = Notebook.Inspect(path);
            info["inLibrary"] = lib.InLibrary(path);
            info["isCurrent"] = string.Equals(Path.GetFullPath(path), lib.Current.FilePath, Places.PathComparison);
            return info;
        });

        public string NbImportAsNew(string path) => Do(() => { lib.ImportAsNew(path); return lib.State(); });

        public string NbReplaceCurrent(string path) => Do(() =>
        {
            string safety = lib.ReplaceCurrent(path);
            var st = lib.State();
            st["safety"] = safety;
            return st;
        });

        // Dialogs return at once; the answer arrives as a web message {id, path, error}
        public void NbExportDialog(string id) =>
            form.Later(() =>
            {
                string path = form.PickSave($"{lib.Current.Name} {DateTime.Now:yyyy-MM-dd}{Notebook.Ext}");
                if (path == null) { form.Reply(id, null); return; }
                try { lib.Export(path); form.Reply(id, path); }
                catch (Exception ex) { form.Reply(id, null, ex.Message); }
            });

        public void NbImportDialog(string id) => form.Later(() => form.Reply(id, form.PickOpen()));

        public void NbFolderDialog(string id) => form.Later(() => form.Reply(id, form.PickFolder(lib.NotebooksDir)));

        /// <summary>Uses another folder for notebooks; move: take the notebooks along.</summary>
        public string NbSetFolder(string path, bool move) => Do(() => { lib.ChangeFolder(path, move); return lib.State(); });

        /// <summary>Something to say once when the page starts (notebooks moved to the new folder, a folder missing), or null.</summary>
        public string TakeNotice() => Do(() => { var n = lib.Notice; lib.Notice = null; return n; });

        /* ---------- notes ---------- */

        public string NotesAll() => Do(() => lib.Current.ListAll());
        public string NoteGet(string id) => Do(() => lib.Current.GetNote(id));
        public string NoteSave(string json) => Do(() => lib.Current.SaveNote(Obj(json)));
        public string NoteDelete(string id) => Do(() => lib.Current.DeleteNote(id));

        public string NotesSearch(string termsJson) => Do(() =>
            lib.Current.Search(((Json.DeserializeObject(termsJson) as IEnumerable) ?? new object[0]).OfType<string>()));

        /* ---------- copying notes from Microsoft Sticky Notes (which is only read, never changed) ---------- */

        /// <summary>
        /// The notes in Sticky Notes, for choosing which to copy: {found: false} when it isn't on this PC, else
        /// {found: true, notes: [{sid, lines, theme, created, updated, copied}]}; copied: into this notebook before.
        /// </summary>
        public string StickyNotesRead() => Do(() =>
        {
            var notes = StickyNotes.Read();
            if (notes == null) return new Dictionary<string, object> { ["found"] = false };
            var before = lib.Current.CopiedSources("sticky:");
            foreach (var n in notes) n["copied"] = before.Contains("sticky:" + n["sid"]);
            return new Dictionary<string, object> { ["found"] = true, ["notes"] = notes };
        });

        /// <summary>Adds the chosen notes to the open notebook, as new notes (see Notebook.AddCopiedNotes). Returns how many.</summary>
        public string StickyNotesCopy(string notesJson) => Do(() =>
            lib.Current.AddCopiedNotes(((Json.DeserializeObject(notesJson) as IEnumerable) ?? new object[0]).OfType<Dictionary<string, object>>()));

        /* ---------- categories ---------- */

        public string CategoriesAll() => Do(() => lib.Current.ListCategories());
        public string CategorySave(string id, string name, string color) => Do(() => lib.Current.SaveCategory(id, name, color));
        public string CategoryDelete(string id) => Do(() => lib.Current.DeleteCategory(id));

        /* ---------- note windows, from the main page ----------
           A note's "state" is what the editor needs to carry on with it in another window: the note, its text,
           and the key while it's unlocked (see Editor.exportState in web\js\editor.js). A note is open in one
           place at a time: the main window or one note window. */

        void MainPageOnly()
        {
            if (noteWin != null) throw new InvalidOperationException("Only the main window can do that.");
        }

        void Failed(Exception ex) =>
#if LINUX
            form.ShowError("The note window couldn't open.\n\n" + ex.Message);
#else
            MessageBox.Show(form, "The note window couldn't open.\n\n" + ex.Message, "NoteSpin", MessageBoxButtons.OK, MessageBoxIcon.Error);
#endif

        /// <summary>The note windows with a note in them: [{win, id}] (id null for a new note not saved yet).</summary>
        public string NoteWinState() => Do(() =>
            form.OpenNoteWindows.Select(w => (object)new Dictionary<string, object> { ["win"] = w.WinId, ["id"] = w.NoteId }).ToList());

        /// <summary>Opens a note in a window of its own (or brings its window forward); activate: give it the focus.</summary>
        public string NoteWinOpen(string stateJson, bool activate) => Do(() =>
        {
            MainPageOnly();
            var state = Obj(stateJson);
            form.Later(async () =>
            {
                try { await form.OpenInWindow(state, activate); }
                catch (Exception ex) { Failed(ex); }
            });
        });

        /// <summary>
        /// Moves the note open in the main window to a window of its own. drag: it's being dragged out, held at
        /// (grabX, grabY) CSS pixels from its top-left corner, so the window follows the mouse until it's let go.
        /// </summary>
        public string NoteWinPopOut(string stateJson, bool drag, double grabX, double grabY, string pointerType) => Do(() =>
        {
            MainPageOnly();
            var state = Obj(stateJson);
            form.Later(async () =>
            {
                try { await form.PopOut(state, drag, grabX, grabY, pointerType == "mouse"); }
                catch (Exception ex) { Failed(ex); }
            });
        });

        /// <summary>The drag that brought a note out of the main window has ended (for touch, where the button can't be watched).</summary>
        public string NoteWinFollowEnd() => Do(() => { foreach (var w in form.OpenNoteWindows) w.EndFollow(); });

        /// <summary>Closes the note in note window win (saving it); the answer arrives as a web message {id} once it's closed.</summary>
        public void NoteWinClose(string win, string id) => form.Later(() =>
        {
            var w = form.WindowById(win);
            if (w == null || !w.HasNote) form.Reply(id, null);
            else w.Send(new Dictionary<string, object> { ["type"] = "close", ["id"] = id });
        });

        /// <summary>Brings the window a note is out in to the front.</summary>
        public string NoteWinFocus(string noteId) => Do(() => form.WindowFor(noteId)?.ShowNote(true));

        /// <summary>The notes that were out when NoteSpin closed; the main page opens them in their windows again (once).</summary>
        public string TakeNoteWindowNotes() => Do(() =>
        {
            MainPageOnly();
            string ids = settings.Get("app:noteOpen") ?? "";
            settings.Set("app:noteOpen", "");
            return ids.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();
        });

        /// <summary>A message for every note page, {type, ...} (categories changed, say).</summary>
        public string ToNote(string json) => Do(() =>
        {
            MainPageOnly();
            form.SendToNotes(Obj(json));
        });

        /* ---------- a note window, from its page ---------- */

        NoteWindow NotePageOnly() => noteWin ?? throw new InvalidOperationException("Only the note window can do that.");

        /// <summary>The note page is listening for messages.</summary>
        public string NoteWinReady() => Do(() => NotePageOnly().PageReady());

        /// <summary>{open, id, locked, title, band, paper}: the note it shows, and its colours.</summary>
        public string NoteWinSetState(string json) => Do(() => NotePageOnly().SetState(Obj(json)));

        public string NoteWinHide() => Do(() => NotePageOnly().HideNote());

        /// <summary>The note's top strip was pressed: the window moves with the pointer until it's let go.</summary>
        public string NoteWinDrag(string pointerType) => Do(() =>
        {
            var w = NotePageOnly();
            form.Later(() => w.MoveByStrip(pointerType == "mouse"));
        });

        public string NoteWinPin(bool on) => Do(() => { var w = NotePageOnly(); w.Pinned = on; return w.Pinned; });

        /// <summary>The note goes back into the main window (state: what the editor needs).</summary>
        public string NoteWinDock(string stateJson) => Do(() =>
        {
            var w = NotePageOnly();
            var state = Obj(stateJson);
            w.HideNote();
            form.DockIn(state);
        });

        /// <summary>Ctrl+N in a note window: a new note in another window of its own (state: {note: null, categoryId, html}).</summary>
        public string NoteWinNew(string stateJson) => Do(() =>
        {
            NotePageOnly();
            var state = Obj(stateJson);
            form.Later(async () =>
            {
                try { await form.OpenInWindow(state, true); }
                catch (Exception ex) { Failed(ex); }
            });
        });

        /// <summary>
        /// A message for the main page, {type, ...} or {id} answering NoteWinClose. Changed categories are passed
        /// on to the other note windows too, so their category lists are up to date.
        /// </summary>
        public string ToMain(string json) => Do(() =>
        {
            var w = NotePageOnly();
            var m = Obj(json);
            form.Post(m);
            if (m.TryGetValue("type", out object t) && t as string == "categoriesChanged")
                form.SendToNotes(new Dictionary<string, object> { ["type"] = "categoriesChanged" }, w);
        });

#if LINUX
        /* ---------- Linux WebKitGTK message dispatch ---------- */

        static string StrArg(IList<object> a, int i) => i < a.Count && a[i] != null ? Convert.ToString(a[i]) : null;
        static bool BoolArg(IList<object> a, int i) => i < a.Count && a[i] != null && Convert.ToBoolean(a[i]);
        static double DblArg(IList<object> a, int i) => i < a.Count && a[i] != null ? Convert.ToDouble(a[i]) : 0;

        internal bool TryDispatch(string method, IList<object> a, out string resultJson)
        {
            resultJson = null;
            switch (method)
            {
                case "AppInfo": resultJson = AppInfo(); return true;
                case "PortableSet": resultJson = PortableSet(BoolArg(a, 0), BoolArg(a, 1)); return true;
                case "PrefGet": resultJson = PrefGet(StrArg(a, 0)); return true;
                case "PrefSet": resultJson = PrefSet(StrArg(a, 0), StrArg(a, 1)); return true;
                case "TakeStartupFile": resultJson = TakeStartupFile(); return true;
                case "FileAssocSet": resultJson = FileAssocSet(BoolArg(a, 0)); return true;
                case "ShowLicenses": resultJson = ShowLicenses(); return true;
                case "SeeThroughSet": resultJson = SeeThroughSet(BoolArg(a, 0)); return true;
                case "BaseHiddenSet": resultJson = BaseHiddenSet(BoolArg(a, 0)); return true;
                case "ShowFolder": resultJson = ShowFolder(StrArg(a, 0)); return true;
                case "NbState": resultJson = NbState(); return true;
                case "NbOpen": resultJson = NbOpen(StrArg(a, 0)); return true;
                case "NbCreate": resultJson = NbCreate(StrArg(a, 0)); return true;
                case "NbRename": resultJson = NbRename(StrArg(a, 0)); return true;
                case "NbDelete": resultJson = NbDelete(StrArg(a, 0)); return true;
                case "NbInspect": resultJson = NbInspect(StrArg(a, 0)); return true;
                case "NbImportAsNew": resultJson = NbImportAsNew(StrArg(a, 0)); return true;
                case "NbReplaceCurrent": resultJson = NbReplaceCurrent(StrArg(a, 0)); return true;
                case "NbExportDialog": NbExportDialog(StrArg(a, 0)); return false;
                case "NbImportDialog": NbImportDialog(StrArg(a, 0)); return false;
                case "NbFolderDialog": NbFolderDialog(StrArg(a, 0)); return false;
                case "NbSetFolder": resultJson = NbSetFolder(StrArg(a, 0), BoolArg(a, 1)); return true;
                case "TakeNotice": resultJson = TakeNotice(); return true;
                case "NotesAll": resultJson = NotesAll(); return true;
                case "NoteGet": resultJson = NoteGet(StrArg(a, 0)); return true;
                case "NoteSave": resultJson = NoteSave(StrArg(a, 0)); return true;
                case "NoteDelete": resultJson = NoteDelete(StrArg(a, 0)); return true;
                case "NotesSearch": resultJson = NotesSearch(StrArg(a, 0)); return true;
                case "StickyNotesRead": resultJson = StickyNotesRead(); return true;
                case "StickyNotesCopy": resultJson = StickyNotesCopy(StrArg(a, 0)); return true;
                case "CategoriesAll": resultJson = CategoriesAll(); return true;
                case "CategorySave": resultJson = CategorySave(StrArg(a, 0), StrArg(a, 1), StrArg(a, 2)); return true;
                case "CategoryDelete": resultJson = CategoryDelete(StrArg(a, 0)); return true;
                case "NoteWinState": resultJson = NoteWinState(); return true;
                case "NoteWinOpen": resultJson = NoteWinOpen(StrArg(a, 0), BoolArg(a, 1)); return true;
                case "NoteWinPopOut": resultJson = NoteWinPopOut(StrArg(a, 0), BoolArg(a, 1), DblArg(a, 2), DblArg(a, 3), StrArg(a, 4)); return true;
                case "NoteWinFollowEnd": resultJson = NoteWinFollowEnd(); return true;
                case "NoteWinClose": NoteWinClose(StrArg(a, 0), StrArg(a, 1)); return false;
                case "NoteWinFocus": resultJson = NoteWinFocus(StrArg(a, 0)); return true;
                case "TakeNoteWindowNotes": resultJson = TakeNoteWindowNotes(); return true;
                case "ToNote": resultJson = ToNote(StrArg(a, 0)); return true;
                case "NoteWinReady": resultJson = NoteWinReady(); return true;
                case "NoteWinSetState": resultJson = NoteWinSetState(StrArg(a, 0)); return true;
                case "NoteWinHide": resultJson = NoteWinHide(); return true;
                case "NoteWinDrag": resultJson = NoteWinDrag(StrArg(a, 0)); return true;
                case "NoteWinPin": resultJson = NoteWinPin(BoolArg(a, 0)); return true;
                case "NoteWinDock": resultJson = NoteWinDock(StrArg(a, 0)); return true;
                case "NoteWinNew": resultJson = NoteWinNew(StrArg(a, 0)); return true;
                case "ToMain": resultJson = ToMain(StrArg(a, 0)); return true;
                case "DroppedFilePath": resultJson = Do(() => form.TakeDroppedFilePath(StrArg(a, 0))); return true;
                case "ReadTextDrop": resultJson = Do(() => ReadTextDrop(StrArg(a, 0))); return true;
                default:
                    resultJson = J(new Dictionary<string, object> { ["__error"] = "Unknown host method: " + method });
                    return true;
            }
        }

        static Dictionary<string, object> ReadTextDrop(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new FileNotFoundException("That file wasn't found.");
            var fi = new FileInfo(path);
            const long maxBytes = 1024 * 1024;
            if (fi.Length > maxBytes)
                return new Dictionary<string, object> { ["name"] = fi.Name, ["size"] = fi.Length, ["tooBig"] = true };
            byte[] bytes = File.ReadAllBytes(path);
            return new Dictionary<string, object> { ["name"] = fi.Name, ["size"] = (long)bytes.Length, ["base64"] = Convert.ToBase64String(bytes) };
        }
#endif
    }
}
