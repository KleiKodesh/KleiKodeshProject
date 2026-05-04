using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Text;

namespace FtsLib.Core
{
    /// <summary>
    /// Manages sorted segment files and LSM-style merging.
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
    /// Because every segment is a sorted run, merging N segments is a
    /// straightforward N-way merge: advance the segment with the smallest
    /// current term, write it, repeat. Pure sequential I/O, zero seeks.
    ///
    /// LSM tiering: fanout = 4, levels grow as needed.
    /// </summary>
    internal sealed class SegmentStore
    {
        private const int FANOUT = 4;

        private readonly string _dir;
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

            using (var fs  = new FileStream(path, FileMode.Create,
                                            FileAccess.Write, FileShare.None,
                                            bufferSize: 4 * 1024 * 1024))
            using (var bw  = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: false))
            {
                foreach (var term in terms)
                {
                    var    entry    = ramIndex[term];
                    byte[] termBytes = Encoding.UTF8.GetBytes(term);
                    byte[] postBuf   = entry.Stream.Buffer;
                    int    postLen   = entry.Stream.ByteLength;
                    long   off       = fs.Position;

                    bw.Write(termBytes.Length);   // 4 bytes
                    bw.Write(termBytes);           // N bytes
                    bw.Write(postLen);             // 4 bytes
                    bw.Write(entry.Stream.Count);  // 4 bytes
                    bw.Write(entry.Stream.LastEncoded); // 4 bytes
                    bw.Write(postBuf, 0, postLen); // M bytes

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

            _levelCount[0]++;
            if (!_liveSegs.TryGetValue(0, out var ls0)) { ls0 = new HashSet<int>(); _liveSegs[0] = ls0; }
            ls0.Add(segId);
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

            // Collect segment ids at this level from _liveSegs (authoritative)
            if (!_liveSegs.TryGetValue(level, out var liveAtLevel) || liveAtLevel.Count < 2)
                return;
            var segIds = new List<int>(liveAtLevel);

            int termsAtLevel = 0;
            foreach (var list in _catalog.Values)
                foreach (var c in list)
                    if (c.Level == level) { termsAtLevel++; break; }

            Console.WriteLine(
                $"[Merger] L{level}→L{nextLevel} seg {newSegId}: " +
                $"{segIds.Count} segs, {termsAtLevel:N0} terms");

            string outPath = SegPath(nextLevel, newSegId);

            // Open all source segment readers
            var readers = new SegmentReader[segIds.Count];
            for (int i = 0; i < segIds.Count; i++)
                readers[i] = new SegmentReader(_segPath(level, segIds[i]));

            // Advance all readers to first entry
            for (int i = 0; i < readers.Length; i++)
                readers[i].MoveNext();

            var newEntries = new List<(string term, long outOff, int outLen, int count, uint lastEncoded)>(
                termsAtLevel);

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
                    // Find the smallest current term across all readers
                    string minTerm = null;
                    for (int i = 0; i < readers.Length; i++)
                    {
                        if (readers[i].Done) continue;
                        if (minTerm == null ||
                            string.CompareOrdinal(readers[i].CurrentTerm, minTerm) < 0)
                            minTerm = readers[i].CurrentTerm;
                    }
                    if (minTerm == null) break; // all exhausted

                    // Collect all chunks for minTerm into a temp buffer,
                    // then write header + data in one shot — no seek-back needed.
                    long outOff      = writePos;
                    int  totalCount  = 0;
                    uint prevEncoded = 0;
                    bool firstChunk  = true;

                    // Use a MemoryStream to accumulate the merged posting bytes
                    var termBuf = new System.IO.MemoryStream(256);

                    for (int i = 0; i < readers.Length; i++)
                    {
                        if (readers[i].Done) continue;
                        if (readers[i].CurrentTerm != minTerm) continue;

                        byte[] chunk    = readers[i].CurrentChunk;
                        int    chunkLen = readers[i].CurrentChunkLen;

                        if (firstChunk)
                        {
                            termBuf.Write(chunk, 0, chunkLen);
                            firstChunk = false;
                        }
                        else
                        {
                            // Re-encode first varint as delta from prevEncoded
                            int  pos          = 0;
                            uint firstEncoded = ReadVarInt(chunk, ref pos, chunkLen);
                            uint newDelta     = firstEncoded - prevEncoded;

                            byte[] hdr    = new byte[5];
                            int    hdrLen = EncodeVarInt(newDelta, hdr);
                            termBuf.Write(hdr, 0, hdrLen);

                            int restLen = chunkLen - pos;
                            if (restLen > 0)
                                termBuf.Write(chunk, pos, restLen);
                        }

                        prevEncoded = readers[i].CurrentLastEncoded;
                        totalCount += readers[i].CurrentCount;
                        readers[i].MoveNext();
                    }

                    // Write header + posting bytes — no seek, no placeholder
                    byte[] termBytes  = Encoding.UTF8.GetBytes(minTerm);
                    int    termOutLen = (int)termBuf.Length;
                    bw.Write(termBytes.Length);
                    bw.Write(termBytes);
                    bw.Write(termOutLen);
                    bw.Write(totalCount);
                    bw.Write(prevEncoded);
                    bw.Flush();
                    byte[] postBytes = termBuf.GetBuffer();
                    outFs.Write(postBytes, 0, termOutLen);

                    writePos += 4 + termBytes.Length + 4 + 4 + 4 + termOutLen;
                    newEntries.Add((minTerm, outOff, termOutLen, totalCount, prevEncoded));

                    termsWritten++;
                    if (termsWritten % REPORT_EVERY == 0)
                        Console.WriteLine(
                            $"[Merger]   L{level}→L{nextLevel}: " +
                            $"{termsWritten:N0}/{termsAtLevel:N0} " +
                            $"({termsWritten * 100 / termsAtLevel}%)  " +
                            $"{writePos / 1024 / 1024:N0} MB");
                }
            }

