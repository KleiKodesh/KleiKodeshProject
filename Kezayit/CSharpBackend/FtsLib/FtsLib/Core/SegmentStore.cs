using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Text;

namespace FtsLib.Core
{
    /// <summary>
    /// Manages sorted segment files (seg_L_ID.dat + seg_L_ID.db).
    ///
    /// Each segment pair is a complete, searchable unit:
    ///   .dat  — sorted varint posting data
    ///   .db   — SQLite term index: term → (offset, length, count) into the .dat
    ///
    /// This is the only persistent format — there is no separate postings.dat or Meta.db.
    /// The "committed" state is simply the result of ForceMergeAll: one segment pair.
    /// Search works across all live segments at any point, mid-build or finalized.
    ///
    /// LSM tiering: fanout = 4, levels grow as needed.
    /// Crash safety: SegmentWal records BEGIN/END for every merge.
    /// </summary>
    internal sealed class SegmentStore
    {
        internal const int Fanout = 4;

        private readonly string        _dir;
        private readonly SegmentMerger _merger;
        internal readonly SegmentWal   Wal;

        private int[]  _levelCount = new int[4];
        private int    _nextSegId;
        private DeleteSet _deleteSet; // null = no deletions to purge

        // level → set of live segIds
        private readonly Dictionary<int, HashSet<int>> _liveSegs =
            new Dictionary<int, HashSet<int>>();

        public SegmentStore(string dir)
        {
            _dir   = dir;
            if (!Directory.Exists(_dir)) Directory.CreateDirectory(_dir);
            Wal    = new SegmentWal(_dir);
            _merger = new SegmentMerger(this);
        }

        // ── Delete set (used during Purge) ────────────────────────────

        /// <summary>
        /// Sets the delete set that will be consulted during the next merge.
        /// Pass null to clear (normal merge with no purge).
        /// </summary>
        internal void SetDeleteSet(DeleteSet ds) => _deleteSet = ds;

        internal DeleteSet GetDeleteSet() => _deleteSet;

        // ── Recovery ─────────────────────────────────────────────────

        public void Recover()
        {
            // Clean up any leftover temp files from an interrupted merge before
            // rebuilding live state — temp files must never be treated as live segments.
            foreach (var tmp in Directory.GetFiles(_dir, "*.tmp"))
            {
                try { File.Delete(tmp); }
                catch { /* best-effort */ }
            }

            RebuildLiveState();
            var recovery = Wal.Analyze();

            if (recovery.PendingMerge != null)
            {
                var op = recovery.PendingMerge;
                Console.WriteLine($"[Recovery] Interrupted merge: L{op.Level} → target {op.Target}");

                DeleteIfExists(SegDatPath(op.Level + 1, op.Target));
                DeleteIfExists(SegDbPath(op.Level + 1, op.Target));
                RemoveFromLive(op.Level + 1, op.Target);

                foreach (int sid in op.Sources)
                    if (File.Exists(SegDatPath(op.Level, sid)))
                        AddToLive(op.Level, sid);

                Wal.Open();
                _merger.MergeLevel(op.Level);
                Wal.Close();
            }
        }

        // ── Flush ────────────────────────────────────────────────────

        public void Flush(RamIndex ramIndex)
        {
            int    segId   = NextSegId();
            string datPath = SegDatPath(0, segId);
            string dbPath  = SegDbPath(0, segId);

            var terms = new List<string>(ramIndex.Count);
            foreach (var kvp in ramIndex) terms.Add(kvp.Key);
            terms.Sort(StringComparer.Ordinal);

            var meta = new List<(string term, long offset, int length, int count)>(terms.Count);

            using (var fs = new FileStream(datPath, FileMode.Create,
                                           FileAccess.Write, FileShare.None,
                                           bufferSize: 4 * 1024 * 1024))
            using (var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: false))
            {
                foreach (var term in terms)
                {
                    var    entry     = ramIndex[term];
                    byte[] termBytes = Encoding.UTF8.GetBytes(term);
                    byte[] postBuf   = entry.Stream.Buffer;
                    int    postLen   = entry.Stream.ByteLength;

                    bw.Write(termBytes.Length);
                    bw.Write(termBytes);
                    bw.Write(postLen);
                    bw.Write(entry.Stream.Count);
                    bw.Write(entry.Stream.LastEncoded);
                    bw.Flush();

                    long off = fs.Position; // offset of posting data, after the header
                    fs.Write(postBuf, 0, postLen);

                    meta.Add((term, off, postLen, entry.Stream.Count));
                }
            }

            WriteMetaDb(dbPath, meta);
            AddToLive(0, segId);
            Wal.Open();
            _merger.MergeIfNeeded(0);
            Wal.Close(); // release file lock after each flush+merge cycle
        }

        // ── Commit (finalize) ─────────────────────────────────────────

        /// <summary>
        /// Force-merges all segments into one. After this the index is a single
        /// segment pair ready for fast single-segment search. Optional — search
        /// works correctly across any number of live segments.
        /// </summary>
        public void Commit()
        {
            Wal.Open();
            _merger.ForceMergeAll();
            Wal.Clear();
            Console.WriteLine("[SegmentStore] Commit complete.");
        }

