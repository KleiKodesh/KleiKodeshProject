using System;
using System.IO;
using System.Threading;

namespace FtsLib.Indexing
{
    /// <summary>
    /// Owns all logic for the force merge operation: incremental LSM-tree
    /// merge, WAL bookkeeping, and crash recovery/resume.
    ///
    /// Force merge collapses every level of the LSM tree into a single segment,
    /// working bottom-up one level at a time. Each level merge is its own
    /// atomic WAL-protected commit so a crash at any point is fully recoverable
    /// without losing data.
    ///
    /// WAL protocol:
    ///   BEGIN_FORCE_MERGE          — written once when the session starts
    ///   BEGIN_MERGE / END_MERGE    — one pair per level merge (existing protocol)
    ///   END_FORCE_MERGE            — written when all levels have converged
    ///   WAL cleared                — after END_FORCE_MERGE
    ///
    /// Crash recovery (called from SegmentStore.Recover):
    ///   If BEGIN_FORCE_MERGE is present without END_FORCE_MERGE on the next
    ///   startup, handle any interrupted level merge first (via the normal
    ///   BEGIN_MERGE recovery path), then call ResumeForceMerge to continue
    ///   merging any remaining levels.
    ///
    /// Concurrency:
    ///   Searches run concurrently with the force merge. Each level merge takes
    ///   the search/merge write lock only around its own commit step (inside
    ///   SegmentMerger.MergeLevel), so searches block only for those instants.
    ///   Flushes cannot race: the flush pipeline is drained before Run is called
    ///   and the IndexWriteLock excludes concurrent builds.
    /// </summary>
    internal sealed class ForceMerger
    {
        private readonly SegmentStore _store;

        internal ForceMerger(SegmentStore store)
        {
            _store = store;
        }

        // ── Public entry point ────────────────────────────────────────

        /// <summary>
        /// Runs a full force merge: writes WAL markers, incrementally merges all
        /// levels bottom-up, then clears the WAL. Searches run concurrently —
        /// each level merge locks only its own commit step.
        /// The WAL must already be open before calling this.
        /// </summary>
        internal void Run()
        {
            var livePaths = _store.Live.GetLiveSegmentPaths();
            FtsLog.Write("ForceMerger.Run",
                $"starting — totalLiveSegs={_store.Live.TotalLiveSegs()} " +
                $"segments=[{string.Join(", ", livePaths.ConvertAll(p => Path.GetFileNameWithoutExtension(p.dat)))}]");

            _store.Wal.BeginForceMerge();
            FtsLog.Write("ForceMerger.Run", "WAL BEGIN_FORCE_MERGE written");

            try
            {
                MergeLevelsIncremental();

                _store.Wal.EndForceMerge();
                FtsLog.Write("ForceMerger.Run", "WAL END_FORCE_MERGE written");

                BuildTrigramSidecars();
            }
            finally
            {
                FtsLog.Write("ForceMerger.Run",
                    $"force merge finished — totalLiveSegs={_store.Live.TotalLiveSegs()}");
                _store.Wal.Clear();
                FtsLog.Write("ForceMerger.Run", "WAL cleared — force merge complete");
            }
        }

        // ── Recovery entry point ──────────────────────────────────────

