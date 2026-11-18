using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NoteSpin
{
    /// <summary>
    /// The object the page reaches as chrome.webview.hostObjects.ns (see web\js\host.js).
    /// Every method returns JSON; a failure comes back as {"__error": message} and host.js turns it into
    /// a rejected promise. Only the app's own pages can load in its windows (PageHost blocks every other
    /// address), so only they can call these. Each page has its own: the main page's has no note window
    /// (noteWin null), a note page's is given the window it's in.
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public class HostApi
    {
        readonly MainForm form;
        readonly Settings settings;
        readonly Library lib;
        readonly NoteWindow noteWin;
        internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

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
            ["dataDir"] = form.DataDir,
            ["notebooksDir"] = lib.NotebooksDir,
            ["defaultDir"] = lib.DefaultDir,
            ["safetyDir"] = lib.SafetyDir,
            ["sqlite"] = Sqlite.Version,
            ["fileAssoc"] = FileAssoc.IsSet(),
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

        public string ShowFolder(string which) => Do(() =>
        {
            string dir = which == "safety" ? lib.SafetyDir : lib.NotebooksDir;
            Directory.CreateDirectory(dir);
            Process.Start("explorer.exe", "\"" + dir + "\"");
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
            info["isCurrent"] = string.Equals(Path.GetFullPath(path), lib.Current.FilePath, StringComparison.OrdinalIgnoreCase);
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
            MessageBox.Show(form, "The note window couldn't open.\n\n" + ex.Message, "NoteSpin", MessageBoxButtons.OK, MessageBoxIcon.Error);

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
    }
}
