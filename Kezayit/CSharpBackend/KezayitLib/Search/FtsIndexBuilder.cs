using FtsLib.Seforim;
using KezayitLib.Bridge;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace KezayitLib.Search
{
    /// <summary>
    /// Manages the FTS index build and background merge lifecycle.
    /// All state mutations go through FtsIndexState under its lock.
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
            lock (_state.Lock)
            {
                if (_state.CurrentState == FtsIndexState.State.Building)
                {
                    Console.WriteLine("[SearchHandler] Already building, skipping");
                    return;
                }
                _state.CurrentState = FtsIndexState.State.Building;
                _state.IndexingCts  = new CancellationTokenSource();
            }

            CancellationTokenSource cts;
            SeforimIndex index;
            lock (_state.Lock) { cts = _state.IndexingCts; index = _state.Index; }

            Task task = Task.Run(() => RunBuild(cts, index));
            lock (_state.Lock) { _state.IndexingTask = task; }
        }

        private void RunBuild(CancellationTokenSource cts, SeforimIndex index)
        {
            long totalLines = 0;
            try { totalLines = index.CountLines(); } catch { }

            Console.WriteLine("[SearchHandler] FTS index build started");
            bool buildSucceeded = false;

            try
            {
                index.BuildIndex(limit: 0, onProgress: (count) =>
                {
                    if (cts.IsCancellationRequested) return;
                    if (totalLines > 0 && count % 5000 == 0)
                    {
                        double pct = Math.Min(99.9, count * 100.0 / totalLines);
                        PushProgress(false, true, pct, (int)count, (int)totalLines, "");
                    }
                });

                if (!cts.IsCancellationRequested)
                    buildSucceeded = true;
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
                lock (_state.Lock)
                {
                    if (_state.CurrentState == FtsIndexState.State.Building &&
                        _state.IndexingCts == cts)
                    {
                        _state.CurrentState = FtsIndexState.State.Idle;
                        _state.IndexingCts  = null;
                    }
                }
                PushProgress(false, false, 0, 0, 0, "");
                return;
            }

            string validationError = FtsIndexState.ValidateFtsIndex();
            if (validationError != null)
            {
                Console.WriteLine("[SearchHandler] FTS index invalid after build: " + validationError);
                lock (_state.Lock)
                {
                    if (_state.CurrentState == FtsIndexState.State.Building &&
                        _state.IndexingCts == cts)
                    {
                        _state.CurrentState = FtsIndexState.State.Idle;
                        _state.IndexingCts  = null;
                    }
                }
                PushProgress(false, false, 100, 0, 0, "");
                return;
            }

            // Write version stamp and delete progress file before marking ready.
            string version = FtsIndexState.GetInstalledAppVersion();
            if (!string.IsNullOrEmpty(version)) FtsIndexState.WriteVersionStamp(version);
            index.DeleteBuildProgressFile();

            lock (_state.Lock)
            {
                if (_state.CurrentState == FtsIndexState.State.Building &&
                    _state.IndexingCts == cts)
                {
                    _state.CurrentState = FtsIndexState.State.Ready;
                    _state.IndexingCts  = null;
                }
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
            lock (_state.Lock)
            {
                // Only start a merge when the index is ready and no merge is running.
                if (_state.CurrentState != FtsIndexState.State.Ready) return;
                if (_state.MergeTask != null) return;
                _state.CurrentState = FtsIndexState.State.Merging;
                index = _state.Index;
            }

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
                    // Non-fatal — the index is still fully searchable across multiple segments.
                    Console.WriteLine("[SearchHandler] Background merge failed (non-fatal): " + ex.Message);
                }
                finally
                {
                    lock (_state.Lock)
                    {
                        if (_state.CurrentState == FtsIndexState.State.Merging)
                            _state.CurrentState = FtsIndexState.State.Ready;
                        _state.MergeTask = null;
                    }
                }
            });

            lock (_state.Lock) { _state.MergeTask = task; }
        }

        // ── Progress ──────────────────────────────────────────────────────────────

        internal void PushCurrentProgress()
        {
            bool ready, indexing;
            lock (_state.Lock) { ready = _state.IsReady; indexing = _state.IsIndexing; }
            PushProgress(ready, indexing, ready ? 100.0 : 0.0, 0, 0, "");
        }

        internal void PushProgress(bool isReady, bool isIndexing, double pct,
                                   int processed, int total, string eta)
        {
            _bridge.PushEvent(new
            {
                @event          = "ftsIndexProgress",
                isReady         = isReady,
                isIndexing      = isIndexing,
                percentage      = Math.Round(pct, 1),
                processedChunks = processed,
                totalChunks     = total,
                eta             = eta
            });
        }
    }
}
