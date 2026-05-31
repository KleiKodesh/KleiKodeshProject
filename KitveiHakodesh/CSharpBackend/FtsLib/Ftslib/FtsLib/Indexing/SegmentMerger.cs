using FtsLib.Search;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FtsLib.Indexing
{
    /// <summary>
    /// Handles LSM-style segment merging.
    /// Called by SegmentStore when a level reaches FANOUT segments.
    /// </summary>
    internal sealed class SegmentMerger
    {
        private readonly SegmentStore _store;

        public SegmentMerger(SegmentStore store)
        {
            _store = store;
        }

        // ── Single-segment rewrite (used by Purge) ───────────────────

        /// <summary>
        /// Rewrites a single segment in-place, physically removing all doc IDs in
        /// <paramref name="deletes"/>. Writes to .tmp files first, then atomically
        /// renames over the originals. The caller is responsible for deleting the
        /// .del file and removing the in-memory delete set after this returns.
        /// </summary>
        internal void RewriteSegment(int level, int segId, DeleteSet deletes)
        {
            string datPath  = _store.Live.SegDatPath(level, segId);
            string dbPath   = _store.Live.SegDbPath(level, segId);
            string metaPath = _store.Live.SegMetaPath(level, segId);

            string tmpDat  = datPath  + ".tmp";
            string tmpDb   = dbPath   + ".tmp";
            string tmpMeta = metaPath + ".tmp";

            DeleteIfExists(tmpDat);
            DeleteIfExists(tmpDb);
            DeleteIfExists(tmpMeta);

            // Open a single reader for this segment.
            var reader = new SegmentReader(datPath);
            reader.MoveNext();

            var readerDeletes = new DeleteSet[] { deletes };
            var readers       = new SegmentReader[] { reader };

            int mergedMin, mergedMax;
            byte[] mergeBuffer = new byte[256];

            var entries = new List<(string term, long skipOffset, int skipCount, long offset, int length, int count)>();

            using (var outFs = new FileStream(tmpDat, FileMode.Create,
                                              FileAccess.Write, FileShare.None,
                                              bufferSize: 4 * 1024 * 1024))
            using (var bw = new System.IO.BinaryWriter(outFs, System.Text.Encoding.UTF8, leaveOpen: false))
            {
                mergedMin = int.MaxValue;
                mergedMax = int.MinValue;

                while (true)
                {
                    string term = FindMinTerm(readers);
                    if (term == null) break;

                    int mergedLen, totalCount, termMin, termMax;
                    uint lastEncoded;
                    int[] skipTable;
                    int   skipLen;

                    MergeChunks(readers, readerDeletes, term,
                                ref mergeBuffer, out mergedLen, out totalCount, out lastEncoded,
                                out skipTable, out skipLen, out termMin, out termMax);

                    if (totalCount == 0) continue;

                    if (termMin < mergedMin) mergedMin = termMin;
                    if (termMax > mergedMax) mergedMax = termMax;

                    int    termByteLen = System.Text.Encoding.UTF8.GetByteCount(term);
                    byte[] termBytes   = System.Buffers.ArrayPool<byte>.Shared.Rent(termByteLen);
                    System.Text.Encoding.UTF8.GetBytes(term, 0, term.Length, termBytes, 0);
                    int skipCount = skipLen / 3;

                    bw.Write(termByteLen);
                    bw.Write(termBytes, 0, termByteLen);
                    bw.Write(mergedLen);
                    bw.Write(totalCount);
                    bw.Write(lastEncoded);
                    bw.Write(skipCount);
                    bw.Flush();

                    long skipOff = outFs.Position;
                    for (int i = 0; i < skipLen; i++) bw.Write(skipTable[i]);
                    bw.Flush();

                    long postOff = outFs.Position;
                    outFs.Write(mergeBuffer, 0, mergedLen);

                    entries.Add((term, skipOff, skipCount, postOff, mergedLen, totalCount));
                    System.Buffers.ArrayPool<byte>.Shared.Return(termBytes);
                }
            }

            reader.Dispose();

            SegmentWriter.WriteMetaDb(tmpDb, entries);

            if (mergedMin <= mergedMax)
                SegmentWriter.WriteMetaFile(tmpMeta, mergedMin, mergedMax);

            // Atomic swap — replace originals with the rewritten files.
            DeleteIfExists(datPath);
            File.Move(tmpDat, datPath);

            DeleteIfExists(dbPath);
            DeleteIfExists(dbPath + "-shm");
            DeleteIfExists(dbPath + "-wal");
            File.Move(tmpDb, dbPath);

            if (mergedMin <= mergedMax)
            {
                DeleteIfExists(metaPath);
                File.Move(tmpMeta, metaPath);
            }

            // Update the range in live state (may have shrunk after purge).
            if (mergedMin <= mergedMax)
                _store.Live.SetSegmentRange(segId, mergedMin, mergedMax);
        }

        // ── Cascade ──────────────────────────────────────────────────

        public void MergeIfNeeded(int level)
        {
            if (_store.Live.LiveSegCount(level) < SegmentStore.Fanout) return;
            _store.Live.EnsureLevel(level + 1);
            MergeLevel(level);
            MergeIfNeeded(level + 1);
        }

        // ── Core merge ───────────────────────────────────────────────

        public void MergeLevel(int level)
        {
            MergeLevel(level, targetSegId: null);
        }

        public void MergeLevel(int level, int? targetSegId)
        {
            var segIds = _store.Live.GetLiveSegIds(level);
            if (segIds.Count < 2) return;

            int    newSegId  = targetSegId ?? _store.Live.NextSegId();
            int    nextLevel = level + 1;
            string outDat    = _store.Live.SegDatPath(nextLevel, newSegId);
            string outDb     = _store.Live.SegDbPath(nextLevel, newSegId);
            string outMeta   = _store.Live.SegMetaPath(nextLevel, newSegId);

            string tmpDat  = outDat  + ".tmp";
            string tmpDb   = outDb   + ".tmp";

            Console.WriteLine($"[Merger] L{level}→L{nextLevel} seg {newSegId}: {segIds.Count} segs");

            _store.Wal.BeginMerge(level, segIds.ToArray(), newSegId);

            DeleteIfExists(tmpDat);
            DeleteIfExists(tmpDb);

            var readers = OpenReaders(level, segIds);

            // Collect per-reader delete sets (null = no deletions for that segment).
            var readerDeletes = new DeleteSet[readers.Length];
            for (int i = 0; i < segIds.Count; i++)
                readerDeletes[i] = _store.GetDeleteSet(segIds[i]);

            int mergedMin, mergedMax;
            var entries = WriteMergedDat(level, nextLevel, readers, readerDeletes,
                                         tmpDat, out mergedMin, out mergedMax);
            CloseReaders(readers);

            SegmentWriter.WriteMetaDb(tmpDb, entries);

            File.Move(tmpDat, outDat);
            File.Move(tmpDb,  outDb);

            // Write the merged segment's .meta file.
            if (mergedMin <= mergedMax)
                SegmentWriter.WriteMetaFile(outMeta, mergedMin, mergedMax);

            // Register the target segment as live BEFORE deleting sources.
            _store.Wal.EndMerge(level, newSegId);
            _store.Live.PromoteSegment(level, segIds, nextLevel, newSegId);
            if (mergedMin <= mergedMax)
                _store.Live.SetSegmentRange(newSegId, mergedMin, mergedMax);

            // Delete the source segments. Search is blocked for the duration of this
            // merge (SegmentStore holds the write lock on _searchMergeLock), so no
            // reader can have these files open — plain File.Delete is safe.
            foreach (int sid in segIds)
            {
                DeleteIfExists(_store.Live.SegDatPath(level, sid));
                DeleteIfExists(_store.Live.SegDbPath(level, sid));
                DeleteIfExists(_store.Live.SegDbPath(level, sid) + "-shm");
                DeleteIfExists(_store.Live.SegDbPath(level, sid) + "-wal");
                // Delete per-segment delete and meta files — deletions were physically
                // purged during the merge, so the merged segment starts clean.
                DeleteIfExists(_store.Live.SegDelPath(level, sid));
                DeleteIfExists(_store.Live.SegMetaPath(level, sid));
                _store.RemoveDeleteSet(sid);
            }

            Console.WriteLine($"[Merger] Done → L{nextLevel} seg {newSegId} ({entries.Count:N0} terms)");
        }

        // ── Merge write ──────────────────────────────────────────────

        private List<(string term, long skipOffset, int skipCount, long offset, int length, int count)> WriteMergedDat(
            int srcLevel, int dstLevel,
            SegmentReader[] readers,
            DeleteSet[]     readerDeletes,
            string outPath,
            out int mergedMin, out int mergedMax)
        {
            var entries = new List<(string, long, int, long, int, int)>();
            int  written  = 0;
            mergedMin = int.MaxValue;
            mergedMax = int.MinValue;

            // Reusable merge buffer — grown as needed, never shrunk.
            // Avoids one MemoryStream allocation per term (1.4M+ over a full merge).
            byte[] mergeBuffer = new byte[256];

            using (var outFs = new FileStream(outPath, FileMode.Create,
                                              FileAccess.Write, FileShare.None,
                                              bufferSize: 4 * 1024 * 1024))
            using (var bw = new BinaryWriter(outFs, Encoding.UTF8, leaveOpen: false))
            {
                while (true)
                {
                    string minTerm = FindMinTerm(readers);
                    if (minTerm == null) break;

                    int mergedLen;
                    int totalCount;
                    uint lastEncoded;
                    int[] skipTable;
                    int   skipLen;
                    int   termMin, termMax;
                    MergeChunks(readers, readerDeletes, minTerm,
                                ref mergeBuffer, out mergedLen, out totalCount, out lastEncoded,
                                out skipTable, out skipLen, out termMin, out termMax);

                    // Skip terms whose entire posting list was purged
                    if (totalCount == 0) continue;

                    // Accumulate the overall doc-ID range for the merged segment.
                    if (termMin < mergedMin) mergedMin = termMin;
                    if (termMax > mergedMax) mergedMax = termMax;

                    int    termByteLen = Encoding.UTF8.GetByteCount(minTerm);
                    byte[] termBytes   = ArrayPool<byte>.Shared.Rent(termByteLen);
                    Encoding.UTF8.GetBytes(minTerm, 0, minTerm.Length, termBytes, 0);
                    int    chunkLen    = mergedLen;
                    int    skipCount   = skipLen / 3;

                    bw.Write(termByteLen);
                    bw.Write(termBytes, 0, termByteLen);
                    bw.Write(chunkLen);
                    bw.Write(totalCount);
                    bw.Write(lastEncoded);
                    bw.Write(skipCount);
                    bw.Flush();

                    long skipOff = outFs.Position;
                    for (int i = 0; i < skipLen; i++)
                        bw.Write(skipTable[i]);
                    bw.Flush();

                    long outOff = outFs.Position; // offset of posting data
                    outFs.Write(mergeBuffer, 0, chunkLen);

                    entries.Add((minTerm, skipOff, skipCount, outOff, chunkLen, totalCount));

                    ArrayPool<byte>.Shared.Return(termBytes);

                    written++;
                }
            }

            return entries;
        }

        // ── Chunk merge ──────────────────────────────────────────────

        /// <summary>
        /// Merges posting chunks for <paramref name="term"/> from all readers into
        /// <paramref name="buf"/>, growing it as needed. Each reader uses its own
        /// <paramref name="readerDeletes"/> entry (null = no deletions for that reader).
        /// Writes <paramref name="mergedLen"/> bytes starting at index 0.
        /// Avoids per-term heap allocation. Rebuilds the skip table from the final
        /// merged bytes after writing. Tracks the min/max doc ID surviving the merge.
        /// </summary>
        private static void MergeChunks(
            SegmentReader[] readers, DeleteSet[] readerDeletes, string term,
            ref byte[] buf, out int mergedLen, out int totalCount, out uint lastEncoded,
            out int[] skipTable, out int skipLen, out int minDocId, out int maxDocId)
        {
            const int SkipInterval = 128;

            uint prevEncoded = 0;
            totalCount  = 0;
            lastEncoded = 0;
            minDocId    = int.MaxValue;
            maxDocId    = int.MinValue;
            bool firstChunk = true;
            int  pos        = 0; // write cursor into buf

            for (int ri = 0; ri < readers.Length; ri++)
            {
                var r       = readers[ri];
                var deletes = readerDeletes[ri]; // null if this segment has no deletions

                if (r.Done || r.CurrentTerm != term) continue;

                byte[] chunk    = r.CurrentChunk;
                int    chunkLen = r.CurrentChunkLen;

                if (deletes == null || deletes.IsEmpty)
                {
                    // Fast path: no deletions — copy chunks verbatim.
                    // We still need to decode to track min/max doc IDs.
                    if (firstChunk)
                    {
                        EnsureCapacity(ref buf, pos + chunkLen);
                        Buffer.BlockCopy(chunk, 0, buf, pos, chunkLen);
                        pos       += chunkLen;
                        firstChunk = false;
                    }
                    else
                    {
                        // Re-encode the first delta relative to the previous chunk's last value.
                        int    readPos        = 0;
                        uint   firstEncoded2  = VarInt.Read(chunk, ref readPos, chunkLen);
                        uint   newDelta       = firstEncoded2 - prevEncoded;
                        byte[] hdr            = new byte[5];
                        int    hdrLen         = VarInt.Encode(newDelta, hdr);
                        int    rest           = chunkLen - readPos;
                        EnsureCapacity(ref buf, pos + hdrLen + rest);
                        Buffer.BlockCopy(hdr, 0, buf, pos, hdrLen);
                        pos += hdrLen;
                        if (rest > 0)
                        {
                            Buffer.BlockCopy(chunk, readPos, buf, pos, rest);
                            pos += rest;
                        }
                    }

                    // Track min/max: first doc in this chunk and last doc.
                    {
                        int rp = 0;
                        uint enc = 0;
                        // Decode first doc ID.
                        enc += VarInt.Read(chunk, ref rp, chunkLen);
                        int firstDoc = (int)((long)enc + int.MinValue);
                        if (firstDoc < minDocId) minDocId = firstDoc;
                        // Last doc ID is encoded in CurrentLastEncoded.
                        int lastDoc = (int)((long)r.CurrentLastEncoded + int.MinValue);
                        if (lastDoc > maxDocId) maxDocId = lastDoc;
                    }

                    prevEncoded  = r.CurrentLastEncoded;
                    totalCount  += r.CurrentCount;
                }
                else
                {
                    // Purge path: decode every doc ID and skip deleted ones.
                    int  readPos = 0;
                    uint encoded = 0;
                    var  tmp     = new byte[5];

                    while (readPos < chunkLen)
                    {
                        uint delta = VarInt.Read(chunk, ref readPos, chunkLen);
                        encoded   += delta;
                        int docId  = (int)((long)encoded + int.MinValue);

                        if (deletes.Contains(docId)) continue;

                        uint outDelta = firstChunk ? encoded : encoded - prevEncoded;
                        int  nBytes   = VarInt.Encode(outDelta, tmp);
                        EnsureCapacity(ref buf, pos + nBytes);
                        Buffer.BlockCopy(tmp, 0, buf, pos, nBytes);
                        pos += nBytes;

                        prevEncoded = encoded;
                        totalCount++;
                        firstChunk = false;

                        if (docId < minDocId) minDocId = docId;
                        if (docId > maxDocId) maxDocId = docId;
                    }
                }

                r.MoveNext();
            }

            mergedLen   = pos;
            lastEncoded = prevEncoded;

            // Rebuild skip table by decoding the final merged bytes.
            // Byte offsets change after merge so we can never copy the source skip tables.
            skipTable = null;
            skipLen   = 0;

            if (totalCount >= SkipInterval * 2)
            {
                int  readPos   = 0;
                uint encoded   = 0;
                uint prevEnc   = 0;
                int  docIndex  = 0;

                while (readPos < mergedLen)
                {
                    int  byteOffsetBefore = readPos;
                    uint delta            = VarInt.Read(buf, ref readPos, mergedLen);
                    prevEnc  = encoded;
                    encoded += delta;
                    int docId = (int)((long)encoded + int.MinValue);
                    docIndex++;

                    // Emit a skip entry after every SkipInterval-th doc (not the very first).
                    if (docIndex > 1 && (docIndex - 1) % SkipInterval == 0)
                    {
                        if (skipTable == null) skipTable = new int[12];
                        else if (skipLen + 3 > skipTable.Length)
                            Array.Resize(ref skipTable, skipTable.Length * 2);

                        skipTable[skipLen]     = docId;
                        skipTable[skipLen + 1] = byteOffsetBefore;
                        skipTable[skipLen + 2] = (int)prevEnc;
                        skipLen += 3;
                    }
                }
            }
        }

        private static void EnsureCapacity(ref byte[] buf, int required)
        {
            if (required <= buf.Length) return;
            int newSize = buf.Length;
            while (newSize < required) newSize *= 2;
            Array.Resize(ref buf, newSize);
        }

        // ── Helpers ──────────────────────────────────────────────────

        private SegmentReader[] OpenReaders(int level, List<int> segIds)
        {
            var readers = new SegmentReader[segIds.Count];
            for (int i = 0; i < segIds.Count; i++)
                readers[i] = new SegmentReader(_store.Live.SegDatPath(level, segIds[i]));
            for (int i = 0; i < readers.Length; i++)
                readers[i].MoveNext();
            return readers;
        }

        private static void CloseReaders(SegmentReader[] readers)
        {
            foreach (var r in readers) r.Dispose();
        }

        private static string FindMinTerm(SegmentReader[] readers)
        {
            string min = null;
            foreach (var r in readers)
            {
                if (r.Done) continue;
                if (min == null || string.CompareOrdinal(r.CurrentTerm, min) < 0)
                    min = r.CurrentTerm;
            }
            return min;
        }

        private void EnsureLevel(int level) => _store.Live.EnsureLevel(level);

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
