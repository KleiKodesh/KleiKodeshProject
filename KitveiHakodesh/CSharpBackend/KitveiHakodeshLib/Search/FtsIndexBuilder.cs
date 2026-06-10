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
            // Acquire the cross-process build lock on this thread.
            // Mutex is thread-affine in .NET — WaitOne and ReleaseMutex must be called
            // from the same thread. Both happen here inside the Task.Run thread so the
            // acquire/release pair is always on the same thread.
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

            // The NRT searcher is installed at the start of BuildIndex (before the first
            // line is processed), so the index is searchable from line 1 — no need to
            // wait for the first disk flush. Mark ready immediately so the frontend
            // shows the search bar as soon as the build begins.
            bool partialReadyPushed = false;
            _state.MarkReadyDirect();
            partialReadyPushed = true;

            var  segmentMarkers = new System.Collections.Generic.List<double>();
            double lastPct      = resumeOffset > 0 && totalLines > 0
                                    ? Math.Min(99.9, resumeOffset * 100.0 / totalLines)
                                    : 0.0;

            // Push an initial progress event so the frontend shows the correct
            // starting percentage immediately — before the first 5000-line tick.
            {
                double initialPct = totalLines > 0
                    ? Math.Min(99.9, resumeOffset * 100.0 / totalLines)
                    : 0.0;
                PushProgress(true, true, initialPct, (int)resumeOffset, (int)totalLines, "", segmentMarkers);
            }

            try
            {
                bool ranToCompletion = index.BuildIndex(limit: 0, totalLines: totalLines, resumeOffset: resumeOffset, onProgress: (sessionCount) =>
                {
                    if (totalLines > 0 && sessionCount % 5000 == 0)
                    {
                        long  totalIndexed = resumeOffset + sessionCount;
                        lastPct = Math.Min(99.9, totalIndexed * 100.0 / totalLines);
                        PushProgress(true, true, lastPct, (int)totalIndexed, (int)totalLines, "", segmentMarkers);
                    }
                }, onFlush: () =>
                {
                    // Fires on the indexing thread each time a segment write completes —
                    // independently of the 5000-line tick, so the marker lands at the
                    // exact percentage when the flush actually finished.
                    flushCount++;
                    segmentMarkers.Add(Math.Round(lastPct, 1));
                }, ct: cts.Token);

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
                // Cancelled or exception: mark idle.
                // WAL-recovery-only (ranToCompletion=false but no exception) is handled
                // above by leaving buildSucceeded=false so we fall through here and
                // keep the partial-ready state that was set mid-build.
                bool wasPartiallyReady = partialReadyPushed;
                _state.TryMarkIdle(cts);
                // Only push a "not indexing" event when the build stopped on its own
                // (exception, WAL-only run). When cancelled via StopAll (cts.IsCancellationRequested),
                // the caller (ResetAndReindex) already pushed ftsIndexInvalidated and will
                // start a new build — pushing isIndexing=false here would kill the overlay
                // prematurely before the new build's first progress event arrives.
                if (!cts.IsCancellationRequested && !wasPartiallyReady)
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

            string dbHash = FtsIndexState.ComputeDbHash(_state.GetDbPath());
            if (!string.IsNullOrEmpty(dbHash)) FtsIndexState.WriteDbHashStamp(dbHash);
            index.DeleteBuildProgressFile();

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

        internal void PushProgress(bool isReady, bool isIndexing, double pct,
                                   int processed, int total, string eta,
                                   System.Collections.Generic.List<double> segmentMarkers)
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
                latestSegmentPct = latestSegmentPct
            });
        }
    }
}
