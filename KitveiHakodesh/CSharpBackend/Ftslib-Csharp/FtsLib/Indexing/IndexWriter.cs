using FtsLib.Search;
using System;
using System.Collections.Generic;
using System.IO;

namespace FtsLib.Indexing
{
    /// <summary>
    /// Builds a full-text index by accepting (lineId, term) pairs and flushing
    /// them to segment files. Line IDs must be added in strictly ascending order.
    ///
    /// The index is stored as segment pairs (seg_L_ID.dat + seg_L_ID.db).
    /// IndexReader can search at any point — mid-build or after Dispose.
    ///
    /// Flush pipeline: when a flush is triggered (threshold or ForceFlush), the
    /// current RamIndex is handed off to SegmentStore and a fresh one is started
    /// immediately. The actual segment write runs on a background task so the
    /// indexing loop is never blocked on I/O. A depth-1 semaphore in SegmentStore
    /// provides back-pressure: the next flush cannot start until the previous
    /// flush write AND any triggered merge both finish.
    /// </summary>
    internal sealed class IndexWriter : IndexDirectory, IDisposable
    {
        /// <summary>Flush the RamIndex when it reaches this many distinct terms.</summary>
        public int FlushThreshold { get; set; } = 500_000;

        /// <summary>
        /// Threshold used for the very first flush only. Allows the first segment to
        /// be written early so the index becomes partially searchable sooner.
        /// After the first flush this is ignored and <see cref="FlushThreshold"/> applies.
        /// 0 (default) = disabled, use <see cref="FlushThreshold"/> from the start.
        /// </summary>
        public int FirstFlushThreshold { get; set; } = 100_000;

        private RamIndex      _ramIndex;
        private SegmentStore  _store;
        private DeleteSet     _deletes;
        private readonly bool _useSkipList;
        private bool          _disposed;
        private int           _lastLineId = int.MinValue;
        private bool          _flushPending;

        /// <summary>
        /// The highest line ID that has been fully written to a segment file on disk.
        /// -1 if no flush has completed yet in this session.
        ///
        /// Updated by the background flush task after the segment is fully written.
        /// Safe to read from the indexing thread at any time — the underlying field
        /// in SegmentStore is volatile.
        /// </summary>
        public int LastFlushedLineId =>
            _store != null ? _store.LastFlushedLineId : int.MinValue;

        /// <summary>
        /// Returns a consistent snapshot of all live segment paths at the moment of
        /// the call. Safe to call from any thread — the underlying SegmentLiveState
        /// is locked during the snapshot.
        ///
        /// Use this to construct an IndexReader that never races with concurrent merges.
        /// </summary>
        public List<(string dat, string db)> GetLiveSegmentPaths()
        {
            if (_store == null) return new List<(string, string)>();
            return _store.GetLiveSegmentPaths();
        }

        public IndexWriter(string indexPath, bool useSkipList = true) : base(indexPath)
        {
            _useSkipList = useSkipList;
            _ramIndex    = new RamIndex(useSkipList: useSkipList);
            _deletes     = DeleteSet.Load(DeletesFile);

            string segDir = IndexPath;
            if (Directory.Exists(segDir) &&
                (Directory.GetFiles(segDir, "seg_*.dat").Length > 0 ||
                 File.Exists(Path.Combine(segDir, "wal.log"))))
            {
                Console.WriteLine("[IndexWriter] Segments found — running crash recovery...");
                _store = new SegmentStore(segDir);
                // CorruptIndexException propagates up — the caller (IndexingPipeline)
                // must catch it, delete the index directory, and restart from scratch.
                _store.Recover();
                Console.WriteLine("[IndexWriter] Recovery complete.");
            }
        }

        /// <summary>
        /// Creates an IndexWriter that reuses an existing SegmentStore.
        /// Use this when the SegmentStore is owned externally (e.g. by SeforimIndex)
        /// so that live segment state persists across build sessions and can be
        /// shared with concurrent readers.
        /// Recovery must have already been run on the store before passing it here.
        /// </summary>
        public IndexWriter(string indexPath, SegmentStore store, bool useSkipList = true)
            : base(indexPath)
        {
            _useSkipList = useSkipList;
            _ramIndex    = new RamIndex(useSkipList: useSkipList);
            _deletes     = DeleteSet.Load(DeletesFile);
            _store       = store;
        }

