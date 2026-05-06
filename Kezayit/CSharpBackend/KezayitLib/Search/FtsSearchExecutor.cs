using FtsLib.SeforimDb;
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
            string query       = root.TryGetProperty("0", out var q) ? q.GetString() : null;
            int    skipCount   = root.TryGetProperty("1", out var s) ? s.GetInt32() : 0;
            int    maxWordDist = root.TryGetProperty("2", out var d) ? d.GetInt32() : 10;
            bool   reqOrdered  = root.TryGetProperty("3", out var o) && o.GetBoolean();
            int    contextWords = root.TryGetProperty("4", out var cw) ? cw.GetInt32() : SeforimIndex.DefaultContextWords;

            bool         ready = _state.IsReady;
            SeforimIndex index = _state.GetIndex();

            if (!ready || index == null || string.IsNullOrWhiteSpace(query))
            {
                _bridge.Reply(id, new { searchId = (string)null });
                return;
            }

            string searchId = "s" + Interlocked.Increment(ref _nextSearchId);
            var cts = new CancellationTokenSource();
            _searches[searchId] = cts;
            _bridge.Reply(id, new { searchId = searchId });

            Console.WriteLine($"[FtsSearchExecutor] Search {searchId} started — query=\"{query}\" skip={skipCount} maxDist={maxWordDist} ordered={reqOrdered} context={contextWords}");

            Task searchTask = Task.Run(
                () => RunSearch(searchId, query, skipCount, maxWordDist, reqOrdered, contextWords, index, cts.Token));

            // Observe the task so that any exception escaping RunSearch's own try/catch
            // is logged rather than silently swallowed by the thread pool.
            searchTask.ContinueWith(
                t => Console.WriteLine("[FtsSearchExecutor] Unhandled search exception: " + t.Exception),
                TaskContinuationOptions.OnlyOnFaulted);
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
                               int maxWordDistance, bool requireOrdered, int contextWords,
                               SeforimIndex index, CancellationToken ct)
        {
            int totalResults = 0;
            try
            {
                // Batching strategy:
                //   Phase 1 — doubling: flush at 1, 2, 4, 8, 16 results.
                //              Gives the user instant first-result feedback and
                //              progressively larger batches as results accumulate.
                //   Phase 2 — timer: once the doubling sequence is exhausted (after
                //              the 16-result flush), switch to flushing every 250ms
                //              regardless of batch size. A memory safety cap of 200
                //              forces a flush even if the timer hasn't fired yet.
                const int TimerIntervalMs = 250;
                const int MemorySafetyCap = 200;

                // Doubling thresholds: flush when batch reaches each of these sizes.
                // After the last threshold is flushed we switch to timer-only mode.
                var doublingThresholds = new[] { 1, 2, 4, 8, 16 };
                int doublingIndex = 0;          // index into doublingThresholds
                bool useTimerOnly = false;

                var     batch   = new List<object>(MemorySafetyCap);
                int     skipped = 0;
                var     timer   = new Stopwatch();
                timer.Start();

                foreach (var result in index.Search(query, cap: 0, ct))
                {
                    if (ct.IsCancellationRequested)
                    {
                        PostSearch(new { type = "searchCancelled", searchId = searchId });
                        return;
                    }

                    var snippet = index.GenerateSnippet(result, requireOrdered, contextWords: contextWords);
                    if (!snippet.IsMatch) continue;
                    if (snippet.WordDistance > maxWordDistance) continue;
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
                        bookId       = 0,
                        bookTitle    = result.BookTitle,
                        tocText      = "",
                        score        = snippet.Score,
                        snippet      = snippet.Html,
                        matchedTerms = matchedTerms.ToArray()
                    });
                    totalResults++;

                    bool shouldFlush;
                    if (useTimerOnly)
                    {
                        shouldFlush = timer.ElapsedMilliseconds >= TimerIntervalMs
                                   || batch.Count >= MemorySafetyCap;
                    }
                    else
                    {
                        int threshold = doublingThresholds[doublingIndex];
                        shouldFlush = batch.Count >= threshold
                                   || batch.Count >= MemorySafetyCap;
                    }

                    if (shouldFlush)
                    {
                        PostSearch(new { type = "searchBatch", searchId = searchId,
                                         results = batch.ToArray() });
                        batch.Clear();
                        timer.Restart();

                        if (!useTimerOnly)
                        {
                            doublingIndex++;
                            if (doublingIndex >= doublingThresholds.Length)
                                useTimerOnly = true;
                        }
                    }
                }

                if (batch.Count > 0)
                    PostSearch(new { type = "searchBatch", searchId = searchId,
                                     results = batch.ToArray() });

                Console.WriteLine($"[FtsSearchExecutor] Search {searchId} complete — query=\"{query}\" results={totalResults} skipped={skipped}");
                PostSearch(new { type = "searchComplete", searchId = searchId });
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"[FtsSearchExecutor] Search {searchId} cancelled — query=\"{query}\" results so far={totalResults}");
                PostSearch(new { type = "searchCancelled", searchId = searchId });
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FtsSearchExecutor] Search error: " + ex);
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
