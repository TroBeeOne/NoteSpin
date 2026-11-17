using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualBasic.FileIO;

namespace NoteSpin
{
    /// <summary>
    /// The notebooks (one .notespin file each) in the notebooks folder, and the one that's open.
    /// The folder is Documents\NoteSpin unless another was chosen in Settings: on a PC where OneDrive backs up
    /// Documents (the usual setup), the notebooks are backed up to the cloud with it. Before a backup replaces
    /// the open notebook, a copy of it goes to "Safety Backups" inside the notebooks folder.
    /// Earlier versions kept notebooks in %LOCALAPPDATA%\NoteSpin\Notebooks; they're moved over on start.
    /// </summary>
    internal sealed class Library : IDisposable
    {
        const int SafetyKeep = 30;
        const string FolderKey = "notebooks:dir";
        readonly Settings settings;
        readonly string legacyDir, legacySafetyDir;

        public string DefaultDir { get; }
        public string NotebooksDir { get; private set; }
        public string SafetyDir => Path.Combine(NotebooksDir, "Safety Backups");
        public Notebook Current { get; private set; }

        /// <summary>Something to tell the person once, when the page starts (notebooks moved, a folder missing).</summary>
        public Dictionary<string, object> Notice { get; set; }

        /// <param name="defaultDir">Documents\NoteSpin (in a test data folder when NOTESPIN_DATA is set).</param>
        public Library(string dataDir, Settings settings, string defaultDir)
        {
            this.settings = settings;
            DefaultDir = Path.GetFullPath(defaultDir);
            legacyDir = Path.Combine(dataDir, "Notebooks");
            legacySafetyDir = Path.Combine(dataDir, "Safety Backups");

            string chosen = settings.Get(FolderKey);
            NotebooksDir = DefaultDir;
            if (!string.IsNullOrEmpty(chosen))
            {
                if (Directory.Exists(chosen)) NotebooksDir = Path.GetFullPath(chosen);
                else Notice = new Dictionary<string, object> { ["missing"] = chosen, ["using"] = DefaultDir };
            }
            Directory.CreateDirectory(NotebooksDir);
            MoveLegacyNotebooks();

            string last = settings.Get("notebook:last");
            if (!string.IsNullOrEmpty(last) && File.Exists(last) && InLibrary(last) && TryOpen(last)) return;
            foreach (string f in Files())
                if (TryOpen(f)) return;
            Switch(Path.Combine(NotebooksDir, "My Notes" + Notebook.Ext));
        }

        public void Dispose() => Current?.Dispose();

        // Notebooks (and safety copies) left in the old AppData folder move to the notebooks folder.
        // One that can't move yet (open in an older NoteSpin, say) stays, and is tried again next time.
        void MoveLegacyNotebooks()
        {
            if (!Directory.Exists(legacyDir) || SamePath(legacyDir, NotebooksDir)) return;
            string last = settings.Get("notebook:last");
            int moved = 0;
            foreach (string f in Directory.GetFiles(legacyDir, "*" + Notebook.Ext))
            {
                try
                {
                    string dest = FreePathIn(NotebooksDir, Path.GetFileNameWithoutExtension(f));
                    File.Move(f, dest);
                    moved++;
                    if (!string.IsNullOrEmpty(last) && SamePath(last, f)) settings.Set("notebook:last", dest);
                }
                catch { }
            }
            if (Directory.Exists(legacySafetyDir))
            {
                foreach (string f in Directory.GetFiles(legacySafetyDir, "*" + Notebook.Ext))
                {
                    try
                    {
                        Directory.CreateDirectory(SafetyDir);
                        File.Move(f, FreePathIn(SafetyDir, Path.GetFileNameWithoutExtension(f)));
                    }
                    catch { }
                }
                TryRemoveEmpty(legacySafetyDir);
            }
            TryRemoveEmpty(legacyDir);
            if (moved > 0) Notice = new Dictionary<string, object> { ["moved"] = moved, ["to"] = NotebooksDir };
        }

