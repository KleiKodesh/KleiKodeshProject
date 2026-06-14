using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FtsLib.Indexing
{
    /// <summary>
    /// Orchestrates the segment lifecycle: flush pipeline and crash recovery.
    ///
    /// Delegates to:
    ///   <see cref="SegmentLiveState"/> — thread-safe registry of live segments
    ///   <see cref="SegmentWriter"/>    — stateless .dat + .db I/O
    ///   <see cref="SegmentMerger"/>    — LSM merge logic
    ///   <see cref="SegmentWal"/>       — write-ahead log for crash safety
    ///
    /// Flush pipeline (fully non-blocking on the indexing thread):
    ///   Flush() hands a completed RamIndex to a background task and returns.
    ///   A depth-1 SemaphoreSlim provides back-pressure: the next flush cannot
    ///   start until the previous flush write AND any triggered merge both finish,
    ///   keeping at most one RamIndex queued in memory at any time.
    ///   After the write, MergeIfNeeded runs on the same task — an LSM merge fires
    ///   only when a level reaches the fanout threshold (4 segments).
    ///   WaitForMerge() drains the entire pipeline.
    ///
    /// Search / merge exclusion:
    ///   A ReaderWriterLockSlim (_searchMergeLock) ensures that no search can read
    ///   the live segment list while a merge is rewriting it.  GetLiveSegmentPaths()
    ///   acquires the read lock; MergeIfNeeded holds the write lock for the duration
    ///   of the merge.  Flush writes (segment writes that do not trigger a merge) do
    ///   not need the lock — they only add a new segment to the live set, which is
    ///   safe to observe mid-search.
    /// </summary>
    internal sealed class SegmentStore
    {
        internal const int Fanout = 4;

        internal readonly SegmentLiveState Live;
        internal readonly SegmentWal       Wal;

        private readonly SegmentMerger        _merger;
        private DeleteSet                     _deleteSet;
        private readonly string               _dir;

        // Excludes searches from observing a partially-merged live set.
        // Write lock: held for the entire duration of any merge (MergeIfNeeded).
        // Read lock: held while snapshotting live segment paths for a search.
        private readonly ReaderWriterLockSlim _searchMergeLock = new ReaderWriterLockSlim();

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

        /// <summary>
        /// Returns a consistent snapshot of all live segment paths.
        /// Throws <see cref="IndexMergingException"/> if a merge is currently in
        /// progress — the caller should surface this to the user rather than blocking.
        /// </summary>
        public List<(string dat, string db)> GetLiveSegmentPaths()
        {
            // Non-blocking: if the write lock is held (merge in progress), fail fast
            // so the search returns an actionable error instead of silently stalling.
            if (!_searchMergeLock.TryEnterReadLock(0))
                throw new IndexMergingException();
            try
            {
                return Live.GetLiveSegmentPaths();
            }
            finally
            {
                _searchMergeLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Returns a consistent snapshot of all live segment paths together with a
        /// search lease that holds the read lock for the caller's lifetime.
        ///
        /// The caller MUST dispose the returned <see cref="SearchLease"/> when the
        /// search is complete (i.e. when the <see cref="IndexReader"/> is disposed).
        /// While the lease is held, any merge that needs to delete source segment
        /// files will block on the write lock — guaranteeing that no file the reader
        /// has open is deleted from under it.
        ///
        /// Throws <see cref="IndexMergingException"/> if a merge is currently in
        /// progress — the caller should surface this to the user rather than blocking.
        /// </summary>
        public SearchLease AcquireSearchLease(out List<(string dat, string db)> livePaths)
        {
            // Non-blocking: if the write lock is held (merge in progress), fail fast.
            if (!_searchMergeLock.TryEnterReadLock(0))
                throw new IndexMergingException();

            // Read lock is now held. Snapshot the live paths while holding it.
            // The lease will release the lock when disposed.
            try
            {
                livePaths = Live.GetLiveSegmentPaths();
                return new SearchLease(_searchMergeLock);
            }
            catch
            {
                _searchMergeLock.ExitReadLock();
                throw;
            }
        }

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

            // Step 2b: Delete all .del tombstones left by a previous version of the code.
            // The current merge implementation uses plain File.Delete (no tombstones),
            // but clean up any that remain from an older build.
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
            // Deletion order: sources are deleted BEFORE END_MERGE is written.
            // So a PendingMerge entry in the WAL means BEGIN_MERGE was written
            // but END_MERGE was not yet written — the merge was interrupted before
            // it fully committed.
            //
            // Possible crash states:
            //   A) Target exists, sources exist  → crash during write or before sources
            //                                      were deleted; delete target, re-run merge
            //   B) Target exists, sources gone   → crash after sources deleted but before
            //                                      END_MERGE was written; target is complete
            //                                      — register it and clear WAL
            //   C) Target missing, sources exist → crash before File.Move; re-run merge
            //   D) Target missing, sources gone  → unrecoverable; wipe and rebuild
            bool targetExists  = File.Exists(targetDat) && File.Exists(targetDb);
            bool sourcesExist  = false;
            foreach (int sid in op.Sources)
            {
                string srcDat = Live.SegDatPath(op.Level, sid);
                if (File.Exists(srcDat))
                {
                    sourcesExist = true;
                    break;
                }
            }

            if (targetExists && !sourcesExist)
            {
                // Case B: sources already deleted, target is complete — register it.
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
            // Back-pressure: block until the previous flush+merge cycle is free.
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
                    // The slot is released only after both the write AND any triggered
                    // merge complete, so the next flush never starts while a merge is
                    // still running on this thread.
                    try
                    {
                        SegmentWriter.WriteSegment(ramIndex, terms, datPath, dbPath);
                        Live.AddToLive(0, segId);
                        LastFlushedLineId = lineId;

                        Wal.Open();
                        try
                        {
                            _searchMergeLock.EnterWriteLock();
                            try
                            {
                                _merger.MergeIfNeeded(0);
                            }
                            finally
                            {
                                _searchMergeLock.ExitWriteLock();
                            }
                        }
                        finally
                        {
                            Wal.Close();
                        }
                    }
                    finally
                    {
                        _flushSlot.Release();
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

        // ── Helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Runs a merge pass across every level that has more than one segment,
        /// holding the write lock so no search can observe the intermediate state.
        /// Used by Purge to physically remove deleted doc IDs from all segments.
        /// </summary>
        internal void MergeAllUnderWriteLock()
        {
            Wal.Open();
            try
            {
                _searchMergeLock.EnterWriteLock();
                try
                {
                    bool progress;
                    do
                    {
                        progress = false;
                        foreach (var level in Live.GetLevelsWithMultiple())
                        {
                            Live.EnsureLevel(level + 1);
                            _merger.MergeLevel(level);
                            progress = true;
                            break; // restart after each merge — level counts change
                        }
                    } while (progress);
                }
                finally
                {
                    _searchMergeLock.ExitWriteLock();
                }
            }
            finally
            {
                Wal.Clear();
            }
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
