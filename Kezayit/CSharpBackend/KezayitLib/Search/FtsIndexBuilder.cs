using FtsLib.SeforimDb;
using KezayitLib.Bridge;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace KezayitLib.Search
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
            long totalLines = 0;
            try { totalLines = index.CountLines(); } catch { }

            // On a resumed build, onProgress counts lines processed in this session
            // starting from 0. Compute the offset so the percentage starts correctly.
            long resumeOffset = 0;
            int  resumeLineId = index.GetResumeLineId();
            if (resumeLineId > 0)
            {
                try { resumeOffset = index.CountLinesUpTo(resumeLineId); } catch { }
            }

            Console.WriteLine("[SearchHandler] FTS index build started");
            bool buildSucceeded    = false;

            // On resume, existing segments are already searchable — mark ready immediately
            // rather than waiting for the first progress tick to detect the first segment.
            bool partialReadyPushed = resumeOffset > 0 && FtsIndexState.ValidateFtsIndex() == null;
            if (partialReadyPushed)
            {
                _state.MarkReadyDirect();
            }

            var  segmentMarkers = new System.Collections.Generic.List<double>();
            double lastPct      = resumeOffset > 0 && totalLines > 0
                                    ? Math.Min(99.9, resumeOffset * 100.0 / totalLines)
                                    : 0.0;

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
                bool ranToCompletion = index.BuildIndex(limit: 0, onProgress: (sessionCount) =>
                {
                    if (totalLines > 0 && sessionCount % 5000 == 0)
                    {
                        long  totalIndexed = resumeOffset + sessionCount;
                        lastPct = Math.Min(99.9, totalIndexed * 100.0 / totalLines);

                        // As soon as the first segment is flushed the index is searchable.
                        if (!partialReadyPushed && FtsIndexState.ValidateFtsIndex() == null)
                        {
                            partialReadyPushed = true;
                            _state.MarkReadyDirect();
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
                        _state.MarkReadyDirect();
                    }
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
            if (!string.IsNullOrEmpty(version)) FtsIndexState.WriteVersionStamp(version);
            index.DeleteBuildProgressFile();

            if (!_state.TryMarkReady(cts))
            {
                // A concurrent StopAll already reset state — don't push ready.
                return;
            }

            Console.WriteLine("[SearchHandler] FTS index ready");
            PushCurrentProgress();
            StartBackgroundMergeIfNeeded();
        }

        // ── Merge ─────────────────────────────────────────────────────────────────

        internal void StartBackgroundMergeIfNeeded()
        {
            if (!FtsIndexState.MergeNeeded()) return;
            StartBackgroundMerge();
        }

        internal void StartBackgroundMerge()
        {
            SeforimIndex index;
            if (!_state.TryStartMerging(out index)) return;

            Task task = Task.Run(() =>
            {
                try
                {
                    index.Optimize();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[SearchHandler] Background merge failed (non-fatal): " + ex);
                }
                finally
                {
                    _state.MarkMergeComplete();
                }
            });

            _state.SetMergeTask(task);
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
