using FtsLib.Indexing;
using FtsLib.SeforimDb;
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
            string query        = root.TryGetProperty("0", out var q) ? q.GetString() : null;
            int    skipCount    = root.TryGetProperty("1", out var s) ? s.GetInt32() : 0;
            int    maxWordDist  = root.TryGetProperty("2", out var d) ? d.GetInt32() : 10;
            bool   reqOrdered   = root.TryGetProperty("3", out var o) && o.GetBoolean();
            int    contextWords = root.TryGetProperty("4", out var cw) ? cw.GetInt32() : SeforimIndex.DefaultContextWords;
            bool   expandKetiv  = root.TryGetProperty("5", out var ek) && ek.GetBoolean();
            bool   expandRelated = root.TryGetProperty("6", out var er) && er.GetBoolean();

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
                () => RunSearch(searchId, query, skipCount, maxWordDist, reqOrdered, contextWords, expandKetiv, expandRelated, index, cts.Token));

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
                               bool expandKetiv, bool expandRelated,
                               SeforimIndex index, CancellationToken ct)
        {
            int totalResults = 0;
            try
            {
                // Inside the worker task: the rewrite opens a SQLite file, which
                // must never run on the UI thread (HandleSearchStart fires on the
                // WebView2 message thread). Failure degrades to unexpanded search.
                if (expandRelated)
                {
                    try { query = SearchExpansion.RewriteQuery(query); }
                    catch (Exception ex) { Console.WriteLine("[FtsSearchExecutor] expansion rewrite failed: " + ex.Message); }
                }

                // Batching strategy:
                //   Phase 1 — doubling: flush at 1, 2, 4, 8, 16 results.
                //              Gives the user instant first-result feedback and
                //              progressively larger batches as results accumulate.
                //   Phase 2 — timer: once the doubling sequence is exhausted (after
                //              the 16-result flush), switch to flushing every 250ms
                //              regardless of batch size. A memory safety cap of 200
                //              forces a flush even if the timer hasn't fired yet.
                const int TimerIntervalMs = 300;
                const int MemorySafetyCap = 200;

                // Doubling thresholds: flush when batch reaches each of these sizes.
                // After the last threshold is flushed we switch to timer-only mode.
                var doublingThresholds = new[] { 1, 2, 4, 8, 16 };
                int doublingIndex = 0;          // index into doublingThresholds
                bool useTimerOnly = false;

                var     batch   = new List<PendingHit>(MemorySafetyCap);
                int     skipped = 0;
                var     timer   = new Stopwatch();
                timer.Start();

                // Embellish the batch's short snippets over their surrounding lines, then
                // project to the frontend shape and post. Runs once per flush; a batch with
                // no short lines pays no DB cost (see EmbellishShortSnippets).
                void EmbellishAndPost(List<PendingHit> b)
                {
                    if (b.Count == 0) return;
                    EmbellishShortSnippets(index, b, requireOrdered, contextWords);
                    var results = new object[b.Count];
                    for (int i = 0; i < b.Count; i++)
                    {
                        var h = b[i];
                        results[i] = new
                        {
                            lineId       = h.Result.LineId,
                            bookId       = 0,
                            bookTitle    = h.Result.BookTitle,
                            tocText      = "",
                            score        = h.Score,
                            wordDistance = h.WordDistance,
                            snippet      = h.Snippet,
                            matchedTerms = h.MatchedTerms
                        };
                    }
                    PostSearch(new { type = "searchBatch", searchId = searchId, results = results });
                }

                foreach (var result in index.Search(query, cap: 0, expandKetiv: expandKetiv, ct: ct))
                {
                    if (ct.IsCancellationRequested)
                    {
                        PostSearch(new { type = "searchCancelled", searchId = searchId });
                        return;
                    }

                    var snippet = index.GenerateSnippet(result, requireOrdered,
                                                        contextWords: contextWords);
                    if (!snippet.IsMatch) continue;
                    if (snippet.WordDistance > maxWordDistance) continue;
                    if (skipped < skipCount) { skipped++; continue; }

                    // Flatten MatchedGroups into a deduplicated list of concrete terms.
                    var matchedTerms = new List<string>();
                    foreach (var group in result.MatchedGroups)
                        foreach (var term in group)
                            if (!matchedTerms.Contains(term))
                                matchedTerms.Add(term);

                    batch.Add(new PendingHit
                    {
                        Result          = result,
                        Score           = snippet.Score,
                        WordDistance    = snippet.WordDistance,
                        WindowWordCount = snippet.WindowWordCount,
                        Snippet         = snippet.Html,
                        MatchedTerms    = matchedTerms.ToArray()
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
                        EmbellishAndPost(batch);
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
                    EmbellishAndPost(batch);

                Console.WriteLine($"[FtsSearchExecutor] Search {searchId} complete — query=\"{query}\" results={totalResults} skipped={skipped}");
                PostSearch(new { type = "searchComplete", searchId = searchId });
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"[FtsSearchExecutor] Search {searchId} cancelled — query=\"{query}\" results so far={totalResults}");
                PostSearch(new { type = "searchCancelled", searchId = searchId });
            }
            catch (IndexMergingException)
            {
                Console.WriteLine($"[FtsSearchExecutor] Search {searchId} rejected — index is merging");
                PostSearch(new { type = "searchError", searchId = searchId,
                                 failReason = "indexMerging" });
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

        // ── Snippet embellishment ─────────────────────────────────────────────────

        // A hit held until flush so its snippet can be embellished with surrounding
        // lines before being projected to the frontend shape. Score/WordDistance are
        // the relevance keys computed on the matched line itself and never change;
        // only Snippet (the HTML) is swapped for the richer, neighbor-aware render.
        private sealed class PendingHit
        {
            public SearchResult Result;
            public int          Score;
            public int          WordDistance;
            public int          WindowWordCount;
            public string       Snippet;
            public string[]     MatchedTerms;
        }

        // How many lines of context to pull on each side when embellishing (same book).
        // Mirrors the service path; two lines fills the snippet's visual space and reaches
        // about the requested per-side word context for prose.
        private const int NeighborLineRadius = 2;

        /// <summary>Re-render the batch's short snippets — those whose window spans fewer
        /// words than the requested context (<paramref name="contextWords"/>), i.e. the
        /// matched line was too short to fill it — over their surrounding lines. One batched
        /// neighbor fetch for the whole batch; re-render runs across cores. No-op — and no
        /// DB hit — when nothing is short.</summary>
        private static void EmbellishShortSnippets(SeforimIndex index, List<PendingHit> batch,
            bool requireOrdered, int contextWords)
        {
            List<PendingHit> shortHits = null;
            List<int>        shortIds  = null;
            foreach (var h in batch)
            {
                if (h.WindowWordCount >= contextWords) continue;
                if (shortHits == null) { shortHits = new List<PendingHit>(); shortIds = new List<int>(); }
                shortHits.Add(h);
                shortIds.Add(h.Result.LineId);
            }
            if (shortHits == null) return; // nothing short — zero extra cost

            var neighbors = index.FetchNeighborContext(shortIds, NeighborLineRadius);
            if (neighbors.Count == 0) return;

            Parallel.ForEach(shortHits, h =>
            {
                if (!neighbors.TryGetValue(h.Result.LineId, out var ctx)) return;
                var re = index.GenerateSnippetWithNeighbors(
                    h.Result, ctx.Prev, ctx.Next, requireOrdered, contextWords);
                // Keep the original relevance keys; only swap in the richer snippet HTML.
                // Guard against a failed re-render (shouldn't happen — same terms).
                if (re.IsMatch && !string.IsNullOrEmpty(re.Html))
                    h.Snippet = re.Html;
            });
        }
    }
}
