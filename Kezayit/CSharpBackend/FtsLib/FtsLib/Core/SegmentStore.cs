using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Text;

namespace FtsLib.Core
{
    /// <summary>
    /// Manages sorted segment files and LSM-style merging with crash recovery.
    ///
    /// Segment file format (self-describing sorted run):
    ///   Per term, in ascending term-string order:
    ///     4 bytes  int    termByteLen
    ///     N bytes         term (UTF-8)
    ///     4 bytes  int    chunkByteLen
    ///     4 bytes  int    docCount
    ///     4 bytes  uint   lastEncoded   (encoded value of last doc ID)
    ///     M bytes         varint posting data
    ///
    /// Crash recovery:
    ///   A WAL (wal.log) records BEGIN/END for every merge and commit.
    ///   On open, Recover() replays the WAL:
    ///     - Interrupted merge  → delete partial target, redo merge
    ///     - Interrupted commit → delete partial postings.dat/Meta.db, redo commit
    ///   Source segments are never deleted until END_MERGE is logged.
    ///   The final segment is never deleted until END_COMMIT is logged.
    ///
    /// LSM tiering: fanout = 4, levels grow as needed.
    /// </summary>
    public sealed class SegmentStore
    {
        private const int FANOUT = 4;

        private readonly string     _dir;
        private readonly SegmentWal _wal;
        private int[]  _levelCount = new int[4];
        private int    _nextSegId;

        // Catalog: term → list of ChunkRef across live segments (flush order = docId order)
        private readonly Dictionary<string, List<ChunkRef>> _catalog =
            new Dictionary<string, List<ChunkRef>>(StringComparer.Ordinal);

        // Explicit set of live segment files per level — source of truth for merging
        private readonly Dictionary<int, HashSet<int>> _liveSegs =
            new Dictionary<int, HashSet<int>>(); // level → set of segIds

        public SegmentStore(string dir)
        {
            _dir = dir;
            if (!Directory.Exists(_dir))
                Directory.CreateDirectory(_dir);
            _wal = new SegmentWal(_dir);
            // WAL is opened for writing lazily in EnsureWalOpen(),
            // so Recover() can read it first without a lock conflict.
        }

        private void EnsureWalOpen()
        {
            _wal.Open(); // no-op if already open
        }

        // ── Recovery ─────────────────────────────────────────────────

