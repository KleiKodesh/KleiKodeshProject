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
            int count = _store.Live.LiveSegCount(level);
            FtsLog.Write("SegmentMerger",
                $"MergeIfNeeded(L{level}): {count} segment(s), fanout={SegmentStore.Fanout}");
            if (count < SegmentStore.Fanout)
            {
                FtsLog.Write("SegmentMerger",
                    $"MergeIfNeeded(L{level}): below fanout — no merge needed");
                return;
            }
            FtsLog.Write("SegmentMerger",
                $"MergeIfNeeded(L{level}): threshold reached — merging L{level}→L{level+1}");
            _store.Live.EnsureLevel(level + 1);
            MergeLevel(level);
            FtsLog.Write("SegmentMerger",
                $"MergeIfNeeded(L{level}): cascade check → MergeIfNeeded(L{level+1})");
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
            if (segIds.Count < 2)
            {
                FtsLog.Write("SegmentMerger",
                    $"MergeLevel(L{level}) skipped — only {segIds.Count} segment(s) at this level");
                return;
            }

            int    newSegId  = targetSegId ?? _store.Live.NextSegId();
            int    nextLevel = level + 1;
            string outDat    = _store.Live.SegDatPath(nextLevel, newSegId);
            string outDb     = _store.Live.SegDbPath(nextLevel, newSegId);

            string tmpDat = outDat + ".tmp";
            string tmpDb  = outDb  + ".tmp";

            Console.WriteLine($"[Merger] L{level}→L{nextLevel} seg {newSegId}: {segIds.Count} segs");
            FtsLog.Write("SegmentMerger",
                $"START L{level}→L{nextLevel} target=seg_{nextLevel}_{newSegId} " +
                $"sources=[{string.Join(",", segIds)}] totalSegsBefore={_store.Live.TotalLiveSegs()}");

            // Log source segment sizes for diagnostics
            foreach (int sid in segIds)
            {
                string srcDat = _store.Live.SegDatPath(level, sid);
                string srcDb  = _store.Live.SegDbPath(level, sid);
                long   datSz  = File.Exists(srcDat) ? new System.IO.FileInfo(srcDat).Length : -1;
                long   dbSz   = File.Exists(srcDb)  ? new System.IO.FileInfo(srcDb).Length  : -1;
                FtsLog.Write("SegmentMerger",
                    $"  source seg_{level}_{sid}: .dat={datSz:N0}B  .db={dbSz:N0}B  exists={File.Exists(srcDat)}/{File.Exists(srcDb)}");
            }

            _store.Wal.BeginMerge(level, segIds.ToArray(), newSegId);
            FtsLog.Write("SegmentMerger", "WAL BEGIN_MERGE written — crash point A: if we crash now, target does not exist yet");

            // Clean up any leftover .tmp files from a previous crash
            bool tmpDatExisted = File.Exists(tmpDat);
            bool tmpDbExisted  = File.Exists(tmpDb);
            if (tmpDatExisted || tmpDbExisted)
                FtsLog.Write("SegmentMerger",
                    $"WARNING: leftover .tmp files from previous crash — tmpDat={tmpDatExisted} tmpDb={tmpDbExisted} — deleting");
            DeleteIfExists(tmpDat);
            DeleteIfExists(tmpDb);

            SegmentReader[] readers = null;
            List<(string term, long skipOffset, int skipCount, long offset, int length, int count)> entries = null;

            try
            {
                readers = OpenReaders(level, segIds);
                FtsLog.Write("SegmentMerger",
                    $"opened {readers.Length} readers — beginning k-way merge → {System.IO.Path.GetFileName(tmpDat)}");

                var sw = System.Diagnostics.Stopwatch.StartNew();
                entries = WriteMergedDat(level, nextLevel, readers, tmpDat);
                sw.Stop();
                FtsLog.Write("SegmentMerger",
                    $"WriteMergedDat complete: {entries.Count:N0} terms in {sw.ElapsedMilliseconds}ms " +
                    $"tmpDat size={( File.Exists(tmpDat) ? new System.IO.FileInfo(tmpDat).Length.ToString("N0") + "B" : "MISSING")}");
            }
            catch (Exception ex)
            {
                FtsLog.Write("SegmentMerger",
                    $"EXCEPTION during WriteMergedDat: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                if (readers != null) CloseReaders(readers);
            }

            try
            {
                FtsLog.Write("SegmentMerger",
                    $"writing meta DB → {System.IO.Path.GetFileName(tmpDb)}");
                SegmentWriter.WriteMetaDb(tmpDb, entries);
                long dbSzTmp = File.Exists(tmpDb) ? new System.IO.FileInfo(tmpDb).Length : -1;
                FtsLog.Write("SegmentMerger",
                    $"WriteMetaDb complete: tmpDb size={dbSzTmp:N0}B");

                // WriteMetaDb checkpoints and removes SQLite WAL sidecars internally,
                // but delete any stragglers explicitly as belt-and-suspenders.
                DeleteIfExists(tmpDb + "-shm");
                DeleteIfExists(tmpDb + "-wal");
            }
            catch (Exception ex)
            {
                FtsLog.Write("SegmentMerger",
                    $"EXCEPTION during WriteMetaDb: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                throw;
            }

            // ── Crash-safe commit sequence ────────────────────────────────────────────
            // Step 1: rename .tmp files to final names
            FtsLog.Write("SegmentMerger",
                "COMMIT step 1: renaming .tmp → final  (crash point B: if we crash here, target may be partial)");
            try
            {
                File.Move(tmpDat, outDat);
                FtsLog.Write("SegmentMerger", $"  renamed tmpDat → {System.IO.Path.GetFileName(outDat)}  size={new System.IO.FileInfo(outDat).Length:N0}B");
                File.Move(tmpDb, outDb);
                FtsLog.Write("SegmentMerger", $"  renamed tmpDb  → {System.IO.Path.GetFileName(outDb)}  size={new System.IO.FileInfo(outDb).Length:N0}B");
            }
            catch (Exception ex)
            {
                FtsLog.Write("SegmentMerger",
                    $"EXCEPTION during File.Move (tmp→final): {ex.GetType().Name}: {ex.Message}");
                throw;
            }
            FtsLog.Write("SegmentMerger",
                "COMMIT step 1 complete: target files renamed to final paths");

            // Step 2: delete source segments
            // If we crash between step 1 and step 3 (before END_MERGE), recovery
            // sees BEGIN_MERGE with sources gone + target exists → Case B: correct.
            FtsLog.Write("SegmentMerger",
                $"COMMIT step 2: deleting {segIds.Count} source segment(s) — crash point C: if crash here, Case B recovery will handle it");
            foreach (int sid in segIds)
            {
                string srcDat = _store.Live.SegDatPath(level, sid);
                string srcDb  = _store.Live.SegDbPath(level, sid);
                bool datExists = File.Exists(srcDat);
                bool dbExists  = File.Exists(srcDb);
                FtsLog.Write("SegmentMerger",
                    $"  deleting seg_{level}_{sid}: dat={datExists} db={dbExists}");
                try
                {
                    DeleteIfExists(srcDat);
                    DeleteIfExists(srcDb);
                    DeleteIfExists(srcDb + "-shm");
                    DeleteIfExists(srcDb + "-wal");
                    FtsLog.Write("SegmentMerger", $"  seg_{level}_{sid} deleted OK");
                }
                catch (Exception ex)
                {
                    FtsLog.Write("SegmentMerger",
                        $"  EXCEPTION deleting seg_{level}_{sid}: {ex.GetType().Name}: {ex.Message}");
                    throw;
                }
            }
            FtsLog.Write("SegmentMerger", "COMMIT step 2 complete: all source segments deleted");

            // Step 3: write END_MERGE to WAL
            FtsLog.Write("SegmentMerger",
                "COMMIT step 3: writing WAL END_MERGE — crash point D: if crash here, RebuildFromDisk finds only target (correct)");
            _store.Wal.EndMerge(level, newSegId);
            FtsLog.Write("SegmentMerger", "WAL END_MERGE written");

            // Step 4: update live state in memory
            _store.Live.PromoteSegment(level, segIds, nextLevel, newSegId);

            // Log final state
            int totalSegs = _store.Live.TotalLiveSegs();
            Console.WriteLine($"[Merger] Done → L{nextLevel} seg {newSegId} ({entries.Count:N0} terms)  totalLiveSegs={totalSegs}");
            FtsLog.Write("SegmentMerger",
                $"DONE L{level}→L{nextLevel} seg_{nextLevel}_{newSegId} ({entries.Count:N0} terms)  totalLiveSegs={totalSegs}");

            // Log current directory state after merge
            LogDirState("after MergeLevel L" + level + "→L" + nextLevel);
        }

        private void LogDirState(string label)
        {
            try
            {
                string dir = _store.Dir;
                var files = Directory.GetFiles(dir, "seg_*.*");
                FtsLog.Write("SegmentMerger.DirState[" + label + "]",
                    $"{files.Length} seg file(s): " +
                    string.Join(", ", System.Array.ConvertAll(files, System.IO.Path.GetFileName)));
            }
            catch { }
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