        /// <summary>
        /// Called from SegmentStore.Recover when BEGIN_FORCE_MERGE was present
        /// in the WAL (without END_FORCE_MERGE) — the force merge was interrupted.
        ///
        /// Any interrupted level merge will already have been handled by the
        /// normal BEGIN_MERGE recovery path before this is called. This method
        /// only needs to continue merging whatever levels remain.
        ///
        /// The WAL must NOT be open before calling this — this method opens and
        /// closes it itself.
        /// </summary>
        internal void ResumeForceMerge()
        {
            FtsLog.Write("ForceMerger.ResumeForceMerge",
                $"resuming force merge — totalLiveSegs={_store.Live.TotalLiveSegs()}");
            Console.WriteLine("[Recovery] Resuming interrupted force merge...");

            _store.Wal.Open();
            _store.Wal.BeginForceMerge();
            FtsLog.Write("ForceMerger.ResumeForceMerge", "WAL re-opened with BEGIN_FORCE_MERGE");

            try
            {
                MergeLevelsIncremental();

                _store.Wal.EndForceMerge();
                FtsLog.Write("ForceMerger.ResumeForceMerge", "WAL END_FORCE_MERGE written");
                Console.WriteLine("[Recovery] Force merge resume complete.");

                BuildTrigramSidecars();
            }
            catch (InvalidDataException ex)
            {
                FtsLog.Write("ForceMerger.ResumeForceMerge",
                    "corrupt segment during resume — wiping index: " + ex.Message);
                _store.Wal.Close();
                _store.WipeIndexDirectoryInternal();
                throw new CorruptIndexException(
                    "Corrupt segment during force merge recovery — index wiped for rebuild.", ex);
            }
            finally
            {
                _store.Wal.Clear();
                FtsLog.Write("ForceMerger.ResumeForceMerge", "WAL cleared");
            }
        }

        // ── Trigram sidecar build (post-merge, best-effort) ───────────

        /// <summary>
        /// Builds a compact disk trigram sidecar (seg.tgm) for every live segment — done ONLY
        /// after force-merge, over the immutable merged segments, so incremental index building
        /// is never slowed. Best-effort: a failure just leaves search to fall back to SQLite LIKE.
        /// </summary>
        private void BuildTrigramSidecars()
        {
            foreach (var (dat, db) in _store.Live.GetLiveSegmentPaths())
            {
                try
                {
                    FtsLib.Search.TrigramIndex.BuildFromDb(db, FtsLib.Search.TrigramIndex.SidecarPath(dat));
                    FtsLog.Write("ForceMerger.BuildTrigramSidecars", $"built {FtsLib.Search.TrigramIndex.SidecarPath(dat)}");
                }
                catch (Exception ex)
                {
                    FtsLog.Write("ForceMerger.BuildTrigramSidecars", $"skip {db}: {ex.Message}");
                }
            }
        }

        // ── Core incremental merge ────────────────────────────────────

        /// <summary>
        /// Walks up the LSM tree from the lowest populated level, merging each
        /// level that has more than one segment into the level above it.
        /// Restarts from the bottom after every merge so the tree is always
        /// processed in the correct order even if a merge at level N creates
        /// a new overflow at level N+1.
        ///
        /// Must be called while the WAL is open. Locking is handled per-commit
        /// inside SegmentMerger.MergeLevel.
        /// </summary>
        internal void MergeLevelsIncremental()
        {
            int pass = 0;
            bool anyProgress;
            do
            {
                anyProgress = false;

                // Shutdown requested — stop between level merges. Every completed
                // pass is fully committed, so stopping here leaves a valid
                // (just not fully collapsed) index.
                if (_store.MergeAbortToken.IsCancellationRequested)
                {
                    FtsLog.Write("ForceMerger.MergeLevelsIncremental",
                        "merge abort requested — stopping force merge between passes");
                    break;
                }

                var levels = _store.Live.GetLevelsWithMultiple();
                if (levels.Count == 0) break;

                // Always merge the lowest level first — bottom-up strategy
                levels.Sort();
                int level = levels[0];
                pass++;

                int srcCount = _store.Live.LiveSegCount(level);
                FtsLog.Write("ForceMerger.MergeLevelsIncremental",
                    $"pass {pass}: merging L{level} ({srcCount} segs) → L{level + 1}");
                Console.WriteLine($"[ForceMerge] Pass {pass}: L{level} ({srcCount} segs) → L{level + 1}");

                _store.Live.EnsureLevel(level + 1);
                _store.Merger.MergeLevel(level);
                anyProgress = true;

                FtsLog.Write("ForceMerger.MergeLevelsIncremental",
                    $"pass {pass}: complete — totalLiveSegs={_store.Live.TotalLiveSegs()}");
            }
            while (anyProgress);

            FtsLog.Write("ForceMerger.MergeLevelsIncremental",
                $"all levels converged after {pass} pass(es) — totalLiveSegs={_store.Live.TotalLiveSegs()}");
        }
    }
}
