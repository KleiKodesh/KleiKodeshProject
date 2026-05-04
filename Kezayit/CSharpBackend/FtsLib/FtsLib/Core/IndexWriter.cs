using System;
using System.Data.SQLite;
using System.IO;

namespace FtsLib.Core
{
    public sealed class IndexWriter : IndexPaths, IDisposable
    {
        /// <summary>
        /// Flush the RamIndex when it reaches this many distinct terms.
        /// 100k is a safe default; tune upward for machines with more RAM.
        /// </summary>
        public int FlushThreshold { get; set; } = 250_000;

        private RamIndex      _ramIndex;
        private SegmentStore  _store;
        private readonly bool _useSkipList;
        private bool          _committed;

        public IndexWriter(string indexPath, bool useSkipList = true) : base(indexPath)
        {
            _useSkipList = useSkipList;
            _ramIndex    = new RamIndex(useSkipList: useSkipList);

            string segDir = Path.Combine(IndexPath, "segments");

            // If segments directory exists and has content, recover before doing anything else.
            // This handles crashes during a previous merge or commit.
            if (Directory.Exists(segDir) &&
                (Directory.GetFiles(segDir, "seg_*.dat").Length > 0 ||
                 File.Exists(Path.Combine(segDir, "wal.log"))))
            {
                Console.WriteLine("[IndexWriter] Segments found — running crash recovery...");
                _store = new SegmentStore(segDir);
                _store.Recover(PostingsPath, MetaDbPath);
                Console.WriteLine("[IndexWriter] Recovery complete.");
            }
        }

        public void Add(int lineId, string term)
        {
            if (_committed)
                throw new InvalidOperationException("IndexWriter has already been committed.");

            _ramIndex.Add(term, lineId);

            if (_ramIndex.Count >= FlushThreshold)
                FlushRam();
        }

        public void Dispose()
        {
            if (!_committed)
                Commit();
        }

        // ── Private ──────────────────────────────────────────────────

        private void FlushRam()
        {
            if (_ramIndex.Count == 0) return;

            if (_store == null)
                _store = new SegmentStore(Path.Combine(IndexPath, "segments"));

            Console.WriteLine($"[IndexWriter] Flushing {_ramIndex.Count:N0} terms to segment...");
            _store.Flush(_ramIndex);
            Console.WriteLine("[IndexWriter] Flush complete.");
            _ramIndex = new RamIndex(useSkipList: _useSkipList);
        }

        private void Commit()
        {
            _committed = true;

            if (_store == null)
            {
                Console.WriteLine("[IndexWriter] Committing (single-pass)...");
                CommitDirect();
                Console.WriteLine("[IndexWriter] Done.");
                return;
            }

            FlushRam(); // flush tail
            Console.WriteLine("[IndexWriter] Committing — merging all segments...");
            _store.Commit(PostingsPath, MetaDbPath);
            Console.WriteLine("[IndexWriter] Done.");
        }

        private void CommitDirect()
        {
            if (File.Exists(PostingsPath)) File.Delete(PostingsPath);
            if (File.Exists(MetaDbPath))   File.Delete(MetaDbPath);

            using (var postings = new FileStream(PostingsPath, FileMode.Create,
                                                 FileAccess.Write, FileShare.None,
                                                 bufferSize: 4 * 1024 * 1024))
            {
                string connStr =
                    $"Data Source={MetaDbPath};Version=3;" +
                    $"Page Size=65536;Cache Size=8000;";

                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();
                    Exec(conn,
                        "PRAGMA journal_mode=WAL;" +
                        "PRAGMA synchronous=NORMAL;" +
                        "PRAGMA temp_store=MEMORY;" +
                        "PRAGMA mmap_size=1073741824;");

                    Exec(conn,
                        "CREATE TABLE term_index (" +
                        "  term    TEXT    NOT NULL," +
                        "  offset  INTEGER NOT NULL," +
                        "  length  INTEGER NOT NULL," +
                        "  count   INTEGER NOT NULL" +
                        ");");

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
                            byte[] buf = kvp.Value.Stream.Buffer;
                            int    len = kvp.Value.Stream.ByteLength;
                            long   off = postings.Position;

                            postings.Write(buf, 0, len);

                            pTerm.Value   = kvp.Key;
                            pOffset.Value = off;
                            pLen.Value    = len;
                            pCount.Value  = kvp.Value.Stream.Count;
                            ins.ExecuteNonQuery();
                        }
                        tx.Commit();
                    }

                    Exec(conn,
                        "CREATE UNIQUE INDEX idx_term ON term_index (term);" +
                        "ANALYZE;");
                }
            }
        }

        private static void Exec(SQLiteConnection conn, string sql)
        {
            using (var cmd = conn.CreateCommand())
            { cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        }
    }
}
