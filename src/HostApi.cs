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
    /// (noteWin null), the note page's is given the window it's in.
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

        /* ---------- categories ---------- */

        public string CategoriesAll() => Do(() => lib.Current.ListCategories());
        public string CategorySave(string id, string name, string color) => Do(() => lib.Current.SaveCategory(id, name, color));
        public string CategoryDelete(string id) => Do(() => lib.Current.DeleteCategory(id));

        /* ---------- the note window, from the main page ----------
           A note's "state" is what the editor needs to carry on with it in another window: the note, its text,
           and the key while it's unlocked (see Editor.exportState in web\js\editor.js). */

        void MainPageOnly()
        {
            if (noteWin != null) throw new InvalidOperationException("Only the main window can do that.");
        }

        /// <summary>Whether a note is open in the note window, and which.</summary>
        public string NoteWinState() => Do(() =>
        {
            var w = form.NoteWin;
            return new Dictionary<string, object> { ["open"] = w?.HasNote == true, ["id"] = w?.HasNote == true ? w.NoteId : null };
        });

        /// <summary>Opens a note in the note window (closing the one that's there); activate: give it the focus.</summary>
        public string NoteWinOpen(string stateJson, bool activate) => Do(() =>
        {
            MainPageOnly();
            var state = Obj(stateJson);
            form.Later(async () =>
            {
                try
                {
                    var w = await form.Notes();
                    w.Open(state);
                    w.ShowNote(activate);
                }
                catch (Exception ex) { MessageBox.Show(form, "The note window couldn't open.\n\n" + ex.Message, "NoteSpin", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            });
        });

        /// <summary>
        /// Moves the note open in the main window to the note window. drag: it's being dragged out, held at
        /// (grabX, grabY) CSS pixels from its top-left corner, so the window follows the mouse until it's let go.
        /// </summary>
        public string NoteWinPopOut(string stateJson, bool drag, double grabX, double grabY, string pointerType) => Do(() =>
        {
            MainPageOnly();
            var state = Obj(stateJson);
            double scale = form.PageScale;
            form.Later(async () =>
            {
                try
                {
                    var w = await form.Notes();
                    w.Open(state);
                    if (drag) w.ShowFollowing(grabX, grabY, scale, pointerType == "mouse");
                    else w.ShowNote(true);
                }
                catch (Exception ex) { MessageBox.Show(form, "The note window couldn't open.\n\n" + ex.Message, "NoteSpin", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            });
        });

        /// <summary>The drag that brought a note out of the main window has ended (for touch, where the button can't be watched).</summary>
        public string NoteWinFollowEnd() => Do(() => form.NoteWin?.EndFollow());

        /// <summary>Closes the note in the note window (saving it); the answer arrives as a web message {id} once it's closed.</summary>
        public void NoteWinClose(string id) => form.Later(() =>
        {
            var w = form.NoteWin;
            if (w == null || !w.HasNote) form.Reply(id, null);
            else w.Send(new Dictionary<string, object> { ["type"] = "close", ["id"] = id });
        });

        public string NoteWinFocus() => Do(() =>
        {
            var w = form.NoteWin;
            if (w != null && w.HasNote) w.ShowNote(true);
        });

        /// <summary>The note page saved the last open note id; the main page opens it again on start (null when there's none).</summary>
        public string TakeNoteWindowNote() => Do(() =>
        {
            MainPageOnly();
            string id = settings.Get("app:noteOpen");
            settings.Set("app:noteOpen", "");
            return string.IsNullOrEmpty(id) ? null : id;
        });

        /// <summary>A message for the note page, {type, ...} (categories changed, say).</summary>
        public string ToNote(string json) => Do(() =>
        {
            MainPageOnly();
            form.NoteWin?.Send(Obj(json));
        });

        /* ---------- the note window, from the note page ---------- */

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

        public string NoteWinPinned() => Do(() => NotePageOnly().Pinned);
        public string NoteWinPin(bool on) => Do(() => { var w = NotePageOnly(); w.Pinned = on; return w.Pinned; });

        /// <summary>The note goes back into the main window (state: what the editor needs).</summary>
        public string NoteWinDock(string stateJson) => Do(() =>
        {
            var w = NotePageOnly();
            var state = Obj(stateJson);
            w.HideNote();
            form.DockIn(state);
        });

        /// <summary>A message for the main page, {type, ...} or {id} answering NoteWinClose.</summary>
        public string ToMain(string json) => Do(() =>
        {
            NotePageOnly();
            form.Post(Obj(json));
        });
    }
}
