using System;
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
            if (_store.LiveSegCount(level) < SegmentStore.Fanout) return;
            EnsureLevel(level + 1);
            MergeLevel(level);
            MergeIfNeeded(level + 1);
        }

        public void ForceMergeAll()
        {
            int total = _store.TotalLiveSegs();
            Console.WriteLine($"[SegmentStore] Force-merge: {total} segment(s)");

            bool progress;
            do
            {
                progress = false;
                int level = _store.FindLevelWithMultiple();
                if (level >= 0)
                {
                    EnsureLevel(level + 1);
                    MergeLevel(level);
                    progress = true;
                }
            } while (progress);

            Console.WriteLine("[SegmentStore] Force-merge complete.");
        }

        // ── Core merge ───────────────────────────────────────────────

        public void MergeLevel(int level)
        {
            var segIds = _store.GetLiveSegIds(level);
            if (segIds.Count < 2) return;

            int    newSegId  = _store.NextSegId();
            int    nextLevel = level + 1;
            string outDat    = _store.SegDatPath(nextLevel, newSegId);
            string outDb     = _store.SegDbPath(nextLevel, newSegId);

            Console.WriteLine($"[Merger] L{level}→L{nextLevel} seg {newSegId}: {segIds.Count} segs");

            _store.Wal.BeginMerge(level, segIds.ToArray(), newSegId);

            var readers = OpenReaders(level, segIds);
            var entries = WriteMergedDat(level, nextLevel, readers, outDat);
            CloseReaders(readers);

            SegmentStore.WriteMetaDb(outDb, entries);

            _store.Wal.EndMerge(level, newSegId);

            // Delete source files — safe now that END_MERGE is logged
            foreach (int sid in segIds)
            {
                DeleteIfExists(_store.SegDatPath(level, sid));
                DeleteIfExists(_store.SegDbPath(level, sid));
            }

            _store.PromoteSegment(level, segIds, nextLevel, newSegId);

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

            using (var outFs = new FileStream(outPath, FileMode.Create,
                                              FileAccess.Write, FileShare.None,
                                              bufferSize: 4 * 1024 * 1024))
            using (var bw = new BinaryWriter(outFs, Encoding.UTF8, leaveOpen: false))
            {
                while (true)
                {
                    string minTerm = FindMinTerm(readers);
                    if (minTerm == null) break;

                    var (mergedChunk, totalCount, lastEncoded) = MergeChunks(readers, minTerm, _store.GetDeleteSet());

                    // Skip terms whose entire posting list was purged
                    if (totalCount == 0) continue;

                    byte[] termBytes  = Encoding.UTF8.GetBytes(minTerm);
                    int    chunkLen   = mergedChunk.Length;

                    bw.Write(termBytes.Length);
                    bw.Write(termBytes);
                    bw.Write(chunkLen);
                    bw.Write(totalCount);
                    bw.Write(lastEncoded);
                    bw.Flush();

                    long outOff = outFs.Position; // offset of posting data, after the header
                    outFs.Write(mergedChunk, 0, chunkLen);

                    writePos += 4 + termBytes.Length + 4 + 4 + 4 + chunkLen;
                    entries.Add((minTerm, outOff, chunkLen, totalCount));

                    written++;
                    if (written % REPORT_EVERY == 0)
                        Console.WriteLine($"[Merger]   L{srcLevel}→L{dstLevel}: {written:N0} terms  {writePos / 1024 / 1024:N0} MB");
                }
            }

            return entries;
        }

        // ── Chunk merge ──────────────────────────────────────────────

        private static (byte[] chunk, int count, uint lastEncoded) MergeChunks(
            SegmentReader[] readers, string term, DeleteSet deletes)
        {
            uint prevEncoded = 0;
            int  totalCount  = 0;
            bool firstChunk  = true;
            var  buf         = new MemoryStream(256);

            foreach (var r in readers)
            {
                if (r.Done || r.CurrentTerm != term) continue;

                byte[] chunk    = r.CurrentChunk;
                int    chunkLen = r.CurrentChunkLen;

                if (deletes == null || deletes.IsEmpty)
                {
                    // Fast path: no deletions — copy chunks verbatim (original behaviour)
                    if (firstChunk)
                    {
                        buf.Write(chunk, 0, chunkLen);
                        firstChunk = false;
                    }
                    else
                    {
                        int  pos          = 0;
                        uint firstEncoded = VarInt.Read(chunk, ref pos, chunkLen);
                        uint newDelta     = firstEncoded - prevEncoded;
                        byte[] hdr        = new byte[5];
                        int    hdrLen     = VarInt.Encode(newDelta, hdr);
                        buf.Write(hdr, 0, hdrLen);
                        int rest = chunkLen - pos;
                        if (rest > 0) buf.Write(chunk, pos, rest);
                    }

                    prevEncoded = r.CurrentLastEncoded;
                    totalCount += r.CurrentCount;
                }
                else
                {
                    // Purge path: decode every doc ID and skip deleted ones
                    int  pos     = 0;
                    uint encoded = 0;
                    var  tmp     = new byte[5];

                    while (pos < chunkLen)
                    {
                        uint delta = VarInt.Read(chunk, ref pos, chunkLen);
                        encoded   += delta;
                        int docId  = (int)((long)encoded + int.MinValue);

                        if (deletes.Contains(docId)) continue;

                        uint outDelta = firstChunk ? encoded : encoded - prevEncoded;
                        int  nBytes   = VarInt.Encode(outDelta, tmp);
                        buf.Write(tmp, 0, nBytes);

                        prevEncoded = encoded;
                        totalCount++;
                        firstChunk = false;
                    }
                }

                r.MoveNext();
            }

            return (buf.ToArray(), totalCount, prevEncoded);
        }

        // ── Helpers ──────────────────────────────────────────────────

        private SegmentReader[] OpenReaders(int level, List<int> segIds)
        {
            var readers = new SegmentReader[segIds.Count];
            for (int i = 0; i < segIds.Count; i++)
                readers[i] = new SegmentReader(_store.SegDatPath(level, segIds[i]));
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

        private void EnsureLevel(int level) => _store.EnsureLevel(level);

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }


    }
}
