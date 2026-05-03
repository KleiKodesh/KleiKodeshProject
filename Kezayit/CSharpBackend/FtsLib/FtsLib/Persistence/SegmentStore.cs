using System;
using System.Collections.Generic;
using System.IO;
using FtsLib.Index;

namespace FtsLib.Persistence
{
    /// <summary>
    /// Manages on-disk segment files and the in-memory metadata catalog.
    ///
    /// Segment file layout
    /// -------------------
    /// A segment is a flat binary file "seg_{level}_{id}.dat" containing
    /// concatenated varint posting chunks, one per term, in arbitrary order.
    /// The exact position of each term's chunk is recorded in the catalog.
    ///
    /// Catalog entry per term per segment
    /// -----------------------------------
    ///   Offset      — byte offset in the segment file
    ///   Length      — byte length of the chunk
    ///   Count       — number of doc IDs in the chunk
    ///   LastEncoded — encoded value of the last doc ID (needed to chain
    ///                 delta encoding when concatenating segments)
    ///
    /// LSM tiering
    /// -----------
    /// Level 0 : freshly flushed RAM segments
    /// Level 1 : merged from FANOUT level-0 segments
    /// Level 2 : merged from FANOUT level-1 segments  (final level for now)
    ///
    /// After a merge the source segments are deleted from disk and their
    /// catalog entries are replaced by the single merged entry.
    /// </summary>
    internal sealed class SegmentStore
    {
        // How many segments at one level trigger a merge into the next level.
        private const int FANOUT = 4;

        private readonly string _dir;

        // catalog[term] = list of chunks across all live segments, in flush order
        // (which is also ascending docId order, since ingestion is ordered).
        private readonly Dictionary<string, List<ChunkRef>> _catalog =
            new Dictionary<string, List<ChunkRef>>(StringComparer.Ordinal);

        // Counters per level — how many segments exist at each level right now.
        private readonly int[] _levelCount = new int[3];

        // Next segment id (monotonically increasing, never reused).
        private int _nextSegId;

        public SegmentStore(string indexDir)
        {
            _dir = indexDir;
            if (!Directory.Exists(_dir))
                Directory.CreateDirectory(_dir);
        }

        /// <summary>Total number of distinct terms across all live segments.</summary>
        public int CatalogTermCount => _catalog.Count;

        // ── Flush ────────────────────────────────────────────────────

        /// <summary>
        /// Flush a RamIndex to a level-0 segment file, update the catalog,
        /// then trigger LSM merges if any level has reached FANOUT segments.
        /// </summary>
        public void Flush(RamIndex ramIndex)
        {
            int segId = _nextSegId++;
            string path = SegPath(0, segId);

            using (var fs = new FileStream(path, FileMode.Create,
                                           FileAccess.Write, FileShare.None,
                                           bufferSize: 4 * 1024 * 1024))
            {
                foreach (var kvp in ramIndex)
                {
                    string term  = kvp.Key;
                    var    entry = kvp.Value;
                    byte[] buf   = entry.Stream.Buffer;
                    int    len   = entry.Stream.ByteLength;
                    long   off   = fs.Position;

                    fs.Write(buf, 0, len);

                    if (!_catalog.TryGetValue(term, out var list))
                    {
                        list = new List<ChunkRef>(4);
                        _catalog[term] = list;
                    }
                    list.Add(new ChunkRef(0, segId, off, len,
                                         entry.Stream.Count,
                                         entry.Stream.LastEncoded));
                }
            }

            _levelCount[0]++;
            MergeIfNeeded(0);
        }

        // ── Merge ────────────────────────────────────────────────────

        private void MergeIfNeeded(int level)
        {
            if (level >= 2) return;                    // level 2 is the top
            if (_levelCount[level] < FANOUT) return;   // not enough segments yet

            MergeLevel(level);
            MergeIfNeeded(level + 1);                  // cascade upward
        }

