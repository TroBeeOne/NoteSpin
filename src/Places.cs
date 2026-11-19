using System;
using System.IO;
using System.Linq;

namespace NoteSpin
{
    /// <summary>
    /// Where NoteSpin keeps its settings, its web view's cache and (unless another folder is chosen) the notebooks.
    /// Normally on this PC: %LOCALAPPDATA%\NoteSpin and Documents\NoteSpin. Portable, when a "NoteSpin Data"
    /// folder is beside NoteSpin.exe: everything in that folder, so the NoteSpin folder can go on a USB drive and
    /// run on any PC, notes and all (see PortableSwitch for turning it on and off).
    /// In tests NOTESPIN_DATA and NOTESPIN_NOTEBOOKS stand in for the two folders on this PC.
    /// </summary>
    internal static class Places
    {
        public const string PortableName = "NoteSpin Data";

        /// <summary>The folder NoteSpin.exe is in (with no \ at the end, unless it's a drive's top folder, E:\).</summary>
        public static string ExeDir { get; } = NoEndSlash(Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory));

        static string NoEndSlash(string dir) => dir.Length > 3 ? dir.TrimEnd('\\') : dir;

        public static string PortableDir => Path.Combine(ExeDir, PortableName);

        /// <summary>Whether this run of NoteSpin is portable (decided once, as it starts: see Decide).</summary>
        public static bool Portable { get; private set; }

        public static void Decide() => Portable = Directory.Exists(PortableDir);

        static string Env(string name)
        {
            string v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }

        /// <summary>This PC's folder for NoteSpin's settings and web view: %LOCALAPPDATA%\NoteSpin.</summary>
        public static string PcDataDir =>
            Env("NOTESPIN_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoteSpin");

        /// <summary>
        /// This PC's notebooks folder unless another is chosen: Documents\NoteSpin, which OneDrive backs up when it
        /// backs up Documents. A test data folder keeps its own notebooks (NOTESPIN_NOTEBOOKS can stand in instead).
        /// </summary>
        public static string PcNotebooksDir
        {
            get
            {
                string nb = Env("NOTESPIN_NOTEBOOKS");
                if (nb != null) return nb;
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                return Env("NOTESPIN_DATA") == null && !string.IsNullOrEmpty(docs)
                    ? Path.Combine(docs, "NoteSpin")
                    : Path.Combine(PcDataDir, "Notebooks");
            }
        }

        public static string DataDir => Portable ? PortableDir : PcDataDir;
        public static string NotebooksDir => Portable ? Path.Combine(PortableDir, "Notebooks") : PcNotebooksDir;

        /* ---------- paths kept in the settings ----------
           A USB drive is E: on one PC and F: on the next, so portable NoteSpin keeps a path on its own drive
           relative to its own folder (".\NoteSpin Data\Notebooks\My Notes.notespin", "..\Notes"). Other paths, and
           every path when it isn't portable, are kept whole. A relative one always reads back against ExeDir. */

        /// <summary>A path as the settings keep it (portable: whether to keep it relative, if it's on NoteSpin's drive).</summary>
        public static string Pack(string path, bool portable)
        {
            if (string.IsNullOrEmpty(path) || !portable) return path;
            try { return Relative(ExeDir, Path.GetFullPath(path)) ?? path; }
            catch { return path; }
        }

        /// <summary>A path the settings keep, as a full path again.</summary>
        public static string Unpack(string value)
        {
            if (string.IsNullOrEmpty(value) || !IsRelative(value)) return value;
            try { return Path.GetFullPath(Path.Combine(ExeDir, value)); }
            catch { return value; }
        }

        static bool IsRelative(string v) => v == "." || v.StartsWith(".\\") || v == ".." || v.StartsWith("..\\");

        // to, relative to the folder from (".\…" or "..\…"); null when they're on different drives
        static string Relative(string from, string to)
        {
            if (!string.Equals(Path.GetPathRoot(from).TrimEnd('\\'), Path.GetPathRoot(to).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return null;
            string[] a = from.TrimEnd('\\').Split('\\'), b = to.TrimEnd('\\').Split('\\');
            int same = 0;
            while (same < a.Length && same < b.Length && string.Equals(a[same], b[same], StringComparison.OrdinalIgnoreCase)) same++;
            if (same == 0) return null;
            string rel = string.Join("\\", Enumerable.Repeat("..", a.Length - same).Concat(b.Skip(same)));
            return rel.Length == 0 ? "." : rel.StartsWith("..") ? rel : ".\\" + rel;
        }
    }
}
