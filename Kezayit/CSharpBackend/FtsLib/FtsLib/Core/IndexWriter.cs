using System;
using System.IO;

namespace FtsLib.Core
{
    /// <summary>
    /// Builds a full-text index by accepting (lineId, term) pairs and flushing
    /// them to segment files. Line IDs must be added in strictly ascending order.
    ///
    /// The index is stored as segment pairs (seg_L_ID.dat + seg_L_ID.db).
    /// IndexReader can search at any point — mid-build or after Dispose.
    ///
    /// Set <see cref="AutoOptimize"/> = true to force-merge all segments into one
    /// on Dispose. Recommended for a one-shot build that will be searched many times.
    /// Leave false (default) for incremental append scenarios.
    /// </summary>
    internal sealed class IndexWriter : IndexPaths, IDisposable
    {
        /// <summary>Flush the RamIndex when it reaches this many distinct terms.</summary>
        public int FlushThreshold { get; set; } = 500_000;

        /// <summary>
        /// When true, force-merges all segments into one on Dispose.
        /// Produces the fastest possible search at the cost of extra merge time at the end.
        /// Default: false.
        /// </summary>
        public bool AutoOptimize { get; set; } = false;

        private RamIndex      _ramIndex;
        private SegmentStore  _store;
        private DeleteSet     _deletes;
        private readonly bool _useSkipList;
        private bool          _disposed;
        private int           _lastLineId = -1;
        private bool          _flushPending;

        /// <summary>
        /// The highest line ID that has been fully flushed to a segment file.
        /// -1 if no flush has occurred yet in this session.
        /// Safe to read from outside — updated atomically after each flush completes.
        /// </summary>
        public int LastFlushedLineId { get; private set; } = -1;

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
                _store.Recover();
                Console.WriteLine("[IndexWriter] Recovery complete.");
            }
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
            if (_ramIndex.Count >= FlushThreshold)
                _flushPending = true;
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
        /// Permanently removes all deleted doc IDs from segment files by forcing a
        /// full merge, then clears the delete set.
        /// After Purge the index is a single segment with no deleted IDs.
        /// </summary>
        public void Purge()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(IndexWriter));

            FlushRam();

            if (_store == null)
                _store = new SegmentStore(IndexPath);

            Console.WriteLine($"[IndexWriter] Purging {_deletes.Count:N0} deleted doc(s)...");
            _store.SetDeleteSet(_deletes);
            _store.Commit();

            _deletes.Clear();
            _deletes.Save(DeletesFile); // removes the file
            _store.SetDeleteSet(null);
            Console.WriteLine("[IndexWriter] Purge complete.");
        }

        /// <summary>
        /// Force-merges all segments into one for fastest subsequent search.
        /// Optional — search works correctly across any number of segments.
        /// </summary>
        public void Optimize()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(IndexWriter));
            if (_store == null)
                _store = new SegmentStore(IndexPath);
            _store.Commit();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                FlushRam();
                if (AutoOptimize) Optimize();
            }
            catch { /* swallow so Dispose never throws */ }
            Console.WriteLine("[IndexWriter] Done.");
        }

        // ── Private ──────────────────────────────────────────────────

        private void FlushRam()
        {
            if (_ramIndex.Count == 0) return;

            if (_store == null)
                _store = new SegmentStore(IndexPath);

            Console.WriteLine($"[IndexWriter] Flushing {_ramIndex.Count:N0} terms to segment...");
            _store.Flush(_ramIndex);
            Console.WriteLine("[IndexWriter] Flush complete.");

            // Record the highest line ID now safely on disk.
            // _lastLineId is the last id passed to Add() — all its terms just flushed.
            if (_lastLineId >= 0)
                LastFlushedLineId = _lastLineId;

            _ramIndex = new RamIndex(useSkipList: _useSkipList);
        }
    }
}
