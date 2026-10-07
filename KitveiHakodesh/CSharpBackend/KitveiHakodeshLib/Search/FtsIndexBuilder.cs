using FtsLib.SeforimDb;
using KitveiHakodeshLib.Bridge;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodeshLib.Search
{
    /// <summary>
    /// Starts and runs the FTS index build and background merge.
    /// Never reads or writes FtsIndexState fields directly — all state transitions
    /// go through FtsIndexState's named methods. This class contains no locks.
    /// </summary>
    internal sealed class FtsIndexBuilder
    {
        private readonly FtsIndexState _state;
        private readonly WebBridge     _bridge;

        internal FtsIndexBuilder(FtsIndexState state, WebBridge bridge)
        {
            _state  = state;
            _bridge = bridge;
        }

        // ── Build ─────────────────────────────────────────────────────────────────

        internal void StartIndexing()
        {
            CancellationTokenSource cts;
            if (!_state.TryStartBuilding(out cts))
                return;

            SeforimIndex index = _state.GetIndex();
            Task task = Task.Run(() => RunBuild(cts, index));
            _state.SetIndexingTask(task);
        }

        private void RunBuild(CancellationTokenSource cts, SeforimIndex index)
        {
            // The cross-process lock is taken HERE, not by StartIndexing, because a Windows
            // mutex is owned by a THREAD: only the thread that waited on it may release it.
            // Acquiring on the actor thread and releasing in this task's finally (on a pool
            // thread) made every ReleaseMutex throw into a swallowed catch, while the owned
            // flag was cleared anyway — so after the very first build the flag said "not
            // owned" while the OS still recorded the actor thread as owner. From then on
            // IsAnotherProcessBuilding took its not-owned branch, re-entered the mutex
            // recursively (mutexes are re-entrant for their owner) and reported "nobody is
            // building" forever, leaking one un-released acquisition per call. Two app
            // instances would then build the same index directory at once — the exact thing
            // this lock exists to prevent. Task.Run bodies with no await stay on one thread,
            // so acquiring and releasing in this method keeps both on the same thread.
            if (!FtsIndexState.TryAcquireBuildLock())
            {
                Console.WriteLine("[FtsIndexBuilder] Could not acquire build lock — another process may be building");
                _state.TryMarkIdle(cts);
                return;
            }
            try
            {
                RunBuildCore(cts, index);
            }
            finally
            {
                FtsIndexState.ReleaseBuildLock();
            }
        }

        private void RunBuildCore(CancellationTokenSource cts, SeforimIndex index)
        {
            // Everything mechanical about the build belongs to Core's SeforimDbFtsIndexer, and
            // is the SAME code the dev service runs: the at-start and at-completion stamps, the
            // resume state, background priority, the force merge, the write-lock retry while a
            // prior instance is still exiting, and clearing the watermark afterwards.
            //
            // What stays here is what only this host knows: the cross-process build mutex (held
            // by the caller), the segment markers and percentage the WebView2 progress bar
            // draws, and the state-machine transitions in FtsIndexState.
            var indexer = new KitveiHakodesh.Core.SeforimDbFullTextSearch.SeforimDbFtsIndexer(
                FtsIndexState.FtsIndexPath, _state.GetDbPath());

            Console.WriteLine("[SearchHandler] FTS index build started");

            // On resume, existing segments are already searchable — mark ready immediately
            // rather than waiting for the first progress tick to detect the first segment.
            index.GetResumeState(out int resumeLineId, out long cachedTotalLines, out long cachedResumeOffset);
            long resumeOffset = cachedResumeOffset;
            long totalLines = cachedTotalLines;

            bool partialReadyPushed = resumeOffset > 0 && FtsIndexState.ValidateFtsIndex() == null;
            if (partialReadyPushed) _state.MarkReadyDirect(cts);

            var segmentMarkers = new System.Collections.Generic.List<double>();
            double lastPct = resumeOffset > 0 && totalLines > 0
                                ? Math.Min(99.9, resumeOffset * 100.0 / totalLines)
                                : 0.0;
            long lastIndexed = resumeOffset;

            // Push an initial progress event so the frontend shows the correct starting
            // percentage immediately — before the first progress tick.
            if (partialReadyPushed)
                PushProgress(true, true, lastPct, (int)resumeOffset, (int)totalLines, "", segmentMarkers);

            bool buildSucceeded = false;
            try
            {
                buildSucceeded = indexer.Build(
                    index,
                    // Core computes the corpus size (with a CountLines scan when no prior
                    // session cached it) and reports it before indexing starts. Needed even
                    // when no line tick fires: a corpus under the tick interval finishes
                    // without one, and the merge phase would otherwise report a total of 0.
                    onTotalKnown: total => totalLines = total,
                    onProgress: (indexed, total) =>
                    {
                        lastIndexed = indexed;
                        if (total <= 0) return;
                        totalLines = total;
                        lastPct = Math.Min(99.9, indexed * 100.0 / total);

                        // As soon as the first segment is flushed the index is searchable.
                        if (!partialReadyPushed && FtsIndexState.ValidateFtsIndex() == null)
                        {
                            partialReadyPushed = true;
                            _state.MarkReadyDirect(cts);
                        }

                        PushProgress(partialReadyPushed, true, lastPct,
                                     (int)indexed, (int)total, "", segmentMarkers);
                    },
                    onSegmentFlushed: () =>
                    {
                        // Fires on the indexing thread each time a segment write completes —
                        // independently of the line tick, so the marker lands at the exact
                        // percentage when the flush actually finished.
                        segmentMarkers.Add(Math.Round(lastPct, 1));

                        if (!partialReadyPushed && FtsIndexState.ValidateFtsIndex() == null)
                        {
                            partialReadyPushed = true;
                            _state.MarkReadyDirect(cts);
                        }
                    },
                    // The force merge is the build's last phase and a long one, with no
                    // line-level progress of its own. Without these ticks the UI held the last
                    // line percentage for minutes and the build looked hung. (0,0) is the
                    // phase-start signal, before pass 1.
                    onMergeProgress: (passesDone, passesExpected) =>
                    {
                        PushProgress(partialReadyPushed, true, lastPct,
                                     (int)lastIndexed, (int)totalLines,
                                     "", segmentMarkers, true, passesDone, passesExpected);
                    },
                    // No isStillCurrent guard, deliberately: this leg cannot have a superseded
                    // build still running. FtsIndexState.StopAll cancels the build and then
                    // Wait()s for the task to finish before anything wipes or rebuilds, so by
                    // the time a replacement starts, this one has already returned. The dev
                    // service needs the guard because its reset gives up waiting.
                    cancellationToken: cts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[SearchHandler] FTS index build EXCEPTION: " + ex);
            }

            if (!buildSucceeded)
            {
                // Cancelled, threw, or returned false because only WAL recovery ran (the
                // IndexWriter replayed an interrupted merge but the DB had no new lines past
                // the resume point). Core writes no completion stamp in any of those cases.
                bool wasPartiallyReady = partialReadyPushed;
                _state.TryMarkIdle(cts);
                if (!wasPartiallyReady)
                    PushProgress(false, false, 0, 0, 0, "", null);
                // If partial segments exist, leave the UI showing partial progress — the next
                // OnDbReady resumes from the progress file.
                return;
            }

            string validationError = FtsIndexState.ValidateFtsIndex();
            if (validationError != null)
            {
                _state.TryMarkIdle(cts);
                PushProgress(false, false, 100, 0, 0, "", null);
                return;
            }

            if (!_state.TryMarkReady(cts))
            {
                // A concurrent StopAll already reset state — don't push ready.
                return;
            }

            Console.WriteLine("[SearchHandler] FTS index ready");
            PushCurrentProgress();
        }

        // ── Progress ──────────────────────────────────────────────────────────────

        internal void PushCurrentProgress()
        {
            PushProgress(_state.IsReady, _state.IsIndexing,
                         _state.IsReady ? 100.0 : 0.0, 0, 0, "", null);
        }

        /// <param name="isMerging">True while the final force merge runs. That phase has no
        /// line-level progress, so the percentage necessarily holds still for minutes; the
        /// frontend needs the flag to show "finishing" instead of a bar that looks stuck.</param>
        /// <param name="mergePassesDone">Committed merge passes, and the current ESTIMATE of
        /// the total — the estimate moves as the merge proceeds, so it drives a phase
        /// indicator, never an ETA.</param>
        internal void PushProgress(bool isReady, bool isIndexing, double pct,
                                   int processed, int total, string eta,
                                   System.Collections.Generic.List<double> segmentMarkers,
                                   bool isMerging = false,
                                   int mergePassesDone = 0, int mergePassesExpected = 0)
        {
            int segmentCount = segmentMarkers?.Count ?? 0;
            double? latestSegmentPct = segmentCount > 0 ? segmentMarkers[segmentCount - 1] : (double?)null;

            _bridge.PushEvent(new
            {
                @event          = "ftsIndexProgress",
                isReady         = isReady,
                isIndexing      = isIndexing,
                percentage      = Math.Round(pct, 1),
                processedChunks = processed,
                totalChunks     = total,
                eta             = eta,
                segmentCount    = segmentCount,
                latestSegmentPct = latestSegmentPct,
                isMerging       = isMerging,
                mergePassesDone = mergePassesDone,
                mergePassesExpected = mergePassesExpected
            });
        }
    }
}
