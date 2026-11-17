using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;

namespace NoteSpin
{
    /// <summary>
    /// The object the page reaches as chrome.webview.hostObjects.ns (see web\js\host.js).
    /// Every method returns JSON; a failure comes back as {"__error": message} and host.js turns it into
    /// a rejected promise. Only the app's own page can load in the window (MainForm blocks every other
    /// address), so only it can call these.
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public class HostApi
    {
        readonly MainForm form;
        readonly Settings settings;
        readonly Library lib;
        internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        internal HostApi(MainForm form, Settings settings, Library lib)
        {
            this.form = form;
            this.settings = settings;
            this.lib = lib;
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

        public string CategorySave(string id, string name, string color) => Do(() => lib.Current.SaveCategory(id, name, color));
        public string CategoryDelete(string id) => Do(() => lib.Current.DeleteCategory(id));
    }
}
