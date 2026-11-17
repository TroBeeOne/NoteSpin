using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NoteSpin
{
    /// <summary>
    /// One notebook: a single SQLite file with the extension .notespin.
    ///   categories  every note belongs to one; "General" (id "general") always exists
    ///   notes       title = the note's first line, body = the note (or, when locked, the encrypted note)
    ///   meta        small facts about the file
    /// The file is stamped with PRAGMA application_id, so NoteSpin knows its own files whatever they're named.
    /// It uses a rollback journal (not WAL), so a closed notebook is always one self-contained file,
    /// and secure_delete, so text replaced by encryption doesn't linger in the file's free space.
    /// </summary>
    internal sealed class Notebook : IDisposable
    {
        public const int AppId = 0x4E53504E;          // "NSPN"
        public const int SchemaVersion = 1;
        public const string Ext = ".notespin";
        public const string GeneralId = "general";
        public const string GeneralColor = "#FFF2A6";

        readonly Sqlite db;
        public string FilePath { get; }
        public string Name => Path.GetFileNameWithoutExtension(FilePath);

        public Notebook(string path)
        {
            FilePath = Path.GetFullPath(path);
            bool isNew = !File.Exists(FilePath) || new FileInfo(FilePath).Length == 0;
            db = new Sqlite(FilePath);
            try
            {
                if (!isNew) CheckStamp(db, FilePath);
                db.Query("PRAGMA journal_mode=DELETE");
                db.Exec("PRAGMA secure_delete=ON");
                db.Exec("PRAGMA foreign_keys=ON");
                long ver = Convert.ToInt64(db.Scalar("PRAGMA user_version"));
                if (ver == 0) CreateSchema();
                // later schema versions migrate here
            }
            catch
            {
                db.Dispose();
                throw;
            }
        }

        public void Dispose() => db.Dispose();

        static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        static string NewId() => Guid.NewGuid().ToString("N");

        /// <summary>Throws a readable error unless the open file is a NoteSpin notebook this version can read.</summary>
        static void CheckStamp(Sqlite s, string path)
        {
            long appId, ver;
            try
            {
                appId = Convert.ToInt64(s.Scalar("PRAGMA application_id"));
                ver = Convert.ToInt64(s.Scalar("PRAGMA user_version"));
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"“{Path.GetFileName(path)}” isn't a NoteSpin notebook ({ex.Message.Replace("Database error: ", "")}).");
            }
            if (appId != AppId) throw new InvalidDataException($"“{Path.GetFileName(path)}” isn't a NoteSpin notebook.");
            if (ver > SchemaVersion) throw new InvalidDataException($"“{Path.GetFileName(path)}” was made by a newer version of NoteSpin.");
        }

        void CreateSchema()
        {
            long now = Now();
            db.Transaction(() =>
            {
                db.Exec("CREATE TABLE meta(key TEXT PRIMARY KEY, value TEXT NOT NULL)");
                db.Exec(@"CREATE TABLE categories(
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    color TEXT NOT NULL,
                    created INTEGER NOT NULL)");
                db.Exec(@"CREATE TABLE notes(
                    id TEXT PRIMARY KEY,
                    category_id TEXT NOT NULL REFERENCES categories(id),
                    title TEXT NOT NULL,
                    body TEXT NOT NULL,
                    preview TEXT NOT NULL,
                    search TEXT NOT NULL,
                    locked INTEGER NOT NULL DEFAULT 0,
                    created INTEGER NOT NULL,
                    updated INTEGER NOT NULL)");
                db.Exec("CREATE INDEX notes_category ON notes(category_id)");
                db.Exec("INSERT INTO categories(id,name,color,created) VALUES(?,?,?,?)", GeneralId, "General", GeneralColor, now);
                db.Exec("INSERT INTO meta(key,value) VALUES('created',?)", now.ToString());
                db.Exec($"PRAGMA application_id={AppId}");
                db.Exec($"PRAGMA user_version={SchemaVersion}");
            });
        }

        /* ---------- reading ---------- */

        public Dictionary<string, object> Info() => new Dictionary<string, object>
        {
            ["name"] = Name,
            ["path"] = FilePath,
            ["notes"] = db.Scalar("SELECT COUNT(*) FROM notes"),
            ["categories"] = db.Scalar("SELECT COUNT(*) FROM categories"),
        };

        public List<object> ListCategories() =>
            db.Query("SELECT id, name, color, created FROM categories ORDER BY name COLLATE NOCASE")
                .Select(r => (object)new Dictionary<string, object> { ["id"] = r[0], ["name"] = r[1], ["color"] = r[2], ["created"] = r[3] })
                .ToList();

        /// <summary>Everything the page needs to draw the cards: all categories and every note without its body.</summary>
        public Dictionary<string, object> ListAll()
        {
            var cats = ListCategories();
            var notes = db.Query("SELECT id, category_id, title, preview, locked, created, updated FROM notes")
                .Select(r => (object)new Dictionary<string, object>
                {
                    ["id"] = r[0], ["cat"] = r[1], ["title"] = r[2], ["preview"] = r[3],
                    ["locked"] = Convert.ToInt64(r[4]) != 0, ["created"] = r[5], ["updated"] = r[6],
                })
                .ToList();
            return new Dictionary<string, object> { ["categories"] = cats, ["notes"] = notes };
        }

        public Dictionary<string, object> GetNote(string id)
        {
            var r = db.Query("SELECT id, category_id, title, body, locked, created, updated FROM notes WHERE id=?", id);
            if (r.Count == 0) return null;
            return new Dictionary<string, object>
            {
                ["id"] = r[0][0], ["cat"] = r[0][1], ["title"] = r[0][2], ["body"] = r[0][3],
                ["locked"] = Convert.ToInt64(r[0][4]) != 0, ["created"] = r[0][5], ["updated"] = r[0][6],
            };
        }

        /// <summary>Ids of the notes whose search text holds every term (the page sends them lower-cased).</summary>
        public List<string> Search(IEnumerable<string> terms)
        {
            var t = terms.Where(s => !string.IsNullOrWhiteSpace(s)).Take(12).ToArray();
            if (t.Length == 0) return new List<string>();
            string where = string.Join(" AND ", t.Select(_ => "instr(search, ?) > 0"));
            return db.Query("SELECT id FROM notes WHERE " + where, t.Cast<object>().ToArray()).Select(r => (string)r[0]).ToList();
        }

        /* ---------- notes ---------- */

        /// <summary>
        /// Creates or updates a note. The page sends the note already prepared: title (first line), body
        /// (the note, or the encrypted note when locked), preview (what the card shows; empty when locked) and
        /// search (lower-cased text to search; just the title when locked). The category is given by name and
        /// made if it doesn't exist yet, with the colour the page picked; a blank name means General.
        /// </summary>
        public Dictionary<string, object> SaveNote(Dictionary<string, object> n)
        {
            string id = Str(n, "id");
            string title = Str(n, "title") ?? "";
            string body = Str(n, "body") ?? "";
            string preview = Str(n, "preview") ?? "";
            string search = Str(n, "search") ?? "";
            bool locked = n.TryGetValue("locked", out object lo) && lo is bool b && b;
            string catName = (Str(n, "category") ?? "").Trim();
            string catColor = Str(n, "categoryColor");
            if (title.Length > 500) title = title.Substring(0, 500);

            var res = new Dictionary<string, object>();
            db.Transaction(() =>
            {
                string catId = GeneralId;
                if (catName.Length > 0)
                {
                    catId = db.Scalar("SELECT id FROM categories WHERE name=?", catName) as string;
                    if (catId == null)
                    {
                        catId = NewId();
                        db.Exec("INSERT INTO categories(id,name,color,created) VALUES(?,?,?,?)", catId, Clip(catName, 60), ValidColor(catColor), Now());
                        res["newCategory"] = catId;
                    }
                }
                long now = Now();
                var old = string.IsNullOrEmpty(id) ? null : db.Query("SELECT category_id, title, body, locked, created, updated FROM notes WHERE id=?", id);
                if (old == null || old.Count == 0)
                {
                    if (string.IsNullOrEmpty(id)) id = NewId();
                    db.Exec("INSERT INTO notes(id,category_id,title,body,preview,search,locked,created,updated) VALUES(?,?,?,?,?,?,?,?,?)",
                        id, catId, title, body, preview, search, locked, now, now);
                    res["created"] = now;
                    res["updated"] = now;
                }
                else
                {
                    var o = old[0];
                    bool changed = (string)o[0] != catId || (string)o[1] != title || (string)o[2] != body || (Convert.ToInt64(o[3]) != 0) != locked;
                    long updated = changed ? now : Convert.ToInt64(o[5]);
                    db.Exec("UPDATE notes SET category_id=?, title=?, body=?, preview=?, search=?, locked=?, updated=? WHERE id=?",
                        catId, title, body, preview, search, locked, updated, id);
                    res["created"] = o[4];
                    res["updated"] = updated;
                }
                res["id"] = id;
                res["cat"] = catId;
            });
            return res;
        }

        public void DeleteNote(string id) => db.Exec("DELETE FROM notes WHERE id=?", id);

        /* ---------- categories ---------- */

        public Dictionary<string, object> SaveCategory(string id, string name, string color)
        {
            name = Clip((name ?? "").Trim(), 60);
            if (id == GeneralId) name = "General";   // General can change colour, not name
            if (name.Length == 0) throw new InvalidOperationException("A category needs a name.");
            var clash = db.Scalar("SELECT id FROM categories WHERE name=? AND id<>?", name, id ?? "") as string;
            if (clash != null) throw new InvalidOperationException($"There's already a category called “{name}”.");
            if (string.IsNullOrEmpty(id) || db.Scalar("SELECT 1 FROM categories WHERE id=?", id) == null)
            {
                if (string.IsNullOrEmpty(id)) id = NewId();
                db.Exec("INSERT INTO categories(id,name,color,created) VALUES(?,?,?,?)", id, name, ValidColor(color), Now());
            }
            else db.Exec("UPDATE categories SET name=?, color=? WHERE id=?", name, ValidColor(color), id);
            return new Dictionary<string, object> { ["id"] = id, ["name"] = name, ["color"] = ValidColor(color) };
        }

        /// <summary>Removes a category; its notes move to General.</summary>
        public void DeleteCategory(string id)
        {
            if (id == GeneralId) throw new InvalidOperationException("General can't be deleted.");
            db.Transaction(() =>
            {
                db.Exec("UPDATE notes SET category_id=? WHERE category_id=?", GeneralId, id);
                db.Exec("DELETE FROM categories WHERE id=?", id);
            });
        }

        /* ---------- copies ---------- */

        /// <summary>Writes a compact, consistent copy of the notebook to dest (which must not exist yet).</summary>
        public void CopyTo(string dest) => db.Exec("VACUUM INTO ?", dest);

        /// <summary>
        /// Checks a file before it's imported: it must be a NoteSpin notebook this version can read, and undamaged.
        /// Opens it read-only, so the file itself is never changed.
        /// </summary>
        public static Dictionary<string, object> Inspect(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException($"“{Path.GetFileName(path)}” wasn't found.");
            using (var s = new Sqlite(path, readOnly: true))
            {
                CheckStamp(s, path);
                string check = s.Scalar("PRAGMA quick_check") as string;
                if (check != "ok") throw new InvalidDataException($"“{Path.GetFileName(path)}” is damaged ({check}).");
                return new Dictionary<string, object>
                {
                    ["name"] = Path.GetFileNameWithoutExtension(path),
                    ["path"] = Path.GetFullPath(path),
                    ["notes"] = s.Scalar("SELECT COUNT(*) FROM notes"),
                    ["locked"] = s.Scalar("SELECT COUNT(*) FROM notes WHERE locked<>0"),
                    ["categories"] = s.Scalar("SELECT COUNT(*) FROM categories"),
                    ["modified"] = new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeMilliseconds(),
                };
            }
        }

        /// <summary>Copies a notebook file (checked with Inspect first) to dest through SQLite, so the copy is complete and compact.</summary>
        public static void CopyFile(string src, string dest)
        {
            using (var s = new Sqlite(src, readOnly: true)) s.Exec("VACUUM INTO ?", dest);
        }

        /* ---------- helpers ---------- */

        static string Str(Dictionary<string, object> d, string key) => d.TryGetValue(key, out object v) ? v as string : null;

        static string Clip(string s, int max) => s.Length > max ? s.Substring(0, max) : s;

        static string ValidColor(string c) =>
            c != null && c.Length == 7 && c[0] == '#' && c.Skip(1).All(Uri.IsHexDigit) ? c.ToUpperInvariant() : GeneralColor;
    }
}
