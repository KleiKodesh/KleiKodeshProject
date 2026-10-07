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
                    // One expander, shared with the service, so the hosted and dev legs can
                    // never answer the same query differently. It never throws — a corrupt or
                    // missing artifact degrades to the unexpanded query inside Core.
                    query = RelatedFormExpander.RewriteQuery(query);
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

                // Hits are accumulated as parallel arrays because that is the shape Core's
                // embellishment pass takes: the engine match, the finished hit, and the window
                // word count that says whether the snippet is short enough to be worth
                // re-rendering over its neighbours.
                // Built per search rather than cached: the user can repoint the app at another
                // library, and the enrichment queries must follow it.
                var searcher = new KitveiHakodesh.Core.SeforimDbFullTextSearch.SeforimDbFtsSearcher(
                    new KitveiHakodesh.Core.SeforimDb.SeforimDbQueries(_state.GetDbPath()));

                var pendingResults = new List<FtsLib.SeforimDb.SearchResult>(MemorySafetyCap);
                var pendingHits    = new List<KitveiHakodesh.Core.SeforimDbFullTextSearch.FtsHit>(MemorySafetyCap);
                var pendingWindows = new List<int>(MemorySafetyCap);
                int     skipped = 0;
                var     timer   = new Stopwatch();
                timer.Start();

                // Embellish the batch's short snippets over their surrounding lines, then
                // project to the frontend shape and post. Runs once per flush; a batch with no
                // short lines pays no DB cost.
                void EmbellishAndPost()
                {
                    if (pendingHits.Count == 0) return;

                    var builtArray = new KitveiHakodesh.Core.SeforimDbFullTextSearch.FtsHit[pendingHits.Count];
                    pendingHits.CopyTo(builtArray);
                    // Embellishes the short snippets AND enriches every hit with its book id
                    // and TOC path — the same tail the service's batches go through, so this
                    // leg stopped shipping bookId 0 / empty tocText for the frontend to
                    // resolve itself.
                    searcher.FinishBatch(
                        index, pendingResults, builtArray, pendingWindows.ToArray(),
                        requireOrdered, contextWords, ct);

                    var results = new object[builtArray.Length];
                    for (int i = 0; i < builtArray.Length; i++)
                    {
                        var h = builtArray[i];
                        results[i] = new
                        {
                            lineId       = h.LineId,
                            bookId       = h.BookId,
                            bookTitle    = h.BookTitle,
                            tocText      = h.TocText,
                            score        = h.Score,
                            wordDistance = h.WordDistance,
                            snippet      = h.Snippet,
                            matchedTerms = h.MatchedTerms
                        };
                    }
                    PostSearch(new { type = "searchBatch", searchId = searchId, results = results });

                    pendingResults.Clear();
                    pendingHits.Clear();
                    pendingWindows.Clear();
                }

                foreach (var result in index.Search(query, cap: 0, expandKetiv: expandKetiv, ct: ct))
                {
                    if (ct.IsCancellationRequested)
                    {
                        PostSearch(new { type = "searchCancelled", searchId = searchId });
                        return;
                    }

                    // The per-hit work — snippet, match filter, word-distance filter, term
                    // flattening — is Core's, and is the same code the dev service runs. Only
                    // the FLUSH RHYTHM below is this leg's own.
                    if (!KitveiHakodesh.Core.SeforimDbFullTextSearch.SeforimDbFtsSearcher.TryBuildHit(
                            index, result, requireOrdered, contextWords, maxWordDistance,
                            out var builtHit, out int windowWords))
                        continue;
                    if (skipped < skipCount) { skipped++; continue; }

                    pendingResults.Add(result);
                    pendingHits.Add(builtHit);
                    pendingWindows.Add(windowWords);
                    totalResults++;

                    bool shouldFlush;
                    if (useTimerOnly)
                    {
                        shouldFlush = timer.ElapsedMilliseconds >= TimerIntervalMs
                                   || pendingHits.Count >= MemorySafetyCap;
                    }
                    else
                    {
                        int threshold = doublingThresholds[doublingIndex];
                        shouldFlush = pendingHits.Count >= threshold
                                   || pendingHits.Count >= MemorySafetyCap;
                    }

                    if (shouldFlush)
                    {
                        EmbellishAndPost();
                        timer.Restart();

                        if (!useTimerOnly)
                        {
                            doublingIndex++;
                            if (doublingIndex >= doublingThresholds.Length)
                                useTimerOnly = true;
                        }
                    }
                }

                EmbellishAndPost();

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

        // ── Snippet embellishment lives in Core ───────────────────────────────────
        //
        // The short-snippet re-render (find the batch's short hits, fetch their neighbouring
        // lines in one query, re-render only those) was implemented here AND in the dev
        // service. It is Core's now — SeforimDbFtsSearcher.EmbellishSnippets — along with the
        // per-hit snippet/filter/term work, so one query cannot be rendered differently in the
        // two apps. What stays on this side is the FLUSH RHYTHM above: this leg flushes at
        // 1, 2, 4, 8, 16 results and then on a timer, so the user sees the first result
        // immediately, where the service streams fixed batches over a pipe.

    }
}
