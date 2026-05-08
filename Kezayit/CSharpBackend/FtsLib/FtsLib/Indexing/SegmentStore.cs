using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FtsLib.Indexing
{
    /// <summary>
    /// Orchestrates the segment lifecycle: flush pipeline, crash recovery, and commit.
    ///
    /// Delegates to:
    ///   <see cref="SegmentLiveState"/> — thread-safe registry of live segments
    ///   <see cref="SegmentWriter"/>    — stateless .dat + .db I/O
    ///   <see cref="SegmentMerger"/>    — LSM merge logic
    ///   <see cref="SegmentWal"/>       — write-ahead log for crash safety
    ///
    /// Flush pipeline (fully non-blocking on the indexing thread):
    ///   Flush() hands a completed RamIndex to a background task and returns.
    ///   A depth-1 SemaphoreSlim provides back-pressure: if the previous write is
    ///   still in flight, the indexing thread blocks only until that write finishes,
    ///   keeping at most one RamIndex queued in memory at any time.
    ///   After the write, MergeIfNeeded runs on the same task — an LSM merge fires
    ///   only when a level reaches the fanout threshold (4 segments).
    ///   WaitForMerge() drains the entire pipeline and must be called before Commit.
    /// </summary>
    internal sealed class SegmentStore
    {
        internal const int Fanout = 4;

        internal readonly SegmentLiveState Live;
        internal readonly SegmentWal       Wal;

        private readonly SegmentMerger _merger;
        private DeleteSet              _deleteSet;
        private readonly string        _dir;

        // ── Flush pipeline ────────────────────────────────────────────
        // _flushSlot: depth-1 semaphore — back-pressure gate between indexing and I/O.
        // _pipelineTask: tail of the flush+merge chain; WaitForMerge() waits on it.
        private readonly SemaphoreSlim _flushSlot    = new SemaphoreSlim(1, 1);
        private readonly object        _pipelineLock = new object();
        private Task                   _pipelineTask = Task.CompletedTask;

        // Written by the background flush task after the segment is fully on disk.
        // Read by the indexing thread — volatile for safe cross-thread visibility.
        internal volatile int LastFlushedLineId = int.MinValue;

        // Set to true when WipeIndexDirectory() is called during recovery.
        // SeforimIndex checks this after BuildIndex to know whether to reset the store.
        internal bool IsWiped { get; private set; }

        internal SegmentStore(string dir)
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Live    = new SegmentLiveState(dir);
            Wal     = new SegmentWal(dir);
            _merger = new SegmentMerger(this);
            _dir    = dir;
        }

        // ── Delete set (used during Purge) ────────────────────────────

        internal void SetDeleteSet(DeleteSet ds) => _deleteSet = ds;
        internal DeleteSet GetDeleteSet()        => _deleteSet;

        // ── Live segment paths (used by IndexReader) ──────────────────

        public List<(string dat, string db)> GetLiveSegmentPaths() =>
            Live.GetLiveSegmentPaths();

        // ── Recovery ─────────────────────────────────────────────────

        public void Recover()
        {
            // Step 1: Scan all segment files (including .tmp) AND the WAL to find
            // the highest segment ID ever allocated, so _nextSegId is set correctly
            // even if a .tmp file or a WAL-mentioned segment is about to be deleted.
            int maxSegId = -1;
            foreach (var file in Directory.GetFiles(_dir, "seg_*.*"))
            {
                string name  = Path.GetFileNameWithoutExtension(file);
                // Handle both "seg_0_5.dat" and "seg_0_5.dat.tmp"
                if (name.EndsWith(".dat")) name = name.Substring(0, name.Length - 4);
                var parts = name.Split('_');
                if (parts.Length == 3 && int.TryParse(parts[2], out int segId))
                {
                    if (segId > maxSegId) maxSegId = segId;
                }
            }

            // Also scan the WAL for any target segment IDs mentioned in BEGIN_MERGE
            // entries — these may be higher than any file on disk if a merge was
            // interrupted before the target file was created.
            var walRecovery = Wal.Analyze();
            if (walRecovery.PendingMerge != null && walRecovery.PendingMerge.Target > maxSegId)
                maxSegId = walRecovery.PendingMerge.Target;

            // Step 2: Delete all .tmp files — these are incomplete writes.
            foreach (var tmp in Directory.GetFiles(_dir, "*.tmp"))
            {
                try { File.Delete(tmp); } catch { /* best-effort */ }
            }

            // Step 2b: Delete all .del tombstones — these are source segments that were
            // renamed by a previous merge but whose delete was deferred because a search
            // held an open handle. They are now safe to delete (no searches run during recovery).
            foreach (var del in Directory.GetFiles(_dir, "*.del"))
            {
                try { File.Delete(del); } catch { /* best-effort */ }
            }

            // Step 2c: Clean up orphaned SQLite WAL files (left behind from deleted segments).
            // These are -shm and -wal files whose corresponding .db file no longer exists.
            foreach (var walFile in Directory.GetFiles(_dir, "*.db-shm").Concat(Directory.GetFiles(_dir, "*.db-wal")))
            {
                string dbFile = walFile.Replace("-shm", "").Replace("-wal", "");
                if (!File.Exists(dbFile))
                {
                    try { File.Delete(walFile); } catch { /* best-effort */ }
                }
            }

            // Step 3: Rebuild live state from disk, passing the max segment ID
            // so _nextSegId starts at maxSegId + 1.
            Live.RebuildFromDisk(maxSegId);

            // Step 4: Validate all segment files — if any are corrupt, wipe the index.
            try
            {
                ValidateAllSegments();
            }
            catch (InvalidDataException ex)
            {
                Console.WriteLine("[Recovery] Corrupt segment detected during validation — wiping index for rebuild: " + ex.Message);
                WipeIndexDirectory();
                throw new CorruptIndexException("Corrupt segment detected during validation — index wiped for rebuild.", ex);
            }

            // Step 5: Check for interrupted merge and redo it if needed.
            if (walRecovery.PendingMerge == null) return;

            var op = walRecovery.PendingMerge;
            Console.WriteLine($"[Recovery] Interrupted merge: L{op.Level} → target {op.Target}");

            string targetDat = Live.SegDatPath(op.Level + 1, op.Target);
            string targetDb  = Live.SegDbPath(op.Level + 1, op.Target);

            // Determine how far the merge got before the crash.
            // With the new deletion order: sources are deleted BEFORE END_MERGE is logged.
            // So if sources are gone and the target exists, the merge completed — just
            // clean up the WAL and register the target as live.
            bool targetExists  = File.Exists(targetDat) && File.Exists(targetDb);
            bool sourcesExist  = false;
            foreach (int sid in op.Sources)
            {
                string srcDat = Live.SegDatPath(op.Level, sid);
                // A source exists if its .dat file is present OR if a .del tombstone
                // remains (rename succeeded but delete was deferred — treat as present).
                if (File.Exists(srcDat) || File.Exists(srcDat + ".del"))
                {
                    sourcesExist = true;
                    break;
                }
            }

            if (targetExists && !sourcesExist)
            {
                // Sources already deleted, target is complete — merge finished, WAL just
                // didn't get END_MERGE written. Register the target and clear the WAL.
                Console.WriteLine($"[Recovery] Merge was complete (sources gone, target exists) — registering target and clearing WAL");
                Live.AddToLive(op.Level + 1, op.Target);
                Wal.Open();
                Wal.EndMerge(op.Level, op.Target);
                Wal.Close();
                return;
            }

            // Sources still exist (or target is missing/partial) — delete any partial
            // target and re-run the merge from the source segments.
            DeleteIfExists(targetDat);
            DeleteIfExists(targetDb);
            // Also delete SQLite's WAL files
            DeleteIfExists(targetDb + "-shm");
            DeleteIfExists(targetDb + "-wal");
            Live.RemoveFromLive(op.Level + 1, op.Target);

            foreach (int sid in op.Sources)
                if (File.Exists(Live.SegDatPath(op.Level, sid)))
                    Live.AddToLive(op.Level, sid);
            if (!sourcesExist)
            {
                // Neither target nor sources exist — the index is unrecoverable.
                Console.WriteLine("[Recovery] Neither merge target nor source segments exist — wiping index for rebuild");
                WipeIndexDirectory();
                throw new CorruptIndexException("Merge source segments missing and target incomplete — index wiped for rebuild.", null);
            }

            Wal.Open();
            try
            {
                _merger.MergeLevel(op.Level, targetSegId: op.Target);
            }
            catch (InvalidDataException ex)
            {
                Console.WriteLine("[Recovery] Corrupt segment detected during merge — wiping index for rebuild: " + ex.Message);
                Wal.Close();
                WipeIndexDirectory();
                throw new CorruptIndexException("Corrupt segment detected during merge — index wiped for rebuild.", ex);
            }
            finally
            {
                Wal.Close();
            }
        }

        /// <summary>
        /// Validates all live segment files by attempting to read their headers.
        /// Throws InvalidDataException if any segment is corrupt.
        /// </summary>
        private void ValidateAllSegments()
        {
            var paths = Live.GetLiveSegmentPaths();
            foreach (var (dat, db) in paths)
            {
                if (!File.Exists(dat))
                    throw new InvalidDataException($"Segment .dat file missing: {dat}");
                if (!File.Exists(db))
                    throw new InvalidDataException($"Segment .db file missing: {db}");

                // Validate the .dat file by reading the first record header.
                // If the file is truncated or corrupt, this will throw.
                try
                {
                    using (var reader = new SegmentReader(dat))
                    {
                        // Just try to read the first record — if it succeeds, the file
                        // is at least minimally valid. A full scan would be too slow.
                        reader.MoveNext();
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException($"Corrupt segment file: {dat}", ex);
                }
            }
        }

        private void WipeIndexDirectory()
        {
            IsWiped = true;
            foreach (var file in Directory.GetFiles(_dir))
            {
                try { File.Delete(file); } catch { /* best-effort */ }
            }
        }

        // ── Flush ─────────────────────────────────────────────────────

        /// <summary>
        /// Schedules <paramref name="ramIndex"/> to be written to a new level-0
        /// segment on a background task and returns immediately.
        ///
        /// Ownership of <paramref name="ramIndex"/> transfers to the background task —
        /// the caller must not touch it after this call.
        ///
        /// If the previous flush write is still in flight, this call blocks on the
        /// calling thread until it finishes (depth-1 back-pressure), then returns.
        ///
        /// <paramref name="lineId"/> is the highest line ID in the batch; it is
        /// written to <see cref="LastFlushedLineId"/> once the segment is on disk.
        /// </summary>
        public void Flush(RamIndex ramIndex, int lineId)
        {
            // Back-pressure: block until the previous write slot is free.
            _flushSlot.Wait();

            int    segId   = Live.NextSegId();
            string datPath = Live.SegDatPath(0, segId);
            string dbPath  = Live.SegDbPath(0, segId);

            // Sort terms on the calling thread — cheap, and keeps the background
            // task focused purely on I/O.
            var terms = new List<string>(ramIndex.Count);
            foreach (var kvp in ramIndex) terms.Add(kvp.Key);
            terms.Sort(StringComparer.Ordinal);

            lock (_pipelineLock)
            {
                _pipelineTask = _pipelineTask.ContinueWith(_ =>
                {
                    try
                    {
                        SegmentWriter.WriteSegment(ramIndex, terms, datPath, dbPath);
                        Live.AddToLive(0, segId);
                        LastFlushedLineId = lineId;
                    }
                    finally
                    {
                        _flushSlot.Release();
                    }

                    Wal.Open();
                    try
                    {
                        _merger.MergeIfNeeded(0);
                    }
                    finally
                    {
                        Wal.Close();
                    }

                }, TaskContinuationOptions.None);
            }
        }

        // ── Pipeline drain ────────────────────────────────────────────

        /// <summary>
        /// Waits for all pending flush writes and any triggered LSM merges to finish.
        /// Must be called before <see cref="Commit"/> or before disposing the store.
        /// </summary>
        public void WaitForMerge()
        {
            Task task;
            lock (_pipelineLock) { task = _pipelineTask; }
            try { task.Wait(); }
            catch (AggregateException ae)
            {
                Console.WriteLine("[SegmentStore] Pipeline exception (non-fatal): " + ae.InnerException?.Message);
            }
        }

        // ── Commit ────────────────────────────────────────────────────

        /// <summary>
        /// Force-merges all segments into one for fastest subsequent search.
        /// Optional — search works correctly across any number of live segments.
        /// </summary>
        public void Commit()
        {
            WaitForMerge();
            Wal.Open();
            try
            {
                _merger.ForceMergeAll();
            }
            finally
            {
                Wal.Clear(); // clears and closes
            }
            Console.WriteLine("[SegmentStore] Commit complete.");
        }

        // ── Helpers ───────────────────────────────────────────────────

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
