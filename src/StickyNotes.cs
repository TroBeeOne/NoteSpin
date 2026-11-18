using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NoteSpin
{
    /// <summary>
    /// Reads the notes in Microsoft Sticky Notes (the Windows app) so some can be copied into a notebook.
    /// Sticky Notes keeps them in a SQLite file of its own, plum.sqlite, with a -wal file beside it holding its
    /// latest changes. Those files are never opened: they're copied to a temporary folder, the copy is read,
    /// and the copy is deleted. So Sticky Notes is never changed, and it can be running meanwhile.
    /// Only the text comes across: pictures, handwriting, window places and sync details stay behind.
    /// </summary>
    internal static class StickyNotes
    {
        const string Package = "Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe";
        const string DbFile = "plum.sqlite", WalFile = "plum.sqlite-wal";
        const long TicksAtUnixEpoch = 621355968000000000;   // Sticky Notes' dates are .NET ticks (UTC)

        // every paragraph of a note starts with its id and a space: \id=<guid>, or \id=<word>_<hex> in some
        static readonly Regex ParagraphId = new Regex(@"^\\id=[0-9A-Za-z_-]+ ?", RegexOptions.Compiled);

        // NOTESPIN_STICKYNOTES=<folder with a plum.sqlite> stands in for Sticky Notes' own folder, for testing
        public static string Folder =>
            Environment.GetEnvironmentVariable("NOTESPIN_STICKYNOTES") is string test && test.Length > 0 ? test
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", Package, "LocalState");

        /// <summary>
        /// The notes with text in them, last edited first: [{sid, lines, theme, created, updated}] (dates in ms
        /// since 1970). Null when Sticky Notes isn't on this PC, or has never kept a note here.
        /// </summary>
        public static List<Dictionary<string, object>> Read()
        {
            if (!File.Exists(Path.Combine(Folder, DbFile))) return null;
            Exception last = null;
            // Sticky Notes may write while the files are copied; a copy caught half-way is made again
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try { return ReadCopy(); }
                catch (InvalidDataException) { throw; }
                catch (Exception ex) { last = ex; System.Threading.Thread.Sleep(300); }
            }
            throw new IOException("Sticky Notes' notes couldn't be read just now (" + last?.Message + "). Close Sticky Notes and try again.");
        }

        static List<Dictionary<string, object>> ReadCopy()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "NoteSpin-StickyNotes-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                // the -wal first: should Sticky Notes fold it into the main file in between, the copy of the
                // main file has those changes already, and the older -wal just repeats them. (The -shm index
                // isn't copied: SQLite builds a fresh one from the -wal, using only what's whole there.)
                string wal = Path.Combine(Folder, WalFile);
                if (File.Exists(wal)) CopyShared(wal, Path.Combine(tmp, WalFile));
                CopyShared(Path.Combine(Folder, DbFile), Path.Combine(tmp, DbFile));
                using (var db = new Sqlite(Path.Combine(tmp, DbFile)))
                {
                    string check = db.Scalar("PRAGMA quick_check") as string;
                    if (check != "ok") throw new IOException("the copy isn't whole: " + check);
                    return ReadNotes(db);
                }
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { /* in the temp folder, which Windows clears */ }
            }
        }

        // Sticky Notes keeps its files open: they're read the way it lets other programs share them
        static void CopyShared(string from, string to)
        {
            using (var i = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var o = new FileStream(to, FileMode.CreateNew, FileAccess.Write))
                i.CopyTo(o);
        }

        static List<Dictionary<string, object>> ReadNotes(Sqlite db)
        {
            var cols = new HashSet<string>(db.Query("PRAGMA table_info(Note)").Select(r => r[1] as string), StringComparer.OrdinalIgnoreCase);
            if (!cols.Contains("Id") || !cols.Contains("Text"))
                throw new InvalidDataException("This version of Sticky Notes keeps its notes in a way NoteSpin can't read.");
            string Col(string c) => cols.Contains(c) ? c : "NULL";
            var notes = new List<Dictionary<string, object>>();
            foreach (var r in db.Query($"SELECT Id, Text, {Col("Theme")}, {Col("CreatedAt")}, {Col("UpdatedAt")}, {Col("DeletedAt")} FROM Note"))
            {
                if (Ticks(r[5]) > 0) continue;   // in Sticky Notes' bin
                var lines = Lines(r[1] as string);
                if (!lines.Any(l => l.Trim().Length > 0)) continue;
                long created = ToMs(Ticks(r[3])), updated = ToMs(Ticks(r[4]));
                if (updated == 0) updated = created;
                notes.Add(new Dictionary<string, object>
                {
                    ["sid"] = r[0] as string ?? Convert.ToString(r[0]),
                    ["lines"] = lines,
                    ["theme"] = r[2] as string ?? "",
                    ["created"] = created == 0 ? updated : created,
                    ["updated"] = updated,
                });
            }
            return notes.OrderByDescending(n => (long)n["updated"]).ToList();
        }

        /// <summary>A note's text as lines: the paragraph ids taken off, and \\ (a backslash in the note) made \ again.</summary>
        internal static List<string> Lines(string text)
        {
            var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
                .Select(l => Unescape(ParagraphId.Replace(l, ""))).ToList();
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
            return lines;
        }

        static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var b = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length && s[i + 1] == '\\') i++;
                b.Append(s[i]);
            }
            return b.ToString();
        }

        static long Ticks(object v)
        {
            try { return v == null || v is DBNull ? 0 : Convert.ToInt64(v); } catch { return 0; }
        }

        static long ToMs(long ticks) => ticks > TicksAtUnixEpoch ? (ticks - TicksAtUnixEpoch) / 10000 : 0;
    }
}