        /// <summary>
        /// Call once after construction (before Flush/Commit) to recover from
        /// any interrupted merge or commit from a previous run.
        ///
        /// Scans the segments directory to rebuild live state, then replays
        /// the WAL to detect and repair any incomplete operations.
        /// </summary>
        public void Recover(string postingsPath, string metaDbPath)
        {
            // Step 1: rebuild live state from files on disk
            RebuildLiveState();

            // Step 2: replay WAL
            var recovery = _wal.Analyze();

            // If segments exist but the WAL shows no pending operation, this is either
            // a clean state (nothing to do) or a pre-WAL interrupted build where the
            // segment was never committed. Distinguish by checking postings.dat health:
            // if postings.dat is missing or smaller than the largest segment, commit.
            bool postingsHealthy = IsPostingsHealthy(postingsPath);

            if (recovery.PendingMerge != null)
            {
                var op = recovery.PendingMerge;
                Console.WriteLine($"[Recovery] Interrupted merge detected: L{op.Level} sources=[{string.Join(",", op.Sources)}] target={op.Target}");

                // Delete the partial target segment if it exists
                string partialTarget = SegPath(op.Level + 1, op.Target);
                if (File.Exists(partialTarget))
                {
                    Console.WriteLine($"[Recovery] Deleting partial target: {Path.GetFileName(partialTarget)}");
                    File.Delete(partialTarget);
                    RemoveFromLive(op.Level + 1, op.Target);
                }

                // Ensure all source segments are still registered as live
                // (they should be — we never delete sources before END_MERGE)
                foreach (int srcId in op.Sources)
                {
                    string srcPath = SegPath(op.Level, srcId);
                    if (File.Exists(srcPath))
                        AddToLive(op.Level, srcId);
                }

                // Redo the merge — EnsureWalOpen() is called inside MergeLevel
                Console.WriteLine($"[Recovery] Redoing merge L{op.Level}→L{op.Level + 1}...");
                EnsureWalOpen();
                MergeLevel(op.Level);
            }
            else if (recovery.PendingCommit != null)
            {
                Console.WriteLine($"[Recovery] Interrupted commit detected: {recovery.PendingCommit}");

                // Delete partial output files — they may be incomplete
                if (File.Exists(postingsPath)) { Console.WriteLine("[Recovery] Deleting partial postings.dat"); File.Delete(postingsPath); }
                if (File.Exists(metaDbPath))   { Console.WriteLine("[Recovery] Deleting partial Meta.db");     File.Delete(metaDbPath); }
                // Also delete WAL-journal files SQLite may have left
                if (File.Exists(metaDbPath + "-wal"))  File.Delete(metaDbPath + "-wal");
                if (File.Exists(metaDbPath + "-shm"))  File.Delete(metaDbPath + "-shm");

                // The segment file named in the WAL should still be on disk
                string segFile = Path.Combine(_dir, recovery.PendingCommit);
                if (File.Exists(segFile))
                {
                    Console.WriteLine($"[Recovery] Redoing commit from {recovery.PendingCommit}...");
                    EnsureWalOpen();
                    DoCommitFromSegment(segFile, postingsPath, metaDbPath);
                }
                else
                {
                    // Segment gone — find the highest-level surviving segment and commit from it
                    string best = FindHighestSegment();
                    if (best != null)
                    {
                        Console.WriteLine($"[Recovery] Original segment missing, committing from {Path.GetFileName(best)}...");
                        EnsureWalOpen();
                        DoCommitFromSegment(best, postingsPath, metaDbPath);
                    }
                    else
                    {
                        Console.WriteLine("[Recovery] No segments found — index is empty.");
                    }
                }
            }
            else if (!postingsHealthy && CountLiveSegs() > 0)
            {
                // No pending WAL operation, but postings.dat is missing/incomplete and
                // segments exist — this is a pre-WAL interrupted build. Commit from the
                // highest segment.
                string best = FindHighestSegment();
                if (best != null)
                {
                    Console.WriteLine($"[Recovery] Orphaned segment detected (no WAL): {Path.GetFileName(best)}");
                    Console.WriteLine("[Recovery] Committing from highest segment...");
                    EnsureWalOpen();
                    DoCommitFromSegment(best, postingsPath, metaDbPath);
                }
            }
        }

        // ── Flush ────────────────────────────────────────────────────

        public void Flush(RamIndex ramIndex)
        {
            int    segId = _nextSegId++;
            string path  = SegPath(0, segId);

            // Sort terms — every segment is a sorted run
            var terms = new List<string>(ramIndex.Count);
            foreach (var kvp in ramIndex) terms.Add(kvp.Key);
            terms.Sort(StringComparer.Ordinal);

            using (var fs = new FileStream(path, FileMode.Create,
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
                    long   off       = fs.Position;

                    bw.Write(termBytes.Length);
                    bw.Write(termBytes);
                    bw.Write(postLen);
                    bw.Write(entry.Stream.Count);
                    bw.Write(entry.Stream.LastEncoded);
                    bw.Write(postBuf, 0, postLen);

                    if (!_catalog.TryGetValue(term, out var list))
                    {
                        list = new List<ChunkRef>(4);
                        _catalog[term] = list;
                    }
                    list.Add(new ChunkRef(0, segId, off, postLen,
                                         entry.Stream.Count,
                                         entry.Stream.LastEncoded));
                }
            }

            AddToLive(0, segId);
            MergeIfNeeded(0);
        }

        // ── Merge cascade ────────────────────────────────────────────

        private void MergeIfNeeded(int level)
        {
            if (!_liveSegs.TryGetValue(level, out var live) || live.Count < FANOUT) return;
            if (level + 1 >= _levelCount.Length)
                Array.Resize(ref _levelCount, level + 2);
            MergeLevel(level);
            MergeIfNeeded(level + 1);
        }

