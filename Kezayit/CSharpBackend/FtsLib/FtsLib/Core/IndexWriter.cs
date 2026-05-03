using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Reflection;

namespace FtsLib.Core
{
    /// <summary>
    /// Persists a built RamIndex to two files:
    ///   postings.bin  — flat binary file, all posting byte-arrays concatenated
    ///   index.db      — SQLite DB: term → (offset, length, count) into postings.bin
    ///
    /// On each call to Write(), both files are deleted and recreated from scratch.
    ///
    /// Speed optimisations:
    ///   - WAL journal + synchronous=OFF + large page/cache
    ///   - Plain rowid table during bulk insert (no B-tree rebalancing per row)
    ///   - Single transaction for all inserts
    ///   - Index built AFTER all rows are inserted
    ///   - postings.bin written with a 4 MB OS buffer
    /// </summary>
    public sealed class IndexWriter : IndexPaths, IDisposable
    {
        private readonly RamIndex _ramIndex;

        public IndexWriter(string indexPath, bool useSkipList = true) : base(indexPath)
        {
            _ramIndex = new RamIndex(useSkipList: useSkipList);
        }

        public void Add(int lineId, string term)
        {
            _ramIndex.Add(term, lineId);
        }


        public void Dispose()
        {
            Commit();
        }

        void Commit()
        {
            // ---- delete existing files ----
            if (File.Exists(PostingsPath)) File.Delete(PostingsPath);
            if (File.Exists(MetaDbPath))  File.Delete(MetaDbPath);

            // ---- write postings.bin + collect metadata ----
            using (var postings = new FileStream(PostingsPath, FileMode.Create,
                                                 FileAccess.Write, FileShare.None,
                                                 bufferSize: 4 * 1024 * 1024))
            {
                var connStr =
                    $"Data Source={MetaDbPath};Version=3;" +
                    $"Page Size=65536;Cache Size=8000;";   // 64 KB pages, ~500 MB cache

                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();

                    // Fastest possible SQLite write settings
                    Exec(conn,
                        "PRAGMA journal_mode=WAL;" +
                        "PRAGMA synchronous=NORMAL;" +
                        "PRAGMA temp_store=MEMORY;" +
                        "PRAGMA mmap_size=1073741824;");  // 1 GB mmap

                    // Plain rowid table — no index yet, no B-tree rebalancing per insert
                    Exec(conn,
                        "CREATE TABLE term_index (" +
                        "  term    TEXT    NOT NULL," +
                        "  offset  INTEGER NOT NULL," +
                        "  length  INTEGER NOT NULL," +
                        "  count   INTEGER NOT NULL" +
                        ");");

                    // Single transaction — all inserts in one batch
                    using (var tx  = conn.BeginTransaction())
                    using (var ins = conn.CreateCommand())
                    {
                        ins.CommandText =
                            "INSERT INTO term_index (term, offset, length, count) " +
                            "VALUES (@t, @o, @l, @c)";
                        var pTerm   = ins.Parameters.Add("@t", System.Data.DbType.String);
                        var pOffset = ins.Parameters.Add("@o", System.Data.DbType.Int64);
                        var pLen    = ins.Parameters.Add("@l", System.Data.DbType.Int32);
                        var pCount  = ins.Parameters.Add("@c", System.Data.DbType.Int32);

                        foreach (var kvp in _ramIndex)
                        {
                            byte[] buf   = kvp.Value.Stream.Buffer;
                            int    len   = kvp.Value.Stream.ByteLength;
                            long   off   = postings.Position;

                            postings.Write(buf, 0, len);

                            pTerm.Value   = kvp.Key;
                            pOffset.Value = off;
                            pLen.Value    = len;
                            pCount.Value  = kvp.Value.Stream.Count;
                            ins.ExecuteNonQuery();
                        }

                        tx.Commit();
                    }

                    // Build the unique index AFTER all rows are inserted — much faster
                    Exec(conn,
                        "CREATE UNIQUE INDEX idx_term ON term_index (term);" +
                        "ANALYZE;");
                }
            }
        }

        private static void Exec(SQLiteConnection conn, string sql)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }
    }
}
