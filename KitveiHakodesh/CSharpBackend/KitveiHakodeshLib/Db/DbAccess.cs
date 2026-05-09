using Dapper;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text.RegularExpressions;

namespace KitveiHakodeshLib.Db
{
    /// <summary>
    /// Thin wrapper around SQLite. Converts positional ? params to named @p0, @p1, ...
    /// because Dapper requires named parameters.
    /// Keeps a single open connection for the lifetime of the instance — the DB is
    /// read-only so there is no reason to open and close a connection per query.
    /// </summary>
    public class DbAccess : IDisposable
    {
        private readonly SQLiteConnection _conn;

        public DbAccess(string path)
        {
            string connectionString = "Data Source=" + path + ";Version=3;Read Only=True;";
            _conn = new SQLiteConnection(connectionString);
            _conn.Open();
            // Increase page cache to 64MB (default is ~2MB) — reduces cold-read latency
            // significantly for large text content in the line table.
            _conn.Execute("PRAGMA cache_size = -65536");  // negative = kibibytes → 64MB
            // Enable memory-mapped I/O up to 256MB — lets the OS serve reads directly
            // from mapped memory instead of going through read() syscalls.
            _conn.Execute("PRAGMA mmap_size = 268435456"); // 256MB
        }

        public IEnumerable<IDictionary<string, object>> Query(string sql, object[] parameters)
        {
            int index = 0;
            string namedSql = Regex.Replace(sql, @"\?", _ => "@p" + index++);

            var dp = new DynamicParameters();
            for (int i = 0; i < parameters.Length; i++)
                dp.Add("@p" + i, parameters[i]);

            return _conn.Query(namedSql, dp)
                        .Cast<IDictionary<string, object>>()
                        .ToList();
        }

        public void Dispose()
        {
            _conn.Close();
            _conn.Dispose();
        }
    }
}
