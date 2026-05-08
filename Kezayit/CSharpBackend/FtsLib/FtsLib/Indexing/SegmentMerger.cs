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

            _store.Wal.BeginMerge(level, segIds.ToArray(), newSegId);

            DeleteIfExists(tmpDat);
            DeleteIfExists(tmpDb);

            var readers = OpenReaders(level, segIds);
            var entries = WriteMergedDat(level, nextLevel, readers, tmpDat);
            CloseReaders(readers);

            SegmentWriter.WriteMetaDb(tmpDb, entries);

            File.Move(tmpDat, outDat);
            File.Move(tmpDb,  outDb);

            // Delete source segments BEFORE logging END_MERGE.
            // Crash-safety invariant: if END_MERGE is not in the WAL, recovery knows
            // the merge may be incomplete and checks whether sources still exist.
            // If sources are gone and the target exists, the merge completed — recovery
            // just writes END_MERGE and registers the target.
            // If sources still exist, the target is partial — recovery deletes it and
            // re-runs the merge from the sources.
            //
            // Rename-before-delete: File.Delete on a file with an open SQLite connection
            // throws IOException on Windows (SQLite does not open with FILE_SHARE_DELETE).
            // Renaming to a .del tombstone succeeds even with open handles, so any
            // in-flight search can finish reading the renamed file. The .del files are
            // cleaned up at the end of this method (if no handle is open) and on the
            // next recovery pass.
            foreach (int sid in segIds)
            {
                string datPath = _store.Live.SegDatPath(level, sid);
                string dbPath  = _store.Live.SegDbPath(level, sid);
                RenameToDelAndDelete(datPath);
                RenameToDelAndDelete(dbPath);
                // Also delete SQLite's WAL files (shared memory and write-ahead log).
                // These are never held open by searches so plain delete is fine.
                DeleteIfExists(dbPath + "-shm");
                DeleteIfExists(dbPath + "-wal");
            }

            _store.Wal.EndMerge(level, newSegId);

            _store.Live.PromoteSegment(level, segIds, nextLevel, newSegId);

            // Best-effort cleanup of any .del tombstones left by this merge.
            // If a search still holds a handle the delete will fail silently —
            // the next recovery pass will clean them up.
            foreach (var delFile in System.IO.Directory.GetFiles(
                System.IO.Path.GetDirectoryName(outDat), "*.del"))
            {
                try { File.Delete(delFile); } catch { /* held open — recovery will clean up */ }
            }

            Console.WriteLine($"[Merger] Done → L{nextLevel} seg {newSegId} ({entries.Count:N0} terms)");
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

        /// <summary>
        /// Renames <paramref name="path"/> to a <c>.del</c> tombstone and then
        /// immediately tries to delete the tombstone.
        ///
        /// Rename succeeds even when a search holds an open SQLite connection or
        /// FileStream on the file (Windows allows rename with FILE_SHARE_READ).
        /// The immediate delete succeeds when no handle is open; if it fails the
        /// tombstone is left for the next recovery pass to clean up.
        ///
        /// If the file does not exist the call is a no-op.
        /// </summary>
        private static void RenameToDelAndDelete(string path)
        {
            if (!File.Exists(path)) return;
            string tombstone = path + ".del";
            DeleteIfExists(tombstone); // remove any stale tombstone from a previous crash
            try
            {
                File.Move(path, tombstone);
                File.Delete(tombstone);
            }
            catch
            {
                // Delete failed — handle still open. Tombstone stays for recovery.
            }
        }


    }
}