        // ── Live segment enumeration (used by IndexReader) ────────────

        /// <summary>
        /// Returns all live (datPath, dbPath) pairs for searching.
        /// Includes every segment at every level.
        /// </summary>
        public List<(string dat, string db)> GetLiveSegmentPaths()
        {
            var result = new List<(string, string)>();
            foreach (var kv in _liveSegs)
                foreach (int sid in kv.Value)
                    result.Add((SegDatPath(kv.Key, sid), SegDbPath(kv.Key, sid)));
            return result;
        }

        // ── Internal helpers used by SegmentMerger ───────────────────

        internal int NextSegId() => _nextSegId++;

        internal int LiveSegCount(int level) =>
            _liveSegs.TryGetValue(level, out var s) ? s.Count : 0;

        internal int TotalLiveSegs()
        {
            int n = 0;
            foreach (var kv in _liveSegs) n += kv.Value.Count;
            return n;
        }

        internal int FindLevelWithMultiple()
        {
            foreach (var kv in _liveSegs)
                if (kv.Value.Count >= 2) return kv.Key;
            return -1;
        }

        internal List<int> GetLiveSegIds(int level)
        {
            if (!_liveSegs.TryGetValue(level, out var set)) return new List<int>();
            return new List<int>(set);
        }

        internal void PromoteSegment(int srcLevel, List<int> removed, int dstLevel, int newSegId)
        {
            if (_liveSegs.TryGetValue(srcLevel, out var src))
            {
                src.ExceptWith(removed);
                _levelCount[srcLevel] = src.Count;
            }
            AddToLive(dstLevel, newSegId);
        }

        internal void EnsureLevel(int level)
        {
            if (level >= _levelCount.Length)
                Array.Resize(ref _levelCount, level + 2);
        }

        internal string SegDatPath(int level, int segId) =>
            Path.Combine(_dir, $"seg_{level}_{segId}.dat");

        internal string SegDbPath(int level, int segId) =>
            Path.Combine(_dir, $"seg_{level}_{segId}.db");

        // ── Meta.db writer (shared by Flush and SegmentMerger) ───────

        internal static void WriteMetaDb(
            string path,
            List<(string term, long offset, int length, int count)> rows)
        {
            string connStr = $"Data Source={path};Version=3;Page Size=65536;Cache Size=8000;";
            using (var conn = new SQLiteConnection(connStr))
            {
                conn.Open();
                Exec(conn,
                    "PRAGMA journal_mode=WAL;PRAGMA synchronous=NORMAL;" +
                    "PRAGMA temp_store=MEMORY;PRAGMA mmap_size=1073741824;");
                Exec(conn,
                    "CREATE TABLE term_index(" +
                    "term TEXT NOT NULL,offset INTEGER NOT NULL," +
                    "length INTEGER NOT NULL,count INTEGER NOT NULL);");

                using (var tx  = conn.BeginTransaction())
                using (var ins = conn.CreateCommand())
                {
                    ins.CommandText =
                        "INSERT INTO term_index(term,offset,length,count) VALUES(@t,@o,@l,@c)";
                    var pT = ins.Parameters.Add("@t", System.Data.DbType.String);
                    var pO = ins.Parameters.Add("@o", System.Data.DbType.Int64);
                    var pL = ins.Parameters.Add("@l", System.Data.DbType.Int32);
                    var pC = ins.Parameters.Add("@c", System.Data.DbType.Int32);
                    foreach (var (term, off, len, cnt) in rows)
                    {
                        pT.Value = term; pO.Value = off; pL.Value = len; pC.Value = cnt;
                        ins.ExecuteNonQuery();
                    }
                    tx.Commit();
                }
                Exec(conn, "CREATE UNIQUE INDEX idx_term ON term_index(term);ANALYZE;");
            }
        }

        // ── Live state ───────────────────────────────────────────────

        private void RebuildLiveState()
        {
            _liveSegs.Clear();
            _nextSegId = 0;

            foreach (var file in Directory.GetFiles(_dir, "seg_*.dat"))
            {
                string name  = Path.GetFileNameWithoutExtension(file);
                var    parts = name.Split('_');
                if (parts.Length != 3) continue;
                if (!int.TryParse(parts[1], out int level)) continue;
                if (!int.TryParse(parts[2], out int segId)) continue;

                AddToLive(level, segId);
                if (segId >= _nextSegId) _nextSegId = segId + 1;
            }

            if (_liveSegs.Count > 0)
                Console.WriteLine($"[Recovery] Found {TotalLiveSegs()} segment(s), nextSegId={_nextSegId}");
        }

        private void AddToLive(int level, int segId)
        {
            if (!_liveSegs.TryGetValue(level, out var set))
            {
                set = new HashSet<int>();
                _liveSegs[level] = set;
            }
            set.Add(segId);
            EnsureLevel(level);
            _levelCount[level] = set.Count;
        }

        private void RemoveFromLive(int level, int segId)
        {
            if (_liveSegs.TryGetValue(level, out var set))
            {
                set.Remove(segId);
                _levelCount[level] = set.Count;
            }
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        private static void Exec(SQLiteConnection conn, string sql)
        {
            using (var cmd = conn.CreateCommand())
            { cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        }
    }
}
