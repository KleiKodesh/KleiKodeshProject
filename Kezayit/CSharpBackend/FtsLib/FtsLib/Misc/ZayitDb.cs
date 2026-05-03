using Microsoft.SqlServer.Server;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace FtsLib.Misc
{
    public class ZayitDb : IDisposable
    {
        SQLiteConnection _connection;
        public ZayitDb(string dbPath)
        {
            OpenConnection(ResolveDbPath(dbPath));
        }

        private void OpenConnection(string dbPath)
        {
            if (dbPath == null)
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string defaultPath = Path.Combine(appData, "io.github.kdroidfilter.seforimapp", "databases", "seforim.db");
                dbPath = Interaction.GetSetting("ZayitApp", "Database", "Path", defaultPath);
            }
            Console.WriteLine("[ZayitDbManager] Opening DB at: " + dbPath + " exists=" + File.Exists(dbPath));
            if (!File.Exists(dbPath)) return;

            _connection = new SQLiteConnection($"Data Source={dbPath};Version=3;Page Size=4096;");
            _connection.Open();

            try
            {
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText =
                        "PRAGMA journal_mode=WAL; " +
                        "PRAGMA cache_size=-65536; " +   // up to 64 MB page cache (ceiling, not reservation)
                        "PRAGMA temp_store=MEMORY;";     // temp indices/tables in RAM, not disk
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex) { Console.WriteLine("[ZayitDbManager] PRAGMA setup failed: " + ex.Message); }
            //var conn = new SQLiteConnection(
            //    $"Data Source={dbPath};Version=3;Read Only=True;Page Size=4096;Cache Size=100000;Temp Store=Memory;");
            //conn.Open();
            //using (var cmd = conn.CreateCommand())
            //{
            //    cmd.CommandText = "PRAGMA mmap_size=2147483648; PRAGMA cache_size=100000; PRAGMA temp_store=MEMORY;";
            //    cmd.ExecuteNonQuery();
            //}
            //return conn;
        }

        string ResolveDbPath(string dbPath)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string def = Path.Combine(appData, "io.github.kdroidfilter.seforimapp", "databases", "seforim.db");
            return Interaction.GetSetting("ZayitApp", "Database", "Path", string.IsNullOrEmpty(dbPath) ? def : dbPath);
        }

        public long CountLines()
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM line";
                return (long)cmd.ExecuteScalar();
            }
        }

        public IEnumerable<(int Id, string Content)> ReadLines(int limit)
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = limit > 0
                    ? $"SELECT id, content FROM line ORDER BY id LIMIT {limit}"
                    : "SELECT id, content FROM line ORDER BY id";

                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        yield return (r.GetInt32(0), r.IsDBNull(1) ? string.Empty : r.GetString(1));
            }
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }
    }
}