        private void MergeLevel(int level)
        {
            int nextLevel = level + 1;
            int newSegId  = _nextSegId++;

            if (!_liveSegs.TryGetValue(level, out var liveAtLevel) || liveAtLevel.Count < 2)
                return;
            var segIds = new List<int>(liveAtLevel);

            int termsAtLevel = 0;
            foreach (var list in _catalog.Values)
                foreach (var c in list)
                    if (c.Level == level) { termsAtLevel++; break; }

            Console.WriteLine($"[Merger] L{level}→L{nextLevel} seg {newSegId}: {segIds.Count} segs, {termsAtLevel:N0} terms");

            string outPath = SegPath(nextLevel, newSegId);

            // ── WAL: record merge start BEFORE writing anything ──────
            EnsureWalOpen();
            _wal.BeginMerge(level, segIds.ToArray(), newSegId);

            var readers = new SegmentReader[segIds.Count];
            for (int i = 0; i < segIds.Count; i++)
                readers[i] = new SegmentReader(SegPath(level, segIds[i]));
            for (int i = 0; i < readers.Length; i++)
                readers[i].MoveNext();

            var newEntries = new List<(string term, long outOff, int outLen, int count, uint lastEncoded)>(termsAtLevel);
            const int REPORT_EVERY = 10_000;
            int  termsWritten = 0;
            long writePos     = 0;

            using (var outFs = new FileStream(outPath, FileMode.Create,
                                              FileAccess.Write, FileShare.None,
                                              bufferSize: 4 * 1024 * 1024))
            using (var bw = new BinaryWriter(outFs, Encoding.UTF8, leaveOpen: false))
            {
                while (true)
                {
                    string minTerm = null;
                    for (int i = 0; i < readers.Length; i++)
                    {
                        if (readers[i].Done) continue;
                        if (minTerm == null || string.CompareOrdinal(readers[i].CurrentTerm, minTerm) < 0)
                            minTerm = readers[i].CurrentTerm;
                    }
                    if (minTerm == null) break;

                    long outOff      = writePos;
                    int  totalCount  = 0;
                    uint prevEncoded = 0;
                    bool firstChunk  = true;
                    var  termBuf     = new MemoryStream(256);

                    for (int i = 0; i < readers.Length; i++)
                    {
                        if (readers[i].Done || readers[i].CurrentTerm != minTerm) continue;

                        byte[] chunk    = readers[i].CurrentChunk;
                        int    chunkLen = readers[i].CurrentChunkLen;

                        if (firstChunk)
                        {
                            termBuf.Write(chunk, 0, chunkLen);
                            firstChunk = false;
                        }
                        else
                        {
                            int  pos          = 0;
                            uint firstEncoded = ReadVarInt(chunk, ref pos, chunkLen);
                            uint newDelta     = firstEncoded - prevEncoded;
                            byte[] hdr        = new byte[5];
                            int    hdrLen     = EncodeVarInt(newDelta, hdr);
                            termBuf.Write(hdr, 0, hdrLen);
                            int restLen = chunkLen - pos;
                            if (restLen > 0) termBuf.Write(chunk, pos, restLen);
                        }

                        prevEncoded = readers[i].CurrentLastEncoded;
                        totalCount += readers[i].CurrentCount;
                        readers[i].MoveNext();
                    }

                    byte[] termBytes  = Encoding.UTF8.GetBytes(minTerm);
                    int    termOutLen = (int)termBuf.Length;
                    bw.Write(termBytes.Length);
                    bw.Write(termBytes);
                    bw.Write(termOutLen);
                    bw.Write(totalCount);
                    bw.Write(prevEncoded);
                    bw.Flush();
                    outFs.Write(termBuf.GetBuffer(), 0, termOutLen);

                    writePos += 4 + termBytes.Length + 4 + 4 + 4 + termOutLen;
                    newEntries.Add((minTerm, outOff, termOutLen, totalCount, prevEncoded));

                    termsWritten++;
                    if (termsWritten % REPORT_EVERY == 0)
                        Console.WriteLine($"[Merger]   L{level}→L{nextLevel}: {termsWritten:N0}/{termsAtLevel:N0} ({termsWritten * 100 / termsAtLevel}%)  {writePos / 1024 / 1024:N0} MB");
                }
            }

            for (int i = 0; i < readers.Length; i++) readers[i].Dispose();

            // ── WAL: record merge complete BEFORE deleting sources ───
            _wal.EndMerge(level, newSegId);

            // Now safe to delete source segments
            foreach (int sid in segIds)
            {
                string sp = SegPath(level, sid);
                if (File.Exists(sp)) File.Delete(sp);
            }

            // Update live state
            liveAtLevel.ExceptWith(segIds);
            AddToLive(nextLevel, newSegId);

            // Update catalog
            foreach (var (term, outOff, outLen, count, lastEncoded) in newEntries)
            {
                var list = _catalog[term];
                for (int i = list.Count - 1; i >= 0; i--)
                    if (list[i].Level == level) list.RemoveAt(i);
                list.Insert(0, new ChunkRef(nextLevel, newSegId, outOff, outLen, count, lastEncoded));
            }

            foreach (var list in _catalog.Values)
                for (int i = list.Count - 1; i >= 0; i--)
                    if (list[i].Level == level && segIds.Contains(list[i].SegId))
                        list.RemoveAt(i);

            _levelCount[level]     = Math.Max(0, _levelCount[level] - segIds.Count);
            _levelCount[nextLevel]++;

            Console.WriteLine($"[Merger] Done → L{nextLevel} seg {newSegId} ({termsWritten:N0} terms, {writePos / 1024 / 1024:N0} MB)");
        }