        static void TryRemoveEmpty(string dir)
        {
            try { if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
        }

        /// <summary>
        /// Uses another folder for notebooks. move: take the notebooks (and their safety copies) along;
        /// otherwise switch to whatever notebooks are already there (a new "My Notes" if there are none).
        /// </summary>
        public void ChangeFolder(string dir, bool move)
        {
            dir = Path.GetFullPath(dir);
            if (SamePath(dir, NotebooksDir)) return;
            if (SamePath(dir, SafetyDir) || dir.StartsWith(SafetyDir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a folder outside Safety Backups.");
            Directory.CreateDirectory(dir);
            string oldDir = NotebooksDir, oldSafety = SafetyDir, current = Current.FilePath, currentNew = null;
            var failed = new List<string>();
            if (move)
            {
                Current.Dispose();
                Current = null;
                foreach (string f in Directory.GetFiles(oldDir, "*" + Notebook.Ext))
                {
                    try
                    {
                        string dest = FreePathIn(dir, Path.GetFileNameWithoutExtension(f));
                        File.Move(f, dest);
                        if (SamePath(f, current)) currentNew = dest;
                    }
                    catch { failed.Add(Path.GetFileNameWithoutExtension(f)); }
                }
                if (Directory.Exists(oldSafety))
                {
                    string newSafety = Path.Combine(dir, "Safety Backups");
                    foreach (string f in Directory.GetFiles(oldSafety, "*" + Notebook.Ext))
                    {
                        try
                        {
                            Directory.CreateDirectory(newSafety);
                            File.Move(f, FreePathIn(newSafety, Path.GetFileNameWithoutExtension(f)));
                        }
                        catch { }
                    }
                    TryRemoveEmpty(oldSafety);
                }
            }
            NotebooksDir = dir;
            settings.Set(FolderKey, dir);
            // open the notebook that was open (if it moved along), else the first one there, else a new "My Notes"
            if (!(currentNew != null && TryOpen(currentNew)) && !Files().Any(TryOpen))
                Switch(FreePathIn(dir, "My Notes"));
            if (failed.Count > 0)
                throw new IOException($"The folder changed, but {failed.Count} notebook(s) couldn't be moved and are still in {oldDir}: {string.Join(", ", failed)}. Are they open in another NoteSpin window?");
        }

        IEnumerable<string> Files() =>
            Directory.EnumerateFiles(NotebooksDir, "*" + Notebook.Ext).OrderBy(f => Path.GetFileName(f), StringComparer.CurrentCultureIgnoreCase);

        bool TryOpen(string path)
        {
            try { Switch(path); return true; }
            catch { return false; }
        }

        void Switch(string path)
        {
            var nb = new Notebook(path);
            Current?.Dispose();
            Current = nb;
            settings.Set("notebook:last", nb.FilePath);
        }

        public bool InLibrary(string path) =>
            string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)).TrimEnd('\\'), NotebooksDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

        static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>The open notebook and every notebook in the folder.</summary>
        public Dictionary<string, object> State()
        {
            var list = Files().Select(f =>
            {
                var fi = new FileInfo(f);
                return (object)new Dictionary<string, object>
                {
                    ["name"] = Path.GetFileNameWithoutExtension(f), ["path"] = fi.FullName,
                    ["modified"] = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeMilliseconds(),
                    ["current"] = SamePath(f, Current.FilePath),
                };
            }).ToList();
            return new Dictionary<string, object> { ["current"] = Current.Info(), ["notebooks"] = list, ["dir"] = NotebooksDir, ["defaultDir"] = DefaultDir };
        }

        /* ---------- names ---------- */

        static string CleanName(string name)
        {
            name = (name ?? "").Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c.ToString(), "");
            name = name.Trim().TrimEnd('.');
            if (name.Length > 80) name = name.Substring(0, 80).Trim();
            return name;
        }

        /// <summary>A path in the notebooks folder for this name, adding " (2)", " (3)"… if it's taken.</summary>
        string FreePath(string name, string except = null) => FreePathIn(NotebooksDir, name, except);

        static string FreePathIn(string dir, string name, string except = null)
        {
            string baseName = CleanName(name);
            if (baseName.Length == 0) baseName = "Notebook";
            for (int i = 1; ; i++)
            {
                string p = Path.Combine(dir, (i == 1 ? baseName : $"{baseName} ({i})") + Notebook.Ext);
                if (!File.Exists(p) || (except != null && SamePath(p, except))) return p;
            }
        }

        /* ---------- notebooks ---------- */

        public void Open(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("That notebook isn't there any more.");
            if (!SamePath(path, Current.FilePath)) Switch(path);
        }

