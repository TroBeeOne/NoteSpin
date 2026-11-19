using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace NoteSpin
{
    /// <summary>
    /// Turns portable on or off (Settings → Portable; see Places). The NoteSpin that's running starts another with
    /// --switch and closes; the new one waits until it's gone, so none of the files is in use, moves things over
    /// and then starts as usual.
    /// - On: makes NoteSpin Data beside NoteSpin.exe, with a copy of this PC's settings and (bring) of its
    ///   notebooks. Nothing on the PC changes.
    /// - Off: renames NoteSpin Data to "NoteSpin Data (off)", with everything still in it (renaming it back makes
    ///   NoteSpin portable again). bring: its settings and notebooks are copied to this PC; a notebook already
    ///   there is never replaced (one of the same name comes in as "Name (2)"), and the PC's own settings as they
    ///   were are kept as settings.db.bak. Without bring, the PC's own settings and notebooks are used as before.
    /// </summary>
    internal static class PortableSwitch
    {
        public const string OffName = Places.PortableName + " (off)";
        const string SettingsFile = "settings.db";

        /// <summary>Starts the NoteSpin that does the switch once this one has closed.</summary>
        public static void Start(bool portable, bool bring)
        {
            string args = $"--switch {(portable ? "on" : "off")}{(bring ? " --bring" : "")} --after {Process.GetCurrentProcess().Id}";
            Process.Start(new ProcessStartInfo(Application.ExecutablePath, args) { UseShellExecute = false });
        }

        /// <summary>Throws, saying why, when NoteSpin can't keep its data in its own folder.</summary>
        public static void CheckCanBePortable()
        {
            string probe = Path.Combine(Places.ExeDir, ".notespin-write-test");
            try
            {
                File.WriteAllText(probe, "");
                File.Delete(probe);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                throw new InvalidOperationException($"NoteSpin can't write in its own folder ({Places.ExeDir}), so it can't keep its data there. " +
                    "Copy the NoteSpin folder somewhere you can write to (a USB drive, say) and make that copy portable.");
            }
        }

        /// <summary>Does the switch, before anything opens. Returns what to tell the person when the page starts (null: nothing to switch).</summary>
        public static Dictionary<string, object> Run(bool portable, bool bring, int afterPid)
        {
            WaitForExit(afterPid);
            return portable ? On(bring) : Off(bring);
        }

        static void WaitForExit(int pid)
        {
            if (pid <= 0) return;
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    if (!string.Equals(p.ProcessName, Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase)) return;
                    if (!p.WaitForExit(30000)) throw new IOException("The NoteSpin that was closing is still running, so nothing was switched.");
                }
            }
            catch (ArgumentException) { }   // gone already
        }

        /* ---------- on ---------- */

        static Dictionary<string, object> On(bool bring)
        {
            string target = Places.PortableDir;
            if (Directory.Exists(target)) return null;
            // made under another name and renamed when it's complete, so a half-made one is never used
            string temp = target + ".partial";
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
            Directory.CreateDirectory(temp);
            var failed = new List<string>();
            int copied = 0;
            try
            {
                CopySettings(Path.Combine(Places.PcDataDir, SettingsFile), Path.Combine(temp, SettingsFile));
                using (var s = new Settings(temp))
                {
                    string from = ChosenFolder(s) ?? Places.PcNotebooksDir, last = s.GetPath(Library.LastKey);
                    s.Remove(Library.FolderKey);   // portable, the notebooks are in NoteSpin Data\Notebooks
                    s.Remove(Library.LastKey);
                    if (bring && Directory.Exists(from))
                    {
                        string dir = Path.Combine(temp, "Notebooks");
                        Directory.CreateDirectory(dir);
                        foreach (string f in Directory.GetFiles(from, "*" + Notebook.Ext))
                        {
                            string dest = Library.FreePathIn(dir, Path.GetFileNameWithoutExtension(f));
                            if (!CopyNotebook(f, dest)) { failed.Add(Path.GetFileNameWithoutExtension(f)); continue; }
                            copied++;
                            if (Same(f, last)) s.Set(Library.LastKey, Places.Pack(Path.Combine(target, "Notebooks", Path.GetFileName(dest)), true));
                        }
                    }
                    if (!bring) s.Remove("app:noteOpen");   // notes that were out: not in the new notebook
                }
                Directory.Move(temp, target);
            }
            catch
            {
                try { Directory.Delete(temp, true); } catch { }
                throw;
            }
            return Notice("on", target, copied, failed);
        }

        /* ---------- off ---------- */

        static Dictionary<string, object> Off(bool bring)
        {
            string src = Places.PortableDir;
            if (!Directory.Exists(src)) return null;
            string off = FreeDir(Path.Combine(Places.ExeDir, OffName));
            MoveWhenFree(src, off);
            var notice = Notice("off", null, 0, new List<string>());
            notice["off"] = off;
            if (!bring) return notice;

            string pcData = Places.PcDataDir, pcSettings = Path.Combine(pcData, SettingsFile);
            Directory.CreateDirectory(pcData);
            // this PC's notebooks folder: the one chosen on it before, else Documents\NoteSpin
            string pcNb = null;
            if (File.Exists(pcSettings))
                using (var old = new Settings(pcData)) pcNb = ChosenFolder(old);
            pcNb = pcNb ?? Places.PcNotebooksDir;
            notice["dir"] = pcNb;

            if (File.Exists(pcSettings)) KeepAsBak(pcSettings);
            CopySettings(Path.Combine(off, SettingsFile), pcSettings);
            var failed = (List<string>)notice["failed"];
            int copied = 0;
            using (var s = new Settings(pcData))
            {
                // the notebooks portable NoteSpin used: in the folder just renamed, unless another one was chosen
                string chosen = Rebase(s.GetPath(Library.FolderKey), src, off), last = Rebase(s.GetPath(Library.LastKey), src, off);
                string from = chosen != null && Directory.Exists(chosen) ? chosen : Path.Combine(off, "Notebooks");
                s.Remove(Library.LastKey);
                if (Same(pcNb, Places.PcNotebooksDir)) s.Remove(Library.FolderKey);
                else s.Set(Library.FolderKey, pcNb);
                string backups = Rebase(s.GetPath("app:backupDir"), src, off);
                if (backups != null) s.Set("app:backupDir", backups);
                Directory.CreateDirectory(pcNb);
                if (Directory.Exists(from))
                {
                    foreach (string f in Directory.GetFiles(from, "*" + Notebook.Ext))
                    {
                        string dest = Library.FreePathIn(pcNb, Path.GetFileNameWithoutExtension(f));
                        if (!CopyNotebook(f, dest)) { failed.Add(Path.GetFileNameWithoutExtension(f)); continue; }
                        copied++;
                        if (Same(f, last)) s.Set(Library.LastKey, dest);
                    }
                }
            }
            notice["copied"] = copied;
            return notice;
        }

        /* ---------- helpers ---------- */

        static Dictionary<string, object> Notice(string switched, string dir, int copied, List<string> failed) =>
            new Dictionary<string, object> { ["switched"] = switched, ["dir"] = dir, ["copied"] = copied, ["failed"] = failed };

        // The notebooks folder chosen in these settings, if it's there
        static string ChosenFolder(Settings s)
        {
            string d = s.GetPath(Library.FolderKey);
            return !string.IsNullOrEmpty(d) && Directory.Exists(d) ? Path.GetFullPath(d) : null;
        }

        static bool Same(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        // A path into a folder that has since been renamed (from → to), pointing into it under its new name
        static string Rebase(string path, string from, string to)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (Same(path, from)) return to;
            string prefix = from.TrimEnd('\\') + "\\";
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? Path.Combine(to, path.Substring(prefix.Length)) : path;
        }

        // settings.db, with its write-ahead log if it has one (NoteSpin was closed, so there's normally none)
        static void CopySettings(string src, string dest)
        {
            if (!File.Exists(src)) return;
            File.Copy(src, dest, true);
            if (File.Exists(src + "-wal")) File.Copy(src + "-wal", dest + "-wal", true);
        }

        // The PC's own settings.db, as it was, goes to settings.db.bak (replacing an older one)
        static void KeepAsBak(string db)
        {
            string bak = db + ".bak";
            foreach (string sfx in new[] { "", "-wal", "-shm" })
                if (File.Exists(bak + sfx)) File.Delete(bak + sfx);
            File.Move(db, bak);
            if (File.Exists(db + "-wal")) File.Move(db + "-wal", bak + "-wal");   // SQLite finds it by the name
            if (File.Exists(db + "-shm")) File.Delete(db + "-shm");
        }

        // Through SQLite, so the copy is complete and consistent; a file SQLite won't read is copied as it is
        static bool CopyNotebook(string src, string dest)
        {
            try { Notebook.CopyFile(src, dest); return true; }
            catch
            {
                try { if (File.Exists(dest)) File.Delete(dest); File.Copy(src, dest); return true; }
                catch { return false; }
            }
        }

        static string FreeDir(string dir)
        {
            for (int i = 1; ; i++)
            {
                string d = i == 1 ? dir : $"{dir.Substring(0, dir.Length - 1)} {i})";
                if (!Directory.Exists(d) && !File.Exists(d)) return d;
            }
        }

        // The web view's own processes may hold its files for a moment after NoteSpin closes
        static void MoveWhenFree(string from, string to)
        {
            var until = DateTime.UtcNow.AddSeconds(20);
            for (;;)
            {
                try { Directory.Move(from, to); return; }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    if (DateTime.UtcNow > until)
                        throw new IOException($"“{from}” is still in use, so NoteSpin stays portable. Close whatever has it open and try again.", ex);
                    Thread.Sleep(250);
                }
            }
        }
    }
}