        // ── Commit ───────────────────────────────────────────────────

        public void Commit(string postingsPath, string metaDbPath)
        {
            ForceMergeAll();

            // Find the highest-level surviving segment
            int aliveLevel = -1, aliveSegId = -1;
            foreach (var kv in _liveSegs)
            {
                if (kv.Value.Count == 1 && kv.Key > aliveLevel)
                {
                    aliveLevel = kv.Key;
                    foreach (int sid in kv.Value) aliveSegId = sid;
                }
            }

            if (aliveLevel < 0)
            {
                File.WriteAllBytes(postingsPath, new byte[0]);
                WriteMetaDb(metaDbPath, new List<(string, long, int, int)>());
                _wal.Clear();
                return;
            }

            string segFile = SegPath(aliveLevel, aliveSegId);
            DoCommitFromSegment(segFile, postingsPath, metaDbPath);
        }

        private void DoCommitFromSegment(string segFile, string postingsPath, string metaDbPath)
        {
            long sizeMb = new FileInfo(segFile).Length / 1024 / 1024;
            Console.WriteLine($"[SegmentStore] Committing from {Path.GetFileName(segFile)} ({sizeMb:N0} MB)...");

            // Delete any previous incomplete output
            if (File.Exists(postingsPath)) File.Delete(postingsPath);
            if (File.Exists(metaDbPath))   File.Delete(metaDbPath);
            if (File.Exists(metaDbPath + "-wal")) File.Delete(metaDbPath + "-wal");
            if (File.Exists(metaDbPath + "-shm")) File.Delete(metaDbPath + "-shm");

            // ── WAL: record commit start BEFORE writing output ───────
            EnsureWalOpen();
            _wal.BeginCommit(segFile);

            var meta = new List<(string term, long offset, int length, int count)>(_catalog.Count);

            using (var reader = new SegmentReader(segFile))
            using (var postFs = new FileStream(postingsPath, FileMode.Create,
                                               FileAccess.Write, FileShare.None,
                                               bufferSize: 4 * 1024 * 1024))
            {
                const int REPORT_EVERY = 50_000;
                int written = 0;

                while (reader.MoveNext())
                {
                    long   off   = postFs.Position;
                    byte[] chunk = reader.CurrentChunk;
                    int    len   = reader.CurrentChunkLen;

                    postFs.Write(chunk, 0, len);
                    meta.Add((reader.CurrentTerm, off, len, reader.CurrentCount));

                    written++;
                    if (written % REPORT_EVERY == 0)
                        Console.WriteLine($"[SegmentStore]   commit: {written:N0} terms, {postFs.Position / 1024 / 1024:N0} MB");
                }
            }

            Console.WriteLine($"[SegmentStore] Writing Meta.db ({meta.Count:N0} terms)...");
            WriteMetaDb(metaDbPath, meta);

            // ── WAL: record commit complete BEFORE deleting segment ──
            _wal.EndCommit();

            // Now safe to delete the segment
            if (File.Exists(segFile)) File.Delete(segFile);

            // Clear WAL — index is fully consistent
            _wal.Clear();
            _catalog.Clear();

            Console.WriteLine("[SegmentStore] Done.");
        }

