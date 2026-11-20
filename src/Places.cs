using System;
using System.IO;
using System.Linq;

namespace NoteSpin
{
    /// <summary>
    /// Where NoteSpin keeps its settings, its web view's cache and (unless another folder is chosen) the notebooks.
    /// Normally on this PC: %LOCALAPPDATA%\NoteSpin (or ~/.local/share/NoteSpin on Linux) and Documents/NoteSpin.
    /// Portable, when a "NoteSpin Data" folder is beside the executable: everything in that folder, so the NoteSpin
    /// folder can go on a USB drive and run on any PC, notes and all (see PortableSwitch for turning it on and off).
    /// In tests NOTESPIN_DATA and NOTESPIN_NOTEBOOKS stand in for the two folders on this PC.
    /// </summary>
    internal static class Places
    {
        public const string PortableName = "NoteSpin Data";
#if LINUX
        public static readonly StringComparison PathComparison = StringComparison.Ordinal;
        public static readonly StringComparer PathComparer = StringComparer.Ordinal;
#else
        public static readonly StringComparison PathComparison = StringComparison.OrdinalIgnoreCase;
        public static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
#endif

        /// <summary>The folder the executable is in (with no trailing separator, unless it's a root like E:\ or /).</summary>
        public static string ExeDir { get; } = NoEndSlash(Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory));

        internal static string NoEndSlash(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return dir;
            if (dir == "/" || (dir.Length == 3 && dir[1] == ':' && (dir[2] == '\\' || dir[2] == '/'))) return dir;
            string trimmed = dir.TrimEnd('\\', '/');
            return trimmed.Length == 0 ? "/" : trimmed;
        }

        public static string PortableDir => Path.Combine(ExeDir, PortableName);

        /// <summary>Whether this run of NoteSpin is portable (decided once, as it starts: see Decide).</summary>
        public static bool Portable { get; private set; }

        public static void Decide() => Portable = Directory.Exists(PortableDir);

        static string Env(string name)
        {
            string v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }

        /// <summary>This PC's folder for NoteSpin's settings and web view: %LOCALAPPDATA%\NoteSpin or ~/.local/share/NoteSpin.</summary>
        public static string PcDataDir =>
            Env("NOTESPIN_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoteSpin");

        /// <summary>
        /// This PC's notebooks folder unless another is chosen: Documents/NoteSpin, which OneDrive backs up when it
        /// backs up Documents on Windows. A test data folder keeps its own notebooks (NOTESPIN_NOTEBOOKS can stand in instead).
        /// </summary>
        public static string PcNotebooksDir
        {
            get
            {
                string nb = Env("NOTESPIN_NOTEBOOKS");
                if (nb != null) return nb;
                if (Env("NOTESPIN_DATA") != null) return Path.Combine(PcDataDir, "Notebooks");
                string docs = DocumentsDir();
                return !string.IsNullOrEmpty(docs)
                    ? Path.Combine(docs, "NoteSpin")
                    : Path.Combine(PcDataDir, "Notebooks");
            }
        }

        internal static string DocumentsDir()
        {
#if LINUX
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            // Parse XDG_DOCUMENTS_DIR from ~/.config/user-dirs.dirs when present
            try
            {
                string cfgHome = Env("XDG_CONFIG_HOME") ?? (string.IsNullOrEmpty(home) ? null : Path.Combine(home, ".config"));
                string userDirs = string.IsNullOrEmpty(cfgHome) ? null : Path.Combine(cfgHome, "user-dirs.dirs");
                if (userDirs != null && File.Exists(userDirs))
                {
                    foreach (string raw in File.ReadAllLines(userDirs))
                    {
                        string line = raw.Trim();
                        if (!line.StartsWith("XDG_DOCUMENTS_DIR=", StringComparison.Ordinal)) continue;
                        string val = line.Substring("XDG_DOCUMENTS_DIR=".Length).Trim().Trim('"');
                        if (val.StartsWith("$HOME/", StringComparison.Ordinal) && !string.IsNullOrEmpty(home))
                            val = Path.Combine(home, val.Substring("$HOME/".Length));
                        else if (val == "$HOME" && !string.IsNullOrEmpty(home))
                            val = home;
                        if (!string.IsNullOrEmpty(val)) return val;
                    }
                }
            }
            catch { }
            return string.IsNullOrEmpty(home) ? null : Path.Combine(home, "Documents");
#else
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
#endif
        }

        public static string DataDir => Portable ? PortableDir : PcDataDir;
        public static string NotebooksDir => Portable ? Path.Combine(PortableDir, "Notebooks") : PcNotebooksDir;

        /* ---------- paths kept in the settings ----------
           A USB drive is E: on one PC and F: on the next (or /run/media/user/USB on Linux), so portable NoteSpin
           keeps a path on its own drive relative to its own folder ("./NoteSpin Data/Notebooks/My Notes.notespin",
           "../Notes"). Other paths, and every path when it isn't portable, are kept whole. A relative one always
           reads back against ExeDir, accepting both \ and / so portable folders travel between Windows and Linux. */

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
            try
            {
                string norm = value.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                return Path.GetFullPath(Path.Combine(ExeDir, norm));
            }
            catch { return value; }
        }

        static bool IsRelative(string v) =>
            v == "." || v.StartsWith(".\\") || v.StartsWith("./") || v == ".." || v.StartsWith("..\\") || v.StartsWith("../");

        // to, relative to the folder from (".\…" or "./…" or "../…"); null when they're on different drives
        static string Relative(string from, string to)
        {
            char sep = Path.DirectorySeparatorChar;
            string rootFrom = (Path.GetPathRoot(from) ?? "").TrimEnd('\\', '/');
            string rootTo = (Path.GetPathRoot(to) ?? "").TrimEnd('\\', '/');
            if (!string.Equals(rootFrom, rootTo, PathComparison)) return null;
            string[] a = from.TrimEnd('\\', '/').Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            string[] b = to.TrimEnd('\\', '/').Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            int same = 0;
            while (same < a.Length && same < b.Length && string.Equals(a[same], b[same], PathComparison)) same++;
#if !LINUX
            if (same == 0) return null;
#endif
            string rel = string.Join(sep.ToString(), Enumerable.Repeat("..", a.Length - same).Concat(b.Skip(same)));
            return rel.Length == 0 ? "." : rel.StartsWith("..") ? rel : "." + sep + rel;
        }
    }
}