        public void Create(string name)
        {
            if (CleanName(name).Length == 0) throw new InvalidOperationException("Give the notebook a name.");
            Switch(FreePath(name));
        }

        public void Rename(string name)
        {
            if (CleanName(name).Length == 0) throw new InvalidOperationException("Give the notebook a name.");
            string from = Current.FilePath, to = FreePath(name, from);
            if (SamePath(from, to) && Path.GetFileName(from) == Path.GetFileName(to)) return;
            Current.Dispose();
            Current = null;
            try
            {
                if (SamePath(from, to))
                {
                    // only the letter case changes: Windows needs a step in between
                    string tmp = from + ".rename";
                    File.Move(from, tmp);
                    File.Move(tmp, to);
                }
                else File.Move(from, to);
            }
            catch (IOException ex)
            {
                Switch(from);
                throw new IOException("The notebook couldn't be renamed. Is it open in another NoteSpin window? (" + ex.Message.TrimEnd('.') + ")");
            }
            Switch(to);
        }

        /// <summary>Sends a notebook to the Recycle Bin. If it's the open one, another opens (or a new "My Notes").</summary>
        public void Delete(string path)
        {
            if (!InLibrary(path)) throw new InvalidOperationException("Only notebooks in the Notebooks folder can be deleted here.");
            bool current = SamePath(path, Current.FilePath);
            if (current)
            {
                Current.Dispose();
                Current = null;
            }
            try { FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin); }
            catch (Exception)
            {
                if (current) Switch(path);
                throw new IOException("The notebook couldn't be deleted. Is it open in another NoteSpin window?");
            }
            if (!current) return;
            foreach (string f in Files())
                if (TryOpen(f)) return;
            Switch(Path.Combine(NotebooksDir, "My Notes" + Notebook.Ext));
        }

        /* ---------- backups ---------- */

        /// <summary>Writes a backup of the open notebook to dest, replacing dest only once the copy is complete.</summary>
        public void Export(string dest)
        {
            dest = Path.GetFullPath(dest);
            if (SamePath(dest, Current.FilePath)) throw new InvalidOperationException("Choose a different file: that's the notebook itself.");
            string tmp = dest + ".partial";
            if (File.Exists(tmp)) File.Delete(tmp);
            try
            {
                Current.CopyTo(tmp);
                if (File.Exists(dest)) File.Replace(tmp, dest, null);
                else File.Move(tmp, dest);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        /// <summary>Adds a backup to the Notebooks folder as a notebook of its own, and opens it.</summary>
        public string ImportAsNew(string src)
        {
            var info = Notebook.Inspect(src);
            string dest = FreePath((string)info["name"]);
            Notebook.CopyFile(src, dest);
            Switch(dest);
            return Current.Name;
        }

        /// <summary>Replaces the open notebook's notes with a backup's. The notebook keeps its name.</summary>
        /// <returns>Where the safety copy of the notebook as it was went.</returns>
        public string ReplaceCurrent(string src)
        {
            Notebook.Inspect(src);
            string target = Current.FilePath;
            string incoming = target + ".incoming";
            if (File.Exists(incoming)) File.Delete(incoming);
            Notebook.CopyFile(src, incoming);
            try
            {
                using (new Notebook(incoming)) { }   // opens as a notebook (and upgrades an older one) before it replaces anything

                Directory.CreateDirectory(SafetyDir);
                string safety = Path.Combine(SafetyDir, $"{Current.Name} before restore {DateTime.Now:yyyy-MM-dd HHmmss}{Notebook.Ext}");
                Current.CopyTo(safety);

                Current.Dispose();
                Current = null;
                try { File.Replace(incoming, target, null); }
                catch (IOException ex)
                {
                    Switch(target);
                    throw new IOException("The notebook couldn't be replaced. Is it open in another NoteSpin window? (" + ex.Message.TrimEnd('.') + ")");
                }
                Switch(target);
                PruneSafety();
                return safety;
            }
            finally
            {
                try { if (File.Exists(incoming)) File.Delete(incoming); } catch { }
            }
        }

        void PruneSafety()
        {
            try
            {
                foreach (var f in new DirectoryInfo(SafetyDir).GetFiles("*" + Notebook.Ext).OrderByDescending(f => f.LastWriteTimeUtc).Skip(SafetyKeep))
                    f.Delete();
            }
            catch { }
        }
    }
}