            // Close and delete source files
            for (int i = 0; i < readers.Length; i++) readers[i].Dispose();
            foreach (int sid in segIds)
            {
                string sp = _segPath(level, sid);
                if (File.Exists(sp)) File.Delete(sp);
            }

            // Update _liveSegs: remove source segs, add new seg
            liveAtLevel.ExceptWith(segIds);
            if (!_liveSegs.TryGetValue(nextLevel, out var liveNext))
            { liveNext = new HashSet<int>(); _liveSegs[nextLevel] = liveNext; }
            liveNext.Add(newSegId);

            // Update catalog — replace all level-N entries with the new merged entry
            foreach (var (term, outOff, outLen, count, lastEncoded) in newEntries)
            {
                var list = _catalog[term];
                for (int i = list.Count - 1; i >= 0; i--)
                    if (list[i].Level == level) list.RemoveAt(i);
                list.Insert(0, new ChunkRef(nextLevel, newSegId, outOff, outLen, count, lastEncoded));
            }

            // Safety sweep: remove any remaining stale entries at this level
            // (should not happen, but guards against catalog/merge inconsistency)
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

            if (File.Exists(postingsPath)) File.Delete(postingsPath);
            if (File.Exists(metaDbPath))   File.Delete(metaDbPath);

            // Find the single surviving segment from _liveSegs
            int aliveLevel = -1, aliveSegId = -1;
            foreach (var kv in _liveSegs)
            {
                if (kv.Value.Count == 1)
                {
                    aliveLevel = kv.Key;
                    foreach (int sid in kv.Value) aliveSegId = sid;
                    break;
                }
            }

            if (aliveLevel < 0)
            {
                File.WriteAllBytes(postingsPath, new byte[0]);
                WriteMetaDb(metaDbPath, new List<(string, long, int, int)>());
                return;
            }

            string segFile = _segPath(aliveLevel, aliveSegId);
            long   sizeMb  = new FileInfo(segFile).Length / 1024 / 1024;
            Console.WriteLine($"[SegmentStore] Reading final segment ({sizeMb:N0} MB) → postings.dat + Meta.db...");

            // Read the sorted segment and write postings.dat + Meta.db in one pass
            var meta = new List<(string term, long offset, int length, int count)>(_catalog.Count);

            using (var reader  = new SegmentReader(segFile))
            using (var postFs  = new FileStream(postingsPath, FileMode.Create,
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

            File.Delete(segFile);

            Console.WriteLine($"[SegmentStore] Writing Meta.db ({meta.Count:N0} terms)...");
            WriteMetaDb(metaDbPath, meta);
            Console.WriteLine($"[SegmentStore] Done.");
            _catalog.Clear();
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
                    { pT.Value=term; pO.Value=off; pL.Value=len; pC.Value=cnt; ins.ExecuteNonQuery(); }
                    tx.Commit();
                }
                Exec(conn, "CREATE UNIQUE INDEX idx_term ON term_index(term);ANALYZE;");
            }
        }

        private static void Exec(SQLiteConnection conn, string sql)
        { using (var cmd = conn.CreateCommand()) { cmd.CommandText = sql; cmd.ExecuteNonQuery(); } }

        // ── Path helpers ─────────────────────────────────────────────

        private string _segPath(int level, int segId) => SegPath(level, segId);
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
        { Level=level; SegId=segId; Offset=offset; Length=length; Count=count; LastEncoded=lastEncoded; }
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
