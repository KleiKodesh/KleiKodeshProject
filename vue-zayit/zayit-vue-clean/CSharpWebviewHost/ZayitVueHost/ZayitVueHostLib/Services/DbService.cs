using Dapper;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace Zayit.Services
{
    /// <summary>
    /// All database access. One method: ExecuteQuery.
    /// DB path is persisted in the registry via VB Interaction settings.
    /// </summary>
    public static class DbService
    {
        private const string VB_APP = "ZayitApp";
        private const string VB_SECTION = "Database";
        private const string VB_KEY_PATH = "DbPath";

        // ── Path management ──────────────────────────────────────────────────

        public static string DbPath
        {
            get
            {
                var saved = Interaction.GetSetting(VB_APP, VB_SECTION, VB_KEY_PATH, "");
                if (!string.IsNullOrEmpty(saved) && File.Exists(saved))
                    return saved;
                return FindDefaultDbPath();
            }
            set
            {
                Interaction.SaveSetting(VB_APP, VB_SECTION, VB_KEY_PATH, value ?? "");
            }
        }

        private static string FindDefaultDbPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (var candidate in new[]
            {
                Path.Combine(baseDir, "zayit.db"),
                Path.Combine(baseDir, "data", "zayit.db"),
                Path.Combine(baseDir, "zayit-vue-app", "zayit.db"),
            })
                if (File.Exists(candidate)) return candidate;

            return Path.Combine(baseDir, "zayit.db"); // fallback (may not exist)
        }

        public static bool IsAvailable() => File.Exists(DbPath);

        // ── Query execution ──────────────────────────────────────────────────

        /// <summary>
        /// Execute any SQL and return rows as an array of dynamic objects.
        /// Pass parameters as an anonymous object, e.g. new { id = 5 }.
        /// </summary>
        public static object[] Query(string sql, object param = null)
        {
            using (var conn = Open())
                return conn.Query<dynamic>(sql, param).AsList().ToArray();
        }

        // ── Bloom indexer helpers (used by BloomFilterIndexer / Searcher) ────

        public static int GetLineCount()
        {
            using (var conn = Open())
                return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM line");
        }

        public static IEnumerable<string> GetAllLineContents()
        {
            using (var conn = Open())
                foreach (var s in conn.Query<string>("SELECT content FROM line ORDER BY id"))
                    yield return s;
        }

        public static IEnumerable<string> GetLineContentsChunk(int chunkIndex, int chunkSize)
        {
            using (var conn = Open())
                foreach (var s in conn.Query<string>(
                    "SELECT content FROM line ORDER BY id LIMIT @limit OFFSET @offset",
                    new { limit = chunkSize, offset = chunkIndex * chunkSize }))
                    yield return s;
        }

        public static (int bookId, string bookTitle, string tocText) GetLineMetadata(int lineId)
        {
            using (var conn = Open())
            {
                var row = conn.QueryFirstOrDefault<dynamic>(
                    @"SELECT l.bookId, b.title AS bookTitle, t.text AS tocText
                      FROM line l
                      LEFT JOIN book b ON b.id = l.bookId
                      LEFT JOIN tocEntry t ON t.id = (
                          SELECT te.id FROM tocEntry te
                          WHERE te.bookId = l.bookId AND te.startLineId <= l.id
                          ORDER BY te.startLineId DESC LIMIT 1)
                      WHERE l.id = @id", new { id = lineId });
                if (row == null) return (0, "", "");
                return ((int)row.bookId, (string)(row.bookTitle ?? ""), (string)(row.tocText ?? ""));
            }
        }

        public static string GetLineContent(int lineId)
        {
            using (var conn = Open())
                return conn.ExecuteScalar<string>("SELECT content FROM line WHERE id = @id", new { id = lineId }) ?? "";
        }

        public static (int lineIndex, int bookId) GetLineIndexFromLineId(int lineId)
        {
            using (var conn = Open())
            {
                var row = conn.QueryFirstOrDefault<dynamic>(
                    "SELECT id, bookId FROM line WHERE id = @id", new { id = lineId });
                return row == null ? (-1, -1) : ((int)row.id, (int)row.bookId);
            }
        }

        // ── Private ──────────────────────────────────────────────────────────

        private static SQLiteConnection Open()
        {
            var conn = new SQLiteConnection($"Data Source={DbPath};Version=3;Read Only=True;");
            conn.Open();
            return conn;
        }
    }
}
