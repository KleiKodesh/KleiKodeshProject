using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FtsLib.Core
{
    /// <summary>
    /// Handles LSM-style segment merging.
    /// Called by SegmentStore when a level reaches FANOUT segments.
    /// Also exposes ForceMergeAll for use at commit / optimize time.
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

        public void ForceMergeAll()
        {
            int total = _store.Live.TotalLiveSegs();
            Console.WriteLine($"[SegmentStore] Force-merge: {total} segment(s)");

            bool progress;
            do
            {
                progress = false;
                int level = _store.Live.FindLevelWithMultiple();
                if (level >= 0)
                {
                    _store.Live.EnsureLevel(level + 1);
                    MergeLevel(level);
                    progress = true;
                }
            } while (progress);

            Console.WriteLine("[SegmentStore] Force-merge complete.");
        }

        // ── Core merge ───────────────────────────────────────────────

        public void MergeLevel(int level)
        {
            var segIds = _store.Live.GetLiveSegIds(level);
            if (segIds.Count < 2) return;

            int    newSegId  = _store.Live.NextSegId();
            int    nextLevel = level + 1;
            string outDat    = _store.Live.SegDatPath(nextLevel, newSegId);
            string outDb     = _store.Live.SegDbPath(nextLevel, newSegId);

            string tmpDat = outDat + ".tmp";
            string tmpDb  = outDb  + ".tmp";

            Console.WriteLine($"[Merger] L{level}→L{nextLevel} seg {newSegId}: {segIds.Count} segs");

            _store.Wal.BeginMerge(level, segIds.ToArray(), newSegId);

            DeleteIfExists(tmpDat);
            DeleteIfExists(tmpDb);

            var readers = OpenReaders(level, segIds);
            var entries = WriteMergedDat(level, nextLevel, readers, tmpDat);
            CloseReaders(readers);

            SegmentWriter.WriteMetaDb(tmpDb, entries);

            File.Move(tmpDat, outDat);
            File.Move(tmpDb,  outDb);

            _store.Wal.EndMerge(level, newSegId);

            foreach (int sid in segIds)
            {
                DeleteIfExists(_store.Live.SegDatPath(level, sid));
                DeleteIfExists(_store.Live.SegDbPath(level, sid));
            }

            _store.Live.PromoteSegment(level, segIds, nextLevel, newSegId);

            Console.WriteLine($"[Merger] Done → L{nextLevel} seg {newSegId} ({entries.Count:N0} terms)");
        }

        // ── Merge write ──────────────────────────────────────────────

        private List<(string term, long offset, int length, int count)> WriteMergedDat(
            int srcLevel, int dstLevel,
            SegmentReader[] readers,
            string outPath)
        {
            var entries = new List<(string, long, int, int)>();
            const int REPORT_EVERY = 10_000;
            int  written  = 0;
            long writePos = 0;

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
                    MergeChunks(readers, minTerm, _store.GetDeleteSet(),
                                ref mergeBuffer, out mergedLen, out totalCount, out lastEncoded);

                    // Skip terms whose entire posting list was purged
                    if (totalCount == 0) continue;

                    int    termByteLen = Encoding.UTF8.GetByteCount(minTerm);
                    byte[] termBytes   = ArrayPool<byte>.Shared.Rent(termByteLen);
                    Encoding.UTF8.GetBytes(minTerm, 0, minTerm.Length, termBytes, 0);
                    int    chunkLen    = mergedLen;

                    bw.Write(termByteLen);
                    bw.Write(termBytes, 0, termByteLen);
                    bw.Write(chunkLen);
                    bw.Write(totalCount);
                    bw.Write(lastEncoded);
                    bw.Flush();

                    long outOff = outFs.Position; // offset of posting data, after the header
                    outFs.Write(mergeBuffer, 0, chunkLen);

                    writePos += 4 + termByteLen + 4 + 4 + 4 + chunkLen;
                    entries.Add((minTerm, outOff, chunkLen, totalCount));

                    ArrayPool<byte>.Shared.Return(termBytes);

                    written++;
                    if (written % REPORT_EVERY == 0)
                        Console.WriteLine($"[Merger]   L{srcLevel}→L{dstLevel}: {written:N0} terms  {writePos / 1024 / 1024:N0} MB");
                }
            }

            return entries;
        }

        // ── Chunk merge ──────────────────────────────────────────────

        /// <summary>
        /// Merges posting chunks for <paramref name="term"/> from all readers into
        /// <paramref name="buf"/>, growing it as needed. Writes <paramref name="mergedLen"/>
        /// bytes starting at index 0. Avoids per-term heap allocation.
        /// </summary>
        private static void MergeChunks(
            SegmentReader[] readers, string term, DeleteSet deletes,
            ref byte[] buf, out int mergedLen, out int totalCount, out uint lastEncoded)
        {
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
