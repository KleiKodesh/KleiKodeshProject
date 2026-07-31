using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace FtsLib.Indexing
{
    /// <summary>
    /// Owns all logic for the force merge operation: incremental LSM-tree
    /// merge, WAL bookkeeping, and crash recovery/resume.
    ///
    /// Force merge collapses the whole LSM tree into ONE segment, working
    /// bottom-up one level at a time; a single segment stranded on a lower level
    /// (e.g. the build's tail flush) is laddered upward until it meets the rest.
    /// Each level merge is its own atomic WAL-protected commit so a crash at any
    /// point is fully recoverable without losing data.
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

                // Success ONLY: clear the WAL. Clearing it on the failure path (the
                // old `finally`) destroyed exactly the record recovery needs — a
                // fault mid merge-commit (e.g. an AV or another process holding a
                // source .db open during deletion) left the renamed target AND the
                // surviving sources on disk with no pending BEGIN_MERGE, so the next
                // startup registered BOTH as live: duplicate doc ranges, silently
                // wrong AND results, baked in permanently by the next merge.
                // With the WAL preserved, normal recovery (generalized Case B / A)
                // finishes or redoes the interrupted level merge correctly.
                _store.Wal.Clear();
                FtsLog.Write("ForceMerger.Run",
                    $"WAL cleared — force merge complete, totalLiveSegs={_store.Live.TotalLiveSegs()}");
            }
            catch
            {
                // Release the file handle but keep wal.log for startup recovery.
                _store.Wal.Close();
                FtsLog.Write("ForceMerger.Run",
                    "force merge FAILED — WAL preserved for startup recovery");
                throw;
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

                // Success only — see Run() for why the failure path must NOT clear.
                _store.Wal.Clear();
                FtsLog.Write("ForceMerger.ResumeForceMerge", "WAL cleared");
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is System.Data.Common.DbException)
            {
                // DbException: a segment's SQLite meta (term_index / doc_source)
                // is unreadable — same corruption class as a torn .dat, same
                // remedy. Both SQLite providers' exceptions derive from it.
                FtsLog.Write("ForceMerger.ResumeForceMerge",
                    "corrupt segment during resume — wiping index: " + ex.Message);
                _store.Wal.Close();
                _store.WipeIndexDirectoryInternal();
                throw new CorruptIndexException(
                    "Corrupt segment during force merge recovery — index wiped for rebuild.", ex);
            }
            catch
            {
                _store.Wal.Close();
                FtsLog.Write("ForceMerger.ResumeForceMerge",
                    "resume FAILED — WAL preserved for the next startup's recovery");
                throw;
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
            // Purge (delete-set) mode: deleted docs are only physically removed
            // when a segment is REWRITTEN. The multi-segment loop below never
            // touches a level that already collapsed to one segment — and
            // IndexWriter.Purge clears the delete set afterwards, which would
            // resurrect every deleted doc still sitting in such a segment. So in
            // purge mode, each PRE-EXISTING single segment gets one 1-source
            // rewrite; segments created during this run had the delete set applied
            // as they were written and are tracked in `cleaned`.
            var  deletes = _store.GetDeleteSet();
            bool purging = deletes != null && !deletes.IsEmpty;
            var  cleaned = purging ? new HashSet<int>() : null;

            int pass = 0;
            while (true)
            {
                // Shutdown requested — stop between level merges. Every completed
                // pass is fully committed, so stopping here leaves a valid
                // (just not fully collapsed) index.
                if (_store.MergeAbortToken.IsCancellationRequested)
                {
                    FtsLog.Write("ForceMerger.MergeLevelsIncremental",
                        "merge abort requested — stopping force merge between passes");
                    break;
                }

                var  levels = _store.Live.GetLevelsWithMultiple();
                int  level;
                bool single = false;

                if (levels.Count > 0)
                {
                    // Always merge the lowest level first — bottom-up strategy
                    levels.Sort();
                    level = levels[0];
                }
                else if (purging && TryFindUnpurgedSingleLevel(cleaned, out level))
                {
                    single = true;
                }
                else if (TryFindStrandedSingleLevel(out level))
                {
                    // Final collapse: per-level convergence can strand single
                    // segments on DIFFERENT levels — e.g. the build's tail RAM
                    // batch flushes a lone L0 after the lower levels were already
                    // consumed, leaving L0(1) + L3(1) "converged" at two segments.
                    // ForceMerge's contract is ONE segment total (search cost and
                    // the trigram sidecar are per-segment), so ladder the lowest
                    // stranded segment up one level at a time until it meets the
                    // next populated level, where the ≥2 rule above merges them.
                    single = true;
                }
                else break;

                pass++;

                int srcCount = _store.Live.LiveSegCount(level);
                FtsLog.Write("ForceMerger.MergeLevelsIncremental",
                    $"pass {pass}: merging L{level} ({srcCount} segs{(single ? ", purge rewrite" : "")}) → L{level + 1}");
                Console.WriteLine($"[ForceMerge] Pass {pass}: L{level} ({srcCount} segs) → L{level + 1}");

                _store.Live.EnsureLevel(level + 1);
                int target = _store.Merger.MergeLevelCore(level, null, allowSingle: single);
                // Any segment written by this run already has the delete set applied.
                if (target >= 0) cleaned?.Add(target);

                FtsLog.Write("ForceMerger.MergeLevelsIncremental",
                    $"pass {pass}: complete — totalLiveSegs={_store.Live.TotalLiveSegs()}");
            }

            FtsLog.Write("ForceMerger.MergeLevelsIncremental",
                $"all levels converged after {pass} pass(es) — totalLiveSegs={_store.Live.TotalLiveSegs()}");
        }

        /// <summary>
        /// Finds the lowest populated level when MORE THAN ONE level still holds a
        /// segment after per-level convergence (each necessarily a single segment,
        /// since no level has two). Returns false once a single populated level
        /// remains — the fully collapsed end state.
        /// </summary>
        private bool TryFindStrandedSingleLevel(out int level)
        {
            var populated = _store.Live.GetPopulatedLevels();
            if (populated.Count < 2)
            {
                level = -1;
                return false;
            }
            level = populated[0];
            return true;
        }

        /// <summary>
        /// Finds the lowest level holding exactly one live segment whose content
        /// has NOT yet been rewritten with the delete set applied. Returns false
        /// when every single-segment level is already clean.
        /// </summary>
        private bool TryFindUnpurgedSingleLevel(HashSet<int> cleaned, out int level)
        {
            foreach (int lvl in _store.Live.GetPopulatedLevels())
            {
                var ids = _store.Live.GetLiveSegIds(lvl);
                if (ids.Count == 1 && !cleaned.Contains(ids[0]))
                {
                    level = lvl;
                    return true;
                }
            }
            level = -1;
            return false;
        }
    }
}
