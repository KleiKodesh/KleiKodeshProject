using FtsLib.Seforim;
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
            {
                Console.WriteLine("[SearchHandler] Already building, skipping");
                return;
            }

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
                Console.WriteLine($"[SearchHandler] Resume offset: {resumeOffset:N0} lines already indexed");
            }

            Console.WriteLine("[SearchHandler] FTS index build started");
            bool buildSucceeded    = false;

            // On resume, existing segments are already searchable — mark ready immediately
            // rather than waiting for the first progress tick to detect the first segment.
            bool partialReadyPushed = resumeOffset > 0 && FtsIndexState.ValidateFtsIndex() == null;
            if (partialReadyPushed)
            {
                _state.MarkReadyDirect();
                Console.WriteLine("[SearchHandler] Resuming with existing segments — partial index searchable");
            }

            int  lastSegmentCount = 0;
            var  segmentMarkers   = new System.Collections.Generic.List<double>();

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
                        double pct = Math.Min(99.9, totalIndexed * 100.0 / totalLines);

                        // Check if a new segment was flushed since the last tick.
                        int currentSegmentCount = 0;
                        try
                        {
                            if (Directory.Exists(FtsIndexState.FtsIndexPath))
                                currentSegmentCount = Directory.GetFiles(FtsIndexState.FtsIndexPath, "seg_*.dat").Length;
                        }
                        catch { }

                        if (currentSegmentCount > lastSegmentCount)
                        {
                            for (int i = lastSegmentCount; i < currentSegmentCount; i++)
                                segmentMarkers.Add(Math.Round(pct, 1));
                            lastSegmentCount = currentSegmentCount;
                        }

                        // As soon as the first segment is flushed the index is searchable.
                        // Push isReady=true once so the frontend can enable search immediately,
                        // while isIndexing=true signals that results are still partial.
                        if (!partialReadyPushed && FtsIndexState.ValidateFtsIndex() == null)
                        {
                            partialReadyPushed = true;
                            _state.MarkReadyDirect();
                            Console.WriteLine("[SearchHandler] First segment flushed — partial index searchable");
                        }

                        PushProgress(partialReadyPushed, true, pct, (int)totalIndexed, (int)totalLines, "", segmentMarkers);
                    }
                }, ct: cts.Token);

                // Only treat as a successful completed build if lines were actually
                // processed. A false return means only WAL recovery ran (e.g. the
                // IndexWriter found existing segments and replayed an interrupted merge
                // but the DB had no new lines to index past the resume point yet).
                // In that case we must not write the version stamp — the build is not done.
                buildSucceeded = ranToCompletion;
                if (!ranToCompletion)
                    Console.WriteLine("[SearchHandler] BuildIndex returned no new lines — WAL recovery only, not marking complete");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[SearchHandler] FTS index build cancelled");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SearchHandler] FTS index build EXCEPTION: " + ex);
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
                Console.WriteLine("[SearchHandler] FTS index invalid after build: " + validationError);
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

            Console.WriteLine("[SearchHandler] Background merge started");

            Task task = Task.Run(() =>
            {
                try
                {
                    index.Optimize();
                    Console.WriteLine("[SearchHandler] Background merge complete");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[SearchHandler] Background merge failed (non-fatal): " + ex);
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