        private void ForceMergeAll()
        {
            int totalSegs = 0;
            foreach (var kv in _liveSegs) totalSegs += kv.Value.Count;
            Console.WriteLine($"[SegmentStore] Force-merge: {totalSegs} segment(s), {_catalog.Count:N0} terms");

            bool progress;
            do
            {
                progress = false;
                foreach (var kv in _liveSegs)
                {
                    if (kv.Value.Count >= 2)
                    {
                        int level = kv.Key;
                        if (level + 1 >= _levelCount.Length)
                            Array.Resize(ref _levelCount, level + 2);
                        MergeLevel(level);
                        progress = true;
                        break;
                    }
                }
            } while (progress);

            Console.WriteLine("[SegmentStore] Force-merge complete.");
        }

        // ── Rebuild live state from disk ─────────────────────────────

        /// <summary>
        /// Scans the segments directory and rebuilds _liveSegs and _nextSegId
        /// from the actual files on disk. Called during recovery before WAL replay.
        /// </summary>
        private void RebuildLiveState()
        {
            _liveSegs.Clear();
            _nextSegId = 0;

            foreach (var file in Directory.GetFiles(_dir, "seg_*.dat"))
            {
                string name = Path.GetFileNameWithoutExtension(file); // seg_L_ID
                var parts = name.Split('_');
                if (parts.Length != 3) continue;
                if (!int.TryParse(parts[1], out int level)) continue;
                if (!int.TryParse(parts[2], out int segId)) continue;

                AddToLive(level, segId);
                if (segId >= _nextSegId) _nextSegId = segId + 1;
            }

            if (_liveSegs.Count > 0)
                Console.WriteLine($"[Recovery] Found {CountLiveSegs()} segment(s) on disk, nextSegId={_nextSegId}");
        }

        // ── Live state helpers ───────────────────────────────────────

        private void AddToLive(int level, int segId)
        {
            if (!_liveSegs.TryGetValue(level, out var set))
            {
                set = new HashSet<int>();
                _liveSegs[level] = set;
            }
            set.Add(segId);

            if (level >= _levelCount.Length)
                Array.Resize(ref _levelCount, level + 2);
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

        private int CountLiveSegs()
        {
            int n = 0;
            foreach (var kv in _liveSegs) n += kv.Value.Count;
            return n;
        }

        /// <summary>
        /// Returns true if postings.dat exists and is at least as large as the
        /// largest segment on disk (a rough but reliable health check).
        /// </summary>
        private bool IsPostingsHealthy(string postingsPath)
        {
            if (!File.Exists(postingsPath)) return false;
            long postingsSize = new FileInfo(postingsPath).Length;

            long maxSegSize = 0;
            foreach (var file in Directory.GetFiles(_dir, "seg_*.dat"))
            {
                long sz = new FileInfo(file).Length;
                if (sz > maxSegSize) maxSegSize = sz;
            }

            // postings.dat is a flattened version of the segment (no headers),
            // so it will be somewhat smaller — but never smaller than 10% of the segment.
            return maxSegSize == 0 || postingsSize >= maxSegSize / 10;
        }

        private string FindHighestSegment()
        {
            int bestLevel = -1, bestId = -1;
            foreach (var kv in _liveSegs)
                foreach (int sid in kv.Value)
                    if (kv.Key > bestLevel || (kv.Key == bestLevel && sid > bestId))
                    { bestLevel = kv.Key; bestId = sid; }

            return bestLevel >= 0 ? SegPath(bestLevel, bestId) : null;
        }

        // ── Meta.db ──────────────────────────────────────────────────

        private static void WriteMetaDb(string path,
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
                    ins.CommandText = "INSERT INTO term_index(term,offset,length,count) VALUES(@t,@o,@l,@c)";
                    var pT = ins.Parameters.Add("@t", System.Data.DbType.String);
                    var pO = ins.Parameters.Add("@o", System.Data.DbType.Int64);
                    var pL = ins.Parameters.Add("@l", System.Data.DbType.Int32);
                    var pC = ins.Parameters.Add("@c", System.Data.DbType.Int32);
                    foreach (var (term, off, len, cnt) in rows)
                    { pT.Value = term; pO.Value = off; pL.Value = len; pC.Value = cnt; ins.ExecuteNonQuery(); }
                    tx.Commit();
                }
                Exec(conn, "CREATE UNIQUE INDEX idx_term ON term_index(term);ANALYZE;");
            }
        }