        /// <summary>
        /// Merge all level-<paramref name="level"/> segments into one level+1 segment.
        /// For each term, concatenate the posting chunks in order, re-encoding
        /// the delta chain so the merged chunk is a single continuous varint stream.
        /// </summary>
        private void MergeLevel(int level)
        {
            int nextLevel = level + 1;
            int newSegId  = _nextSegId++;
            string newPath = SegPath(nextLevel, newSegId);

            // Collect the segment ids that belong to this level
            var segIds = new HashSet<int>();
            foreach (var list in _catalog.Values)
                foreach (var c in list)
                    if (c.Level == level)
                        segIds.Add(c.SegId);

            // Open all source segment files for reading
            var readers = new Dictionary<int, FileStream>();
            foreach (int sid in segIds)
                readers[sid] = new FileStream(SegPath(level, sid),
                                              FileMode.Open, FileAccess.Read,
                                              FileShare.Read, 64 * 1024);

            using (var outFs = new FileStream(newPath, FileMode.Create,
                                              FileAccess.Write, FileShare.None,
                                              bufferSize: 4 * 1024 * 1024))
            {
                // Reusable read buffer
                byte[] readBuf = new byte[64 * 1024];

                foreach (var kvp in _catalog)
                {
                    string term  = kvp.Key;
                    var    list  = kvp.Value;

                    // Collect chunks that belong to the level being merged, in order
                    var toMerge = new List<ChunkRef>(list.Count);
                    foreach (var c in list)
                        if (c.Level == level)
                            toMerge.Add(c);

                    if (toMerge.Count == 0) continue;

                    long   outOff       = outFs.Position;
                    int    totalCount   = 0;
                    uint   prevEncoded  = 0;
                    bool   firstChunk   = true;

                    foreach (var chunk in toMerge)
                    {
                        var fs = readers[chunk.SegId];

                        // Read the raw varint bytes for this chunk
                        int needed = chunk.Length;
                        if (readBuf.Length < needed)
                            readBuf = new byte[needed * 2];

                        fs.Seek(chunk.Offset, SeekOrigin.Begin);
                        int got = 0;
                        while (got < needed)
                            got += fs.Read(readBuf, got, needed - got);

                        if (firstChunk)
                        {
                            // First chunk: copy verbatim — its delta chain starts from 0
                            outFs.Write(readBuf, 0, needed);
                            firstChunk   = false;
                        }
                        else
                        {
                            // Subsequent chunks: the first varint encodes
                            //   (firstDocEncoded - 0) = firstDocEncoded  (absolute)
                            // but we need it to encode
                            //   (firstDocEncoded - prevEncoded)  (delta from previous chunk's last)
                            //
                            // Strategy: decode the first value, subtract prevEncoded,
                            // re-encode as varint, then copy the rest verbatim.
                            int pos = 0;
                            uint firstEncoded = ReadVarInt(readBuf, ref pos, needed);
                            // firstEncoded is the absolute encoded value of the first doc
                            // in this chunk (because each chunk starts its own delta chain).
                            // The delta we need to write is firstEncoded - prevEncoded.
                            uint delta = firstEncoded - prevEncoded;
                            WriteVarInt(outFs, delta);
                            // Copy the rest of the chunk verbatim — those varints are
                            // already correct relative deltas within the chunk.
                            if (pos < needed)
                                outFs.Write(readBuf, pos, needed - pos);
                        }

                        prevEncoded  = chunk.LastEncoded;
                        totalCount  += chunk.Count;
                    }

                    long outLen = outFs.Position - outOff;

                    // Replace the merged chunks in the catalog with one new entry
                    list.RemoveAll(c => c.Level == level);
                    list.Insert(0, new ChunkRef(nextLevel, newSegId, outOff,
                                                (int)outLen, totalCount,
                                                prevEncoded));
                }
            }

            // Close and delete source files
            foreach (var kv in readers) { kv.Value.Dispose(); }
            foreach (int sid in segIds)
            {
                string p = SegPath(level, sid);
                if (File.Exists(p)) File.Delete(p);
            }

            _levelCount[level]    -= FANOUT;   // we consumed FANOUT segments
            _levelCount[nextLevel]++;
        }

        // ── Final write ──────────────────────────────────────────────

