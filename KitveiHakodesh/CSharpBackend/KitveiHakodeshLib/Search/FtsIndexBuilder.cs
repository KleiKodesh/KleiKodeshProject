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
            // Record which DB this build session indexes FROM — written at build START so
            // even an interrupted build carries provenance. ExecuteOnDbReady refuses to
            // resume when this stamp no longer matches the current DB (resuming a foreign
            // build would permanently skip every line below the old watermark).
            FtsIndexState.WriteSourceStamp(FtsIndexState.ComputeDbStamp(_state.GetDbPath()));

            // Read all resume state from the progress file — zero DB queries on resume.
            index.GetResumeState(out int resumeLineId, out long cachedTotalLines, out long cachedResumeOffset);

            // CountLines() is a full table scan (~2-5s cold). Only run it when we have
            // no cached value (i.e. the very first build session, or after a wipe).
            long totalLines = cachedTotalLines > 0 ? cachedTotalLines : 0;
            if (totalLines == 0)
            {
                try { totalLines = index.CountLines(); } catch { }
            }

            // resumeOffset is the number of lines already indexed before this session.
            // On the first session it is 0. On resume it comes from the progress file —
            // no CountLinesUpTo() query needed.
            long resumeOffset = cachedResumeOffset;

            Console.WriteLine("[SearchHandler] FTS index build started");
            bool buildSucceeded    = false;
            int  flushCount        = 0;

            // On resume, existing segments are already searchable — mark ready immediately
            // rather than waiting for the first progress tick to detect the first segment.
            bool partialReadyPushed = resumeOffset > 0 && FtsIndexState.ValidateFtsIndex() == null;
            if (partialReadyPushed)
            {
                _state.MarkReadyDirect(cts);
            }

            var  segmentMarkers = new System.Collections.Generic.List<double>();
            double lastPct      = resumeOffset > 0 && totalLines > 0
                                    ? Math.Min(99.9, resumeOffset * 100.0 / totalLines)
                                    : 0.0;
            long lastSessionCount = 0;

            // Push an initial progress event so the frontend shows the correct
            // starting percentage immediately — before the first 5000-line tick.
            if (partialReadyPushed)
            {
                double initialPct = totalLines > 0
                    ? Math.Min(99.9, resumeOffset * 100.0 / totalLines)
                    : 0.0;
                PushProgress(true, true, initialPct, (int)resumeOffset, (int)totalLines, "", segmentMarkers);
            }

            try
            {
                bool ranToCompletion = index.BuildIndex(limit: 0, totalLines: totalLines, resumeOffset: resumeOffset, forceMergeOnComplete: true, onProgress: (sessionCount) =>
                {
                    lastSessionCount = sessionCount;
                    if (totalLines > 0 && sessionCount % 5000 == 0)
                    {
                        long  totalIndexed = resumeOffset + sessionCount;
                        lastPct = Math.Min(99.9, totalIndexed * 100.0 / totalLines);

                        // As soon as the first segment is flushed the index is searchable.
                        if (!partialReadyPushed && FtsIndexState.ValidateFtsIndex() == null)
                        {
                            partialReadyPushed = true;
                            _state.MarkReadyDirect(cts);
                        }

                        PushProgress(partialReadyPushed, true, lastPct, (int)totalIndexed, (int)totalLines, "", segmentMarkers);
                    }
                }, onFlush: () =>
                {
                    // Fires on the indexing thread each time a segment write completes —
                    // independently of the 5000-line tick, so the marker lands at the
                    // exact percentage when the flush actually finished.
                    flushCount++;
                    segmentMarkers.Add(Math.Round(lastPct, 1));

                    if (!partialReadyPushed && FtsIndexState.ValidateFtsIndex() == null)
                    {
                        partialReadyPushed = true;
                        _state.MarkReadyDirect(cts);
                    }
                // Background processing mode (lowest CPU + very-low I/O priority): the FTS
                // build is the long, heavy one, and this stops it contending with the UI,
                // searches, and the catalog/file-search rebuilds an app reset starts
                // alongside it — they all run in parallel and the FTS build takes whatever
                // the machine has left over.
                }, ct: cts.Token, backgroundPriority: true,
                   // The force merge that forceMergeOnComplete runs is the build's last
                   // phase and a long one, with no line-level progress of its own. Without
                   // these ticks the UI held the last line percentage for minutes and the
                   // build looked hung. (0,0) is the phase-start signal, before pass 1.
                   onMergeProgress: (passesDone, passesExpected) =>
                   {
                       PushProgress(partialReadyPushed, true, lastPct,
                                    (int)(resumeOffset + lastSessionCount), (int)totalLines,
                                    "", segmentMarkers, true, passesDone, passesExpected);
                   });

                // Only treat as a successful completed build if lines were actually
                // processed. A false return means only WAL recovery ran (e.g. the
                // IndexWriter found existing segments and replayed an interrupted merge
                // but the DB had no new lines to index past the resume point yet).
                // In that case we must not write the version stamp — the build is not done.
                buildSucceeded = ranToCompletion;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[SearchHandler] FTS index build EXCEPTION: " + ex);
            }

            if (!buildSucceeded)
            {
                // Cancelled or exception: mark idle, push not-ready.
                // WAL-recovery-only (ranToCompletion=false but no exception) is handled
                // above by leaving buildSucceeded=false so we fall through here and
                // keep the partial-ready state that was set mid-build.
                bool wasPartiallyReady = partialReadyPushed;
                _state.TryMarkIdle(cts);
                if (!wasPartiallyReady)
                    PushProgress(false, false, 0, 0, 0, "", null);
                // If partial segments exist, leave the UI showing partial progress —
                // the next OnDbReady will resume from the progress file.
                return;
            }

            string validationError = FtsIndexState.ValidateFtsIndex();
            if (validationError != null)
            {
                _state.TryMarkIdle(cts);
                PushProgress(false, false, 100, 0, 0, "", null);
                return;
            }

            string version = FtsIndexState.GetInstalledAppVersion();
            // Write a version stamp so the next boot recognises the index as complete.
            // If no registry version is available (running from build output without the
            // installer), write a sentinel value so the index is not mistakenly wiped and
            // rebuilt from scratch on every restart.
            FtsIndexState.WriteVersionStamp(!string.IsNullOrEmpty(version) ? version : "unversioned");
            index.DeleteBuildProgressFile();

            // NO ForceMerge call here. BuildIndex was given forceMergeOnComplete:true, so
            // the index (perf audit F04) was ALREADY collapsed inside the call above. The
            // explicit call that used to sit here ran a SECOND MergeAll over an
            // already-converged tree: not a second full pass set — it found nothing to
            // merge and returned after the level scan — but still a pipeline drain, a WAL
            // open/close and a scan, all of it progress-silent.
            //
            // THIS MOVED THE VERSION STAMP FROM BEFORE THE MERGE TO AFTER IT, and that is
            // a deliberate trade, not an oversight. The old ordering was documented as
            // leaving a complete, stamped, multi-segment index if the merge was
            // interrupted — a performance-only difference. Now an interruption during the
            // merge leaves the index UNSTAMPED with its build.progress file intact, so the
            // next start treats it as unfinished and resumes instead of trusting it. That
            // is the safer direction (an unstamped index is never mistaken for finished),
            // and WAL recovery still settles the partial merge via ResumeForceMerge.
            //
            // Still NOT cancellable, and that has not changed: the merge inside BuildIndex
            // ignores the build token, so a StopAll arriving during it blocks on
            // indexingTask.Wait() for the whole merge. That is the accepted cost of the
            // untimed wait that fixed the index-corruption bug; do not "fix" it by putting
            // a timeout back.

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