        private static void Exec(SQLiteConnection conn, string sql)
        { using (var cmd = conn.CreateCommand()) { cmd.CommandText = sql; cmd.ExecuteNonQuery(); } }

        // ── Path helpers ─────────────────────────────────────────────

        private string SegPath(int level, int segId) =>
            Path.Combine(_dir, $"seg_{level}_{segId}.dat");

        // ── Varint helpers ───────────────────────────────────────────

        private static uint ReadVarInt(byte[] buf, ref int pos, int len)
        {
            int shift = 0; uint result = 0;
            while (pos < len)
            {
                byte b = buf[pos++];
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
            }
            return result;
        }

        private static int EncodeVarInt(uint v, byte[] buf)
        {
            int i = 0;
            while (v >= 0x80) { buf[i++] = (byte)(v | 0x80); v >>= 7; }
            buf[i++] = (byte)v;
            return i;
        }
    }

    // ── ChunkRef ─────────────────────────────────────────────────────

    internal readonly struct ChunkRef
    {
        public readonly int   Level;
        public readonly int   SegId;
        public readonly long  Offset;
        public readonly int   Length;
        public readonly int   Count;
        public readonly uint  LastEncoded;

        public ChunkRef(int level, int segId, long offset, int length, int count, uint lastEncoded)
        { Level = level; SegId = segId; Offset = offset; Length = length; Count = count; LastEncoded = lastEncoded; }
    }

    // ── SegmentReader ─────────────────────────────────────────────────

    /// <summary>
    /// Forward-only reader for a sorted segment file.
    /// Reads one term at a time in ascending term order.
    /// </summary>
    internal sealed class SegmentReader : IDisposable
    {
        private readonly FileStream   _fs;
        private readonly BinaryReader _br;

        public string CurrentTerm        { get; private set; }
        public byte[] CurrentChunk       { get; private set; }
        public int    CurrentChunkLen    { get; private set; }
        public int    CurrentCount       { get; private set; }
        public uint   CurrentLastEncoded { get; private set; }
        public bool   Done               { get; private set; }

        public SegmentReader(string path)
        {
            _fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                 FileShare.Read, bufferSize: 4 * 1024 * 1024);
            _br = new BinaryReader(_fs, Encoding.UTF8, leaveOpen: false);
        }

        public bool MoveNext()
        {
            if (Done || _fs.Position >= _fs.Length) { Done = true; return false; }

            int    termLen     = _br.ReadInt32();
            byte[] termBytes   = _br.ReadBytes(termLen);
            int    chunkLen    = _br.ReadInt32();
            int    count       = _br.ReadInt32();
            uint   lastEncoded = _br.ReadUInt32();
            byte[] chunk       = _br.ReadBytes(chunkLen);

            CurrentTerm        = Encoding.UTF8.GetString(termBytes);
            CurrentChunk       = chunk;
            CurrentChunkLen    = chunkLen;
            CurrentCount       = count;
            CurrentLastEncoded = lastEncoded;
            return true;
        }

        public void Dispose() { _br?.Dispose(); _fs?.Dispose(); }
    }
}