        /// <summary>
        /// Merge everything down to a single stream and write the final
        /// postings.dat + Meta.db that DiskIndexReader expects.
        /// Cleans up all segment files afterwards.
        /// </summary>
        public void Commit(string postingsPath, string indexDbPath)
        {
            // Force-merge all remaining levels top-down until one segment remains
            ForceMergeAll();

            if (File.Exists(postingsPath)) File.Delete(postingsPath);
            if (File.Exists(indexDbPath))  File.Delete(indexDbPath);

            // At this point every term has exactly one ChunkRef.
            // Open the single remaining segment file.
            // Find which segment file is still alive.
            int aliveLevel = -1, aliveSegId = -1;
            foreach (var list in _catalog.Values)
            {
                if (list.Count > 0)
                {
                    aliveLevel = list[0].Level;
                    aliveSegId = list[0].SegId;
                    break;
                }
            }

            if (aliveLevel < 0)
            {
                // Empty index — write empty files
                File.WriteAllBytes(postingsPath, Array.Empty<byte>());
                WriteMetaDb(indexDbPath, new List<(string, long, int, int)>());
                return;
            }

            string segFile = SegPath(aliveLevel, aliveSegId);

            // Copy segment → postings.dat, adjusting offsets
            var metaRows = new List<(string term, long offset, int length, int count)>(_catalog.Count);

            using (var src = new FileStream(segFile, FileMode.Open,
                                            FileAccess.Read, FileShare.Read, 64 * 1024))
            using (var dst = new FileStream(postingsPath, FileMode.Create,
                                            FileAccess.Write, FileShare.None,
                                            bufferSize: 4 * 1024 * 1024))
            {
                byte[] buf = new byte[64 * 1024];
                foreach (var kvp in _catalog)
                {
                    var chunk  = kvp.Value[0];
                    long newOff = dst.Position;

                    src.Seek(chunk.Offset, SeekOrigin.Begin);
                    int remaining = chunk.Length;
                    while (remaining > 0)
                    {
                        int read = src.Read(buf, 0, Math.Min(buf.Length, remaining));
                        dst.Write(buf, 0, read);
                        remaining -= read;
                    }

                    metaRows.Add((kvp.Key, newOff, chunk.Length, chunk.Count));
                }
            }

            WriteMetaDb(indexDbPath, metaRows);

            // Clean up segment file
            if (File.Exists(segFile)) File.Delete(segFile);
            _catalog.Clear();
        }

        // ── Helpers ──────────────────────────────────────────────────

        private void ForceMergeAll()
        {
            // Keep merging until only one segment remains across all levels
            bool merged;
            do
            {
                merged = false;
                for (int level = 0; level <= 1; level++)
                {
                    // Count distinct segment ids at this level
                    var ids = new HashSet<int>();
                    foreach (var list in _catalog.Values)
                        foreach (var c in list)
                            if (c.Level == level)
                                ids.Add(c.SegId);

                    if (ids.Count >= 2)
                    {
                        // Temporarily set _levelCount so MergeLevel triggers
                        _levelCount[level] = ids.Count;
                        MergeLevel(level);
                        merged = true;
                        break;
                    }
                }
            } while (merged);
        }

        private static void WriteMetaDb(string path,
            List<(string term, long offset, int length, int count)> rows)
        {
            var connStr = $"Data Source={path};Version=3;Page Size=65536;Cache Size=8000;";
            using (var conn = new System.Data.SQLite.SQLiteConnection(connStr))
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
                    var pT = ins.Parameters.Add("@t", System.Data.DbType.String);
                    var pO = ins.Parameters.Add("@o", System.Data.DbType.Int64);
                    var pL = ins.Parameters.Add("@l", System.Data.DbType.Int32);
                    var pC = ins.Parameters.Add("@c", System.Data.DbType.Int32);

                    foreach (var (term, off, len, cnt) in rows)
                    {
                        pT.Value = term;
                        pO.Value = off;
                        pL.Value = len;
                        pC.Value = cnt;
                        ins.ExecuteNonQuery();
                    }
                    tx.Commit();
                }

                Exec(conn,
                    "CREATE UNIQUE INDEX idx_term ON term_index (term);" +
                    "ANALYZE;");
            }
        }

        private string SegPath(int level, int segId) =>
            Path.Combine(_dir, $"seg_{level}_{segId}.dat");

        private static void Exec(System.Data.SQLite.SQLiteConnection conn, string sql)
        {
            using (var cmd = conn.CreateCommand())
            { cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        }

        // ── Varint helpers (no allocation) ───────────────────────────

        private static uint ReadVarInt(byte[] buf, ref int pos, int len)
        {
            int  shift  = 0;
            uint result = 0;
            while (pos < len)
            {
                byte b = buf[pos++];
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
            }
            return result;
        }

        private static void WriteVarInt(Stream s, uint v)
        {
            while (v >= 0x80)
            {
                s.WriteByte((byte)(v | 0x80));
                v >>= 7;
            }
            s.WriteByte((byte)v);
        }

        // ── ChunkRef ─────────────────────────────────────────────────

        internal readonly struct ChunkRef
        {
            public readonly int   Level;
            public readonly int   SegId;
            public readonly long  Offset;
            public readonly int   Length;
            public readonly int   Count;
            public readonly uint  LastEncoded;  // encoded value of last doc in chunk

            public ChunkRef(int level, int segId, long offset, int length,
                            int count, uint lastEncoded)
            {
                Level       = level;
                SegId       = segId;
                Offset      = offset;
                Length      = length;
                Count       = count;
                LastEncoded = lastEncoded;
            }
        }
    }
}
