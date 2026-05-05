using FtsLib.Seforim;
using KezayitLib.Bridge;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KezayitLib.Search
{
    /// <summary>
    /// Executes full-text searches against the FTS index and streams results
    /// back to the frontend in batches via the WebBridge.
    /// </summary>
    internal sealed class FtsSearchExecutor
    {
        private readonly FtsIndexState _state;
        private readonly WebBridge     _bridge;

        private readonly ConcurrentDictionary<string, CancellationTokenSource> _searches
            = new ConcurrentDictionary<string, CancellationTokenSource>();
        private int _nextSearchId = 1;

        internal FtsSearchExecutor(FtsIndexState state, WebBridge bridge)
        {
            _state  = state;
            _bridge = bridge;
        }

        // ── Action handlers ───────────────────────────────────────────────────────

        internal void HandleSearchStart(JsonElement root, string id)
        {
            string query    = root.TryGetProperty("0", out var q) ? q.GetString() : null;
            int    skipCount = root.TryGetProperty("1", out var s) ? s.GetInt32() : 0;

            SeforimIndex index;
            bool ready;
            lock (_state.Lock) { ready = _state.IsReady; index = _state.Index; }

            if (!ready || index == null || string.IsNullOrWhiteSpace(query))
            {
                _bridge.Reply(id, new { searchId = (string)null });
                return;
            }

            string searchId = "s" + Interlocked.Increment(ref _nextSearchId);
            var cts = new CancellationTokenSource();
            _searches[searchId] = cts;
            _bridge.Reply(id, new { searchId = searchId });
            // Capture index at call time — safe even if _state.Index is replaced by a
            // concurrent ResetAndReindex, because the old SeforimIndex object remains
            // valid for the duration of this search.
            Task.Run(() => RunSearch(searchId, query, skipCount, index, cts.Token));
        }

        internal void HandleSearchCancel(JsonElement root, string id)
        {
            string searchId = root.TryGetProperty("0", out var s) ? s.GetString() : null;
            if (searchId != null && _searches.TryRemove(searchId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
            _bridge.Reply(id, new { });
        }

        // ── Search execution ──────────────────────────────────────────────────────

        private void RunSearch(string searchId, string query, int skipCount,
                               SeforimIndex index, CancellationToken ct)
        {
            try
            {
                const int InitialBatchSize       = 1;
                const int SwitchToTimerThreshold = 16;
                const int FlushTimeoutMs         = 150;
                const int MemorySafetyCap        = 200;

                var  batch            = new List<object>(50);
                int  currentThreshold = InitialBatchSize;
                int  skipped          = 0;
                var  batchTimer       = new Stopwatch();
                bool useTimerOnly     = false;
                batchTimer.Start();

                foreach (var result in index.Search(query, cap: 0, ct))
                {
                    if (ct.IsCancellationRequested)
                    {
                        PostSearch(new { type = "searchCancelled", searchId = searchId });
                        return;
                    }

                    var snippet = index.GenerateSnippet(result);
                    if (!snippet.IsMatch) continue;

                    if (skipped < skipCount) { skipped++; continue; }

                    // Flatten MatchedGroups into a deduplicated list of concrete terms.
                    var matchedTerms = new List<string>();
                    foreach (var group in result.MatchedGroups)
                        foreach (var term in group)
                            if (!matchedTerms.Contains(term))
                                matchedTerms.Add(term);

                    batch.Add(new
                    {
                        lineId       = result.LineId,
                        bookId       = 0,       // frontend fetches via GET_LINE_INDEX_FROM_LINE_ID
                        bookTitle    = result.BookTitle,
                        tocText      = "",      // frontend enriches via GET_TOC_PATHS_FOR_LINES
                        score        = snippet.Score,
                        snippet      = snippet.Html,
                        matchedTerms = matchedTerms.ToArray()
                    });

                    bool shouldFlush;
                    if (useTimerOnly)
                    {
                        shouldFlush = batch.Count > 0 &&
                            (batchTimer.ElapsedMilliseconds >= FlushTimeoutMs ||
                             batch.Count >= MemorySafetyCap);
                    }
                    else
                    {
                        bool reachedThreshold = batch.Count >= currentThreshold;
                        bool timedOut         = batch.Count > 0 &&
                                                batchTimer.ElapsedMilliseconds >= FlushTimeoutMs;
                        bool memoryCapReached = batch.Count >= MemorySafetyCap;
                        shouldFlush = reachedThreshold || timedOut || memoryCapReached;

                        if (shouldFlush && reachedThreshold &&
                            currentThreshold >= SwitchToTimerThreshold)
                            useTimerOnly = true;
                    }

                    if (shouldFlush)
                    {
                        PostSearch(new { type = "searchBatch", searchId = searchId,
                                         results = batch.ToArray() });
                        batch.Clear();
                        batchTimer.Restart();

                        if (!useTimerOnly && currentThreshold < SwitchToTimerThreshold)
                            currentThreshold = Math.Min(currentThreshold * 2,
                                                        SwitchToTimerThreshold);
                    }
                }

                if (batch.Count > 0)
                    PostSearch(new { type = "searchBatch", searchId = searchId,
                                     results = batch.ToArray() });
                PostSearch(new { type = "searchComplete", searchId = searchId });
            }
            catch (OperationCanceledException)
            {
                PostSearch(new { type = "searchCancelled", searchId = searchId });
            }
            catch (Exception ex)
            {
                PostSearch(new { type = "searchError", searchId = searchId,
                                 error = ex.Message });
            }
            finally
            {
                if (_searches.TryRemove(searchId, out var cts)) cts.Dispose();
            }
        }

        private void PostSearch(object payload) => _bridge.PushEvent(payload);
    }
}
