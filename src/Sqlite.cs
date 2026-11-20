using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace NoteSpin
{
    /// <summary>
    /// Minimal wrapper over the system SQLite engine (winsqlite3.dll on Windows, libsqlite3.so.0 on Linux),
    /// so the app carries no database library of its own. Used from the UI thread only.
    /// </summary>
    internal sealed class Sqlite : IDisposable
    {
#if LINUX
        const string Lib = "libsqlite3.so.0";
#else
        const string Lib = "winsqlite3.dll";
#endif
        const int SQLITE_OK = 0, SQLITE_ROW = 100, SQLITE_DONE = 101;
        const int SQLITE_INTEGER = 1, SQLITE_NULL = 5;
        const int OPEN_READONLY = 0x1, OPEN_READWRITE = 0x2, OPEN_CREATE = 0x4, OPEN_FULLMUTEX = 0x10000;
        static readonly IntPtr Transient = new IntPtr(-1);   // SQLITE_TRANSIENT: SQLite copies bound values

        [DllImport(Lib)] static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(Lib)] static extern int sqlite3_close_v2(IntPtr db);
        [DllImport(Lib)] static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport(Lib)] static extern IntPtr sqlite3_libversion();
        [DllImport(Lib)] static extern int sqlite3_busy_timeout(IntPtr db, int ms);
        [DllImport(Lib)] static extern int sqlite3_changes(IntPtr db);
        [DllImport(Lib)] static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int nByte, out IntPtr stmt, IntPtr tail);
        [DllImport(Lib)] static extern int sqlite3_step(IntPtr stmt);
        [DllImport(Lib)] static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport(Lib)] static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] value, int nByte, IntPtr destructor);
        [DllImport(Lib)] static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);
        [DllImport(Lib)] static extern int sqlite3_bind_null(IntPtr stmt, int index);
        [DllImport(Lib)] static extern int sqlite3_column_count(IntPtr stmt);
        [DllImport(Lib)] static extern int sqlite3_column_type(IntPtr stmt, int col);
        [DllImport(Lib)] static extern long sqlite3_column_int64(IntPtr stmt, int col);
        [DllImport(Lib)] static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);
        [DllImport(Lib)] static extern int sqlite3_column_bytes(IntPtr stmt, int col);

        IntPtr db;

        /// <param name="readOnly">Open an existing file without changing it (used to check a file before importing it).</param>
        public Sqlite(string path, bool readOnly = false)
        {
            int flags = (readOnly ? OPEN_READONLY : OPEN_READWRITE | OPEN_CREATE) | OPEN_FULLMUTEX;
            int rc = sqlite3_open_v2(Utf8Z(path), out db, flags, IntPtr.Zero);
            if (rc != SQLITE_OK)
            {
                string msg = db != IntPtr.Zero ? Error() : "error code " + rc;
                Dispose();
                throw new InvalidOperationException(msg);
            }
            sqlite3_busy_timeout(db, 5000);   // a second window may be writing at the same moment
        }

        public static string Version => FromUtf8(sqlite3_libversion(), -1);

        public int Exec(string sql, params object[] args)
        {
            IntPtr st = Prepare(sql, args);
            try
            {
                int rc;
                while ((rc = sqlite3_step(st)) == SQLITE_ROW) { }
                if (rc != SQLITE_DONE) throw Fail();
                return sqlite3_changes(db);
            }
            finally { sqlite3_finalize(st); }
        }

        public List<object[]> Query(string sql, params object[] args)
        {
            IntPtr st = Prepare(sql, args);
            try
            {
                var rows = new List<object[]>();
                int n = sqlite3_column_count(st), rc;
                while ((rc = sqlite3_step(st)) == SQLITE_ROW)
                {
                    var row = new object[n];
                    for (int i = 0; i < n; i++)
                    {
                        switch (sqlite3_column_type(st, i))
                        {
                            case SQLITE_INTEGER: row[i] = sqlite3_column_int64(st, i); break;
                            case SQLITE_NULL: row[i] = null; break;
                            default: row[i] = FromUtf8(sqlite3_column_text(st, i), sqlite3_column_bytes(st, i)) ?? ""; break;
                        }
                    }
                    rows.Add(row);
                }
                if (rc != SQLITE_DONE) throw Fail();
                return rows;
            }
            finally { sqlite3_finalize(st); }
        }

        public object Scalar(string sql, params object[] args)
        {
            var rows = Query(sql, args);
            return rows.Count > 0 ? rows[0][0] : null;
        }

        public void Transaction(Action body)
        {
            Exec("BEGIN IMMEDIATE");
            try { body(); Exec("COMMIT"); }
            catch
            {
                try { Exec("ROLLBACK"); } catch { }
                throw;
            }
        }

        public void Dispose()
        {
            if (db != IntPtr.Zero) { sqlite3_close_v2(db); db = IntPtr.Zero; }
        }

        IntPtr Prepare(string sql, object[] args)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(sql);
            if (sqlite3_prepare_v2(db, bytes, bytes.Length, out IntPtr st, IntPtr.Zero) != SQLITE_OK) throw Fail();
            for (int i = 0; i < args.Length; i++)
            {
                int rc;
                switch (args[i])
                {
                    case null: rc = sqlite3_bind_null(st, i + 1); break;
                    case string s:
                        byte[] b = Encoding.UTF8.GetBytes(s);
                        rc = sqlite3_bind_text(st, i + 1, b, b.Length, Transient);
                        break;
                    case long l: rc = sqlite3_bind_int64(st, i + 1, l); break;
                    case int n: rc = sqlite3_bind_int64(st, i + 1, n); break;
                    case bool f: rc = sqlite3_bind_int64(st, i + 1, f ? 1 : 0); break;
                    default:
                        sqlite3_finalize(st);
                        throw new ArgumentException("Unsupported parameter type " + args[i].GetType().Name);
                }
                if (rc != SQLITE_OK)
                {
                    var ex = Fail();
                    sqlite3_finalize(st);
                    throw ex;
                }
            }
            return st;
        }

        Exception Fail() => new InvalidOperationException("Database error: " + Error());

        string Error() => FromUtf8(sqlite3_errmsg(db), -1);

        static byte[] Utf8Z(string s)
        {
            byte[] b = Encoding.UTF8.GetBytes(s);
            Array.Resize(ref b, b.Length + 1);
            return b;
        }

        static string FromUtf8(IntPtr p, int len)
        {
            if (p == IntPtr.Zero) return null;
            if (len < 0) { len = 0; while (Marshal.ReadByte(p, len) != 0) len++; }
            var buf = new byte[len];
            Marshal.Copy(p, buf, 0, len);
            return Encoding.UTF8.GetString(buf);
        }
    }
}
