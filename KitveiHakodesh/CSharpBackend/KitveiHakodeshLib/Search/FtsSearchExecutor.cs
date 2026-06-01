using SearchEngine.SeforimDb;
using KitveiHakodeshLib.Bridge;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodeshLib.Search
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
            string query           = root.TryGetProperty("0", out var q)  ? q.GetString()  : null;
            // Parameter "1": array of lineIds already in the frontend cache.
            // C# skips snippet generation for these IDs — the frontend already has their snippets.
            var    excludedLineIds = new HashSet<int>();
            if (root.TryGetProperty("1", out var excl) && excl.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in excl.EnumerateArray())
                    excludedLineIds.Add(el.GetInt32());
            }
            int    maxWordDist  = root.TryGetProperty("2", out var d)  ? d.GetInt32()   : 10;
            bool   reqOrdered   = root.TryGetProperty("3", out var o)  && o.GetBoolean();
            int    contextWords = root.TryGetProperty("4", out var cw) ? cw.GetInt32()  : SeforimIndex.DefaultContextWords;
            bool   expandKetiv  = root.TryGetProperty("5", out var ek) && ek.GetBoolean();

            bool         ready = _state.IsReady;
            SeforimIndex index = _state.GetIndex();

            if (string.IsNullOrWhiteSpace(query))
            {
                _bridge.Reply(id, new { searchId = (string)null, failReason = (string)null });
                return;
            }

            if (!ready || index == null)
            {
                string reason = !ready ? "indexNotReady" : "indexNotReady";
                _bridge.Reply(id, new { searchId = (string)null, failReason = reason });
                return;
            }

            string searchId = "s" + Interlocked.Increment(ref _nextSearchId);
            var cts = new CancellationTokenSource();
            _searches[searchId] = cts;
            _bridge.Reply(id, new { searchId = searchId, failReason = (string)null });

            Console.WriteLine($"[FtsSearchExecutor] Search {searchId} started — query=\"{query}\" skip={skipCount} maxDist={maxWordDist} ordered={reqOrdered} context={contextWords} ketiv={expandKetiv}");

            Task searchTask = Task.Run(
                () => RunSearch(searchId, query, skipCount, maxWordDist, reqOrdered, contextWords, expandKetiv, index, cts.Token));

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
                               bool expandKetiv,
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
                int doublingIndex = 0;
                bool useTimerOnly = false;

                var  batch   = new List<object>(MemorySafetyCap);
                int  skipped = 0;
                var  timer   = new Stopwatch();
                timer.Start();

                // Single-pass search: one Lucene query, one DB connection, batch title lookups.
                // Replaces the old Search() + GenerateSnippet() loop which was O(N) Lucene
                // queries — one per result — and O(2N) individual SQLite round-trips.
                foreach (var (rowId, bookTitle, snippet) in index.SearchWithSnippets(
                    query,
                    maxWordDistance: maxWordDistance,
                    requireOrdered:  requireOrdered,
                    contextWords:    contextWords,
                    expandKetiv:     expandKetiv,
                    ct:              ct))
                {
                    if (ct.IsCancellationRequested)
                    {
                        PostSearch(new { type = "searchCancelled", searchId = searchId });
                        return;
                    }

                    if (skipped < skipCount) { skipped++; continue; }

                    batch.Add(new
                    {
                        lineId       = rowId,
                        bookId       = 0,
                        bookTitle    = bookTitle,
                        tocText      = "",
                        score        = snippet.Score,
                        snippet      = snippet.Html,
                        matchedTerms = Array.Empty<string>()
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
                                 failReason = "searchFailed", error = ex.Message });
            }
            finally
            {
                if (_searches.TryRemove(searchId, out var cts)) cts.Dispose();
            }
        }

        private void PostSearch(object payload) => _bridge.PushEvent(payload);
    }
}
