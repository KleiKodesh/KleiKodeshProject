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

            string tmpDat = outDat + ".tmp";
            string tmpDb  = outDb  + ".tmp";

            Console.WriteLine($"[Merger] L{level}→L{nextLevel} seg {newSegId}: {segIds.Count} segs");
            FtsLog.Write("SegmentMerger",
                $"START L{level}→L{nextLevel} target=seg_{nextLevel}_{newSegId} sources=[{string.Join(",", segIds)}]");

            _store.Wal.BeginMerge(level, segIds.ToArray(), newSegId);
            FtsLog.Write("SegmentMerger", "WAL BEGIN_MERGE written");

            DeleteIfExists(tmpDat);
            DeleteIfExists(tmpDb);

            var readers = OpenReaders(level, segIds);
            FtsLog.Write("SegmentMerger", $"merging dat → {System.IO.Path.GetFileName(tmpDat)}");
            var entries = WriteMergedDat(level, nextLevel, readers, tmpDat);
            CloseReaders(readers);

            SegmentWriter.WriteMetaDb(tmpDb, entries);
            FtsLog.Write("SegmentMerger", $"tmp files written ({entries.Count:N0} terms) — renaming to final");

            File.Move(tmpDat, outDat);
            File.Move(tmpDb,  outDb);
            FtsLog.Write("SegmentMerger", "target files renamed to final paths");

            // Delete source segments BEFORE writing END_MERGE.
            //
            // Crash-safety ordering:
            //   1. target files renamed to final path  (target exists, sources exist)
            //   2. source files deleted                (target exists, sources gone)
            //   3. END_MERGE written to WAL            (no pending merge on next boot)
            //   4. live state updated in memory
            //
            // If the process crashes between steps 2 and 3, recovery sees a pending
            // BEGIN_MERGE with target present but sources gone → Case B in SegmentStore:
            // registers the complete target and writes END_MERGE. Correct.
            //
            // If the process crashes between steps 3 and 4, recovery sees no pending
            // merge (END_MERGE was written), RebuildFromDisk finds only the target
            // on disk (sources already deleted). Correct — no duplicate data.
            //
            // Search is blocked for the entire duration of this merge (SegmentStore
            // holds the write lock on _searchMergeLock), so no reader can have the
            // source files open — plain File.Delete is safe.
            FtsLog.Write("SegmentMerger",
                $"deleting {segIds.Count} source segment(s): [{string.Join(",", segIds)}]");
            foreach (int sid in segIds)
            {
                DeleteIfExists(_store.Live.SegDatPath(level, sid));
                DeleteIfExists(_store.Live.SegDbPath(level, sid));
                DeleteIfExists(_store.Live.SegDbPath(level, sid) + "-shm");
                DeleteIfExists(_store.Live.SegDbPath(level, sid) + "-wal");
            }
            FtsLog.Write("SegmentMerger", "source segments deleted");

            _store.Wal.EndMerge(level, newSegId);
            FtsLog.Write("SegmentMerger", "WAL END_MERGE written");

            _store.Live.PromoteSegment(level, segIds, nextLevel, newSegId);

            Console.WriteLine($"[Merger] Done → L{nextLevel} seg {newSegId} ({entries.Count:N0} terms)");
            FtsLog.Write("SegmentMerger",
                $"DONE L{level}→L{nextLevel} seg_{nextLevel}_{newSegId} ({entries.Count:N0} terms)");
        }

        // ── Merge write ──────────────────────────────────────────────

        private List<(string term, long skipOffset, int skipCount, long offset, int length, int count)> WriteMergedDat(
            int srcLevel, int dstLevel,
            SegmentReader[] readers,
            string outPath)
        {
            var entries = new List<(string, long, int, long, int, int)>();
            int  written  = 0;

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
                    MergeChunks(readers, minTerm, _store.GetDeleteSet(),
                                ref mergeBuffer, out mergedLen, out totalCount, out lastEncoded,
                                out skipTable, out skipLen);

                    // Skip terms whose entire posting list was purged
                    if (totalCount == 0) continue;

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
        /// <paramref name="buf"/>, growing it as needed. Writes <paramref name="mergedLen"/>
        /// bytes starting at index 0. Avoids per-term heap allocation.
        /// Rebuilds the skip table from the final merged bytes after writing.
        /// </summary>
        private static void MergeChunks(
            SegmentReader[] readers, string term, DeleteSet deletes,
            ref byte[] buf, out int mergedLen, out int totalCount, out uint lastEncoded,
            out int[] skipTable, out int skipLen)
        {
            const int SkipInterval = 128;

            uint prevEncoded = 0;
            totalCount  = 0;
            lastEncoded = 0;
            bool firstChunk = true;
            int  pos        = 0; // write cursor into buf

            foreach (var r in readers)
            {
                if (r.Done || r.CurrentTerm != term) continue;

                byte[] chunk    = r.CurrentChunk;
                int    chunkLen = r.CurrentChunkLen;

                if (deletes == null || deletes.IsEmpty)
                {
                    // Fast path: no deletions — copy chunks verbatim.
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