        /// <summary>
        /// Adds a (lineId, term) pair to the index.
        /// Line IDs must be strictly ascending across all Add calls.
        /// </summary>
        public void Add(int lineId, string term)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(IndexWriter));

            // If a flush was triggered on the previous line, execute it now —
            // before writing any terms for the new line, so no line is split
            // across two segments.
            if (_flushPending && lineId != _lastLineId)
            {
                FlushRam();
                _flushPending = false;
            }

            _ramIndex.Add(term, lineId);
            _lastLineId = lineId;

            // Arm the flush flag once the threshold is reached, but don't flush
            // yet — more terms for this same lineId may still arrive.
            int activeThreshold = (FirstFlushThreshold > 0 && LastFlushedLineId == int.MinValue)
                ? FirstFlushThreshold
                : FlushThreshold;
            if (_ramIndex.Count >= activeThreshold)
                _flushPending = true;
        }

        /// <summary>
        /// Immediately hands the current RAM index off for background writing to a
        /// new level-0 segment, regardless of whether the flush threshold has been
        /// reached. A fresh RAM index is started on the calling thread before this
        /// returns.
        ///
        /// Use this to guarantee a segment boundary at a known point — for example,
        /// after every N lines processed — so that the progress file stays current
        /// and a resume after a crash re-indexes as little as possible.
        ///
        /// The segment write runs on a background task. If the previous flush write
        /// has not yet finished, this call blocks briefly until it does (depth-1
        /// back-pressure), then returns. Does nothing if the RAM index is empty.
        /// </summary>
        public void ForceFlush()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(IndexWriter));

            _flushPending = false;
            FlushRam();
        }

        /// <summary>
        /// Logically deletes a document from the index.
        /// The doc ID is added to the delete set and persisted immediately.
        /// It will be filtered from all subsequent searches and removed permanently
        /// from segment files the next time a merge passes through them (or on Purge).
        /// </summary>
        public void Delete(int lineId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(IndexWriter));

            _deletes.Add(lineId);
            _deletes.Save(DeletesFile);
        }

        /// <summary>
        /// Permanently removes all deleted doc IDs from segment files by running a
        /// merge pass across all levels, then clears the delete set.
        /// </summary>
        public void Purge()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(IndexWriter));

            FlushRam();

            if (_store == null)
                _store = new SegmentStore(IndexPath);

            // Drain any in-flight flush before starting the purge merge so the
            // delete set is applied to all segments.
            _store.WaitForMerge();

            Console.WriteLine($"[IndexWriter] Purging {_deletes.Count:N0} deleted doc(s)...");
            _store.SetDeleteSet(_deletes);
            _store.MergeAllUnderWriteLock();
            _deletes.Clear();
            _deletes.Save(DeletesFile); // removes the file
            _store.SetDeleteSet(null);
            Console.WriteLine("[IndexWriter] Purge complete.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                FtsLog.Write("IndexWriter.Dispose", "flushing remaining RAM index and draining pipeline...");
                FlushRam();
                // Drain the entire background pipeline (flush write + any triggered
                // LSM merge) before exiting.
                _store?.WaitForMerge();
                FtsLog.Write("IndexWriter.Dispose", "pipeline drained — all segments on disk");
            }
            catch (Exception ex)
            {
                FtsLog.Write("IndexWriter.Dispose", "exception during drain: " + ex.Message);
            }
            Console.WriteLine("[IndexWriter] Done.");
        }

        // ── Private ──────────────────────────────────────────────────

        private void FlushRam()
        {
            if (_ramIndex.Count == 0) return;

            if (_store == null)
                _store = new SegmentStore(IndexPath);

            Console.WriteLine($"[IndexWriter] Scheduling flush of {_ramIndex.Count:N0} terms...");

            // Hand the completed RamIndex to SegmentStore and start a fresh one
            // immediately. The actual write happens on a background task inside Flush().
            // _lastLineId is captured now — it is the highest line ID in this batch.
            RamIndex batch = _ramIndex;
            _ramIndex = new RamIndex(useSkipList: _useSkipList);

            _store.Flush(batch, _lastLineId);

            Console.WriteLine("[IndexWriter] Flush scheduled.");
        }
    }
}
