using System;
using System.Collections.Generic;
using System.IO;

namespace NoteSpin
{
    /// <summary>
    /// The app's own settings (window placement, the notebook last used, page preferences), in settings.db in
    /// the data folder: %LOCALAPPDATA%\NoteSpin, or NoteSpin Data when portable (see Places). Notes are never
    /// kept here: they live in the notebook files.
    /// </summary>
    internal sealed class Settings : IDisposable
    {
        readonly Sqlite db;

        public Settings(string dataDir)
        {
            db = new Sqlite(Path.Combine(dataDir, "settings.db"));
            db.Query("PRAGMA journal_mode=WAL");
            db.Exec("CREATE TABLE IF NOT EXISTS kv(key TEXT PRIMARY KEY, value TEXT NOT NULL)");
        }

        public string Get(string key) => db.Scalar("SELECT value FROM kv WHERE key=?", key) as string;

        public void Set(string key, string value) =>
            db.Exec("INSERT INTO kv(key,value) VALUES(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value", key, value ?? "");

        public void Remove(string key) => db.Exec("DELETE FROM kv WHERE key=?", key);

        public Dictionary<string, string> GetAllPrefs()
        {
            var map = new Dictionary<string, string>();
            foreach (var row in db.Query("SELECT key, value FROM kv WHERE substr(key,1,5)='pref:'"))
            {
                if (row[0] is string k && k.Length > 5) map[k.Substring(5)] = row[1] as string;
            }
            return map;
        }

        /// <summary>A file or folder kept in the settings. Portable, one on NoteSpin's own drive is kept relative to its folder (see Places).</summary>
        public string GetPath(string key) => Places.Unpack(Get(key));

        public void SetPath(string key, string path) => Set(key, Places.Pack(path, Places.Portable));

        public void Dispose() => db.Dispose();
    }
}
