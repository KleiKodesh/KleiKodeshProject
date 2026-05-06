using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FtsLib.Core
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
            foreach (var tmp in Directory.GetFiles(_dir, "*.tmp"))
            {
                try { File.Delete(tmp); } catch { /* best-effort */ }
            }

            Live.RebuildFromDisk();
            var recovery = Wal.Analyze();

            if (recovery.PendingMerge == null) return;

            var op = recovery.PendingMerge;
            Console.WriteLine($"[Recovery] Interrupted merge: L{op.Level} → target {op.Target}");

            DeleteIfExists(Live.SegDatPath(op.Level + 1, op.Target));
            DeleteIfExists(Live.SegDbPath(op.Level + 1, op.Target));
            Live.RemoveFromLive(op.Level + 1, op.Target);

            foreach (int sid in op.Sources)
                if (File.Exists(Live.SegDatPath(op.Level, sid)))
                    Live.AddToLive(op.Level, sid);

            Wal.Open();
            try
            {
                _merger.MergeLevel(op.Level);
            }
            finally
            {
                Wal.Close();
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
